namespace Vivarium.Core;

/// <summary>
/// Maps a <see cref="CreatureDef.ActionSet"/> name (from <c>creatures.json</c>) to a candidate
/// action table, so a creature type's behavior repertoire is a data string instead of every
/// brain sharing one global <see cref="BrainConfig.Actions"/> list. Unknown/absent names resolve
/// to null — the caller falls back to the brain config's default table.
/// </summary>
public static class ActionSetCatalog
{
    /// <summary>The v1 "full five" (plus flock/player lanes) action table — every herbivore's
    /// full repertoire. Currently the only entry; a "predator" set can be added here later
    /// without touching brain or config code.</summary>
    public static IReadOnlyList<BehaviorAction> Herbivore { get; } = BehaviorConfig.DefaultActions();

    /// <summary>Resolve an action-set name to its table, or null when unknown/absent.</summary>
    public static IReadOnlyList<BehaviorAction>? Resolve(string? name) => name?.ToLowerInvariant() switch
    {
        "herbivore" => Herbivore,
        _ => null,
    };
}
