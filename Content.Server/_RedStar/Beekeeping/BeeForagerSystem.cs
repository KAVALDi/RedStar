using System.Numerics;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Visible forager bees: a short picture of one visit. The bee flies to the tray, circles over the plant, dives into the
/// flower, comes back out, flies home and dives into the hive. A tray or hive that was moved is looked for nearby; a bee
/// that cannot find it is lost, circles, can be caught by hand and dies after a while. Positions are set in the
/// coordinates of the grid, every tick only while a player is near; the client smooths them and animates the dives.
/// </summary>
public sealed partial class BeeForagerSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateForagers();
    }

    /// <summary>
    /// How far from where it should be a tray or hive still counts as in place, in tiles.
    /// </summary>
    private const float InPlace = 0.6f;

    /// <summary>
    /// Radius in tiles of a lost bee's circling.
    /// </summary>
    private const float LostRadius = 0.6f;

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
        var hiveMap = _transform.GetMapCoordinates(hive, xform);
        for (var i = comp.PendingForagers.Count - 1; i >= 0; i--)
        {
            var (tray, at) = comp.PendingForagers[i];
            if (now < at)
                continue;

            comp.PendingForagers.RemoveAt(i);
            if (!TerminatingOrDeleted(tray))
                TrySpawnForager(hive, xform, hiveMap, tray);
        }
    }

    public bool TrySpawnForager(Entity<BeehiveComponent> hive,
        TransformComponent hiveXform,
        MapCoordinates hiveMap,
        EntityUid tray)
    {
        var comp = hive.Comp;
        comp.Foragers.RemoveAll(f => TerminatingOrDeleted(f));
        if (comp.Foragers.Count >= comp.MaxForagers)
            return false;

        if (hiveXform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var home = _map.WorldToLocal(grid, gridComp, hiveMap.Position);
        // Wherever the tray stands now, bolted to a tile or not.
        var target = _transform.WithEntityId(Transform(tray).Coordinates, grid).Position;

        var bee = Spawn(comp.ForagerPrototype, new EntityCoordinates(grid, home));
        if (!TryComp<BeeForagerComponent>(bee, out var forager))
        {
            Del(bee);
            return false;
        }

        forager.Hive = hive;
        forager.Tray = tray;
        forager.Home = home;
        forager.Target = target;

        comp.Foragers.Add(bee);

        // It grows out of the hive first, the same way it later dives into the flower.
        SetFacing(bee, target - home);
        SetSize(bee, BeeFlightSize.Growing);
        SetPhase((bee, forager), BeeForagerPhase.Emerge, ShrinkTime(bee));
        return true;
    }

    /// <summary>
    /// Bees farther than this from every player are not moved every tick, in tiles: nobody sees them, so their position
    /// is only set where it matters, when a phase ends.
    /// </summary>
    private const float ObservedRange = 35f;

    private readonly List<MapCoordinates> _viewers = new();

    /// <summary>
    /// Where the players are, once per tick: observers and ghosts count too, as an admin may be watching.
    /// </summary>
    private void CollectViewers()
    {
        _viewers.Clear();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var viewer, out _, out var xform))
        {
            _viewers.Add(_transform.GetMapCoordinates(viewer, xform));
        }
    }

    private bool IsObserved(TransformComponent bee)
    {
        var map = bee.MapID;
        var position = _transform.GetWorldPosition(bee);
        foreach (var viewer in _viewers)
        {
            if (viewer.MapId == map && (viewer.Position - position).LengthSquared() <= ObservedRange * ObservedRange)
                return true;
        }

        return false;
    }

    private void UpdateForagers()
    {
        var now = _timing.CurTime;
        var collected = false;
        var query = EntityQueryEnumerator<BeeForagerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var forager, out var beeXform))
        {
            var bee = new Entity<BeeForagerComponent>(uid, forager);

            if (!collected)
            {
                CollectViewers();
                collected = true;
            }

            // Out of everyone's sight a flying or circling bee is only placed where the phase ends: whatever
            // reads its position (the end of a flight, a search for the tray) gets the right one.
            var watched = IsObserved(beeXform) || now >= forager.PhaseEnd;

            switch (forager.Phase)
            {
                case BeeForagerPhase.Emerge:
                    if (now >= forager.PhaseEnd)
                    {
                        SetSize(uid, BeeFlightSize.Full);
                        StartFlight(bee, BeeForagerPhase.Outbound, forager.Home, forager.Target);
                    }
                    break;

                case BeeForagerPhase.Outbound:
                    if (watched)
                        MoveAlong(bee, now);
                    if (now < forager.PhaseEnd)
                        break;

                    ArriveAtTray(bee);
                    break;

                case BeeForagerPhase.Hover:
                    if (watched)
                        Circle(bee, now);
                    if (now >= forager.PhaseEnd)
                    {
                        // From where the circle ended down into the plant, shrinking on the way.
                        forager.From = Transform(uid).LocalPosition;
                        SetSize(uid, BeeFlightSize.Shrinking);
                        SetPhase(bee, BeeForagerPhase.DiveIn, ShrinkTime(uid));
                    }
                    break;

                case BeeForagerPhase.DiveIn:
                    if (watched)
                        MoveAlong(bee, now);
                    if (now >= forager.PhaseEnd)
                    {
                        SetSize(uid, BeeFlightSize.Hidden);
                        SetPhase(bee, BeeForagerPhase.Hidden, forager.HiddenTime);
                    }
                    break;

                case BeeForagerPhase.Hidden:
                    if (now >= forager.PhaseEnd)
                    {
                        SetSize(uid, BeeFlightSize.Growing);
                        SetPhase(bee, BeeForagerPhase.DiveOut, ShrinkTime(uid));
                    }
                    break;

                case BeeForagerPhase.DiveOut:
                    if (now >= forager.PhaseEnd)
                    {
                        SetSize(uid, BeeFlightSize.Full);
                        forager.Searches = 0;
                        StartReturn(bee, forager.Target);
                    }
                    break;

                case BeeForagerPhase.Return:
                    if (watched)
                        MoveAlong(bee, now);
                    if (now >= forager.PhaseEnd)
                        ArriveHome(bee);
                    break;

                case BeeForagerPhase.Land:
                    if (now >= forager.PhaseEnd)
                        QueueDel(uid);
                    break;

                case BeeForagerPhase.Lost:
                    if (watched)
                        Circle(bee, now);
                    if (now >= forager.PhaseEnd)
                        LoseForager(bee);
                    break;
            }
        }
    }

    /// <summary>
    /// At the tray's spot: the tray is there, or it was moved a little and the bee flies on to it. If the bee cannot
    /// find it, it flies home empty.
    /// </summary>
    private void ArriveAtTray(Entity<BeeForagerComponent> bee)
    {
        var comp = bee.Comp;
        if (FindNearby(bee, comp.Tray) is not { } trayPos)
        {
            comp.Searches = 0;
            StartReturn(bee, Transform(bee).LocalPosition);
            return;
        }

        var off = (trayPos - comp.Target).Length() > InPlace;
        if (off)
        {
            comp.Searches++;
            comp.Target = trayPos;
            StartFlight(bee, BeeForagerPhase.Outbound, Transform(bee).LocalPosition, trayPos);
            return;
        }

        StartCircle(bee, BeeForagerPhase.Hover,
            TimeSpan.FromSeconds(_random.NextDouble(comp.HoverTimeMin.TotalSeconds, comp.HoverTimeMax.TotalSeconds)),
            _random.NextFloat(comp.HoverRadiusMin, comp.HoverRadiusMax));
    }

    /// <summary>
    /// At the hive's spot: the hive is there, or it was moved a little and the bee flies on to it, or it is lost.
    /// </summary>
    private void ArriveHome(Entity<BeeForagerComponent> bee)
    {
        var comp = bee.Comp;
        if (FindNearby(bee, comp.Hive) is not { } hivePos)
        {
            StartLost(bee);
            return;
        }

        if ((hivePos - comp.Home).Length() > InPlace)
        {
            comp.Searches++;
            comp.Home = hivePos;
            StartFlight(bee, BeeForagerPhase.Return, Transform(bee).LocalPosition, hivePos);
            return;
        }

        SetSize(bee, BeeFlightSize.Shrinking);
        SetPhase(bee, BeeForagerPhase.Land, ShrinkTime(bee));
    }

    /// <summary>
    /// Where the tray or hive the bee is after stands now, in grid coordinates: if it is on the bee's grid, not
    /// inside anything but a locker, within search range and in a clear straight line. Null if the bee cannot find it, or has
    /// searched twice already on this trip.
    /// </summary>
    private Vector2? FindNearby(Entity<BeeForagerComponent> bee, EntityUid? target)
    {
        if (target is not { } uid || TerminatingOrDeleted(uid) || bee.Comp.Searches >= 2)
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
    /// The bee lost its way: it circles where it is until someone catches it or it dies.
    /// </summary>
    private void StartLost(Entity<BeeForagerComponent> bee)
    {
        StartCircle(bee, BeeForagerPhase.Lost, bee.Comp.LostTime, LostRadius);
    }

    /// <summary>
    /// Nobody caught the lost bee: it dies, and the colony has one bee less.
    /// </summary>
    private void LoseForager(Entity<BeeForagerComponent> bee)
    {
        if (TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive) && !TerminatingOrDeleted(bee.Comp.Hive))
            _hive.RemoveHomeBee((bee.Comp.Hive, hive));

        QueueDel(bee);
    }

    /// <summary>
    /// A lost bee caught by hand becomes a bee in hand, like one taken from the hive, and can be brought back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnForagerInteractHand(Entity<BeeForagerComponent> ent, ref InteractHandEvent args)
    {
        // A lone queen's flight is caught in the shared system.
        if (args.Handled || ent.Comp.Phase != BeeForagerPhase.Lost)
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
            Dirty(hive);
        }

        _queen.GiveTraits(hive, (item, bee));
        _hands.PickupOrDrop(args.User, item);
        _popup.PopupEntity(Loc.GetString("beekeeping-forager-caught"), args.User, args.User);
        QueueDel(ent);
    }

    public void StartFlight(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, Vector2 from, Vector2 to)
    {
        var speed = Math.Max(0.1f, bee.Comp.Speed);
        bee.Comp.From = from;
        if (phase == BeeForagerPhase.Return)
            bee.Comp.Home = to;
        else
            bee.Comp.Target = to;
        SetPhase(bee, phase, TimeSpan.FromSeconds((to - from).Length() / speed));
        _transform.SetLocalPosition(bee, from);
        SetFacing(bee, to - from);
    }

    public void SetPhase(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, TimeSpan length)
    {
        var now = _timing.CurTime;
        bee.Comp.Phase = phase;
        bee.Comp.PhaseStart = now;
        bee.Comp.PhaseEnd = now + length;
    }

    /// <summary>
    /// Straight from <see cref="BeeForagerComponent.From"/> to the hive when returning, to the target otherwise.
    /// </summary>
    private void MoveAlong(Entity<BeeForagerComponent> bee, TimeSpan now)
    {
        var to = bee.Comp.Phase == BeeForagerPhase.Return ? bee.Comp.Home : bee.Comp.Target;
        var total = (bee.Comp.PhaseEnd - bee.Comp.PhaseStart).TotalSeconds;
        var progress = total > 0 ? (float) Math.Clamp((now - bee.Comp.PhaseStart).TotalSeconds / total, 0, 1) : 1f;
        _transform.SetLocalPosition(bee, Vector2.Lerp(bee.Comp.From, to, progress));
    }

    /// <summary>
    /// Starts a circle that begins where the bee is, with a random direction, speed and starting side, so no
    /// two visits look the same.
    /// </summary>
    private void StartCircle(Entity<BeeForagerComponent> bee, BeeForagerPhase phase, TimeSpan length, float radius)
    {
        var comp = bee.Comp;
        var start = _random.NextFloat() * MathF.Tau;
        var turns = _random.NextFloat(comp.HoverTurnsMin, comp.HoverTurnsMax);
        comp.CircleTurns = _random.Prob(0.5f) ? turns : -turns;
        comp.CircleRadius = radius;
        comp.CircleStart = start;
        comp.CircleCenter = Transform(bee).LocalPosition - new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        SetPhase(bee, phase, length);
    }

    /// <summary>
    /// Circling around the current circle's centre, with a slight wobble of the radius.
    /// </summary>
    private void Circle(Entity<BeeForagerComponent> bee, TimeSpan now)
    {
        var comp = bee.Comp;
        var time = (float) (now - comp.PhaseStart).TotalSeconds;
        var angle = comp.CircleStart + time * comp.CircleTurns * MathF.Tau;
        var radius = comp.CircleRadius * (1f + 0.2f * MathF.Sin(angle * 2.5f));
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        _transform.SetLocalPosition(bee, comp.CircleCenter + direction * radius);
        var tangent = new Vector2(-direction.Y, direction.X) * MathF.Sign(comp.CircleTurns);
        SetFacing(bee, tangent);
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

    /// <summary>
    /// The way back home.
    /// </summary>
    private void StartReturn(Entity<BeeForagerComponent> bee, Vector2 from)
    {
        StartFlight(bee, BeeForagerPhase.Return, from, bee.Comp.Home);
    }
}
