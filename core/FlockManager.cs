using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Owns flock membership reconciliation — the periodic form / join / leave / merge pass over the
/// entity list — extracted from the Simulator so the group-membership rules live in one focused,
/// testable place. Holds only the re-decision timer; the flock list and entities are passed in,
/// so a test can drive it against a hand-built world. Deterministic — positions only, no RNG.
/// </summary>
public sealed class FlockManager
{
    private double _flockTimer;

    /// <summary>
    /// Reconcile flock membership over <paramref name="entities"/>, mutating <paramref name="flocks"/>
    /// in place. Runs at most once per <see cref="BehaviorConfig.DecisionInterval"/>:
    ///   • <b>Leave</b>: drop members strayed past <see cref="BehaviorConfig.FlockLeaveRadius"/>.
    ///   • <b>Join</b>: an unflocked kin within <see cref="BehaviorConfig.FlockJoinRadius"/> of a
    ///     flock's anchor joins it.
    ///   • <b>Form</b>: clusters of still-unflocked kin seed a new flock at their centroid.
    ///   • <b>Merge</b>: flocks whose anchors close within <see cref="BehaviorConfig.FlockMergeRadius"/>
    ///     fold the smaller into the larger.
    /// Only brained creatures flock; the kin gate (<see cref="Genetics.Similarity"/> ≥
    /// <see cref="BehaviorConfig.HerdKinThreshold"/>) keeps non-kin (Sprouts, the player) out.
    /// <paramref name="groundFloor"/> resolves a new flock's anchor height.
    /// TODO: hysteresis timer on Leave + split-on-oversize flock.
    /// </summary>
    public void Update(
        double delta,
        IReadOnlyList<Creature> entities,
        List<Flock> flocks,
        BehaviorConfig behavior,
        Func<Vector3, float> groundFloor)
    {
        _flockTimer -= delta;
        if (_flockTimer > 0) return;
        _flockTimer = behavior.DecisionInterval;

        float joinR = behavior.FlockJoinRadius;
        float leaveR = behavior.FlockLeaveRadius;
        float mergeR = behavior.FlockMergeRadius;

        // 1. Leave — drop strayed members.
        foreach (var flock in flocks)
        {
            for (int i = flock.Members.Count - 1; i >= 0; i--)
            {
                var m = flock.Members[i];
                if (Vec.HorizDist(m.Position, flock.Anchor) > leaveR)
                {
                    m.Flock = null;
                    flock.Members.RemoveAt(i);
                }
            }
        }
        flocks.RemoveAll(f => f.Members.Count == 0);

        // 2. Join — an unflocked kin near an existing flock's anchor joins it.
        foreach (var e in entities)
        {
            if (e.Brain is null || e.Flock is not null) continue;
            Flock? best = null;
            float bestD = joinR;
            foreach (var flock in flocks)
            {
                float d = Vec.HorizDist(e.Position, flock.Anchor);
                if (d <= bestD && Genetics.Similarity(e, flock.Members[0]) >= behavior.HerdKinThreshold)
                {
                    bestD = d;
                    best = flock;
                }
            }
            if (best is not null)
            {
                best.Members.Add(e);
                e.Flock = best;
            }
        }

        // 3. Form — cluster still-unflocked kin into new flocks.
        for (int i = 0; i < entities.Count; i++)
        {
            var a = entities[i];
            if (a.Brain is null || a.Flock is not null) continue;
            List<Creature>? group = null;
            for (int j = 0; j < entities.Count; j++)
            {
                if (i == j) continue;
                var b = entities[j];
                if (b.Brain is null || b.Flock is not null) continue;
                if (Vec.HorizDist(a.Position, b.Position) <= joinR
                    && Genetics.Similarity(a, b) >= behavior.HerdKinThreshold)
                {
                    group ??= new List<Creature> { a };
                    group.Add(b);
                }
            }
            if (group is not null)
            {
                // Skip clusters too small to seed a herd.
                if (group.Count < behavior.FlockMinSize) continue;

                var centroid = Vector3.Zero;
                foreach (var m in group) centroid += m.Position;
                centroid /= group.Count;
                var flock = new Flock(new Vector3(centroid.X, groundFloor(centroid), centroid.Z));
                foreach (var m in group)
                {
                    flock.Members.Add(m);
                    m.Flock = flock;
                }
                flocks.Add(flock);
            }
        }

        // 4. Merge — fold a smaller flock into a nearby larger kin flock.
        for (int i = 0; i < flocks.Count; i++)
        {
            if (flocks[i].Members.Count == 0) continue;
            for (int j = i + 1; j < flocks.Count; j++)
            {
                var fa = flocks[i];
                var fb = flocks[j];
                if (fa.Members.Count == 0) break;
                if (fb.Members.Count == 0) continue;
                if (Vec.HorizDist(fa.Anchor, fb.Anchor) > mergeR) continue;
                if (Genetics.Similarity(fa.Members[0], fb.Members[0]) < behavior.HerdKinThreshold) continue;

                var (keep, drop) = fa.Members.Count >= fb.Members.Count ? (fa, fb) : (fb, fa);
                foreach (var m in drop.Members)
                {
                    m.Flock = keep;
                    keep.Members.Add(m);
                }
                drop.Members.Clear();
            }
        }
        flocks.RemoveAll(f => f.Members.Count == 0);
    }
}
