namespace Vivarium.Core;

/// <summary>
/// A body-plan slot. A subset of the locked ~10-slot model (see
/// docs/features/art-and-animation.md) — enough to assemble the first creature, and the
/// seed of the future <c>PartGene.slot</c>. One creature carries one part per slot, except
/// inherently-paired parts (eyes, legs) which the data lists explicitly.
/// </summary>
public enum PartSlot
{
    Core,
    Head,
    Eyes,
    Locomotion,
    Appendage,
    Tail,
    Surface,
}
