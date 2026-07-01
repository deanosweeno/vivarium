using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Per-agent navigation state: the cached A* path (world-space waypoint centers), which waypoint
/// the agent is heading for, the repath countdown, and the goal cell the current path was built
/// for. Held by a <see cref="Creature"/> (as <see cref="Creature.Nav"/>) and by a
/// <see cref="Flock"/> anchor. Mutated by <see cref="NavSystem"/>; deterministic (no RNG, timers
/// advance by delta only).
/// </summary>
public sealed class NavState
{
    /// <summary>Current path as world-space cell centers (Y = 0), or null when there is no path
    /// (agent falls back to straight steering). Excludes the start cell.</summary>
    public List<Vector3>? Waypoints { get; set; }

    /// <summary>Index into <see cref="Waypoints"/> of the waypoint currently being steered toward.</summary>
    public int Index { get; set; }

    /// <summary>Seconds until the next forced repath. Counts down each tick.</summary>
    public float RepathTimer { get; set; }

    /// <summary>The goal cell the current <see cref="Waypoints"/> were computed for. A change forces
    /// an immediate repath so the agent retargets when its goal moves to a new cell.</summary>
    public (int cx, int cz) GoalCell { get; set; } = (int.MinValue, int.MinValue);
}
