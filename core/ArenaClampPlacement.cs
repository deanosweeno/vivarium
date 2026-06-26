using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Leaf placement strategy: clamps the desired position to arena bounds with a
/// radius margin. Stateless — share a single instance via <see cref="Instance"/>.
/// </summary>
public sealed class ArenaClampPlacement : IPlacementStrategy
{
    public static readonly ArenaClampPlacement Instance = new();

    public Vector3 Place(Vector3 desired, CreatureTraits traits, PlacementContext ctx)
        => ctx.Arena.Clamp(desired, traits.Radius);
}
