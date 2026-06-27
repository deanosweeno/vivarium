using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// A single growable food object in the world — a lightweight entity, NOT a
/// <see cref="Creature"/> (no brain, needs, movement, or gravity). Creatures graze it down
/// over time; once depleted it stays gone for its type's <see cref="FoodDef.RespawnSeconds"/>,
/// then regrows in place. Owned and ticked by the <see cref="Simulator"/>.
/// </summary>
public sealed class FoodItem
{
    /// <summary>World position where this item grows.</summary>
    public Vector3 Position { get; init; }

    /// <summary>The food type — supplies nutrition, graze rate, respawn time, color.</summary>
    public FoodDef Def { get; init; } = FoodDef.Neutral("");

    /// <summary>Remaining fraction of the item, 1 = full, 0 = eaten. Grazed down by <see cref="Bite"/>.</summary>
    public float Amount { get; set; } = 1f;

    /// <summary>Whether the item can currently be eaten.</summary>
    public bool Available => Amount > 0f;

    /// <summary>Whether the player can pick this item up right now — its type permits it and it's grown.</summary>
    public bool Pickable => Def.Pickable && Available;

    /// <summary>Seconds left until a depleted item regrows. Counts down only while <see cref="Amount"/> is 0.</summary>
    public float RespawnTimer { get; set; }

    /// <summary>
    /// Graze for <paramref name="dt"/> seconds: drain <see cref="Amount"/> by
    /// <c>Def.GrazeRate·dt</c> (not below 0) and return the Hunger actually removed —
    /// <c>amountEaten · Def.Nutrition</c>. When the item empties, arms the respawn timer.
    /// </summary>
    public float Bite(float dt)
    {
        if (Amount <= 0f) return 0f;

        float eaten = MathF.Min(Amount, Def.GrazeRate * dt);
        Amount -= eaten;
        if (Amount <= 0f)
        {
            Amount = 0f;
            RespawnTimer = Def.RespawnSeconds;
        }
        return eaten * Def.Nutrition;
    }

    /// <summary>
    /// Pick the whole item up off the ground: empties it and arms the respawn timer so it regrows
    /// in place later (reuses the graze depletion machinery). Returns false — leaving the item
    /// untouched — when it isn't <see cref="Pickable"/>.
    /// </summary>
    public bool Pluck()
    {
        if (!Pickable) return false;
        Amount = 0f;
        RespawnTimer = Def.RespawnSeconds;
        return true;
    }

    /// <summary>
    /// Advance the respawn countdown for a depleted item; regrow to full when it elapses.
    /// No-op while the item still has food. Deterministic (time only).
    /// </summary>
    public void Regrow(float dt)
    {
        if (Amount > 0f) return;
        RespawnTimer -= dt;
        if (RespawnTimer <= 0f)
        {
            Amount = 1f;
            RespawnTimer = 0f;
        }
    }
}
