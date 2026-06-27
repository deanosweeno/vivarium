using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Creates the player-controlled <see cref="Blob"/>: gold color, <see cref="PlayerInputMode"/>
/// movement, no brain (input drives movement directly). The factory that previously lived in
/// <see cref="Simulator.SpawnPlayer"/>.
/// </summary>
public sealed class PlayerFactory : ICreatureFactory
{
    public static readonly PlayerFactory Instance = new();

    public Creature Create(
        Vector3 position,
        CreatureTraits traits,
        IMovementMode? movement,
        Drives drives,
        PlacementContext ctx)
    {
        var input = new PlayerInputMode();
        return new Blob(position, 1f, 0.84f, 0.2f, ctx.Rng, traits: traits, drives: drives)
        {
            Movement = input,
            Brain = null, // input drives movement directly
            IsPlayer = true,
        };
    }
}
