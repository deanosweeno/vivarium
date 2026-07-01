using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Phase C: liveliness reaction transitions (Startled/Curious/Content). Pure function — no sim,
/// no RNG. Happy/Dislike (interaction-triggered) are covered by the player-interaction tests.
/// </summary>
public class ReactionSystemTests
{
    private static readonly BehaviorAction ForageAction = new()
    {
        Name = "Forage",
        Steering = SteeringKind.Forage,
        HoldWhile = new HoldWhile(InputKind.Hunger, Threshold: 0.15f),
    };

    private static readonly BehaviorAction RestAction = new()
    {
        Name = "Rest",
        Steering = SteeringKind.Rest,
        HoldWhile = new HoldWhile(InputKind.Fatigue),
    };

    private static readonly BehaviorAction ApproachAction = new() { Name = "Approach", Steering = SteeringKind.Approach };
    private static readonly BehaviorAction SeekFlockAction = new() { Name = "SeekFlock", Steering = SteeringKind.SeekFlock };
    private static readonly BehaviorAction WanderAction = new() { Name = "Wander", Steering = SteeringKind.Wander };

    [Fact]
    public void PlayerPanicOnset_ProducesStartled()
    {
        var previous = new SenseContext { IsPlayerThreat = false, HasPlayer = false, HasFlock = false };
        var current = new SenseContext { IsPlayerThreat = true, HasPlayer = true, HasFlock = false };

        var result = ReactionSystem.ResolveTransition(WanderAction, WanderAction, previous, current);

        Assert.Equal(ReactionKind.Startled, result);
    }

    [Fact]
    public void PlayerPanicSustained_DoesNotRepeatStartled()
    {
        var previous = new SenseContext { IsPlayerThreat = true, HasPlayer = true, HasFlock = false };
        var current = new SenseContext { IsPlayerThreat = true, HasPlayer = true, HasFlock = false };

        var result = ReactionSystem.ResolveTransition(WanderAction, WanderAction, previous, current);

        Assert.Null(result);
    }

    [Fact]
    public void ForageLatchRelease_ProducesContent()
    {
        var previous = new SenseContext { Hunger = 0.5f };
        var current = new SenseContext { Hunger = 0.1f }; // dropped below the 0.15 satiation floor

        var result = ReactionSystem.ResolveTransition(ForageAction, WanderAction, previous, current);

        Assert.Equal(ReactionKind.Content, result);
    }

    [Fact]
    public void RestLatchRelease_ProducesContent()
    {
        var previous = new SenseContext { Fatigue = 0.3f };
        var current = new SenseContext { Fatigue = 0f };

        var result = ReactionSystem.ResolveTransition(RestAction, WanderAction, previous, current);

        Assert.Equal(ReactionKind.Content, result);
    }

    [Fact]
    public void ForageStillHungry_NoReaction_EvenIfActionChanges()
    {
        // Latch still active (Hunger above threshold) on both sides → no "satisfied" transition,
        // even though something else (an emergency interrupt) forced an action change.
        var previous = new SenseContext { Hunger = 0.5f };
        var current = new SenseContext { Hunger = 0.4f };

        var result = ReactionSystem.ResolveTransition(ForageAction, WanderAction, previous, current);

        Assert.Null(result);
    }

    [Fact]
    public void TransitionIntoApproach_ProducesCurious()
    {
        var senses = default(SenseContext);
        var result = ReactionSystem.ResolveTransition(WanderAction, ApproachAction, senses, senses);
        Assert.Equal(ReactionKind.Curious, result);
    }

    [Fact]
    public void TransitionIntoSeekFlock_ProducesCurious()
    {
        var senses = default(SenseContext);
        var result = ReactionSystem.ResolveTransition(WanderAction, SeekFlockAction, senses, senses);
        Assert.Equal(ReactionKind.Curious, result);
    }

    [Fact]
    public void NoActionChange_ProducesNoReaction()
    {
        var senses = default(SenseContext);
        var result = ReactionSystem.ResolveTransition(WanderAction, WanderAction, senses, senses);
        Assert.Null(result);
    }

    [Fact]
    public void FirstDecision_NullPreviousAction_HandledSafely()
    {
        var senses = default(SenseContext);
        var result = ReactionSystem.ResolveTransition(null, WanderAction, senses, senses);
        Assert.Null(result);
    }

    // ---------- CreatureReaction factories ----------

    [Fact]
    public void Startled_BumpsStampAndSetsFullStrength()
    {
        var previous = CreatureReaction.Happy(0.5f, CreatureReaction.None);
        var startled = CreatureReaction.Startled(previous);

        Assert.Equal(ReactionKind.Startled, startled.Kind);
        Assert.Equal(1f, startled.Strength);
        Assert.Equal(previous.Stamp + 1, startled.Stamp);
    }

    [Fact]
    public void Dislike_ClampsStrengthToUnitRange()
    {
        var reaction = CreatureReaction.Dislike(1.5f, CreatureReaction.None);
        Assert.Equal(1f, reaction.Strength);
        Assert.Equal(ReactionKind.Dislike, reaction.Kind);
    }
}
