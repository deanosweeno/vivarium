using Godot;

namespace Vivarium.Scripts;

/// <summary>
/// Attach to a Camera3D node. Right-drag orbits around a target,
/// scroll wheel zooms in/out.
/// </summary>
public partial class CameraOrbit : Camera3D
{
    [Export] private Vector3 _target = Vector3.Zero;
    [Export] private float _distance = 14f;
    [Export] private float _yaw = Mathf.Pi / 4f;
    [Export] private float _pitch = Mathf.DegToRad(55f);
    [Export] private float _minPitch = Mathf.DegToRad(10f);
    [Export] private float _maxPitch = Mathf.DegToRad(85f);
    [Export] private float _minDistance = 4f;
    [Export] private float _maxDistance = 240f;

    private bool _orbiting;

    public override void _Ready()
    {
        UpdateTransform();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _orbiting = mb.Pressed;
                GetViewport().SetInputAsHandled();
            }
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Max(_minDistance, _distance - 1f);
                UpdateTransform();
            }
            if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Min(_maxDistance, _distance + 1f);
                UpdateTransform();
            }
        }

        if (@event is InputEventMouseMotion && _orbiting)
        {
            var mm = (InputEventMouseMotion)@event;
            _yaw -= mm.Relative.X * 0.005f;
            _pitch -= mm.Relative.Y * 0.005f;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
            UpdateTransform();
        }
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
