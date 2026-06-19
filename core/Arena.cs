using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Defines the playable bounds of the vivarium.
/// Uses XZ-plane coordinates (X = world X, Y = world Z).
/// </summary>
public readonly struct Arena
{
    public Vector2 Center { get; }
    public Vector2 Size { get; }

    public float MinX => Center.X - Size.X / 2f;
    public float MaxX => Center.X + Size.X / 2f;
    public float MinZ => Center.Y - Size.Y / 2f;  // Y component stores Z
    public float MaxZ => Center.Y + Size.Y / 2f;

    public Arena(Vector2 center, Vector2 size)
    {
        Center = center;
        Size = size;
    }

    /// <summary>Check if a position is within the arena bounds.</summary>
    public bool Contains(Vector2 position)
    {
        return Contains(position, 0f);
    }

    /// <summary>Check if a circle of given radius fits within the arena bounds.</summary>
    public bool Contains(Vector2 position, float radius)
    {
        return position.X - radius >= MinX && position.X + radius <= MaxX
            && position.Y - radius >= MinZ && position.Y + radius <= MaxZ;
    }

    /// <summary>Clamp a position to stay within the arena bounds.</summary>
    public Vector2 Clamp(Vector2 position)
    {
        return Clamp(position, 0f);
    }

    /// <summary>Clamp a position so a circle of given radius stays within bounds.</summary>
    public Vector2 Clamp(Vector2 position, float radius)
    {
        return new Vector2(
            Math.Clamp(position.X, MinX + radius, MaxX - radius),
            Math.Clamp(position.Y, MinZ + radius, MaxZ - radius)
        );
    }

    /// <summary>
    /// If the position is at or past a boundary, reflect the velocity
    /// off that wall and clamp the position.
    /// </summary>
    public (Vector2 ClampedPosition, Vector2 ReflectedVelocity) Reflect(
        Vector2 position, Vector2 velocity)
    {
        return Reflect(position, velocity, 0f);
    }

    /// <summary>
    /// If a circle of given radius is at or past a boundary, reflect the velocity
    /// off that wall and clamp the position so the edge sits on the wall.
    /// </summary>
    public (Vector2 ClampedPosition, Vector2 ReflectedVelocity) Reflect(
        Vector2 position, Vector2 velocity, float radius)
    {
        var clamped = Clamp(position, radius);
        var reflected = velocity;

        if (position.X - radius <= MinX || position.X + radius >= MaxX)
            reflected.X = -reflected.X;
        if (position.Y - radius <= MinZ || position.Y + radius >= MaxZ)
            reflected.Y = -reflected.Y;

        return (clamped, reflected);
    }
}
