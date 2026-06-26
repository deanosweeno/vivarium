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

    /// <summary>
    /// Tunables + action table for the Utility AI. Shared by reference with every
    /// creature's <see cref="UtilityBrain"/>. Defaults to the v1 "full five" action set.
    /// </summary>
    public BehaviorConfig Behavior { get; set; } = new();

    /// <summary>
    /// Growable food items in the world. Creatures graze these to satisfy Hunger; depleted
    /// items regrow on a timer (see <see cref="FoodItem"/>). Seeded by <see cref="SeedFood"/>.
    /// </summary>
    public List<FoodItem> Food { get; } = new();

    /// <summary>
    /// Optional food-type catalog. Together with <see cref="Map"/> + <see cref="Biomes"/> it
    /// lets <see cref="SeedFood"/> place each biome's food type. Null = no food in the world.
    /// </summary>
    public FoodCatalog? Foods { get; set; }

    /// <summary>Tunables for food density and eat range. Defaults are sensible for a small arena.</summary>
    public FoodConfig FoodSpawn { get; set; } = new();

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
    public Blob SpawnBlob(Vector3 position) => SpawnBlob(position, traits: null, drives: null);

    /// <summary>
    /// Spawn a blob with explicit traits and/or temperament — the seam for tailored creatures
    /// (e.g. the sheep) without disturbing the random-blob path. Null <paramref name="traits"/>
    /// uses <see cref="Blob.DefaultBlobTraits"/>; null <paramref name="drives"/> rolls a random
    /// temperament (so the plain <see cref="SpawnBlob(Vector3)"/> behavior is unchanged). The
    /// caller assigns a <see cref="Creature.Body"/> body plan afterward.
    /// </summary>
    public Blob SpawnBlob(Vector3 position, CreatureTraits? traits, Drives? drives)
    {
        traits ??= Blob.DefaultBlobTraits;
        float radius = traits.Radius;
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
        var blob = new Blob(clamped, r, g, b, Rng, traits: traits, drives: drives ?? Drives.Randomized(Rng));
        blob.Brain = new UtilityBrain(Behavior);
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

    /// <summary>
    /// Spawn the player-controlled avatar at the given position. It's a <see cref="Blob"/>
    /// (so it reuses the blob visual) but with a distinctive gold color, a faster
    /// <see cref="CreatureTraits.MaxSpeed"/> than grazing blobs, and <see cref="Creature.Brain"/>
    /// left null — the sim therefore skips AI decisions and need updates for it. Its movement
    /// comes from the returned <see cref="PlayerInputMode"/>, which the IO layer feeds each
    /// frame. Placement reuses the same overlap/clamp retry as <see cref="SpawnBlob"/>.
    /// </summary>
    public (Blob Player, PlayerInputMode Input) SpawnPlayer(Vector3 position)
    {
        var traits = new CreatureTraits(Blob.DefaultBlobTraits) { MaxSpeed = 2.0f };
        float radius = traits.Radius;
        var clamped = Arena.Clamp(position, radius);

        float minDist = radius * 2f;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!OverlapsAny(clamped, minDist))
                break;

            float rx = (float)(Rng.NextDouble() * (Arena.MaxX - Arena.MinX - radius * 2) + Arena.MinX + radius);
            float rz = (float)(Rng.NextDouble() * (Arena.MaxZ - Arena.MinZ - radius * 2) + Arena.MinZ + radius);
            clamped = new Vector3(rx, 0f, rz);
        }

        // Bright gold so the avatar reads as "you" against the pastel blobs.
        var input = new PlayerInputMode();
        var player = new Blob(clamped, 1f, 0.84f, 0.2f, Rng, traits: traits)
        {
            Movement = input,
        };
        Entities.Add(player);
        return (player, input);
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
    /// Scatter initial food across the arena. The attempt count scales with arena area and
    /// <see cref="FoodConfig.DensityPer100SqUnits"/>; each candidate is accepted with the
    /// local biome's <see cref="BiomeDef.FoodChance"/> probability and assigned that biome's
    /// <see cref="BiomeDef.FoodType"/>. No-op without a <see cref="Foods"/> catalog. Uses
    /// <see cref="Rng"/>, so a given seed produces the same food layout (deterministic).
    /// </summary>
    public void SeedFood()
    {
        Food.Clear();
        if (Foods is null) return;

        float width = Arena.MaxX - Arena.MinX;
        float depth = Arena.MaxZ - Arena.MinZ;
        if (!float.IsFinite(width) || !float.IsFinite(depth) || width <= 0f || depth <= 0f)
            return;

        int attempts = Math.Max(0, (int)MathF.Round(width * depth / 100f * FoodSpawn.DensityPer100SqUnits));
        for (int i = 0; i < attempts; i++)
        {
            float x = (float)(Rng.NextDouble() * width) + Arena.MinX;
            float z = (float)(Rng.NextDouble() * depth) + Arena.MinZ;
            var pos = new Vector3(x, 0f, z);

            // Biome gates placement (FoodChance) and selects the food type.
            string typeId = "";
            float chance = 1f;
            if (Map is not null && Biomes is not null)
            {
                var def = Biomes.Get(Map.BiomeAt(pos));
                typeId = def.FoodType;
                chance = def.FoodChance;
            }
            if (string.IsNullOrEmpty(typeId)) continue;
            if (Rng.NextDouble() > chance) continue;

            pos = new Vector3(x, GroundFloor(pos), z);
            Food.Add(new FoodItem { Position = pos, Def = Foods.Get(typeId) });
        }
    }

    /// <summary>
    /// Advance all entities by <paramref name="delta"/> seconds.
    ///
    /// Pipeline (applied uniformly to every creature):
    ///   1. Apply gravity (scaled by creature's GravityScale)
    ///   2. Movement tick (wander / AI / wall bounce)
    ///   3. Ground placement — terrain-bound creatures (GravityScale 0) snap to the
    ///      surface under them; gravity creatures clamp up if they fell through it.
    ///      The surface is <see cref="MapData.HeightAt"/> when a map is set, else the
    ///      flat arena floor.
    ///   4. Resolve entity-entity collisions (sphere-sphere push-apart)
    ///   5. Post-collision ground re-placement (collision may push off the surface)
    /// </summary>
    public void Tick(double delta)
    {
        // --- Food regrowth (depleted items count down toward regrowing) ---
        float fdt = (float)delta;
        foreach (var item in Food)
            item.Regrow(fdt);

        // --- Per-entity pipeline ---
        foreach (var entity in Entities)
        {
            // 1. Apply gravity
            entity.Velocity = new Vector3(
                entity.Velocity.X,
                entity.Velocity.Y - Gravity * entity.Traits.GravityScale * (float)delta,
                entity.Velocity.Z);

            // 1b. Utility-AI decision → sets DesiredVelocity (no-op if the creature has no brain)
            if (entity.Brain is not null)
            {
                var senses = BuildSenses(entity);
                entity.Brain.Tick(delta, entity, senses, Rng);
                entity.FocusPosition = ResolveFocus(entity.Brain.Current?.Steering, senses);
            }

            // 2. Movement tick (steer toward DesiredVelocity + wall bounce)
            entity.Movement.Tick(delta, entity, Arena, Rng);

            // 2b. Biome effects (happiness, speed) if a map + catalog are present
            ApplyBiomeEffects(entity, delta);

            // 2c. Advance dynamic needs based on how the creature actually moved
            if (entity.Brain is not null)
                UpdateNeeds(entity, delta);

            // 3. Ground placement — rest the entity on the terrain surface under it.
            float floor = GroundFloor(entity.Position) + entity.Traits.Radius;
            if (entity.Traits.GravityScale == 0f)
            {
                // Terrain-bound (no gravity): hug the surface going up AND down.
                if (entity.Position.Y != floor)
                    entity.Position = new Vector3(
                        entity.Position.X, floor, entity.Position.Z);
            }
            else if (entity.Position.Y < floor)
            {
                // Gravity-driven: stop the fall at the surface.
                entity.Position = new Vector3(
                    entity.Position.X, floor, entity.Position.Z);
                entity.Velocity = new Vector3(
                    entity.Velocity.X, 0f, entity.Velocity.Z);
            }
        }

        // --- Grazing: foraging creatures eat the food they've reached ---
        ResolveGrazing(delta);

        // --- Entity collision ---
        ResolveEntityCollisions();

        // --- Post-collision ground re-placement ---
        foreach (var entity in Entities)
        {
            float floor = GroundFloor(entity.Position) + entity.Traits.Radius;
            if (entity.Traits.GravityScale == 0f)
            {
                if (entity.Position.Y != floor)
                    entity.Position = new Vector3(entity.Position.X, floor, entity.Position.Z);
            }
            else if (entity.Position.Y < floor)
                entity.Position = new Vector3(entity.Position.X, floor, entity.Position.Z);
        }
    }

    /// <summary>
    /// Resting ground height under a world position: the baked terrain surface
    /// (<see cref="MapData.HeightAt"/>) when a <see cref="Map"/> is present, else the flat
    /// arena floor. Reads position only — no randomness — so the sim stays deterministic.
    /// </summary>
    private float GroundFloor(Vector3 pos)
        => Map is not null ? Map.HeightAt(pos) : Arena.MinY;

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
    // Perception & needs (Utility AI support)
    // -------------------------------------------------

    /// <summary>
    /// Assemble the perception snapshot for one creature: nearest neighbor within sense
    /// radius, terrain discomfort + comfort gradient (when a map+catalog are present), and
    /// a copy of its current needs. Reads positions only — no randomness — so decisions
    /// stay deterministic.
    /// </summary>
    private SenseContext BuildSenses(Creature self)
    {
        float radius = Behavior.SenseRadius;

        // Nearest neighbor (horizontal distance), plus the centroid of all in-range neighbors
        // for herd cohesion. One pass over the entity list.
        Creature? nearest = null;
        float nearestDist = float.MaxValue;
        var herdSum = Vector3.Zero;
        int herdCount = 0;
        foreach (var other in Entities)
        {
            if (ReferenceEquals(other, self)) continue;
            var d = other.Position - self.Position;
            float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
            if (dist <= radius)
            {
                herdSum += other.Position;
                herdCount++;
            }
        }

        bool hasNeighbor = nearest is not null && nearestDist <= radius;
        float proximity = hasNeighbor ? 1f - nearestDist / radius : 0f;

        // A herd is two or more in-range neighbors; centroid is their average position.
        bool hasHerd = herdCount >= 2;
        Vector3 herdCentroid = hasHerd ? herdSum / herdCount : self.Position;

        // Nearest available food this creature can eat (horizontal distance).
        var (foodItem, foodDist) = NearestFood(self.Position, self.Diet);
        bool hasFood = foodItem is not null && foodDist <= radius;
        float foodProximity = hasFood ? 1f - foodDist / radius : 0f;

        return new SenseContext
        {
            SelfPosition = self.Position,
            HasNeighbor = hasNeighbor,
            NeighborPosition = nearest?.Position ?? self.Position,
            NeighborProximity = proximity,
            HasHerd = hasHerd,
            HerdCentroid = herdCentroid,
            HasFood = hasFood,
            FoodPosition = foodItem?.Position ?? self.Position,
            FoodProximity = foodProximity,
            Hunger = self.Needs.Hunger,
            Fatigue = self.Needs.Fatigue,
            Boredom = self.Needs.Boredom,
        };
    }

    /// <summary>
    /// What the creature is looking at, given its active steering and current senses:
    /// food while foraging, the neighbor while approaching/fleeing, otherwise nothing.
    /// Cosmetic only — consumed by the visual layer's head/eye look-at.
    /// </summary>
    private static Vector3? ResolveFocus(SteeringKind? steering, in SenseContext senses)
    {
        switch (steering)
        {
            case SteeringKind.Forage when senses.HasFood:
                return senses.FoodPosition;
            case SteeringKind.Approach when senses.HasNeighbor:
            case SteeringKind.Flee when senses.HasNeighbor:
                return senses.NeighborPosition;
            case SteeringKind.Flock when senses.HasHerd:
                return senses.HerdCentroid;
            default:
                return null;
        }
    }

    /// <summary>
    /// Nearest currently-available food item to a world position (horizontal distance), or
    /// (null, +inf) when there is none. O(food); shares the perception pass's partitioning TODO.
    /// When <paramref name="diet"/> is non-null and non-empty, only food whose
    /// <see cref="FoodDef.Id"/> is in the set is considered.
    /// </summary>
    private (FoodItem? Item, float Dist) NearestFood(Vector3 from, HashSet<string>? diet = null)
    {
        FoodItem? nearest = null;
        float nearestDist = float.MaxValue;
        foreach (var item in Food)
        {
            if (!item.Available) continue;
            if (diet is { Count: > 0 } && !diet.Contains(item.Def.Id)) continue;
            var d = item.Position - from;
            float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = item;
            }
        }
        return (nearest, nearestDist);
    }

    /// <summary>
    /// Each foraging creature grazes the nearest available food within eat range, draining the
    /// item and lowering the creature's Hunger by the nutrition consumed this tick. Only
    /// creatures whose current action steers toward <see cref="SteeringKind.Forage"/> eat, so a
    /// creature fleeing across food doesn't snack mid-panic.
    /// </summary>
    private void ResolveGrazing(double delta)
    {
        if (Food.Count == 0) return;
        float dt = (float)delta;

        foreach (var entity in Entities)
        {
            if (entity.Brain?.Current?.Steering != SteeringKind.Forage) continue;

            var (item, dist) = NearestFood(entity.Position, entity.Diet);
            if (item is null) continue;

            float eatRange = entity.Traits.Radius + FoodSpawn.EatRange;
            if (dist > eatRange) continue;

            entity.Needs.Hunger -= item.Bite(dt);
            entity.Needs.Clamp();
        }
    }

    /// <summary>
    /// Advance a creature's needs by one tick. Fatigue drains while moving and recovers at
    /// rest; boredom is the inverse; hunger creeps up steadily (satisfied once food exists).
    /// </summary>
    private void UpdateNeeds(Creature entity, double delta)
    {
        float dt = (float)delta;
        var n = entity.Needs;

        float maxSpeed = MathF.Max(entity.Traits.MaxSpeed, 1e-3f);
        float speed = MathF.Sqrt(entity.Velocity.X * entity.Velocity.X + entity.Velocity.Z * entity.Velocity.Z);
        float speedFrac = Math.Clamp(speed / maxSpeed, 0f, 1f);

        if (speedFrac < 0.1f)
        {
            n.Fatigue -= Behavior.FatigueRecoverPerSec * dt;
            n.Boredom += Behavior.BoredomGainPerSec * dt;
        }
        else
        {
            n.Fatigue += Behavior.FatigueGainPerSec * speedFrac * dt;
            n.Boredom -= Behavior.BoredomRelievePerSec * dt;
        }
        n.Hunger += Behavior.HungerGainPerSec * dt;
        n.Clamp();
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
