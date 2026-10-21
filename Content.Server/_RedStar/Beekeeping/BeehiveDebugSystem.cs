using System.Linq;
using Content.Shared.Administration;
using Content.Shared.Administration.Managers;
using Content.Shared.Botany.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Verbs;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Testing tools for admins with the Debug flag: verbs in the "Debug" category of the hive's context menu.
/// They change the hive directly and skip the normal waits; the verb system logs who used them.
/// </summary>
public sealed partial class BeehiveDebugSystem : EntitySystem
{
    [Dependency] private MeatBeehiveSystem _meat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BeeForagerSystem _foragers = default!;
    [Dependency] private BeeQueenSystem _queen = default!;
    [Dependency] private BeeSwarmSystem _swarm = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityQuery<PlantTrayComponent> _trayQuery = default!;
    [Dependency] private BeeScoutSystem _scout = default!;
    [Dependency] private BeehiveSystem _hive = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedAdminManager _admin = default!;

    private readonly List<Entity<BeeFrameComponent>> _frames = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateDebugView();
    }

    /// <summary>
    /// Speed set by the "faster" debug verb.
    /// </summary>
    private const float DebugFastSpeed = 10f;

    [SubscribeLocalEvent]
    private void OnHiveDebugVerbs(Entity<BeehiveComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!_admin.HasAdminFlag(args.User, AdminFlags.Debug))
            return;

        AddDebugVerb(ref args, "beekeeping-debug-fill-nectar", () =>
        {
            ent.Comp.Nectar = ent.Comp.MaxNectar;
            _hive.UpdateNectarLevel(ent);
        });

        if (ent.Comp.Wild)
        {
            // A wild hive keeps its combs inside, with no frames: it is filled to half or to the brim.
            AddDebugVerb(ref args, "beekeeping-debug-wild-half", () => FillWildCombs(ent, ent.Comp.MaxWildCombs / 2));
            AddDebugVerb(ref args, "beekeeping-debug-wild-full", () => FillWildCombs(ent, ent.Comp.MaxWildCombs));
        }
        else
        {
            AddDebugVerb(ref args, "beekeeping-debug-frames-honey", () => FillFrames(ent, false));
            AddDebugVerb(ref args, "beekeeping-debug-frames-wax", () => FillFrames(ent, true));
        }

        AddDebugVerb(ref args, "beekeeping-debug-fill-bees", () =>
        {
            var bees = Math.Max(ent.Comp.Bees, ent.Comp.MaxBees - ent.Comp.OutBees);
            if (bees == ent.Comp.Bees)
                return;

            ent.Comp.Bees = bees;
            if (ent.Comp.ScoutState == BeeScoutState.Lost)
                ent.Comp.ScoutState = BeeScoutState.Home;
            Dirty(ent);
            _hive.UpdateAmbience(ent);
        });

        AddDebugVerb(ref args, "beekeeping-debug-restore-scout", () =>
        {
            if (ent.Comp.ScoutState != BeeScoutState.Lost || ent.Comp.Bees <= 0)
                return;

            ent.Comp.ScoutState = BeeScoutState.Home;
            Dirty(ent);
        });

        AddDebugVerb(ref args, "beekeeping-debug-scout-now", () => ScoutWholeRange(ent));

        // Plants or meat, to try the meat colony without waiting for a swarm. Sources the old diet found are dropped.
        AddDebugVerb(ref args, ent.Comp.Diet == BeehiveDiet.Meat ? "beekeeping-debug-diet-to-plants" : "beekeeping-debug-diet-to-meat", () =>
        {
            ent.Comp.Diet = ent.Comp.Diet == BeehiveDiet.Meat ? BeehiveDiet.Plants : BeehiveDiet.Meat;
            if (ent.Comp.Diet == BeehiveDiet.Meat)
                _meat.MeatOf(ent);
            else
                RemComp<MeatBeehiveComponent>(ent);

            ent.Comp.KnownSources.Clear();
            Dirty(ent);
            _hive.UpdateHiveAppearance(ent);
        });

        // Lets the swarm out right now: a full colony, then the swarm itself, with no waiting for the egg or its
        // cooldown. If it cannot leave, the admin is told why.
        var swarmUser = args.User;
        AddDebugVerb(ref args, "beekeeping-debug-swarm", () =>
        {
            var comp = ent.Comp;
            if (_hive.GetQueen(ent) == null)
            {
                _popup.PopupEntity(Loc.GetString("beekeeping-debug-swarm-no-queen"), ent, swarmUser);
                return;
            }

            comp.Bees = Math.Max(Math.Max(comp.Bees, comp.SwarmMinBees), comp.MaxBees - comp.OutBees);
            if (comp.ScoutState == BeeScoutState.Lost)
                comp.ScoutState = BeeScoutState.Home;
            comp.Nectar = comp.MaxNectar;
            _hive.UpdateNectarLevel(ent);
            _hive.UpdateAmbience(ent);

            if (_swarm.TrySwarm(ent))
            {
                _queen.EndEgg(ent);
                comp.EggCooldownUntil = _timing.CurTime;
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("beekeeping-debug-swarm-failed"), ent, swarmUser);
            }

            Dirty(ent);
        });

        // A forager flies to a random known tray right away; if it cannot, the admin is told why.
        var debugUser = args.User;
        AddDebugVerb(ref args, "beekeeping-debug-forager-launch", () =>
        {
            var comp = ent.Comp;
            var trays = comp.KnownSources.Where(s => !TerminatingOrDeleted(s.Tray)).Select(s => s.Tray).ToList();
            comp.Foragers.RemoveAll(f => TerminatingOrDeleted(f));

            string reason;
            if (trays.Count == 0)
                reason = "beekeeping-debug-forager-no-trays";
            else if (comp.Foragers.Count >= comp.MaxForagers)
                reason = "beekeeping-debug-forager-too-many";
            else if (SharedBeehiveSystem.WorkersAtHome(comp) <= 0)
                reason = "beekeeping-debug-forager-no-workers";
            else
            {
                var xform = Transform(ent);
                reason = _foragers.TrySpawnForager(ent, xform, _transform.GetMapCoordinates(ent, xform), _random.Pick(trays))
                    ? "beekeeping-debug-forager-sent"
                    : "beekeeping-debug-forager-failed";
            }

            _popup.PopupEntity(Loc.GetString(reason, ("max", comp.MaxForagers)), ent, debugUser);
        });

        // The scout takes off on a real flight right away instead of waiting for her next trip.
        AddDebugVerb(ref args, "beekeeping-debug-scout-launch", () =>
        {
            if (SharedBeehiveSystem.ScoutsAtHome(ent.Comp) <= 0 || _scout.ScoutFlying(ent.Comp))
                return;

            if (_scout.LaunchScout(ent))
                ent.Comp.NextScoutTrip = TimeSpan.Zero;
        });

        if (!ent.Comp.HasEgg)
        {
            AddDebugVerb(ref args, "beekeeping-debug-egg", () =>
            {
                ent.Comp.HasEgg = true;
                ent.Comp.EggDiesAt = _timing.CurTime + ent.Comp.EggLifetime;
                Dirty(ent);
            });
        }
        else
        {
            // The egg dies right now, as if nobody collected it: the swarm leaves if it can.
            AddDebugVerb(ref args, "beekeeping-debug-egg-die", () => ent.Comp.EggDiesAt = _timing.CurTime);
        }

        var fast = ent.Comp.DebugSpeed > 1f;
        AddDebugVerb(ref args, fast ? "beekeeping-debug-speed-normal" : "beekeeping-debug-speed-fast", () =>
        {
            var old = Math.Max(ent.Comp.DebugSpeed, 0.01f);
            ent.Comp.DebugSpeed = fast ? 1f : DebugFastSpeed;
            // Running egg timers follow the new speed.
            var factor = old / ent.Comp.DebugSpeed;
            var now = _timing.CurTime;
            if (ent.Comp.HasEgg)
                ent.Comp.EggDiesAt = now + (ent.Comp.EggDiesAt - now) * factor;
            if (ent.Comp.EggCooldownUntil > now)
                ent.Comp.EggCooldownUntil = now + (ent.Comp.EggCooldownUntil - now) * factor;
            Dirty(ent);
            ent.Comp.NextScoutTrip = TimeSpan.Zero;
        });
    }

    /// <summary>
    /// A wild hive's combs up to <paramref name="combs"/>; one that has more already keeps them.
    /// </summary>
    private void FillWildCombs(Entity<BeehiveComponent> hive, int combs)
    {
        hive.Comp.WildCombs = Math.Max(hive.Comp.WildCombs, Math.Min(combs, hive.Comp.MaxWildCombs));
        Dirty(hive);
        _hive.UpdateHiveAppearance(hive);
    }

    /// <summary>
    /// Fills the inserted frames. To honey: every frame becomes "filling" (one comb short of full); a frame that is
    /// already further along is left alone. To wax: every frame is full and capped.
    /// </summary>
    private void FillFrames(Entity<BeehiveComponent> hive, bool capped)
    {
        _hive.GetFrames(hive, _frames);
        foreach (var frame in _frames)
        {
            var target = capped ? frame.Comp.MaxCombs : frame.Comp.MaxCombs - 1;
            if (target <= frame.Comp.Combs)
                continue;

            frame.Comp.Combs = target;
            Dirty(frame);
            _hive.UpdateFrameAppearance(frame);
        }

        _hive.UpdateHiveAppearance(hive);
    }

    private void AddDebugVerb(ref GetVerbsEvent<Verb> args, string text, Action act)
    {
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(text),
            Category = VerbCategory.Debug,
            Impact = LogImpact.Low,
            Act = act,
        });
    }

    /// <summary>
    /// One scouting pass over the whole area the scout can cover, instead of a random spot.
    /// </summary>
    private void ScoutWholeRange(Entity<BeehiveComponent> hive)
    {
        // A meat hive has no scout: send a forager at a target now, whatever the workers' pace.
        if (hive.Comp.Diet == BeehiveDiet.Meat)
        {
            hive.Comp.VisitBudget = Math.Max(1f, hive.Comp.VisitBudget);
            _meat.Hunt(hive);
            return;
        }

        var xform = Transform(hive);
        if (xform.GridUid is not { } grid)
            return;

        var hiveMap = _transform.GetMapCoordinates(hive, xform);
        _scout.ScoutAround(hive, grid, hiveMap, hiveMap, hive.Comp.ScoutRange + hive.Comp.ScoutScanRadius);
    }
}
