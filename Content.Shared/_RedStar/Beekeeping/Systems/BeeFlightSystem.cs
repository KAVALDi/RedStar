using System.Numerics;
using Robust.Shared.Timing;
using Content.Shared._RedStar.Beekeeping.Components;

namespace Content.Shared._RedStar.Beekeeping.Systems;

/// <summary>
/// Owns <see cref="BeeFlightComponent"/>: starts the legs of a flying bee and says where it is on them. The entity is
/// only moved when a leg starts or is settled at its end; in between the client draws the bee where
/// <see cref="GetLocalPosition"/> says.
/// </summary>
public sealed partial class BeeFlightSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// A straight flight from <paramref name="from"/> to <paramref name="to"/> at <paramref name="speed"/> tiles per
    /// second, in the coordinates of the bee's parent. Returns how long it takes.
    /// </summary>
    public TimeSpan StartLine(Entity<BeeFlightComponent> ent, Vector2 from, Vector2 to, float speed)
    {
        var length = TimeSpan.FromSeconds((to - from).Length() / Math.Max(0.1f, speed));
        SetLeg(ent, BeeFlightMotion.Line, from, to, length);
        return length;
    }

    /// <summary>
    /// Circling around <paramref name="center"/>, starting at <paramref name="startAngle"/>.
    /// </summary>
    public void StartCircle(Entity<BeeFlightComponent> ent,
        Vector2 center,
        float radius,
        float turns,
        float startAngle,
        TimeSpan length)
    {
        ent.Comp.Radius = radius;
        ent.Comp.Turns = turns;
        ent.Comp.StartAngle = startAngle;
        SetLeg(ent, BeeFlightMotion.Circle, center, center, length);
    }

    /// <summary>
    /// Staying at <paramref name="at"/>, for a dive, a pause in a flower and the like.
    /// </summary>
    public void Hold(Entity<BeeFlightComponent> ent, Vector2 at, TimeSpan length)
    {
        SetLeg(ent, BeeFlightMotion.Still, at, at, length);
    }

    /// <summary>
    /// Puts the entity where its current leg ends, so what reads its position next (a search for the tray, the next
    /// leg) gets the right one.
    /// </summary>
    public void Settle(Entity<BeeFlightComponent> ent)
    {
        _transform.SetLocalPosition(ent, GetLocalPosition(ent, ent.Comp.End));
    }

    /// <summary>
    /// Where the bee is at <paramref name="time"/> on its current leg, in the coordinates of its parent.
    /// </summary>
    public Vector2 GetLocalPosition(Entity<BeeFlightComponent> ent, TimeSpan time)
    {
        var flight = ent.Comp;
        switch (flight.Motion)
        {
            case BeeFlightMotion.Line:
                var total = (flight.End - flight.Start).TotalSeconds;
                var progress = total > 0 ? (float) Math.Clamp((time - flight.Start).TotalSeconds / total, 0, 1) : 1f;
                return Vector2.Lerp(flight.From, flight.To, progress);

            case BeeFlightMotion.Circle:
                var seconds = (float) Math.Clamp((time - flight.Start).TotalSeconds, 0, (flight.End - flight.Start).TotalSeconds);
                var angle = flight.StartAngle + seconds * flight.Turns * MathF.Tau;
                // A slight wobble of the radius, so the circle does not look drawn with a compass.
                var radius = flight.Radius * (1f + 0.2f * MathF.Sin(angle * 2.5f));
                return flight.From + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

            default:
                return flight.From;
        }
    }

    private void SetLeg(Entity<BeeFlightComponent> ent, BeeFlightMotion motion, Vector2 from, Vector2 to, TimeSpan length)
    {
        var now = _timing.CurTime;
        ent.Comp.Motion = motion;
        ent.Comp.From = from;
        ent.Comp.To = to;
        ent.Comp.Start = now;
        ent.Comp.End = now + length;
        Dirty(ent);
        _transform.SetLocalPosition(ent, from);
    }
}
