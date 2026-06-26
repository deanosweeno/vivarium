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

    /// <summary>Whether any available food item is within sense radius. For Forage.</summary>
    public bool HasFood { get; init; }

    /// <summary>Nearest available food's position (valid only when <see cref="HasFood"/>).</summary>
    public Vector3 FoodPosition { get; init; }

    /// <summary>1 when food is touching, 0 at/beyond sense radius (or none). For Forage.</summary>
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

    // Needs are copied in so the brain reads everything from one struct.
    public float Hunger { get; init; }
    public float Fatigue { get; init; }
    public float Boredom { get; init; }
}
