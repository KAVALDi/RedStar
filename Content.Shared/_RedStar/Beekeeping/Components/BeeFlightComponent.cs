using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared._RedStar.Beekeeping.Systems;

namespace Content.Shared._RedStar.Beekeeping.Components;

/// <summary>
/// The current leg of a flying bee: a straight line, a circle or a pause, with its start and end time. The server sets a
/// leg only when the bee's plan changes and keeps the entity where the leg starts; the client draws the bee moving along
/// it, so nobody moves the entity every tick. See <see cref="BeeFlightSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(BeeFlightSystem))]
public sealed partial class BeeFlightComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public BeeFlightMotion Motion;

    /// <summary>
    /// Where the leg starts, or the centre of a circle, in the coordinates of the bee's parent.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Vector2 From;

    /// <summary>
    /// Where a straight leg ends, in the coordinates of the bee's parent.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Vector2 To;

    [ViewVariables, AutoNetworkedField, AutoPausedField]
    public TimeSpan Start;

    [ViewVariables, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    /// <summary>
    /// A circle: its radius in tiles, turns per second (negative: clockwise) and the angle it starts at.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float Radius;

    [ViewVariables, AutoNetworkedField]
    public float Turns;

    [ViewVariables, AutoNetworkedField]
    public float StartAngle;
}

[Serializable, NetSerializable]
public enum BeeFlightMotion : byte
{
    /// <summary>
    /// Stays at <see cref="BeeFlightComponent.From"/>: diving, hidden in a flower, growing out of the hive.
    /// </summary>
    Still,

    Line,

    Circle,
}
