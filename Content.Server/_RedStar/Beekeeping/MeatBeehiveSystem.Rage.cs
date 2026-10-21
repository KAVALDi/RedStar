using Content.Shared._RedStar.Beekeeping;
using Content.Shared.Damage.Systems;
using System.Numerics;
using Content.Shared.Destructible;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// The foragers of a meat colony can be hit. Each one killed costs the colony a bee and makes it furious for a while:
/// the fewer bees are left, the faster they fly, the more are out at once and the likelier they go after whoever did
/// it (a couple of times only, and never with the last few bees left).
/// </summary>
public sealed partial class MeatBeehiveSystem
{
    /// <summary>
    /// A wide swing hits what has a body to hit, and a bee has none: it can only be hit by a click straight on it. When a
    /// swing finds nothing and a meat forager was right in front of the attacker, he is told so.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSwing(Entity<MeleeWeaponComponent> weapon, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count > 0 || args.Direction is not { } direction || direction.LengthSquared() < 0.0001f)
            return;

        var user = args.User;
        var userMap = _transform.GetMapCoordinates(user);
        var forward = direction.Normalized();

        // Only the bees right around the attacker are looked at.
        _swingBees.Clear();
        _lookup.GetEntitiesInRange(userMap, SwingMissRange, _swingBees);
        foreach (var (uid, flight) in _swingBees)
        {
            if (flight.Phase is BeeForagerPhase.Hidden or BeeForagerPhase.VentHidden ||
                !TryComp<BeehiveComponent>(flight.Hive, out var hive) ||
                hive.Diet != BeehiveDiet.Meat)
            {
                continue;
            }

            var offset = _transform.GetWorldPosition(uid) - userMap.Position;
            var distance = offset.Length();
            if (distance > SwingMissRange || distance < 0.01f || Vector2.Dot(offset / distance, forward) < 0.6f)
                continue;

            _popup.PopupEntity(Loc.GetString("beekeeping-bee-swing-miss"), user, user, PopupType.Small);
            return;
        }
    }

    /// <summary>
    /// How far in front of a swing a bee counts as the one it missed, in tiles.
    /// </summary>
    private const float SwingMissRange = 2f;

    private readonly HashSet<Entity<BeeForagerComponent>> _swingBees = new();

    /// <summary>
    /// How furious the colony is, from 0 (calm) to 1: at least <see cref="BeehiveComponent.RageMin"/> once provoked,
    /// growing as bees are lost.
    /// </summary>
    public float RageLevel(Entity<BeehiveComponent> hive)
    {
        var comp = hive.Comp;
        if (comp.Diet != BeehiveDiet.Meat ||
            !TryComp<MeatBeehiveComponent>(hive, out var meatComp) ||
            _timing.CurTime >= meatComp.RageUntil)
        {
            return 0f;
        }

        var left = comp.MaxBees > 0 ? (comp.Bees + comp.OutBees) / (float) comp.MaxBees : 0f;
        return Math.Clamp(Math.Max(meatComp.RageMin, 1f - left), 0f, 1f);
    }

    /// <summary>
    /// Whoever hit a forager is remembered, whether the bee dies or not.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnForagerHurt(Entity<BeeForagerComponent> bee, ref DamageDealtEvent args)
    {
        if (!args.Damage.AnyPositive() ||
            args.Origin is not { } attacker ||
            TerminatingOrDeleted(bee.Comp.Hive) ||
            !TryComp<BeehiveComponent>(bee.Comp.Hive, out var hive) ||
            hive.Diet != BeehiveDiet.Meat)
        {
            return;
        }

        // Someone already marked keeps what is left of the revenge: hitting more bees does not earn more attacks.
        var meatComp = MeatOf(bee.Comp.Hive);
        var now = _timing.CurTime;
        if (meatComp.Attackers.TryGetValue(attacker, out var known) && now < known.Until)
            meatComp.Attackers[attacker] = (now + meatComp.RageDuration, known.Left);
        else
            meatComp.Attackers[attacker] = (now + meatComp.RageDuration, meatComp.RageRevenge);
    }

    [SubscribeLocalEvent]
    private void OnForagerKilled(Entity<BeeForagerComponent> bee, ref DestructionEventArgs args)
    {
        if (TerminatingOrDeleted(bee.Comp.Hive) ||
            !TryComp<BeehiveComponent>(bee.Comp.Hive, out var comp) ||
            comp.Diet != BeehiveDiet.Meat)
        {
            return;
        }

        // A visible forager is one of the hive's own bees: it is not taken out of Bees while it flies, so its death
        // takes one bee from that count, once. Its slot among the foragers out is freed here as well.
        var hive = new Entity<BeehiveComponent>(bee.Comp.Hive, comp);
        _hive.RemoveHomeBee(hive);
        var meatComp = MeatOf(hive);
        meatComp.RageUntil = _timing.CurTime + meatComp.RageDuration;
        comp.Foragers.Remove(bee);
    }

    /// <summary>
    /// How much likelier a creature is to be the next target: it killed a bee lately and the colony remembers.
    /// </summary>
    private float AttackerBias(Entity<BeehiveComponent> hive, EntityUid target, float rage, TimeSpan now)
    {
        var comp = hive.Comp;
        var meatComp = MeatOf(hive);
        // The last few bees do not risk themselves for revenge.
        return rage > 0f &&
               comp.Bees + comp.OutBees > meatComp.RageRevengeMinBees &&
               meatComp.Attackers.TryGetValue(target, out var mark) && now < mark.Until && mark.Left > 0
            ? 1f + (meatComp.RageAttackerBias - 1f) * rage
            : 1f;
    }

    /// <summary>
    /// A bite is sent at a creature the colony wants revenge on: one of its few attacks is used up.
    /// </summary>
    private void UseRevenge(Entity<BeehiveComponent> hive, EntityUid target)
    {
        var meatComp = MeatOf(hive);
        if (meatComp.Attackers.TryGetValue(target, out var mark) && mark.Left > 0)
            meatComp.Attackers[target] = (mark.Until, mark.Left - 1);
    }
}
