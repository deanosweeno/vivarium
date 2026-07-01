using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Decorator placement strategy: after the inner strategy resolves a position,
/// checks for overlap with existing entities. On overlap, generates a new random
/// desired position and retries through the inner strategy, up to
/// <paramref name="MaxAttempts"/> times. Falls back to the last position if all
/// attempts fail.
///
/// Because it passes every retry through the inner strategy, composed chains like
/// <c>OverlapAvoiding(BiomeFiltered(ArenaClampPlacement))</c> preserve both
/// constraints on every attempt.
/// </summary>
public sealed class OverlapAvoidingPlacement : IPlacementStrategy
{
    private readonly IPlacementStrategy _inner;
    private readonly int _maxAttempts;

    public OverlapAvoidingPlacement(IPlacementStrategy inner, int maxAttempts = 10)
    {
        _inner = inner;
        _maxAttempts = maxAttempts;
    }

    public Vector3 Place(Vector3 desired, CreatureTraits traits, PlacementContext ctx)
    {
        float radius = traits.Radius;
        float minDist = radius * 2f;
        float originalY = desired.Y;

        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            var pos = _inner.Place(desired, traits, ctx);
            if (!OverlapsAny(pos, minDist, ctx.Existing))
                return pos;

            // Try a random position within the arena as the next desired position.
            // Keep the original Y (terrain height) so creatures don't spawn underground/underwater.
            // The inner strategy (e.g. BiomeFiltered) will re-validate it.
            desired = new Vector3(
                (float)(ctx.Rng.NextDouble() * (ctx.Arena.MaxX - ctx.Arena.MinX - radius * 2) + ctx.Arena.MinX + radius),
                originalY,
                (float)(ctx.Rng.NextDouble() * (ctx.Arena.MaxZ - ctx.Arena.MinZ - radius * 2) + ctx.Arena.MinZ + radius));
        }

        return _inner.Place(desired, traits, ctx);
    }

    private static bool OverlapsAny(Vector3 position, float minDist, IReadOnlyList<Creature> existing)
    {
        foreach (var entity in existing)
        {
            if ((entity.Position - position).Length() < minDist)
                return true;
        }
        return false;
    }
}
