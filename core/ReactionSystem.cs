namespace Vivarium.Core;

/// <summary>
/// Resolves the non-interaction <see cref="ReactionKind"/> tells — player-threat onset, a newly
/// curious approach, and a satisfied held need — from one tick's action/sense transition. Pure
/// function of (previous, current), mirroring <see cref="NeedBroadcast"/>. The interaction verbs
/// (Feed/Soothe/Play) set <see cref="ReactionKind.Happy"/>/<see cref="ReactionKind.Dislike"/>
/// directly; this covers the tells that aren't tied to a player interaction.
/// </summary>
public static class ReactionSystem
{
    /// <summary>
    /// Resolve the reaction (if any) for this tick, or null when nothing new happened.
    /// </summary>
    public static ReactionKind? ResolveTransition(
        BehaviorAction? previousAction, BehaviorAction? currentAction,
        in SenseContext previousSenses, in SenseContext currentSenses)
    {
        // Startled: the player just became a threat to this (isolated) creature.
        if (!previousSenses.PlayerPanic && currentSenses.PlayerPanic)
            return ReactionKind.Startled;

        if (currentAction is null || ReferenceEquals(currentAction, previousAction))
            return null;

        // Content: the action we just left was holding on a need (Rest/Forage) that is no
        // longer active — the same condition UtilityBrain's anti-dither latch checks, so
        // "content" fires exactly when the need that was gripping the creature let go.
        if (previousAction?.HoldWhile is { } latch && latch.Active(previousSenses) && !latch.Active(currentSenses))
            return ReactionKind.Content;

        // Curious: the brain just committed to closing a social distance.
        if (currentAction.Steering is SteeringKind.Approach or SteeringKind.SeekFlock)
            return ReactionKind.Curious;

        return null;
    }
}
