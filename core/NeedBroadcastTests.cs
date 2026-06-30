using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Need-broadcast resolution: which player-lane need a creature surfaces as a thought-bubble.
/// Pure function — no sim, no RNG. Verifies the satisfier tagging (Affection is player-only and
/// always asks; Hunger/Boredom are suppressed while the creature self-satisfies) and urgency order.
/// </summary>
public class NeedBroadcastTests
{
    private static readonly BehaviorConfig Cfg = new();

    [Fact]
    public void LowBond_BroadcastsAffection()
    {
        var n = new CreatureNeeds { Affection = 0.2f, Hunger = 0f, Boredom = 0f };
        Assert.Equal(BroadcastNeed.Affection, NeedBroadcast.Resolve(n, isForaging: false, isFrolicking: false, Cfg));
    }

    [Fact]
    public void HighHunger_OutranksMildAffectionWant()
    {
        var n = new CreatureNeeds { Affection = 0.8f, Hunger = 0.9f, Boredom = 0f };
        Assert.Equal(BroadcastNeed.Hunger, NeedBroadcast.Resolve(n, isForaging: false, isFrolicking: false, Cfg));
    }

    [Fact]
    public void Hunger_SuppressedWhileForaging()
    {
        var n = new CreatureNeeds { Affection = 0.8f, Hunger = 0.9f, Boredom = 0f };
        // Self-satisfying hunger → falls back to the standing affection want.
        Assert.Equal(BroadcastNeed.Affection, NeedBroadcast.Resolve(n, isForaging: true, isFrolicking: false, Cfg));
    }

    [Fact]
    public void Boredom_SuppressedWhileFrolicking()
    {
        var n = new CreatureNeeds { Affection = 0.84f, Hunger = 0f, Boredom = 0.9f };
        Assert.Equal(BroadcastNeed.Affection, NeedBroadcast.Resolve(n, isForaging: false, isFrolicking: true, Cfg));
    }

    [Fact]
    public void Boredom_BroadcastsWhenIdleAndHigh()
    {
        var n = new CreatureNeeds { Affection = 0.84f, Hunger = 0f, Boredom = 0.9f };
        Assert.Equal(BroadcastNeed.Boredom, NeedBroadcast.Resolve(n, isForaging: false, isFrolicking: false, Cfg));
    }

    [Fact]
    public void FullyContent_BroadcastsNone()
    {
        var n = new CreatureNeeds { Affection = 0.9f, Hunger = 0.1f, Boredom = 0.1f };
        Assert.Equal(BroadcastNeed.None, NeedBroadcast.Resolve(n, isForaging: false, isFrolicking: false, Cfg));
    }
}
