using Content.Shared.Destructible;
using Content.Server.Construction;
using Content.Server.Stack;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared._RedStar.Beekeeping;
using Robust.Shared.Containers;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// The server side of cutting a frame (the tool check and the progress bar are shared), in two steps on a capped frame: the first cut takes the wax caps off, the
/// second the combs. A frame that is not capped yet gives its combs in one cut and no wax. The frame stays and goes
/// back into the hive empty. Wax used on a frame makes room for one more comb.
/// </summary>
public sealed partial class BeehiveSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    protected override Type[] FrameInteractBefore => new[] { typeof(ConstructionSystem) };

    /// <summary>
    /// One piece of wax adds room for one more comb. A full frame then has room again and goes back to filling.
    /// </summary>
    protected override void UpgradeFrame(Entity<BeeFrameComponent> frame, Entity<StackComponent> wax, EntityUid user)
    {
        if (frame.Comp.MaxCombs >= frame.Comp.MaxCombsLimit)
        {
            _popup.PopupEntity(Loc.GetString("beekeeping-frame-upgrade-max", ("max", frame.Comp.MaxCombs)), frame, user);
            return;
        }

        if (!_stack.TryUse(wax.AsNullable(), 1))
            return;

        frame.Comp.MaxCombs++;
        Dirty(frame);
        UpdateFrameAppearance(frame);
        _popup.PopupEntity(Loc.GetString("beekeeping-frame-upgraded", ("max", frame.Comp.MaxCombs)), frame, user);
        UpdateFrameHive(frame);
    }

    [SubscribeLocalEvent]
    private void OnFrameCut(Entity<BeeFrameComponent> ent, ref BeeFrameCutDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var combs = ent.Comp.Combs;
        if (combs <= 0)
            return;

        // First cut on a capped frame: only the wax caps come off, the honey stays for the second cut.
        if (GetFill(ent.Comp) == BeeFrameFill.Wax)
        {
            ent.Comp.Uncapped = true;
            Dirty(ent);
            UpdateFrameAppearance(ent);
            var waxEnt = SpawnNextToOrDrop(ent.Comp.WaxPrototype, ent);
            if (TryComp<StackComponent>(waxEnt, out var waxStack))
                _stack.SetCount((waxEnt, waxStack), ent.Comp.WaxPerCappedFrame);
            _popup.PopupEntity(Loc.GetString("beekeeping-frame-cut-caps", ("wax", ent.Comp.WaxPerCappedFrame)),
                ent, args.User);
            UpdateFrameHive(ent);
            return;
        }

        ent.Comp.Combs = 0;
        ent.Comp.Uncapped = false;
        Dirty(ent);
        UpdateFrameAppearance(ent);
        DropCombs(ent, combs, ent);

        _popup.PopupEntity(Loc.GetString("beekeeping-frame-cut-combs", ("combs", combs)), ent, args.User);

        UpdateFrameHive(ent);
    }

    /// <summary>
    /// One stack of combs next to <paramref name="near"/>. A stack's solution is one comb's worth, so each comb gets
    /// its share of the payload.
    /// </summary>
    private void DropCombs(Entity<BeeFrameComponent> frame, int combs, EntityUid near)
    {
        var payload = frame.Comp.Payload;
        frame.Comp.Payload = new Solution();
        var comb = SpawnNextToOrDrop(frame.Comp.CombPrototype, near);
        if (TryComp<StackComponent>(comb, out var combStack))
            _stack.SetCount((comb, combStack), combs);
        if (payload.Volume > 0 && Solutions.TryGetSolution(comb, frame.Comp.CombSolution, out var combSolution, out _))
            Solutions.TryAddSolution(combSolution.Value, payload.SplitSolution(payload.Volume / combs));
    }

    /// <summary>
    /// A frame broken or taken apart with a crowbar leaves its combs behind. Deleting it any other way (a map going
    /// away, an admin) gives nothing.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFrameDestroyed(Entity<BeeFrameComponent> ent, ref DestructionEventArgs args)
    {
        var combs = ent.Comp.Combs;
        if (combs <= 0)
            return;

        ent.Comp.Combs = 0;
        DropCombs(ent, combs, ent);
    }

    /// <summary>
    /// A frame that is in a hive changed how full it is: the hive's picture follows.
    /// </summary>
    private void UpdateFrameHive(Entity<BeeFrameComponent> frame)
    {
        if (_container.TryGetContainingContainer((frame.Owner, null, null), out var container) &&
            TryComp<BeehiveComponent>(container.Owner, out var hive))
        {
            UpdateHiveAppearance((container.Owner, hive));
        }
    }
}
