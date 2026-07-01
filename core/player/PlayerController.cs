using System.Collections.Generic;
using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Orchestrates the player's compositional spine each tick: routes queued input intents to the
/// registered <see cref="IPlayerInteraction"/> verbs, and derives the animation-facing
/// <see cref="PlayerState"/> from the movement + interaction seams. The Simulator owns one of these
/// and delegates to it, keeping the verb logic out of the main loop. Deterministic — reads positions
/// and intent flags only.
/// </summary>
public sealed class PlayerController
{
    /// <summary>How long (seconds) an Interacting state is held after a verb lands, so the visual
    /// layer has time to play the interaction pose before falling back to Idle/Walking.</summary>
    private const float InteractDisplaySeconds = 0.3f;

    private readonly IReadOnlyList<IPlayerInteraction> _interactions;
    private string? _lastVerbId;
    private float _interactTimer;
    private float _lastHeading;

    public PlayerController(IReadOnlyList<IPlayerInteraction> interactions)
    {
        _interactions = interactions;
    }

    /// <summary>
    /// Resolve this frame's queued player intents against the nearest creature in reach. Edge-triggered:
    /// every queued intent is consumed whether or not its verb lands, so one keypress = one attempt.
    /// </summary>
    public void Resolve(Creature player, IReadOnlyList<Creature> entities, IReadOnlyList<FoodItem> food, BehaviorConfig cfg, Random rng, GeneCatalog? genes = null, GeneticsConfig? geneticsCfg = null)
    {
        if (player.Movement is not PlayerInputMode input) return;

        var target = NearestInteractable(player, entities, cfg.InteractReach);
        var foodTarget = NearestPickableFood(player, food, cfg.InteractReach);
        var ctx = new InteractionContext
        {
            Player = player,
            Target = target,
            FoodTarget = foodTarget,
            Input = input,
            Config = cfg,
            HoldingFood = input.HoldingFood,
            Rng = rng,
            Genes = genes,
            GeneticsCfg = geneticsCfg ?? GeneticsConfig.Default,
        };

        bool any = false;
        foreach (var verb in _interactions)
        {
            if (!input.ConsumeIntent(verb.Id)) continue;
            if (verb.CanApply(in ctx))
            {
                verb.Apply(in ctx);
                _lastVerbId = verb.Id;
                any = true;
            }
        }
        input.ClearIntents();   // drop any queued intents with no matching verb

        if (target is not null) target.Needs.Clamp();
        if (any) _interactTimer = InteractDisplaySeconds;
    }

    /// <summary>Derive the player's animation state: Interacting while the post-verb timer runs, else
    /// Walking when moving, else Idle. Writes <see cref="Creature.PlayerState"/> for the visual layer.</summary>
    public void UpdateState(Creature player, double delta)
    {
        if (_interactTimer > 0f)
            _interactTimer -= (float)delta;

        bool moving = player.Movement is IPlayerMovement mv && mv.IsMoving;
        PlayerStateKind kind =
            _interactTimer > 0f ? PlayerStateKind.Interacting :
            moving ? PlayerStateKind.Walking :
            PlayerStateKind.Idle;

        // Derive animation-facing heading/speed from the avatar's actual velocity (so facing tracks
        // steering, not raw input). Hold the last heading while stopped so the figure doesn't snap.
        var vel = player.Velocity;
        float speed = MathF.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        if (speed > 1e-3f)
            _lastHeading = MathF.Atan2(vel.X, vel.Z);
        float maxSpeed = player.Traits.MaxSpeed;
        float speed01 = maxSpeed > 0f ? Math.Clamp(speed / maxSpeed, 0f, 1f) : 0f;

        player.PlayerState = new PlayerState
        {
            Kind = kind,
            LastVerbId = _lastVerbId,
            Heading = _lastHeading,
            Speed01 = speed01,
        };
    }

    /// <summary>Nearest brained creature within <paramref name="reach"/> (horizontal) of the player, or
    /// null. The player itself and brainless entities are skipped.</summary>
    private static Creature? NearestInteractable(Creature player, IReadOnlyList<Creature> entities, float reach)
        => Vec.NearestBy(
            entities, player.Position,
            e => e.Position,
            e => !ReferenceEquals(e, player) && !e.IsPlayer && e.Brain is not null,
            reach).Item;

    /// <summary>Nearest pickable food item within <paramref name="reach"/> (horizontal) of the
    /// player, or null. Mirrors <c>Simulator.NearestFood</c> but filters on <see cref="FoodItem.Pickable"/>.</summary>
    private static FoodItem? NearestPickableFood(Creature player, IReadOnlyList<FoodItem> food, float reach)
        => Vec.NearestBy(food, player.Position, item => item.Position, item => item.Pickable, reach).Item;
}
