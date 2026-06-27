namespace Vivarium.Core;

/// <summary>
/// Play (lively pet) with the nearest creature: relieves boredom and deepens the bond. Gated behind
/// <see cref="BehaviorConfig.PartialBondThreshold"/> like <see cref="SootheInteraction"/>.
/// </summary>
public sealed class PlayInteraction : IPlayerInteraction
{
    public string Id => "play";

    public bool CanApply(in InteractionContext ctx) =>
        ctx.Target is not null && ctx.Target.Needs.Affection >= ctx.Config.PartialBondThreshold;

    public void Apply(in InteractionContext ctx)
    {
        var n = ctx.Target!.Needs;
        n.Boredom -= ctx.Config.PlayBoredomRelief;
        n.Affection += ctx.Config.PlayBond;
    }
}
