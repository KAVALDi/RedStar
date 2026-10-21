using Content.Server.NPC.Pathfinding;
using System.Numerics;
using Content.Shared.Botany.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Random.Helpers;
using Content.Shared.Storage.Components;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// The scout and the hive's known sources. When a trip is due the scout grows out of the hive and explores tile by
/// tile over free tiles, so it never passes walls, windows or closed doors; it prefers tiles the hive has not explored
/// yet. On every tile it looks around: a tray with a living plant the hive could visit in a straight line is noted, the
/// scout flies to it and back, and the hive learns the trays when it lands. The paths come from the engine's tile
/// pathfinder. The sources part keeps the known trays checked and sends the foragers out.
/// </summary>
public sealed partial class BeeScoutSystem : EntitySystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private BeeForagerSystem _foragers = default!;
    [Dependency] private BeeQueenSystem _queen = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MeatBeehiveSystem _meat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private BeehiveSystem _hive = default!;
    [Dependency] private IGameTiming _timing = default!;
    private static readonly Vector2i[] Steps4 = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    private readonly Dictionary<Vector2i, bool> _freeTiles = new();
    private readonly List<Vector2i> _unexplored = new();
    private readonly List<Vector2i> _candidates = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateScoutFlights();
    }

    /// <summary>
    /// Sends the scout out on a flight. Returns false if the hive is not on a grid.
    /// </summary>
    public bool LaunchScout(Entity<BeehiveComponent> hive)
    {
        var xform = Transform(hive);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var start = _map.TileCenterToVector((grid, gridComp), tile);
        var bee = Spawn(hive.Comp.ScoutFlyerPrototype, new EntityCoordinates(grid, start));
        if (!TryComp<BeeScoutFlightComponent>(bee, out var flight))
        {
            Del(bee);
            return false;
        }

        if (_meat.MeatLook(hive) is { } look)
            _queen.SetTraitVisuals(bee, look, false);

        flight.Hive = hive;
        flight.Phase = BeeScoutPhase.Emerge;
        flight.MaxFind = Math.Clamp(flight.MaxFind + _hive.GetHiveTotals(hive).ScoutFinds, 1, 5);
        flight.StepFrom = flight.StepTo = start;
        flight.PhaseEnd = _timing.CurTime + _foragers.ShrinkTime(bee);
        hive.Comp.ScoutFlyer = bee;
        _foragers.SetSize(bee, BeeFlightSize.Growing);
        return true;
    }

    private void UpdateScoutFlights()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BeeScoutFlightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flight, out var xform))
        {
            if (TerminatingOrDeleted(flight.Hive) ||
                !TryComp<BeehiveComponent>(flight.Hive, out var hive) ||
                xform.GridUid is not { } grid ||
                !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                QueueDel(uid);
                continue;
            }

            // Tile checks are cached per scout and tick; scouts on other grids must not share them.
            _freeTiles.Clear();
            var scout = new Entity<BeeScoutFlightComponent>(uid, flight);
            var hiveEnt = new Entity<BeehiveComponent>(flight.Hive, hive);
            var gridEnt = new Entity<MapGridComponent>(grid, gridComp);

            switch (flight.Phase)
            {
                case BeeScoutPhase.Emerge:
                    if (now < flight.PhaseEnd)
                        break;

                    _foragers.SetSize(uid, BeeFlightSize.Full);
                    flight.Phase = BeeScoutPhase.Explore;
                    ScoutArrived(scout, hiveEnt, gridEnt, now);
                    break;

                case BeeScoutPhase.Explore:
                case BeeScoutPhase.ToPlant:
                case BeeScoutPhase.Home:
                    if (!StepAlong(scout, now))
                        ScoutArrived(scout, hiveEnt, gridEnt, now);
                    break;

                case BeeScoutPhase.DiveIn:
                    if (now < flight.PhaseEnd)
                        break;

                    _foragers.SetSize(uid, BeeFlightSize.Hidden);
                    flight.Phase = BeeScoutPhase.Feeding;
                    flight.PhaseEnd = now + flight.FeedTime;
                    break;

                case BeeScoutPhase.Feeding:
                    if (now < flight.PhaseEnd)
                        break;

                    _foragers.SetSize(uid, BeeFlightSize.Growing);
                    flight.Phase = BeeScoutPhase.DiveOut;
                    flight.PhaseEnd = now + _foragers.ShrinkTime(uid);
                    break;

                case BeeScoutPhase.DiveOut:
                    if (now < flight.PhaseEnd)
                        break;

                    _foragers.SetSize(uid, BeeFlightSize.Full);
                    NoticeNearby(scout, hiveEnt, gridEnt);
                    GoHome(scout, hiveEnt, gridEnt, now);
                    break;

                case BeeScoutPhase.Land:
                    if (now < flight.PhaseEnd)
                        break;

                    LandScout(scout, flight.Hive);
                    QueueDel(uid);
                    break;
            }
        }
    }

    /// <summary>
    /// Moves the scout along its current step. Returns false once the step and the path are done.
    /// </summary>
    private bool StepAlong(Entity<BeeScoutFlightComponent> scout, TimeSpan now)
    {
        var flight = scout.Comp;
        var total = (flight.StepEnd - flight.StepStart).TotalSeconds;
        var progress = total > 0 ? (float) Math.Clamp((now - flight.StepStart).TotalSeconds / total, 0, 1) : 1f;
        _transform.SetLocalPosition(scout, Vector2.Lerp(flight.StepFrom, flight.StepTo, progress));

        if (progress < 1f)
            return true;

        if (flight.Path.Count == 0)
            return false;

        var xform = Transform(scout);
        var grid = xform.GridUid!.Value;
        var gridEnt = new Entity<MapGridComponent>(grid, Comp<MapGridComponent>(grid));

        // Cut corners: fly straight to the farthest tile of the path that is in clear sight, not tile by tile.
        var from = _transform.GetMapCoordinates(scout, xform);
        while (flight.Path.Count > 1 &&
               ClearFlightPath(from, _transform.ToMapCoordinates(
                   new EntityCoordinates(grid, _map.TileCenterToVector(gridEnt, flight.Path[1]))), scout))
        {
            flight.Path.RemoveAt(0);
        }

        var next = flight.Path[0];
        flight.Path.RemoveAt(0);
        StartStep(scout, _map.TileCenterToVector(gridEnt, next), now);
        return true;
    }

    private void StartStep(Entity<BeeScoutFlightComponent> scout, Vector2 to, TimeSpan now)
    {
        var flight = scout.Comp;
        var xform = Transform(scout);
        flight.StepFrom = xform.LocalPosition;
        flight.StepTo = to;

        flight.StepStart = now;
        flight.StepEnd = now + TimeSpan.FromSeconds((to - flight.StepFrom).Length() / Math.Max(0.1f, flight.Speed));
    }

    /// <summary>
    /// The scout reached the end of its path: decide what it does next.
    /// </summary>
    private void ScoutArrived(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        Entity<MapGridComponent> grid,
        TimeSpan now)
    {
        var flight = scout.Comp;
        switch (flight.Phase)
        {
            case BeeScoutPhase.ToPlant:
                _foragers.SetSize(scout, BeeFlightSize.Shrinking);
                flight.Phase = BeeScoutPhase.DiveIn;
                flight.PhaseEnd = now + _foragers.ShrinkTime(scout);
                return;

            case BeeScoutPhase.Home:
                // The last bit to the hive itself, which need not stand in the middle of its tile.
                var hiveXform = Transform(hive);
                if (hiveXform.GridUid == grid.Owner)
                {
                    var hivePos = _transform.WithEntityId(hiveXform.Coordinates, grid).Position;
                    if ((hivePos - Transform(scout).LocalPosition).Length() > 0.05f)
                    {
                        StartStep(scout, hivePos, now);
                        return;
                    }
                }

                _foragers.SetSize(scout, BeeFlightSize.Shrinking);
                flight.Phase = BeeScoutPhase.Land;
                flight.PhaseEnd = now + _foragers.ShrinkTime(scout);
                return;
        }

        // Exploring: this tile is explored now; look around it.
        var tile = _map.TileIndicesFor(grid, grid.Comp, Transform(scout).Coordinates);
        hive.Comp.ScoutExplored.Add(tile);
        flight.Steps++;

        if (ScoutLookAround(scout, hive, grid, tile) is { } plant)
        {
            flight.Plant = plant;
            var plantTile = _map.TileIndicesFor(grid, grid.Comp, Transform(plant).Coordinates);
            if (FindPath(grid, hive, tile, plantTile, flight.Path))
            {
                flight.Phase = BeeScoutPhase.ToPlant;
                StepAlong(scout, now);
                return;
            }
        }

        if (flight.Steps >= flight.MaxSteps || !PickNextTile(scout, hive, grid, tile))
        {
            GoHome(scout, hive, grid, now);
            return;
        }

        StepAlong(scout, now);
    }

    /// <summary>
    /// The next tile to explore: a free unexplored neighbour if there is one, otherwise the path to the nearest
    /// unexplored free tile in range. With nothing left to explore, the hive's memory of explored tiles starts
    /// over and the scout goes home. Returns false if it should go home.
    /// </summary>
    private bool PickNextTile(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        var explored = hive.Comp.ScoutExplored;
        _candidates.Clear();
        foreach (var step in Steps4)
        {
            var next = tile + step;
            if (!explored.Contains(next) && InScoutRange(hive, grid, next) && IsFreeTile(grid, next))
                _candidates.Add(next);
        }

        scout.Comp.Path.Clear();
        if (_candidates.Count > 0)
        {
            scout.Comp.Path.Add(_random.Pick(_candidates));
            return true;
        }

        if (FindPath(grid, hive, tile, null, scout.Comp.Path))
            return true;

        explored.Clear();
        return false;
    }

    /// <summary>
    /// The tiles to fly, without the start, from <paramref name="start"/> to <paramref name="goal"/>, or to the nearest
    /// unexplored tile if the goal is null, over free tiles in scouting range. The engine's tile pathfinder does the
    /// search; this only says which tiles a bee can fly through. Written into <paramref name="path"/>.
    /// </summary>
    private bool FindPath(Entity<MapGridComponent> grid,
        Entity<BeehiveComponent> hive,
        Vector2i start,
        Vector2i? goal,
        List<Vector2i> path)
    {
        path.Clear();

        // The goal tile itself may hold the tray or the hive; it only has to be reachable.
        float Cost(Vector2i tile) =>
            InScoutRange(hive, grid, tile, 1) && (tile == goal || IsFreeTile(grid, tile)) ? 1f : 0f;

        PathfindingSystem.SimplePathResult result;
        if (goal is { } target)
        {
            result = _pathfinding.GetPath(new PathfindingSystem.SimplePathArgs
            {
                Start = start,
                End = target,
                TileCost = Cost,
            });
        }
        else
        {
            _unexplored.Clear();
            var hiveTile = _map.TileIndicesFor(grid, grid.Comp, Transform(hive).Coordinates);
            var reach = (int) MathF.Ceiling(hive.Comp.ScoutRange);
            for (var x = -reach; x <= reach; x++)
            {
                for (var y = -reach; y <= reach; y++)
                {
                    var tile = hiveTile + new Vector2i(x, y);
                    if (tile != start &&
                        !hive.Comp.ScoutExplored.Contains(tile) &&
                        InScoutRange(hive, grid, tile) &&
                        IsFreeTile(grid, tile))
                    {
                        _unexplored.Add(tile);
                    }
                }
            }

            if (_unexplored.Count == 0)
                return false;

            result = _pathfinding.GetBreadthPath(new PathfindingSystem.BreadthPathArgs
            {
                Start = start,
                Ends = _unexplored,
                TileCost = Cost,
            });
        }

        // The pathfinder's path starts with the start tile.
        if (result.Path is not { Count: > 1 } found)
            return false;

        for (var i = 1; i < found.Count; i++)
        {
            path.Add(found[i]);
        }

        return true;
    }

    private bool InScoutRange(Entity<BeehiveComponent> hive, Entity<MapGridComponent> grid, Vector2i tile, float extra = 0f)
    {
        var hiveTile = _map.TileIndicesFor(grid, grid.Comp, Transform(hive).Coordinates);
        return (tile - hiveTile).Length <= hive.Comp.ScoutRange + extra;
    }

    /// <summary>
    /// A tile a bee can fly through: floor, and nothing a flying bee bumps into. Cached for the current scout.
    /// </summary>
    private bool IsFreeTile(Entity<MapGridComponent> grid, Vector2i tile)
    {
        if (_freeTiles.TryGetValue(tile, out var free))
            return free;

        free = !_turf.IsSpace(_map.GetTileRef(grid, tile)) &&
               !_turf.IsTileBlocked(grid, tile, CollisionGroup.FlyingMobMask, grid.Comp);
        _freeTiles[tile] = free;
        return free;
    }

    /// <summary>
    /// Looks around the tile for a tray with a living plant the hive could visit. Returns it, if the scout still
    /// has room to remember one.
    /// </summary>
    private EntityUid? ScoutLookAround(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        if (scout.Comp.Found.Count >= scout.Comp.MaxFind)
            return null;

        var center = _transform.ToMapCoordinates(new EntityCoordinates(grid, _map.TileCenterToVector(grid, tile)));
        _foundTrays.Clear();
        _lookup.GetEntitiesInRange(center, scout.Comp.SightRadius, _foundTrays);
        foreach (var tray in _foundTrays)
        {
            if (scout.Comp.Found.Contains(tray) || !ScoutCanUse(scout, hive, grid, tray, center))
                continue;

            scout.Comp.Found.Add(tray);
            return tray;
        }

        return null;
    }

    /// <summary>
    /// A tray the scout may note: a living plant, on the hive's grid, not inside anything, not known to the hive
    /// yet, seen from where the scout is, and in a clear straight line from the hive so foragers can fly there.
    /// </summary>
    private bool ScoutCanUse(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        EntityUid grid,
        Entity<PlantTrayComponent> tray,
        MapCoordinates from)
    {
        var trayXform = Transform(tray);
        if (trayXform.GridUid != grid || _container.IsEntityInContainer(tray) || GetLivingPlant(tray.Comp) == null)
            return false;

        if (hive.Comp.SourceWhitelist != null && !_whitelist.IsValid(hive.Comp.SourceWhitelist, tray))
            return false;

        foreach (var known in hive.Comp.KnownSources)
        {
            if (known.Tray == tray.Owner && known.Active)
                return false;
        }

        var trayMap = _transform.GetMapCoordinates(tray, trayXform);
        var hiveMap = _transform.GetMapCoordinates(hive);
        return (trayMap.Position - hiveMap.Position).Length() <= hive.Comp.VisitRange &&
               ClearFlightPath(from, trayMap, scout, tray) &&
               ClearFlightPath(hiveMap, trayMap, hive, tray);
    }

    /// <summary>
    /// Sitting in the flower, the scout notices other trays right next to it, up to how many it can remember.
    /// </summary>
    private void NoticeNearby(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        Entity<MapGridComponent> grid)
    {
        if (scout.Comp.Plant is not { } plant || TerminatingOrDeleted(plant))
            return;

        var center = _transform.GetMapCoordinates(plant);
        _foundTrays.Clear();
        _lookup.GetEntitiesInRange(center, scout.Comp.NearbyRadius, _foundTrays);
        foreach (var tray in _foundTrays)
        {
            if (scout.Comp.Found.Count >= scout.Comp.MaxFind)
                return;

            if (!scout.Comp.Found.Contains(tray) && ScoutCanUse(scout, hive, grid, tray, center))
                scout.Comp.Found.Add(tray);
        }
    }

    /// <summary>
    /// Walks home tile by tile; if the hive was moved where no free path leads, it flies straight.
    /// </summary>
    private void GoHome(Entity<BeeScoutFlightComponent> scout,
        Entity<BeehiveComponent> hive,
        Entity<MapGridComponent> grid,
        TimeSpan now)
    {
        var flight = scout.Comp;
        flight.Phase = BeeScoutPhase.Home;
        var here = _map.TileIndicesFor(grid, grid.Comp, Transform(scout).Coordinates);
        var hiveTile = _map.TileIndicesFor(grid, grid.Comp, Transform(hive).Coordinates);
        if (!FindPath(grid, hive, here, hiveTile, flight.Path))
        {
            flight.Path.Clear();
            flight.Path.Add(hiveTile);
        }

        StepAlong(scout, now);
    }

    /// <summary>
    /// The scout is back: the hive learns every tray it saw that still checks out.
    /// </summary>
    private void LandScout(Entity<BeeScoutFlightComponent> scout, EntityUid hiveUid)
    {
        if (TerminatingOrDeleted(hiveUid) || !TryComp<BeehiveComponent>(hiveUid, out var comp))
            return;

        var hive = new Entity<BeehiveComponent>(hiveUid, comp);
        var now = _timing.CurTime;
        var xform = Transform(hiveUid);
        var hiveMap = _transform.GetMapCoordinates(hiveUid, xform);

        foreach (var tray in scout.Comp.Found)
        {
            if (TerminatingOrDeleted(tray) || AddSource(hive, tray, now) is not { } source)
                continue;

            if (CheckSource(hive, xform, hiveMap, source, out _) == BeeSourceCheck.Inactive)
            {
                source.Active = false;
                source.NextVisit = now + comp.InactiveRecheck;
            }
        }

        scout.Comp.Found.Clear();
        if (comp.ScoutFlyer == scout.Owner)
            comp.ScoutFlyer = null;
    }

    /// <summary>
    /// The scout in the air is gone at once: it was caught, lost or the hive stopped. Its findings are lost too.
    /// </summary>
    public void RecallScout(BeehiveComponent comp)
    {
        if (comp.ScoutFlyer is { } flyer && !TerminatingOrDeleted(flyer))
            QueueDel(flyer);

        comp.ScoutFlyer = null;
    }

    /// <summary>
    /// True while this hive's scout is in the air.
    /// </summary>
    public bool ScoutFlying(BeehiveComponent comp)
    {
        if (comp.ScoutFlyer is not { } flyer)
            return false;

        if (!TerminatingOrDeleted(flyer))
            return true;

        comp.ScoutFlyer = null;
        return false;
    }

    /// <summary>
    /// Whether a bee can fly straight from one point to the other: no wall, window or closed door between them.
    /// Open doors let it through. The two ends themselves (the hive, the tray, the bee) never block.
    /// </summary>
    /// <summary>
    /// The locker, closet or crate the entity sits in, if any. Bees fly in and out of a wild hive there even with
    /// the door shut.
    /// </summary>
    public EntityUid? StorageOf(EntityUid? uid)
    {
        if (uid is not { } inside ||
            !_container.TryGetContainingContainer(inside, out var container) ||
            !HasComp<EntityStorageComponent>(container.Owner))
        {
            return null;
        }

        return container.Owner;
    }

    public bool ClearFlightPath(MapCoordinates from, MapCoordinates to, EntityUid? ignoreA = null, EntityUid? ignoreB = null)
    {
        if (from.MapId != to.MapId)
            return false;

        var delta = to.Position - from.Position;
        var length = delta.Length();
        if (length < 0.01f)
            return true;

        return FreeFlight(from, delta / length, length, ignoreA, ignoreB) >= length;
    }

    /// <summary>
    /// How far a bee flies from <paramref name="from"/> along <paramref name="direction"/> before something solid
    /// stops it, up to <paramref name="maxLength"/>.
    /// </summary>
    public float FreeFlight(MapCoordinates from, Vector2 direction, float maxLength, EntityUid? ignoreA = null,
        EntityUid? ignoreB = null)
    {
        // A hive or tray inside a locker: the locker does not block its own bees.
        var shellA = StorageOf(ignoreA);
        var shellB = StorageOf(ignoreB);

        // Not the first hit the query returns: that is not always the nearest one.
        var ray = new CollisionRay(from.Position, direction, (int) CollisionGroup.FlyingMobMask);
        var free = maxLength;
        foreach (var hit in _physics.IntersectRayWithPredicate(from.MapId, ray, maxLength,
                     uid => uid == ignoreA || uid == ignoreB || uid == shellA || uid == shellB, returnOnFirstHit: false))
        {
            free = Math.Min(free, hit.Distance);
        }

        return free;
    }
}
