using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared._RedStar.Beekeeping.Components;
using Content.Shared._RedStar.Beekeeping.Prototypes;
using Content.Shared._RedStar.Beekeeping.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._RedStar.Beekeeping.UI;

[Serializable, NetSerializable]
public enum ApiaryAnalyzerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ApiaryTrayState : byte
{
    /// <summary>The plant is ripe: the workers visit it.</summary>
    Ripe,
    /// <summary>The plant is still growing.</summary>
    Growing,
    /// <summary>No plant, or the workers cannot reach the tray now.</summary>
    Blind,
}

[Serializable, NetSerializable]
public enum ApiaryNectarUse : byte
{
    /// <summary>Nothing to spend it on.</summary>
    Idle,
    /// <summary>The colony is not full yet but no comb can be built: nectar waits for the next bee.</summary>
    Bees,
    /// <summary>The colony grows and the rest of the nectar becomes combs.</summary>
    BeesAndCombs,
    /// <summary>The colony is full: nectar becomes combs.</summary>
    Combs,
    /// <summary>Every frame is full: nectar piles up until combs are cut.</summary>
    FramesFull,
    /// <summary>No workers at home to build combs.</summary>
    NoWorkers,
    /// <summary>A hive without frames: no room for bees or combs.</summary>
    NoFrames,
}

[Serializable, NetSerializable]
public enum ApiaryScanKind : byte
{
    Hive,
    Queen,
    Scout,
    Pollinator,
}

/// <summary>
/// What the apiary analyzer shows. A hive fills the colony part, a queen or a bee only the traits and its own lines.
/// No timers: admins read those in View Variables.
/// </summary>
[Serializable, NetSerializable]
public sealed class ApiaryAnalyzerState : BoundUserInterfaceState
{
    public NetEntity Target;
    public ApiaryScanKind Kind;

    /// <summary>
    /// The traits of the queen (or of the hive's queen) or of the bee.
    /// </summary>
    public List<ProtoId<BeeTraitPrototype>> Traits = new();

    public BeeTraitTotals Totals = new();

    /// <summary>
    /// A queen is known: the scanned one or the one in the hive.
    /// </summary>
    public bool HasQueen;

    public int UsedSlots;
    public int Instability;
    public BeehiveDiet Diet;

    /// <summary>
    /// A meat hive: pieces of meat that would fall out if it were broken now, the most it gives, the bites on the dead
    /// that add one more piece, the chance of each kind and what the hive holds.
    /// </summary>
    public int MeatDrops;
    public int MeatMax;
    public int MeatBase;
    public int MeatPerDrop;
    public List<ApiaryMeatChance> MeatChances = new();
    public List<ApiaryReagentData> Chemicals = new();
    public int Slots;
    public float Payload;
    public float PayloadCap;

    #region Hive

    public bool Wild;
    public int Bees;
    public int OutBees;
    public int MaxBees;
    public float Nectar;
    public float MaxNectar;
    public ApiaryNectarUse NectarUse;

    /// <summary>
    /// Rough seconds until the queen makes the next bee; negative when it cannot be told (full colony, no queen, no
    /// workers at home, the hive paused).
    /// </summary>
    public bool BroodGoing;

    /// <summary>
    /// Workers flying to the trays and back right now.
    /// </summary>
    public int Foraging;
    public int Combs;
    public int MaxCombs;
    public float Health;
    public float MaxHealth;
    public bool HasEgg;
    public List<ApiaryFrameData> Frames = new();
    public List<ApiaryTrayData> Trays = new();

    #endregion

    #region Bee

    public int Charges;

    /// <summary>
    /// The scout's own hive still stands, so it has somewhere to bring its trays.
    /// </summary>
    public bool HiveKnown;

    public int Memory;
    public int MemoryLimit;

    #endregion
}

[Serializable, NetSerializable]
public sealed class ApiaryFrameData
{
    public NetEntity Frame;
    public EntProtoId? Prototype;
    public int Combs;
    public int MaxCombs;
}

[Serializable, NetSerializable]
public sealed class ApiaryTrayData
{
    public NetEntity Tray;
    public NetEntity? Plant;
    public EntProtoId? PlantPrototype;
    public ApiaryTrayState State;

    /// <summary>
    /// A creature of a meat hive instead of a tray. Nothing that identifies it is sent, no name and no entity: only its
    /// kind, the species for a humanoid (its localized name) or the prototype of an animal, and the plain picture of
    /// that kind.
    /// </summary>
    public bool Creature;
    public string? CreatureName;
    public EntProtoId? CreaturePrototype;
}

[Serializable, NetSerializable]
public sealed partial class ApiaryAnalyzerDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed class ApiaryMeatChance
{
    public ProtoId<BeeMeatPrototype> Meat;
    public float Percent;
}

[Serializable, NetSerializable]
public sealed class ApiaryReagentData
{
    public ProtoId<ReagentPrototype> Reagent;
    public float Amount;
}
