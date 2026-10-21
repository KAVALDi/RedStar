using System.Linq;
using System.Numerics;
using Content.Shared._RedStar.Beekeeping;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._RedStar.Beekeeping;

/// <summary>
/// Draws flying bees where they are on their current leg (<see cref="BeeFlightComponent"/>): the entity stays where the
/// server put it, and the sprite is offset to the point of the leg, so the server never moves a decorative bee every
/// tick. On top of that a buzzing bee (<see cref="BeeBuzzVisualsComponent"/>) wobbles along a loop that never quite
/// repeats: two waves per axis with different speeds. Each bee gets its own phase from its id, so a swarm does not move
/// in step. A swarm drawn as one sprite wobbles each of its bees (a group of layers) on its own, around where the
/// prototype put it.
/// </summary>
public sealed partial class BeeFlightVisualizerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private BeeFlightSystem _flight = default!;
    [Dependency] private EntityQuery<BeeFlightComponent> _flightQuery;
    [Dependency] private EntityQuery<BeeBuzzVisualsComponent> _buzzQuery;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var time = (float) _timing.RealTime.TotalSeconds;
        var buzzing = EntityQueryEnumerator<BeeBuzzVisualsComponent, SpriteComponent>();
        while (buzzing.MoveNext(out var uid, out var buzz, out var sprite))
        {
            Buzz((uid, buzz, sprite), FlightOffset((uid, sprite)), time);
        }

        // Flying bees that do not buzz only follow their leg.
        var flying = EntityQueryEnumerator<BeeFlightComponent, SpriteComponent>();
        while (flying.MoveNext(out var uid, out _, out var sprite))
        {
            if (!_buzzQuery.HasComp(uid))
                _sprite.SetOffset((uid, sprite), FlightOffset((uid, sprite)));
        }
    }

    /// <summary>
    /// How far the sprite has to be moved from the entity to show the bee where its leg says, in the sprite's own axes.
    /// </summary>
    private Vector2 FlightOffset(Entity<SpriteComponent> ent)
    {
        if (!_flightQuery.TryComp(ent, out var flight))
            return Vector2.Zero;

        var xform = Transform(ent);
        var local = _flight.GetLocalPosition((ent, flight), _timing.CurTime) - xform.LocalPosition;
        var world = _transform.GetWorldRotation(xform.ParentUid).RotateVec(local);

        // A noRot sprite is drawn turned against the eye, any other one turned with the entity.
        return ent.Comp.NoRotation
            ? _eye.CurrentEye.Rotation.RotateVec(world)
            : (-_transform.GetWorldRotation(xform)).RotateVec(world);
    }

    private void Buzz(Entity<BeeBuzzVisualsComponent, SpriteComponent> ent, Vector2 flight, float time)
    {
        var (uid, buzz, sprite) = ent;
        var phase = uid.Id * 1.618f;
        var t = time * buzz.Speed * MathF.Tau;
        // A bee diving into a flower or the hive shrinks; its wobble shrinks with it, so it ends where it lands.
        var amplitude = buzz.Amplitude * Math.Min(1f, Math.Abs(sprite.Scale.X));
        if (buzz.WhileFlying &&
            !(_appearance.TryGetData<bool>(uid, BeeForagerVisuals.Flying, out var flying) && flying))
        {
            amplitude = 0f;
        }

        if (buzz.LayerGroup <= 0)
        {
            _sprite.SetOffset((uid, sprite), flight + Wobble(t, phase) * amplitude);
            return;
        }

        _sprite.SetOffset((uid, sprite), flight);

        // The layers' own offsets are where each bee sits in the swarm; read them once, before any wobble.
        buzz.BaseOffsets ??= sprite.LayerData.Select(layer => layer.Offset).ToList();

        // The bees are side views facing left: the whole cloud is mirrored while it flies to the right.
        var right = _appearance.TryGetData<bool>(uid, BeeForagerVisuals.FacingRight, out var facing) && facing;
        var mirror = right ? -1f : 1f;
        for (var i = 0; i < buzz.BaseOffsets.Count; i++)
        {
            var bee = i / buzz.LayerGroup;
            var offset = Wobble(t, phase + bee * 2.39f) * amplitude;
            var baseOffset = buzz.BaseOffsets[i];
            _sprite.LayerSetOffset((uid, sprite), i, new Vector2(baseOffset.X * mirror, baseOffset.Y) + offset);
            _sprite.LayerSetScale((uid, sprite), i, new Vector2(mirror, 1f));
        }
    }

    private static Vector2 Wobble(float t, float phase)
    {
        var x = MathF.Sin(t + phase) * 0.7f + MathF.Sin(t * 2.3f + phase * 1.7f) * 0.3f;
        var y = MathF.Sin(t * 1.3f + phase * 0.6f) * 0.7f + MathF.Cos(t * 2.9f + phase) * 0.3f;
        return new Vector2(x, y);
    }
}
