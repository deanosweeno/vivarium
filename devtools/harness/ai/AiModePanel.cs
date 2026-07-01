using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Core;

namespace Vivarium.DevTools;

/// <summary>
/// Mode 1 — Creature AI. Spawn creatures by species, click one to inspect its live action + drive
/// needs, force it into any action via the <see cref="UtilityBrain.ForceAction"/> seam, and tweak
/// the shared <see cref="BehaviorConfig"/> sub-records live (every creature reads them by reference,
/// so sliders take effect next tick).
/// </summary>
public partial class AiModePanel : VBoxContainer, IHarnessPanel
{
    public string ModeName => "Creature AI";

    private HarnessSimHost _host = null!;
    private OptionButton _species = null!;
    private RichTextLabel _inspector = null!;

    // Per-creature editors: each re-points at the current selection and re-seeds its value when the
    // selection changes. get/set read+write the selected creature's own mutable Needs/Traits.
    private readonly record struct CreatureField(
        HSlider Slider, Label Label, string Name, Func<Creature, float> Get, Action<Creature, float> Set);
    private readonly List<CreatureField> _fields = new();
    private CheckButton _canFly = null!;
    private Label _selHint = null!;
    private Creature? _lastSelected;

    // Every action name in the default table — force-action buttons.
    private static readonly string[] ActionNames =
    {
        "Wander", "Approach", "Flee", "Rest", "Forage",
        "Flock", "SeekFlock", "Frolic", "FleePlayer", "FollowPlayer",
    };

    public void Build(HarnessSimHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(HarnessUi.Heading("Creature AI"));

        // --- spawn row ---
        _species = new OptionButton();
        foreach (var id in host.Creatures.Ids)
            _species.AddItem(id);
        AddChild(_species);

        var spawnRow = new HBoxContainer();
        spawnRow.AddChild(HarnessUi.Button("Spawn one", () => _host.SpawnSingle(SelectedSpecies())));
        spawnRow.AddChild(HarnessUi.Button("Spawn herd", () => _host.SpawnHerd(SelectedSpecies())));
        spawnRow.AddChild(HarnessUi.Button("Despawn all", () => _host.DespawnAll()));
        AddChild(spawnRow);

        // --- force-action grid ---
        AddChild(HarnessUi.Label("Force action (on selected):"));
        var grid = new GridContainer { Columns = 3 };
        foreach (var name in ActionNames)
        {
            string captured = name;
            grid.AddChild(HarnessUi.Button(captured, () => _host.Selected?.Brain?.ForceAction(captured)));
        }
        AddChild(grid);

        // --- live config sliders (bind to shared BehaviorConfig sub-records) ---
        AddChild(HarnessUi.Heading("Global tunables (all creatures)"));
        var b = _host.Sim.Behavior;
        AddChild(HarnessUi.Slider("HungerGainPerSec", 0f, 0.05f, 0.001f, b.Need.HungerGainPerSec,
            v => _host.Sim.Behavior.Need = _host.Sim.Behavior.Need with { HungerGainPerSec = v }));
        AddChild(HarnessUi.Slider("BoredomGainPerSec", 0f, 0.1f, 0.001f, b.Need.BoredomGainPerSec,
            v => _host.Sim.Behavior.Need = _host.Sim.Behavior.Need with { BoredomGainPerSec = v }));
        AddChild(HarnessUi.Slider("SwitchMargin", 0f, 1f, 0.01f, b.Brain.SwitchMargin,
            v => _host.Sim.Behavior.Brain = _host.Sim.Behavior.Brain with { SwitchMargin = v }));
        AddChild(HarnessUi.Slider("DecisionNoise", 0f, 0.5f, 0.01f, b.Brain.DecisionNoise,
            v => _host.Sim.Behavior.Brain = _host.Sim.Behavior.Brain with { DecisionNoise = v }));
        AddChild(HarnessUi.Slider("SenseRadius", 1f, 30f, 0.5f, b.Brain.SenseRadius,
            v => _host.Sim.Behavior.Brain = _host.Sim.Behavior.Brain with { SenseRadius = v }));
        AddChild(HarnessUi.Slider("FlockBaseRadius", 0.5f, 10f, 0.1f, b.Flock.FlockBaseRadius,
            v => _host.Sim.Behavior.Flock = _host.Sim.Behavior.Flock with { FlockBaseRadius = v }));

        // --- per-selected-creature editors (mutate that creature's own Needs/Traits) ---
        AddChild(new HSeparator());
        AddChild(HarnessUi.Heading("Selected creature"));
        _selHint = HarnessUi.Label("(no selection — left-click a creature, or Tab to cycle)");
        AddChild(_selHint);

        AddChild(HarnessUi.Label("needs:"));
        Field("Hunger", 0f, 1f, 0.01f, c => c.Needs.Hunger, (c, v) => c.Needs.Hunger = v);
        Field("Fatigue", 0f, 1f, 0.01f, c => c.Needs.Fatigue, (c, v) => c.Needs.Fatigue = v);
        Field("Boredom", 0f, 1f, 0.01f, c => c.Needs.Boredom, (c, v) => c.Needs.Boredom = v);
        Field("Affection", 0f, 1f, 0.01f, c => c.Needs.Affection, (c, v) => c.Needs.Affection = v);

        AddChild(HarnessUi.Label("traits:"));
        Field("MaxSpeed", 0f, 3f, 0.05f, c => c.Traits.MaxSpeed, (c, v) => c.Traits.MaxSpeed = v);
        Field("JumpHeight", 0f, 4f, 0.05f, c => c.Traits.JumpHeight, (c, v) => c.Traits.JumpHeight = v);
        Field("Acceleration", 0f, 8f, 0.1f, c => c.Traits.Acceleration, (c, v) => c.Traits.Acceleration = v);
        Field("TurnRate", 0f, 10f, 0.1f, c => c.Traits.TurnRate, (c, v) => c.Traits.TurnRate = v);
        Field("Radius", 0.1f, 2f, 0.05f, c => c.Traits.Radius, (c, v) => c.Traits.Radius = v);
        Field("GravityScale", -1f, 2f, 0.05f, c => c.Traits.GravityScale, (c, v) => c.Traits.GravityScale = v);
        Field("FatigueGainPerSec", 0f, 0.3f, 0.005f, c => c.Traits.FatigueGainPerSec, (c, v) => c.Traits.FatigueGainPerSec = v);
        Field("GrazeHungerThreshold", 0f, 1f, 0.01f, c => c.Traits.GrazeHungerThreshold, (c, v) => c.Traits.GrazeHungerThreshold = v);

        // CanFly is a data flag only — movement mode (Walk/Fly) is chosen at spawn, so toggling it
        // live sets the trait for inspection/next-spawn but does not hot-swap the active mode.
        _canFly = HarnessUi.Toggle("CanFly (next spawn — no live mode swap)", false,
            on => { if (_host.Selected is { } c) c.Traits.CanFly = on; });
        _canFly.Disabled = true;
        AddChild(_canFly);

        AddChild(HarnessUi.Button("Randomize needs",
            () => _host.Selected?.Needs.Randomize(_host.Sim.Rng)));

        // --- inspector ---
        AddChild(new HSeparator());
        AddChild(HarnessUi.Label("Inspector (click a creature):"));
        _inspector = new RichTextLabel
        {
            FitContent = true,
            CustomMinimumSize = new Vector2(240, 120),
            BbcodeEnabled = false,
        };
        AddChild(_inspector);
    }

    public void OnEnter() { Visible = true; SetProcess(true); }
    public void OnExit() { Visible = false; }

    public void Refresh(double delta)
    {
        var sel = _host.Selected;

        // Re-seed the per-creature editors when the selection changes (or clears), so each slider
        // shows the newly selected creature's own value. SetValueNoSignal avoids re-firing the
        // mutation callback back into the creature.
        if (!ReferenceEquals(sel, _lastSelected))
        {
            _lastSelected = sel;
            bool has = sel is not null;
            _selHint.Visible = !has;
            foreach (var f in _fields)
            {
                f.Slider.Editable = has;
                if (has) f.Slider.SetValueNoSignal(f.Get(sel!));
                f.Label.Text = has ? $"{f.Name}: {f.Get(sel!):0.###}" : $"{f.Name}: —";
            }
            _canFly.Disabled = !has;
            if (has) _canFly.SetPressedNoSignal(sel!.Traits.CanFly);
        }

        _inspector.Text = sel is { } c
            ? Inspect.CreatureReadout(c)
            : "(no selection — left-click a creature, or Tab to cycle)";
    }

    /// <summary>Build a per-creature slider that writes into the current selection and register it
    /// for reload-on-selection.</summary>
    private void Field(string name, float min, float max, float step,
        Func<Creature, float> get, Action<Creature, float> set)
    {
        // No selection at build time — seed at min; Refresh re-seeds once a creature is selected.
        float seed = _host.Selected is { } s ? get(s) : min;
        var (box, slider, label) = HarnessUi.LiveSlider(name, min, max, step, seed,
            v => { if (_host.Selected is { } c) set(c, v); });
        slider.Editable = _host.Selected is not null;
        AddChild(box);
        _fields.Add(new CreatureField(slider, label, name, get, set));
    }

    private string SelectedSpecies()
        => _species.ItemCount > 0 ? _species.GetItemText(_species.Selected) : "sprout";
}
