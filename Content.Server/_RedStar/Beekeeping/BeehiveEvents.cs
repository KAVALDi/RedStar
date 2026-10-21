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
/// Raised on a visible bee (<see cref="BeeForagerComponent"/>) when a leg of its way ends, before the forager's own
/// plan runs. A swarm or another kind of flyer sets <see cref="Handled"/> and starts its own next leg.
/// </summary>
[ByRefEvent]
public record struct BeeLegEndedEvent(BeeForagerPhase Phase, bool Handled = false);
