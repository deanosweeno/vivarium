namespace Vivarium.Core;

/// <summary>
/// Decision-cadence, anti-dither, perception, and action-table tunables for the Utility AI.
/// Grouped out of the former monolithic <see cref="BehaviorConfig"/> — see that class for the
/// full split.
/// </summary>
public sealed record BrainConfig
{
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

    /// <summary>Half-extent (in map cells) of the coarse scan for the nearest preferred-biome
    /// cell when a creature is outside its biome. Larger = sees farther, costs more per tick.</summary>
    public int BiomeSearchCells { get; init; } = 12;

    /// <summary>Cell stride of that coarse biome scan — every Nth cell is sampled, trading
    /// precision for speed (a full per-cell scan is O(map²)).</summary>
    public int BiomeSearchStep { get; init; } = 2;

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

    /// <summary>Width of the steering slow-down band, in multiples of the creature's own
    /// <see cref="CreatureTraits.Radius"/>, used by Approach's Standoff and Forage's Arrive to
    /// ease the final approach instead of braking abruptly.</summary>
    public float SteeringSlowRadiusRadii { get; init; } = 2f;

    // --- wander dwell (how long before a creature re-rolls its wander direction) ---

    /// <summary>Minimum seconds a wandering creature holds a direction before re-rolling.</summary>
    public float WanderDwellMin { get; init; } = 3f;

    /// <summary>Maximum seconds a wandering creature holds a direction before re-rolling.</summary>
    public float WanderDwellMax { get; init; } = 7f;

    // --- frolic (boredom play behavior) ---

    /// <summary>Minimum seconds a frolicking creature holds a darty direction before re-rolling.
    /// Much shorter than WanderDwell so play reads as an energetic zig-zag, not a stroll.</summary>
    public float FrolicDwellMin { get; init; } = 0.4f;

    /// <summary>Maximum seconds a frolicking creature holds a darty direction before re-rolling.</summary>
    public float FrolicDwellMax { get; init; } = 0.9f;

    // --- action table ---

    /// <summary>The candidate actions scored each decision.</summary>
    public IReadOnlyList<BehaviorAction> Actions { get; init; } = BehaviorConfig.DefaultActions();
}

/// <summary>
/// Flock (group entity) + seek-flock tunables. Grouped out of the former monolithic
/// <see cref="BehaviorConfig"/> — see that class for the full split.
/// </summary>
public sealed record FlockConfig
{
    /// <summary>Fraction of a flock member's speed cap that its separation push may reach. Clamps
    /// separation so a dense pack can't out-shove cohesion and explode the herd.</summary>
    public float FlockSeparationCapFraction { get; init; } = 0.5f;

    /// <summary>Weight of the darty frolic drift mixed into the anchor pull while a flocked
    /// creature frolics — keeps play tethered to the herd instead of bolting.</summary>
    public float FrolicDriftWeight { get; init; } = 0.5f;

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

    /// <summary>Weight of the light peer-alignment term (average neighbor heading) blended into
    /// Flock steering — the "boids" alignment rule, layered on top of anchor cohesion so members
    /// loosely match each other's direction instead of all independently orbiting the anchor.
    /// 0 disables it (anchor-only cohesion, the pre-Phase-C behavior).</summary>
    public float FlockAlignmentWeight { get; init; } = 0.08f;

    /// <summary>Circle radius at one member; grows with √(member count) by FlockRadiusPerMember.</summary>
    public float FlockBaseRadius { get; init; } = 2.5f;

    /// <summary>Per-√member growth of the flock circle radius — keeps a larger herd uncramped.</summary>
    public float FlockRadiusPerMember { get; init; } = 0.6f;

    /// <summary>Minimum number of nearby kin required to seed a new flock.  Below this threshold
    /// unflocked kin remain solitary.  Default 2 (pair of sheep).</summary>
    public int FlockMinSize { get; init; } = 2;

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
}

/// <summary>
/// Need-dynamics (per second) + broadcast-threshold tunables. Grouped out of the former
/// monolithic <see cref="BehaviorConfig"/> — see that class for the full split.
/// </summary>
public sealed record NeedConfig
{
    /// <summary>Hunger gained per second (seated; satisfied by foraging once food exists).</summary>
    public float HungerGainPerSec { get; init; } = 0.003f;

    /// <summary>Boredom gained per second while idle/resting. Halved from 0.03 to space
    /// out play bursts so Frolic reads as an occasional lively moment, not a constant state.</summary>
    public float BoredomGainPerSec { get; init; } = 0.015f;

    /// <summary>Boredom relieved per second while frolicking.</summary>
    public float BoredomRelievePerSec { get; init; } = 0.4f;

    /// <summary>Hunger above which the creature broadcasts a feed-me bubble (when not foraging).</summary>
    public float BroadcastHungerThreshold { get; init; } = 0.5f;

    /// <summary>Boredom above which the creature broadcasts a play-with-me bubble (when not frolicking).</summary>
    public float BroadcastBoredomThreshold { get; init; } = 0.5f;

    /// <summary>Fraction of max speed below which a moving creature counts as "nearly stopped" and
    /// recovers Fatigue instead of accruing it. Extracted from a magic literal in the former
    /// <c>Simulator.UpdateNeeds</c> (now <see cref="NeedSystem.Resolve"/>).</summary>
    public float FatigueRecoverSpeedThreshold { get; init; } = 0.1f;
}

/// <summary>
/// Player interaction / taming bond tunables. Grouped out of the former monolithic
/// <see cref="BehaviorConfig"/> — see that class for the full split.
/// </summary>
public sealed record InteractionConfig
{
    /// <summary>Affection at/above which the pet verbs (Soothe/Play) become available — the
    /// creature has warmed up enough to be handled. Below it, pet attempts no-op.</summary>
    public float PartialBondThreshold { get; init; } = 0.4f;

    /// <summary>Affection at/above which a creature counts as fully tamed (reserved for follow-always
    /// and breeding hooks; not gating behaviour in v1).</summary>
    public float FullBondThreshold { get; init; } = 0.85f;

    /// <summary>Horizontal distance (arena units) within which a player interaction (feed/soothe/play)
    /// can land on the nearest creature.</summary>
    public float InteractReach { get; init; } = 1.5f;

    /// <summary>Hunger removed by one feed.</summary>
    public float FeedHungerRelief { get; init; } = 0.5f;

    /// <summary>Affection gained per feed — the primary trust-builder.</summary>
    public float FeedBond { get; init; } = 0.2f;

    /// <summary>Fatigue removed by one soothe (calm pet lets the creature rest).</summary>
    public float SootheFatigueRelief { get; init; } = 0.5f;

    /// <summary>Affection gained per soothe.</summary>
    public float SootheBond { get; init; } = 0.15f;

    /// <summary>Boredom removed by one play interaction.</summary>
    public float PlayBoredomRelief { get; init; } = 0.5f;

    /// <summary>Affection gained per play.</summary>
    public float PlayBond { get; init; } = 0.15f;

    /// <summary>
    /// Floor on the personality flavor-match multiplier applied to bond gain. A perfectly matched
    /// interaction (a lively creature played with, a cuddly one soothed) gains the full bond; a
    /// mismatched one still gains <c>FlavorMismatchFloor ×</c> the bond — never zero, never negative.
    /// Cozy by design: reading temperament is a bonus, not a wrong-answer trap. 1.0 disables the
    /// personality effect entirely.
    /// </summary>
    public float FlavorMismatchFloor { get; init; } = 0.4f;
}

/// <summary>
/// All tunable numbers and the action table for the Utility AI. Mirrors the role of
/// <see cref="MapGenConfig"/> / <see cref="BiomeDef"/>: behavior is retuned by editing
/// data here, never by changing the brain's logic. One shared instance is typically held
/// by the <see cref="Simulator"/> and referenced by every creature's <see cref="UtilityBrain"/>.
///
/// A thin aggregate over four focused sub-configs (<see cref="BrainConfig"/>,
/// <see cref="FlockConfig"/>, <see cref="NeedConfig"/>, <see cref="InteractionConfig"/>), grouped
/// by concern instead of one flat 50-field bag. The original flat properties are kept as
/// delegating pass-throughs so existing call sites (and tests) are unaffected; new code should
/// prefer reading the sub-config directly (<c>Behavior.Brain.SenseRadius</c> etc.).
/// </summary>
public sealed class BehaviorConfig
{
    public BrainConfig Brain { get; set; } = new();
    public FlockConfig Flock { get; set; } = new();
    public NeedConfig Need { get; set; } = new();
    public InteractionConfig Interaction { get; set; } = new();

    // --- decision cadence & anti-dithering (delegates to Brain) ---

    public float DecisionInterval { get => Brain.DecisionInterval; init => Brain = Brain with { DecisionInterval = value }; }
    public float SwitchMargin { get => Brain.SwitchMargin; init => Brain = Brain with { SwitchMargin = value }; }
    public float CommitmentBonus { get => Brain.CommitmentBonus; init => Brain = Brain with { CommitmentBonus = value }; }
    public float CommitmentDecayPerSec { get => Brain.CommitmentDecayPerSec; init => Brain = Brain with { CommitmentDecayPerSec = value }; }
    public float DecisionNoise { get => Brain.DecisionNoise; init => Brain = Brain with { DecisionNoise = value }; }
    public float SatiationThreshold { get => Brain.SatiationThreshold; init => Brain = Brain with { SatiationThreshold = value }; }

    // --- perception (delegates to Brain) ---

    public float SenseRadius { get => Brain.SenseRadius; init => Brain = Brain with { SenseRadius = value }; }
    public float FoodSenseRadius { get => Brain.FoodSenseRadius; init => Brain = Brain with { FoodSenseRadius = value }; }
    public float BiomeGradientWeight { get => Brain.BiomeGradientWeight; init => Brain = Brain with { BiomeGradientWeight = value }; }
    public int BiomeSearchCells { get => Brain.BiomeSearchCells; init => Brain = Brain with { BiomeSearchCells = value }; }
    public int BiomeSearchStep { get => Brain.BiomeSearchStep; init => Brain = Brain with { BiomeSearchStep = value }; }
    public float HerdKinThreshold { get => Brain.HerdKinThreshold; init => Brain = Brain with { HerdKinThreshold = value }; }
    public float PersonalSpaceRadii { get => Brain.PersonalSpaceRadii; init => Brain = Brain with { PersonalSpaceRadii = value }; }
    public float SteeringSlowRadiusRadii { get => Brain.SteeringSlowRadiusRadii; init => Brain = Brain with { SteeringSlowRadiusRadii = value }; }

    // --- flock separation/drift (delegates to Flock) ---

    public float FlockSeparationCapFraction { get => Flock.FlockSeparationCapFraction; init => Flock = Flock with { FlockSeparationCapFraction = value }; }
    public float FrolicDriftWeight { get => Flock.FrolicDriftWeight; init => Flock = Flock with { FrolicDriftWeight = value }; }
    public float FlockWanderFloor { get => Flock.FlockWanderFloor; init => Flock = Flock with { FlockWanderFloor = value }; }
    public float FlockPace { get => Flock.FlockPace; init => Flock = Flock with { FlockPace = value }; }
    public float FlockAlignmentWeight { get => Flock.FlockAlignmentWeight; init => Flock = Flock with { FlockAlignmentWeight = value }; }

    // --- wander dwell (delegates to Brain) ---

    public float WanderDwellMin { get => Brain.WanderDwellMin; init => Brain = Brain with { WanderDwellMin = value }; }
    public float WanderDwellMax { get => Brain.WanderDwellMax; init => Brain = Brain with { WanderDwellMax = value }; }

    // --- flock (group entity) tunables (delegates to Flock) ---

    public float FlockBaseRadius { get => Flock.FlockBaseRadius; init => Flock = Flock with { FlockBaseRadius = value }; }
    public float FlockRadiusPerMember { get => Flock.FlockRadiusPerMember; init => Flock = Flock with { FlockRadiusPerMember = value }; }
    public int FlockMinSize { get => Flock.FlockMinSize; init => Flock = Flock with { FlockMinSize = value }; }
    public float FlockDecisionInterval { get => Flock.FlockDecisionInterval; init => Flock = Flock with { FlockDecisionInterval = value }; }
    public float FlockWanderDwellMin { get => Flock.FlockWanderDwellMin; init => Flock = Flock with { FlockWanderDwellMin = value }; }
    public float FlockWanderDwellMax { get => Flock.FlockWanderDwellMax; init => Flock = Flock with { FlockWanderDwellMax = value }; }
    public float FlockGrazeHungerThreshold { get => Flock.FlockGrazeHungerThreshold; init => Flock = Flock with { FlockGrazeHungerThreshold = value }; }
    public float FlockGrazeFoodRange { get => Flock.FlockGrazeFoodRange; init => Flock = Flock with { FlockGrazeFoodRange = value }; }
    public float FlockJoinRadius { get => Flock.FlockJoinRadius; init => Flock = Flock with { FlockJoinRadius = value }; }
    public float FlockLeaveRadius { get => Flock.FlockLeaveRadius; init => Flock = Flock with { FlockLeaveRadius = value }; }
    public float FlockMergeRadius { get => Flock.FlockMergeRadius; init => Flock = Flock with { FlockMergeRadius = value }; }
    public float SeekFlockDelay { get => Flock.SeekFlockDelay; init => Flock = Flock with { SeekFlockDelay = value }; }

    // --- need dynamics (delegates to Need) ---

    public float HungerGainPerSec { get => Need.HungerGainPerSec; init => Need = Need with { HungerGainPerSec = value }; }
    public float BoredomGainPerSec { get => Need.BoredomGainPerSec; init => Need = Need with { BoredomGainPerSec = value }; }
    public float BoredomRelievePerSec { get => Need.BoredomRelievePerSec; init => Need = Need with { BoredomRelievePerSec = value }; }

    // --- frolic (delegates to Brain) ---

    public float FrolicDwellMin { get => Brain.FrolicDwellMin; init => Brain = Brain with { FrolicDwellMin = value }; }
    public float FrolicDwellMax { get => Brain.FrolicDwellMax; init => Brain = Brain with { FrolicDwellMax = value }; }

    // --- player interaction & taming (delegates to Interaction) ---

    public float PartialBondThreshold { get => Interaction.PartialBondThreshold; init => Interaction = Interaction with { PartialBondThreshold = value }; }
    public float FullBondThreshold { get => Interaction.FullBondThreshold; init => Interaction = Interaction with { FullBondThreshold = value }; }
    public float InteractReach { get => Interaction.InteractReach; init => Interaction = Interaction with { InteractReach = value }; }
    public float FeedHungerRelief { get => Interaction.FeedHungerRelief; init => Interaction = Interaction with { FeedHungerRelief = value }; }
    public float FeedBond { get => Interaction.FeedBond; init => Interaction = Interaction with { FeedBond = value }; }
    public float SootheFatigueRelief { get => Interaction.SootheFatigueRelief; init => Interaction = Interaction with { SootheFatigueRelief = value }; }
    public float SootheBond { get => Interaction.SootheBond; init => Interaction = Interaction with { SootheBond = value }; }
    public float PlayBoredomRelief { get => Interaction.PlayBoredomRelief; init => Interaction = Interaction with { PlayBoredomRelief = value }; }
    public float PlayBond { get => Interaction.PlayBond; init => Interaction = Interaction with { PlayBond = value }; }
    public float FlavorMismatchFloor { get => Interaction.FlavorMismatchFloor; init => Interaction = Interaction with { FlavorMismatchFloor = value }; }

    // --- need broadcast (delegates to Need) ---

    public float BroadcastHungerThreshold { get => Need.BroadcastHungerThreshold; init => Need = Need with { BroadcastHungerThreshold = value }; }
    public float BroadcastBoredomThreshold { get => Need.BroadcastBoredomThreshold; init => Need = Need with { BroadcastBoredomThreshold = value }; }

    // --- action table (delegates to Brain) ---

    /// <summary>The candidate actions scored each decision.</summary>
    public IReadOnlyList<BehaviorAction> Actions { get => Brain.Actions; init => Brain = Brain with { Actions = value }; }

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
            // Rest until fatigue is fully recovered.
            HoldWhile = new HoldWhile(InputKind.Fatigue),
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
            // Eat-until-full latch: stays committed to Forage until Hunger drops back to the
            // satiation floor, so Flock/Approach can't yank a creature off food after one bite.
            HoldWhile = new HoldWhile(InputKind.Hunger, Threshold: 0.15f),
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
            // Play until boredom is fully drained.
            HoldWhile = new HoldWhile(InputKind.Boredom),
            Considerations =
            [
                new Consideration { Input = InputKind.Boredom,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 10f } },
            ],
        },

        // FleePlayer — a creature latches into a panic flee when the player is a threat
        // (strategy-owned decision: sheep flee unless food is offered). Emergency-capable
        // so it can interrupt a committed Flock/Wander; latched so it stays panicked until
        // the player leaves SafeDistance or the creature rejoins its flock. Isolated
        // creatures score high when threatened; flocked creatures score zero (the flock
        // flees as a group instead).
        new BehaviorAction
        {
            Name = "FleePlayer",
            Steering = SteeringKind.AvoidPlayer,
            BaseWeight = 1f,
            EmergencyCapable = true,
            EmergencyThreshold = 0.6f,
            // Flee until the player is no longer a threat or the creature has rejoined a flock.
            // Driven by the same SenseContext.PlayerPanic property as the brain's unconditional
            // override (Phase A), not a re-derived predicate.
            HoldWhile = new HoldWhile(InputKind.PlayerPanic, Threshold: 0.5f),
            Considerations =
            [
                // Threat gate: 1 when the player is a threat, 0 otherwise → kills the
                // action when the player is safe (holding food, bonded, out of range).
                new Consideration { Input = InputKind.PlayerThreat, Drive = DriveKind.Fear },
                // Proximity: a closer player means more urgency. 0 when the player is
                // at/beyond sense radius, 1 when touching.
                new Consideration { Input = InputKind.PlayerProximity, Drive = DriveKind.Fear,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 0.6f } },
                // Isolation gate: Inverse curve on HerdPresence so the action scores
                // 1 when isolated (HerdPresence=0 → Inverse=1), 0 when flocked
                // (HerdPresence=1 → Inverse=0). Flock-level flee handles the group case.
                new Consideration { Input = InputKind.HerdPresence,
                    Curve = new ResponseCurve { Type = CurveType.Inverse } },
            ],
        },

        // FollowPlayer — the lure. When the player carries food, a creature in range looks at and
        // eases toward the player so it can be walked into feeding range. Keyed purely on
        // proximity × holding-food, so it fires regardless of bond (a hungry stranger still comes
        // for food) yet vanishes the moment the food is put away.
        new BehaviorAction
        {
            Name = "FollowPlayer",
            Steering = SteeringKind.FollowPlayer,
            BaseWeight = 1f,
            Considerations =
            [
                new Consideration { Input = InputKind.PlayerProximity,
                    Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 0.6f } },
                new Consideration { Input = InputKind.PlayerHoldingFood },
            ],
        },
    ];
}
