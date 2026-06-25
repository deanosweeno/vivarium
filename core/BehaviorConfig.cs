namespace Vivarium.Core;

/// <summary>
/// All tunable numbers and the action table for the Utility AI. Mirrors the role of
/// <see cref="MapGenConfig"/> / <see cref="BiomeDef"/>: behavior is retuned by editing
/// data here, never by changing the brain's logic. One shared instance is typically held
/// by the <see cref="Simulator"/> and referenced by every creature's <see cref="UtilityBrain"/>.
/// </summary>
public sealed class BehaviorConfig
{
    // --- decision cadence & anti-dithering ---

    /// <summary>Seconds between action re-selections. Steering is recomputed every tick regardless.</summary>
    public float DecisionInterval { get; init; } = 0.5f;

    /// <summary>A challenger must beat the current action's score by this margin to take over.</summary>
    public float SwitchMargin { get; init; } = 0.15f;

    /// <summary>Score bonus added to the committed action (before decay/progress scaling).</summary>
    public float CommitmentBonus { get; init; } = 0.25f;

    /// <summary>Per-second decay of the commitment bonus — a stalled action eventually loosens its grip.</summary>
    public float CommitmentDecayPerSec { get; init; } = 0.1f;

    /// <summary>Seeded jitter added to each score before argmax. 0 = deterministic argmax.</summary>
    public float DecisionNoise { get; init; } = 0f;

    // --- perception ---

    /// <summary>How far a creature senses neighbors / samples terrain, in arena units.</summary>
    public float SenseRadius { get; init; } = 5f;

    // --- need dynamics (per second) ---

    /// <summary>Fatigue gained per second while moving (scaled by speed fraction).</summary>
    public float FatigueGainPerSec { get; init; } = 0.06f;

    /// <summary>Fatigue recovered per second while resting.</summary>
    public float FatigueRecoverPerSec { get; init; } = 0.25f;

    /// <summary>Hunger gained per second (seated; satisfied by foraging once food exists).</summary>
    public float HungerGainPerSec { get; init; } = 0.02f;

    /// <summary>Boredom gained per second while idle/resting (seated; relieved by play later).</summary>
    public float BoredomGainPerSec { get; init; } = 0.03f;

    /// <summary>Boredom relieved per second while actively moving.</summary>
    public float BoredomRelievePerSec { get; init; } = 0.05f;

    // --- action table ---

    /// <summary>The candidate actions scored each decision.</summary>
    public IReadOnlyList<BehaviorAction> Actions { get; init; } = DefaultActions();

    /// <summary>
    /// The v1 "full five" action table. Wander / Approach / Flee / Rest / SeekComfort,
    /// each built from considerations over the existing world systems.
    /// </summary>
    public static IReadOnlyList<BehaviorAction> DefaultActions() =>
    [
        // Wander — a low constant floor scaled by curiosity, so there's always something to do.
        new BehaviorAction
        {
            Name = "Wander",
            Steering = SteeringKind.Wander,
            BaseWeight = 0.25f,
            Considerations =
            [
                new Consideration { Input = InputKind.Constant, Drive = DriveKind.Curiosity,
                    Curve = new ResponseCurve { Type = CurveType.Linear, Slope = 0.6f, Offset = 0.4f } },
            ],
        },

        // Approach — sociable, neighbor in range, dampened by fear and lifted by aggression.
        new BehaviorAction
        {
            Name = "Approach",
            Steering = SteeringKind.Approach,
            BaseWeight = 1f,
            Considerations =
            [
                // Want a neighbor that's near but not already on top of us.
                new Consideration { Input = InputKind.NeighborProximity, Drive = DriveKind.Sociability,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 0.6f } },
                // ×(1 − fear): a fearful creature hesitates to close in.
                new Consideration { Input = InputKind.Constant, Drive = DriveKind.Fear, InvertDrive = true },
            ],
        },

        // Flee — fear × how close the neighbor is, soft-thresholded. Emergency-capable.
        new BehaviorAction
        {
            Name = "Flee",
            Steering = SteeringKind.Flee,
            BaseWeight = 1f,
            EmergencyCapable = true,
            EmergencyThreshold = 0.7f,
            Considerations =
            [
                new Consideration { Input = InputKind.NeighborProximity, Drive = DriveKind.Fear,
                    Curve = new ResponseCurve { Type = CurveType.Logistic, Midpoint = 0.6f, Steepness = 9f } },
            ],
        },

        // Rest — cubed fatigue: stays quiet until genuinely tired, then dominates.
        new BehaviorAction
        {
            Name = "Rest",
            Steering = SteeringKind.Rest,
            BaseWeight = 1f,
            Considerations =
            [
                new Consideration { Input = InputKind.Fatigue,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 3f } },
            ],
        },

        // SeekComfort — appetite × terrain discomfort. Forage proxy via biome happiness.
        // TODO: replace terrain-discomfort with real nearest-food perception when food entities exist.
        new BehaviorAction
        {
            Name = "SeekComfort",
            Steering = SteeringKind.SeekComfort,
            BaseWeight = 0.8f,
            Considerations =
            [
                new Consideration { Input = InputKind.TerrainDiscomfort, Drive = DriveKind.Appetite,
                    Curve = new ResponseCurve { Type = CurveType.Linear } },
            ],
        },
    ];
}
