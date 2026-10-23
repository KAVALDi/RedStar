using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage.Systems;
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
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Caught by hand, hurt, killed or lost on the way: what the colony makes of it.
/// </summary>
public sealed partial class BeeErrandSystem
{
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
    /// Someone hurt a forager or the scout: the colony may send angry bees after them (the angry hive decides).
    /// </summary>
    [SubscribeLocalEvent]
    private void OnDamaged(Entity<BeeErrandComponent> ent, ref DamageDealtEvent args)
    {
        if (ent.Comp.Kind is not (BeeErrandKind.Forage or BeeErrandKind.Scout) ||
            args.Origin is not { } attacker ||
            !args.Damage.AnyPositive() ||
            TerminatingOrDeleted(ent.Comp.Hive))
        {
            return;
        }

        var ev = new BeeErrandAttackedEvent(ent, ent.Comp.Hive, attacker);
        RaiseLocalEvent(ref ev);
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
}
