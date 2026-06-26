using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// One placeholder part of a creature's body, positioned at a rest <see cref="Socket"/> and
/// animated per its <see cref="Role"/>. This is the data seed of the future <c>PartGene</c>:
/// <see cref="Slot"/>→<c>PartGene.slot</c>, <see cref="Shape"/>/<see cref="Size"/>/<see cref="Tint"/>
/// →<c>PartGene.mesh</c>/<c>visualMods</c>, <see cref="Socket"/>→<c>PartGene.socket</c>.
/// Pure data (no Godot) so a body plan stays deterministic and testable in core.
/// </summary>
public sealed class BodyPart
{
    /// <summary>Which body slot this part fills.</summary>
    public PartSlot Slot { get; init; }

    /// <summary>Placeholder primitive mesh the visual layer builds.</summary>
    public ShapePrimitive Shape { get; init; } = ShapePrimitive.Sphere;

    /// <summary>Part dimensions (interpreted per <see cref="Shape"/> by the visual layer).</summary>
    public Vector3 Size { get; init; } = Vector3.One;

    /// <summary>Rest offset of the part from the creature's origin, in model units.</summary>
    public Vector3 Socket { get; init; } = Vector3.Zero;

    /// <summary>Display color as a "#rrggbb" hex string (read by the Godot visual).</summary>
    public string Tint { get; init; } = "#FFFFFF";

    /// <summary>How the cosmetic animation layer moves this part.</summary>
    public AnimRole Role { get; init; } = AnimRole.Static;

    /// <summary>Oscillator phase offset (radians) — e.g. opposite legs use 0 and π.</summary>
    public float Phase { get; init; }

    /// <summary>Oscillator frequency (radians/sec) for <see cref="AnimRole.Limb"/>/<see cref="AnimRole.Tail"/> motion.</summary>
    public float Freq { get; init; }
}
