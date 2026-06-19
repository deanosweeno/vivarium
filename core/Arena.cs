using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Defines the playable bounds of the vivarium in 3D space.
/// X = world X, Y = world Y (up), Z = world Z.
/// </summary>
public readonly struct Arena
{
    public Vector3 Center { get; }
    public Vector3 Size { get; }

    public float MinX => Center.X - Size.X / 2f;
    public float MaxX => Center.X + Size.X / 2f;
    public float MinY => Center.Y - Size.Y / 2f;
    public float MaxY => Center.Y + Size.Y / 2f;
    public float MinZ => Center.Z - Size.Z / 2f;
    public float MaxZ => Center.Z + Size.Z / 2f;

    public Arena(Vector3 center, Vector3 size)
    {
        Center = center;
        Size = size;
    }

    /// <summary>
    /// Create a ground arena: floor at Y=0, no ceiling, centered at XZ origin.
    /// </summary>
    public static Arena GroundArena(float width, float depth)
    {
        var halfMax = float.MaxValue / 2f;
        return new Arena(
            new Vector3(0f, halfMax, 0f),
            new Vector3(width, float.MaxValue, depth));
    }

    /// <summary>Check if a position is within the arena bounds.</summary>
    public bool Contains(Vector3 position)
    {
        return Contains(position, 0f);
    }

    /// <summary>Check if a sphere of given radius fits within the arena bounds.</summary>
    public bool Contains(Vector3 position, float radius)
    {
        return position.X - radius >= MinX && position.X + radius <= MaxX
            && position.Y - radius >= MinY && position.Y + radius <= MaxY
            && position.Z - radius >= MinZ && position.Z + radius <= MaxZ;
    }

    /// <summary>Clamp a position to stay within the arena bounds.</summary>
    public Vector3 Clamp(Vector3 position)
    {
        return Clamp(position, 0f);
    }

    /// <summary>Clamp a position so a sphere of given radius stays within bounds.</summary>
    public Vector3 Clamp(Vector3 position, float radius)
    {
        return new Vector3(
            Math.Clamp(position.X, MinX + radius, MaxX - radius),
            Math.Clamp(position.Y, MinY + radius, MaxY - radius),
            Math.Clamp(position.Z, MinZ + radius, MaxZ - radius)
        );
    }

    /// <summary>
    /// If the position is at or past a boundary, reflect the velocity
    /// off that wall and clamp the position.
    /// </summary>
    public (Vector3 ClampedPosition, Vector3 ReflectedVelocity) Reflect(
        Vector3 position, Vector3 velocity)
    {
        return Reflect(position, velocity, 0f);
    }

    /// <summary>
    /// If a sphere of given radius is at or past a boundary, reflect the velocity
    /// off that wall and clamp the position so the edge sits on the wall.
    /// </summary>
    public (Vector3 ClampedPosition, Vector3 ReflectedVelocity) Reflect(
        Vector3 position, Vector3 velocity, float radius)
    {
        var clamped = Clamp(position, radius);
        var reflected = velocity;

        if (position.X - radius <= MinX || position.X + radius >= MaxX)
            reflected.X = -reflected.X;
        if (position.Y - radius <= MinY || position.Y + radius >= MaxY)
            reflected.Y = -reflected.Y;
        if (position.Z - radius <= MinZ || position.Z + radius >= MaxZ)
            reflected.Z = -reflected.Z;

        return (clamped, reflected);
    }

    /// <summary>
    /// Clamp position and reflect velocity off XZ walls only.
    /// Does NOT touch Y — the caller handles vertical boundaries.
    /// Used by ground-based movement modes that stay on the XZ plane.
    /// Returns true if a wall was hit (so the caller can trigger direction changes).
    /// </summary>
    public bool ReflectXZ(ref Vector3 position, ref Vector3 velocity, float radius)
    {
        float minX = MinX + radius;
        float maxX = MaxX - radius;
        float minZ = MinZ + radius;
        float maxZ = MaxZ - radius;

        bool hit = false;

        if (position.X < minX) { position.X = minX; velocity.X = Math.Abs(velocity.X); hit = true; }
        else if (position.X > maxX) { position.X = maxX; velocity.X = -Math.Abs(velocity.X); hit = true; }
        if (position.Z < minZ) { position.Z = minZ; velocity.Z = Math.Abs(velocity.Z); hit = true; }
        else if (position.Z > maxZ) { position.Z = maxZ; velocity.Z = -Math.Abs(velocity.Z); hit = true; }

        return hit;
    }
}
