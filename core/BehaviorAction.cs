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

    /// <summary>score = BaseWeight × ∏ considerations (in [0,1]).</summary>
    public float Score(in SenseContext ctx, Drives drives)
    {
        float score = BaseWeight;
        foreach (var c in Considerations)
            score *= c.Evaluate(ctx, drives);
        return Math.Clamp(score, 0f, 1f);
    }
}
