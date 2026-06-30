using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Personality flavor-match on the pet verbs: a creature's <see cref="Drives.PlayCuddle"/> temperament
/// scales bond gain (Play favours energetic, Soothe favours calm). Cozy invariant: a mismatched verb
/// still gains bond — never zero, never negative. Feed is flavor-agnostic.
/// </summary>
public class FlavorMatchTests
{
    private static Simulator NewSim() => new(Arena.GroundArena(32, 32), seed: 1);
    private static FoodDef Snack => new() { Id = "snack", Name = "Snack" };

    // Above PartialBondThreshold so Play/Soothe are enabled.
    private static Drives Lively => new() { PlayCuddle = 0.9f, Fear = 0.2f };
    private static Drives Cuddly => new() { PlayCuddle = 0.1f, Fear = 0.2f };

    private static float PlayDelta(Drives drives)
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(Vector3.Zero);
        var c = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: drives);
        c.Needs.Affection = 0.5f;
        c.Needs.Boredom = 0.8f;
        input.PlayPressed = true;
        sim.Tick(0.1);
        return c.Needs.Affection - 0.5f;
    }

    private static float SootheDelta(Drives drives)
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(Vector3.Zero);
        var c = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: drives);
        c.Needs.Affection = 0.5f;
        c.Needs.Fatigue = 0.8f;
        input.SoothePressed = true;
        sim.Tick(0.1);
        return c.Needs.Affection - 0.5f;
    }

    [Fact]
    public void LivelyCreature_BondsMoreFromPlayThanSoothe()
    {
        Assert.True(PlayDelta(Lively) > SootheDelta(Lively));
    }

    [Fact]
    public void CuddlyCreature_BondsMoreFromSootheThanPlay()
    {
        Assert.True(SootheDelta(Cuddly) > PlayDelta(Cuddly));
    }

    [Fact]
    public void MismatchedVerb_StillGainsBond_NoPunishment()
    {
        // Cuddly creature played with: the worst flavor match — must still be a positive gain.
        Assert.True(PlayDelta(Cuddly) > 0f);
        Assert.True(SootheDelta(Lively) > 0f);
    }

    [Fact]
    public void Feed_IsFlavorAgnostic()
    {
        float Feed(Drives drives)
        {
            var sim = NewSim();
            var (_, input) = sim.SpawnPlayer(Vector3.Zero);
            var c = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: drives);
            c.Needs.Hunger = 0.8f;
            input.CarriedFood = Snack;
            input.FeedPressed = true;
            sim.Tick(0.1);
            return c.Needs.Affection;
        }

        Assert.Equal(Feed(Lively), Feed(Cuddly), 4);
    }

    [Fact]
    public void Interaction_SetsHappyReaction()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(Vector3.Zero);
        var c = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Lively);
        c.Needs.Affection = 0.5f;
        input.PlayPressed = true;
        sim.Tick(0.1);

        Assert.Equal(ReactionKind.Happy, c.LastReaction.Kind);
        Assert.True(c.LastReaction.Strength > 0.5f, "lively+play should read as a strong match");
    }
}
