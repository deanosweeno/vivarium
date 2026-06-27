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

    /// <summary>A challenger must beat the current action's score by this margin to take over.
    /// Kept narrow enough that a zero-boredom Wander can retake control from a zero-boredom
    /// Frolic even when flockless (so play never traps a creature permanently). The CommitmentBonus
    /// still provides anti-dithering stickiness after a switch.</summary>
    public float SwitchMargin { get; init; } = 0.06f;

    /// <summary>Score bonus added to the committed action (before decay/progress scaling).</summary>
    public float CommitmentBonus { get; init; } = 0.25f;

    /// <summary>Per-second decay of the commitment bonus — a stalled action eventually loosens its grip.</summary>
    public float CommitmentDecayPerSec { get; init; } = 0.1f;

    /// <summary>Seeded jitter added to each score before argmax. 0 = deterministic argmax.</summary>
    public float DecisionNoise { get; init; } = 0f;

    /// <summary>
    /// Hunger level a forager eats down to before it will abandon food. While the current action
    /// is Forage and Hunger is above this, the brain latches Forage (an "eat until full" hold) so a
    /// grazing creature isn't yo-yoed back to the herd after a single bite — see <see cref="UtilityBrain"/>.
    /// </summary>
    public float SatiationThreshold { get; init; } = 0.15f;

    // --- perception ---

    /// <summary>How far a creature senses neighbors / samples terrain, in arena units.</summary>
    public float SenseRadius { get; init; } = 5f;

    /// <summary>How far a creature can sense food, in arena units. Larger than SenseRadius
    /// so foraging always targets the nearest food instead of falling through to aimless wander.</summary>
    public float FoodSenseRadius { get; init; } = 20f;

    /// <summary>How strongly (0-1) a creature is pushed toward its preferred biome when
    /// outside one. 0 = no push, 1 = full-speed push (overpowers steering). 0.3 is a
    /// gentle nudge — the creature still follows its brain-chosen action but curves toward
    /// preferred terrain.</summary>
    public float BiomeGradientWeight { get; init; } = 0.3f;

    /// <summary>
    /// Minimum <see cref="Genetics.Similarity"/> for another creature to count as herd-mate, so a
    /// creature flocks only with genetically similar kin (and near-kin hybrids), not any neighbor.
    /// </summary>
    public float HerdKinThreshold { get; init; } = 0.85f;

    /// <summary>
    /// Personal-space radius, in multiples of the creature's own <see cref="CreatureTraits.Radius"/>.
    /// Any body closer than this contributes a separation push (summed over all such bodies, so a
    /// creature is shoved out of a crowd, not just away from its single nearest neighbor). Drives the
    /// avoidance term shared by the Approach and Flock steering, keeping noses out of faces.
    /// </summary>
    public float PersonalSpaceRadii { get; init; } = 4f;

    /// <summary>
    /// Shared social idle-drift floor: fraction of max speed of wander drift mixed into the
    /// Flock and Approach steering so a settled cluster keeps milling instead of freezing at a
    /// zero-velocity equilibrium (Flock's cohesion/separation cancel; Approach's Standoff eases
    /// to a motionless hold). 0 = old freeze-then-Boredom behavior.
    /// </summary>
    public float FlockWanderFloor { get; init; } = 0.35f;

    /// <summary>Speed (arena units/sec) the flock anchor drifts — ~80% of a typical sheep's max speed.
    /// Faster than the old 0.25 so the herd reads as one migrating mass.</summary>
    public float FlockPace { get; init; } = 0.4f;

    // --- wander dwell (how long before a creature re-rolls its wander direction) ---

    /// <summary>Minimum seconds a wandering creature holds a direction before re-rolling.</summary>
    public float WanderDwellMin { get; init; } = 3f;

    /// <summary>Maximum seconds a wandering creature holds a direction before re-rolling.</summary>
    public float WanderDwellMax { get; init; } = 7f;

    // --- flock (group entity) tunables ---

    /// <summary>Circle radius at one member; grows with √(member count) by FlockRadiusPerMember.</summary>
    public float FlockBaseRadius { get; init; } = 2.5f;

    /// <summary>Per-√member growth of the flock circle radius — keeps a larger herd uncramped.</summary>
    public float FlockRadiusPerMember { get; init; } = 0.6f;



    /// <summary>Seconds between flock-brain (Wander vs Graze) re-decisions.</summary>
    public float FlockDecisionInterval { get; init; } = 2f;

    /// <summary>Minimum seconds the flock anchor holds a wander direction (long dwell for steady herd drift).</summary>
    public float FlockWanderDwellMin { get; init; } = 15f;

    /// <summary>Maximum seconds the flock anchor holds a wander direction.</summary>
    public float FlockWanderDwellMax { get; init; } = 30f;

    /// <summary>Average member Hunger above which the flock prefers Graze (if food is near the anchor).</summary>
    public float FlockGrazeHungerThreshold { get; init; } = 0.4f;

    /// <summary>Food must be within SenseRadius × this of the anchor for the flock to commit to Graze.</summary>
    public float FlockGrazeFoodRange { get; init; } = 2f;

    /// <summary>An unflocked kin within this distance of a flock's anchor (or seed kin) joins/forms it.</summary>
    public float FlockJoinRadius { get; init; } = 8f;

    /// <summary>A member straying beyond this distance from its anchor is dropped from the flock.</summary>
    public float FlockLeaveRadius { get; init; } = 12f;

    /// <summary>Two flocks whose anchors come within this distance merge (smaller folds into larger).</summary>
    public float FlockMergeRadius { get; init; } = 4f;

    /// <summary>Seconds a creature must be separated from any flock before
    /// SeekFlock becomes available. Normalized 0→1 in SenseContext.</summary>
    public float SeekFlockDelay { get; init; } = 60f;

    // --- need dynamics (per second) ---

    /// <summary>Hunger gained per second (seated; satisfied by foraging once food exists).</summary>
    public float HungerGainPerSec { get; init; } = 0.003f;

    /// <summary>Boredom gained per second while idle/resting. Halved from 0.03 to space
    /// out play bursts so Frolic reads as an occasional lively moment, not a constant state.</summary>
    public float BoredomGainPerSec { get; init; } = 0.015f;

    /// <summary>Boredom relieved per second while frolicking.</summary>
    public float BoredomRelievePerSec { get; init; } = 0.4f;

    // --- frolic (boredom play behavior) ---

    /// <summary>Minimum seconds a frolicking creature holds a darty direction before re-rolling.
    /// Much shorter than WanderDwell so play reads as an energetic zig-zag, not a stroll.</summary>
    public float FrolicDwellMin { get; init; } = 0.4f;

    /// <summary>Maximum seconds a frolicking creature holds a darty direction before re-rolling.</summary>
    public float FrolicDwellMax { get; init; } = 0.9f;

    // --- action table ---

    /// <summary>The candidate actions scored each decision.</summary>
    public IReadOnlyList<BehaviorAction> Actions { get; init; } = DefaultActions();

    /// <summary>
    /// The v1 "full five" action table. Wander / Approach / Flee / Rest / Forage,
    /// each built from considerations over the existing world systems.
    /// </summary>
    public static IReadOnlyList<BehaviorAction> DefaultActions() =>
    [
        // Wander — driven by Boredom (cubed, like Rest's fatigue), so it stays quiet while a
        // creature is engaged but rises sharply once it's been idle too long, eventually
        // out-scoring a parked Approach/Flock equilibrium and rousing it to roam. A curiosity floor
        // tints the cadence by personality without gating it (every creature roams when bored).
        // Boredom builds during Wander (only Frolic relieves it), so a wander eventually
        // transitions to Frolic once boredom peaks.
        new BehaviorAction
        {
            Name = "Wander",
            Steering = SteeringKind.Wander,
            Grazing = GrazingMode.WhenHungry,
            BaseWeight = 0.9f,
            Considerations =
            [
                // Offset is the always-on roaming floor (keeps Wander the default fallback when
                // nothing else fires); boredom³ lifts it until it can rouse a parked equilibrium.
                new Consideration { Input = InputKind.Boredom,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 3f, Offset = 0.15f } },
                new Consideration { Input = InputKind.Constant, Drive = DriveKind.Curiosity,
                    Curve = new ResponseCurve { Type = CurveType.Linear, Slope = 0.4f, Offset = 0.6f } },
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
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 8f } },
            ],
        },

        // Forage — driven by Hunger (squared, so it stays quiet until the creature is
        // genuinely hungry) and weighted by the Appetite drive. No food-proximity gate:
        // a hungry creature commits to foraging and *searches* (Wander) until food is in
        // range, then the Forage steering paths to it and the Simulator grazes it down.
        new BehaviorAction
        {
            Name = "Forage",
            Steering = SteeringKind.Forage,
            Grazing = GrazingMode.Always,
            BaseWeight = 0.9f,
            Considerations =
            [
                new Consideration { Input = InputKind.Hunger, Drive = DriveKind.Appetite,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 2f } },
            ],
        },

        // Flock — hold formation in the creature's flock, scaled by Sociability. BaseWeight is now
        // the GROUP-DEFAULT (0.7): with a flock present this beats idle Wander, so a sociable
        // creature stays in the herd by default and does NOT wander off without reason. Genuine
        // needs still peel it off because their considerations spike past Flock — Forage (Hunger²×
        // Appetite), Rest (Fatigue³), Flee (emergency), Approach (a close neighbour/player). Once
        // the need subsides, Flock wins again and the member eases back to the (moved) anchor —
        // "return when satisfied" falls out of the utility scoring, no dedicated return action.
        new BehaviorAction
        {
            Name = "Flock",
            Steering = SteeringKind.Flock,
            Grazing = GrazingMode.WhenHungry,
            BaseWeight = 0.7f,
            Considerations =
            [
                new Consideration { Input = InputKind.HerdPresence, Drive = DriveKind.Sociability },
            ],
        },

        // SeekFlock — after SeparationTime passes the delay threshold, a separated
        // creature looks for a kin flock to rejoin. Sociability weights urgency.
        // The logistic curve creates a sharp "click" near 0.5 normalized (≈30 real seconds
        // into the 60s delay) so the action doesn't creep in gradually.
        new BehaviorAction
        {
            Name = "SeekFlock",
            Steering = SteeringKind.SeekFlock,
            BaseWeight = 0.8f,
            Considerations =
            [
                new Consideration { Input = InputKind.SeparationTime, Drive = DriveKind.Sociability,
                    Curve = new ResponseCurve { Type = CurveType.Logistic, Midpoint = 0.5f, Steepness = 10f } },
            ],
        },

        // Frolic — the only action that relieves Boredom. A Power(x¹⁰) curve stays near
        // zero until Boredom is very near 1.0, then spikes sharply — Frolic fires only when
        // genuinely bored stiff. Once frolicking, BoredomRelievePerSec drains Boredom to 0
        // and a latch holds until empty, so play finishes completely.
        new BehaviorAction
        {
            Name = "Frolic",
            Steering = SteeringKind.Frolic,
            BaseWeight = 1f,
            Considerations =
            [
                new Consideration { Input = InputKind.Boredom,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 10f } },
            ],
        },
    ];
}
