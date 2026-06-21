using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Base class for all vivarium entities. Composes an <see cref="IMovementMode"/>
/// for physics and a <see cref="CreatureTraits"/> config bag for tunable stats.
///
/// Concrete creature types (e.g., Blob) extend this class and add
/// creature-specific behavior (wander AI, color, etc.).
/// </summary>
public class Creature
{
    /// <summary>
    /// Current world position. Mutated by movement modes and the simulator.
    /// </summary>
    public Vector3 Position { get; internal set; }

    /// <summary>
    /// Current velocity in arena units per second. Mutated by movement modes.
    /// </summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>
    /// Mutable physical traits (speed, jump, radius, etc.). The reference is
    /// fixed for the creature's lifetime, but the property values within
    /// are mutable.
    /// </summary>
    public CreatureTraits Traits { get; }

    /// <summary>
    /// The movement strategy for this creature. Swappable at runtime
    /// (e.g., WalkMode → FlyMode when CanFly toggles).
    /// </summary>
    public IMovementMode Movement { get; set; }

    /// <summary>
    /// Accumulated happiness, driven by the biome the creature occupies (see
    /// <see cref="Simulator"/> biome effects). Starts at 0; biomes add or remove
    /// happiness per second. The first of an intended set of biome-affected stats.
    /// </summary>
    public float Happiness { get; internal set; }

    /// <summary>
    /// Create a creature at the given position with specified traits and
    /// movement strategy. If <paramref name="traits"/> is null, defaults
    /// are used.
    /// </summary>
    public Creature(Vector3 position, CreatureTraits? traits, IMovementMode movement)
    {
        Position = position;
        Velocity = Vector3.Zero;
        Traits = traits ?? CreatureTraits.Default;
        Movement = movement;
    }
}
