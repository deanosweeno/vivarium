using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Movement strategy for a creature. Implementations handle their own physics
/// (gravity, ground clamping, buoyancy, etc.) within Tick.
/// Composed into Creature and swappable at runtime.
/// </summary>
public interface IMovementMode
{
    /// <summary>
    /// Advance the creature's movement by <paramref name="delta"/> seconds.
    /// Reads creature.Traits for configuration (MaxSpeed, JumpHeight, etc.)
    /// and mutates creature.Position / creature.Velocity as needed.
    /// Uses <paramref name="arena"/> for boundary checks and reflections.
    /// Uses <paramref name="rng"/> for randomized behaviors (wander direction, etc.).
    /// </summary>
    void Tick(double delta, Creature creature, Arena arena, Random rng);
}
