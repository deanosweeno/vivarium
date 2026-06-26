using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Creates and wires a <see cref="Creature"/> at the given position. Separates
/// entity construction (color, movement mode, brain assignment) from placement
/// strategy (clamp, overlap, biome filter).
/// </summary>
public interface ICreatureFactory
{
    /// <summary>
    /// Create a creature at <paramref name="position"/> with the given traits,
    /// movement mode, drives, and arena context. The position has already been
    /// resolved by a placement strategy.
    /// </summary>
    Creature Create(
        Vector3 position,
        CreatureTraits traits,
        IMovementMode? movement,
        Drives drives,
        PlacementContext ctx);
}
