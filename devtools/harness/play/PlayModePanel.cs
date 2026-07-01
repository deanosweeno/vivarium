using System.Linq;
using Godot;
using Vivarium.Core;
using Vivarium.Scripts;
using SNVector2 = System.Numerics.Vector2;

namespace Vivarium.DevTools;

/// <summary>
/// Mode 5 — Play: a controllable avatar with a real input scheme (WASD + verb keys), camera
/// follows the player instead of the click-selection. Mirrors <see cref="Vivarium.Scripts.VivariumMain"/>'s
/// <c>UpdatePlayerInput</c>/<c>_UnhandledKeyInput</c> (camera-relative move, F/G/1/2/3 verbs), and adds
/// <b>H</b> for the new <c>harvest</c> verb plus <b>Tab</b> to pull up the real, in-game
/// <see cref="Vivarium.Scripts.SpliceUi"/> (same scene the shipped game uses).
/// This is the "actually play it" mode — <see cref="PlayerModePanel"/> stays a button-driven inspector.
/// </summary>
public partial class PlayModePanel : VBoxContainer, IHarnessPanel
{
    public string ModeName => "Play";

    private HarnessSimHost _host = null!;
    private RichTextLabel _inspector = null!;
    private Label _poolSummary = null!;
    private SpliceUi _overlay = null!;
    private bool _active;

    public void Build(HarnessSimHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(HarnessUi.Heading("Play"));
        AddChild(HarnessUi.Label(
            "WASD move · F pickup · G place · 1 feed · 2 soothe · 3 play · H harvest · Tab splice"));

        var row = new HBoxContainer();
        row.AddChild(HarnessUi.Button("Give food", GiveFood));
        row.AddChild(HarnessUi.Button("Splice…", () => _overlay.Toggle()));
        AddChild(row);

        AddChild(HarnessUi.Label("Nearest target:"));
        _inspector = new RichTextLabel
        {
            FitContent = true,
            CustomMinimumSize = new Vector2(240, 120),
            BbcodeEnabled = false,
        };
        AddChild(_inspector);

        AddChild(HarnessUi.Label("Gene pool:"));
        _poolSummary = new Label();
        AddChild(_poolSummary);

        var spliceScene = ResourceLoader.Load<PackedScene>("res://scenes/splice_ui.tscn");
        _overlay = spliceScene.Instantiate<SpliceUi>();
        AddChild(_overlay);
        _overlay.Init(host);
    }

    public void OnEnter()
    {
        Visible = true;
        SetProcess(true);
        _active = true;
        _host.SpawnPlayer();
        if (_host.PlayerInput is { } input)
            GenePoolSeed.FillAll(input.Pool, _host.Creatures, _host.Genes);
    }

    public void OnExit()
    {
        Visible = false;
        _active = false;
        _overlay.Close();
    }

    public void Refresh(double delta)
    {
        if (!_active) return;

        DriveMoveInput();

        if (_host.Camera is CameraOrbit orbit && _host.Player is { } player)
            orbit.Target = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);

        _inspector.Text = NearestTargetReadout();
        _poolSummary.Text = PoolSummary();
    }

    /// <summary>Camera-relative WASD, mirroring <see cref="Vivarium.Scripts.VivariumMain"/>.UpdatePlayerInput.</summary>
    private void DriveMoveInput()
    {
        if (_host.PlayerInput is null) return;

        var move = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        float yaw = (_host.Camera as CameraOrbit)?.Yaw ?? 0f;
        float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
        float wx = move.X * cos + move.Y * sin;
        float wz = -move.X * sin + move.Y * cos;
        _host.PlayerInput.MoveInput = new SNVector2(wx, wz);
    }

    // _Input (not _UnhandledKeyInput) so Tab reaches us before Godot's default Control focus
    // traversal eats it — a focused button (the common case after any click in this harness)
    // would otherwise swallow Tab as "focus next" and it would never reach unhandled input.
    public override void _Input(InputEvent @event)
    {
        if (!_active || _host.PlayerInput is null) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } k) return;

        switch (k.Keycode)
        {
            case Key.F: _host.PlayerInput.QueueIntent("pickup"); break;
            case Key.G: _host.PlayerInput.QueueIntent("place"); break;
            case Key.Key1: _host.PlayerInput.QueueIntent("feed"); break;
            case Key.Key2: _host.PlayerInput.QueueIntent("soothe"); break;
            case Key.Key3: _host.PlayerInput.QueueIntent("play"); break;
            case Key.H: _host.PlayerInput.QueueIntent("harvest"); break;
            case Key.Tab:
                _overlay.Toggle();
                break;
            default: return;
        }
        GetViewport().SetInputAsHandled(); // don't also let GUI focus traversal / the host's Tab-cycle see it
    }

    private void GiveFood()
    {
        if (_host.PlayerInput is null) return;
        _host.PlayerInput.CarriedFood = _host.Sim.Food.FirstOrDefault()?.Def;
    }

    private static Creature? NearestTarget(HarnessSimHost host)
    {
        if (host.Player is null) return null;
        return Vec.NearestBy(
            host.Sim.Entities, host.Player.Position,
            e => e.Position,
            e => !ReferenceEquals(e, host.Player) && !e.IsPlayer && e.Brain is not null,
            host.Sim.Behavior.Interaction.InteractReach).Item;
    }

    private string NearestTargetReadout()
    {
        if (_host.Player is null) return "(no player)";
        var target = NearestTarget(_host);
        return target is null ? "(nothing in reach)" : Inspect.CreatureReadout(target);
    }

    private string PoolSummary()
    {
        var pool = _host.PlayerInput?.Pool;
        if (pool is null || pool.Collected.Count == 0) return "(empty)";

        var bySpecies = pool.Collected.GroupBy(g => g.SourceSpecies);
        var lines = bySpecies.Select(grp =>
        {
            var missing = pool.Missing(grp.Key, _host.Genes);
            string status = missing.Count == 0 ? "full set" : $"missing {missing.Count}";
            return $"{grp.Key}: {grp.Count()} genes ({status})";
        });
        return string.Join("\n", lines);
    }
}
