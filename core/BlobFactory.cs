using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Creates a <see cref="Blob"/> with a random pastel color, <see cref="SteeringLocomotion"/>,
/// and a <see cref="UtilityBrain"/> wired to the shared <see cref="BehaviorConfig"/>.
/// The blob-specific construction that previously lived in <see cref="Simulator.SpawnBlob"/>.
/// </summary>
public sealed class BlobFactory : ICreatureFactory
{
    private readonly BehaviorConfig _behavior;
    private readonly Random _rng;

    public BlobFactory(BehaviorConfig behavior, Random rng)
    {
        _behavior = behavior;
        _rng = rng;
    }

    public Creature Create(
        Vector3 position,
        CreatureTraits traits,
        IMovementMode? movement,
        Drives drives,
        PlacementContext ctx)
    {
        var (r, g, b) = Blob.RandomPastelColor(ctx.Rng);
        var blob = new Blob(position, r, g, b, ctx.Rng, traits: traits, drives: drives)
        {
            Movement = movement ?? new SteeringLocomotion(),
            Brain = new UtilityBrain(_behavior),
        };
        return blob;
    }
}
