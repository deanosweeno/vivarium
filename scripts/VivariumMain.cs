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

    // Debug overlay (F3 toggles)
    private bool _showDebug;
    private readonly Dictionary<Blob, Label3D> _debugLabels = new();

    private Blob? _player;
    private PlayerInputMode? _playerInput;
    private CameraOrbit? _cameraOrbit;
    private PlayerVisual? _playerVisual;
    private Label? _foodHudLabel;

    public override void _Ready()
    {
        // Register debug toggle action at runtime (no project.godot editing needed).
        if (!InputMap.HasAction("debug_toggle"))
        {
            InputMap.AddAction("debug_toggle");
            var debugEv = new InputEventKey { Keycode = Key.F3 };
            InputMap.ActionAddEvent("debug_toggle", debugEv);
        }

        // Screen-space HUD for the food-in-hand state (F pick up / G drop). Built in code — no scene HUD exists.
        var hud = new CanvasLayer { Name = "TamingHud" };
        AddChild(hud);
        _foodHudLabel = new Label
        {
            Name = "FoodHud",
            Position = new Vector2(16, 16),
            Text = "Food: empty  (F to pick up)",
        };
        hud.AddChild(_foodHudLabel);

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

        // Load the creature catalog (body plans + per-type sim rules).
        var creatures = LoadCreatures(_creaturesPath);
        _sproutPlan = creatures.Get("sprout");
        var sheepDef = creatures.GetDef("sheep");
        _sheepPlan = sheepDef?.Body;

        _foodScene ??= ResourceLoader.Load<PackedScene>("res://scenes/Food.tscn");

        // Spawn a handful of creatures scattered across the arena.
        float spawnHalfX = arenaWidth / 2f - 1f;
        float spawnHalfZ = arenaDepth / 2f - 1f;

        // Placement strategy: arena clamp + overlap avoidance — the default spawn path.
        var spawnPlacement = new OverlapAvoidingPlacement(ArenaClampPlacement.Instance);

        // A few Sprouts (random temperament) keep the original creature in the mix.
        for (int i = 0; i < 4; i++)
        {
            var x = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfX;
            var z = (float)(_sim.Rng.NextDouble() * 2 - 1) * spawnHalfZ;
            var blob = (Blob)_sim.Spawn(
                new SNVector3(x, 0f, z),
                new BlobFactory(_sim.Behavior, _sim.FleeStrategy, _sim.Rng),
                spawnPlacement);
            blob.Body = _sproutPlan;
        }

        // --- Sheep herds: all stats/drives/herd config are data-driven (assets/creatures.json) ---
        if (mapView?.Map != null && sheepDef?.Herd != null)
        {
            HerdSpawner.SpawnHerds(_sim, sheepDef, mapView.Map, _sim.Rng);
        }

        // Spawn the player avatar at the arena center and point the follow-camera at it.
        (_player, _playerInput) = _sim.SpawnPlayer(SNVector3.Zero);
        _cameraOrbit = _camera as CameraOrbit;
        if (_cameraOrbit != null)
            _cameraOrbit.Target = new Vector3(_player.Position.X, _player.Position.Y, _player.Position.Z);
    }

    public override void _Process(double delta)
    {
        // Toggle debug labels on F3.
        if (Input.IsActionJustPressed("debug_toggle"))
            _showDebug = !_showDebug;

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

            // Debug label above this creature.
            if (_showDebug)
            {
                if (!_debugLabels.TryGetValue(blob, out var label))
                {
                    label = new Label3D
                    {
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                        OutlineSize = 1,
                        FontSize = 14,
                        Modulate = Colors.White,
                        PixelSize = 0.005f,
                    };
                    AddChild(label);
                    _debugLabels[blob] = label;
                }
                label.Text = DebugLabelText(blob);
                label.Position = new Vector3(
                    blob.Position.X,
                    blob.Position.Y + blob.Traits.Radius + 1.2f,
                    blob.Position.Z);
            }
        }

        // Food-in-hand HUD cue, so pick-up/drop is observable.
        if (_foodHudLabel != null && _playerInput != null)
        {
            var carried = _playerInput.CarriedFood;
            _foodHudLabel.Text = carried != null
                ? $"Food: {carried.Name}  (G to drop)"
                : "Food: empty  (F to pick up)";
            _foodHudLabel.Modulate = carried != null ? Colors.LightGreen : Colors.White;
        }

        // Remove all debug labels when toggled off.
        if (!_showDebug && _debugLabels.Count > 0)
        {
            foreach (var (_, label) in _debugLabels)
                label.QueueFree();
            _debugLabels.Clear();
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
                _playerVisual.SyncFromModel(delta);
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

    /// <summary>
    /// Edge-triggered taming verbs (POC keybinds; no InputMap actions needed):
    ///   F = pick up food · G = drop food · 1 = feed · 2 = soothe (calm-pet) · 3 = play.
    /// (1/2/3 avoid the Q/E camera-rotate bindings in project.godot.)
    /// Feed/soothe/play set a one-shot intent the Simulator consumes on its next Tick.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_playerInput == null || @event is not InputEventKey k || !k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case Key.F:    _playerInput.QueueIntent("pickup"); break;
            case Key.G:    _playerInput.QueueIntent("place"); break;
            case Key.Key1: _playerInput.QueueIntent("feed"); break;
            case Key.Key2: _playerInput.QueueIntent("soothe"); break;
            case Key.Key3: _playerInput.QueueIntent("play"); break;
        }
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

    private MapView? FindMapView()
    {
        foreach (var child in GetChildren())
        {
            if (child is MapView view)
                return view;
        }
        return null;
    }

    /// <summary>
    /// Build the debug-label text for a creature: current action name, then one line each
    /// for hunger, fatigue, and boredom — each a 10-segment bar + percentage.
    /// </summary>
    private static string DebugLabelText(Blob blob)
    {
        string action = blob.Brain?.CurrentName ?? string.Empty;
        if (string.IsNullOrEmpty(action)) action = "—";

        float hunger = blob.Needs.Hunger;
        int hFilled = Math.Clamp((int)MathF.Round(hunger * 10f), 0, 10);
        string hBar = new string('█', hFilled) + new string('░', 10 - hFilled);
        int hPct = (int)MathF.Round(hunger * 100f);

        float fatigue = blob.Needs.Fatigue;
        int fFilled = Math.Clamp((int)MathF.Round(fatigue * 10f), 0, 10);
        string fBar = new string('█', fFilled) + new string('░', 10 - fFilled);
        int fPct = (int)MathF.Round(fatigue * 100f);

        float boredom = blob.Needs.Boredom;
        int bFilled = Math.Clamp((int)MathF.Round(boredom * 10f), 0, 10);
        string bBar = new string('█', bFilled) + new string('░', 10 - bFilled);
        int bPct = (int)MathF.Round(boredom * 100f);

        float bond = blob.Needs.Affection;
        int oFilled = Math.Clamp((int)MathF.Round(bond * 10f), 0, 10);
        string oBar = new string('█', oFilled) + new string('░', 10 - oFilled);
        int oPct = (int)MathF.Round(bond * 100f);

        return $"{action,-10}\n  eat  {hBar} {hPct,3}%\n  rest {fBar} {fPct,3}%\n  play {bBar} {bPct,3}%\n  bond {oBar} {oPct,3}%";
    }

}
