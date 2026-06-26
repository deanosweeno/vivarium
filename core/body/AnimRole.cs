namespace Vivarium.Core;

/// <summary>
/// How the cosmetic animation layer (<c>scripts/CreatureVisual</c>) moves a <see cref="BodyPart"/>.
/// Pure data here; the actual motion (squash, oscillation, look-at) lives in the engine layer
/// and never feeds back into the deterministic sim.
/// </summary>
public enum AnimRole
{
    /// <summary>No procedural motion — rigidly fixed at its socket.</summary>
    Static,

    /// <summary>The core body: bobs and squashes/stretches with movement and landings.</summary>
    Body,

    /// <summary>A limb (leg/arm/fin): oscillates by <see cref="BodyPart.Phase"/>/<see cref="BodyPart.Freq"/> for a walk cycle.</summary>
    Limb,

    /// <summary>A tail: sways side to side.</summary>
    Tail,

    /// <summary>The head: turns to look toward the creature's current focus / heading.</summary>
    Head,

    /// <summary>An eye: follows the head's look-at.</summary>
    Eye,
}
