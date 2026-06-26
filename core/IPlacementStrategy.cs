using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Resolves a desired spawn position into a valid placement within the arena,
/// applying any constraints (clamp, overlap avoidance, biome filtering, etc.).
/// Composable — decorators chain around a leaf strategy.
/// </summary>
public interface IPlacementStrategy
{
    /// <summary>
    /// Resolve <paramref name="desired"/> into a valid arena position, given the
    /// creature's traits and the current arena state.
    /// </summary>
    Vector3 Place(Vector3 desired, CreatureTraits traits, PlacementContext ctx);
}

/// <summary>
/// Read-only snapshot of the arena state needed by placement strategies.
/// A plain data carrier — no behavior, no interface needed.
/// </summary>
public sealed class PlacementContext
{
    public Arena Arena { get; }
    public IReadOnlyList<Creature> Existing { get; }
    public Random Rng { get; }

    public PlacementContext(Arena arena, IReadOnlyList<Creature> existing, Random rng)
    {
        Arena = arena;
        Existing = existing;
        Rng = rng;
    }
}
