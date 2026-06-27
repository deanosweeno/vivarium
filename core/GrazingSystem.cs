using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Resolves grazing: each creature whose current action permits it eats the nearest available food
/// within eat range, draining the item and lowering Hunger by the nutrition consumed this tick.
/// Extracted from the Simulator as a pure function of (entities, food query, range). Grazing
/// eligibility is declared by the action's <see cref="GrazingMode"/> — the system doesn't care
/// which specific <see cref="SteeringKind"/> it is. Deterministic.
/// </summary>
public static class GrazingSystem
{
    /// <summary>
    /// Run one grazing tick over <paramref name="entities"/>. <paramref name="nearestFood"/> supplies
    /// the world's food query; <paramref name="hasFood"/> short-circuits when the world is foodless.
    /// </summary>
    public static void Resolve(
        double delta,
        IReadOnlyList<Creature> entities,
        bool hasFood,
        FoodConfig foodSpawn,
        Func<Vector3, HashSet<string>?, (FoodItem? Item, float Dist)> nearestFood)
    {
        if (!hasFood) return;
        float dt = (float)delta;

        foreach (var entity in entities)
        {
            var action = entity.Brain?.Current;
            bool canGraze = action?.Grazing switch
            {
                GrazingMode.Always => true,
                GrazingMode.WhenHungry => entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold,
                _ => false
            };
            if (!canGraze) continue;

            var (item, dist) = nearestFood(entity.Position, entity.Diet);
            if (item is null) continue;

            float eatRange = entity.Traits.Radius + foodSpawn.EatRange;
            if (dist > eatRange) continue;

            entity.Needs.Hunger -= item.Bite(dt);
            entity.Needs.Clamp();
        }
    }
}
