using Godot;

namespace Vivarium.DevTools;

/// <summary>
/// A single mode panel in the dev harness. Each panel is a <see cref="Control"/> that builds its
/// own UI and reads/pokes the shared <see cref="HarnessSimHost"/>. The root only ever toggles
/// panel visibility and forwards a per-frame <see cref="Refresh"/> to the active panel — it never
/// branches on mode beyond that, so adding a mode is adding one panel class.
/// </summary>
public interface IHarnessPanel
{
    /// <summary>Human-readable name shown in the mode dropdown.</summary>
    string ModeName { get; }

    /// <summary>Build the panel's controls. Called once, before first show.</summary>
    void Build(HarnessSimHost host);

    /// <summary>Called when this panel becomes the active mode (make visible, resume refresh).</summary>
    void OnEnter();

    /// <summary>Called when another mode takes over (hide, pause refresh).</summary>
    void OnExit();

    /// <summary>Per-frame overlay/inspector refresh while this panel is active.</summary>
    void Refresh(double delta);
}
