using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Default sheep flee strategy. Sheep flee the player at a gallop unless the player is
/// holding food. Isolated sheep run toward the nearest kin flock; flocked sheep trigger
/// the whole flock to bolt as a group.
/// </summary>
public sealed class SheepFleeStrategy : IFleeStrategy
{
    // Affection at/above which a sheep no longer fears the player (it's been tamed). Sourced
    // from BehaviorConfig.PartialBondThreshold so the taming gate has one authoritative value;
    // the default mirrors that config default for the parameterless fallback construction.
    private readonly float _bondThreshold;

    public SheepFleeStrategy(float bondThreshold = 0.4f) => _bondThreshold = bondThreshold;

    public bool IsPlayerThreat(bool holdingFood, float affection)
        // Sheep fear the player unless food is offered OR the creature is bonded.
        => !holdingFood && affection < _bondThreshold;

    public float FleeSpeedMultiplier => 2.0f;        // 2× gallop panic
    public float SafeDistance => 5f;                  // same as default SenseRadius
    public bool FlockFleesAsGroup => true;
    public bool FleeOverridesAll => true;             // sheep panic overrides everything

    public Vector3? GetFleeTarget(Vector3 self, Vector3 player, Vector3? nearestFlock)
        // Isolated sheep flee toward the nearest flock for safety.
        // When no flock exists, returns null → the brain steers directly away from the player.
        => nearestFlock;
}
