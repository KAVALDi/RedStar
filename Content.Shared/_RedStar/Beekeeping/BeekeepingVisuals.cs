using Robust.Shared.Serialization;

namespace Content.Shared._RedStar.Beekeeping;

/// <summary>
/// Appearance data keys of the beehive.
/// </summary>
[Serializable, NetSerializable]
public enum BeehiveVisuals : byte
{
    /// <summary>
    /// Int: how many frames are inserted. Picks the <c>frames-N</c> base state.
    /// </summary>
    Frames,

    /// <summary>
    /// <see cref="BeeFrameFill"/> of the first inserted frame (counting only inserted frames, not slots).
    /// </summary>
    Frame1,

    /// <summary>
    /// <see cref="BeeFrameFill"/> of the second inserted frame.
    /// </summary>
    Frame2,

    /// <summary>
    /// <see cref="BeeFrameFill"/> of the third inserted frame.
    /// </summary>
    Frame3,

    /// <summary>
    /// <see cref="BeeFrameFill"/> of the fourth inserted frame.
    /// </summary>
    Frame4,
}

/// <summary>
/// Sprite layers of the beehive, mapped in the prototype.
/// </summary>
[Serializable, NetSerializable]
public enum BeehiveVisualLayers : byte
{
    Base,
    Honey1,
    Honey2,
    Honey3,
    Honey4,
}

/// <summary>
/// Appearance data keys of a bee frame.
/// </summary>
[Serializable, NetSerializable]
public enum BeeFrameVisuals : byte
{
    /// <summary>
    /// <see cref="BeeFrameFill"/> of the frame.
    /// </summary>
    Fill,

    /// <summary>
    /// Color: what the combs on the frame look like, from the reagents in them.
    /// </summary>
    Color,
}

/// <summary>
/// Sprite layers of a bee frame.
/// </summary>
[Serializable, NetSerializable]
public enum BeeFrameVisualLayers : byte
{
    Base,

    /// <summary>
    /// Grey overlay tinted with the color of the combs.
    /// </summary>
    Comb,

    /// <summary>
    /// The same shape in wax, shown on a capped frame instead of the combs. Not tinted.
    /// </summary>
    Caps,

    /// <summary>
    /// Grey comb outline tinted with the color of the combs.
    /// </summary>
    Outline,
}

/// <summary>
/// How full a frame is. Also used for the per-frame honey overlay on the hive.
/// </summary>
[Serializable, NetSerializable]
public enum BeeFrameFill : byte
{
    /// <summary>
    /// Empty foundation. The hive overlay is hidden.
    /// </summary>
    None,

    /// <summary>
    /// Some combs, not full yet. The hive overlay shows honey.
    /// </summary>
    Honey,

    /// <summary>
    /// Full and capped. The hive overlay shows wax.
    /// </summary>
    Wax,
}

/// <summary>
/// Appearance data keys of the visual forager bee.
/// </summary>
[Serializable, NetSerializable]
public enum BeeForagerVisuals : byte
{
    /// <summary>
    /// Bool: whether the bee flies to the right, so the side-view sprite is mirrored. Also used by bee and queen
    /// items flying home.
    /// </summary>
    FacingRight,

    /// <summary>
    /// <see cref="BeeFlightSize"/>: the bee dives into a flower or the hive by shrinking, and comes back out.
    /// </summary>
    Size,

    /// <summary>
    /// Bool: a bee or queen item is flying home on its own right now, so it wobbles like a flying bee.
    /// </summary>
    Flying,
}

/// <summary>
/// Size of a flying bee sprite. The client animates the change between full and hidden.
/// </summary>
[Serializable, NetSerializable]
public enum BeeFlightSize : byte
{
    Full,
    Shrinking,
    Hidden,
    Growing,
}

/// <summary>
/// Sprite layers of a flying bee (visual forager, bee item, queen item) that are mirrored when it faces right.
/// </summary>
[Serializable, NetSerializable]
public enum BeeForagerVisualLayers : byte
{
    Body,
    Grey,
    Wings,
    Angry,
}

/// <summary>
/// What a queen or a bee shows of its colony's traits: the colour of its grey mask (the payload, or honey) and the
/// angry eyes of aggressive bees.
/// </summary>
[Serializable, NetSerializable]
public enum BeeTraitVisuals : byte
{
    GreyColor,
    Angry,
}

/// <summary>
/// Bool: a wild hive is at least half full of combs and shows its honey.
/// </summary>
[Serializable, NetSerializable]
public enum WildBeehiveVisuals : byte
{
    Honey,
}

/// <summary>
/// Appearance data keys of a honeycomb.
/// </summary>
[Serializable, NetSerializable]
public enum BeeCombVisuals : byte
{
    /// <summary>
    /// Color: the mixed color of the reagents in the comb that are not honey.
    /// </summary>
    Color,

    /// <summary>
    /// Float, 0 to 1: how much of the comb is not honey. At 0 the layers keep their own honey colors.
    /// </summary>
    Share,
}

/// <summary>
/// Sprite layers of a honeycomb that take the comb's color. The shine layer stays as it is.
/// </summary>
[Serializable, NetSerializable]
public enum BeeCombVisualLayers : byte
{
    Drips,
    Cells,
    Outline,
}
