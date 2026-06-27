namespace Vivarium.Core;

/// <summary>
/// Pick a grown, pickable food item up off the ground into hand. Replaces the old food cheat toggle:
/// only lands when the player is empty-handed and a pickable item sits within reach. Plucks the item
/// (it regrows in place later) and records its type as the carried food.
/// </summary>
public sealed class PickUpInteraction : IPlayerInteraction
{
    public string Id => "pickup";

    public bool CanApply(in InteractionContext ctx) => !ctx.HoldingFood && ctx.FoodTarget is { Pickable: true };

    public void Apply(in InteractionContext ctx)
    {
        if (ctx.FoodTarget!.Pluck())
            ctx.Input.CarriedFood = ctx.FoodTarget.Def;
    }
}
