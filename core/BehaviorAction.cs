namespace Vivarium.Core;

/// <summary>
/// How a chosen action turns into a desired velocity. The brain owns intent (this kind);
/// the locomotion layer (<see cref="SteeringLocomotion"/>) owns how to move. See <see cref="Steering"/>.
/// </summary>
public enum SteeringKind
{
    /// <summary>Roam: periodic random direction at a relaxed speed.</summary>
    Wander,
    /// <summary>Arrive at the nearest neighbor (decelerate as it closes).</summary>
    Approach,
    /// <summary>Move directly away from the nearest neighbor.</summary>
    Flee,
    /// <summary>Stop and recover.</summary>
    Rest,
    /// <summary>Seek and graze the nearest food (search by wandering when none is in range).</summary>
    Forage,
    /// <summary>Cohere toward the herd centroid (search by wandering when alone).</summary>
    Flock,
    /// <summary>Smoothly approach the nearest kin flock anchor, searching by wandering
    /// when no kin flock is in range. Triggered after SeparationTime passes threshold.</summary>
    SeekFlock,
    /// <summary>Energetic, darty play driven by high Boredom. Chooses a flavor from senses each
    /// tick: gambol around a near neighbor (play-chase), frolic near the flock anchor, or solo
    /// zig-zag zoomies. Reads as a hoppy "pronk" in the visual; relieves Boredom as it moves.</summary>
    Frolic,
}

/// <summary>
/// How an action interacts with nearby food during <see cref="Simulator.ResolveGrazing"/>.
/// The action declares its own grazing policy; the Simulator respects it without coupling
/// to specific <see cref="SteeringKind"/> values.
/// </summary>
public enum GrazingMode
{
    /// <summary>Never graze (default for Rest, Flee, Frolic, Approach, SeekFlock).</summary>
    None,
    /// <summary>Graze nearby food unconditionally (Forage).</summary>
    Always,
    /// <summary>Graze when Hunger ≥ <see cref="CreatureTraits.GrazeHungerThreshold"/> (Wander, Flock).</summary>
    WhenHungry,
}

/// <summary>
/// A candidate behavior the Utility AI can choose. Pure data: a steering kind, a base
/// weight, and a list of <see cref="Consideration"/>s multiplied to a [0,1] score. An
/// action may be flagged emergency-capable so a high score can interrupt a committed
/// action immediately (the defeasible override in <see cref="UtilityBrain"/>).
/// </summary>
public sealed class BehaviorAction
{
    /// <summary>Stable name (for debug overlay and tests).</summary>
    public string Name { get; init; } = "";

    public SteeringKind Steering { get; init; }

    /// <summary>Multiplied into the final score — a floor/ceiling for this action's salience.</summary>
    public float BaseWeight { get; init; } = 1f;

    public IReadOnlyList<Consideration> Considerations { get; init; } = [];

    /// <summary>When true, a score ≥ <see cref="EmergencyThreshold"/> bypasses stickiness.</summary>
    public bool EmergencyCapable { get; init; }

    /// <summary>Score above which this action interrupts a committed action (if emergency-capable).</summary>
    public float EmergencyThreshold { get; init; } = 0.8f;

    /// <summary>
    /// Food-grazing policy for this action. The Simulator's ResolveGrazing respects this
    /// flag — no coupling to which specific <see cref="SteeringKind"/> the action uses.
    /// </summary>
    public GrazingMode Grazing { get; init; } = GrazingMode.None;

    /// <summary>score = BaseWeight × ∏ considerations (in [0,1]).</summary>
    public float Score(in SenseContext ctx, Drives drives)
    {
        float score = BaseWeight;
        foreach (var c in Considerations)
            score *= c.Evaluate(ctx, drives);
        return Math.Clamp(score, 0f, 1f);
    }
}
