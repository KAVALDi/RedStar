using Content.Shared.Botany.Components;
using Content.Shared.Examine;
using Content.Shared.Whitelist;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Nectar: the automatic scout finds trays near the hive, the hive remembers the trays, and workers make
/// abstract visits to them. A tray does not have to be bolted down: a moved tray is still the same tray. A visit and a find both need a clear straight line from the hive to the tray.
/// Visible forager bees (the Foragers part) only show a visit; they compute nothing.
/// </summary>
public sealed partial class BeeScoutSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IRobustRandom _random = default!;

    [Dependency] private EntityQuery<PlantHolderComponent> _holderQuery;
    [Dependency] private EntityQuery<PlantComponent> _plantQuery;
    [Dependency] private EntityQuery<PlantTrayComponent> _trayQuery;

    private readonly HashSet<Entity<PlantTrayComponent>> _foundTrays = new();


    #region Scouting

    /// <summary>
    /// While the scout is home it flies out every so often (see the ScoutFlight part); the pause between flights
    /// starts when it is back. A scout that is taken out, lost or kept home by bad air is called back at once.
    /// </summary>
    public void UpdateScouting(Entity<BeehiveComponent> hive)
    {
        var comp = hive.Comp;
        var now = _timing.CurTime;

        // A meat colony has no scout: its workers find their own targets.
        if (comp.Diet == BeehiveDiet.Meat)
        {
            RecallScout(comp);
            comp.NextScoutTrip = TimeSpan.Zero;
            return;
        }

        if (SharedBeehiveSystem.ScoutsAtHome(comp) <= 0)
        {
            comp.NextScoutTrip = TimeSpan.Zero;
            RecallScout(comp);
            return;
        }

        if (ScoutFlying(comp))
            return;

        if (comp.NextScoutTrip == TimeSpan.Zero)
        {
            if (comp.ScoutFirstTripNow)
            {
                // The first flight of a new colony goes out within seconds; the normal pauses follow it.
                comp.ScoutFirstTripNow = false;
                comp.NextScoutTrip = now + comp.FirstScoutDelay;
                return;
            }
            else
            {
                comp.NextScoutTrip = now + RandomTrip(comp, _hive.GetHiveTotals(hive).ScoutPause);
                return;
            }
        }

        if (now < comp.NextScoutTrip)
            return;

        // The pause starts over once this flight is done. No spot in sight: try again after a pause.
        comp.NextScoutTrip = LaunchScout(hive) ? TimeSpan.Zero : now + RandomTrip(comp);
    }

    private TimeSpan RandomTrip(BeehiveComponent comp, float pause = 1f)
    {
        var min = comp.ScoutTripMin.TotalSeconds;
        var max = Math.Max(min, comp.ScoutTripMax.TotalSeconds);
        var speed = Math.Max(comp.DebugSpeed, 0.01f);
        return TimeSpan.FromSeconds((min + _random.NextDouble() * (max - min)) / speed * pause);
    }

    /// <summary>
    /// Adds every tray with a living plant around <paramref name="spot"/> that the hive can see, bolted or not.
    /// </summary>
    public void ScoutAround(Entity<BeehiveComponent> hive,
        EntityUid grid,
        MapCoordinates hiveMap,
        MapCoordinates spot,
        float radius)
    {
        var comp = hive.Comp;
        _foundTrays.Clear();
        _lookup.GetEntitiesInRange(spot, radius, _foundTrays);

        var now = _timing.CurTime;
        var sightRange = comp.ScoutRange + comp.ScoutScanRadius + 1f;
        foreach (var tray in _foundTrays)
        {
            var trayXform = Transform(tray);
            if (trayXform.GridUid != grid || _container.IsEntityInContainer(tray))
                continue;

            if (GetLivingPlant(tray.Comp) == null)
                continue;

            if (comp.SourceWhitelist != null && !_whitelist.IsValid(comp.SourceWhitelist, tray))
                continue;

            var trayMap = _transform.GetMapCoordinates(tray, trayXform);
            if ((trayMap.Position - hiveMap.Position).Length() > sightRange ||
                !ClearFlightPath(hiveMap, trayMap, hive, tray))
            {
                continue;
            }

            AddSource(hive, tray, now);
        }
    }

    #endregion

    #region Known places

    /// <summary>
    /// Remembers a tray. A tray already known is refreshed and takes no slot. When the memory is full, trays
    /// that are gone go first, then the oldest inactive one, then the oldest one.
    /// Returns the record, or null if the hive remembers nothing at all.
    /// </summary>
    public BeehiveSource? AddSource(Entity<BeehiveComponent> hive, EntityUid tray, TimeSpan now)
    {
        var sources = hive.Comp.KnownSources;
        foreach (var source in sources)
        {
            if (source.Tray != tray)
                continue;

            // Seen again: an inactive place gets a visit right away; an active one keeps its visit timer.
            if (!source.Active)
                source.NextVisit = now;

            source.LastSeen = now;
            source.Active = true;
            return source;
        }

        if (hive.Comp.MaxKnownSources <= 0)
            return null;

        if (sources.Count >= hive.Comp.MaxKnownSources)
            sources.RemoveAll(s => TerminatingOrDeleted(s.Tray));

        while (sources.Count >= hive.Comp.MaxKnownSources)
        {
            sources.RemoveAt(OldestSource(sources));
        }

        var added = new BeehiveSource(tray, now);
        sources.Add(added);
        return added;
    }

    private static int OldestSource(List<BeehiveSource> sources)
    {
        var oldest = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i].Active)
                continue;

            if (oldest < 0 || sources[i].LastSeen < sources[oldest].LastSeen)
                oldest = i;
        }

        if (oldest >= 0)
            return oldest;

        oldest = 0;
        for (var i = 1; i < sources.Count; i++)
        {
            if (sources[i].LastSeen < sources[oldest].LastSeen)
                oldest = i;
        }

        return oldest;
    }

    /// <summary>
    /// Checks a tray against where the hive and the tray are now. Both may have been unbolted and moved, so both
    /// positions are read every time.
    /// </summary>
    public BeeSourceCheck CheckSource(Entity<BeehiveComponent> hive,
        TransformComponent hiveXform,
        MapCoordinates hiveMap,
        BeehiveSource source,
        out PlantHolderComponent? plant)
    {
        plant = null;

        if (hive.Comp.Diet == BeehiveDiet.Meat)
            return _meat.CheckCreature(hive, hiveXform, hiveMap, source.Tray);

        if (TerminatingOrDeleted(source.Tray) || !_trayQuery.TryComp(source.Tray, out var tray))
            return BeeSourceCheck.Invalid;

        var trayXform = Transform(source.Tray);
        if (hiveXform.GridUid == null ||
            trayXform.GridUid != hiveXform.GridUid ||
            _container.IsEntityInContainer(source.Tray))
        {
            return BeeSourceCheck.Inactive;
        }

        var center = _transform.GetMapCoordinates(source.Tray, trayXform);
        if (center.MapId != hiveMap.MapId ||
            (center.Position - hiveMap.Position).Length() > hive.Comp.VisitRange)
        {
            return BeeSourceCheck.Inactive;
        }

        if (!ClearFlightPath(hiveMap, center, hive, source.Tray))
            return BeeSourceCheck.Inactive;

        if (GetLivingPlant(tray) is not { } plantUid)
            return BeeSourceCheck.Inactive;

        plant = _holderQuery.Comp(plantUid);
        var maturation = _plantQuery.TryComp(plantUid, out var plantComp) ? plantComp.Maturation : 0f;
        return plant.Age >= maturation || plant.ReadyForHarvest ? BeeSourceCheck.Ripe : BeeSourceCheck.Unripe;
    }

    /// <summary>
    /// How a known tray looks to the workers right now, for the analyzer.
    /// </summary>
    public ApiaryTrayState GetTrayState(Entity<BeehiveComponent> hive, BeehiveSource source)
    {
        var xform = Transform(hive);
        var hiveMap = _transform.GetMapCoordinates(hive, xform);
        return CheckSource(hive, xform, hiveMap, source, out _) switch
        {
            BeeSourceCheck.Ripe => ApiaryTrayState.Ripe,
            BeeSourceCheck.Unripe => ApiaryTrayState.Growing,
            _ => ApiaryTrayState.Blind,
        };
    }

    /// <summary>
    /// The living plant growing in a tray, if there is one. The plant is its own entity on top of the tray.
    /// </summary>
    public EntityUid? GetLivingPlant(PlantTrayComponent tray)
    {
        if (tray.PlantEntity is not { } plant ||
            TerminatingOrDeleted(plant) ||
            !_holderQuery.TryComp(plant, out var holder) ||
            holder.Dead)
        {
            return null;
        }

        return plant;
    }

    #endregion

    #region Visits

    /// <summary>
    /// Every worker at home makes one visit per <see cref="BeehiveComponent.VisitInterval"/>. A place takes
    /// at most one visit per growth cycle of its plant, so one ripe plant feeds a few workers at most.
    /// </summary>
    public void UpdateVisits(Entity<BeehiveComponent> hive, float seconds)
    {
        var comp = hive.Comp;
        var interval = (float) comp.VisitInterval.TotalSeconds;
        if (interval > 0f)
            comp.VisitBudget += SharedBeehiveSystem.WorkersAtHome(comp) * seconds / interval;

        if (comp.VisitBudget < 1f)
            return;

        if (comp.Diet == BeehiveDiet.Meat)
        {
            _meat.Hunt(hive);
            comp.VisitBudget = Math.Min(comp.VisitBudget, 1f);
            return;
        }

        var now = _timing.CurTime;
        var xform = Transform(hive);
        var hiveMap = _transform.GetMapCoordinates(hive, xform);

        var i = 0;
        while (comp.VisitBudget >= 1f && i < comp.KnownSources.Count)
        {
            var source = comp.KnownSources[i];
            if (now < source.NextVisit)
            {
                i++;
                continue;
            }

            switch (CheckSource(hive, xform, hiveMap, source, out var plant))
            {
                case BeeSourceCheck.Invalid:
                    comp.KnownSources.RemoveAt(i);
                    continue;
                case BeeSourceCheck.Inactive:
                    source.Active = false;
                    source.NextVisit = now + comp.InactiveRecheck;
                    break;
                case BeeSourceCheck.Unripe:
                    source.Active = true;
                    source.LastSeen = now;
                    source.NextVisit = now + plant!.CycleDelay;
                    break;
                case BeeSourceCheck.Ripe:
                    source.Active = true;
                    source.LastSeen = now;
                    source.NextVisit = now + plant!.CycleDelay;
                    comp.VisitBudget -= 1f;
                    _hive.AddNectar(hive, comp.NectarPerVisit * _hive.GetHiveTotals(hive).NectarPerVisit);
                    // Visits made in the same update are shown over the next while, not as one swarm.
                    comp.PendingForagers.Add((source.Tray,
                        now + TimeSpan.FromSeconds(_random.NextFloat() * (float) comp.ForagerSpread.TotalSeconds)));
                    break;
            }

            i++;
        }

        comp.VisitBudget = Math.Min(comp.VisitBudget, 1f);
    }

    #endregion
}

public enum BeeSourceCheck : byte
{
    /// <summary>
    /// The tray is gone. The record is deleted.
    /// </summary>
    Invalid,

    /// <summary>
    /// No living plant in the tray, the tray is too far, out of sight, inside something or on another grid.
    /// </summary>
    Inactive,

    /// <summary>
    /// A living plant that does not give nectar yet.
    /// </summary>
    Unripe,

    /// <summary>
    /// A ripe plant: a visit brings nectar.
    /// </summary>
    Ripe,
}
