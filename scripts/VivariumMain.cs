using System.Collections.Generic;
using Godot;
using Vivarium.Core;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.Scripts;

public partial class VivariumMain : Node3D
{
    [Export] private PackedScene? _blobScene;
    [Export] private Camera3D? _camera;
    [Export] private int _seed;

    private Simulator _sim = null!;
    private readonly Dictionary<Blob, BlobVisual> _visuals = new();

    public override void _Ready()
    {
        if (_camera == null)
        {
            foreach (var child in GetChildren())
            {
                if (child is Camera3D cam)
                {
                    _camera = cam;
                    break;
                }
            }
        }

        if (_camera == null)
        {
            GD.PrintErr("VivariumMain: no Camera3D found");
            return;
        }

        var arena = Arena.GroundArena(10, 10);
        var seed = _seed != 0 ? _seed : System.Environment.TickCount;
        _sim = new Simulator(arena, seed);

        _blobScene ??= ResourceLoader.Load<PackedScene>("res://scenes/Blob.tscn");

        for (int i = 0; i < 3; i++)
        {
            var x = (float)(_sim.Rng.NextDouble() * 8 - 4);
            var z = (float)(_sim.Rng.NextDouble() * 8 - 4);
            _sim.SpawnBlob(new SNVector3(x, 0f, z));
        }
    }

    public override void _Process(double delta)
    {
        _sim.Tick(delta);

        foreach (var entity in _sim.Entities)
        {
            if (entity is not Blob blob) continue;

            if (!_visuals.TryGetValue(blob, out var visual))
            {
                if (_blobScene == null) continue;
                var instance = _blobScene.Instantiate<BlobVisual>();
                AddChild(instance);
                instance.Init(blob);
                _visuals[blob] = instance;
            }
            else
            {
                visual.SyncFromModel();
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_camera == null) return;

        if (@event is InputEventMouseButton mb
            && mb.ButtonIndex == MouseButton.Left
            && mb.Pressed)
        {
            SpawnAtMouse(mb.Position);
        }
    }

    private void SpawnAtMouse(Vector2 mousePos)
    {
        var origin = _camera!.ProjectRayOrigin(mousePos);
        var direction = _camera!.ProjectRayNormal(mousePos);

        var plane = new Plane(Vector3.Up, 0f);
        var hit = plane.IntersectsRay(origin, direction);

        if (hit.HasValue)
        {
            var point = hit.Value;
            var worldPos = new SNVector3(point.X, 0f, point.Z);

            if (_sim.Arena.Contains(worldPos))
            {
                _sim.SpawnBlob(worldPos);
            }
        }
    }
}
