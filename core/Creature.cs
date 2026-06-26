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
    /// Innate personality weights — the first slice of the genotype. Stable for the
    /// creature's lifetime; consumed by <see cref="Brain"/> when scoring actions.
    /// </summary>
    public Drives Drives { get; }

    /// <summary>Dynamic need state (hunger/fatigue/boredom), updated each tick by the Simulator.</summary>
    public CreatureNeeds Needs { get; }

    /// <summary>
    /// The creature's tailored body description — primitive parts at sockets, palette, scale.
    /// Read by the engine's CreatureVisual to assemble + procedurally animate the creature.
    /// Null = no body plan (falls back to the legacy cube visual). The data seed of the future
    /// genotype→phenotype output; sim logic does not depend on it.
    /// </summary>
    public BodyPlan? Body { get; set; }

    /// <summary>
    /// World point the creature is currently attending to (nearest neighbor/food when the
    /// active action is Approach/Flee/Forage), or null. Cosmetic only — drives head/eye
    /// look-at in the visual layer so creatures appear to "think". Set by the Simulator.
    /// </summary>
    public Vector3? FocusPosition { get; internal set; }

    /// <summary>
    /// Steering target set by <see cref="Brain"/> each tick; the <see cref="Movement"/>
    /// layer accelerates toward it. Zero when the creature should stand still.
    /// </summary>
    public Vector3 DesiredVelocity { get; internal set; }

    /// <summary>
    /// Optional Utility-AI brain. When set, the Simulator builds a <see cref="SenseContext"/>
    /// and ticks it each frame to drive <see cref="DesiredVelocity"/>. Null = no brain
    /// (the creature relies purely on its movement mode, preserving older behavior).
    /// </summary>
    public UtilityBrain? Brain { get; set; }

    /// <summary>
    /// Create a creature at the given position with specified traits and
    /// movement strategy. If <paramref name="traits"/> is null, defaults
    /// are used. If <paramref name="drives"/> is null, a neutral temperament is used.
    /// </summary>
    public Creature(Vector3 position, CreatureTraits? traits, IMovementMode movement, Drives? drives = null)
    {
        Position = position;
        Velocity = Vector3.Zero;
        Traits = traits ?? CreatureTraits.Default;
        Movement = movement;
        Drives = drives ?? Drives.Default;
        Needs = new CreatureNeeds();
    }
}
