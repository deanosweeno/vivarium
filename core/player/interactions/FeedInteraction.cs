namespace Vivarium.Core;

/// <summary>
/// Feed the nearest creature: needs food in hand, works on any creature (the primary trust-builder),
/// relieves hunger and raises the Affection bond, then consumes the held item. Magnitudes from
/// <see cref="BehaviorConfig"/>.
/// </summary>
public sealed class FeedInteraction : IPlayerInteraction
{
    public string Id => "feed";

    public bool CanApply(in InteractionContext ctx) => ctx.HoldingFood && ctx.Target is not null;

    public void Apply(in InteractionContext ctx)
    {
        var target = ctx.Target!;
        var n = target.Needs;
        n.Hunger -= ctx.Config.FeedHungerRelief;
        // Food is universally welcome — no flavor axis, so it always lands at full bond and reads
        // as a full-strength happy reaction.
        n.Affection += ctx.Config.FeedBond;
        target.LastReaction = CreatureReaction.Happy(1f, target.LastReaction);
        ctx.Input.CarriedFood = null;   // the item is eaten — empty the hand
    }
}
