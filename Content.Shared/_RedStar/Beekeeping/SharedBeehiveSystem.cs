using Robust.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.Timing;

namespace Content.Shared._RedStar.Beekeeping;

/// <summary>
/// Shared part of beekeeping: reading the hive slots and the examine text. The examine text uses only
/// networked fields and the slot contents, so the client shows the same as the server.
/// All state changes happen on the server.
/// </summary>
public abstract partial class SharedBeehiveSystem : EntitySystem
{
    [Dependency] protected ItemSlotsSystem ItemSlots = default!;
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeFrames();
    }

    #region Helpers

    /// <summary>
    /// The queen in the hive, if there is one.
    /// </summary>
    public EntityUid? GetQueen(Entity<BeehiveComponent> hive)
    {
        var item = ItemSlots.GetItemOrNull(hive.Owner, hive.Comp.QueenSlot);
        return item != null && HasComp<BeeQueenComponent>(item) ? item : null;
    }

    /// <summary>
    /// Inserted frames in slot order. The index in this list is the frame number on the sprite.
    /// </summary>
    public void GetFrames(Entity<BeehiveComponent> hive, List<Entity<BeeFrameComponent>> frames)
    {
        frames.Clear();
        foreach (var slot in hive.Comp.FrameSlots)
        {
            if (ItemSlots.GetItemOrNull(hive.Owner, slot) is { } item && TryComp<BeeFrameComponent>(item, out var frame))
                frames.Add((item, frame));
        }
    }

    public static BeeFrameFill GetFill(BeeFrameComponent frame)
    {
        if (frame.Combs <= 0)
            return BeeFrameFill.None;

        return frame.Combs >= frame.MaxCombs && !frame.Uncapped ? BeeFrameFill.Wax : BeeFrameFill.Honey;
    }

    /// <summary>
    /// Bees at home that scout on their own. Zero while the only scout is outside or lost.
    /// </summary>
    public static int ScoutsAtHome(BeehiveComponent hive)
    {
        return hive.ScoutState == BeeScoutState.Home ? Math.Min(Math.Max(hive.Scouts, 0), Math.Max(hive.Bees, 0)) : 0;
    }

    /// <summary>
    /// Bees at home that carry nectar and make combs.
    /// </summary>
    public static int WorkersAtHome(BeehiveComponent hive)
    {
        return Math.Max(0, hive.Bees - ScoutsAtHome(hive));
    }

    #endregion

    #region Examine

    [SubscribeLocalEvent]
    private void OnHiveExamined(Entity<BeehiveComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        var frames = 0;
        foreach (var slot in comp.FrameSlots)
        {
            if (ItemSlots.GetItemOrNull(ent.Owner, slot) != null)
                frames++;
        }

        var hasQueen = GetQueen(ent) != null;

        using (args.PushGroup(nameof(BeehiveComponent)))
        {
            args.PushMarkup(Loc.GetString(hasQueen
                ? "beekeeping-hive-examine-queen"
                : "beekeeping-hive-examine-no-queen"));
            if (!comp.Wild)
            {
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-frames",
                    ("count", frames), ("max", comp.FrameSlots.Count)));
            }

            if (comp.Bees > 0 || comp.OutBees > 0)
            {
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-bees",
                    ("bees", comp.Bees), ("max", comp.MaxBees), ("outside", comp.OutBees)));
            }

            args.PushMarkup(Loc.GetString("beekeeping-hive-examine-nectar",
                ("level", comp.NectarLevel.ToString())));

            if (comp.Wild && comp.Diet == BeehiveDiet.Meat)
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-meat"));

            if (comp.Wild && comp.SwarmModifier is { } modifierId && Proto.TryIndex(modifierId, out var modifier))
            {
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-swarm-modifier",
                    ("color", modifier.Color.ToHex()),
                    ("name", Loc.GetString(modifier.Name)),
                    ("desc", Loc.GetString(modifier.Description))));
            }

            switch (comp.Crowding)
            {
                case BeehiveCrowding.Cramped:
                    args.PushMarkup(Loc.GetString("beekeeping-hive-examine-cramped"));
                    break;
                case BeehiveCrowding.Leaving:
                    args.PushMarkup(Loc.GetString("beekeeping-hive-examine-leaving"));
                    break;
            }

            if (!hasQueen && comp.Bees > 0)
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-dying"));

            if (comp.HasEgg)
            {
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-egg"));
                if (comp.EggDiesAt - Timing.CurTime <= comp.EggDyingWarning)
                    args.PushMarkup(Loc.GetString("beekeeping-hive-examine-egg-dying"));
            }

            if (comp.Bees > 0 || comp.OutBees > 0)
                PushScoutLine(comp, ref args);

            if (comp.Pause != BeehivePauseReason.None)
            {
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-paused",
                    ("reason", comp.Pause.ToString())));
            }
        }
    }

    private void PushScoutLine(BeehiveComponent comp, ref ExaminedEvent args)
    {
        switch (comp.ScoutState)
        {
            case BeeScoutState.Outside:
                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-scout-outside"));
                break;
            case BeeScoutState.Lost:
                if (comp.Bees < comp.ScoutReplaceMinBees)
                {
                    args.PushMarkup(Loc.GetString("beekeeping-hive-examine-scout-lost-grow"));
                    break;
                }

                var left = comp.ScoutReplaceAt - Timing.CurTime;
                if (left < TimeSpan.Zero)
                    left = TimeSpan.Zero;

                args.PushMarkup(Loc.GetString("beekeeping-hive-examine-scout-lost",
                    ("minutes", (int) left.TotalMinutes), ("seconds", left.Seconds)));
                break;
        }
    }

    [SubscribeLocalEvent]
    private void OnFrameExamined(Entity<BeeFrameComponent> ent, ref ExaminedEvent args)
    {
        var fill = GetFill(ent.Comp);
        args.PushMarkup(Loc.GetString("beekeeping-frame-examine",
            ("state", fill.ToString()), ("combs", ent.Comp.Combs), ("max", ent.Comp.MaxCombs)));
    }

    [SubscribeLocalEvent]
    private void OnBeeExamined(Entity<BeeItemComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        using (args.PushGroup(nameof(BeeItemComponent)))
        {
            switch (comp.Role)
            {
                case BeeRole.Scout:
                    args.PushMarkup(Loc.GetString("beekeeping-bee-examine-scout",
                        ("count", comp.MemoryCount), ("max", comp.MemoryLimit)));
                    break;
                case BeeRole.Pollinator:
                    args.PushMarkup(Loc.GetString("beekeeping-bee-examine-pollinator",
                        ("charges", comp.Charges)));
                    break;
            }

            if (comp.Traits.Count > 0)
                args.PushMarkup(Loc.GetString("beekeeping-bee-examine-traits", ("traits", TraitList(comp.Traits))));

            if (comp.ExpiresAt > TimeSpan.Zero && comp.ExpiresAt - Timing.CurTime <= comp.DyingWarning)
                args.PushMarkup(Loc.GetString("beekeeping-bee-examine-dying"));
        }
    }

    #endregion
}
