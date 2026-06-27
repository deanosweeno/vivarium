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
public sealed class Simulator : IFlockEnv
{
    /// <summary>
    /// All entities in the simulation. Includes both generic Creatures and
    /// Blob instances (which extend Creature).
    /// </summary>
    public List<Creature> Entities { get; } = new();

    /// <summary>
    /// Active flocks (explicit group entities). Formed, grown, merged, and pruned each
    /// <see cref="BehaviorConfig.DecisionInterval"/> by <see cref="UpdateFlocks"/>; each flock's
    /// anchor is then advanced so its member circle drifts as one. Read by the visual layer if it
    /// wants to draw group state.
    /// </summary>
    public List<Flock> Flocks { get; } = new();

    private double _flockTimer;

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
    /// The unified spawn seam. Resolves the desired position through an optional
    /// <see cref="IPlacementStrategy"/> chain, then creates the creature via the given
    /// <see cref="ICreatureFactory"/>. Default placement is arena-clamp only (no overlap
    /// checks); compose <see cref="OverlapAvoidingPlacement"/> / <see cref="BiomeFilteredPlacement"/>
    /// for the typical spawn path.
    /// </summary>
    public Creature Spawn(
        Vector3 desiredPosition,
        ICreatureFactory factory,
        IPlacementStrategy? placement = null,
        CreatureTraits? traits = null,
        IMovementMode? movement = null,
        Drives? drives = null)
    {
        traits ??= CreatureTraits.Default;
        drives ??= Drives.Default;
        placement ??= ArenaClampPlacement.Instance;

        var ctx = new PlacementContext(Arena, Entities, Rng);
        var position = placement.Place(desiredPosition, traits, ctx);
        var creature = factory.Create(position, traits, movement, drives, ctx);
        Entities.Add(creature);
        return creature;
    }

    // ---- convenience wrappers (delegate to Spawn) ----

    /// <summary>
    /// Spawn a blob with a random pastel color at the given position, avoiding overlap
    /// (the original <c>SpawnBlob</c> behavior).
    /// </summary>
    public Blob SpawnBlob(Vector3 position) => SpawnBlob(position, traits: null, drives: null);

    /// <summary>
    /// Spawn a blob with explicit traits and/or drives, avoiding overlap.
    /// </summary>
    public Blob SpawnBlob(Vector3 position, CreatureTraits? traits, Drives? drives)
        => (Blob)Spawn(position,
               new BlobFactory(Behavior, Rng),
               new OverlapAvoidingPlacement(ArenaClampPlacement.Instance),
               traits ?? Blob.DefaultBlobTraits,
               null,
               drives ?? Drives.Randomized(Rng));

    /// <summary>
    /// Spawn a Creature with the given movement strategy, avoiding overlap.
    /// </summary>
    public Creature SpawnCreature(
        Vector3 position,
        CreatureTraits? traits = null,
        IMovementMode? movement = null)
        => Spawn(position,
               BaseCreatureFactory.Instance,
               new OverlapAvoidingPlacement(ArenaClampPlacement.Instance),
               traits,
               movement,
               null);

    /// <summary>
    /// Spawn the player avatar: gold color, PlayerInputMode, no brain, avoiding overlap.
    /// </summary>
    public (Blob Player, PlayerInputMode Input) SpawnPlayer(Vector3 position)
    {
        var traits = new CreatureTraits(Blob.DefaultBlobTraits) { MaxSpeed = 2.0f };
        var creature = Spawn(position,
                             PlayerFactory.Instance,
                             new OverlapAvoidingPlacement(ArenaClampPlacement.Instance),
                             traits);
        var blob = (Blob)creature;
        return (blob, (PlayerInputMode)blob.Movement);
    }

    // ---- food ----

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
                // Track separation time for SeekFlock: reset when in a flock, accumulate otherwise.
                if (entity.Flock != null)
                    entity.SeparationTimer = 0f;
                else
                    entity.SeparationTimer += (float)delta;

                var senses = BuildSenses(entity);
                entity.Brain.Tick(delta, entity, senses, Rng);
                entity.FocusPosition = ResolveFocus(entity.Brain.Current?.Steering, senses);

                // Biome gradient: when outside a preferred biome, push gently toward the nearest
                // preferred cell. Applied AFTER the brain so biome preference is physics, not
                // decision-making. The brain decides the action; the sim biases its path.
                if (senses.BiomeComfort < 1f && senses.BiomePush.LengthSquared() > 1e-6f)
                {
                    var desired = entity.DesiredVelocity;
                    float maxSpeed = entity.Traits.MaxSpeed;
                    var bias = senses.BiomePush * maxSpeed * Behavior.BiomeGradientWeight;
                    var blended = desired + bias;
                    float blendedLen = blended.Length();
                    if (blendedLen > maxSpeed && blendedLen > 1e-6f)
                        blended *= maxSpeed / blendedLen;
                    entity.DesiredVelocity = blended;
                }
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

        // --- Flocks: re-group membership periodically, then drift each flock's anchor as one ---
        UpdateFlocks(delta);
        foreach (var flock in Flocks)
            flock.AdvanceAnchor(delta, Arena, Rng, this, Behavior);

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
    internal SenseContext BuildSenses(Creature self)
    {
        float radius = Behavior.SenseRadius;

        // Nearest neighbor (horizontal distance). One pass over the entity list.
        Creature? nearest = null;
        float nearestDist = float.MaxValue;
        // Personal space and the running separation push, summed over every crowding body so a
        // creature is shoved out of a clump, not just away from its single nearest neighbor.
        float personalSpace = self.Traits.Radius * Behavior.PersonalSpaceRadii;
        var separationPush = Vector3.Zero;
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
            // Avoidance reacts to ANY body in personal space, summed (a weighted away-vector, deeper
            // intrusion = stronger push). Skip exact overlap — ResolveEntityCollisions handles that.
            if (dist < personalSpace && dist > 1e-4f)
                separationPush += new Vector3(-d.X, 0f, -d.Z) / dist * (1f - dist / personalSpace);
        }

        bool hasNeighbor = nearest is not null && nearestDist <= radius;
        float proximity = hasNeighbor ? 1f - nearestDist / radius : 0f;

        // Flock cohesion targets the creature's flock anchor (an explicit group entity managed by
        // UpdateFlocks), not a live kin centroid — the herd moves as one circle around the anchor.
        var flock = self.Flock;
        bool hasFlock = flock is { Members.Count: > 0 };
        Vector3 flockAnchor = hasFlock ? flock!.Anchor : self.Position;
        float flockRadius = hasFlock ? flock!.Radius : 0f;

        // Nearest available food this creature can eat (horizontal distance). Always populated
        // so Forage can path to food even when it's outside immediate sense range.
        var (foodItem, foodDist) = NearestFood(self.Position, self.Diet);
        float foodSenseRadius = Behavior.FoodSenseRadius;
        bool hasFood = foodItem is not null && foodDist <= foodSenseRadius;
        float foodProximity = hasFood ? 1f - foodDist / foodSenseRadius : 0f;

        // --- biome awareness ---
        Biome currentBiome = Biome.Plains;
        float biomeComfort = 1f;
        Vector3 biomePush = Vector3.Zero;
        if (Map is not null && Biomes is not null && self.Traits.PreferredBiomes.Count > 0)
        {
            currentBiome = Map.BiomeAt(self.Position);
            var preferred = self.Traits.PreferredBiomes;
            biomeComfort = preferred.Contains(currentBiome.ToString()) ? 1f : 0f;

            // If outside a preferred biome, compute a push toward the nearest preferred cell.
            // Scans a coarse grid (every 2 cells) for speed; a full per-cell scan is O(map²).
            if (biomeComfort < 1f)
            {
                float bestDist = float.MaxValue;
                int bestCx = 0, bestCz = 0;
                var (sx, sz) = Map.WorldToCell(self.Position);
                // Search radius in cells — cap at map bounds
                int searchCells = 12;
                int minCx = Math.Max(0, sx - searchCells);
                int maxCx = Math.Min(Map.Width - 1, sx + searchCells);
                int minCz = Math.Max(0, sz - searchCells);
                int maxCz = Math.Min(Map.Depth - 1, sz + searchCells);
                for (int cx = minCx; cx <= maxCx; cx += 2)
                {
                    for (int cz = minCz; cz <= maxCz; cz += 2)
                    {
                        if (!preferred.Contains(Map.GetBiome(cx, cz).ToString())) continue;
                        float wx = (cx - Map.Width / 2f + 0.5f) * Map.CellSize;
                        float wz = (cz - Map.Depth / 2f + 0.5f) * Map.CellSize;
                        float dx = wx - self.Position.X;
                        float dz = wz - self.Position.Z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < bestDist)
                        {
                            bestDist = d2;
                            bestCx = cx;
                            bestCz = cz;
                        }
                    }
                }
                if (bestDist < float.MaxValue)
                {
                    float wx = (bestCx - Map.Width / 2f + 0.5f) * Map.CellSize;
                    float wz = (bestCz - Map.Depth / 2f + 0.5f) * Map.CellSize;
                    var toTarget = new Vector3(wx - self.Position.X, 0f, wz - self.Position.Z);
                    float len = toTarget.Length();
                    if (len > 1e-6f)
                        biomePush = toTarget / len;
                }
            }
        }

        // Normalized separation time for SeekFlock consideration.
        float separationTime = MathF.Min(1f, self.SeparationTimer / Behavior.SeekFlockDelay);

        // Nearest kin flock (for SeekFlock steering). Reuses the kin gate from UpdateFlocks Join.
        bool hasNearbyFlock = false;
        Vector3 nearestFlockAnchor = self.Position;
        if (!hasFlock)
        {
            float bestFlockDist = float.MaxValue;
            foreach (var otherFlock in Flocks)
            {
                if (otherFlock.Members.Count == 0) continue;
                float d = HorizDist(self.Position, otherFlock.Anchor);
                if (d < bestFlockDist
                    && Genetics.Similarity(self, otherFlock.Members[0]) >= Behavior.HerdKinThreshold)
                {
                    bestFlockDist = d;
                    nearestFlockAnchor = otherFlock.Anchor;
                    hasNearbyFlock = true;
                }
            }
        }

        return new SenseContext
        {
            SelfPosition = self.Position,
            HasNeighbor = hasNeighbor,
            NeighborPosition = nearest?.Position ?? self.Position,
            NeighborProximity = proximity,
            SeparationPush = separationPush,
            HasFlock = hasFlock,
            FlockAnchor = flockAnchor,
            FlockRadius = flockRadius,
            HasFood = hasFood,
            FoodPosition = foodItem?.Position ?? self.Position,
            FoodDistance = foodDist,
            FoodProximity = foodProximity,
            SeparationTime = separationTime,
            HasNearbyFlock = hasNearbyFlock,
            NearestFlockAnchor = nearestFlockAnchor,
            Hunger = self.Needs.Hunger,
            Fatigue = self.Needs.Fatigue,
            Boredom = self.Needs.Boredom,
            CurrentBiome = currentBiome,
            BiomeComfort = biomeComfort,
            BiomePush = biomePush,
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
            case SteeringKind.Flock when senses.HasFlock:
                return senses.FlockAnchor;
            case SteeringKind.SeekFlock when senses.HasNearbyFlock:
                return senses.NearestFlockAnchor;
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

    // -------------------------------------------------
    // Flock system (membership: form / join / leave / merge)
    // -------------------------------------------------

    /// <summary>
    /// Periodically (every <see cref="BehaviorConfig.DecisionInterval"/>) reconcile flock membership
    /// over the entity list — deterministic, positions only:
    ///   • <b>Leave</b>: drop members that have strayed past <see cref="BehaviorConfig.FlockLeaveRadius"/>.
    ///   • <b>Join</b>: an unflocked kin within <see cref="BehaviorConfig.FlockJoinRadius"/> of an
    ///     existing flock's anchor joins it.
    ///   • <b>Form</b>: clusters of still-unflocked kin seed a new flock at their centroid.
    ///   • <b>Merge</b>: flocks whose anchors close within <see cref="BehaviorConfig.FlockMergeRadius"/>
    ///     fold the smaller into the larger.
    /// Only brained creatures flock; the kin gate (<see cref="Genetics.Similarity"/> ≥
    /// <see cref="BehaviorConfig.HerdKinThreshold"/>) keeps non-kin (Sprouts, the player) out.
    /// TODO: hysteresis timer on Leave + split-on-oversize flock.
    /// </summary>
    private void UpdateFlocks(double delta)
    {
        _flockTimer -= delta;
        if (_flockTimer > 0) return;
        _flockTimer = Behavior.DecisionInterval;

        float joinR = Behavior.FlockJoinRadius;
        float leaveR = Behavior.FlockLeaveRadius;
        float mergeR = Behavior.FlockMergeRadius;

        // 1. Leave — drop strayed members.
        foreach (var flock in Flocks)
        {
            for (int i = flock.Members.Count - 1; i >= 0; i--)
            {
                var m = flock.Members[i];
                if (HorizDist(m.Position, flock.Anchor) > leaveR)
                {
                    m.Flock = null;
                    flock.Members.RemoveAt(i);
                }
            }
        }
        Flocks.RemoveAll(f => f.Members.Count == 0);

        // 2. Join — an unflocked kin near an existing flock's anchor joins it.
        foreach (var e in Entities)
        {
            if (e.Brain is null || e.Flock is not null) continue;
            Flock? best = null;
            float bestD = joinR;
            foreach (var flock in Flocks)
            {
                float d = HorizDist(e.Position, flock.Anchor);
                if (d <= bestD && Genetics.Similarity(e, flock.Members[0]) >= Behavior.HerdKinThreshold)
                {
                    bestD = d;
                    best = flock;
                }
            }
            if (best is not null)
            {
                best.Members.Add(e);
                e.Flock = best;
            }
        }

        // 3. Form — cluster still-unflocked kin into new flocks.
        for (int i = 0; i < Entities.Count; i++)
        {
            var a = Entities[i];
            if (a.Brain is null || a.Flock is not null) continue;
            List<Creature>? group = null;
            for (int j = 0; j < Entities.Count; j++)
            {
                if (i == j) continue;
                var b = Entities[j];
                if (b.Brain is null || b.Flock is not null) continue;
                if (HorizDist(a.Position, b.Position) <= joinR
                    && Genetics.Similarity(a, b) >= Behavior.HerdKinThreshold)
                {
                    group ??= new List<Creature> { a };
                    group.Add(b);
                }
            }
            if (group is not null)
            {
                var centroid = Vector3.Zero;
                foreach (var m in group) centroid += m.Position;
                centroid /= group.Count;
                var flock = new Flock(new Vector3(centroid.X, GroundFloor(centroid), centroid.Z));
                foreach (var m in group)
                {
                    flock.Members.Add(m);
                    m.Flock = flock;
                }
                Flocks.Add(flock);
            }
        }

        // 4. Merge — fold a smaller flock into a nearby larger kin flock.
        for (int i = 0; i < Flocks.Count; i++)
        {
            if (Flocks[i].Members.Count == 0) continue;
            for (int j = i + 1; j < Flocks.Count; j++)
            {
                var fa = Flocks[i];
                var fb = Flocks[j];
                if (fa.Members.Count == 0) break;
                if (fb.Members.Count == 0) continue;
                if (HorizDist(fa.Anchor, fb.Anchor) > mergeR) continue;
                if (Genetics.Similarity(fa.Members[0], fb.Members[0]) < Behavior.HerdKinThreshold) continue;

                var (keep, drop) = fa.Members.Count >= fb.Members.Count ? (fa, fb) : (fb, fa);
                foreach (var m in drop.Members)
                {
                    m.Flock = keep;
                    keep.Members.Add(m);
                }
                drop.Members.Clear();
            }
        }
        Flocks.RemoveAll(f => f.Members.Count == 0);
    }

    private static float HorizDist(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    // --- IFlockEnv: read-only world access for Flock.AdvanceAnchor ---

    float IFlockEnv.GroundFloor(Vector3 pos) => GroundFloor(pos);

    (Vector3 Position, bool Has) IFlockEnv.NearestFood(Vector3 from, HashSet<string>? diet)
    {
        var (item, _) = NearestFood(from, diet);
        return item is null ? (from, false) : (item.Position, true);
    }

    /// <summary>
    /// Each creature grazes the nearest available food within eat range, draining the
    /// item and lowering the creature's Hunger by the nutrition consumed this tick.
    /// Grazing eligibility is declared by the current action's <see cref="GrazingMode"/>
    /// — the Simulator doesn't care which specific <see cref="SteeringKind"/> it is.
    /// </summary>
    private void ResolveGrazing(double delta)
    {
        if (Food.Count == 0) return;
        float dt = (float)delta;

        foreach (var entity in Entities)
        {
            var action = entity.Brain?.Current;
            bool canGraze = action?.Grazing switch
            {
                GrazingMode.Always => true,
                GrazingMode.WhenHungry => entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold,
                _ => false
            };
            if (!canGraze) continue;

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

        // Fatigue: recovers only when nearly stopped, accrues with travel speed.
        if (speedFrac < 0.1f)
            n.Fatigue -= entity.Traits.FatigueRecoverPerSec * dt;
        else
            n.Fatigue += entity.Traits.FatigueGainPerSec * speedFrac * dt;

        // Boredom: relieved only by Frolic (play). Every other action — including
        // active Wander, Flock jostling, Forage — builds it. This makes boredom a
        // genuine "need for play" meter, not a speedometer.
        if (entity.IsFrolicking)
            n.Boredom -= Behavior.BoredomRelievePerSec * dt;
        else
            n.Boredom += Behavior.BoredomGainPerSec * dt;
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

                // Horizontal overlap check before push, so we can also strip the inward velocity
                // that drove them together — otherwise momentum re-rams them next tick (jitter).
                var sep = new Vector3(a.Position.X - b.Position.X, 0f, a.Position.Z - b.Position.Z);
                float horiz = sep.Length();
                bool overlapping = horiz < minDist && horiz > 1e-6f;

                (a.Position, b.Position) = PushApart(a.Position, b.Position, minDist);

                if (overlapping)
                {
                    var axis = sep / horiz;                 // unit vector from b toward a, XZ plane
                    KillInwardVelocity(a, axis);            // a moving toward b (-axis) → cancel it
                    KillInwardVelocity(b, -axis);
                }
            }
        }
    }

    /// <summary>
    /// Push two positions apart if they overlap, each by half the overlap.
    /// If the distance is near-zero, nudges apart on a fixed axis.
    /// </summary>
    /// <summary>
    /// Remove the component of a creature's horizontal velocity that points along
    /// <paramref name="outwardAxis"/> negated — i.e. any speed driving it <em>into</em> the body it
    /// just collided with. Leaves Y untouched (gravity) and any sideways/separating motion intact,
    /// so a settled pair stops ramming instead of bouncing. Deterministic: velocities only, no RNG.
    /// </summary>
    private static void KillInwardVelocity(Creature c, Vector3 outwardAxis)
    {
        var v = c.Velocity;
        var horiz = new Vector3(v.X, 0f, v.Z);
        float inward = Vector3.Dot(horiz, outwardAxis);   // <0 means moving toward the other body
        if (inward < 0f)
        {
            horiz -= outwardAxis * inward;                // cancel only the inward component
            c.Velocity = new Vector3(horiz.X, v.Y, horiz.Z);
        }
    }

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
