using System.Numerics;

namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// The hive's scout out exploring. It moves tile by tile to free neighbouring tiles, never through walls, and
/// prefers tiles the hive has not explored yet. Every tile it reaches it looks around; when it finds a tray with a
/// living plant it flies to it, sits in the flower for a moment, and goes home the same way, tile by tile. The hive
/// learns the trays when it lands.
/// </summary>
[RegisterComponent]
public sealed partial class BeeScoutFlightComponent : Component
{
    /// <summary>
    /// Tiles per second.
    /// </summary>
    [DataField]
    public float Speed = 2f;

    /// <summary>
    /// Most tiles one flight explores before the scout goes home.
    /// </summary>
    [DataField]
    public int MaxSteps = 30;

    /// <summary>
    /// How far around a tile the scout sees trays, in tiles.
    /// </summary>
    [DataField]
    public float SightRadius = 1.2f;

    /// <summary>
    /// Trays one flight brings home: the one the scout lands on, and others it notices right next to it.
    /// </summary>
    [DataField]
    public int MaxFind = 1;

    /// <summary>
    /// How close other trays must be to the one it lands on to be noticed too, in tiles.
    /// </summary>
    [DataField]
    public float NearbyRadius = 2.5f;

    /// <summary>
    /// How long the scout stays in the flower.
    /// </summary>
    [DataField]
    public TimeSpan FeedTime = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public EntityUid Hive;

    [ViewVariables]
    public BeeScoutPhase Phase;

    /// <summary>
    /// Tiles still to walk, in order.
    /// </summary>
    [ViewVariables]
    public List<Vector2i> Path = new();

    /// <summary>
    /// The current step from one tile centre to the next, in grid coordinates.
    /// </summary>
    [ViewVariables]
    public Vector2 StepFrom;

    [ViewVariables]
    public Vector2 StepTo;

    [ViewVariables]
    public TimeSpan StepStart;

    [ViewVariables]
    public TimeSpan StepEnd;

    /// <summary>
    /// Tiles explored on this flight.
    /// </summary>
    [ViewVariables]
    public int Steps;

    /// <summary>
    /// The tray the scout is flying to or sitting in.
    /// </summary>
    [ViewVariables]
    public EntityUid? Plant;

    /// <summary>
    /// Trays seen on this flight. The hive learns them on landing; a scout caught on the way forgets them.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Found = new();

    [ViewVariables]
    public TimeSpan PhaseEnd;
}

public enum BeeScoutPhase : byte
{
    /// <summary>
    /// Growing out of the hive.
    /// </summary>
    Emerge,

    /// <summary>
    /// Walking tile by tile to unexplored tiles.
    /// </summary>
    Explore,

    /// <summary>
    /// Walking to the tray it found.
    /// </summary>
    ToPlant,

    /// <summary>
    /// Shrinking into the flower, inside it, and growing back out.
    /// </summary>
    DiveIn,
    Feeding,
    DiveOut,

    /// <summary>
    /// Walking home.
    /// </summary>
    Home,

    /// <summary>
    /// Shrinking into the hive; the scout is removed after this.
    /// </summary>
    Land,
}
