using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Resolves need dynamics for one creature over one tick: fatigue drains while moving and
/// recovers at rest; boredom is relieved only by Frolic; hunger creeps up steadily. Extracted
/// from the Simulator as a pure function of (entity, delta, config), mirroring
/// <see cref="GrazingSystem"/>. Deterministic — reads entity state only, no RNG.
/// </summary>
public static class NeedSystem
{
    /// <summary>
    /// Advance <paramref name="entity"/>'s needs by <paramref name="delta"/> seconds.
    /// </summary>
    public static void Resolve(double delta, Creature entity, NeedConfig config)
    {
        float dt = (float)delta;
        var n = entity.Needs;

        float maxSpeed = MathF.Max(entity.Traits.MaxSpeed, 1e-3f);
        float speed = MathF.Sqrt(entity.Velocity.X * entity.Velocity.X + entity.Velocity.Z * entity.Velocity.Z);
        float speedFrac = Math.Clamp(speed / maxSpeed, 0f, 1f);

        // Fatigue: recovers only when nearly stopped, accrues with travel speed.
        if (speedFrac < config.FatigueRecoverSpeedThreshold)
            n.Fatigue -= entity.Traits.FatigueRecoverPerSec * dt;
        else
            n.Fatigue += entity.Traits.FatigueGainPerSec * speedFrac * dt;

        // Boredom: relieved only by Frolic (play). Every other action — including
        // active Wander, Flock jostling, Forage — builds it. This makes boredom a
        // genuine "need for play" meter, not a speedometer.
        if (entity.IsFrolicking)
            n.Boredom -= config.BoredomRelievePerSec * dt;
        else
            n.Boredom += config.BoredomGainPerSec * dt;
        n.Hunger += config.HungerGainPerSec * dt;
        n.Clamp();
    }
}
