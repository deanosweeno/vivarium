using Godot;
using Vivarium.Scripts;
using SNVector3 = System.Numerics.Vector3;

namespace Vivarium.DevTools;

/// <summary>
/// Root of the dev/QA harness scene. Builds the 3D stage (camera, light, environment), the shared
/// <see cref="HarnessSimHost"/>, and the whole Control UI in code — a top control strip (mode switch
/// + time control + world reset) plus the four mode panels. Only ever toggles panel visibility and
/// forwards a per-frame <see cref="IHarnessPanel.Refresh"/> to the active panel; all mechanic logic
/// lives in the panels + the sim host. This scene is never referenced as the game's main scene —
/// it exists purely for interactive testing (see devtools/README.md).
/// </summary>
public partial class DevHarnessRoot : Node3D
{
    private HarnessSimHost _host = null!;
    private CameraOrbit _camera = null!;
    private IHarnessPanel[] _panels = System.Array.Empty<IHarnessPanel>();
    private int _active;

    public override void _Ready()
    {
        BuildStage(out _camera);

        _host = new HarnessSimHost { Name = "SimHost", Camera = _camera };
        AddChild(_host);

        BuildUi();
    }

    public override void _Process(double delta)
    {
        // Camera eases to follow the selected creature; otherwise frames world center. User keeps
        // full yaw/pitch/zoom control (Q/E, middle-drag, wheel) on top via CameraOrbit.
        SNVector3? sel = _host.Selected?.Position;
        _camera.Target = sel is { } p ? new Vector3(p.X, p.Y, p.Z) : Vector3.Zero;

        if (_panels.Length > 0)
            _panels[_active].Refresh(delta);
    }

    // -------------------------------------------------
    // 3D stage (minimal copy of VivariumMain._Ready)
    // -------------------------------------------------

    private void BuildStage(out CameraOrbit camera)
    {
        var light = new DirectionalLight3D
        {
            Rotation = new Vector3(-Mathf.Pi / 3f, Mathf.Pi / 5f, 0f),
            Position = new Vector3(0, 30, 0),
            LightColor = new Color(1, 1, 0.98f),
            LightEnergy = 1.0f,
            ShadowEnabled = true,
        };
        AddChild(light);

        var env = new Godot.Environment
        {
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.4f, 0.45f, 0.55f),
            AmbientLightEnergy = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        // Orbit camera (Q/E rotate, middle-drag orbit, wheel zoom) framed to the ~96-unit map.
        // Distance is set before AddChild so CameraOrbit._Ready seeds from it.
        camera = new CameraOrbit { Name = "HarnessCamera", Distance = 70f, Current = true };
        AddChild(camera);
    }

    // -------------------------------------------------
    // UI
    // -------------------------------------------------

    private void BuildUi()
    {
        // Panels first, so the control strip can name its mode dropdown from them.
        _panels = new IHarnessPanel[]
        {
            new AiModePanel(),
            new PlayerModePanel(),
            new MapModePanel(),
            new GeneticsModePanel(),
        };

        var layer = new CanvasLayer { Name = "HarnessUi" };
        AddChild(layer);

        // Left column: control strip on top, active panel below, in a backing panel.
        var margin = new MarginContainer { CustomMinimumSize = new Vector2(300, 0) };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide);
        layer.AddChild(margin);

        var backing = new PanelContainer();
        margin.AddChild(backing);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        backing.AddChild(column);

        column.AddChild(BuildControlStrip());
        column.AddChild(new HSeparator());

        var (scroll, body) = HarnessUi.ScrollBody();
        column.AddChild(scroll);
        foreach (var panel in _panels)
        {
            var control = (Control)panel;
            panel.Build(_host);
            control.Visible = false;
            body.AddChild(control);
        }
        _panels[0].OnEnter();
    }

    private Control BuildControlStrip()
    {
        var box = new VBoxContainer();

        // Mode dropdown, named from the panels.
        var mode = new OptionButton();
        foreach (var panel in _panels)
            mode.AddItem(panel.ModeName);
        mode.Selected = 0;
        mode.ItemSelected += idx => SwitchMode((int)idx);
        box.AddChild(mode);

        // Time control row.
        var row = new HBoxContainer();
        var pause = new CheckButton { Text = "Pause" };
        pause.Toggled += on => _host.Paused = on;
        row.AddChild(pause);
        row.AddChild(HarnessUi.Button("Step", () => _host.StepOnce()));
        box.AddChild(row);

        box.AddChild(HarnessUi.Slider("TimeScale", 0.1f, 4f, 0.1f, 1f, v => _host.TimeScale = v));

        // World reset.
        var resetRow = new HBoxContainer();
        var seed = new SpinBox { MinValue = 0, MaxValue = 99999, Value = 1, Step = 1 };
        resetRow.AddChild(new Label { Text = "Seed:" });
        resetRow.AddChild(seed);
        resetRow.AddChild(HarnessUi.Button("Reset world", () => _host.ResetWorld((int)seed.Value)));
        box.AddChild(resetRow);

        return box;
    }

    private void SwitchMode(int idx)
    {
        if (idx < 0 || idx >= _panels.Length || idx == _active) return;
        _panels[_active].OnExit();
        _active = idx;
        _panels[_active].OnEnter();
    }
}
