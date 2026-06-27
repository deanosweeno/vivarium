namespace Vivarium.Core;

/// <summary>
/// What the player avatar is doing this tick, in animation terms. Set by
/// <see cref="PlayerController.UpdateState"/> from the movement + interaction seams; read by the
/// Godot visual layer (an <c>IPlayerAnimation</c>) to pick a pose/clip. Core stays Godot-free:
/// it only describes intent, never touches an AnimationPlayer.
/// </summary>
public enum PlayerStateKind
{
    /// <summary>Standing still, no active interaction.</summary>
    Idle,

    /// <summary>Moving under player input.</summary>
    Walking,

    /// <summary>An interaction verb fired recently (held briefly so the anim can play out).</summary>
    Interacting,
}

/// <summary>
/// Immutable snapshot of the player's animation-facing state. <see cref="LastVerbId"/> lets the
/// visual layer distinguish interaction flavours (feed vs play) without the core knowing about clips.
/// </summary>
public readonly struct PlayerState
{
    public PlayerStateKind Kind { get; init; }

    /// <summary>Id of the most recent interaction verb that landed (e.g. "feed"), or null if none yet.</summary>
    public string? LastVerbId { get; init; }

    /// <summary>Target facing yaw in radians (atan2 of horizontal velocity), held while stopped.
    /// The visual turns the avatar toward this so it faces its WASD heading.</summary>
    public float Heading { get; init; }

    /// <summary>Normalized horizontal speed, 0 (still) to 1 (max), scaling the walk-cycle intensity.</summary>
    public float Speed01 { get; init; }

    public static readonly PlayerState Idle = new() { Kind = PlayerStateKind.Idle };
}
