using System.Numerics;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// A visible bee out of its hive: a forager showing one visit, or the scout bringing home what it found. It flies in a
/// straight line from its hive to a tray, circles over the plant, dives into the flower (shrinks), comes back out,
/// flies home and dives into the hive. The visit itself is abstract; the bee only shows it. If the tray or the hive was
/// moved, it looks for it nearby. A bee that cannot find its tray flies home; one that cannot find its hive is lost: it
/// circles where it is, can be caught by hand like a bee taken from the hive, and dies after a while.
/// Each leg of the way is a <see cref="Content.Shared._RedStar.Beekeeping.BeeFlightComponent"/> leg: the server only
/// decides what comes next, the client draws the movement.
/// </summary>
[RegisterComponent]
public sealed partial class BeeForagerComponent : Component
{
    /// <summary>
    /// Flight speed in tiles per second.
    /// </summary>
    [DataField]
    public float Speed = 2f;

    /// <summary>
    /// How long the bee circles over the plant before diving in, at least and at most.
    /// </summary>
    [DataField]
    public TimeSpan HoverTimeMin = TimeSpan.FromSeconds(0.8);

    [DataField]
    public TimeSpan HoverTimeMax = TimeSpan.FromSeconds(2.2);

    /// <summary>
    /// Radius in tiles of the circle over the plant, at least and at most.
    /// </summary>
    [DataField]
    public float HoverRadiusMin = 0.08f;

    [DataField]
    public float HoverRadiusMax = 0.3f;

    /// <summary>
    /// Circles per second over the plant, at least and at most; the direction is random.
    /// </summary>
    [DataField]
    public float HoverTurnsMin = 0.5f;

    [DataField]
    public float HoverTurnsMax = 1.4f;

    /// <summary>
    /// How long the bee stays inside the flower.
    /// </summary>
    [DataField]
    public TimeSpan HiddenTime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How far from where it expected the tray or the hive the bee still finds it, in tiles.
    /// </summary>
    [DataField]
    public float SearchRange = 3f;

    /// <summary>
    /// How long a lost bee circles before it dies.
    /// </summary>
    [DataField]
    public TimeSpan LostTime = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The hive the bee belongs to.
    /// </summary>
    [ViewVariables]
    public EntityUid Hive;

    /// <summary>
    /// The tray it flies to.
    /// </summary>
    [ViewVariables]
    public EntityUid? Tray;

    /// <summary>
    /// Where the bee starts and returns to, in the coordinates of its parent grid.
    /// </summary>
    [ViewVariables]
    public Vector2 Home;

    /// <summary>
    /// Where it is flying to, in the coordinates of the parent grid.
    /// </summary>
    [ViewVariables]
    public Vector2 Target;

    [ViewVariables]
    public BeeForagerPhase Phase;

    /// <summary>
    /// A meat forager reaching its target through the pipes: the vent it dives into near the hive, the vent it comes
    /// out of near the target, and where it is on the way.
    /// </summary>
    [ViewVariables]
    public EntityUid? VentEntry;

    [ViewVariables]
    public EntityUid? VentExit;

    [ViewVariables]
    public BeeVentStage VentStage;

    /// <summary>
    /// Times the bee already went looking for a moved tray or hive on this trip.
    /// </summary>
    [ViewVariables]
    public int Searches;

    /// <summary>
    /// The scout's trays: the hive learns them when it lands. Null for a forager.
    /// </summary>
    [ViewVariables]
    public List<EntityUid>? Found;
}

public enum BeeVentStage : byte
{
    /// <summary>A straight flight, or the route is done.</summary>
    None,
    /// <summary>Flying from the hive to the vent it dives into.</summary>
    ToEntry,
    /// <summary>In the pipes, on the way to the target's vent.</summary>
    PipeOut,
    /// <summary>Out of the target's vent, flying to the target.</summary>
    ToTarget,
    /// <summary>Flying from the target back to that vent.</summary>
    ToExit,
    /// <summary>In the pipes, on the way back.</summary>
    PipeBack,
}

public enum BeeForagerPhase : byte
{
    /// <summary>
    /// Growing out of the hive before it flies off.
    /// </summary>
    Emerge,

    /// <summary>
    /// Flying from the hive to the plant.
    /// </summary>
    Outbound,

    /// <summary>
    /// Circling over the plant.
    /// </summary>
    Hover,

    /// <summary>
    /// Shrinking into the flower.
    /// </summary>
    DiveIn,

    /// <summary>
    /// Inside the flower, not visible.
    /// </summary>
    Hidden,

    /// <summary>
    /// Growing back out of the flower.
    /// </summary>
    DiveOut,

    /// <summary>
    /// Flying from the plant back to the hive.
    /// </summary>
    Return,

    /// <summary>
    /// Shrinking into the hive; the bee is removed after this.
    /// </summary>
    Land,

    /// <summary>
    /// A swarm shrinking into an air vent.
    /// </summary>
    VentIn,

    /// <summary>
    /// A swarm inside the pipes, on its way to the exit vent.
    /// </summary>
    VentHidden,

    /// <summary>
    /// A swarm growing out of the exit vent.
    /// </summary>
    VentOut,

    /// <summary>
    /// Could not find the tray or the hive: circling until caught or dead.
    /// </summary>
    Lost,
}
