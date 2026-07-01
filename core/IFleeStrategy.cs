using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Composable per-creature flee-from-player strategy. All flee behavior is data-driven through
/// this interface — safe distance, flee direction, flock-flee policy. Each creature type
/// (sheep, deer, sloth, …) injects its own implementation, so flee behavior can be changed
/// per-creature and reused across types without touching the brain or flock code.
///
/// Sprint speed during flee is owned by <see cref="CreatureTraits.SprintSpeed"/>, not by this
/// interface — so one creature type can share a flee strategy but sprint at different speeds
/// tuned purely through its data.
/// </summary>
public interface IFleeStrategy
{
    /// <summary>
    /// Whether the player is currently a threat to this creature. A skittish sheep
    /// delegates fully to this so the brain never hardcodes holding-food or bond logic.
    /// </summary>
    /// <param name="holdingFood">Whether the player is carrying food in hand.</param>
    /// <param name="affection">This creature's bond with the player [0,1].</param>
    bool IsPlayerThreat(bool holdingFood, float affection);

    /// <summary>
    /// Horizontal distance (arena units) the creature must reach from the player to feel safe.
    /// When the player is beyond this distance the flee latch releases. Up to a future design
    /// to make this radius-based vs. line-of-sight; for v1 it's a simple distance.
    /// </summary>
    float SafeDistance { get; }

    /// <summary>
    /// Where to flee toward, or null to flee directly away from the player.
    /// An isolated sheep returns the nearest flock anchor; a creature with no refuge returns
    /// null and simply runs away from the player.
    /// </summary>
    /// <param name="self">This creature's position.</param>
    /// <param name="player">The player's position.</param>
    /// <param name="nearestFlock">Nearest kin flock anchor, or null if none in the world.</param>
    Vector3? GetFleeTarget(Vector3 self, Vector3 player, Vector3? nearestFlock);

    /// <summary>
    /// Whether this creature-type's flocks should flee as a group when any member detects
    /// a player threat. True for sheep; false for solitary creatures whose "flocks" are just
    /// loose aggregations.
    /// </summary>
    bool FlockFleesAsGroup { get; }

    /// <summary>
    /// When true, flee-from-player is an unconditional override — it preempts the
    /// entire scoring loop and fires immediately whenever the player is a threat and
    /// the creature is isolated (not in a flock). When false, flee competes in the
    /// normal action table via scoring.
    /// </summary>
    bool FleeOverridesAll { get; }
}
