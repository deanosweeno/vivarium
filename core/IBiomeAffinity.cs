namespace Vivarium.Core;

/// <summary>
/// How comfortable a creature is in a given biome — pure data contract.
/// 1.0 = preferred, 0.0 = hostile. The <see cref="Simulator"/> reads this
/// to compute a biome gradient push; the brain never sees it (biome steering
/// is physics, not decision-making). Each creature type gets its own
/// implementation wired via <see cref="CreatureTraits"/>.
/// </summary>
public interface IBiomeAffinity
{
    /// <summary>Comfort score for the given biome in [0,1].</summary>
    float Comfort(Biome biome);
}
