namespace Vivarium.Core;

/// <summary>
/// Soothe (calm pet) the nearest creature: lets it rest (relieves fatigue) and deepens the bond.
/// Gated behind <see cref="BehaviorConfig.PartialBondThreshold"/> — a wild creature won't be handled.
/// Calm flavor — bond gain scales with the <i>inverse</i> of <see cref="Drives.PlayCuddle"/> (low =
/// loves cuddling), the opposite axis to <see cref="PlayInteraction"/>. Mismatch still helps (floored).
/// </summary>
public sealed class SootheInteraction : IPlayerInteraction
{
    public string Id => "soothe";

    public bool CanApply(in InteractionContext ctx) =>
        ctx.Target is not null && ctx.Target.Needs.Affection >= ctx.Config.PartialBondThreshold;

    public void Apply(in InteractionContext ctx)
    {
        var target = ctx.Target!;
        var n = target.Needs;
        n.Fatigue -= ctx.Config.SootheFatigueRelief;
        // Calm flavor: match = 1 - PlayCuddle (1 = loves cuddling).
        float match = 1f - target.Drives.PlayCuddle;
        n.Affection += ctx.Config.SootheBond * FlavorMatch.Multiplier(match, ctx.Config);
        target.LastReaction = match < 0.5f
            ? CreatureReaction.Dislike(1f - match, target.LastReaction)
            : CreatureReaction.Happy(match, target.LastReaction);
    }
}
