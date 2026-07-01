using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// A creature that never treats the player as a threat — used for tame/passive types
/// (future predators that hunt rather than flee, or docile creatures like a sloth).
/// </summary>
public sealed class NeverFleeStrategy : IFleeStrategy
{
    public bool IsPlayerThreat(bool holdingFood, float affection) => false;
    public float SafeDistance => 0f;
    public Vector3? GetFleeTarget(Vector3 self, Vector3 player, Vector3? nearestFlock) => null;
    public bool FlockFleesAsGroup => false;
    public bool FleeOverridesAll => false;
}

/// <summary>
/// A creature that always treats the player as a threat, regardless of food or bond —
/// reserved for skittish future creature types that can never be tamed out of fleeing.
/// </summary>
public sealed class AlwaysFleeStrategy : IFleeStrategy
{
    public bool IsPlayerThreat(bool holdingFood, float affection) => true;
    public float SafeDistance => 5f;
    public Vector3? GetFleeTarget(Vector3 self, Vector3 player, Vector3? nearestFlock) => nearestFlock;
    public bool FlockFleesAsGroup => true;
    public bool FleeOverridesAll => true;
}

/// <summary>
/// Maps a <see cref="CreatureDef.FleeStrategy"/> name (from <c>creatures.json</c>) to an
/// <see cref="IFleeStrategy"/> instance, so a creature type's flee behavior is a data string
/// instead of a hardcoded Simulator-wide field. Unknown/absent names resolve to null — the
/// caller falls back to its own default strategy.
/// </summary>
public static class FleeStrategyRegistry
{
    /// <summary>
    /// Resolve a strategy name to an instance. <paramref name="bondThreshold"/> feeds
    /// <see cref="SheepFleeStrategy"/>'s taming gate (sourced from
    /// <see cref="InteractionConfig.PartialBondThreshold"/> so there's one authoritative value).
    /// </summary>
    public static IFleeStrategy? Resolve(string? name, float bondThreshold) => name?.ToLowerInvariant() switch
    {
        "sheep" => new SheepFleeStrategy(bondThreshold),
        "never" => new NeverFleeStrategy(),
        "always" => new AlwaysFleeStrategy(),
        _ => null,
    };
}
