using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RedStar.Beekeeping;

/// <summary>
/// Client to server: an admin with the Debug flag turns the bee debug view on or off. While it is on, the server
/// sends <see cref="BeeDebugStateEvent"/> every second.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestBeeDebugEvent(bool enabled) : EntityEventArgs
{
    public readonly bool Enabled = enabled;
}

/// <summary>
/// Server to the admins who asked: what every hive knows and where its bees fly. Positions are world positions.
/// </summary>
[Serializable, NetSerializable]
public sealed class BeeDebugStateEvent(List<BeeHiveDebugData> hives) : EntityEventArgs
{
    public readonly List<BeeHiveDebugData> Hives = hives;
}

[Serializable, NetSerializable]
public sealed class BeeHiveDebugData
{
    public NetEntity Hive;
    public MapId Map;
    public Vector2 Position;

    public int Bees;
    public int OutBees;
    public int MaxBees;
    public float Nectar;
    public float MaxNectar;

    /// <summary>
    /// Health left before the hive breaks, and the full amount.
    /// </summary>
    public float Health;
    public float MaxHealth;

    /// <summary>
    /// Combs in the frames (or inside a wild hive), and how many fit.
    /// </summary>
    public int Combs;
    public int MaxCombs;

    /// <summary>
    /// The queen egg: whether there is one, seconds until it dies, seconds of cooldown left, and seconds the colony
    /// has been full enough for the next one.
    /// </summary>
    public bool HasEgg;
    public float EggLeft;
    public float EggCooldown;
    public float EggFullFor;

    /// <summary>
    /// The queen's traits, slots and payload.
    /// </summary>
    public List<ProtoId<BeeTraitPrototype>> Traits = new();
    public int Slots;
    public float Payload;
    public bool HasQueen;
    public BeehivePauseReason Pause;
    public BeeScoutState ScoutState;
    public float ScoutRange;
    public float VisitRange;

    /// <summary>
    /// Seconds until the next automatic scout trip; negative while the scout is not at home.
    /// </summary>
    public float NextScoutTrip;

    /// <summary>
    /// Seconds until the next forager takes off: a queued one, or the soonest visit due to an active tray.
    /// Negative while no tray is known.
    /// </summary>
    public float NextForagerTrip;

    public List<BeeSourceDebugData> Sources = new();

    /// <summary>
    /// The scout on its flight, if it is in the air: where it is, where it flies and how many trays it saw.
    /// </summary>
    public BeeForagerDebugData? Scout;

    /// <summary>
    /// Where the trays the flying scout has seen so far stand.
    /// </summary>
    public List<Vector2> ScoutFound = new();

    /// <summary>
    /// How far around itself the scout looks.
    /// </summary>
    public float ScoutScanRadius;

    public List<BeeForagerDebugData> Foragers = new();

    /// <summary>
    /// Bees of this hive outside it and how long each has left.
    /// </summary>
    public List<BeeOutsideDebugData> Outside = new();
}

[Serializable, NetSerializable]
public sealed class BeeOutsideDebugData
{
    public BeeOutsideKind Kind;
    public float SecondsLeft;
}

[Serializable, NetSerializable]
public enum BeeOutsideKind : byte
{
    Scout,
    Pollinator,
    Lost,
}

[Serializable, NetSerializable]
public sealed class BeeSourceDebugData
{
    public NetEntity Tray;

    /// <summary>
    /// The plant growing in the tray, if any.
    /// </summary>
    public NetEntity? Plant;

    public Vector2 Position;
    public BeeSourceDebugState State;

    /// <summary>
    /// Seconds until the next worker visit; zero or less means due.
    /// </summary>
    public float NextVisit;
}

[Serializable, NetSerializable]
public sealed class BeeForagerDebugData
{
    public Vector2 Position;
    public Vector2 Target;
}

[Serializable, NetSerializable]
public enum BeeSourceDebugState : byte
{
    /// <summary>
    /// The tray is gone: the hive will forget it.
    /// </summary>
    Invalid,

    /// <summary>
    /// No living plant, too far, out of sight, inside something or on another grid.
    /// </summary>
    Inactive,

    /// <summary>
    /// A living plant that does not give nectar yet.
    /// </summary>
    Unripe,

    /// <summary>
    /// A ripe plant: workers bring nectar from it.
    /// </summary>
    Ripe,
}
