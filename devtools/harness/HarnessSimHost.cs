using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Core;
using Vivarium.Scripts;
using FileAccess = Godot.FileAccess;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.DevTools;

/// <summary>
/// Owns the single live <see cref="Simulator"/> shared by every harness mode plus the 3D
/// presentation (terrain via <see cref="MapView"/>, creature/food/player visuals). Ticks the sim
/// each frame under harness time-control (pause / step / time-scale) and exposes spawn/despawn,
/// world-reset, and click-to-select seams the mode panels drive. Mirrors <see cref="VivariumMain"/>
/// but stripped of the shipped game's HUD/keybind specifics — this node only ever exists inside
/// the dev harness scene.
/// </summary>
public partial class HarnessSimHost : Node3D, ISpliceHost
{
    private const string CreaturesPath = "res://assets/creatures.json";
    private const string FoodsPath = "res://assets/foods.json";
    private const string GenesPath = "res://assets/genes.json";

    /// <summary>The live simulation. Never null after <see cref="_Ready"/>.</summary>
    public Simulator Sim { get; private set; } = null!;

    /// <summary>Creature catalog (body plans + sim rules) — species source for spawn menus.</summary>
    public CreatureCatalog Creatures { get; private set; } = CreatureCatalog.Empty;

    /// <summary>Harvestable-gene catalog (§3) — read by the genetics panel.</summary>
    public GeneCatalog Genes { get; private set; } = GeneCatalog.Empty;

    /// <summary>Loaded biome rules (via the MapView), for map-mode inspection.</summary>
    public BiomeCatalog Biomes => _mapView?.Biomes ?? BiomeCatalog.Empty;

    /// <summary>The terrain view — the map panel regenerates through <see cref="Vivarium.Scripts.MapView.Rebuild"/>.</summary>
    public MapView? MapView => _mapView;

    /// <summary>Active camera, injected by the root, used for click-to-select ray picks.</summary>
    public Camera3D? Camera { get; set; }

    // --- time control (shared by all modes) ---
    public bool Paused { get; set; }
    public float TimeScale { get; set; } = 1f;
    private bool _stepPending;

    /// <summary>Advance exactly one fixed tick on the next frame while paused.</summary>
    public void StepOnce() => _stepPending = true;

    // --- selection (shared by AI / Player modes) ---
    public Creature? Selected { get; private set; }

    // --- player ---
    public Blob? Player { get; private set; }
    public PlayerInputMode? PlayerInput { get; private set; }
    public SNVector3 PlayerPosition => Player?.Position ?? SNVector3.Zero;

    private MapView _mapView = null!;
    private BodyPlan? _sproutPlan;
    private PackedScene? _foodScene;
    private MeshInstance3D? _selectionMarker;

    private readonly Dictionary<Blob, CreatureVisual> _visuals = new();
    private readonly Dictionary<FoodItem, FoodVisual> _foodVisuals = new();
    private PlayerVisual? _playerVisual;

    private int _worldSeed = 1;

    /// <summary>Current terrain-generation config — the map panel replaces this then resets.</summary>
    public MapGenConfig MapConfig { get; private set; } = new()
    {
        Width = 96,
        Depth = 96,
        LakeCount = 2,
        LakeRadius = 8,
        RockClusters = 3,
        BiomeSeedCount = 6,
    };

    public override void _Ready()
    {
        // Terrain view: a child so its meshes/visuals sit under the host. It loads biomes in its
        // own _Ready; we ignore its baked map and drive it with a freshly generated one below.
        _mapView = new MapView { Name = "MapView" };
        AddChild(_mapView);

        Creatures = LoadCreatures(CreaturesPath);
        _sproutPlan = Creatures.Get("sprout");
        Genes = LoadGenes(GenesPath);
        _foodScene = ResourceLoader.Load<PackedScene>("res://scenes/Food.tscn");

        BuildWorld(_worldSeed);
    }

    /// <summary>
    /// Generate terrain for <paramref name="seed"/>, (re)build the <see cref="Simulator"/> sized to
    /// it, and seed food. Shared by first boot and the "Reset world" button.
    /// </summary>
    private void BuildWorld(int seed)
    {
        _worldSeed = seed;

        var map = MapGenerator.Generate(MapConfig, _mapView.Biomes, seed);
        _mapView.Rebuild(map);

        var arena = Arena.GroundArena(map.Width * map.CellSize, map.Depth * map.CellSize);
        Sim = new Simulator(arena, seed)
        {
            Map = map,
            Biomes = _mapView.Biomes,
            Foods = LoadFoods(FoodsPath),
            Genes = Genes,
        };
        Sim.SeedFood();

        Selected = null;
        Player = null;
        PlayerInput = null;
    }

    public override void _Process(double delta)
    {
        // --- time control ---
        if (!Paused)
            Sim.Tick(delta * TimeScale);
        else if (_stepPending)
        {
            Sim.Tick(1.0 / 60.0);
            _stepPending = false;
        }

        SyncVisuals(delta);
        UpdateSelectionMarker();
    }

    // -------------------------------------------------
    // Spawn / despawn seams (driven by the mode panels)
    // -------------------------------------------------

    /// <summary>Spawn one creature of the given species at a random arena position; returns it.</summary>
    public Blob SpawnSingle(string species)
    {
        var placement = new OverlapAvoidingPlacement(ArenaClampPlacement.Instance);
        float hx = (Sim.Arena.MaxX - Sim.Arena.MinX) / 2f - 1f;
        float hz = (Sim.Arena.MaxZ - Sim.Arena.MinZ) / 2f - 1f;
        float x = (float)(Sim.Rng.NextDouble() * 2 - 1) * hx;
        float z = (float)(Sim.Rng.NextDouble() * 2 - 1) * hz;

        var def = Creatures.GetDef(species);
        var blob = (Blob)Sim.Spawn(
            new SNVector3(x, 0f, z),
            new BlobFactory(Sim.Behavior, Sim.FleeStrategy, Sim.Rng),
            placement);
        blob.Body = def?.Body ?? _sproutPlan;
        return blob;
    }

    /// <summary>Spawn full data-driven herds for a species that carries a Herd config (e.g. sheep).</summary>
    public bool SpawnHerd(string species)
    {
        var def = Creatures.GetDef(species);
        if (def?.Herd is null || Sim.Map is null)
            return false;
        HerdSpawner.SpawnHerds(Sim, def, Sim.Map, Sim.Rng);
        return true;
    }

    /// <summary>Spawn the player avatar at world center (once).</summary>
    public Blob SpawnPlayer()
    {
        if (Player is not null)
            return Player;
        (Player, PlayerInput) = Sim.SpawnPlayer(SNVector3.Zero);
        Player.Body = _sproutPlan;
        return Player;
    }

    /// <summary>Remove every creature (and clear selection/player). Food + terrain are kept.</summary>
    public void DespawnAll()
    {
        Sim.Entities.Clear();
        Sim.Flocks.Clear();
        Selected = null;
        Player = null;
        PlayerInput = null;
    }

    /// <summary>Regenerate the whole world with a new seed — new terrain, empty of creatures.</summary>
    public void ResetWorld(int seed) => ResetWorld(seed, MapConfig);

    /// <summary>Regenerate with an explicit terrain config (map panel drives this from sliders).</summary>
    public void ResetWorld(int seed, MapGenConfig config)
    {
        MapConfig = config;
        foreach (var v in _visuals.Values) v.QueueFree();
        _visuals.Clear();
        foreach (var v in _foodVisuals.Values) v.QueueFree();
        _foodVisuals.Clear();
        _playerVisual?.QueueFree();
        _playerVisual = null;
        BuildWorld(seed);
    }

    // -------------------------------------------------
    // Selection (click-to-pick, math-only — visuals carry no colliders)
    // -------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Camera is null) return;

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
            PickAt(mb.Position);
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Tab, Echo: false })
            CycleSelection();
    }

    /// <summary>Ray from the camera through the screen point, intersect the y=0 plane, pick the
    /// nearest creature within a small radius of that hit.</summary>
    private void PickAt(Vector2 screen)
    {
        if (Camera is null) return;
        var origin = Camera.ProjectRayOrigin(screen);
        var dir = Camera.ProjectRayNormal(screen);
        if (Mathf.IsZeroApprox(dir.Y)) return;
        float t = -origin.Y / dir.Y;
        if (t < 0) return;
        var hit = origin + dir * t;
        var hit2 = new SNVector3(hit.X, 0f, hit.Z);

        Creature? best = null;
        float bestDist = 3f; // pick radius in world units
        foreach (var c in Sim.Entities)
        {
            float d = Vec.HorizDist(c.Position, hit2);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        if (best is not null)
            Selected = best;
    }

    /// <summary>Tab fallback: cycle through spawned creatures when a click misses.</summary>
    private void CycleSelection()
    {
        if (Sim.Entities.Count == 0) { Selected = null; return; }
        int idx = Selected is null ? -1 : Sim.Entities.IndexOf(Selected);
        Selected = Sim.Entities[(idx + 1) % Sim.Entities.Count];
    }

    private void UpdateSelectionMarker()
    {
        if (Selected is null || !Sim.Entities.Contains(Selected))
        {
            if (Selected is not null && !Sim.Entities.Contains(Selected))
                Selected = null;
            if (_selectionMarker is not null) _selectionMarker.Visible = false;
            return;
        }

        if (_selectionMarker is null)
        {
            _selectionMarker = new MeshInstance3D
            {
                Name = "SelectionMarker",
                Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.9f, 0.2f),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            AddChild(_selectionMarker);
        }

        _selectionMarker.Visible = true;
        _selectionMarker.Position = new Vector3(
            Selected.Position.X,
            Selected.Position.Y + Selected.Traits.Radius + 1.4f,
            Selected.Position.Z);
    }

    // -------------------------------------------------
    // Visual sync (mirrors VivariumMain._Process)
    // -------------------------------------------------

    private void SyncVisuals(double delta)
    {
        // Creatures: lazy-instantiate a CreatureVisual per Blob (it self-animates thereafter).
        foreach (var entity in Sim.Entities)
        {
            if (entity is not Blob blob || blob == Player) continue;
            if (!_visuals.ContainsKey(blob) && blob.Body is not null)
            {
                var visual = new CreatureVisual();
                AddChild(visual);
                visual.Init(blob);
                _visuals[blob] = visual;
            }
        }

        // Free visuals whose model was despawned.
        foreach (var (blob, visual) in _visuals.ToList())
        {
            if (!Sim.Entities.Contains(blob))
            {
                visual.QueueFree();
                _visuals.Remove(blob);
            }
        }

        // Player.
        if (Player is not null)
        {
            if (_playerVisual is null)
            {
                _playerVisual = new PlayerVisual();
                AddChild(_playerVisual);
                _playerVisual.Init(Player);
            }
            else
            {
                _playerVisual.SyncFromModel(delta);
            }
        }

        // Food.
        if (_foodScene is not null)
        {
            foreach (var item in Sim.Food)
            {
                if (!_foodVisuals.TryGetValue(item, out var fv))
                {
                    var instance = _foodScene.Instantiate<FoodVisual>();
                    AddChild(instance);
                    instance.Init(item);
                    _foodVisuals[item] = instance;
                }
                else
                {
                    fv.SyncFromModel();
                }
            }
        }
    }

    // -------------------------------------------------
    // Asset loading (FileAccess string seam, mirrors VivariumMain)
    // -------------------------------------------------

    private static CreatureCatalog LoadCreatures(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PrintErr($"HarnessSimHost: cannot open creatures '{path}'.");
            return CreatureCatalog.Empty;
        }
        try { return CreatureCatalog.Parse(file.GetAsText()); }
        catch (System.Exception e) { GD.PrintErr($"HarnessSimHost: creatures parse failed: {e.Message}"); return CreatureCatalog.Empty; }
    }

    private static FoodCatalog LoadFoods(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null) return FoodCatalog.Empty;
        try { return FoodCatalog.Parse(file.GetAsText()); }
        catch (System.Exception e) { GD.PrintErr($"HarnessSimHost: foods parse failed: {e.Message}"); return FoodCatalog.Empty; }
    }

    private static GeneCatalog LoadGenes(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null) return GeneCatalog.Empty;
        try { return GeneCatalog.Parse(file.GetAsText()); }
        catch (System.Exception e) { GD.PrintErr($"HarnessSimHost: genes parse failed: {e.Message}"); return GeneCatalog.Empty; }
    }
}
