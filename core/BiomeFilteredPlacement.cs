using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Decorator placement strategy: after the inner strategy resolves a position,
/// checks that it falls within a <see cref="TargetBiome"/>. If not, the original
/// desired position is jittered within <see cref="JitterRadius"/> and retried
/// through the inner strategy up to <see cref="MaxRetries"/> times. Falls back
/// to the last position if all retries fail.
///
/// Composes with <see cref="OverlapAvoidingPlacement"/> to guarantee biome
/// constraints survive overlap retries.
/// </summary>
public sealed class BiomeFilteredPlacement : IPlacementStrategy
{
    private readonly IPlacementStrategy _inner;
    private readonly MapData _map;
    private readonly float _jitterRadius;
    private readonly int _maxRetries;

    public Biome TargetBiome { get; }

    public BiomeFilteredPlacement(
        IPlacementStrategy inner,
        MapData map,
        Biome targetBiome,
        int maxRetries = 20,
        float jitterRadius = 2f)
    {
        _inner = inner;
        _map = map;
        TargetBiome = targetBiome;
        _maxRetries = maxRetries;
        _jitterRadius = jitterRadius;
    }

    public Vector3 Place(Vector3 desired, CreatureTraits traits, PlacementContext ctx)
    {
        var saveDesired = desired;

        for (int i = 0; i < _maxRetries; i++)
        {
            var pos = _inner.Place(desired, traits, ctx);
            if (_map.BiomeAt(pos) == TargetBiome)
                return pos;

            // Jitter the original desired position for the next attempt.
            desired = new Vector3(
                saveDesired.X + (float)(ctx.Rng.NextDouble() * 2 - 1) * _jitterRadius,
                saveDesired.Y,
                saveDesired.Z + (float)(ctx.Rng.NextDouble() * 2 - 1) * _jitterRadius);
        }

        return _inner.Place(desired, traits, ctx); // fallback: last try
    }
}
