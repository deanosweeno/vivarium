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
        }

        SyncFromModel();
    }

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

        // --- Movement factor: how fast the creature is travelling vs its top speed ---
        var vel = _model.Velocity;
        float speed = Mathf.Sqrt(vel.X * vel.X + vel.Z * vel.Z);
        float maxSpeed = Mathf.Max(0.01f, _model.Traits.MaxSpeed);
        float moveFactor = Mathf.Clamp(speed / maxSpeed, 0f, 1f);

        // --- Face heading: smoothly turn the whole body toward where it's moving ---
        float t = 1f - Mathf.Exp(-10f * dt);
        if (speed > 0.02f)
        {
            float targetYaw = Mathf.Atan2(vel.X, vel.Z);
            _facingYaw = LerpAngle(_facingYaw, targetYaw, t);
        }
        Rotation = new Vector3(0f, _facingYaw, 0f);

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
                    float swing = Mathf.Sin(_animTime * p.Freq + p.Phase) * 0.5f * moveFactor;
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
    }

    private static float LerpAngle(float from, float to, float t)
        => from + Mathf.AngleDifference(from, to) * t;
}
