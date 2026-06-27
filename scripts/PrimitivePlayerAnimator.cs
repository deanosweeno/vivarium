using Godot;
using Vivarium.Core;

namespace Vivarium.Scripts;

/// <summary>
/// Procedural animator for the primitive-mesh player avatar: turns the figure to face its movement
/// heading and plays a simple walk cycle (body bob + opposite-phase arm swing) scaled by speed, with
/// a brief squash on Interacting. Mirrors the CreatureVisual approach (smoothed atan2 facing, motion
/// scaled by a 0–1 move factor) without any clips.
///
/// TODO: replace with an AnimationPlayer/AnimationTree-driven clip set once the avatar has a rig.
/// Switch on <see cref="PlayerState.LastVerbId"/> for per-verb interaction animations (feed vs play).
/// </summary>
public sealed class PrimitivePlayerAnimator : IPlayerAnimation
{
    private const float TurnRate = 10f;     // exponential ease toward target heading
    private const float StepRate = 9f;      // walk-cycle frequency
    private const float ArmSwing = 0.6f;    // peak arm rotation (radians) at full speed

    private readonly Node3D _armL;
    private readonly Node3D _armR;
    private readonly float _armRestPitch;

    private float _phase;
    private float _yaw;

    public PrimitivePlayerAnimator(Node3D armL, Node3D armR)
    {
        _armL = armL;
        _armR = armR;
        _armRestPitch = armL.Rotation.X;
    }

    public void Apply(Node3D root, PlayerState state, double delta)
    {
        float dt = (float)delta;

        // Face the movement heading — smoothly, so turns don't snap.
        float t = 1f - Mathf.Exp(-TurnRate * dt);
        _yaw = Mathf.LerpAngle(_yaw, state.Heading, t);
        root.Rotation = new Vector3(0f, _yaw, 0f);

        switch (state.Kind)
        {
            case PlayerStateKind.Walking:
                _phase += dt * StepRate * Mathf.Max(state.Speed01, 0.1f);
                root.Position += new Vector3(0f, Mathf.Abs(Mathf.Sin(_phase)) * 0.04f * state.Speed01, 0f);
                root.Scale = Vector3.One;
                SwingArms(state.Speed01);
                break;

            case PlayerStateKind.Interacting:
                // Brief squash-and-reach — a stand-in "doing something" pose.
                root.Scale = new Vector3(1.05f, 0.92f, 1.05f);
                SwingArms(0f);
                break;

            default: // Idle
                _phase = 0f;
                root.Scale = Vector3.One;
                SwingArms(0f);
                break;
        }
    }

    /// <summary>Swing the arms fore/aft in opposite phase, scaled by speed (0 = rest pose).</summary>
    private void SwingArms(float speed01)
    {
        float swing = Mathf.Sin(_phase) * ArmSwing * speed01;
        _armL.Rotation = new Vector3(_armRestPitch + swing, 0f, 0f);
        _armR.Rotation = new Vector3(_armRestPitch - swing, 0f, 0f);
    }
}
