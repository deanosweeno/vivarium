namespace Vivarium.Core;

/// <summary>
/// Soothe (calm pet) the nearest creature: lets it rest (relieves fatigue) and deepens the bond.
/// Gated behind <see cref="BehaviorConfig.PartialBondThreshold"/> — a wild creature won't be handled.
/// </summary>
public sealed class SootheInteraction : IPlayerInteraction
{
    public string Id => "soothe";

    public bool CanApply(in InteractionContext ctx) =>
        ctx.Target is not null && ctx.Target.Needs.Affection >= ctx.Config.PartialBondThreshold;

    public void Apply(in InteractionContext ctx)
    {
        var n = ctx.Target!.Needs;
        n.Fatigue -= ctx.Config.SootheFatigueRelief;
        n.Affection += ctx.Config.SootheBond;
    }
}
