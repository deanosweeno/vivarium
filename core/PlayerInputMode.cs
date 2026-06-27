using System.Collections.Generic;
using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Player-driven locomotion: the IO layer writes a horizontal move intent into
/// <see cref="MoveInput"/> each frame; this mode turns it into a
/// <see cref="Creature.DesiredVelocity"/> and then defers all the actual motion math
/// (accelerate → clamp → integrate → wall reflection) to a wrapped
/// <see cref="SteeringLocomotion"/>.
///
/// It is the player's analogue of the <see cref="UtilityBrain"/>: the brain decides the
/// desired velocity for autonomous creatures; here the keyboard does. Living in
/// <c>core</c> lets it set the <c>internal</c> <see cref="Creature.DesiredVelocity"/> — the
/// seam the Godot layer otherwise can't reach.
/// </summary>
public sealed class PlayerInputMode : IPlayerMovement
{
    private readonly SteeringLocomotion _loco = new();

    /// <summary>Verb intents queued by the IO layer this frame, keyed by
    /// <see cref="IPlayerInteraction.Id"/>. Consumed (and cleared) by the
    /// <see cref="PlayerController"/> during the next tick. A set, so a duplicate keypress in one
    /// frame is still a single attempt.</summary>
    private readonly HashSet<string> _pending = new();

    /// <summary>
    /// World-space horizontal move intent for this frame: X = right(+)/left(−),
    /// Y = forward(+)/back(−). Magnitude is treated as throttle and clamped to 1, so a
    /// diagonal isn't faster than a cardinal. The caller has already rotated this into world
    /// space (e.g. by the camera yaw). Set by the input layer; defaults to no movement.
    /// </summary>
    public Vector2 MoveInput { get; set; }

    /// <summary>
    /// The food type the player is carrying in hand, or null when empty-handed. Set by the
    /// pick-up verb (and cleared by feed/place), replacing the old cheat toggle. Knowing the
    /// *type* lets the HUD/visual show what's held and a future place-verb regrow the right item.
    /// </summary>
    public FoodDef? CarriedFood { get; set; }

    /// <summary>
    /// Whether the player currently has food "in hand". Feeding requires this; it also makes nearby
    /// creatures follow the player (the lure). Derived from <see cref="CarriedFood"/>.
    /// </summary>
    public bool HoldingFood => CarriedFood is not null;

    /// <summary>Queue a verb intent by its <see cref="IPlayerInteraction.Id"/> (e.g. "feed"). The
    /// canonical input entry point — the IO layer calls this on keypress.</summary>
    public void QueueIntent(string id) => _pending.Add(id);

    /// <summary>True if <paramref name="id"/> was queued; removes it. Edge-trigger consumed by the
    /// <see cref="PlayerController"/>.</summary>
    internal bool ConsumeIntent(string id) => _pending.Remove(id);

    /// <summary>Drop all remaining queued intents (those with no matching verb this tick).</summary>
    internal void ClearIntents() => _pending.Clear();

    // --- IO convenience shims: edge-triggered verb setters that map to QueueIntent. Write-only;
    //     the canonical API is QueueIntent. Kept so callers/tests can set a single verb tersely. ---
    /// <summary>Edge-triggered "feed" intent. Only acts while <see cref="HoldingFood"/>.</summary>
    public bool FeedPressed { set { if (value) QueueIntent("feed"); } }

    /// <summary>Edge-triggered "soothe" (calm-pet) intent — lets a bonded creature rest.</summary>
    public bool SoothePressed { set { if (value) QueueIntent("soothe"); } }

    /// <summary>Edge-triggered "play" (lively-pet) intent — relieves a bonded creature's boredom.</summary>
    public bool PlayPressed { set { if (value) QueueIntent("play"); } }

    /// <inheritdoc/>
    public bool IsMoving => MoveInput.LengthSquared() > 1e-6f;

    public void Tick(double delta, Creature creature, Arena arena, Random rng)
    {
        var dir = new Vector3(MoveInput.X, 0f, MoveInput.Y);
        float len = dir.Length();
        if (len > 1f)
            dir /= len;

        creature.DesiredVelocity = dir * creature.Traits.MaxSpeed;
        _loco.Tick(delta, creature, arena, rng);
    }
}
