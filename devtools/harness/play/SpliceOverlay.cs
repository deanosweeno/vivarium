using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Core;
using FileAccess = Godot.FileAccess;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.DevTools;

/// <summary>
/// Pull-up splice UI for <see cref="PlayModePanel"/> — craft/splice against the player's live
/// <see cref="GenePool"/> (<see cref="PlayerInputMode.Pool"/>) instead of a mode-local one, so the
/// harvest verb and this overlay share state. Pauses the sim while open (restoring the prior pause
/// state on close) and persists the pool to <see cref="PoolSavePath"/> on demand. Adapted from
/// <see cref="GeneticsModePanel"/>'s craft/splice flow.
/// </summary>
public partial class SpliceOverlay : CanvasLayer
{
    public const string PoolSavePath = "user://genepool.json";

    private HarnessSimHost _host = null!;
    private PanelContainer _panel = null!;
    private OptionButton _species = null!;
    private VBoxContainer _specialtyBox = null!;
    private RichTextLabel _readout = null!;
    private readonly List<CheckButton> _specialtyChecks = new();
    private readonly Dictionary<string, Gene> _craftedBase = new();
    private bool _pausedBeforeOpen;

    public void Build(HarnessSimHost host)
    {
        _host = host;
        Layer = 10;
        Visible = false;

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(_panel);

        var (scroll, body) = HarnessUi.ScrollBody();
        scroll.CustomMinimumSize = new Vector2(320, 420);
        _panel.AddChild(scroll);

        body.AddChild(HarnessUi.Heading("Splice"));

        _species = new OptionButton();
        _species.ItemSelected += _ => RefreshSpecialtyList();
        body.AddChild(_species);

        var row1 = new HBoxContainer();
        row1.AddChild(HarnessUi.Button("Craft base", CraftBase));
        row1.AddChild(HarnessUi.Button("Splice + Spawn", SpliceAndSpawn));
        body.AddChild(row1);

        body.AddChild(HarnessUi.Label($"Specialty (max {GeneticsConfig.Default.DefaultSpliceBudget}):"));
        _specialtyBox = new VBoxContainer();
        body.AddChild(_specialtyBox);

        var row2 = new HBoxContainer();
        row2.AddChild(HarnessUi.Button("Save pool", SavePool));
        row2.AddChild(HarnessUi.Button("Load pool", LoadPool));
        row2.AddChild(HarnessUi.Button("Close", Close));
        body.AddChild(row2);

        _readout = new RichTextLabel
        {
            FitContent = true,
            CustomMinimumSize = new Vector2(280, 100),
            BbcodeEnabled = false,
            Text = "(collect genes with H, then craft + splice here)",
        };
        body.AddChild(_readout);
    }

    public bool IsOpen => Visible;

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Open()
    {
        _pausedBeforeOpen = _host.Paused;
        _host.Paused = true;
        RefreshSpeciesList();
        Visible = true;
    }

    public void Close()
    {
        _host.Paused = _pausedBeforeOpen;
        Visible = false;
    }

    private GenePool Pool => _host.PlayerInput?.Pool ?? new GenePool();

    private void RefreshSpeciesList()
    {
        string? prev = Species();
        _species.Clear();
        var species = Pool.Collected.Select(g => g.SourceSpecies).Distinct().OrderBy(s => s).ToList();
        foreach (var sp in species)
            _species.AddItem(sp);

        if (species.Count > 0)
        {
            int idx = prev is not null ? species.IndexOf(prev) : -1;
            _species.Selected = idx >= 0 ? idx : 0;
        }
        RefreshSpecialtyList();
    }

    private void RefreshSpecialtyList()
    {
        foreach (var c in _specialtyChecks) c.QueueFree();
        _specialtyChecks.Clear();

        string sp = Species();
        if (sp == "") return;

        foreach (var gene in Pool.Collected.Where(g => g.SourceSpecies == sp).OrderBy(g => g.Id))
        {
            var check = HarnessUi.Toggle($"{gene.Id} [{gene.Rarity}]", value: false, onChanged: EnforceBudget);
            _specialtyBox.AddChild(check);
            _specialtyChecks.Add(check);
        }
    }

    private void EnforceBudget(bool _)
    {
        int budget = GeneticsConfig.Default.DefaultSpliceBudget;
        int checkedCount = _specialtyChecks.Count(c => c.ButtonPressed);
        if (checkedCount <= budget) return;

        // Uncheck the most recently pressed one over budget (last in visual order that's on).
        var over = _specialtyChecks.LastOrDefault(c => c.ButtonPressed);
        over?.SetPressedNoSignal(false);
    }

    private string Species() => _species.ItemCount > 0 && _species.Selected >= 0
        ? _species.GetItemText(_species.Selected)
        : "";

    private void CraftBase()
    {
        string sp = Species();
        if (sp == "") { _readout.Text = "no species with collected genes yet."; return; }
        try
        {
            var baseGene = Craft.CraftBase(sp, Pool, _host.Creatures, _host.Genes);
            _craftedBase[sp] = baseGene;
            _readout.Text = $"crafted base gene '{baseGene.Id}' for '{sp}'.";
        }
        catch (System.InvalidOperationException e)
        {
            _readout.Text = e.Message;
        }
    }

    private void SpliceAndSpawn()
    {
        string sp = Species();
        if (sp == "") { _readout.Text = "no species selected."; return; }
        if (!_craftedBase.TryGetValue(sp, out var baseGene))
        {
            _readout.Text = "no crafted base for this species yet — Craft base first.";
            return;
        }
        if (_host.Player is not { } player)
        {
            _readout.Text = "no player avatar — spawn one first.";
            return;
        }

        var specialty = _specialtyChecks
            .Zip(Pool.Collected.Where(g => g.SourceSpecies == sp).OrderBy(g => g.Id), (check, gene) => (check, gene))
            .Where(p => p.check.ButtonPressed)
            .Select(p => p.gene)
            .Take(GeneticsConfig.Default.DefaultSpliceBudget)
            .ToList();

        var envelope = _host.Creatures.GetDef(sp)?.Body is { } body ? BodyEnvelope.From(body) : null;
        var genome = Splicer.Splice(baseGene, specialty, GeneticsConfig.Default, envelope);
        var pheno = Expressor.Express(genome, _host.Sim.Rng);
        var pos = player.Position + new SNVector3(1.5f, 0f, 0f);
        var creature = _host.Sim.SpawnFromPhenotype(pos, pheno, genome);

        _readout.Text = $"spliced + spawned '{pheno.Body.Name}' with {specialty.Count} specialty gene(s) "
            + $"at ({creature.Position.X:0.#}, {creature.Position.Z:0.#}).";
    }

    private void SavePool()
    {
        if (_host.PlayerInput is null) { _readout.Text = "no player avatar."; return; }
        using var file = FileAccess.Open(PoolSavePath, FileAccess.ModeFlags.Write);
        if (file is null) { _readout.Text = $"save failed: {FileAccess.GetOpenError()}"; return; }
        file.StoreString(_host.PlayerInput.Pool.Save());
        _readout.Text = $"saved {_host.PlayerInput.Pool.Collected.Count} genes to {PoolSavePath}.";
    }

    private void LoadPool()
    {
        if (_host.PlayerInput is null) { _readout.Text = "no player avatar."; return; }
        using var file = FileAccess.Open(PoolSavePath, FileAccess.ModeFlags.Read);
        if (file is null) { _readout.Text = $"no saved pool found ({PoolSavePath})."; return; }
        try
        {
            _host.PlayerInput.Pool = GenePool.Load(file.GetAsText(), _host.Genes);
            RefreshSpeciesList();
            _readout.Text = $"loaded {_host.PlayerInput.Pool.Collected.Count} genes.";
        }
        catch (System.Exception e)
        {
            _readout.Text = $"load failed: {e.Message}";
        }
    }
}
