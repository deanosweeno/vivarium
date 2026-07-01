using System.Collections.Generic;
using System.Text;
using Godot;
using Vivarium.Core;

namespace Vivarium.DevTools;

/// <summary>
/// Mode 3 — Map / terrain. Regenerate terrain deterministically from a seed + a handful of the
/// most visually legible <see cref="MapGenConfig"/> knobs (lakes, rocks, biome patches), rebuilding
/// the mesh through <see cref="Vivarium.Scripts.MapView.Rebuild"/>. Inspector tallies terrain/biome
/// cells so a config change's effect is legible without eyeballing the mesh. Decoupled from the
/// creature sim — a good de-risking target for the shared shell.
/// </summary>
public partial class MapModePanel : VBoxContainer, IHarnessPanel
{
    public string ModeName => "Map / Terrain";

    private HarnessSimHost _host = null!;
    private RichTextLabel _inspector = null!;
    private SpinBox _seed = null!;

    private int _lakeCount = 2, _lakeRadius = 8, _rockClusters = 3, _biomeSeeds = 6;

    public void Build(HarnessSimHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(HarnessUi.Heading("Map / Terrain"));

        var seedRow = new HBoxContainer();
        seedRow.AddChild(HarnessUi.Label("Seed:"));
        _seed = new SpinBox { MinValue = 0, MaxValue = 99999, Value = 1, Step = 1 };
        seedRow.AddChild(_seed);
        seedRow.AddChild(HarnessUi.Button("Regenerate", Regenerate));
        seedRow.AddChild(HarnessUi.Button("Random seed", () => { _seed.Value = GD.RandRange(0.0, 99999.0); Regenerate(); }));
        AddChild(seedRow);

        AddChild(HarnessUi.Slider("LakeCount", 0, 8, 1, _lakeCount, v => _lakeCount = (int)v));
        AddChild(HarnessUi.Slider("LakeRadius", 2, 24, 1, _lakeRadius, v => _lakeRadius = (int)v));
        AddChild(HarnessUi.Slider("RockClusters", 0, 12, 1, _rockClusters, v => _rockClusters = (int)v));
        AddChild(HarnessUi.Slider("BiomeSeedCount", 1, 16, 1, _biomeSeeds, v => _biomeSeeds = (int)v));

        AddChild(HarnessUi.Label("Cell tally:"));
        _inspector = new RichTextLabel
        {
            FitContent = true,
            CustomMinimumSize = new Vector2(240, 160),
            BbcodeEnabled = false,
        };
        AddChild(_inspector);
    }

    public void OnEnter() { Visible = true; SetProcess(true); }
    public void OnExit() { Visible = false; }

    // Terrain tally is static between regenerations — refresh cheaply on a timer, not every frame.
    private double _accum;
    public void Refresh(double delta)
    {
        _accum += delta;
        if (_accum < 0.5) return;
        _accum = 0;
        _inspector.Text = Tally();
    }

    private void Regenerate()
    {
        var config = new MapGenConfig
        {
            Width = _host.MapConfig.Width,
            Depth = _host.MapConfig.Depth,
            CellSize = _host.MapConfig.CellSize,
            LakeCount = _lakeCount,
            LakeRadius = _lakeRadius,
            RockClusters = _rockClusters,
            BiomeSeedCount = _biomeSeeds,
        };
        _host.ResetWorld((int)_seed.Value, config);
        _inspector.Text = Tally();
    }

    private string Tally()
    {
        var map = _host.Sim.Map;
        if (map is null) return "(no map)";

        var terrain = new Dictionary<Terrain, int>();
        var biome = new Dictionary<Biome, int>();
        for (int z = 0; z < map.Depth; z++)
        for (int x = 0; x < map.Width; x++)
        {
            var cell = map.GetCell(x, z);
            terrain[cell.Terrain] = terrain.GetValueOrDefault(cell.Terrain) + 1;
            biome[cell.Biome] = biome.GetValueOrDefault(cell.Biome) + 1;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"map: {map.Width}x{map.Depth}  seed applied\n");
        sb.AppendLine("terrain:");
        foreach (var (t, n) in terrain) sb.AppendLine($"  {t,-6}: {n}");
        sb.AppendLine("biome:");
        foreach (var (bm, n) in biome) sb.AppendLine($"  {bm,-8}: {n}");
        return sb.ToString();
    }
}
