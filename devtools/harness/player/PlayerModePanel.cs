using System.Linq;
using Godot;
using Vivarium.Core;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.DevTools;

/// <summary>
/// Mode 2 — Player interactions / taming. Spawn the avatar, warp it next to the selected creature,
/// and drive Feed/Soothe/Play through the real intent path (<see cref="PlayerInputMode.QueueIntent"/>
/// → <see cref="Simulator.Tick"/> → PlayerController). Inspector shows the selected creature's bond /
/// broadcast / reaction; sliders tune the shared <see cref="InteractionConfig"/> live.
/// </summary>
public partial class PlayerModePanel : VBoxContainer, IHarnessPanel
{
    public string ModeName => "Player";

    private HarnessSimHost _host = null!;
    private RichTextLabel _inspector = null!;

    public void Build(HarnessSimHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(HarnessUi.Heading("Player Interactions"));

        var row1 = new HBoxContainer();
        row1.AddChild(HarnessUi.Button("Spawn player", () => _host.SpawnPlayer()));
        row1.AddChild(HarnessUi.Button("Warp to selected", WarpToSelected));
        AddChild(row1);

        var row2 = new HBoxContainer();
        row2.AddChild(HarnessUi.Button("Give food", GiveFood));
        row2.AddChild(HarnessUi.Button("Feed", () => Intent("feed", food: true)));
        row2.AddChild(HarnessUi.Button("Soothe", () => Intent("soothe")));
        row2.AddChild(HarnessUi.Button("Play", () => Intent("play")));
        AddChild(row2);

        AddChild(HarnessUi.Label("Live tunables:"));
        var it = _host.Sim.Behavior.Interaction;
        AddChild(HarnessUi.Slider("InteractReach", 0.5f, 6f, 0.1f, it.InteractReach,
            v => _host.Sim.Behavior.Interaction = _host.Sim.Behavior.Interaction with { InteractReach = v }));
        AddChild(HarnessUi.Slider("FeedBond", 0f, 1f, 0.01f, it.FeedBond,
            v => _host.Sim.Behavior.Interaction = _host.Sim.Behavior.Interaction with { FeedBond = v }));
        AddChild(HarnessUi.Slider("PartialBondThreshold", 0f, 1f, 0.01f, it.PartialBondThreshold,
            v => _host.Sim.Behavior.Interaction = _host.Sim.Behavior.Interaction with { PartialBondThreshold = v }));
        AddChild(HarnessUi.Slider("PlayBond", 0f, 1f, 0.01f, it.PlayBond,
            v => _host.Sim.Behavior.Interaction = _host.Sim.Behavior.Interaction with { PlayBond = v }));

        AddChild(HarnessUi.Label("Inspector (selected creature):"));
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
        string carried = _host.PlayerInput?.CarriedFood?.Name ?? "empty";
        string head = _host.Player is null
            ? "(no player — Spawn player first)\n"
            : $"player food: {carried}\n\n";
        _inspector.Text = head + (_host.Selected is { } c
            ? Inspect.CreatureReadout(c)
            : "(no selection — click a creature)");
    }

    private void WarpToSelected()
    {
        if (_host.Player is null || _host.Selected is null) return;
        var s = _host.Selected.Position;
        _host.Player.Position = new SNVector3(s.X + 1f, s.Y, s.Z);
    }

    private void GiveFood()
    {
        if (_host.PlayerInput is null) return;
        _host.PlayerInput.CarriedFood = _host.Sim.Food.FirstOrDefault()?.Def;
    }

    private void Intent(string id, bool food = false)
    {
        if (_host.PlayerInput is null) return;
        if (food && _host.PlayerInput.CarriedFood is null)
            GiveFood();
        _host.PlayerInput.QueueIntent(id);
    }
}
