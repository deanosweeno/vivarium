namespace Vivarium.Core;

/// <summary>
/// Drop the held food, emptying the hand — the symmetric off-path to <see cref="PickUpInteraction"/>
/// now that the cheat toggle is gone.
///
/// Phase-1: simply clears the carried item. TODO: spawn a fresh ground <see cref="FoodItem"/> at the
/// player's feet so dropped food returns to the world to be re-picked or grazed.
/// </summary>
public sealed class PlaceFoodInteraction : IPlayerInteraction
{
    public string Id => "place";

    public bool CanApply(in InteractionContext ctx) => ctx.HoldingFood;

    public void Apply(in InteractionContext ctx) => ctx.Input.CarriedFood = null;
}
