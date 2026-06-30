namespace Vivarium.Core;

/// <summary>
/// Identifies where on the body a part sits — slot plus a discriminator for slots that
/// can carry more than one part (e.g. paired eyes, multi-limb locomotion).
/// Value-equality (record) so it works as a dictionary key.
/// </summary>
public readonly record struct Locus(PartSlot Slot, string Discriminator);
