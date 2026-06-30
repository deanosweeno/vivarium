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
    /// <see cref="BehaviorConfig.DecisionInterval"/> by <see cref="FlockManager"/>; each flock's
    /// anchor is then advanced so its member circle drifts as one. Read by the visual layer if it
    /// wants to draw group state.
    /// </summary>
    public List<Flock> Flocks { get; } = new();

    /// <summary>Reconciles flock membership (form/join/leave/merge) each decision interval.</summary>
    private readonly FlockManager _flockManager = new();

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
    /// Injected flee-from-player strategy — owns all flee tunables (speed, direction, threat
    /// detection). The Simulator passes this to creature brains and flock anchors so flee
    /// behavior is per-creature-type without coupling the brain to a specific creature.</summary>
    public IFleeStrategy FleeStrategy { get; set; } = new SheepFleeStrategy(new BehaviorConfig().PartialBondThreshold);

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

    /// <summary>
    /// The player-controlled avatar, or null before <see cref="SpawnPlayer"/> runs. Singled out of
    /// the generic neighbour scan so creatures can react to it specifically (flee / follow), and
    /// the source of the player-interaction intents resolved each tick.
    /// </summary>
    public Creature? Player { get; private set; }

    /// <summary>Drives the player's compositional spine: routes input intents to interaction verbs
    /// and derives the avatar's <see cref="PlayerState"/>. Verbs registered in
    /// <see cref="PlayerInteractions.Default"/>.</summary>
    private readonly PlayerController _playerController = new(PlayerInteractions.Default());

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
               new BlobFactory(Behavior, FleeStrategy, Rng),
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
        Player = blob;
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

        // --- Player interactions (feed / soothe / play) consume this frame's intents ---
        ResolvePlayerInteractions();

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
                entity.LastSenses = senses;
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
            {
                UpdateNeeds(entity, delta);

                // 2d. Resolve the player-lane need bubble. Suppress hunger/boredom when the
                // creature is already self-satisfying them (foraging / frolicking).
                bool foraging = entity.Brain.Current?.Steering == SteeringKind.Forage;
                entity.Broadcast = NeedBroadcast.Resolve(entity.Needs, foraging, entity.IsFrolicking, Behavior);
            }

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

        // --- Player animation state (Idle/Walking/Interacting) from the movement + verb seams ---
        if (Player is not null)
            _playerController.UpdateState(Player, delta);

        // --- Flocks: re-group membership periodically, then drift each flock's anchor as one ---
        _flockManager.Update(delta, Entities, Flocks, Behavior, GroundFloor);
        foreach (var flock in Flocks)
        {
            // Does any member of this flock see the player as a threat?
            // If so, the whole flock bolts — anchor moves away, members cohere.
            bool flockFlee = false;
            Vector3 playerPos = Vector3.Zero;
            if (FleeStrategy.FlockFleesAsGroup)
            {
                foreach (var m in flock.Members)
                {
                    if (m.LastSenses.IsPlayerThreat && m.LastSenses.HasPlayer)
                    {
                        flockFlee = true;
                        playerPos = m.LastSenses.PlayerPosition;
                        break;
                    }
                }
            }
            flock.AdvanceAnchor(delta, Arena, Rng, this, Behavior, FleeStrategy, flockFlee, playerPos);
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
    internal SenseContext BuildSenses(Creature self)
        => PerceptionBuilder.Build(
            self, Entities, Flocks, Player, Map, Biomes, Behavior, FleeStrategy, NearestFood);

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
            case SteeringKind.FollowPlayer when senses.HasPlayer:
            case SteeringKind.AvoidPlayer when senses.HasPlayer:
                return senses.PlayerPosition;
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
        => Vec.NearestBy(
            Food, from,
            item => item.Position,
            item => item.Available && (diet is not { Count: > 0 } || diet.Contains(item.Def.Id)));

    // -------------------------------------------------
    // Flock system (membership: form / join / leave / merge)
    // -------------------------------------------------

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
        => GrazingSystem.Resolve(delta, Entities, Food.Count > 0, FoodSpawn, NearestFood);

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

    /// <summary>
    /// Resolve the player's per-frame interaction intents against the nearest creature in reach.
    /// Edge-triggered: each intent flag is consumed (cleared) whether or not it lands, so one
    /// keypress = one interaction. Feeding needs food in hand and works on any creature (the trust
    /// builder); the pet verbs (Soothe/Play) require the creature to have crossed
    /// <see cref="BehaviorConfig.PartialBondThreshold"/> first. All effects raise Affection, driving
    /// the taming arc. Deterministic — reads positions and flags only.
    /// </summary>
    private void ResolvePlayerInteractions()
    {
        if (Player is null) return;
        _playerController.Resolve(Player, Entities, Food, Behavior, Rng);
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
