namespace Vivarium.Core;

/// <summary>
/// Deterministic A* over <see cref="MapData.IsWalkable"/>. Pure function of the grid — no RNG,
/// no wall-clock — so the same start/goal on the same map always yields the same path.
///
/// 8-directional with an octile heuristic. Diagonals never cut corners: a diagonal step is
/// allowed only when both orthogonally-adjacent cells are also walkable. The <c>start</c> cell
/// is treated as expandable even if it is itself blocked (an agent that slid onto a rock edge can
/// still path out); the <c>goal</c> cell must be walkable or no path is returned.
/// </summary>
public static class GridPathfinder
{
    private const float Sqrt2 = 1.41421356f;

    // Fixed neighbor order (4 orthogonal, then 4 diagonal) → deterministic expansion + tie-breaks.
    private static readonly (int dx, int dz)[] Neighbors =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    /// <summary>
    /// Find a path from <paramref name="start"/> to <paramref name="goal"/> (both cell coords).
    /// Returns the waypoint cells from the first step through the goal inclusive, <b>excluding</b>
    /// the start cell. Returns an empty list when start == goal, and <c>null</c> when no path
    /// exists (goal blocked / unreachable / <paramref name="maxExpansions"/> exceeded).
    /// </summary>
    public static List<(int cx, int cz)>? FindPath(
        MapData map, (int cx, int cz) start, (int cx, int cz) goal, int maxExpansions)
    {
        if (!map.InBounds(start.cx, start.cz) || !map.InBounds(goal.cx, goal.cz))
            return null;
        if (!map.IsWalkable(goal.cx, goal.cz))
            return null;
        if (start == goal)
            return new List<(int, int)>();

        int w = map.Width;
        int cells = w * map.Depth;
        int Idx(int cx, int cz) => cz * w + cx;

        var g = new float[cells];
        var cameFrom = new int[cells];
        var closed = new bool[cells];
        for (int i = 0; i < cells; i++) { g[i] = float.PositiveInfinity; cameFrom[i] = -1; }

        int startIdx = Idx(start.cx, start.cz);
        int goalIdx = Idx(goal.cx, goal.cz);

        var open = new MinHeap(cells);
        g[startIdx] = 0f;
        open.Push(startIdx, Heuristic(start.cx, start.cz, goal.cx, goal.cz));

        int expansions = 0;
        while (open.TryPop(out int current))
        {
            if (current == goalIdx)
                return Reconstruct(cameFrom, current, w);
            if (closed[current]) continue;
            closed[current] = true;

            if (++expansions > maxExpansions)
                return null;

            int cx = current % w;
            int cz = current / w;

            foreach (var (dx, dz) in Neighbors)
            {
                int nx = cx + dx, nz = cz + dz;
                if (!map.InBounds(nx, nz) || !map.IsWalkable(nx, nz)) continue;

                bool diagonal = dx != 0 && dz != 0;
                // No corner-cutting: both orthogonal neighbors of a diagonal must be open.
                if (diagonal && (!map.IsWalkable(cx + dx, cz) || !map.IsWalkable(cx, cz + dz)))
                    continue;

                int ni = Idx(nx, nz);
                if (closed[ni]) continue;

                float tentative = g[current] + (diagonal ? Sqrt2 : 1f);
                if (tentative < g[ni])
                {
                    g[ni] = tentative;
                    cameFrom[ni] = current;
                    open.Push(ni, tentative + Heuristic(nx, nz, goal.cx, goal.cz));
                }
            }
        }

        return null;
    }

    private static float Heuristic(int ax, int az, int bx, int bz)
    {
        int dx = Math.Abs(ax - bx);
        int dz = Math.Abs(az - bz);
        int min = Math.Min(dx, dz);
        int max = Math.Max(dx, dz);
        return max + (Sqrt2 - 1f) * min; // octile
    }

    private static List<(int, int)> Reconstruct(int[] cameFrom, int current, int w)
    {
        var path = new List<(int, int)>();
        // Walk back to (but excluding) the start cell — the agent is already there.
        while (cameFrom[current] != -1)
        {
            path.Add((current % w, current / w));
            current = cameFrom[current];
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Tiny binary min-heap keyed on (priority, insertion sequence). The sequence tie-break makes
    /// pop order fully deterministic when two nodes share an f-score. Allows duplicate entries per
    /// node (lazy deletion) — the closed-set check in the search discards stale pops.
    /// </summary>
    private sealed class MinHeap
    {
        private struct Entry { public float Priority; public long Seq; public int Node; }

        private readonly List<Entry> _heap;
        private long _seq;

        public MinHeap(int capacityHint) => _heap = new List<Entry>(Math.Min(capacityHint, 256));

        public void Push(int node, float priority)
        {
            _heap.Add(new Entry { Priority = priority, Seq = _seq++, Node = node });
            SiftUp(_heap.Count - 1);
        }

        public bool TryPop(out int node)
        {
            if (_heap.Count == 0) { node = -1; return false; }
            node = _heap[0].Node;
            int last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);
            if (_heap.Count > 0) SiftDown(0);
            return true;
        }

        private static bool Less(in Entry a, in Entry b)
            => a.Priority < b.Priority || (a.Priority == b.Priority && a.Seq < b.Seq);

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Less(_heap[i], _heap[parent])) break;
                (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            int n = _heap.Count;
            while (true)
            {
                int left = 2 * i + 1, right = 2 * i + 2, smallest = i;
                if (left < n && Less(_heap[left], _heap[smallest])) smallest = left;
                if (right < n && Less(_heap[right], _heap[smallest])) smallest = right;
                if (smallest == i) break;
                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
        }
    }
}
