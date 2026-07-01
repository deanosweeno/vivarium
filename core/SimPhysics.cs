using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Ground placement and entity-entity collision resolution, extracted from the Simulator so the
/// two identical ground-placement call sites (pre- and post-collision) share one implementation.
/// Deterministic — reads positions/velocities only, no RNG.
/// </summary>
public static class SimPhysics
{
    /// <summary>
    /// Rest <paramref name="entity"/> on the terrain surface under it. Terrain-bound creatures
    /// (GravityScale 0) hug the surface going up and down; gravity-driven creatures are stopped
    /// (and their fall-velocity zeroed) only when they've sunk below it.
    /// </summary>
    public static void PlaceOnGround(Creature entity, Func<Vector3, float> groundFloor)
    {
        float floor = groundFloor(entity.Position) + entity.Traits.Radius;
        if (entity.Traits.GravityScale == 0f)
        {
            if (entity.Position.Y != floor)
                entity.Position = new Vector3(entity.Position.X, floor, entity.Position.Z);
        }
        else if (entity.Position.Y < floor)
        {
            entity.Position = new Vector3(entity.Position.X, floor, entity.Position.Z);
            entity.Velocity = new Vector3(entity.Velocity.X, 0f, entity.Velocity.Z);
        }
    }

    /// <summary>
    /// Axis-separated tile collision: given where an agent was (<paramref name="prev"/>, assumed
    /// walkable) and where it wants to be (<paramref name="next"/>), return the furthest legal
    /// position that does not enter a non-walkable cell. The X and Z moves are tested independently
    /// so motion parallel to an obstacle survives (the agent <em>slides</em> along a rock/lake face)
    /// while motion into it is cancelled. Y is carried through untouched.
    ///
    /// Robustness: if <paramref name="prev"/> is itself on a blocked cell (e.g. spawned on rock),
    /// the move is allowed as-is rather than locking the agent in place.
    /// Deterministic — reads the grid only, no RNG.
    /// </summary>
    public static Vector3 SlideAgainstTerrain(Vector3 prev, Vector3 next, MapData map)
    {
        // If we started on a blocked cell, don't trap the agent — let it move freely out.
        if (!map.IsWalkableWorld(prev))
            return next;

        float x = prev.X;
        float z = prev.Z;

        // Try the X move first (keeping Z at the old, known-good value), then the Z move.
        var tryX = new Vector3(next.X, next.Y, prev.Z);
        if (map.IsWalkableWorld(tryX)) x = next.X;

        var tryZ = new Vector3(x, next.Y, next.Z);
        if (map.IsWalkableWorld(tryZ)) z = next.Z;

        return new Vector3(x, next.Y, z);
    }

    /// <summary>
    /// Apply <see cref="SlideAgainstTerrain"/> to a creature that just integrated its movement,
    /// zeroing the horizontal velocity component on any axis that got blocked so accumulated
    /// momentum doesn't keep re-ramming the obstacle next tick (mirrors the wall/entity
    /// jitter-cancel elsewhere in this class).
    /// </summary>
    public static void ResolveTerrainCollision(Creature entity, Vector3 prevPos, MapData map)
    {
        var resolved = SlideAgainstTerrain(prevPos, entity.Position, map);
        if (resolved == entity.Position)
            return;

        var v = entity.Velocity;
        if (resolved.X != entity.Position.X) v.X = 0f;
        if (resolved.Z != entity.Position.Z) v.Z = 0f;
        entity.Position = resolved;
        entity.Velocity = v;
    }

    /// <summary>
    /// Resolve sphere-sphere overlaps for all entity pairs. Each pair is pushed apart by half
    /// the overlap distance, and any inward velocity that drove them together is cancelled so
    /// momentum doesn't re-ram them next tick (jitter).
    /// </summary>
    public static void ResolveEntityCollisions(IReadOnlyList<Creature> entities)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                var a = entities[i];
                var b = entities[j];
                float minDist = a.Traits.Radius + b.Traits.Radius;

                // Horizontal overlap check before push, so we can also strip the inward velocity
                // that drove them together — otherwise momentum re-rams them next tick (jitter).
                var sep = new Vector3(a.Position.X - b.Position.X, 0f, a.Position.Z - b.Position.Z);
                float horiz = sep.Length();
                bool overlapping = horiz < minDist && horiz > 1e-6f;

                (a.Position, b.Position) = PushApart(a.Position, b.Position, minDist);

                if (overlapping)
                {
                    var axis = sep / horiz;                 // unit vector from b toward a, XZ plane
                    KillInwardVelocity(a, axis);            // a moving toward b (-axis) → cancel it
                    KillInwardVelocity(b, -axis);
                }
            }
        }
    }

    /// <summary>
    /// Remove the component of a creature's horizontal velocity that points along
    /// <paramref name="outwardAxis"/> negated — i.e. any speed driving it <em>into</em> the body it
    /// just collided with. Leaves Y untouched (gravity) and any sideways/separating motion intact,
    /// so a settled pair stops ramming instead of bouncing. Deterministic: velocities only, no RNG.
    /// </summary>
    private static void KillInwardVelocity(Creature c, Vector3 outwardAxis)
    {
        var v = c.Velocity;
        var horiz = new Vector3(v.X, 0f, v.Z);
        float inward = Vector3.Dot(horiz, outwardAxis);   // <0 means moving toward the other body
        if (inward < 0f)
        {
            horiz -= outwardAxis * inward;                // cancel only the inward component
            c.Velocity = new Vector3(horiz.X, v.Y, horiz.Z);
        }
    }

    /// <summary>
    /// Push two positions apart if they overlap, each by half the overlap.
    /// If the distance is near-zero, nudges apart on a fixed axis.
    /// </summary>
    private static (Vector3 A, Vector3 B) PushApart(Vector3 a, Vector3 b, float minDist)
    {
        var delta = a - b;
        float distance = delta.Length();

        if (distance >= minDist)
            return (a, b);

        if (distance < 1e-6f)
        {
            delta = new Vector3(0.001f, 0f, 0f);
            distance = delta.Length();
        }

        float overlap = minDist - distance;
        var axis = delta / distance;
        var push = axis * (overlap / 2f);

        return (a + push, b - push);
    }
}
