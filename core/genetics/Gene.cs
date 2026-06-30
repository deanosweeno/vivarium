namespace Vivarium.Core;

/// <summary>
/// Unified gene type — a gene may carry visual payload (<see cref="Parts"/>), stat payload
/// (<see cref="Pins"/>), or both (e.g. wool: a visible coat part plus a warmth stat pin).
/// No subclasses; <see cref="Kind"/> distinguishes the one required Base gene from the
/// many optional Specialty genes in a <see cref="Genome"/>.
/// </summary>
public sealed class Gene
{
    public required string Id { get; init; }
    public required GeneKind Kind { get; init; }
    public required Rarity Rarity { get; init; }
    public required int Tier { get; init; }
    public required bool Visible { get; init; }
    public IReadOnlyList<BodyPart>? Parts { get; init; }
    public IReadOnlyList<StatPin>? Pins { get; init; }
    public required string SourceSpecies { get; init; }
}
