using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Locomotion executor: steers a creature toward its <see cref="Creature.DesiredVelocity"/>
/// (set by the <see cref="UtilityBrain"/>) using <see cref="CreatureTraits"/> acceleration
/// and max speed, then integrates position and bounces off arena walls.
///
/// This is the "how to move" layer — purely terrain traversal. It knows nothing about
/// *why* (that's the brain), which keeps the seam clean for future FlyMode/SwimMode.
/// Y velocity is preserved untouched — gravity and ground clamping stay in the Simulator.
///
/// Sprint detection is implicit: when <see cref="Creature.DesiredVelocity"/> exceeds
/// <see cref="CreatureTraits.MaxSpeed"/>, the locomotion switches to
/// <see cref="CreatureTraits.SprintSpeed"/> / <see cref="CreatureTraits.SprintAcceleration"/>.
/// </summary>
public sealed class SteeringLocomotion : IMovementMode
{
    public void Tick(double delta, Creature creature, Arena arena, Random rng)
    {
        float dt = (float)delta;
        var vel = creature.Velocity;
        var horiz = new Vector3(vel.X, 0f, vel.Z);
        var desired = new Vector3(creature.DesiredVelocity.X, 0f, creature.DesiredVelocity.Z);

        // Sprint when DesiredVelocity exceeds normal MaxSpeed — the brain encoded sprint intent
        // into the vector magnitude, so the locomotion layer needs no knowledge of *why*.
        float maxSpeed = creature.Traits.MaxSpeed;
        float accel = creature.Traits.Acceleration;
        if (desired.Length() > maxSpeed + 1e-6f)
        {
            maxSpeed = creature.Traits.SprintSpeed;
            accel = creature.Traits.SprintAcceleration;
        }

        // Accelerate toward the desired velocity (capped per-tick by Acceleration).
        var diff = desired - horiz;
        float maxDelta = accel * dt;
        if (diff.Length() <= maxDelta || maxDelta <= 0f)
            horiz = desired;
        else
            horiz += Vector3.Normalize(diff) * maxDelta;

        // Clamp to max speed.
        if (horiz.Length() > maxSpeed && maxSpeed > 0f)
            horiz = Vector3.Normalize(horiz) * maxSpeed;

        creature.Velocity = new Vector3(horiz.X, vel.Y, horiz.Z);
        creature.Position += creature.Velocity * dt;

        // XZ wall reflection (Y handled by Simulator gravity/ground).
        var pos = creature.Position;
        var v = creature.Velocity;
        arena.ReflectXZ(ref pos, ref v, creature.Traits.Radius);
        creature.Position = pos;
        creature.Velocity = v;
    }
}
