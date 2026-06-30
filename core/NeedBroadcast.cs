namespace Vivarium.Core;

/// <summary>
/// The single player-lane need a creature is currently "asking" the player to fill — the value
/// behind the thought-bubble above its head. None when the creature has nothing the player can
/// usefully act on right now.
/// </summary>
public enum BroadcastNeed
{
    None,
    Hunger,
    Boredom,
    Affection,
}

/// <summary>
/// Resolves which need a creature broadcasts to the player each tick. Pure and deterministic
/// (no RNG, no clock) so it stays unit-testable and reproducible.
///
/// The need model is shared with the AI, tagged by <i>who can satisfy it</i>:
/// <list type="bullet">
///   <item>Affection — <b>player only</b>: the creature can never self-satisfy it, so it is the
///         standing reason a creature wants the player around. Always a candidate until the bond
///         is full.</item>
///   <item>Hunger — <b>both</b>: surfaced only when the creature is <i>not</i> already foraging
///         (i.e. it can't self-satisfy right now — no food path), so a creature that feeds itself
///         never begs.</item>
///   <item>Boredom — <b>both</b>: surfaced only when the creature is <i>not</i> already frolicking.</item>
///   <item>Fatigue / Temperature — <b>self</b>: never broadcast; the creature rests / seeks
///         shade on its own.</item>
/// </list>
/// Among the active candidates the most urgent wins, so a starving creature out-shouts a mild
/// want of company, but an otherwise-content creature still asks to bond.
/// </summary>
public static class NeedBroadcast
{
    /// <param name="needs">The creature's current need state.</param>
    /// <param name="isForaging">True when the brain's active action is Forage (self-satisfying hunger).</param>
    /// <param name="isFrolicking">True when the brain's active action is Frolic (self-satisfying boredom).</param>
    /// <param name="cfg">Tunable broadcast thresholds.</param>
    public static BroadcastNeed Resolve(CreatureNeeds needs, bool isForaging, bool isFrolicking, BehaviorConfig cfg)
    {
        BroadcastNeed best = BroadcastNeed.None;
        float bestUrgency = 0f;

        // Affection — player-only; the want fades as the bond approaches full.
        if (needs.Affection < cfg.FullBondThreshold)
        {
            float urgency = cfg.FullBondThreshold - needs.Affection;
            if (urgency > bestUrgency) { bestUrgency = urgency; best = BroadcastNeed.Affection; }
        }

        // Hunger — only when the creature isn't already handling it by foraging.
        if (!isForaging && needs.Hunger > cfg.BroadcastHungerThreshold)
        {
            if (needs.Hunger > bestUrgency) { bestUrgency = needs.Hunger; best = BroadcastNeed.Hunger; }
        }

        // Boredom — only when the creature isn't already frolicking.
        if (!isFrolicking && needs.Boredom > cfg.BroadcastBoredomThreshold)
        {
            if (needs.Boredom > bestUrgency) { bestUrgency = needs.Boredom; best = BroadcastNeed.Boredom; }
        }

        return best;
    }
}
