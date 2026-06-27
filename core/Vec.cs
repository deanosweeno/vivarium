using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Small shared vector helpers for the simulation. Centralizes the horizontal-distance
/// metric (the sim is effectively 2.5D — proximity is measured on the X/Z ground plane,
/// ignoring Y) and the nearest-entity scan, both of which were duplicated across the
/// Simulator, Flock, and PlayerController. Pure and deterministic.
/// </summary>
public static class Vec
{
    /// <summary>Distance between two points on the X/Z ground plane (Y ignored).</summary>
    public static float HorizDist(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// The item nearest <paramref name="from"/> by horizontal distance, with its distance.
    /// <paramref name="filter"/> excludes candidates (e.g. unavailable food, the player itself);
    /// <paramref name="maxDist"/> caps the search radius (default: unbounded). Returns
    /// (null, <paramref name="maxDist"/>) when nothing qualifies. O(n).
    /// </summary>
    public static (T? Item, float Dist) NearestBy<T>(
        IEnumerable<T> items,
        Vector3 from,
        Func<T, Vector3> position,
        Func<T, bool>? filter = null,
        float maxDist = float.MaxValue) where T : class
    {
        T? nearest = null;
        float nearestDist = maxDist;
        foreach (var item in items)
        {
            if (filter is not null && !filter(item)) continue;
            float d = HorizDist(position(item), from);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = item;
            }
        }
        return (nearest, nearestDist);
    }
}
