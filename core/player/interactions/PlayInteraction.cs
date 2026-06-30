namespace Vivarium.Core;

/// <summary>
/// Play (lively pet) with the nearest creature: relieves boredom and deepens the bond. Gated behind
/// <see cref="BehaviorConfig.PartialBondThreshold"/> like <see cref="SootheInteraction"/>. Energetic
/// flavor — bond gain scales with the creature's <see cref="Drives.PlayCuddle"/> (high = loves play),
/// so a lively creature bonds harder over play than over soothing. Mismatch still helps (floored).
/// </summary>
public sealed class PlayInteraction : IPlayerInteraction
{
    public string Id => "play";

    public bool CanApply(in InteractionContext ctx) =>
        ctx.Target is not null && ctx.Target.Needs.Affection >= ctx.Config.PartialBondThreshold;

    public void Apply(in InteractionContext ctx)
    {
        var target = ctx.Target!;
        var n = target.Needs;
        n.Boredom -= ctx.Config.PlayBoredomRelief;
        // Energetic flavor: match = PlayCuddle (1 = loves lively play).
        float match = target.Drives.PlayCuddle;
        n.Affection += ctx.Config.PlayBond * FlavorMatch.Multiplier(match, ctx.Config);
        target.LastReaction = CreatureReaction.Happy(match, target.LastReaction);
    }
}
