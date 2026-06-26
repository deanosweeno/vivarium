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

    /// <summary>Whether two or more other creatures are within sense radius (a herd to cohere to).</summary>
    public bool HasHerd { get; init; }

    /// <summary>
    /// Average position of all neighbors within sense radius (valid only when <see cref="HasHerd"/>).
    /// The point a flocking creature steers toward — see <see cref="Steering.Cohesion"/>.
    /// </summary>
    public Vector3 HerdCentroid { get; init; }

    /// <summary>Whether any available food item is within sense radius. For Forage.</summary>
    public bool HasFood { get; init; }

    /// <summary>Nearest available food's position (valid only when <see cref="HasFood"/>).</summary>
    public Vector3 FoodPosition { get; init; }

    /// <summary>1 when food is touching, 0 at/beyond sense radius (or none). For Forage.</summary>
    public float FoodProximity { get; init; }

    // Needs are copied in so the brain reads everything from one struct.
    public float Hunger { get; init; }
    public float Fatigue { get; init; }
    public float Boredom { get; init; }
}
