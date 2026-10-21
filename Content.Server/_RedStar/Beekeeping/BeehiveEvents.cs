namespace Content.Server._RedStar.Beekeeping;

/// <summary>
/// Raised (broadcast) when a hive is broken, after <see cref="BeehiveSystem"/> has cleared what the hive knew. The
/// other beekeeping systems add what a broken hive of their kind leaves behind.
/// </summary>
[ByRefEvent]
public readonly record struct BeehiveBrokenEvent(EntityUid Hive);
