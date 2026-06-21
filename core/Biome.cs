namespace Vivarium.Core;

/// <summary>
/// Biome (terrain region) of a single map cell. A biome is a *region* axis,
/// independent of <see cref="Terrain"/> — a Grass cell in a Desert and a Grass
/// cell in a Forest share the same terrain but belong to different biomes, which
/// bias generation and affect creatures at runtime (rules live in data, see
/// <see cref="BiomeDef"/> / <see cref="BiomeCatalog"/>).
///
/// Values are stable and serialized to save files — add new biomes only by
/// appending; never reorder or renumber. The names here must match the
/// <c>Biome</c> field used in <c>assets/biomes.json</c>.
/// </summary>
public enum Biome
{
    /// <summary>Default temperate region.</summary>
    Plains = 0,
    /// <summary>Arid region — little water, harsh on creatures.</summary>
    Desert = 1,
    /// <summary>Wooded region — food-rich, slower movement.</summary>
    Forest = 2,
    /// <summary>Marshy region — water-heavy.</summary>
    Wetland = 3,
}
