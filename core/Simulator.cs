using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Headless simulation container. Owns the entity list, arena, and RNG.
/// Call Tick(delta) each frame; read Entities to sync visuals.
///
/// All entities are <see cref="Creature"/> instances (including <see cref="Blob"/>
/// which extends Creature). Physics, gravity, ground clamping, and collision
/// are applied uniformly to all entities through the creature pipeline.
/// </summary>
public sealed class Simulator
{
    /// <summary>
    /// All entities in the simulation. Includes both generic Creatures and
    /// Blob instances (which extend Creature).
    /// </summary>
    public List<Creature> Entities { get; } = new();

    /// <summary>
    /// Global gravity constant applied to all creatures each tick.
    /// In arena units per second². Creatures with GravityScale = 0
    /// (e.g., blobs) are unaffected.
    /// </summary>
    public float Gravity { get; set; } = 9.8f;

    public Arena Arena { get; }
    public Random Rng { get; }

    /// <summary>
    /// Optional terrain map. When set together with <see cref="Biomes"/>, each tick
    /// samples the biome under every creature and applies its effects. Null = no
    /// biome effects (the original headless behavior), so existing callers are unaffected.
    /// </summary>
    public MapData? Map { get; set; }

    /// <summary>
    /// Optional biome rule catalog, paired with <see cref="Map"/>. Supplies the
    /// per-biome effect numbers (happiness rate, speed multiplier, …).
    /// </summary>
    public BiomeCatalog? Biomes { get; set; }

    /// <summary>Total number of entities in the simulation.</summary>
    public int EntityCount => Entities.Count;

    public Simulator(Arena arena, int seed = 0)
    {
        Arena = arena;
        Rng = new Random(seed);
    }

    /// <summary>
    /// Spawn a new blob at the given position with a random pastel color.
    /// The position is clamped to arena bounds (with radius margin) and
    /// retried up to 10 times if it overlaps an existing entity.
    /// </summary>
    public Blob SpawnBlob(Vector3 position)
    {
        float radius = Blob.DefaultBlobTraits.Radius;
        var clamped = Arena.Clamp(position, radius);

        // Avoid overlapping existing entities
        float minDist = radius * 2f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OverlapsAny(clamped, minDist))
                break;

            // Try a random position within the arena
            float rx = (float)(Rng.NextDouble() * (Arena.MaxX - Arena.MinX - radius * 2) + Arena.MinX + radius);
            float rz = (float)(Rng.NextDouble() * (Arena.MaxZ - Arena.MinZ - radius * 2) + Arena.MinZ + radius);
            clamped = new Vector3(rx, 0f, rz);
        }

        var (r, g, b) = Blob.RandomPastelColor(Rng);
        var blob = new Blob(clamped, r, g, b, Rng);
        Entities.Add(blob);
        return blob;
    }

    /// <summary>
    /// Spawn a new creature at the given position with the specified movement
    /// strategy. The position is clamped to arena bounds (with radius margin)
    /// and retried up to 10 times if it overlaps an existing entity.
    /// If <paramref name="traits"/> is null, <see cref="CreatureTraits.Default"/>
    /// is used. If <paramref name="movement"/> is null, <see cref="WalkMode"/>
    /// is used.
    /// </summary>
    public Creature SpawnCreature(
        Vector3 position,
        CreatureTraits? traits = null,
        IMovementMode? movement = null)
    {
        traits ??= CreatureTraits.Default;
        movement ??= new WalkMode();
        float radius = traits.Radius;
        var clamped = Arena.Clamp(position, radius);

        float minDist = radius * 2f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OverlapsAny(clamped, minDist))
                break;

            float rx = (float)(Rng.NextDouble() * (Arena.MaxX - Arena.MinX - radius * 2) + Arena.MinX + radius);
            float rz = (float)(Rng.NextDouble() * (Arena.MaxZ - Arena.MinZ - radius * 2) + Arena.MinZ + radius);
            clamped = new Vector3(rx, clamped.Y, rz);
        }

        var creature = new Creature(clamped, traits, movement);
        Entities.Add(creature);
        return creature;
    }

    private bool OverlapsAny(Vector3 position, float minDist)
    {
        foreach (var entity in Entities)
        {
            if ((entity.Position - position).Length() < minDist)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Advance all entities by <paramref name="delta"/> seconds.
    ///
    /// Pipeline (applied uniformly to every creature):
    ///   1. Apply gravity (scaled by creature's GravityScale)
    ///   2. Movement tick (wander / AI / wall bounce)
    ///   3. Ground clamp (falling through floor → clamp + zero Y velocity)
    ///   4. Resolve entity-entity collisions (sphere-sphere push-apart)
    ///   5. Post-collision ground re-clamp (collision may push below floor)
    /// </summary>
    public void Tick(double delta)
    {
        // --- Per-entity pipeline ---
        foreach (var entity in Entities)
        {
            // 1. Apply gravity
            entity.Velocity = new Vector3(
                entity.Velocity.X,
                entity.Velocity.Y - Gravity * entity.Traits.GravityScale * (float)delta,
                entity.Velocity.Z);

            // 2. Movement tick (wander + wall bounce)
            entity.Movement.Tick(delta, entity, Arena, Rng);

            // 2b. Biome effects (happiness, speed) if a map + catalog are present
            ApplyBiomeEffects(entity, delta);

            // 3. Ground clamp — prevent falling through the floor
            float floor = Arena.MinY + entity.Traits.Radius;
            if (entity.Position.Y < floor)
            {
                entity.Position = new Vector3(
                    entity.Position.X, floor, entity.Position.Z);
                entity.Velocity = new Vector3(
                    entity.Velocity.X, 0f, entity.Velocity.Z);
            }
        }

        // --- Entity collision ---
        ResolveEntityCollisions();

        // --- Post-collision ground re-clamp ---
        foreach (var entity in Entities)
        {
            float floor = Arena.MinY + entity.Traits.Radius;
            if (entity.Position.Y < floor)
                entity.Position = new Vector3(entity.Position.X, floor, entity.Position.Z);
        }
    }

    /// <summary>
    /// Apply the effects of the biome under a creature for this tick: accumulate
    /// happiness and cap horizontal speed by the biome's speed multiplier. No-op
    /// unless both <see cref="Map"/> and <see cref="Biomes"/> are set. Reads position
    /// only — draws no randomness — so the simulation stays deterministic.
    /// </summary>
    private void ApplyBiomeEffects(Creature entity, double delta)
    {
        if (Map is null || Biomes is null)
            return;

        var def = Biomes.Get(Map.BiomeAt(entity.Position));

        entity.Happiness += def.HappinessRate * (float)delta;

        // Cap horizontal speed to the biome-adjusted maximum (terrain slows/speeds movement).
        float maxSpeed = entity.Traits.MaxSpeed * def.SpeedMultiplier;
        var v = entity.Velocity;
        var horizontal = new System.Numerics.Vector3(v.X, 0f, v.Z);
        float speed = horizontal.Length();
        if (speed > maxSpeed && speed > 1e-6f)
        {
            float scale = maxSpeed / speed;
            entity.Velocity = new System.Numerics.Vector3(v.X * scale, v.Y, v.Z * scale);
        }
    }

    // -------------------------------------------------
    // Collision resolution
    // -------------------------------------------------

    /// <summary>
    /// Resolve sphere-sphere overlaps for all entity pairs.
    /// Each pair is pushed apart by half the overlap distance.
    /// </summary>
    private void ResolveEntityCollisions()
    {
        for (int i = 0; i < Entities.Count; i++)
        {
            for (int j = i + 1; j < Entities.Count; j++)
            {
                var a = Entities[i];
                var b = Entities[j];
                float minDist = a.Traits.Radius + b.Traits.Radius;
                (a.Position, b.Position) = PushApart(a.Position, b.Position, minDist);
            }
        }
    }

    /// <summary>
    /// Push two positions apart if they overlap, each by half the overlap.
    /// If the distance is near-zero, nudges apart on a fixed axis.
    /// </summary>
    private static (Vector3 A, Vector3 B) PushApart(Vector3 a, Vector3 b, float minDist)
    {
        var delta = a - b;
        float distance = delta.Length();

        if (distance >= minDist)
            return (a, b);

        if (distance < 1e-6f)
        {
            delta = new Vector3(0.001f, 0f, 0f);
            distance = delta.Length();
        }

        float overlap = minDist - distance;
        var axis = delta / distance;
        var push = axis * (overlap / 2f);

        return (a + push, b - push);
    }
}
