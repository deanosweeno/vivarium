using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// Animation seam for the player avatar (Godot side). The core sets a Godot-free
/// <see cref="PlayerState"/> each tick; an implementation maps that to a pose/clip on the visual.
/// This keeps animation out of <c>core/</c> and lets richer animators (AnimationPlayer-driven clip
/// sets, blend trees) drop in later without touching the simulation.
/// </summary>
public interface IPlayerAnimation
{
    /// <summary>Apply the given player state to the visual for this frame.</summary>
    /// <param name="root">The avatar's visual root node (its children are the body parts).</param>
    /// <param name="state">Idle/Walking/Interacting + last verb id, from the core.</param>
    /// <param name="delta">Frame time in seconds, for time-driven motion.</param>
    void Apply(Node3D root, PlayerState state, double delta);
}
