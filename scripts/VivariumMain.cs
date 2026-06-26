using System.Collections.Generic;
using Godot;
using Vivarium.Core;
using FileAccess = Godot.FileAccess;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.Scripts;

public partial class VivariumMain : Node3D
{
    [Export] private PackedScene? _blobScene;
    [Export] private PackedScene? _foodScene;
    [Export] private string _foodsPath = "res://assets/foods.json";
    [Export] private string _creaturesPath = "res://assets/creatures.json";
    [Export] private Camera3D? _camera;
    [Export] private int _seed;

    private Simulator _sim = null!;
    private BodyPlan? _sproutPlan;
    private BodyPlan? _sheepPlan;
    private readonly Dictionary<Blob, CreatureVisual> _visuals = new();
    private readonly Dictionary<FoodItem, FoodVisual> _foodVisuals = new();

    private Blob? _player;
    private PlayerInputMode? _playerInput;
    private CameraOrbit? _cameraOrbit;
    private PlayerVisual? _playerVisual;

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

        // Load food types and scatter food across the world (per-biome, deterministic by seed).
        _sim.Foods = LoadFoods(_foodsPath);
        _sim.SeedFood();

        // Load the creature body-plan catalog.
        var creatures = LoadCreatures(_creaturesPath);
        _sproutPlan = creatures.Get("sprout");
        _sheepPlan = creatures.Get("sheep");

        _foodScene ??= ResourceLoader.Load<PackedScene>("res://scenes/Food.tscn");

        // Spawn a handful of creatures scattered across the arena.
        float spawnHalfX = arenaWidth / 2f - 1f;
        float spawnHalfZ = arenaDepth / 2f - 1f;

        // A few Sprouts (random temperament) keep the original creature in the mix.
        for (int i = 0; i < 4; i++)
        {
            var x = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfX;
            var z = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfZ;
            var blob = _sim.SpawnBlob(new SNVector3(x, 0f, z));
            blob.Body = _sproutPlan;
        }

        // --- Sheep herd: Plains only, berries diet, 12-strong ---
        if (mapView?.Map != null && _sheepPlan != null)
        {
            var sheepPositions = SampleBiomePositions(mapView.Map, Biome.Plains, 12, _sim.Rng);
            var sheepTraits = new CreatureTraits
            {
                Radius = 0.6f,
                MaxSpeed = 0.35f,
                JumpHeight = 2.2f,
                TurnRate = 1.8f,
                Acceleration = 1.6f,
                GravityScale = 0f,
            };
            var sheepDrives = new Drives
            {
                Curiosity = 0.3f,
                Fear = 0.15f,
                Sociability = 0.9f,
                Appetite = 0.6f,
                Aggression = 0.1f,
                PlayCuddle = 0.3f,
            };
            var sheepDiet = new HashSet<string> { "berries" };
            foreach (var pos in sheepPositions)
            {
                var sheep = _sim.SpawnBlob(pos, new CreatureTraits(sheepTraits), new Drives(sheepDrives));
                sheep.Body = _sheepPlan;
                sheep.Diet = sheepDiet;
            }
        }

        // Spawn the player avatar at the arena center and point the follow-camera at it.
        (_player, _playerInput) = _sim.SpawnPlayer(SNVector3.Zero);
        _cameraOrbit = _camera as CameraOrbit;
        if (_cameraOrbit != null)
            _cameraOrbit.Target = new Vector3(_player.Position.X, _player.Position.Y, _player.Position.Z);
    }

    public override void _Process(double delta)
    {
        UpdatePlayerInput();
        _sim.Tick(delta);
        TrackPlayerWithCamera();

        foreach (var entity in _sim.Entities)
        {
            if (entity is not Blob blob) continue;
            if (blob == _player) continue; // player has its own PlayerVisual

            if (!_visuals.TryGetValue(blob, out var visual))
            {
                var instance = new CreatureVisual();
                AddChild(instance);
                instance.Init(blob);
                _visuals[blob] = instance;
            }
            // CreatureVisual animates itself in _Process; no per-frame sync needed here.
        }

        // Player visual — lazy-instantiate on first frame, sync every frame after.
        if (_player != null)
        {
            if (_playerVisual == null)
            {
                _playerVisual = new PlayerVisual();
                AddChild(_playerVisual);
                _playerVisual.Init(_player);
            }
            else
            {
                _playerVisual.SyncFromModel();
            }
        }

        foreach (var item in _sim.Food)
        {
            if (!_foodVisuals.TryGetValue(item, out var foodVisual))
            {
                if (_foodScene == null) continue;
                var instance = _foodScene.Instantiate<FoodVisual>();
                AddChild(instance);
                instance.Init(item);
                _foodVisuals[item] = instance;
            }
            else
            {
                foodVisual.SyncFromModel();
            }
        }
    }

    /// <summary>
    /// Read WASD/arrow movement, rotate it into world space by the camera yaw (so "forward"
    /// is away from the camera), and hand it to the player's movement mode. The Simulator
    /// integrates it on the next Tick alongside every other entity.
    /// </summary>
    private void UpdatePlayerInput()
    {
        if (_playerInput == null) return;

        // X = right(+)/left(−), Y = back(+)/forward(−) in Godot's down-positive 2D vector.
        var move = Input.GetVector("move_left", "move_right", "move_forward", "move_back");

        // Rotate the input by the camera yaw so movement is camera-relative.
        float yaw = _cameraOrbit?.Yaw ?? 0f;
        float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
        // move.Y < 0 = forward (away from camera, world −Z when yaw=0).
        float wx = move.X * cos + move.Y * sin;
        float wz = -move.X * sin + move.Y * cos;

        _playerInput.MoveInput = new System.Numerics.Vector2(wx, wz);
    }

    /// <summary>Keep the orbit camera centered on the avatar each frame.</summary>
    private void TrackPlayerWithCamera()
    {
        if (_cameraOrbit == null || _player == null) return;
        _cameraOrbit.Target = new Vector3(_player.Position.X, _player.Position.Y, _player.Position.Z);
    }

    /// <summary>
    /// Load the food-type catalog through Godot's FileAccess (string seam, works inside an
    /// exported .pck). Falls back to an empty catalog (no food) if the file is missing.
    /// </summary>
    private static FoodCatalog LoadFoods(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"VivariumMain: could not open foods at '{path}' ({FileAccess.GetOpenError()}); no food.");
            return FoodCatalog.Empty;
        }
        try
        {
            return FoodCatalog.Parse(file.GetAsText());
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"VivariumMain: failed to parse foods at '{path}': {e.Message}; no food.");
            return FoodCatalog.Empty;
        }
    }

    /// <summary>
    /// Load the creature body-plan catalog through Godot's FileAccess (string seam, works
    /// inside an exported .pck). Falls back to an empty catalog if the file is missing.
    /// </summary>
    private static CreatureCatalog LoadCreatures(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"VivariumMain: could not open creatures at '{path}' ({FileAccess.GetOpenError()}); no body plans.");
            return CreatureCatalog.Empty;
        }
        try
        {
            return CreatureCatalog.Parse(file.GetAsText());
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"VivariumMain: failed to parse creatures at '{path}': {e.Message}; no body plans.");
            return CreatureCatalog.Empty;
        }
    }

    /// <summary>
    /// Return up to <paramref name="count"/> random world positions inside cells of the given
    /// <paramref name="biome"/>. Positions land on terrain height (<see cref="MapData.HeightAt"/>).
    /// If no cells of that biome exist, returns an empty list.
    /// </summary>
    private static List<System.Numerics.Vector3> SampleBiomePositions(
        MapData map, Biome biome, int count, System.Random rng)
    {
        // Collect all cell coordinates that belong to the requested biome.
        var cells = new List<(int cx, int cz)>();
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (map.GetBiome(cx, cz) == biome)
                cells.Add((cx, cz));
        }

        var result = new List<System.Numerics.Vector3>();
        if (cells.Count == 0) return result;

        float halfCell = map.CellSize / 2f;
        for (int i = 0; i < count; i++)
        {
            var (cx, cz) = cells[rng.Next(cells.Count)];
            float px = map.CellSize * (cx - map.Width / 2f + (float)rng.NextDouble());
            float pz = map.CellSize * (cz - map.Depth / 2f + (float)rng.NextDouble());
            float py = map.HeightAt(new System.Numerics.Vector3(px, 0f, pz));
            result.Add(new System.Numerics.Vector3(px, py, pz));
        }
        return result;
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

}
