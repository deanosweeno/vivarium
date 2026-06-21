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

        // Configure directional light: high-noon direction, shadow settings, and GI.
        foreach (var child in GetChildren())
        {
            if (child is DirectionalLight3D light)
            {
                // Rotate to shine straight down (12:00).
                // DirectionalLight3D shines along local -Z; rotate 90° around X
                // so local -Z points to world -Y.
                light.Rotation = new Vector3(-Mathf.Pi / 2, 0, 0);
                light.Position = new Vector3(0, 30, 0);
                light.LightColor = new Color(1, 1, 0.98f);
                light.LightEnergy = 1.0f;

                // Shadow settings to prevent acne on the dense terrain mesh.
                light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                light.DirectionalShadowSplit1 = 0.1f;
                light.ShadowBias = 0.02f;
                light.ShadowNormalBias = 0.5f;
                break;
            }
        }

        // Add WorldEnvironment with SDFGI + ambient light so shadows aren't pitch black.
        var env = new Godot.Environment();
        env.SdfgiEnabled = true;
        env.SdfgiUseOcclusion = false;
        env.SdfgiReadSkyLight = true;
        env.SdfgiBounceFeedback = 0.5f;
        env.SdfgiCascades = 4;
        env.SdfgiMinCellSize = 0.5f;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.25f, 0.30f, 0.45f);
        env.AmbientLightSkyContribution = 0.0f;
        env.AmbientLightEnergy = 0.4f;
        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

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
