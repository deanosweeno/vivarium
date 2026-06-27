using System.Collections.Generic;
using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Configuration for <see cref="HerdSpawner"/>. Tune counts, spacing, biome, and
/// whether to jitter each creature's initial needs so they're out of phase.
/// </summary>
public sealed record HerdSpawnConfig
{
    /// <summary>Number of separated herd centers to pick within the biome.</summary>
    public int HerdCount { get; init; } = 3;

    /// <summary>Minimum horizontal distance between herd centers.</summary>
    public float MinHerdSeparation { get; init; } = 18f;

    /// <summary>Spawn spread around each herd center (uniform radius).</summary>
    public float HerdJitter { get; init; } = 2f;

    /// <summary>Minimum creatures per herd (inclusive).</summary>
    public int HerdSizeMin { get; init; } = 4;

    /// <summary>Maximum creatures per herd (exclusive).</summary>
    public int HerdSizeMax { get; init; } = 6;

    /// <summary>Biome the herds must stay within.</summary>
    public Biome Biome { get; init; } = Biome.Plains;

    /// <summary>Randomize each creature's Hunger/Fatigue/Boredom after spawn.</summary>
    public bool JitterNeeds { get; init; } = true;
}

/// <summary>
/// Spawns creature herds at separated biome centers. Pure algorithm — no Godot
/// dependency. Reusable for any creature type that spawns in groups within a biome.
/// </summary>
public static class HerdSpawner
{
    /// <summary>
    /// Spawn herds from a data-driven <see cref="CreatureDef"/> (its traits, drives, herd config,
    /// and body plan). Throws if the def has no <see cref="CreatureDef.Herd"/> — a non-herding type.
    /// </summary>
    public static List<Creature> SpawnHerds(
        Simulator sim, ICreatureFactory factory, CreatureDef def, MapData map, Random rng)
    {
        if (def.Herd is null)
            throw new InvalidOperationException($"Creature '{def.Id}' has no Herd config to spawn from.");
        return SpawnHerds(sim, factory,
            def.Traits ?? CreatureTraits.Default,
            def.Drives ?? Drives.Default,
            map, def.Herd, rng, def.Body);
    }

    /// <summary>
    /// Spawn herds according to <paramref name="config"/> using the given factory,
    /// traits, and drives. Returns all spawned creatures.
    /// </summary>
    public static List<Creature> SpawnHerds(
        Simulator sim,
        ICreatureFactory factory,
        CreatureTraits traits,
        Drives drives,
        MapData map,
        HerdSpawnConfig config,
        Random rng,
        BodyPlan? bodyPlan = null)
    {
        var placement = new BiomeFilteredPlacement(
            new OverlapAvoidingPlacement(ArenaClampPlacement.Instance),
            map, config.Biome);

        var herdCenters = PickSeparatedBiomeCenters(
            map, config.Biome, config.HerdCount, config.MinHerdSeparation, rng);

        var spawned = new List<Creature>();
        foreach (var herdPos in herdCenters)
        {
            int herdSize = config.HerdSizeMin + rng.Next(0, config.HerdSizeMax - config.HerdSizeMin);
            for (int i = 0; i < herdSize; i++)
            {
                float ox = (float)(rng.NextDouble() * 2 - 1) * config.HerdJitter;
                float oz = (float)(rng.NextDouble() * 2 - 1) * config.HerdJitter;
                var creature = (Blob)sim.Spawn(
                    new Vector3(herdPos.X + ox, herdPos.Y, herdPos.Z + oz),
                    factory,
                    placement,
                    traits,
                    movement: null,
                    drives);
                if (bodyPlan != null)
                    creature.Body = bodyPlan;
                if (config.JitterNeeds)
                    creature.Needs.Randomize(rng);
                spawned.Add(creature);
            }
        }

        return spawned;
    }

    /// <summary>
    /// Pick one random cell of the given <paramref name="biome"/> and return its world
    /// center (with terrain height). Returns null if no cell of that biome exists.
    /// </summary>
    public static Vector3? PickBiomeCenter(MapData map, Biome biome, Random rng)
    {
        var cells = new List<(int cx, int cz)>();
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (map.GetBiome(cx, cz) == biome)
                cells.Add((cx, cz));
        }

        if (cells.Count == 0) return null;

        var (pickCx, pickCz) = cells[rng.Next(cells.Count)];
        var center = map.CellToWorldCenter(pickCx, pickCz);
        float py = map.HeightAt(center);
        return new Vector3(center.X, py, center.Z);
    }

    /// <summary>
    /// Pick up to <paramref name="count"/> biome-cell centers that are each at least
    /// <paramref name="minSep"/> apart (horizontal), so herds spawn as distinct, separated
    /// groups rather than one clump. Returns fewer if the map can't satisfy the spacing
    /// — graceful degradation. Deterministic for a seeded RNG.
    /// </summary>
    public static List<Vector3> PickSeparatedBiomeCenters(
        MapData map, Biome biome, int count, float minSep, Random rng)
    {
        var picks = new List<Vector3>();
        float minSepSq = minSep * minSep;
        int attempts = count * 20;
        while (picks.Count < count && attempts-- > 0)
        {
            var candidate = PickBiomeCenter(map, biome, rng);
            if (candidate is not Vector3 c) break;
            bool farEnough = true;
            foreach (var p in picks)
            {
                float dx = p.X - c.X, dz = p.Z - c.Z;
                if (dx * dx + dz * dz < minSepSq) { farEnough = false; break; }
            }
            if (farEnough) picks.Add(c);
        }
        return picks;
    }
}
