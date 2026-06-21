using Xunit;

namespace Vivarium.Core.Tests;

public class HeightNoiseTests
{
    [Fact]
    public void Fbm_SameSeedAndCoords_IsDeterministic()
    {
        var a = new HeightNoise(1234);
        var b = new HeightNoise(1234);

        for (int i = 0; i < 20; i++)
        {
            float x = i * 0.37f;
            float z = i * 0.91f;
            Assert.Equal(a.Fbm(x, z, 4), b.Fbm(x, z, 4), 6);
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentNoise()
    {
        var a = new HeightNoise(1);
        var b = new HeightNoise(2);

        bool anyDifferent = false;
        for (int i = 0; i < 20 && !anyDifferent; i++)
            if (MathF.Abs(a.Fbm(i * 0.5f, i * 0.5f, 4) - b.Fbm(i * 0.5f, i * 0.5f, 4)) > 1e-4f)
                anyDifferent = true;

        Assert.True(anyDifferent);
    }

    [Fact]
    public void Fbm_StaysInUnitRange()
    {
        var noise = new HeightNoise(99);
        for (int i = 0; i < 200; i++)
        {
            float v = noise.Fbm(i * 0.13f, i * 0.27f, 5);
            Assert.InRange(v, 0f, 1f);
        }
    }

    [Fact]
    public void ValueNoise_IsContinuous_NoLargeJumpsBetweenNearbyPoints()
    {
        var noise = new HeightNoise(7);
        float prev = noise.ValueNoise(0f, 0f);
        for (int i = 1; i < 100; i++)
        {
            float v = noise.ValueNoise(i * 0.01f, 0f); // tiny steps
            Assert.True(MathF.Abs(v - prev) < 0.1f, "noise should vary smoothly over small steps");
            prev = v;
        }
    }

    [Fact]
    public void Fbm_VariesAcrossSpace()
    {
        var noise = new HeightNoise(42);
        var seen = new HashSet<int>();
        for (int i = 0; i < 50; i++)
            seen.Add((int)(noise.Fbm(i * 1.7f, i * 2.3f, 4) * 1000));

        Assert.True(seen.Count > 5, "fBm should produce a spread of values across space");
    }
}
