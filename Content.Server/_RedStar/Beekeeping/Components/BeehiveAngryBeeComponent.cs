using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._RedStar.Beekeeping.Components;

/// <summary>
/// An angry bee out of a hive. It lives for its <c>TimedDespawn</c> lifetime and then flies back into the hive;
/// killed, it is lost to the colony. Its sting carries the payload it took from the queen.
/// Someone in a sealed suit (outer clothing that holds pressure plus a helmet) is not hurt by it.
/// </summary>
[RegisterComponent]
public sealed partial class BeehiveAngryBeeComponent : Component
{
    /// <summary>
    /// The hive the bee came out of.
    /// </summary>
    [ViewVariables]
    public EntityUid? Hive;

    /// <summary>
    /// This is the hive's scout.
    /// </summary>
    [DataField]
    public bool Scout;

    /// <summary>
    /// Whether the bee is still counted outside its hive. Cleared once, by whichever way it ends.
    /// </summary>
    [ViewVariables]
    public bool Counted;

    /// <summary>
    /// What the sting injects. Taken from the queen when the bee flies out.
    /// </summary>
    [ViewVariables]
    public Solution Payload = new();

    /// <summary>
    /// Payload one sting injects.
    /// </summary>
    [DataField]
    public FixedPoint2 PayloadPerSting = 1;

    /// <summary>
    /// Calmed by water or foam right now, with its AI asleep.
    /// </summary>
    [ViewVariables]
    public bool Calmed;
}
