using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Core;

namespace Vivarium.DevTools;

/// <summary>
/// Mode 4 — Genetics (§3 harvest / pool / craft / splice), run in isolation. Owns its own
/// <see cref="GenePool"/> + <see cref="GeneticsConfig"/> and calls the pure core functions directly
/// (<see cref="HarvestTable.Roll"/> / <see cref="Craft.CraftBase"/> / <see cref="Splicer.Splice"/> /
/// <see cref="Expressor.Express"/>). Splice produces a phenotype text readout only — wiring a spliced
/// genome into a live sim creature is the still-open §8 spawn integration and is deliberately out of
/// scope here (see the TODO below).
/// </summary>
public partial class GeneticsModePanel : VBoxContainer, IHarnessPanel
{
    public string ModeName => "Genetics";

    private HarnessSimHost _host = null!;
    private OptionButton _species = null!;
    private ItemList _collected = null!;
    private RichTextLabel _readout = null!;

    private readonly GenePool _pool = new();
    private GeneticsConfig _cfg = GeneticsConfig.Default;
    private readonly Dictionary<string, Gene> _craftedBase = new();

    public void Build(HarnessSimHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(HarnessUi.Heading("Genetics (harvest / craft / splice)"));

        _species = new OptionButton();
        foreach (var id in host.Creatures.Ids)
            if (host.Genes.GenesFor(id).Count > 0)
                _species.AddItem(id);
        AddChild(_species);

        var row = new HBoxContainer();
        row.AddChild(HarnessUi.Button("Harvest", Harvest));
        row.AddChild(HarnessUi.Button("Craft base", CraftBase));
        row.AddChild(HarnessUi.Button("Splice", Splice));
        AddChild(row);

        AddChild(HarnessUi.Slider("SpliceBudget", 1, 6, 1, _cfg.DefaultSpliceBudget,
            v => _cfg = _cfg with { DefaultSpliceBudget = (int)v }));
        AddChild(HarnessUi.Slider("MaxDrops", 1, 6, 1, _cfg.MaxDrops,
            v => _cfg = _cfg with { MaxDrops = (int)v }));

        AddChild(HarnessUi.Label("Collected genes:"));
        _collected = new ItemList { CustomMinimumSize = new Vector2(240, 120) };
        AddChild(_collected);

        AddChild(HarnessUi.Label("Result:"));
        _readout = new RichTextLabel
        {
            FitContent = true,
            CustomMinimumSize = new Vector2(240, 140),
            BbcodeEnabled = false,
            Text = "(harvest a species, collect its full Common set, craft its base, then splice)",
        };
        AddChild(_readout);
    }

    public void OnEnter() { Visible = true; SetProcess(true); }
    public void OnExit() { Visible = false; }
    public void Refresh(double delta) { /* event-driven; nothing per-frame */ }

    private string Species()
        => _species.ItemCount > 0 ? _species.GetItemText(_species.Selected) : "";

    private void Harvest()
    {
        string sp = Species();
        if (sp == "") return;
        var drops = HarvestTable.Roll(sp, _host.Genes, _cfg, _host.Sim.Rng);
        int added = drops.Count(_pool.Add);
        RefreshCollected();
        var missing = _pool.Missing(sp, _host.Genes);
        _readout.Text = $"harvested {drops.Count} ({added} new).\n"
            + (missing.Count == 0
                ? $"'{sp}' Common set COMPLETE — craft its base."
                : $"still missing: {string.Join(", ", missing)}");
    }

    private void CraftBase()
    {
        string sp = Species();
        if (sp == "") return;
        try
        {
            var baseGene = Craft.CraftBase(sp, _pool, _host.Creatures, _host.Genes);
            _craftedBase[sp] = baseGene;
            _readout.Text = $"crafted base gene '{baseGene.Id}' for '{sp}'. Now Splice.";
        }
        catch (System.InvalidOperationException e)
        {
            _readout.Text = e.Message;
        }
    }

    private void Splice()
    {
        string sp = Species();
        if (sp == "") return;
        if (!_craftedBase.TryGetValue(sp, out var baseGene))
        {
            _readout.Text = "no crafted base for this species yet — Craft base first.";
            return;
        }

        var specialty = _pool.Collected
            .Where(g => g.Kind == GeneKind.Specialty && g.SourceSpecies == sp)
            .Take(_cfg.DefaultSpliceBudget)
            .ToList();

        var envelope = _host.Creatures.GetDef(sp)?.Body is { } body ? BodyEnvelope.From(body) : null;
        var genome = Splicer.Splice(baseGene, specialty, _cfg, envelope);
        var pheno = Expressor.Express(genome, _host.Sim.Rng);

        _readout.Text =
            $"spliced '{sp}' base + {specialty.Count} specialty:\n"
            + $"  body    : {pheno.Body.Name}\n"
            + $"  parts   : {pheno.Body.Parts.Count}\n"
            + $"  maxSpeed: {pheno.Traits.MaxSpeed:0.###}\n"
            + $"  radius  : {pheno.Traits.Radius:0.###}\n"
            + $"  social  : {pheno.Drives.Sociability:0.###}\n"
            + $"  specialty: {string.Join(", ", specialty.Select(g => g.Id))}";
        // TODO §8: a "Spawn this genome into the live sim" button hooks in here once the
        // genotype→live-creature spawn path lands (see docs/features/splicing.md §8).
    }

    private void RefreshCollected()
    {
        _collected.Clear();
        foreach (var g in _pool.Collected.OrderBy(g => g.SourceSpecies).ThenBy(g => g.Id))
            _collected.AddItem($"{g.Id}  [{g.Rarity}]");
    }
}
