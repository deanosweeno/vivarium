using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// A creature's perception snapshot for one decision — what it can sense right now.
/// Assembled by the <see cref="Simulator"/> (which owns the entity list, map, and biomes)
/// and handed to the <see cref="UtilityBrain"/>. Pure data with no Godot deps, so the
/// brain is trivially testable against crafted contexts.
///
/// Lean by design: it grows one field at a time as the world gains systems (nearest food,
/// nearest threat, player distance, …). v1 carries only what the five actions consume.
/// </summary>
public readonly struct SenseContext
{
    /// <summary>The sensing creature's own position.</summary>
    public Vector3 SelfPosition { get; init; }

    /// <summary>Whether any other entity is within sense radius.</summary>
    public bool HasNeighbor { get; init; }

    /// <summary>Nearest neighbor's position (valid only when <see cref="HasNeighbor"/>).</summary>
    public Vector3 NeighborPosition { get; init; }

    /// <summary>1 when a neighbor is touching, 0 at/beyond sense radius (or none). For Approach/Flee.</summary>
    public float NeighborProximity { get; init; }

    /// <summary>
    /// Accumulated avoidance direction: the sum, over every body inside personal space, of a unit
    /// away-vector weighted by how deep that body is inside personal space (0 at the edge, →1 when
    /// overlapping). XZ only, not yet scaled to speed — the brain multiplies by maxSpeed. Summing
    /// over <em>all</em> crowders (not just the nearest) is what stops a creature from nosing into a
    /// non-nearest neighbor. Reacts to any body, kin or not. Zero when nothing is in personal space.
    /// </summary>
    public Vector3 SeparationPush { get; init; }

    /// <summary>Whether this creature belongs to a non-empty <see cref="Flock"/> to cohere toward.</summary>
    public bool HasFlock { get; init; }

    /// <summary>
    /// The flock's moving anchor — the center of the wandering circle (valid only when
    /// <see cref="HasFlock"/>). A member eases toward this via <see cref="Steering.Cohesion"/>,
    /// settling anywhere inside <see cref="FlockRadius"/> so the herd mills as one moving disc.
    /// </summary>
    public Vector3 FlockAnchor { get; init; }

    /// <summary>
    /// Radius of the flock's circle (valid only when <see cref="HasFlock"/>). No longer consumed
    /// by steering (smooth Standoff replaced Arrive-based Cohesion); retained as world state.
    /// </summary>
    public float FlockRadius { get; init; }

    /// <summary>Whether any available food item is within food-sense radius (see FoodSenseRadius). For Forage.</summary>
    public bool HasFood { get; init; }

    /// <summary>Nearest available food's position. Always populated (unlike HasFood); the
    /// creature knows where the nearest edible item is, even beyond immediate reach.</summary>
    public Vector3 FoodPosition { get; init; }

    /// <summary>Horizontal distance to the nearest available food (arena units), or float.PositiveInfinity.</summary>
    public float FoodDistance { get; init; }

    /// <summary>1 when food is touching, 0 at/beyond food-sense radius (or none). For Forage.</summary>
    public float FoodProximity { get; init; }

    /// <summary>
    /// Normalized [0,1] separation time: 0 = still in a flock or just left, 1 = been alone
    /// for ≥SeekFlockDelay seconds. For the SeekFlock consideration.
    /// </summary>
    public float SeparationTime { get; init; }

    /// <summary>Whether any kin flock exists within the world. When false, SeekFlock
    /// steering falls back to Wander (the creature searches randomly).</summary>
    public bool HasNearbyFlock { get; init; }

    /// <summary>Nearest kin flock's anchor (valid only when <see cref="HasNearbyFlock"/>).
    /// The SeekFlock steering smoothly approaches this point.</summary>
    public Vector3 NearestFlockAnchor { get; init; }

    // --- player awareness (the taming channel) ---

    /// <summary>Whether the player avatar is within sense radius. When false the player-reaction
    /// actions (FleePlayer/FollowPlayer) score zero and the creature ignores the player.</summary>
    public bool HasPlayer { get; init; }

    /// <summary>The player's position (valid only when <see cref="HasPlayer"/>). For FleePlayer/FollowPlayer steering.</summary>
    public Vector3 PlayerPosition { get; init; }

    /// <summary>1 when the player is touching, 0 at/beyond sense radius (or absent). For the player-reaction considerations.</summary>
    public float PlayerProximity { get; init; }

    /// <summary>Whether the player currently holds food — flips a wary creature from fleeing to following (the lure).</summary>
    public bool PlayerHoldingFood { get; init; }

    /// <summary>Whether the player is currently a threat to this creature, as decided by the
    /// injected <see cref="IFleeStrategy"/>. 1 = threat, 0 = safe (holding food, bonded, etc.).</summary>
    public bool IsPlayerThreat { get; init; }

    // Needs are copied in so the brain reads everything from one struct.
    public float Hunger { get; init; }
    public float Fatigue { get; init; }
    public float Boredom { get; init; }

    /// <summary>This creature's bond with the player [0,1] — gates how strongly it flees (suppressed
    /// as the bond grows). Copied from <see cref="CreatureNeeds.Affection"/>.</summary>
    public float Affection { get; init; }

    // --- biome awareness ---

    /// <summary>The biome of the cell under the creature's position, or Plains when map is absent.</summary>
    public Biome CurrentBiome { get; init; }

    /// <summary>Comfort in current biome [0,1]. 1 = preferred, 0 = hostile (or no preference set).
    /// Computed from CreatureTraits.PreferredBiomes in the Simulator.</summary>
    public float BiomeComfort { get; init; }

    /// <summary>Unit direction (XZ) toward the nearest cell of a preferred biome, or zero when
    /// already in one or when no map is present. The Simulator blends this into velocity after
    /// the brain runs so biome preference is physics, not decision-making.</summary>
    public Vector3 BiomePush { get; init; }
}
