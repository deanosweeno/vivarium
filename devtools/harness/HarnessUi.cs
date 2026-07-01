using System;
using Godot;

namespace Vivarium.DevTools;

/// <summary>
/// Small factory helpers for the code-built harness UI. The dev harness constructs its whole
/// control tree in C# (no .tscn wiring) so panels stay self-contained; these keep the callers
/// terse. Presentation-only — no simulation logic lives here.
/// </summary>
public static class HarnessUi
{
    /// <summary>A plain label.</summary>
    public static Label Label(string text) => new() { Text = text };

    /// <summary>A section heading (bold-ish via a leading marker + larger font).</summary>
    public static Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 16);
        return l;
    }

    /// <summary>A push button wired to <paramref name="onPressed"/>.</summary>
    public static Button Button(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }

    /// <summary>
    /// A labeled horizontal slider: "Name: value" label above an <see cref="HSlider"/>. The label
    /// live-updates and <paramref name="onChanged"/> fires with the new value on every drag.
    /// Returns the wrapping VBox so the caller just adds it to a container.
    /// </summary>
    public static VBoxContainer Slider(
        string name, float min, float max, float step, float value, Action<float> onChanged)
        => LiveSlider(name, min, max, step, value, onChanged).Box;

    /// <summary>
    /// Like <see cref="Slider"/> but also hands back the <see cref="HSlider"/> and its
    /// <see cref="Label"/>, so a caller can later re-seed the value with
    /// <c>slider.SetValueNoSignal(v)</c> (no callback re-fire) and relabel it. Used by panels that
    /// re-point a slider at a changing target — e.g. the per-selected-creature editors.
    /// </summary>
    public static (VBoxContainer Box, HSlider Slider, Label Label) LiveSlider(
        string name, float min, float max, float step, float value, Action<float> onChanged)
    {
        var box = new VBoxContainer();
        var label = new Label { Text = $"{name}: {value:0.###}" };
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(220, 0),
        };
        slider.ValueChanged += v =>
        {
            label.Text = $"{name}: {v:0.###}";
            onChanged((float)v);
        };
        box.AddChild(label);
        box.AddChild(slider);
        return (box, slider, label);
    }

    /// <summary>A labeled on/off <see cref="CheckButton"/> wired to <paramref name="onChanged"/>.</summary>
    public static CheckButton Toggle(string name, bool value, Action<bool> onChanged)
    {
        var t = new CheckButton { Text = name, ButtonPressed = value };
        t.Toggled += on => onChanged(on);
        return t;
    }

    /// <summary>A scrollable vertical container — the common panel body wrapper.</summary>
    public static (ScrollContainer Scroll, VBoxContainer Body) ScrollBody()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);
        return (scroll, body);
    }
}
