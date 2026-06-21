namespace Vivarium.Core;

/// <summary>
/// A small, pure, deterministic 2D <b>value-noise + fBm</b> source used to sculpt
/// terrain height. Given the same seed and coordinates it always returns the same
/// value — it uses an integer hash of the lattice point (no <see cref="Random"/>,
/// <c>DateTime</c>, <c>Random.Shared</c>, or threads at sample time), so generation
/// stays byte-reproducible.
///
/// It is deliberately its own unit (not inlined in <see cref="MapGenerator"/>) so the
/// height <i>shape</i> algorithm can be unit-tested and swapped (e.g. for Perlin/simplex)
/// without touching the generation pipeline — mirroring how <see cref="BiomeCatalog"/>
/// is an independent piece.
/// </summary>
public sealed class HeightNoise
{
    private readonly uint _seed;

    /// <summary>Create a noise source for the given seed.</summary>
    public HeightNoise(int seed) => _seed = unchecked((uint)seed);

    /// <summary>
    /// Fractal (fBm) noise at (<paramref name="x"/>, <paramref name="z"/>): sum
    /// <paramref name="octaves"/> layers of value noise, each at double the frequency
    /// and half the amplitude, normalized to roughly <c>[0, 1]</c>. Higher octaves add
    /// finer detail on top of the broad shape.
    /// </summary>
    public float Fbm(float x, float z, int octaves)
    {
        octaves = Math.Max(1, octaves);
        float sum = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxAmplitude = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * ValueNoise(x * frequency, z * frequency);
            maxAmplitude += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum / maxAmplitude;
    }

    /// <summary>
    /// Smooth value noise at a point, in <c>[0, 1]</c>: bilinear interpolation of the
    /// four surrounding integer-lattice hash values, with a smoothstep fade so the
    /// result has continuous slopes (no grid creasing).
    /// </summary>
    public float ValueNoise(float x, float z)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float fx = Fade(x - x0);
        float fz = Fade(z - z0);

        float h00 = Lattice(x0, z0);
        float h10 = Lattice(x0 + 1, z0);
        float h01 = Lattice(x0, z0 + 1);
        float h11 = Lattice(x0 + 1, z0 + 1);

        float top = h00 + (h10 - h00) * fx;
        float bottom = h01 + (h11 - h01) * fx;
        return top + (bottom - top) * fz;
    }

    /// <summary>Smoothstep fade (3t² − 2t³) for C1-continuous interpolation.</summary>
    private static float Fade(float t) => t * t * (3f - 2f * t);

    /// <summary>Deterministic hash of a lattice point + seed into <c>[0, 1]</c>.</summary>
    private float Lattice(int xi, int zi)
    {
        unchecked
        {
            uint h = _seed;
            h ^= (uint)xi * 0x9E3779B1u;
            h ^= (uint)zi * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0xD2B74407u;
            h ^= h >> 13;
            h *= 0x165667B1u;
            h ^= h >> 16;
            // Map to [0,1] using the full 32-bit range.
            return h / (float)uint.MaxValue;
        }
    }
}
