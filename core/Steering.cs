using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Steering primitives: pure functions that turn a chosen action + context into a desired
/// XZ velocity. The <see cref="UtilityBrain"/> picks the action; these compute the vector;
/// <see cref="SteeringLocomotion"/> then accelerates the creature toward it. Y is left at
/// zero — gravity and ground clamping are the <see cref="Simulator"/>'s job.
/// </summary>
public static class Steering
{
    /// <summary>Full-speed toward a target.</summary>
    public static Vector3 Seek(Vector3 self, Vector3 target, float maxSpeed)
        => Flatten(target - self) is var d && d.LengthSquared() > 1e-8f
            ? Vector3.Normalize(d) * maxSpeed
            : Vector3.Zero;

    /// <summary>Full-speed directly away from a target.</summary>
    public static Vector3 Flee(Vector3 self, Vector3 target, float maxSpeed)
        => -Seek(self, target, maxSpeed);

    /// <summary>
    /// Seek but decelerate inside <paramref name="slowRadius"/> so the creature settles
    /// next to the target instead of overshooting and orbiting it.
    /// </summary>
    public static Vector3 Arrive(Vector3 self, Vector3 target, float maxSpeed, float slowRadius)
    {
        var d = Flatten(target - self);
        float dist = d.Length();
        if (dist < 1e-4f) return Vector3.Zero;
        float speed = dist < slowRadius ? maxSpeed * (dist / slowRadius) : maxSpeed;
        return d / dist * speed;
    }

    /// <summary>Move along a precomputed (already-normalized) direction at full speed.</summary>
    public static Vector3 Along(Vector3 direction, float maxSpeed)
        => direction.LengthSquared() > 1e-8f ? Vector3.Normalize(Flatten(direction)) * maxSpeed : Vector3.Zero;

    /// <summary>Stand still.</summary>
    public static Vector3 Stop() => Vector3.Zero;

    private static Vector3 Flatten(Vector3 v) => new(v.X, 0f, v.Z);
}
