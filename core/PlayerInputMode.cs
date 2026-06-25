using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Player-driven locomotion: the IO layer writes a horizontal move intent into
/// <see cref="MoveInput"/> each frame; this mode turns it into a
/// <see cref="Creature.DesiredVelocity"/> and then defers all the actual motion math
/// (accelerate → clamp → integrate → wall reflection) to a wrapped
/// <see cref="SteeringLocomotion"/>.
///
/// It is the player's analogue of the <see cref="UtilityBrain"/>: the brain decides the
/// desired velocity for autonomous creatures; here the keyboard does. Living in
/// <c>core</c> lets it set the <c>internal</c> <see cref="Creature.DesiredVelocity"/> — the
/// seam the Godot layer otherwise can't reach.
/// </summary>
public sealed class PlayerInputMode : IMovementMode
{
    private readonly SteeringLocomotion _loco = new();

    /// <summary>
    /// World-space horizontal move intent for this frame: X = right(+)/left(−),
    /// Y = forward(+)/back(−). Magnitude is treated as throttle and clamped to 1, so a
    /// diagonal isn't faster than a cardinal. The caller has already rotated this into world
    /// space (e.g. by the camera yaw). Set by the input layer; defaults to no movement.
    /// </summary>
    public Vector2 MoveInput { get; set; }

    public void Tick(double delta, Creature creature, Arena arena, Random rng)
    {
        var dir = new Vector3(MoveInput.X, 0f, MoveInput.Y);
        float len = dir.Length();
        if (len > 1f)
            dir /= len;

        creature.DesiredVelocity = dir * creature.Traits.MaxSpeed;
        _loco.Tick(delta, creature, arena, rng);
    }
}
