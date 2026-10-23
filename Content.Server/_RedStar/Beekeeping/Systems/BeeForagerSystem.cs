using System.Numerics;
using Content.Server._RedStar.Beekeeping.Components;
using Content.Server._RedStar.Beekeeping.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Tools.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared._RedStar.Beekeeping.Components;
using Content.Shared._RedStar.Beekeeping.Systems;
using Content.Shared._RedStar.Beekeeping.Visuals;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping.Systems;

/// <summary>
/// Visible bees out of the hive: a forager showing one visit, the scout coming back with what it found. The bee flies to
/// the tray, circles over the plant, dives into the flower, comes back out, flies home and dives into the hive. A tray or
/// hive that was moved is looked for nearby; a bee that cannot find it is lost, circles, can be caught by hand and dies
/// after a while. A meat colony's bees hunt creatures and may go through the pipes. This system only decides what the bee does when a leg of its way ends; the legs themselves are
/// <see cref="BeeFlightComponent"/> legs that the client draws.
/// </summary>
public sealed partial class BeeForagerSystem : EntitySystem
{
    [Dependency] private WeldableSystem _weldable = default!;
    [Dependency] private MeatBeehiveSystem _meat = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private BeeFlightSystem _flight = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private BeehiveSystem _hive = default!;
    [Dependency] private BeeScoutSystem _scout = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BeeQueenSystem _queen = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float DefaultShrinkTime = 0.6f;

    /// <summary>
    /// How far from where it should be a tray or hive still counts as in place, in tiles.
    /// </summary>
    private const float InPlace = 0.6f;

    /// <summary>
    /// How near a bee must be to a creature to bite it, in tiles.
    /// </summary>
    private const float BiteReach = 1f;

    /// <summary>
    /// Radius in tiles of a lost bee's circling.
    /// </summary>
    private const float LostRadius = 0.6f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BeeForagerComponent, BeeFlightComponent>();
        while (query.MoveNext(out var uid, out var forager, out var flight))
        {
            if (now < flight.End)
            {
                if (flight.Tracked)
                    _transform.SetLocalPosition(uid, _flight.GetLocalPosition((uid, flight), now));
                continue;
            }

            // The leg is over: the bee is put where it ends, and what comes next is decided from there.
            _flight.Settle((uid, flight));
            var ev = new BeeLegEndedEvent(forager.Phase);
            RaiseLocalEvent(uid, ref ev);
            if (!ev.Handled)
                NextLeg((uid, forager), (uid, flight));
        }
    }

    /// <summary>
    /// Lets out the visible foragers whose time has come.
    /// </summary>
    public void UpdatePendingForagers(Entity<BeehiveComponent> hive)
    {
        var comp = hive.Comp;
        if (comp.PendingForagers.Count == 0)
            return;

        var now = _timing.CurTime;
        var xform = Transform(hive);
        for (var i = comp.PendingForagers.Count - 1; i >= 0; i--)
        {
            var (tray, at) = comp.PendingForagers[i];
            if (now < at)
                continue;

            comp.PendingForagers.RemoveAt(i);
            if (!TerminatingOrDeleted(tray))
                TrySpawnForager(hive, xform, tray);
        }
    }

    /// <summary>
    /// A forager flies out to <paramref name="tray"/>, if the hive has room for one more in the air. A meat colony's
    /// bee may go by a <paramref name="route"/> through the pipes.
    /// </summary>
    public bool TrySpawnForager(Entity<BeehiveComponent> hive,
        TransformComponent hiveXform,
        EntityUid tray,
        int? limit = null,
        (EntityUid Entry, EntityUid Exit)? route = null)
    {
        var comp = hive.Comp;
        comp.Foragers.RemoveAll(f => TerminatingOrDeleted(f));
        if (comp.Foragers.Count >= (limit ?? comp.MaxForagers))
            return false;

        var meatComp = comp.Diet == BeehiveDiet.Meat ? _meat.MeatOf(hive) : null;
        if (SpawnBee(hive, hiveXform, meatComp?.MeatForagerPrototype ?? comp.ForagerPrototype, tray) is not { } bee)
            return false;

        var forager = Comp<BeeForagerComponent>(bee);
        if (meatComp != null)
        {
            // A meat colony's bees take the color of the blood and, once it is furious, show angry eyes.
            _queen.SetTraitVisuals(bee, _meat.MeatLook(hive), _meat.RageLevel(hive) > 0f);
            forager.Speed *= 1f + meatComp.RageSpeedBonus * _meat.RageLevel(hive);
        }

        if (route is { } way)
        {
            forager.VentEntry = way.Entry;
            forager.VentExit = way.Exit;
            forager.VentStage = BeeVentStage.ToEntry;
        }

        comp.Foragers.Add(bee);
        return true;
    }

    /// <summary>
    /// The scout flies to the first of the trays it found and back; the hive learns all of them when it lands.
    /// </summary>
    public bool TrySpawnScout(Entity<BeehiveComponent> hive, TransformComponent hiveXform, List<EntityUid> found)
    {
        if (found.Count == 0 || SpawnBee(hive, hiveXform, hive.Comp.ScoutFlyerPrototype, found[0]) is not { } bee)
            return false;

        Comp<BeeForagerComponent>(bee).Found = new List<EntityUid>(found);
        hive.Comp.ScoutFlyer = bee;
        return true;
    }

    private EntityUid? SpawnBee(Entity<BeehiveComponent> hive, TransformComponent hiveXform, string prototype, EntityUid tray)
    {
        if (hiveXform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        var home = _map.WorldToLocal(grid, gridComp, _transform.GetWorldPosition(hiveXform));
        // Wherever the tray stands now, bolted to a tile or not.
        var target = _transform.WithEntityId(Transform(tray).Coordinates, grid).Position;

        var bee = Spawn(prototype, new EntityCoordinates(grid, home));
        if (!TryComp<BeeForagerComponent>(bee, out var forager) || !TryComp<BeeFlightComponent>(bee, out var flight))
        {
            Del(bee);
            return null;
        }

        forager.Tray = tray;
        Launch((bee, forager), hive, home, target);
        return bee;
    }

    /// <summary>
    /// The bee grows out of its hive at <paramref name="home"/> and then flies to <paramref name="target"/>, both in the
    /// coordinates of its grid.
    /// </summary>
    public void Launch(Entity<BeeForagerComponent> bee, EntityUid hive, Vector2 home, Vector2 target)
    {
        bee.Comp.Hive = hive;
        bee.Comp.Home = home;
        bee.Comp.Target = target;
        SetFacing(bee, target - home);
        Shrink(bee, BeeForagerPhase.Emerge, BeeFlightSize.Growing, home);
    }

    /// <summary>
    /// What the bee does once a leg of its way is over.
    /// </summary>
    private void NextLeg(Entity<BeeForagerComponent> bee, Entity<BeeFlightComponent> flight)
    {
        var comp = bee.Comp;
        var here = Transform(bee).LocalPosition;
        switch (comp.Phase)
        {
            case BeeForagerPhase.Emerge:
                SetSize(bee, BeeFlightSize.Full);
                // A meat forager going by the pipes flies to the vent first.
                if (comp.VentStage == BeeVentStage.ToEntry && VentSpot(comp.VentEntry, bee) is { } entrySpot)
                    Fly(bee, BeeForagerPhase.Outbound, comp.Home, entrySpot);
                else
                    Fly(bee, BeeForagerPhase.Outbound, comp.Home, comp.Target);
                break;

            case BeeForagerPhase.Outbound when comp.VentStage == BeeVentStage.ToEntry:
                // The vent was welded shut or removed while the bee flew to it: it goes back home.
                if (VentSpot(comp.VentEntry, bee) == null)
                {
                    ClearRoute(comp);
                    Fly(bee, BeeForagerPhase.Return, here, comp.Home);
                }
                else
                {
                    EnterPipe(bee, BeeVentStage.PipeOut, here);
                }
                break;

            case BeeForagerPhase.Outbound:
                ArriveAtTray(bee, flight);
                break;

            case BeeForagerPhase.Hover:
                // From where the circle ended down into the plant, shrinking on the way.
                SetSize(bee, BeeFlightSize.Shrinking);
                comp.Phase = BeeForagerPhase.DiveIn;
                _flight.StartLine(flight, here, comp.Target, Distance(here, comp.Target) / (float) ShrinkTime(bee).TotalSeconds);
                break;

            case BeeForagerPhase.DiveIn:
                SetSize(bee, BeeFlightSize.Hidden);
                Hold(bee, BeeForagerPhase.Hidden, here, comp.HiddenTime);
                break;

            case BeeForagerPhase.Hidden:
                Shrink(bee, BeeForagerPhase.DiveOut, BeeFlightSize.Growing, here);
                break;

            case BeeForagerPhase.DiveOut:
                SetSize(bee, BeeFlightSize.Full);
                comp.Searches = 0;
                StartReturn(bee, here);
                break;

            case BeeForagerPhase.Return when comp.VentStage == BeeVentStage.ToExit:
                // The vent it came by is shut now: the bee is lost, the route and its slot are freed.
                if (VentSpot(comp.VentExit, bee) == null)
                    LoseInPipe(bee);
                else
                    EnterPipe(bee, BeeVentStage.PipeBack, here);
                break;

            case BeeForagerPhase.Return:
                ArriveHome(bee, flight);
                break;

            case BeeForagerPhase.Land:
                if (comp.Found != null)
                    _scout.LandScout(bee);
                QueueDel(bee);
                break;

            case BeeForagerPhase.VentIn:
                SetSize(bee, BeeFlightSize.Hidden);
                Hold(bee, BeeForagerPhase.VentHidden, here, PipeTime(comp));
                break;

            case BeeForagerPhase.VentHidden:
                ComeOutOfPipe(bee);
                break;

            case BeeForagerPhase.VentOut:
                LeftPipe(bee);
                break;

            case BeeForagerPhase.Lost:
                LoseForager(bee);
                break;
        }
    }

    /// <summary>
    /// At the tray's spot: the tray is there, or it was moved a little and the bee flies on to it. If the bee cannot
    /// find it, it flies home empty.
    /// </summary>
    private void ArriveAtTray(Entity<BeeForagerComponent> bee, Entity<BeeFlightComponent> flight)
    {
        var comp = bee.Comp;
        var here = Transform(bee).LocalPosition;
        if (FindNearby(bee, comp.Tray) is not { } trayPos)
        {
            comp.Searches = 0;
            StartReturn(bee, here);
            return;
        }

        // A creature moves about: its bee bites as soon as it is within reach, not only at the spot it first flew to.
        BeehiveComponent? meatHive = null;
        var hunting = !TerminatingOrDeleted(comp.Hive) &&
                      TryComp(comp.Hive, out meatHive) &&
                      meatHive.Diet == BeehiveDiet.Meat;

        // One dragged out of the hive's range or sight on the way is no longer a source: the bee goes back empty.
        if (hunting && comp.Tray is { } hunted)
        {
            var hiveXform = Transform(comp.Hive);
            if (_meat.CheckCreature((comp.Hive, meatHive!), hiveXform, _transform.GetMapCoordinates(comp.Hive, hiveXform), hunted) != BeeSourceCheck.Ripe)
            {
                comp.Searches = 0;
                StartReturn(bee, here);
                return;
            }
        }

        var off = hunting
            ? (trayPos - here).Length() > BiteReach
            : (trayPos - comp.Target).Length() > InPlace;
        if (off)
        {
            comp.Searches++;
            comp.Target = trayPos;
            Fly(bee, BeeForagerPhase.Outbound, here, trayPos);
            return;
        }

        // A meat colony's bee bites a living creature without landing and flies straight back; it lands on a corpse.
        if (hunting && comp.Tray is { } creature)
        {
            var dead = _meat.BiteCreature(bee, (comp.Hive, meatHive!), creature);
            comp.Searches = 0;
            if (!dead)
            {
                StartReturn(bee, here);
                return;
            }
        }

        StartCircle(bee, flight, BeeForagerPhase.Hover,
            TimeSpan.FromSeconds(_random.NextDouble(comp.HoverTimeMin.TotalSeconds, comp.HoverTimeMax.TotalSeconds)),
            _random.NextFloat(comp.HoverRadiusMin, comp.HoverRadiusMax));
    }

    /// <summary>
    /// At the hive's spot: the hive is there, or it was moved a little and the bee flies on to it, or it is lost.
    /// </summary>
    private void ArriveHome(Entity<BeeForagerComponent> bee, Entity<BeeFlightComponent> flight)
    {
        var comp = bee.Comp;
        var here = Transform(bee).LocalPosition;
        if (FindNearby(bee, comp.Hive) is not { } hivePos)
        {
            StartCircle(bee, flight, BeeForagerPhase.Lost, comp.LostTime, LostRadius);
            return;
        }

        if ((hivePos - comp.Home).Length() > InPlace)
        {
            comp.Searches++;
            comp.Home = hivePos;
            Fly(bee, BeeForagerPhase.Return, here, hivePos);
            return;
        }

        Shrink(bee, BeeForagerPhase.Land, BeeFlightSize.Shrinking, here);
    }

    /// <summary>
    /// Where the tray or hive the bee is after stands now, in grid coordinates: if it is on the bee's grid, not
    /// inside anything but a locker, within search range and in a clear straight line. Null if the bee cannot find it, or has
    /// searched twice already on this trip.
    /// </summary>
    private Vector2? FindNearby(Entity<BeeForagerComponent> bee, EntityUid? target)
    {
        // A creature moves about, so a meat colony's bee keeps after it longer.
        var searches = TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive) && hive.Diet == BeehiveDiet.Meat ? 6 : 2;
        if (target is not { } uid || TerminatingOrDeleted(uid) || bee.Comp.Searches >= searches)
            return null;

        var beeXform = Transform(bee);
        var xform = Transform(uid);
        // Inside anything but a locker it is out of reach; a wild hive in a locker still takes its bees in.
        if (xform.GridUid != beeXform.GridUid || beeXform.GridUid is not { } grid ||
            _container.IsEntityInContainer(uid) && _scout.StorageOf(uid) == null)
        {
            return null;
        }

        var position = _transform.WithEntityId(xform.Coordinates, grid).Position;
        if ((position - beeXform.LocalPosition).Length() > bee.Comp.SearchRange + InPlace)
            return null;

        return _scout.ClearFlightPath(_transform.GetMapCoordinates(bee, beeXform), _transform.GetMapCoordinates(uid, xform), bee, uid)
            ? position
            : null;
    }

    /// <summary>
    /// Nobody caught the lost bee: it dies, and the colony has one bee less. A lost scout only forgets what it found.
    /// </summary>
    private void LoseForager(Entity<BeeForagerComponent> bee)
    {
        if (bee.Comp.Found == null &&
            TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive) &&
            !TerminatingOrDeleted(bee.Comp.Hive))
        {
            _hive.RemoveHomeBee((bee.Comp.Hive, hive));
        }

        QueueDel(bee);
    }

    /// <summary>
    /// A lost forager caught by hand becomes a bee in hand, like one taken from the hive, and can be brought back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnForagerInteractHand(Entity<BeeForagerComponent> ent, ref InteractHandEvent args)
    {
        // A lone queen's flight is caught in the shared system; a swarm cannot be caught.
        if (args.Handled || HasComp<BeeSwarmComponent>(ent) || ent.Comp.Phase != BeeForagerPhase.Lost || ent.Comp.Found != null)
            return;

        args.Handled = true;

        if (!TryComp<BeehiveComponent>(ent.Comp.Hive, out var comp) || TerminatingOrDeleted(ent.Comp.Hive))
        {
            QueueDel(ent);
            return;
        }

        var hive = new Entity<BeehiveComponent>(ent.Comp.Hive, comp);
        var item = Spawn(comp.PollinatorPrototype, Transform(args.User).Coordinates);
        if (!TryComp<BeeItemComponent>(item, out var bee))
        {
            Del(item);
            return;
        }

        // The worker it showed now really is outside the hive.
        if (comp.Bees > 0)
        {
            comp.Bees--;
            comp.OutBees++;
            bee.SourceHive = hive;
            bee.CountedOutside = true;
        }

        _queen.GiveTraits(hive, (item, bee));
        _hands.PickupOrDrop(args.User, item);
        _popup.PopupEntity(Loc.GetString("beekeeping-forager-caught"), args.User, args.User);
        QueueDel(ent);
    }

    /// <summary>
    /// A straight flight at the bee's speed; the phase says what the bee does when it gets there.
    /// </summary>
    public void Fly(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, Vector2 from, Vector2 to)
    {
        bee.Comp.Phase = phase;
        SetFacing(bee, to - from);
        if (TryComp<BeeFlightComponent>(bee, out var flight))
            _flight.StartLine((bee, flight), from, to, bee.Comp.Speed);
    }

    /// <summary>
    /// The bee grows or shrinks where it is: out of the hive or the flower, or into them.
    /// </summary>
    public void Shrink(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, BeeFlightSize size, Vector2 at)
    {
        Hold(bee, phase, at, ShrinkTime(bee));
        SetSize(bee, size);
    }

    /// <summary>
    /// The bee stays at <paramref name="at"/> for a while: hidden in a flower, in the pipes and the like.
    /// </summary>
    public void Hold(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, Vector2 at, TimeSpan length)
    {
        bee.Comp.Phase = phase;
        if (TryComp<BeeFlightComponent>(bee, out var flight))
            _flight.Hold((bee, flight), at, length);
    }

    /// <summary>
    /// Starts a circle that begins where the bee is, with a random direction, speed and starting side, so no two visits
    /// look the same.
    /// </summary>
    private void StartCircle(Entity<BeeForagerComponent> bee,
        Entity<BeeFlightComponent> flight,
        BeeForagerPhase phase,
        TimeSpan length,
        float radius)
    {
        var comp = bee.Comp;
        var start = _random.NextFloat() * MathF.Tau;
        var turns = _random.NextFloat(comp.HoverTurnsMin, comp.HoverTurnsMax);
        var center = Transform(bee).LocalPosition - new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        comp.Phase = phase;
        _flight.StartCircle(flight, center, radius, _random.Prob(0.5f) ? turns : -turns, start, length);
    }

    /// <summary>
    /// The sprite is a side view facing left: mirror it when the bee moves to the right on screen. The camera turns
    /// with the grid the player stands on, so "right" is taken along the grid, not the world: on a station whose
    /// grid is turned in the world, the world direction would mirror the bees the wrong way round.
    /// </summary>
    public void SetFacing(EntityUid bee, Vector2 localDirection)
    {
        if (localDirection.LengthSquared() < 0.0001f)
            return;

        _appearance.SetData(bee, BeeForagerVisuals.FacingRight, localDirection.X > 0f);
    }

    public void SetSize(EntityUid bee, BeeFlightSize size)
    {
        _appearance.SetData(bee, BeeForagerVisuals.Size, size);
    }

    public TimeSpan ShrinkTime(EntityUid bee)
    {
        var seconds = TryComp<BeeShrinkVisualsComponent>(bee, out var visuals)
            ? visuals.ShrinkTime
            : DefaultShrinkTime;
        return TimeSpan.FromSeconds(seconds);
    }

    private static float Distance(Vector2 a, Vector2 b)
    {
        return Math.Max((a - b).Length(), 0.01f);
    }

    /// <summary>
    /// Where a vent is, in the coordinates of the bee's grid. Null if the vent is gone, welded shut or off the grid.
    /// </summary>
    private Vector2? VentSpot(EntityUid? vent, EntityUid bee)
    {
        if (vent is not { } uid || TerminatingOrDeleted(uid) || _weldable.IsWelded(uid))
            return null;

        var grid = Transform(bee).GridUid;
        return grid != null && Transform(uid).GridUid == grid
            ? _transform.WithEntityId(Transform(uid).Coordinates, grid.Value).Position
            : null;
    }

    /// <summary>
    /// The bee reaches a vent and dives into it.
    /// </summary>
    private void EnterPipe(Entity<BeeForagerComponent> bee, BeeVentStage stage, Vector2 at)
    {
        bee.Comp.VentStage = stage;
        Shrink(bee, BeeForagerPhase.VentIn, BeeFlightSize.Shrinking, at);
    }

    /// <summary>
    /// The bee is lost in the pipes (a vent on its route was welded shut or removed): the route is cleared, the bee
    /// goes, and it no longer counts as one of the hive's foragers out.
    /// </summary>
    private void LoseInPipe(Entity<BeeForagerComponent> bee)
    {
        ClearRoute(bee.Comp);
        if (TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive))
            hive.Foragers.Remove(bee);

        QueueDel(bee);
    }

    private static void ClearRoute(BeeForagerComponent forager)
    {
        forager.VentStage = BeeVentStage.None;
        forager.VentEntry = null;
        forager.VentExit = null;
    }

    /// <summary>
    /// How long the bee is in the pipes: the straight distance between the two vents over the pipe speed, and a bit.
    /// </summary>
    private TimeSpan PipeTime(BeeForagerComponent forager)
    {
        var speed = 3f;
        if (TryComp<MeatBeehiveComponent>(forager.Hive, out var meatComp))
            speed = Math.Max(0.5f, meatComp.VentPipeSpeed);

        if (forager.VentEntry is not { } entry || forager.VentExit is not { } exit ||
            TerminatingOrDeleted(entry) || TerminatingOrDeleted(exit))
        {
            return TimeSpan.FromSeconds(1);
        }

        var distance = (_transform.GetWorldPosition(entry) - _transform.GetWorldPosition(exit)).Length();
        return TimeSpan.FromSeconds(distance / speed + 0.5f);
    }

    /// <summary>
    /// The bee leaves the pipes by the vent at the other end. If that vent is gone or welded shut, the bee is lost.
    /// </summary>
    private void ComeOutOfPipe(Entity<BeeForagerComponent> bee)
    {
        var comp = bee.Comp;
        var vent = comp.VentStage == BeeVentStage.PipeOut ? comp.VentExit : comp.VentEntry;
        if (comp.VentStage is not (BeeVentStage.PipeOut or BeeVentStage.PipeBack) ||
            vent is not { } uid || TerminatingOrDeleted(uid) || _weldable.IsWelded(uid))
        {
            // The vent at the end of the pipes is shut: the bee is lost, and nothing of the route is left behind.
            LoseInPipe(bee);
            return;
        }

        _transform.SetCoordinates(bee, Transform(uid).Coordinates);
        Shrink(bee, BeeForagerPhase.VentOut, BeeFlightSize.Growing, Transform(bee).LocalPosition);
    }

    /// <summary>
    /// Out of the vent: at the target's end the bee flies to the creature, at the hive's end it flies home.
    /// </summary>
    private void LeftPipe(Entity<BeeForagerComponent> bee)
    {
        var comp = bee.Comp;
        SetSize(bee, BeeFlightSize.Full);
        var here = Transform(bee).LocalPosition;
        if (comp.VentStage == BeeVentStage.PipeBack)
        {
            comp.VentStage = BeeVentStage.None;
            Fly(bee, BeeForagerPhase.Return, here, comp.Home);
            return;
        }

        comp.VentStage = BeeVentStage.ToTarget;
        if (comp.Tray is { } creature &&
            !TerminatingOrDeleted(creature) &&
            Transform(bee).GridUid is { } grid &&
            Transform(creature).GridUid == grid)
        {
            Fly(bee, BeeForagerPhase.Outbound, here, _transform.WithEntityId(Transform(creature).Coordinates, grid).Position);
            return;
        }

        StartReturn(bee, here);
    }

    /// <summary>
    /// The way back: straight home, or through the same pipes the bee came by, first to the vent it came out of.
    /// </summary>
    private void StartReturn(Entity<BeeForagerComponent> bee, Vector2 from)
    {
        var comp = bee.Comp;
        if (comp.VentStage is BeeVentStage.ToTarget or BeeVentStage.ToExit && VentSpot(comp.VentExit, bee) is { } spot)
        {
            comp.VentStage = BeeVentStage.ToExit;
            Fly(bee, BeeForagerPhase.Return, from, spot);
            return;
        }

        comp.VentStage = BeeVentStage.None;
        Fly(bee, BeeForagerPhase.Return, from, comp.Home);
    }
}
