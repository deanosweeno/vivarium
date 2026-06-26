using Godot;

namespace Vivarium.Scripts;

/// <summary>
/// Attach to a Camera3D node for a Baldur's Gate 3-style follow camera.
/// The camera orbits a target point that tracks the player avatar each frame.
/// Q/E or middle-mouse-drag rotate the view, the scroll wheel zooms in/out, and
/// both the follow target and zoom distance ease toward their desired values so
/// motion glides instead of snapping.
/// </summary>
public partial class CameraOrbit : Camera3D
{
    [Export] private Vector3 _target = Vector3.Zero;
    [Export] private float _distance = 14f;
    [Export] private float _yaw = Mathf.Pi / 4f;
    [Export] private float _pitch = Mathf.DegToRad(55f);
    [Export] private float _minPitch = Mathf.DegToRad(10f);
    [Export] private float _maxPitch = Mathf.DegToRad(85f);
    [Export] private float _minDistance = 2.5f;
    [Export] private float _maxDistance = 240f;
    [Export] private float _zoomStep = 1f;
    [Export] private float _keyRotateSpeed = 2.0f;
    [Export] private float _dragRotateSpeed = 0.005f;
    [Export] private float _smoothing = 10f;

    // Desired (input-driven) values; the actual _target/_distance ease toward these.
    private Vector3 _desiredTarget;
    private float _desiredDistance;
    private bool _orbiting;

    /// <summary>
    /// World point the camera follows. Set each frame to track a moving avatar; the camera
    /// eases toward it, preserving the current yaw/pitch/zoom.
    /// </summary>
    public Vector3 Target
    {
        get => _target;
        set => _desiredTarget = value;
    }

    /// <summary>Current horizontal orbit angle (radians), so callers can make input camera-relative.</summary>
    public float Yaw => _yaw;

    public override void _Ready()
    {
        _desiredTarget = _target;
        _desiredDistance = _distance;
        UpdateTransform();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _orbiting = mb.Pressed;
                GetViewport().SetInputAsHandled();
            }
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _desiredDistance = Mathf.Max(_minDistance, _desiredDistance - _zoomStep);
            }
            if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _desiredDistance = Mathf.Min(_maxDistance, _desiredDistance + _zoomStep);
            }
        }

        if (@event is InputEventMouseMotion mm && _orbiting)
        {
            _yaw -= mm.Relative.X * _dragRotateSpeed;
            _pitch -= mm.Relative.Y * _dragRotateSpeed;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Q/E keyboard rotation.
        float rotateAxis = Input.GetAxis("cam_rotate_left", "cam_rotate_right");
        _yaw -= rotateAxis * _keyRotateSpeed * dt;

        // Ease the follow target and zoom toward their desired values (frame-rate independent).
        float t = 1f - Mathf.Exp(-_smoothing * dt);
        _target = _target.Lerp(_desiredTarget, t);
        _distance = Mathf.Lerp(_distance, _desiredDistance, t);

        UpdateTransform();
    }

    private void UpdateTransform()
    {
        float h = _distance * Mathf.Cos(_pitch);
        float x = _target.X + h * Mathf.Sin(_yaw);
        float y = _target.Y + _distance * Mathf.Sin(_pitch);
        float z = _target.Z + h * Mathf.Cos(_yaw);

        Position = new Vector3(x, y, z);
        LookAt(_target);
    }
}
