using System.Linq;
using Godot;
using Vivarium.Core;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.Scripts;

/// <summary>
/// Player-facing splice screen: drag a base gene into the center slot and up to
/// <see cref="GeneticsConfig.DefaultSpliceBudget"/> specialty genes into the ring, then Splice to
/// spawn a hybrid. Opened/closed via <see cref="Toggle"/> (bound to Tab in
/// <see cref="VivariumMain"/>), which also pauses the sim. Layout (slot positions, tray sizing)
/// lives entirely in <c>splice_ui.tscn</c> — free to redesign in the Godot editor without
/// touching this script, since every node is looked up by exported NodePath.
/// </summary>
public partial class SpliceUi : CanvasLayer
{
    [Export] private NodePath _baseSlotPath = new();
    [Export] private NodePath[] _ringSlotPaths = System.Array.Empty<NodePath>();
    [Export] private NodePath _baseTrayPath = new();
    [Export] private NodePath _specialtyTrayPath = new();
    [Export] private NodePath _spliceButtonPath = new();
    [Export] private NodePath _closeButtonPath = new();
    [Export] private NodePath _statusLabelPath = new();

    private GeneSlotView _baseSlot = null!;
    private GeneSlotView[] _ringSlots = null!;
    private GeneTrayView _baseTray = null!;
    private GeneTrayView _specialtyTray = null!;
    private Button _spliceButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;

    private ISpliceHost _host = null!;
    private bool _pausedBeforeOpen;

    public override void _Ready()
    {
        _baseSlot = GetNode<GeneSlotView>(_baseSlotPath);
        _ringSlots = _ringSlotPaths.Select(GetNode<GeneSlotView>).ToArray();
        _baseTray = GetNode<GeneTrayView>(_baseTrayPath);
        _specialtyTray = GetNode<GeneTrayView>(_specialtyTrayPath);
        _spliceButton = GetNode<Button>(_spliceButtonPath);
        _closeButton = GetNode<Button>(_closeButtonPath);
        _statusLabel = GetNode<Label>(_statusLabelPath);

        _baseSlot.Role = GeneSlotView.SlotRole.BaseSlot;
        foreach (var slot in _ringSlots)
            slot.Role = GeneSlotView.SlotRole.RingSlot;

        int budget = GeneticsConfig.Default.DefaultSpliceBudget;
        for (int i = 0; i < _ringSlots.Length; i++)
            _ringSlots[i].Locked = i >= budget;

        _spliceButton.Pressed += OnSplicePressed;
        _closeButton.Pressed += Close;

        Visible = false;
    }

    /// <summary>Wires the splice UI to its host so it can reach the player's pool, the sim, and
    /// the creature catalog (for splice envelope lookup).</summary>
    public void Init(ISpliceHost host) => _host = host;

    public bool IsOpen => Visible;

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Open()
    {
        Populate();
        _pausedBeforeOpen = _host.Paused;
        _host.Paused = true;
        Visible = true;
    }

    public void Close()
    {
        _host.Paused = _pausedBeforeOpen;
        Visible = false;
    }

    private void Populate()
    {
        _baseTray.Clear();
        _specialtyTray.Clear();
        _baseSlot.SetGene(null);
        foreach (var slot in _ringSlots)
            slot.SetGene(null);

        var pool = _host.PlayerInput?.Pool;
        if (pool is null)
        {
            _statusLabel.Text = "No player avatar.";
            return;
        }

        foreach (var gene in pool.Collected.Where(g => g.Kind == GeneKind.Base))
            _baseTray.AddChip(gene);
        foreach (var gene in pool.Collected.Where(g => g.Kind == GeneKind.Specialty))
            _specialtyTray.AddChip(gene);

        _statusLabel.Text = "Drag a base gene to the center, specialty genes to the ring, then Splice.";
    }

    private void OnSplicePressed()
    {
        if (_baseSlot.Gene is not { } baseGene)
        {
            _statusLabel.Text = "Pick a base gene first.";
            return;
        }

        var specialty = _ringSlots
            .Where(s => !s.Locked && s.Gene is not null)
            .Select(s => s.Gene!)
            .ToList();

        try
        {
            var envelope = _host.Creatures.GetDef(baseGene.SourceSpecies)?.Body is { } body
                ? BodyEnvelope.From(body)
                : null;
            var genome = Splicer.Splice(baseGene, specialty, GeneticsConfig.Default, envelope);
            var pheno = Expressor.Express(genome, _host.Sim.Rng);
            var spawnPos = _host.PlayerPosition + new SNVector3(1.5f, 0f, 0f);
            var creature = _host.Sim.SpawnFromPhenotype(spawnPos, pheno, genome);

            _statusLabel.Text = $"Spliced '{pheno.Body.Name}' with {specialty.Count} specialty gene(s) "
                + $"at ({creature.Position.X:0.#}, {creature.Position.Z:0.#}).";
        }
        catch (System.ArgumentException e)
        {
            _statusLabel.Text = $"Splice failed: {e.Message}";
        }
    }
}
