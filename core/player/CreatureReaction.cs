namespace Vivarium.Core;

/// <summary>
/// How a creature reacted to the most recent player interaction, in animation terms.
/// Mirrors <see cref="PlayerState"/>: the core never touches a clip — it only describes the
/// reaction, and the Godot visual layer (CreatureVisual) reads it to play a tell (a happy
/// bounce, a small nod). This is the feedback channel that teaches the player a creature's
/// temperament — a well-matched interaction reads bigger than a mismatched one.
/// </summary>
public enum ReactionKind
{
    /// <summary>No reaction pending — the resting state.</summary>
    None,

    /// <summary>The interaction landed well — play a positive tell scaled by Strength.</summary>
    Happy,

    /// <summary>The player just became a threat (panic onset) — a startled flinch/recoil tell.</summary>
    Startled,

    /// <summary>The brain just committed to Approach/SeekFlock out of curiosity — a small
    /// head-tilt/look tell.</summary>
    Curious,

    /// <summary>A held need (Rest/Forage) was just satisfied — a settled, relaxed tell.</summary>
    Content,

    /// <summary>A flavor-mismatched interaction landed (still helped — see
    /// <see cref="FlavorMatch"/>'s bonus-never-punishment floor) — a small, muted tell.</summary>
    Dislike,
}

/// <summary>
/// Immutable snapshot of a creature's last interaction reaction. <see cref="Strength"/> (0..1)
/// is the flavor-match: 1 = the interaction perfectly fit this creature's temperament (a lively
/// creature played with, a cuddly one soothed); lower = an off-flavor pick that still helped but
/// reads smaller. The visual decays the tell on its own once played.
/// </summary>
public readonly struct CreatureReaction
{
    public ReactionKind Kind { get; init; }

    /// <summary>Flavor-match strength in [0,1]; scales the size of the visual tell.</summary>
    public float Strength { get; init; }

    /// <summary>Monotonic stamp bumped each time a reaction is set, so the visual can detect a
    /// fresh reaction (and replay the tell) even when two reactions share Kind+Strength.</summary>
    public int Stamp { get; init; }

    public static readonly CreatureReaction None = new() { Kind = ReactionKind.None };

    /// <summary>A fresh Happy reaction of the given match strength, bumping the stamp off the
    /// creature's previous reaction so the visual detects it as new.</summary>
    public static CreatureReaction Happy(float strength, CreatureReaction previous) => new()
    {
        Kind = ReactionKind.Happy,
        Strength = Math.Clamp(strength, 0f, 1f),
        Stamp = previous.Stamp + 1,
    };

    /// <summary>A fresh Dislike reaction of the given (muted) strength — a mismatched interaction
    /// that still helped, per <see cref="FlavorMatch"/>'s floor.</summary>
    public static CreatureReaction Dislike(float strength, CreatureReaction previous) => new()
    {
        Kind = ReactionKind.Dislike,
        Strength = Math.Clamp(strength, 0f, 1f),
        Stamp = previous.Stamp + 1,
    };

    /// <summary>A fresh Startled reaction — player-threat onset.</summary>
    public static CreatureReaction Startled(CreatureReaction previous) => new()
    {
        Kind = ReactionKind.Startled,
        Strength = 1f,
        Stamp = previous.Stamp + 1,
    };

    /// <summary>A fresh Curious reaction — the brain just committed to Approach/SeekFlock.</summary>
    public static CreatureReaction Curious(CreatureReaction previous) => new()
    {
        Kind = ReactionKind.Curious,
        Strength = 0.6f,
        Stamp = previous.Stamp + 1,
    };

    /// <summary>A fresh Content reaction — a held need (Rest/Forage) was just satisfied.</summary>
    public static CreatureReaction Content(CreatureReaction previous) => new()
    {
        Kind = ReactionKind.Content,
        Strength = 0.7f,
        Stamp = previous.Stamp + 1,
    };
}
