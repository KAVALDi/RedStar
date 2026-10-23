using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._RedStar.Beekeeping.Components;
using Content.Server._RedStar.Beekeeping.Data;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared._RedStar.Beekeeping;
using Content.Shared._RedStar.Beekeeping.Components;
using Content.Shared._RedStar.Beekeeping.Data;
using Content.Shared._RedStar.Beekeeping.Systems;
using Content.Shared._RedStar.Beekeeping.Visuals;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping.Systems;

/// <summary>
/// Letting bees out: the hive's foragers and scout, a loitering bee, an angry bee sent home, and the limits on how many
/// are out.
/// </summary>
public sealed partial class BeeErrandSystem
{
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

        // The visits are counted already: with nobody around to see them, the bees are not shown at all.
        if (!SomeoneWatching(xform.Coordinates))
        {
            comp.PendingForagers.Clear();
            return;
        }

        for (var i = comp.PendingForagers.Count - 1; i >= 0; i--)
        {
            var (tray, at) = comp.PendingForagers[i];
            if (now < at)
                continue;

            comp.PendingForagers.RemoveAt(i);

            // No bee flies off into fire or bad air; the visit is counted all the same.
            if (!TerminatingOrDeleted(tray) && _hive.AirFor(comp, tray) == BeehivePauseReason.None)
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

        if (Release(hive, hiveXform, prototype ?? comp.PollinatorPrototype, target, route) is not { } bee)
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
            Release(hive, hiveXform, hive.Comp.ScoutPrototype, found[0], null, kind: BeeErrandKind.Scout) is not { } bee)
        {
            return false;
        }

        Comp<BeeErrandComponent>(bee).Found = new List<EntityUid>(found);
        hive.Comp.ScoutFlyer = bee;
        return true;
    }

    /// <summary>
    /// A bee of <paramref name="prototype"/> comes out of the hive on its errand. A prototype that names its own kind of
    /// errand keeps it; any other bee goes on a <paramref name="kind"/> errand.
    /// </summary>
    public EntityUid? Release(EntityUid hive,
        TransformComponent hiveXform,
        string prototype,
        EntityUid? target,
        (EntityUid Entry, EntityUid Exit)? route,
        EntityCoordinates? spot = null,
        BeeErrandKind kind = BeeErrandKind.Forage)
    {
        var bee = Spawn(prototype, hiveXform.Coordinates);
        if (!HasComp<HTNComponent>(bee))
        {
            Log.Error($"{prototype} has no {nameof(HTNComponent)} and cannot fly an errand");
            Del(bee);
            return null;
        }

        if (!TryComp<BeeErrandComponent>(bee, out var errand))
        {
            errand = AddComp<BeeErrandComponent>(bee);
            errand.Kind = kind;
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
    /// Whether a player is close enough to <paramref name="coordinates"/> to see a bee there. Ghosts and observers
    /// count: an admin may be watching.
    /// </summary>
    public bool SomeoneWatching(EntityCoordinates coordinates)
    {
        _watchers.Clear();
        _lookup.GetEntitiesInRange(coordinates, WatchRange, _watchers);
        return _watchers.Count > 0;
    }

    /// <summary>
    /// Now and then, while someone is near and a worker is free, one flies a short loop around the hive and back in.
    /// It is only something to look at: the colony loses nothing by it, unless the bee is killed.
    /// </summary>
    public void UpdateLoiter(Entity<BeehiveComponent> hive)
    {
        var comp = hive.Comp;
        var now = _timing.CurTime;

        // A colony that has just got its queen settles in first: no bee circles the hive the moment she goes in.
        if (comp.NextLoiter == TimeSpan.Zero)
        {
            comp.NextLoiter = now + comp.LoiterInterval * (0.5 + _random.NextDouble());
            return;
        }

        if (now < comp.NextLoiter)
            return;

        comp.NextLoiter = now + comp.LoiterInterval * (0.5 + _random.NextDouble());
        var xform = Transform(hive);
        comp.Foragers.RemoveAll(f => TerminatingOrDeleted(f));
        if (SharedBeehiveSystem.WorkersAtHome(comp) <= 0 ||
            comp.Foragers.Count >= comp.MaxForagers ||
            xform.GridUid is not { } grid ||
            !SomeoneWatching(xform.Coordinates) ||
            !AreaHasRoom(xform.Coordinates))
        {
            return;
        }

        // A spot a little way off in a random direction that the bee can fly to in a straight line.
        var hiveMap = _transform.GetMapCoordinates(hive, xform);
        var angle = _random.NextFloat() * MathF.Tau;
        var direction = new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var distance = Math.Min(LoiterRange, _scout.FreeFlight(hiveMap, direction, LoiterRange, hive) - 0.3f);
        if (distance < 0.8f)
            return;

        var spot = _transform.ToCoordinates(grid, new MapCoordinates(hiveMap.Position + direction * distance, hiveMap.MapId));
        if (Release(hive, xform, comp.PollinatorPrototype, null, null, spot) is { } bee)
            comp.Foragers.Add(bee);
    }

    /// <summary>
    /// A bee that is already out (an angry bee whose time is up) flies home on a return errand: its task becomes the
    /// errand's, it goes into the hive and the hive gets it back (<see cref="BeeErrandHomeEvent"/>).
    /// </summary>
    public void SendHome(EntityUid bee, EntityUid hive)
    {
        var errand = EnsureComp<BeeErrandComponent>(bee);
        errand.Kind = BeeErrandKind.Return;
        errand.Hive = hive;
        errand.Stage = BeeErrandStage.Back;
        if (!TryComp<HTNComponent>(bee, out var htn))
            return;

        htn.RootTask = new HTNCompoundTask { Task = ErrandTask };
        _npc.WakeNPC(bee, htn);
        _htn.Replan(htn);
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
}
