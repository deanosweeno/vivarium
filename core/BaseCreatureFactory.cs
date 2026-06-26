using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Creates a plain <see cref="Creature"/> with the given movement mode
/// (defaults to <see cref="WalkMode"/>). No color, no brain — the simplest
/// creature factory.
/// </summary>
public sealed class BaseCreatureFactory : ICreatureFactory
{
    public static readonly BaseCreatureFactory Instance = new();

    public Creature Create(
        Vector3 position,
        CreatureTraits traits,
        IMovementMode? movement,
        Drives drives,
        PlacementContext ctx)
        => new(position, traits, movement ?? new WalkMode(), drives);
}
