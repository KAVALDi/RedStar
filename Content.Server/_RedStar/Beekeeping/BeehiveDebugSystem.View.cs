using Content.Shared._RedStar.Beekeeping;
using Content.Server.Destructible;
using Content.Shared.Administration;
using Content.Shared.Damage.Components;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Spawners;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// The admin bee debug view: admins with the Debug flag who turned it on get, once a second, what every hive
/// knows (its trays and how each one checks out right now) and where the visible foragers fly. The client draws it over the map and lists it in the hive's debug window.
/// </summary>
public sealed partial class BeehiveDebugSystem
{
    private static readonly TimeSpan DebugViewInterval = TimeSpan.FromSeconds(1);

    [Dependency] private DestructibleSystem _destructible = default!;

    private readonly HashSet<ICommonSession> _debugViewers = new();
    private TimeSpan _nextDebugView;

    [SubscribeNetworkEvent]
    private void OnBeeDebugRequest(RequestBeeDebugEvent msg, EntitySessionEventArgs args)
    {
        if (!msg.Enabled)
        {
            _debugViewers.Remove(args.SenderSession);
            return;
        }

        if (_admin.HasAdminFlag(args.SenderSession, AdminFlags.Debug))
            _debugViewers.Add(args.SenderSession);
    }

    private void UpdateDebugView()
    {
        if (_debugViewers.Count == 0)
            return;

        var now = _timing.CurTime;
        if (now < _nextDebugView)
            return;

        _nextDebugView = now + DebugViewInterval;

        // Someone who left or lost the flag stops getting it.
        _debugViewers.RemoveWhere(s => s.Status == SessionStatus.Disconnected ||
                                       !_admin.HasAdminFlag(s, AdminFlags.Debug));
        if (_debugViewers.Count == 0)
            return;

        var hives = new List<BeeHiveDebugData>();
        var query = EntityQueryEnumerator<BeehiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            hives.Add(GetHiveDebugData((uid, comp), xform, now));
        }

        RaiseNetworkEvent(new BeeDebugStateEvent(hives), Filter.Empty().AddPlayers(_debugViewers));
    }

    /// <summary>
    /// The hive's bees outside: in hands or on the floor, angry, or lost on the way home.
    /// </summary>
    private void AddOutsideBees(EntityUid hive, BeeHiveDebugData data, TimeSpan now)
    {
        var items = EntityQueryEnumerator<BeeItemComponent, TimedDespawnComponent>();
        while (items.MoveNext(out _, out var bee, out var despawn))
        {
            if (bee.SourceHive != hive || !bee.CountedOutside)
                continue;

            data.Outside.Add(new BeeOutsideDebugData
            {
                Kind = bee.Role == BeeRole.Scout ? BeeOutsideKind.Scout : BeeOutsideKind.Pollinator,
                SecondsLeft = despawn.Lifetime,
            });
        }

        var foragers = EntityQueryEnumerator<BeeForagerComponent>();
        while (foragers.MoveNext(out _, out var forager))
        {
            if (forager.Hive != hive || forager.Phase != BeeForagerPhase.Lost)
                continue;

            data.Outside.Add(new BeeOutsideDebugData
            {
                Kind = BeeOutsideKind.Lost,
                SecondsLeft = (float) (forager.PhaseEnd - now).TotalSeconds,
            });
        }
    }

    /// <summary>
    /// When the next forager takes off, as an estimate: a queued one is about to appear; otherwise the visit waits for
    /// the workers' visit budget to fill and for an active tray that is due. Negative with no workers or no tray.
    /// </summary>
    private static float SoonestForagerTrip(BeehiveComponent comp, TimeSpan now)
    {
        TimeSpan? queued = null;
        foreach (var (_, at) in comp.PendingForagers)
        {
            if (queued == null || at < queued)
                queued = at;
        }

        if (queued is { } soonest)
            return Math.Max(0f, (float) (soonest - now).TotalSeconds);

        TimeSpan? due = null;
        foreach (var source in comp.KnownSources)
        {
            if (source.Active && (due == null || source.NextVisit < due))
                due = source.NextVisit;
        }

        var workers = SharedBeehiveSystem.WorkersAtHome(comp);
        var interval = (float) comp.VisitInterval.TotalSeconds;
        if (due == null || workers <= 0 || interval <= 0f)
            return -1f;

        // The budget grows by workers per interval, faster under the admin speed-up.
        var perSecond = workers * Math.Max(comp.DebugSpeed, 0.01f) / interval;
        var budgetWait = Math.Max(0f, (1f - comp.VisitBudget) / perSecond);
        var sourceWait = Math.Max(0f, (float) (due.Value - now).TotalSeconds);
        return Math.Max(budgetWait, sourceWait);
    }

    private BeeHiveDebugData GetHiveDebugData(Entity<BeehiveComponent> hive, TransformComponent xform, TimeSpan now)
    {
        var comp = hive.Comp;
        var hiveMap = _transform.GetMapCoordinates(hive, xform);
        var data = new BeeHiveDebugData
        {
            Hive = GetNetEntity(hive),
            Map = hiveMap.MapId,
            Position = hiveMap.Position,
            Bees = comp.Bees,
            OutBees = comp.OutBees,
            MaxBees = comp.MaxBees,
            Nectar = comp.Nectar,
            MaxNectar = comp.MaxNectar,
            HasQueen = _hive.GetQueen(hive) != null,
            Pause = comp.Pause,
            ScoutState = comp.ScoutState,
            ScoutRange = comp.ScoutRange,
            ScoutScanRadius = comp.ScoutScanRadius,
            VisitRange = comp.VisitRange,
            NextScoutTrip = SharedBeehiveSystem.ScoutsAtHome(comp) > 0 && comp.NextScoutTrip != TimeSpan.Zero
                ? (float) (comp.NextScoutTrip - now).TotalSeconds
                : -1f,
            NextForagerTrip = SoonestForagerTrip(comp, now),
        };

        foreach (var source in comp.KnownSources)
        {
            var state = _scout.CheckSource(hive, xform, hiveMap, source, out _) switch
            {
                BeeSourceCheck.Ripe => BeeSourceDebugState.Ripe,
                BeeSourceCheck.Unripe => BeeSourceDebugState.Unripe,
                BeeSourceCheck.Inactive => BeeSourceDebugState.Inactive,
                _ => BeeSourceDebugState.Invalid,
            };

            data.Sources.Add(new BeeSourceDebugData
            {
                Tray = GetNetEntity(source.Tray),
                // Any plant in the tray, dead or alive: the picture shows what is there.
                Plant = _trayQuery.TryComp(source.Tray, out var trayComp) &&
                        trayComp.PlantEntity is { } plant &&
                        !TerminatingOrDeleted(plant)
                    ? GetNetEntity(plant)
                    : null,
                Position = TerminatingOrDeleted(source.Tray)
                    ? hiveMap.Position
                    : _transform.GetWorldPosition(source.Tray),
                State = state,
                NextVisit = (float) (source.NextVisit - now).TotalSeconds,
            });
        }

        if (_scout.ScoutFlying(comp) &&
            comp.ScoutFlyer is { } flyer &&
            TryComp<BeeScoutFlightComponent>(flyer, out var flight) &&
            Transform(flyer).GridUid is { } scoutGrid &&
            TryComp<MapGridComponent>(scoutGrid, out var scoutGridComp))
        {
            // Where its current walk ends: the last tile of the path, or the end of the current step.
            var end = flight.Path.Count > 0
                ? _map.TileCenterToVector((scoutGrid, scoutGridComp), flight.Path[^1])
                : flight.StepTo;
            data.Scout = new BeeForagerDebugData
            {
                Position = _transform.GetWorldPosition(flyer),
                Target = System.Numerics.Vector2.Transform(end, _transform.GetWorldMatrix(scoutGrid)),
            };
            foreach (var tray in flight.Found)
            {
                if (!TerminatingOrDeleted(tray))
                    data.ScoutFound.Add(_transform.GetWorldPosition(tray));
            }
        }

        foreach (var bee in comp.Foragers)
        {
            if (TerminatingOrDeleted(bee) ||
                !TryComp<BeeForagerComponent>(bee, out var forager) ||
                Transform(bee).GridUid is not { } beeGrid)
            {
                continue;
            }

            var gridMatrix = _transform.GetWorldMatrix(beeGrid);
            data.Foragers.Add(new BeeForagerDebugData
            {
                Position = _transform.GetWorldPosition(bee),
                Target = System.Numerics.Vector2.Transform(forager.Target, gridMatrix),
            });
        }

        AddOutsideBees(hive, data, now);
        data.HasEgg = comp.HasEgg;
        data.EggLeft = comp.HasEgg ? (float) (comp.EggDiesAt - now).TotalSeconds : 0f;
        data.EggCooldown = Math.Max(0f, (float) (comp.EggCooldownUntil - now).TotalSeconds);
        data.EggFullFor = comp.EggFullSince is { } full
            ? Math.Max(0f, (float) (comp.EggFullTime * _hive.GetHiveTotals(hive).EggWait / Math.Max(comp.DebugSpeed, 0.01f) - (now - full)).TotalSeconds)
            : 0f;

        _hive.GetFrames(hive, _frames);
        foreach (var frame in _frames)
        {
            data.Combs += frame.Comp.Combs;
            data.MaxCombs += frame.Comp.MaxCombs;
        }

        if (_hive.GetQueen(hive) is { } queenUid && TryComp<BeeQueenComponent>(queenUid, out var queen))
        {
            data.Traits.AddRange(queen.Traits);
            data.Slots = queen.Slots;
            if (_solutions.TryGetSolution(queenUid, queen.Solution, out _, out var payload))
                data.Payload = payload.Volume.Float();
        }

        if (TryComp<DamageableComponent>(hive, out var damageable))
        {
            var max = _destructible.DestroyedAt(hive).Float();
            data.MaxHealth = max;
            // Only shown to admins as a rough number; nothing in the game depends on it.
#pragma warning disable CS0618
            data.Health = Math.Max(0f, max - _damageable.GetTotalDamage((hive, damageable)).Float());
#pragma warning restore CS0618
        }

        return data;
    }
}
