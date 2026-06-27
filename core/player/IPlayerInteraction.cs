namespace Vivarium.Core;

/// <summary>
/// A single player interaction verb (feed, soothe, play, and — later — pick-up, place-food…), as a
/// self-contained object. Replaces the hardcoded switch in the Simulator: verbs are registered in a
/// list (<see cref="PlayerInteractions.Default"/>) and dispatched by <see cref="PlayerController"/>,
/// so adding a capability means adding a class, not editing dispatch.
///
/// <para><see cref="Id"/> is the routing key the input layer queues (see
/// <see cref="PlayerInputMode.QueueIntent"/>). <see cref="CanApply"/> is the pure gate (food in hand,
/// bond threshold, …); <see cref="Apply"/> mutates need state. Tuning lives in
/// <see cref="BehaviorConfig"/>, never in the verb.</para>
/// </summary>
public interface IPlayerInteraction
{
    /// <summary>Stable routing id matched against queued player intents (e.g. "feed").</summary>
    string Id { get; }

    /// <summary>True if this verb may land given the current context (target present, gates met).</summary>
    bool CanApply(in InteractionContext ctx);

    /// <summary>Apply the verb's effect. Only called when <see cref="CanApply"/> returned true.</summary>
    void Apply(in InteractionContext ctx);
}
