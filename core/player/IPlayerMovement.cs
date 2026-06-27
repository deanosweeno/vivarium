namespace Vivarium.Core;

/// <summary>
/// Player-driven locomotion seam. Extends the shared <see cref="IMovementMode"/> with the one
/// signal the animation/state layer needs — whether the avatar is actively moving this frame.
/// Today there is a single implementation (<see cref="PlayerInputMode"/>); future modes
/// (sprint, swim, fly) implement this and are swapped onto <see cref="Creature.Movement"/>.
/// </summary>
public interface IPlayerMovement : IMovementMode
{
    /// <summary>True when the player is supplying movement input this frame.</summary>
    bool IsMoving { get; }
}
