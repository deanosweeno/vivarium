namespace Vivarium.Core;

/// <summary>
/// The expressed creature — what <see cref="Expressor"/> produces from a <see cref="Genome"/>.
/// Exactly the phenotype shape the rest of the sim already consumes: a visual
/// <see cref="BodyPlan"/> plus the simulation <see cref="CreatureTraits"/> and <see cref="Drives"/>.
/// Holds no genetics — once expressed, the spawn/visual path needs no knowledge of genes.
/// </summary>
public sealed record Phenotype(BodyPlan Body, CreatureTraits Traits, Drives Drives);
