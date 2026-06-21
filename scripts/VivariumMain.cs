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

        // Size the arena to the loaded map's extents if a MapView is present,
        // otherwise fall back to a small default arena.
        float arenaWidth = 10f, arenaDepth = 10f;
        var mapView = FindMapView();
        if (mapView?.Map != null)
        {
            arenaWidth = mapView.WorldWidth;
            arenaDepth = mapView.WorldDepth;
        }

        var arena = Arena.GroundArena(arenaWidth, arenaDepth);
        var seed = _seed != 0 ? _seed : System.Environment.TickCount;
        _sim = new Simulator(arena, seed);

        // Wire the map + biome rules into the sim so biomes affect creatures at runtime.
        if (mapView?.Map != null)
        {
            _sim.Map = mapView.Map;
            _sim.Biomes = mapView.Biomes;
        }

        _blobScene ??= ResourceLoader.Load<PackedScene>("res://scenes/Blob.tscn");

        // Spawn a handful of blobs scattered across the arena.
        float spawnHalfX = arenaWidth / 2f - 1f;
        float spawnHalfZ = arenaDepth / 2f - 1f;
        for (int i = 0; i < 8; i++)
        {
            var x = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfX;
            var z = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfZ;
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

    private MapView? FindMapView()
    {
        foreach (var child in GetChildren())
        {
            if (child is MapView view)
                return view;
        }
        return null;
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
