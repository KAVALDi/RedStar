namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Raised (broadcast) when a hive is broken, after <see cref="BeehiveSystem"/> has cleared what the hive knew. The
/// other beekeeping systems add what a broken hive of their kind leaves behind.
/// </summary>
[ByRefEvent]
public readonly record struct BeehiveBrokenEvent(EntityUid Hive);

/// <summary>
/// Raised (broadcast) when a broken wild hive dropped its combs, so a colony that kept something else in them can add it.
/// </summary>
[ByRefEvent]
public readonly record struct WildCombsDroppedEvent(EntityUid Hive, EntityUid Combs, int Count);

/// <summary>
/// Raised on a bee on an errand (<see cref="BeeErrandComponent"/>) when it reaches its target on the way out. The system
/// of that kind of bee (a swarm, a meat colony's bee) does there what the errand is for and says what comes next. Not
/// handled: the bee visits the flower and turns back.
/// </summary>
[ByRefEvent]
public record struct BeeErrandArrivedEvent(EntityUid? Target, bool Handled = false, BeeErrandArrival Result = BeeErrandArrival.Visit);

public enum BeeErrandArrival : byte
{
    /// <summary>
    /// Into the flower for a while, then home.
    /// </summary>
    Visit,

    /// <summary>
    /// Straight home.
    /// </summary>
    TurnBack,

    /// <summary>
    /// On to a new target or spot the handler set; the errand goes on.
    /// </summary>
    KeepGoing,

    /// <summary>
    /// The errand is over and the bee is gone (a swarm that built its hive, bees that scattered).
    /// </summary>
    Gone,
}

/// <summary>
/// Raised (broadcast) when a bee on an errand is gone without coming home: killed, lost in the pipes or never found its
/// way. The colony already lost the bee; this is for what else should follow (a meat colony's rage).
/// </summary>
[ByRefEvent]
public readonly record struct BeeErrandLostEvent(EntityUid Bee, EntityUid Hive, BeeErrandKind Kind);
