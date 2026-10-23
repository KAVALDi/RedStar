using Content.Server.NPC.HTN;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Bees out of the hive on errands (<see cref="BeeErrandComponent"/>). They are NPCs: the HTN task flies them with the
/// engine's pathfinding and steering, and this system only says where the next stop is and what the bee does there.
/// The colony's numbers do not depend on them: a visit's nectar is counted by the hive, the forager only acts it out.
/// A bee that is killed or never finds its way home is a bee the colony loses.
/// </summary>
public sealed partial class BeeErrandSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private BeehiveSystem _hive = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private BeeQueenSystem _queen = default!;
    [Dependency] private BeeScoutSystem _scout = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WeldableSystem _weldable = default!;

    private const float DefaultShrinkTime = 0.6f;

    /// <summary>
    /// How close to the hive a bee flies before it goes in, in tiles. The hive is solid: the bee stops next to it.
    /// </summary>
    private const float HomeRange = 1f;

    /// <summary>
    /// How fast a bee goes through the pipes, in tiles per second of straight distance between the two vents.
    /// </summary>
    private const float PipeSpeed = 3f;

    /// <summary>
    /// How many times a bee flies on to a target that moved before it gives up and goes home.
    /// </summary>
    private const int MaxRetargets = 6;

    private readonly HashSet<Entity<BeeErrandComponent>> _errandsNear = new();
    private readonly HashSet<Entity<BeehiveAngryBeeComponent>> _angryNear = new();

    #region Letting bees out

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
    /// A forager flies out to <paramref name="target"/>, if the hive has room for one more in the air and the area is not
    /// full of bees. A meat colony's bee may go by a <paramref name="route"/> through the pipes.
    /// </summary>
    public EntityUid? TrySpawnForager(Entity<BeehiveComponent> hive,
        TransformComponent hiveXform,
        EntityUid target,
        int? limit = null,
        EntProtoId? prototype = null,
        (EntityUid Entry, EntityUid Exit)? route = null)
    {
        var comp = hive.Comp;
        comp.Foragers.RemoveAll(f => TerminatingOrDeleted(f));
        if (comp.Foragers.Count >= (limit ?? comp.MaxForagers) || !AreaHasRoom(hiveXform.Coordinates))
            return null;

        if (Release(hive, hiveXform, prototype ?? comp.ForagerPrototype, target, route) is not { } bee)
            return null;

        comp.Foragers.Add(bee);
        return bee;
    }

    /// <summary>
    /// The scout flies to the first of the trays it found and back; the hive learns all of them when it lands.
    /// </summary>
    public bool TrySpawnScout(Entity<BeehiveComponent> hive, TransformComponent hiveXform, List<EntityUid> found)
    {
        if (found.Count == 0 ||
            !AreaHasRoom(hiveXform.Coordinates) ||
            Release(hive, hiveXform, hive.Comp.ScoutFlyerPrototype, found[0], null) is not { } bee)
        {
            return false;
        }

        Comp<BeeErrandComponent>(bee).Found = new List<EntityUid>(found);
        hive.Comp.ScoutFlyer = bee;
        return true;
    }

    /// <summary>
    /// A bee of <paramref name="prototype"/> comes out of the hive on its errand.
    /// </summary>
    public EntityUid? Release(EntityUid hive,
        TransformComponent hiveXform,
        string prototype,
        EntityUid? target,
        (EntityUid Entry, EntityUid Exit)? route,
        EntityCoordinates? spot = null)
    {
        var bee = Spawn(prototype, hiveXform.Coordinates);
        if (!TryComp<BeeErrandComponent>(bee, out var errand))
        {
            Del(bee);
            return null;
        }

        errand.Hive = hive;
        errand.Target = target;
        errand.Spot = spot;
        errand.VentEntry = route?.Entry;
        errand.VentExit = route?.Exit;
        // It grows out of the hive as it sets off.
        SetSize(bee, BeeFlightSize.Growing);
        return bee;
    }

    /// <summary>
    /// Whether one more bee may come out around <paramref name="coordinates"/>: bees on errands and angry bees within
    /// <see cref="BeekeepingCVars.AreaBeeRadius"/> are counted, once, and only when a bee is about to leave.
    /// </summary>
    public bool AreaHasRoom(EntityCoordinates coordinates)
    {
        var limit = _cfg.GetCVar(BeekeepingCVars.AreaBeeLimit);
        var radius = _cfg.GetCVar(BeekeepingCVars.AreaBeeRadius);
        _errandsNear.Clear();
        _angryNear.Clear();
        _lookup.GetEntitiesInRange(coordinates, radius, _errandsNear);
        _lookup.GetEntitiesInRange(coordinates, radius, _angryNear);
        return _errandsNear.Count + _angryNear.Count < limit;
    }

    #endregion

    #region The way

    /// <summary>
    /// Where the bee flies next and how close it has to get. False when there is nowhere to go: the hive is gone or the
    /// bee has nothing left to do on the way out.
    /// </summary>
    public bool TryGetNextStop(EntityUid uid, out EntityCoordinates coordinates, out float range)
    {
        coordinates = default;
        range = 0f;
        if (!TryComp<BeeErrandComponent>(uid, out var errand) || errand.Act != BeeErrandAct.None)
            return false;

        range = errand.StopRange;
        EntityUid? stop;
        if (errand.Stage == BeeErrandStage.Out)
        {
            if (errand.VentEntry != null && !errand.PipedOut)
            {
                stop = OpenVent(errand.VentEntry);
                if (stop == null)
                    ClearRoute(errand);
            }
            else
            {
                stop = null;
            }

            if (stop == null && errand.Target is { } target && !TerminatingOrDeleted(target))
                stop = target;

            if (stop == null)
            {
                // A spot to fly to, or nothing: then the bee acts where it is, and whoever sent it says what next.
                coordinates = errand.Spot ?? Transform(uid).Coordinates;
                range = errand.Spot == null ? float.MaxValue : errand.StopRange;
                Face(uid, coordinates);
                return true;
            }
        }
        else
        {
            if (TerminatingOrDeleted(errand.Hive) || _container.IsEntityInContainer(errand.Hive) && _scout.StorageOf(errand.Hive) == null)
                return false;

            stop = errand.VentExit != null && errand.PipedOut && !errand.PipedBack ? OpenVent(errand.VentExit) : null;
            if (stop == null)
            {
                stop = errand.Hive;
                range = HomeRange;
            }
        }

        // A locker a wild hive sits in is flown to, not the hive inside it.
        coordinates = Transform(_scout.StorageOf(stop) ?? stop.Value).Coordinates;
        Face(uid, coordinates);
        return true;
    }

    /// <summary>
    /// The vent, if it is still there and open.
    /// </summary>
    private EntityUid? OpenVent(EntityUid? vent)
    {
        return vent is { } uid && !TerminatingOrDeleted(uid) && !_weldable.IsWelded(uid) ? uid : null;
    }

    private static void ClearRoute(BeeErrandComponent errand)
    {
        errand.VentEntry = null;
        errand.VentExit = null;
    }

    /// <summary>
    /// A new target or spot for a bee that is on its way out: a creature that moved, a swarm's next hop.
    /// </summary>
    public void SetTarget(Entity<BeeErrandComponent?> bee, EntityUid? target, EntityCoordinates? spot = null)
    {
        if (!Resolve(bee, ref bee.Comp))
            return;

        bee.Comp.Target = target;
        bee.Comp.Spot = spot;
    }

    /// <summary>
    /// A way through the pipes from <paramref name="entry"/> to <paramref name="exit"/> for a bee on its way out.
    /// </summary>
    public void SetRoute(Entity<BeeErrandComponent?> bee, EntityUid entry, EntityUid exit)
    {
        if (!Resolve(bee, ref bee.Comp))
            return;

        bee.Comp.VentEntry = entry;
        bee.Comp.VentExit = exit;
        bee.Comp.PipedOut = false;
    }

    /// <summary>
    /// The bee flies faster (or slower) than its prototype says: a furious meat colony's bees.
    /// </summary>
    public void SetSpeed(EntityUid bee, float multiplier)
    {
        if (!TryComp<MovementSpeedModifierComponent>(bee, out var move))
            return;

        _speed.ChangeBaseSpeed(bee,
            move.BaseWalkSpeed * multiplier,
            move.BaseSprintSpeed * multiplier,
            move.Acceleration,
            move);
    }

    #endregion

    #region At a stop

    /// <summary>
    /// The bee reached its stop: what it does there.
    /// </summary>
    public void StartAct(EntityUid uid)
    {
        if (!TryComp<BeeErrandComponent>(uid, out var errand))
            return;

        // It stops where it is to act: nothing steers it until the next stop is planned, and a flying body keeps
        // drifting.
        _physics.SetLinearVelocity(uid, System.Numerics.Vector2.Zero);

        var bee = new Entity<BeeErrandComponent>(uid, errand);
        if (errand.Stage == BeeErrandStage.Out)
        {
            if (errand.VentEntry != null && !errand.PipedOut)
            {
                StartStep(bee, BeeErrandAct.Pipe, BeeFlightSize.Shrinking, ShrinkTime(uid));
                return;
            }

            Arrive(bee);
            return;
        }

        if (errand.VentExit != null && errand.PipedOut && !errand.PipedBack)
        {
            StartStep(bee, BeeErrandAct.Pipe, BeeFlightSize.Shrinking, ShrinkTime(uid));
            return;
        }

        StartStep(bee, BeeErrandAct.Enter, BeeFlightSize.Shrinking, ShrinkTime(uid));
    }

    /// <summary>
    /// At the target: the system of that kind of bee does what the errand is for; a forager or the scout visits the
    /// flower.
    /// </summary>
    private void Arrive(Entity<BeeErrandComponent> bee)
    {
        var ev = new BeeErrandArrivedEvent(bee.Comp.Target);
        RaiseLocalEvent(bee, ref ev);

        // An empty spot with nothing handling it: there is nothing to do there, go home.
        var result = ev.Handled ? ev.Result
            : bee.Comp.Target == null ? BeeErrandArrival.TurnBack
            : BeeErrandArrival.Visit;

        switch (result)
        {
            case BeeErrandArrival.Visit:
                StartStep(bee, BeeErrandAct.Visit, BeeFlightSize.Shrinking, ShrinkTime(bee));
                break;
            case BeeErrandArrival.TurnBack:
                bee.Comp.Stage = BeeErrandStage.Back;
                break;
            case BeeErrandArrival.KeepGoing:
                // A target that keeps moving off is given up after a few tries; a searching swarm keeps on.
                if (bee.Comp.Kind != BeeErrandKind.Swarm && ++bee.Comp.Retargets > MaxRetargets)
                    bee.Comp.Stage = BeeErrandStage.Back;
                break;
            case BeeErrandArrival.Gone:
                End(bee);
                break;
        }
    }

    public HTNOperatorStatus UpdateAct(EntityUid uid)
    {
        if (!TryComp<BeeErrandComponent>(uid, out var errand) || TerminatingOrDeleted(uid))
            return HTNOperatorStatus.Failed;

        var bee = new Entity<BeeErrandComponent>(uid, errand);
        if (errand.Act == BeeErrandAct.None)
            return HTNOperatorStatus.Finished;

        if (_timing.CurTime < errand.ActStepEnd)
            return HTNOperatorStatus.Continuing;

        switch (errand.Act)
        {
            case BeeErrandAct.Visit:
                return NextVisitStep(bee);
            case BeeErrandAct.Pipe:
                return NextPipeStep(bee);
            case BeeErrandAct.Enter:
                EnterHive(bee);
                return HTNOperatorStatus.Finished;
        }

        return HTNOperatorStatus.Finished;
    }

    /// <summary>
    /// Into the flower, a while inside, and back out: then home.
    /// </summary>
    private HTNOperatorStatus NextVisitStep(Entity<BeeErrandComponent> bee)
    {
        switch (bee.Comp.ActStep)
        {
            case 0:
                Hide(bee, true);
                StartStep(bee, BeeErrandAct.Visit, BeeFlightSize.Hidden, bee.Comp.VisitTime, 1);
                return HTNOperatorStatus.Continuing;
            case 1:
                Hide(bee, false);
                StartStep(bee, BeeErrandAct.Visit, BeeFlightSize.Growing, ShrinkTime(bee), 2);
                return HTNOperatorStatus.Continuing;
            default:
                bee.Comp.Act = BeeErrandAct.None;
                bee.Comp.ActStep = 0;
                bee.Comp.Stage = BeeErrandStage.Back;
                return HTNOperatorStatus.Finished;
        }
    }

    /// <summary>
    /// Into the vent, through the pipes, and out of the vent at the other end. If that one is shut by now, the bee is
    /// lost in the pipes.
    /// </summary>
    private HTNOperatorStatus NextPipeStep(Entity<BeeErrandComponent> bee)
    {
        var comp = bee.Comp;
        var outward = !comp.PipedOut;
        switch (comp.ActStep)
        {
            case 0:
                Hide(bee, true);
                StartStep(bee, BeeErrandAct.Pipe, BeeFlightSize.Hidden, PipeTime(comp), 1);
                return HTNOperatorStatus.Continuing;
            case 1:
                if (OpenVent(outward ? comp.VentExit : comp.VentEntry) is not { } exit)
                {
                    QueueDel(bee);
                    return HTNOperatorStatus.Failed;
                }

                _transform.SetCoordinates(bee, Transform(exit).Coordinates);
                Hide(bee, false);
                StartStep(bee, BeeErrandAct.Pipe, BeeFlightSize.Growing, ShrinkTime(bee), 2);
                return HTNOperatorStatus.Continuing;
            default:
                comp.Act = BeeErrandAct.None;
                comp.ActStep = 0;
                if (outward)
                    comp.PipedOut = true;
                else
                    comp.PipedBack = true;
                return HTNOperatorStatus.Finished;
        }
    }

    /// <summary>
    /// How long the bee is in the pipes: the straight distance between the two vents over the pipe speed, and a bit.
    /// </summary>
    private TimeSpan PipeTime(BeeErrandComponent errand)
    {
        if (errand.VentEntry is not { } entry || errand.VentExit is not { } exit ||
            TerminatingOrDeleted(entry) || TerminatingOrDeleted(exit))
        {
            return TimeSpan.FromSeconds(1);
        }

        var distance = (_transform.GetWorldPosition(entry) - _transform.GetWorldPosition(exit)).Length();
        return TimeSpan.FromSeconds(distance / PipeSpeed + 0.5f);
    }

    /// <summary>
    /// The bee went into its hive: the errand is over. A scout's findings become the hive's.
    /// </summary>
    private void EnterHive(Entity<BeeErrandComponent> bee)
    {
        if (bee.Comp.Kind == BeeErrandKind.Scout)
            _scout.LandScout(bee);

        End(bee);
    }

    /// <summary>
    /// The errand is over without the bee being lost: it is home, or it built a hive, or it scattered.
    /// </summary>
    private void End(Entity<BeeErrandComponent> bee)
    {
        bee.Comp.Ended = true;
        bee.Comp.Act = BeeErrandAct.None;
        if (TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive))
            hive.Foragers.Remove(bee);

        QueueDel(bee);
    }

    private void StartStep(Entity<BeeErrandComponent> bee, BeeErrandAct act, BeeFlightSize size, TimeSpan length, int step = 0)
    {
        bee.Comp.Act = act;
        bee.Comp.ActStep = step;
        bee.Comp.ActStepEnd = _timing.CurTime + length;
        SetSize(bee, size);
    }

    /// <summary>
    /// Inside a flower or the pipes the bee is out of the way: nothing bumps into it or hits it.
    /// </summary>
    private void Hide(EntityUid bee, bool hidden)
    {
        _physics.SetCanCollide(bee, !hidden);
    }

    #endregion

    #region Caught, killed, lost

    /// <summary>
    /// A forager caught by hand becomes a bee in hand, like one taken from the hive, and can be brought back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInteractHand(Entity<BeeErrandComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || ent.Comp.Kind != BeeErrandKind.Forage || ent.Comp.Act != BeeErrandAct.None)
            return;

        args.Handled = true;
        if (!TryComp<BeehiveComponent>(ent.Comp.Hive, out var comp) || TerminatingOrDeleted(ent.Comp.Hive))
            return;

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
        End(ent);
    }

    /// <summary>
    /// A killed bee is gone at once; it never makes it home.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<BeeErrandComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            QueueDel(ent);
    }

    /// <summary>
    /// A bee that goes without its errand being over (killed, lost in the pipes, out of time looking for its way) is a
    /// worker the colony loses. The scout keeps its place; only what it found is lost. A swarm's bees left the colony when
    /// it set off.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTerminating(Entity<BeeErrandComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Ended || TerminatingOrDeleted(ent.Comp.Hive) || !TryComp<BeehiveComponent>(ent.Comp.Hive, out var hive))
            return;

        ent.Comp.Ended = true;
        hive.Foragers.Remove(ent);
        if (ent.Comp.Kind is BeeErrandKind.Forage or BeeErrandKind.Hunt)
            _hive.RemoveHomeBee((ent.Comp.Hive, hive));

        var ev = new BeeErrandLostEvent(ent, ent.Comp.Hive, ent.Comp.Kind);
        RaiseLocalEvent(ref ev);
    }

    #endregion

    #region Looks

    /// <summary>
    /// The sprite is a side view facing left: mirror it while the bee heads to the right of where it is, along its
    /// grid, since the camera turns with the grid the player stands on.
    /// </summary>
    private void Face(EntityUid bee, EntityCoordinates to)
    {
        var xform = Transform(bee);
        if (xform.GridUid is not { } grid)
            return;

        var from = _transform.WithEntityId(xform.Coordinates, grid).Position;
        var target = _transform.WithEntityId(to, grid).Position;
        SetFacing(bee, target - from);
    }

    public void SetFacing(EntityUid bee, System.Numerics.Vector2 localDirection)
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

    #endregion
}
