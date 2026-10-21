using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._RedStar.Beekeeping;

/// <summary>
/// What a tool does to a frame. Shared, so the client starts the cut's progress bar at once instead of waiting for
/// the server; the result of the cut and of the wax is the server's.
/// </summary>
public abstract partial class SharedBeehiveSystem
{
    [Dependency] private SharedDoAfterSystem _frameDoAfter = default!;
    [Dependency] private SharedToolSystem _frameTool = default!;

    /// <summary>
    /// Tool quality that cuts the combs off a frame.
    /// </summary>
    private static readonly ProtoId<ToolQualityPrototype> SlicingQuality = "Slicing";

    /// <summary>
    /// Tool quality that dismantles a frame through its construction graph.
    /// </summary>
    private static readonly ProtoId<ToolQualityPrototype> PryingQuality = "Prying";

    /// <summary>
    /// Systems that must see a tool on a frame after this one: a tool that can both slice and pry should cut here,
    /// not dismantle.
    /// </summary>
    protected virtual Type[] FrameInteractBefore => Array.Empty<Type>();

    private void InitializeFrames()
    {
        SubscribeLocalEvent<BeeFrameComponent, InteractUsingEvent>(OnFrameInteractUsing, before: FrameInteractBefore);
    }

    private void OnFrameInteractUsing(Entity<BeeFrameComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp<StackComponent>(args.Used, out var wax) && wax.StackTypeId == ent.Comp.WaxStack)
        {
            args.Handled = true;
            UpgradeFrame(ent, (args.Used, wax), args.User);
            return;
        }

        if (!_frameTool.HasQuality(args.Used, SlicingQuality))
        {
            // The graph spawns the planks at the frame's own position, which in a hand is the holder:
            // dismantle only frames lying on the floor.
            if (_frameTool.HasQuality(args.Used, PryingQuality) && _containers.IsEntityInContainer(ent))
            {
                args.Handled = true;
                _popupShared.PopupEntity(Loc.GetString("beekeeping-frame-pry-in-hand"), ent, args.User);
            }

            return;
        }

        args.Handled = true;

        if (ent.Comp.Combs <= 0)
        {
            _popupShared.PopupEntity(Loc.GetString("beekeeping-frame-cut-empty"), ent, args.User);
            return;
        }

        _frameDoAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.CutDelay,
            new BeeFrameCutDoAfterEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    /// <summary>
    /// One piece of wax adds room for one more comb. Only the server changes the frame.
    /// </summary>
    protected virtual void UpgradeFrame(Entity<BeeFrameComponent> frame, Entity<StackComponent> wax, EntityUid user)
    {
    }
}
