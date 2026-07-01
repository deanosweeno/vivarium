using System;
using Vivarium.Core;

namespace Vivarium.DevTools;

/// <summary>
/// Read-only formatting of core objects into inspector text for the harness overlays. Mirrors
/// <see cref="Vivarium.Scripts.VivariumMain"/>'s DebugLabelText, but returns a string for a
/// RichTextLabel instead of drawing a 3D label. No writes into core — inspection only.
/// </summary>
public static class Inspect
{
    /// <summary>Action + drive-need bars + broadcast/reaction tell for one creature.</summary>
    public static string CreatureReadout(Creature c)
    {
        string? action = c.Brain?.CurrentName;
        if (string.IsNullOrEmpty(action)) action = "—";

        var n = c.Needs;
        return $"action : {action}\n"
             + Bar("hunger ", n.Hunger)
             + Bar("fatigue", n.Fatigue)
             + Bar("boredom", n.Boredom)
             + Bar("bond   ", n.Affection)
             + $"flock  : {(c.Flock is null ? "—" : "yes")}\n"
             + $"bubble : {c.Broadcast}\n"
             + $"tell   : {c.LastReaction.Kind} ({c.LastReaction.Strength:0.00})";
    }

    private static string Bar(string label, float v)
    {
        int filled = Math.Clamp((int)MathF.Round(v * 10f), 0, 10);
        string bar = new string('#', filled) + new string('.', 10 - filled);
        return $"{label}: {bar} {(int)MathF.Round(v * 100f),3}%\n";
    }
}
