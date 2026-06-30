using System.Collections.Generic;
using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// Assembles a creature from its <see cref="Creature.Body"/> primitive parts and drives
/// <b>layer-2(a)</b> procedural animation (see docs/research/procedural-animation.md):
/// face-heading turn, body squash/stretch + bob, limb/tail oscillators, and head/eye
/// look-at toward the creature's <see cref="Creature.FocusPosition"/>.
///
/// Strictly cosmetic — reads the deterministic sim state each frame and animates from engine
/// time; never feeds back into the sim. Follows the BlobVisual/PlayerVisual Init/SyncFromModel
/// contract but builds its own children, so no .tscn is required.
/// </summary>
public partial class CreatureVisual : Node3D
{
    private sealed class Part
    {
        public required MeshInstance3D Node;
        public required AnimRole Role;
        public Vector3 RestPos;
        public float Phase;
        public float Freq;
    }

    private Creature _model = null!;
    private readonly List<Part> _parts = new();

    private float _facingYaw;   // smoothed body heading (radians)
    private float _animTime;    // accumulated time for oscillators
    private float _bodyBase = 1f;
    private float _frolic;      // eased 0→1 "is frolicking" blend, for the pronk hop tell

    private Label3D? _bubble;   // thought-bubble above the head (need broadcast)
    private float _bubbleY;     // local Y where the bubble floats
    private int _reactionStamp; // last consumed reaction stamp, to detect a fresh tell
    private float _reactionT;   // remaining reaction-bounce time (seconds)
    private float _reactionStrength;

    private const float RockFreq = 3f;      // side-to-side rocks per second
    private const float RockAngle = 0.26f;  // peak roll angle in radians (~15°)
    private const float ReactionDuration = 0.5f; // how long a happy bounce plays

    public void Init(Creature model)
    {
        _model = model;
        var plan = model.Body;
        if (plan == null)
        {
            GD.PrintErr("CreatureVisual: model has no BodyPlan");
            return;
        }

        var primary = Color.FromHtml(plan.PrimaryHex);
        float scale = plan.BaseScale;

        foreach (var part in plan.Parts)
        {
            var mesh = BuildMesh(part);

            // Unify pass: nudge each part toward the primary palette so mismatched parts
            // read as one creature — but leave eyes (dark, deliberate) alone.
            var tint = Color.FromHtml(part.Tint);
            if (part.Role != AnimRole.Eye)
                tint = tint.Lerp(primary, 0.18f);

            var rest = new Vector3(part.Socket.X, part.Socket.Y, part.Socket.Z) * scale;
            var node = new MeshInstance3D
            {
                Mesh = mesh,
                // Shaded (not unshaded): lets light model the parts so head/body/limbs read
                // as distinct 3D forms instead of a flat silhouette (mirrors PlayerVisual).
                MaterialOverride = new StandardMaterial3D { AlbedoColor = tint },
                Position = rest,
            };
            AddChild(node);

            _parts.Add(new Part
            {
                Node = node,
                Role = part.Role,
                RestPos = rest,
                Phase = part.Phase,
                Freq = part.Freq,
            });

            // Track the tallest point so the bubble floats clear of the body.
            float top = rest.Y + part.Size.Y * 0.5f;
            if (top > _bubbleY) _bubbleY = top;
        }

        BuildBubble();
        SyncFromModel();
    }

    /// <summary>Create the thought-bubble label that floats above the head. Hidden until the model
    /// broadcasts a player-lane need. Placeholder glyphs (text) until icon art lands.</summary>
    private void BuildBubble()
    {
        _bubble = new Label3D
        {
            Position = new Vector3(0f, _bubbleY + 0.4f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.006f,
            Modulate = Colors.White,
            OutlineSize = 12,
            OutlineModulate = new Color(0f, 0f, 0f, 0.6f),
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_bubble);
    }

    /// <summary>Glyph + tint for a broadcast need. Words stand in for icon art (deferred).</summary>
    private static (string Text, Color Tint) BubbleFor(BroadcastNeed need) => need switch
    {
        BroadcastNeed.Hunger    => ("food", new Color(1f, 0.55f, 0.4f)),
        BroadcastNeed.Boredom   => ("play", new Color(0.5f, 0.8f, 1f)),
        BroadcastNeed.Affection => ("♥", new Color(1f, 0.85f, 0.3f)),
        _ => ("", Colors.White),
    };

    private static Mesh BuildMesh(BodyPart part)
    {
        var s = part.Size;
        return part.Shape switch
        {
            ShapePrimitive.Sphere => new SphereMesh { Radius = s.X * 0.5f, Height = s.Y },
            ShapePrimitive.Capsule => new CapsuleMesh { Radius = s.X * 0.5f, Height = s.Y },
            ShapePrimitive.Cylinder => new CylinderMesh
            {
                TopRadius = s.X * 0.5f,
                BottomRadius = s.X * 0.5f,
                Height = s.Y,
            },
            _ => new BoxMesh { Size = new Vector3(s.X, s.Y, s.Z) },
        };
    }

    /// <summary>Position-only sync (parity with BlobVisual). Animation runs in _Process.</summary>
    public void SyncFromModel()
    {
        if (_model == null) return;
        // The sim rests the creature origin at (terrain + Radius); body-plan sockets are
        // measured from the feet (y=0), so drop the visual by Radius to plant feet on the ground.
        Position = new Vector3(
            _model.Position.X,
            _model.Position.Y - _model.Traits.Radius,
            _model.Position.Z);
    }

    public override void _Process(double delta)
    {
        if (_model == null) return;
        float dt = (float)delta;
        _animTime += dt;

        SyncFromModel();

        // --- Frolic side-to-side body rock: ease the tell in/out, then roll the body
        // around its forward axis (X rotation) on a sinusoidal sway. ---
        float frolicTarget = _model.IsFrolicking ? 1f : 0f;
        _frolic = Mathf.Lerp(_frolic, frolicTarget, 1f - Mathf.Exp(-6f * dt));
        float rockRoll = 0f;
        if (_frolic > 0.001f)
            rockRoll = Mathf.Sin(_animTime * Mathf.Pi * 2f * RockFreq) * RockAngle * _frolic;

        // --- Movement factor: how fast the creature is travelling vs its top speed ---
        var vel = _model.Velocity;
        float speed = Mathf.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        float maxSpeed = Mathf.Max(0.01f, _model.Traits.MaxSpeed);
        float moveFactor = Mathf.Clamp(speed / maxSpeed, 0f, 1f);
        // Frolic exaggerates the limb/tail swing so play reads as lively even at the same speed.
        float limbGain = 1f + _frolic * 0.8f;

        // --- Face heading: smoothly turn the whole body toward where it's moving ---
        float t = 1f - Mathf.Exp(-10f * dt);
        if (speed > 0.02f)
        {
            float targetYaw = Mathf.Atan2(vel.X, vel.Z);
            _facingYaw = LerpAngle(_facingYaw, targetYaw, t);
        }
        Rotation = new Vector3(rockRoll, _facingYaw, 0f);

        // --- Head look-at: yaw the head toward the focus target, relative to the body ---
        float headYaw = 0f;
        if (_model.FocusPosition is { } focus)
        {
            float dx = focus.X - _model.Position.X;
            float dz = focus.Z - _model.Position.Z;
            if (dx * dx + dz * dz > 1e-4f)
            {
                float worldYaw = Mathf.Atan2(dx, dz);
                headYaw = Mathf.Clamp(Mathf.AngleDifference(_facingYaw, worldYaw), -1.0f, 1.0f);
            }
        }

        // --- Per-part procedural motion ---
        foreach (var p in _parts)
        {
            switch (p.Role)
            {
                case AnimRole.Body:
                {
                    // Bob + squash/stretch while moving: a quick vertical bounce, volume-preserving.
                    float bob = Mathf.Sin(_animTime * 9f) * 0.06f * moveFactor;
                    p.Node.Position = p.RestPos + new Vector3(0f, Mathf.Abs(bob), 0f);
                    p.Node.Scale = new Vector3(_bodyBase - bob * 0.5f, _bodyBase + bob, _bodyBase - bob * 0.5f);
                    break;
                }
                case AnimRole.Limb:
                {
                    // Fore/aft swing for a walk cycle; amplitude scales with travel speed.
                    float swing = Mathf.Sin(_animTime * p.Freq + p.Phase) * 0.5f * moveFactor * limbGain;
                    p.Node.Rotation = new Vector3(swing, 0f, 0f);
                    break;
                }
                case AnimRole.Tail:
                {
                    // Gentle constant sway, livelier when moving.
                    float sway = Mathf.Sin(_animTime * p.Freq + p.Phase) * (0.25f + 0.35f * moveFactor);
                    p.Node.Rotation = new Vector3(0f, sway, 0f);
                    break;
                }
                case AnimRole.Head:
                {
                    p.Node.Rotation = new Vector3(0f, headYaw, 0f);
                    break;
                }
                case AnimRole.Eye:
                {
                    // Nudge the eyes toward the focus direction for a lively glance.
                    float side = headYaw * 0.06f;
                    p.Node.Position = p.RestPos + new Vector3(side, 0f, Mathf.Abs(side) * 0.5f);
                    break;
                }
            }
        }

        UpdateBubble(dt);
        UpdateReaction(dt);
    }

    /// <summary>Show/hide the thought-bubble from the model's current broadcast, with a gentle
    /// floating bob so it reads as a soft "cozy" tell rather than a hard UI element.</summary>
    private void UpdateBubble(float dt)
    {
        if (_bubble == null) return;
        var need = _model.Broadcast;
        if (need == BroadcastNeed.None)
        {
            _bubble.Visible = false;
            return;
        }
        var (text, tint) = BubbleFor(need);
        _bubble.Text = text;
        _bubble.Modulate = tint;
        _bubble.Visible = true;
        float bob = Mathf.Sin(_animTime * 2f) * 0.05f;
        _bubble.Position = new Vector3(0f, _bubbleY + 0.4f + bob, 0f);
    }

    /// <summary>Play a brief happy bounce when the model reports a fresh reaction. The pop scales
    /// with the interaction's flavor-match strength, so a well-matched verb reads bigger — the
    /// channel that teaches the player a creature's temperament. Decays on its own.</summary>
    private void UpdateReaction(float dt)
    {
        var r = _model.LastReaction;
        if (r.Kind == ReactionKind.Happy && r.Stamp != _reactionStamp)
        {
            _reactionStamp = r.Stamp;
            _reactionT = ReactionDuration;
            // Even a mismatched pick gives a small nod; a perfect match gives a full pop.
            _reactionStrength = 0.3f + 0.7f * r.Strength;
        }

        if (_reactionT <= 0f) return;
        _reactionT = Mathf.Max(0f, _reactionT - dt);

        // Half-sine envelope over the duration: rise then settle.
        float u = _reactionT / ReactionDuration;          // 1 → 0
        float env = Mathf.Sin(u * Mathf.Pi);              // 0 → 1 → 0
        float pop = env * 0.25f * _reactionStrength;
        Scale = new Vector3(1f + pop, 1f + pop * 1.4f, 1f + pop);
        // A little hop on top of the position the sync already set.
        Position += new Vector3(0f, env * 0.18f * _reactionStrength, 0f);
    }

    private static float LerpAngle(float from, float to, float t)
        => from + Mathf.AngleDifference(from, to) * t;
}
