using System.Collections.Generic;

namespace Vivarium.Core;

/// <summary>
/// The registered player interaction verbs — mirrors <c>BehaviorConfig.DefaultActions()</c> as a
/// single place new capabilities are appended. Future verbs (pick-up/carry, place-food) slot in here;
/// the dispatcher routes by <see cref="IPlayerInteraction.Id"/> with no other code change.
/// </summary>
public static class PlayerInteractions
{
    public static IReadOnlyList<IPlayerInteraction> Default() => new IPlayerInteraction[]
    {
        new PickUpInteraction(),
        new PlaceFoodInteraction(),
        new FeedInteraction(),
        new SootheInteraction(),
        new PlayInteraction(),
        new HarvestInteraction(),
        // TODO: migrate magnitudes to assets/interactions.json.
    };
}
