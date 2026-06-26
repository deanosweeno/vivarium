namespace Vivarium.Core;

/// <summary>
/// A creature's complete tailored body: an ordered list of <see cref="BodyPart"/>s plus an
/// overall scale and a pastel palette for the visual unify pass. Held by <see cref="Creature.Body"/>
/// and consumed by the engine's <c>CreatureVisual</c> to assemble + animate the creature.
///
/// This is the data shape a future <c>Expressor</c> (genotype→phenotype) will produce, so the
/// visual/animation layer needs no rewrite when splicing lands. Loaded from
/// <c>assets/creatures.json</c> via <see cref="CreatureCatalog"/>.
/// </summary>
public sealed class BodyPlan
{
    /// <summary>Stable identifier referenced when spawning a creature.</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable name (tooling/debug).</summary>
    public string Name { get; init; } = "";

    /// <summary>Uniform scale applied to the whole assembled body.</summary>
    public float BaseScale { get; init; } = 1f;

    /// <summary>Primary pastel tint ("#rrggbb"); the visual nudges parts toward it to unify the look.</summary>
    public string PrimaryHex { get; init; } = "#FFFFFF";

    /// <summary>Secondary pastel tint ("#rrggbb") for accents.</summary>
    public string SecondaryHex { get; init; } = "#DDDDDD";

    /// <summary>The parts that make up the body, in assembly order.</summary>
    public IReadOnlyList<BodyPart> Parts { get; init; } = new List<BodyPart>();
}
