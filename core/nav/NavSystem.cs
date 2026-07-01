using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Turns a goal-seeking agent's target point into obstacle-avoiding steering by following an A*
/// path across the walkable grid. Sits between the decision layer (which picks the goal) and the
/// locomotion layer (which accelerates toward the returned velocity) — the brain decides *where*,
/// nav decides *how to get there without ramming a rock*.
///
/// Shared by individual <see cref="Creature"/>s and by <see cref="Flock"/> anchors: both hold a
/// <see cref="NavState"/> and call <see cref="Steer"/>. Deterministic — A* draws no randomness and
/// the repath timer advances by delta only.
/// </summary>
public static class NavSystem
{
    /// <summary>Which steering kinds path to a goal. Flee/AvoidPlayer/Wander/Frolic/Rest stay
    /// reactive (local steer only, kept off obstacles by <see cref="SimPhysics.SlideAgainstTerrain"/>);
    /// Flock members follow their anchor reactively — the anchor is the herd's nav agent.</summary>
    public static bool IsGoalSeeking(SteeringKind kind) => kind
        is SteeringKind.Approach
        or SteeringKind.Forage
        or SteeringKind.SeekFlock
        or SteeringKind.FollowPlayer;

    /// <summary>
    /// Steer <paramref name="pos"/> toward <paramref name="goal"/> along a cached/recomputed A*
    /// path, returning the desired XZ velocity. Returns <c>null</c> when there is no usable path
    /// (goal blocked/unreachable, or already at the goal cell) — the caller then falls back to its
    /// straight-line steering, and terrain collision still prevents pass-through.
    /// </summary>
    public static Vector3? Steer(
        Vector3 pos, Vector3 goal, NavState nav, MapData map, NavConfig cfg, float delta, float maxSpeed)
    {
        var goalCell = map.WorldToCell(goal);

        nav.RepathTimer -= delta;
        bool needRepath =
            nav.Waypoints is null ||
            nav.GoalCell != goalCell ||
            nav.RepathTimer <= 0f ||
            NextWaypointBlocked(nav, map);

        if (needRepath)
        {
            nav.RepathTimer = cfg.RepathInterval;
            nav.GoalCell = goalCell;

            var startCell = map.WorldToCell(pos);
            var path = GridPathfinder.FindPath(map, startCell, goalCell, cfg.MaxExpansions);
            if (path is null || path.Count == 0)
            {
                nav.Waypoints = null; // fall back to straight steering
                return null;
            }

            nav.Waypoints = new List<Vector3>(path.Count);
            foreach (var (cx, cz) in path)
                nav.Waypoints.Add(map.CellToWorldCenter(cx, cz));
            nav.Index = 0;
        }

        var wps = nav.Waypoints;
        if (wps is null || wps.Count == 0)
            return null;

        // Pop reached waypoints, but never past the last so we keep steering to the goal cell.
        while (nav.Index < wps.Count - 1 && Vec.HorizDist(pos, wps[nav.Index]) <= cfg.WaypointArriveRadius)
            nav.Index++;

        var target = wps[nav.Index];
        bool isFinal = nav.Index == wps.Count - 1;
        // Full speed through intermediate corners; ease onto the final waypoint so the agent settles.
        return isFinal
            ? Steering.Arrive(pos, target, maxSpeed, cfg.WaypointArriveRadius)
            : Steering.Seek(pos, target, maxSpeed);
    }

    private static bool NextWaypointBlocked(NavState nav, MapData map)
    {
        var wps = nav.Waypoints;
        if (wps is null || nav.Index >= wps.Count) return false;
        return !map.IsWalkableWorld(wps[nav.Index]);
    }
}
