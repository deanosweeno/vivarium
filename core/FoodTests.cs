using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the Food &amp; Foraging system: food-type catalog, the grow/graze/respawn
/// <see cref="FoodItem"/> mechanics, deterministic seeding, and the closed Hunger loop
/// (a foraging creature grazes food and its Hunger drops). All headless — no Godot.
/// </summary>
public class FoodTests
{
    private static readonly FoodCatalog Catalog = FoodCatalog.Parse("""
        [
          { "Id": "berries", "Nutrition": 0.5, "GrazeRate": 0.5, "RespawnSeconds": 4.0, "ColorHex": "#C0344B" }
        ]
        """);

    // A map whose every cell is the given biome (mirrors SimulatorTests.UniformBiomeMap).
    private static MapData UniformBiomeMap(Biome biome, int size = 16)
    {
        var map = new MapData(size, size, 1f);
        for (int cz = 0; cz < size; cz++)
        for (int cx = 0; cx < size; cx++)
            map.SetCell(cx, cz, new Cell { Terrain = Terrain.Grass, Biome = biome });
        return map;
    }

    // ---------- catalog ----------

    [Fact]
    public void Catalog_ReadsFields()
    {
        var def = Catalog.Get("berries");
        Assert.Equal("berries", def.Id);
        Assert.Equal(0.5f, def.Nutrition, 5);
        Assert.Equal(4.0f, def.RespawnSeconds, 5);
    }

    [Fact]
    public void Catalog_UnknownId_ReturnsNeutralFallback()
    {
        var def = Catalog.Get("does-not-exist");
        Assert.Equal("does-not-exist", def.Id);   // never throws
        Assert.Equal(FoodDef.Neutral("x").Nutrition, def.Nutrition, 5);
    }

    // ---------- FoodItem mechanics ----------

    [Fact]
    public void Bite_DrainsAmount_AndReturnsNutritionEaten()
    {
        var item = new FoodItem { Def = Catalog.Get("berries") }; // Amount 1
        float hungerRemoved = item.Bite(1.0f);                    // graze 1s at rate 0.5

        Assert.Equal(0.5f, item.Amount, 5);                       // drained by GrazeRate·dt
        Assert.Equal(0.25f, hungerRemoved, 5);                    // 0.5 eaten × 0.5 nutrition
    }

    [Fact]
    public void Bite_ToEmpty_GoesUnavailable_AndArmsRespawn()
    {
        var item = new FoodItem { Def = Catalog.Get("berries") };
        item.Bite(10f);   // way more than enough to empty it

        Assert.Equal(0f, item.Amount, 5);
        Assert.False(item.Available);
        Assert.Equal(4.0f, item.RespawnTimer, 5);   // RespawnSeconds
    }

    [Fact]
    public void Regrow_RefillsAfterRespawnSeconds()
    {
        var item = new FoodItem { Def = Catalog.Get("berries") };
        item.Bite(10f);                       // empty → RespawnTimer = 4s
        Assert.False(item.Available);

        for (int i = 0; i < 39; i++) item.Regrow(0.1f);   // 3.9s — not yet
        Assert.False(item.Available);

        item.Regrow(0.2f);                    // crosses 4s
        Assert.True(item.Available);
        Assert.Equal(1f, item.Amount, 5);
    }

    // ---------- seeding ----------

    [Fact]
    public void SeedFood_NoCatalog_PlacesNothing()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1);
        sim.SeedFood();
        Assert.Empty(sim.Food);
    }

    [Fact]
    public void SeedFood_IsDeterministicForASeed()
    {
        var a = MakeSeededSim(seed: 7);
        var b = MakeSeededSim(seed: 7);

        Assert.Equal(a.Food.Count, b.Food.Count);
        Assert.NotEmpty(a.Food);
        for (int i = 0; i < a.Food.Count; i++)
            Assert.Equal(a.Food[i].Position, b.Food[i].Position);
    }

    [Fact]
    public void SeedFood_FoodChanceZero_PlacesNothing()
    {
        var biomes = BiomeCatalog.Parse("""
            [ { "Biome": "Desert", "FoodChance": 0.0, "FoodType": "berries" } ]
            """);
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 3)
        {
            Map = UniformBiomeMap(Biome.Desert),
            Biomes = biomes,
            Foods = Catalog,
        };
        sim.SeedFood();
        Assert.Empty(sim.Food);
    }

    // ---------- closed Hunger loop via Simulator ----------

    [Fact]
    public void HungryForager_GrazesNearbyFood_LoweringHungerAndAmount()
    {
        var sim = new Simulator(Arena.GroundArena(10, 10), seed: 5);
        var forager = AddForager(sim, Vector3.Zero, hunger: 1f);
        var item = new FoodItem { Def = Catalog.Get("berries"), Position = new Vector3(0.2f, 0f, 0f) };
        sim.Food.Add(item);

        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        Assert.Equal("Forage", forager.Brain!.CurrentName);
        Assert.True(forager.Needs.Hunger < 1f, $"hunger should drop while grazing, was {forager.Needs.Hunger}");
        Assert.True(item.Amount < 1f, $"food should be depleted by grazing, was {item.Amount}");
    }

    [Fact]
    public void DepletedFood_RegrowsAfterRespawn_InSimulator()
    {
        var sim = new Simulator(Arena.GroundArena(10, 10), seed: 5);
        var item = new FoodItem { Def = Catalog.Get("berries"), Position = new Vector3(3f, 0f, 3f) };
        item.Bite(10f);                       // start depleted (RespawnSeconds 4)
        sim.Food.Add(item);

        for (int i = 0; i < 39; i++) sim.Tick(0.1);
        Assert.False(item.Available);
        for (int i = 0; i < 3; i++) sim.Tick(0.1);
        Assert.True(item.Available);
    }

    // ---------- drive influence ----------

    [Fact]
    public void HungryAppetitiveCreature_ChoosesForage_OverWander()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Appetite = 1f, Curiosity = 0.2f });
        brain.Tick(0.1, self, new SenseContext { Hunger = 1f }, new Random(1));
        Assert.Equal("Forage", brain.CurrentName);
    }

    [Fact]
    public void SatedCreature_DoesNotForage()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Appetite = 1f, Curiosity = 1f });
        brain.Tick(0.1, self, new SenseContext { Hunger = 0f }, new Random(1));
        Assert.NotEqual("Forage", brain.CurrentName);
    }

    // ---------- helpers ----------

    private static Simulator MakeSeededSim(int seed)
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed)
        {
            Map = UniformBiomeMap(Biome.Plains),
            Biomes = BiomeCatalog.Parse("""
                [ { "Biome": "Plains", "FoodChance": 1.0, "FoodType": "berries" } ]
                """),
            Foods = Catalog,
        };
        sim.SeedFood();
        return sim;
    }

    private static Creature MakeCreature(Drives drives)
        => new(Vector3.Zero, new CreatureTraits { MaxSpeed = 1f }, new SteeringLocomotion(), drives);

    // Build a brained, ground-bound, high-appetite creature and register it with the sim.
    private static Creature AddForager(Simulator sim, Vector3 pos, float hunger)
    {
        var traits = new CreatureTraits { MaxSpeed = 1f, Radius = 0.5f, GravityScale = 0f };
        var creature = new Creature(pos, traits, new SteeringLocomotion(), new Drives { Appetite = 1f })
        {
            Brain = new UtilityBrain(sim.Behavior),
        };
        creature.Needs.Hunger = hunger;
        sim.Entities.Add(creature);
        return creature;
    }
}
