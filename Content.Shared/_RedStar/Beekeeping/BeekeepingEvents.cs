using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._RedStar.Beekeeping;

/// <summary>
/// Cutting the combs off a frame with a sharp tool. Raised on the frame.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BeeFrameCutDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
/// Taking a bee out of a hive in the chosen role. Raised on the hive.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BeeTakeDoAfterEvent : DoAfterEvent
{
    [DataField]
    public BeeRole Role;

    private BeeTakeDoAfterEvent()
    {
    }

    public BeeTakeDoAfterEvent(BeeRole role)
    {
        Role = role;
    }

    public override DoAfterEvent Clone() => this;
}

/// <summary>
/// Taking the queen out of a hive. Raised on the hive.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BeeTakeQueenDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Applying a bee in hand to a tray: a scout remembers it, a pollinator pollinates the plant. Raised on the bee.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BeeApplyDoAfterEvent : SimpleDoAfterEvent
{
}

/// <summary>
/// Collecting royal jelly from the queen egg into the container in hand.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class BeeJellyDoAfterEvent : SimpleDoAfterEvent;
