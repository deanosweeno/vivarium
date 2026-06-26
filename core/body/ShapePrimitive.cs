namespace Vivarium.Core;

/// <summary>
/// The primitive mesh shapes the visual layer knows how to build for a <see cref="BodyPart"/>.
/// Kept deliberately small — these are placeholder "socket puppet" parts (no Blender/glTF yet),
/// the seed of the future authored <c>PartGene.mesh</c>.
/// </summary>
public enum ShapePrimitive
{
    Sphere,
    Capsule,
    Box,
    Cylinder,
}
