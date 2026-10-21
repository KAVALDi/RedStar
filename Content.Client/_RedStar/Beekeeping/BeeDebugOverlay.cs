using Content.Shared._RedStar.Beekeeping;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._RedStar.Beekeeping;

/// <summary>
/// Draws what a hive knows: its scout range, a line to every known tray coloured by how it checks out now,
/// and the visible foragers heading for their targets.
/// </summary>
public sealed class BeeDebugOverlay(BeeDebugSystem system) : Overlay
{
    private static readonly Color RangeColor = Color.White.WithAlpha(0.25f);
    private static readonly Color HiveColor = Color.Orange;
    private static readonly Color ForagerColor = Color.Cyan;
    private static readonly Color ScoutColor = Color.White;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        foreach (var (net, hive) in system.Hives)
        {
            if (hive.Map != args.MapId || !system.IsShown(net))
                continue;

            handle.DrawCircle(hive.Position, 0.4f, HiveColor, false);
            handle.DrawCircle(hive.Position, hive.ScoutRange, RangeColor, false);

            foreach (var source in hive.Sources)
            {
                var color = SourceColor(source.State);
                handle.DrawLine(hive.Position, source.Position, color.WithAlpha(0.6f));
                handle.DrawCircle(source.Position, 0.35f, color, false);
            }

            if (hive.Scout is { } scout)
            {
                handle.DrawLine(scout.Position, scout.Target, ScoutColor);
                handle.DrawCircle(scout.Target, 0.2f, ScoutColor, false);
                handle.DrawCircle(scout.Position, 0.15f, ScoutColor);
                // What the scout looks at right now, and the trays it has seen on this flight.
                handle.DrawCircle(scout.Position, hive.ScoutScanRadius, ScoutColor.WithAlpha(0.5f), false);
                foreach (var found in hive.ScoutFound)
                {
                    handle.DrawLine(scout.Position, found, ScoutColor.WithAlpha(0.4f));
                    handle.DrawCircle(found, 0.45f, ScoutColor, false);
                }
            }

            foreach (var forager in hive.Foragers)
            {
                handle.DrawLine(forager.Position, forager.Target, ForagerColor);
                handle.DrawCircle(forager.Position, 0.1f, ForagerColor);
            }
        }
    }

    public static Color SourceColor(BeeSourceDebugState state)
    {
        return state switch
        {
            BeeSourceDebugState.Ripe => Color.LimeGreen,
            BeeSourceDebugState.Unripe => Color.Yellow,
            BeeSourceDebugState.Inactive => Color.Red,
            _ => Color.Gray,
        };
    }
}
