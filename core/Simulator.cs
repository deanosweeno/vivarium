using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Headless simulation container. Owns the entity list, arena, and RNG.
/// Call Tick(delta) each frame; read Blobs to sync visuals.
/// </summary>
public sealed class Simulator
{
    public List<Blob> Blobs { get; } = new();
    public Arena Arena { get; }
    public Random Rng { get; }

    public int BlobCount => Blobs.Count;

    public Simulator(Arena arena, int seed = 0)
    {
        Arena = arena;
        Rng = new Random(seed);
    }

    /// <summary>
    /// Spawn a new blob at the given position with a random pastel color.
    /// The position is clamped to arena bounds (with radius margin) and
    /// retried up to 10 times if it overlaps an existing blob.
    /// </summary>
    public Blob SpawnBlob(Vector2 position)
    {
        var clamped = Arena.Clamp(position, Blob.Radius);

        // Avoid overlapping existing blobs
        float minDist = Blob.Radius * 2f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OverlapsAny(clamped, minDist))
                break;

            // Try a random position within the arena
            float rx = (float)(Rng.NextDouble() * (Arena.MaxX - Arena.MinX - Blob.Radius * 2) + Arena.MinX + Blob.Radius);
            float rz = (float)(Rng.NextDouble() * (Arena.MaxZ - Arena.MinZ - Blob.Radius * 2) + Arena.MinZ + Blob.Radius);
            clamped = new Vector2(rx, rz);
        }

        var (r, g, b) = Blob.RandomPastelColor(Rng);
        var blob = new Blob(clamped, r, g, b, Rng);
        Blobs.Add(blob);
        return blob;
    }

    private bool OverlapsAny(Vector2 position, float minDist)
    {
        foreach (var blob in Blobs)
        {
            if ((blob.Position - position).Length() < minDist)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Advance all blobs by <paramref name="delta"/> seconds,
    /// then resolve blob-to-blob overlaps.
    /// </summary>
    public void Tick(double delta)
    {
        foreach (var blob in Blobs)
        {
            blob.Tick(delta, Arena, Rng);
        }

        ResolveBlobCollisions();
    }

    private void ResolveBlobCollisions()
    {
        float minDist = Blob.Radius * 2f; // 1.0f

        for (int i = 0; i < Blobs.Count; i++)
        {
            for (int j = i + 1; j < Blobs.Count; j++)
            {
                var a = Blobs[i];
                var b = Blobs[j];

                var delta = a.Position - b.Position;
                float distance = delta.Length();

                if (distance >= minDist)
                    continue;

                if (distance < 1e-6f)
                {
                    // Same position — nudge apart along a fixed axis
                    delta = new Vector2(0.001f, 0.0f);
                    distance = delta.Length();
                }

                float overlap = minDist - distance;
                var axis = delta / distance;
                var push = axis * (overlap / 2f);

                a.Position += push;
                b.Position -= push;
            }
        }
    }
}
