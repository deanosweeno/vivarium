using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Builds the per-creature <see cref="SenseContext"/> — the perception snapshot the Utility AI
/// reads. Extracted from the Simulator so perception is a pure, independently testable function
/// of (self, world): nearest neighbour + crowd separation push, flock cohesion target, nearest
/// edible food, biome comfort + gradient, and the player/taming channel. Reads positions only —
/// no randomness — so decisions stay deterministic.
/// </summary>
public static class PerceptionBuilder
{
    /// <summary>
    /// Assemble the perception snapshot for <paramref name="self"/>. <paramref name="nearestFood"/>
    /// supplies the world's food query (kept as a delegate so this stays decoupled from food storage).
    /// </summary>
    public static SenseContext Build(
        Creature self,
        IReadOnlyList<Creature> entities,
        IReadOnlyList<Flock> flocks,
        Creature? player,
        MapData? map,
        BiomeCatalog? biomes,
        BehaviorConfig behavior,
        IFleeStrategy fleeStrategy,
        Func<Vector3, HashSet<string>?, (FoodItem? Item, float Dist)> nearestFood)
    {
        // Per-creature-type override (CreatureTraits.SenseRadius, set from creatures.json) beats
        // the shared config default, so a keen-eyed vs. dull creature perceives differently.
        float radius = self.Traits.SenseRadius ?? behavior.SenseRadius;

        // Nearest neighbour (horizontal distance) + crowd separation push, summed over every
        // crowding body so a creature is shoved out of a clump, not just away from its nearest.
        Creature? nearest = null;
        float nearestDist = float.MaxValue;
        float personalSpace = self.Traits.Radius * behavior.PersonalSpaceRadii;
        var separationPush = Vector3.Zero;
        foreach (var other in entities)
        {
            if (ReferenceEquals(other, self)) continue;
            // The player is sensed through its own dedicated channel below, not as a generic
            // neighbour — so it never pulls a creature into Approach or counts as a flock-mate.
            if (other.IsPlayer) continue;
            var d = other.Position - self.Position;
            float dist = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
            // Avoidance reacts to ANY body in personal space, summed (deeper intrusion = stronger
            // push). Skip exact overlap — ResolveEntityCollisions handles that.
            if (dist < personalSpace && dist > 1e-4f)
                separationPush += new Vector3(-d.X, 0f, -d.Z) / dist * (1f - dist / personalSpace);
        }

        bool hasNeighbor = nearest is not null && nearestDist <= radius;
        float proximity = hasNeighbor ? 1f - nearestDist / radius : 0f;

        // Flock cohesion targets the creature's flock anchor (an explicit group entity managed by
        // the flock system), not a live kin centroid — the herd moves as one circle around it.
        var flock = self.Flock;
        bool hasFlock = flock is { Members.Count: > 0 };
        Vector3 flockAnchor = hasFlock ? flock!.Anchor : self.Position;
        float flockRadius = hasFlock ? flock!.Radius : 0f;

        // Nearest available food this creature can eat (horizontal distance). Always populated
        // so Forage can path to food even when it's outside immediate sense range.
        var (foodItem, foodDist) = nearestFood(self.Position, self.Diet);
        float foodSenseRadius = self.Traits.FoodSenseRadius ?? behavior.FoodSenseRadius;
        bool hasFood = foodItem is not null && foodDist <= foodSenseRadius;
        float foodProximity = hasFood ? 1f - foodDist / foodSenseRadius : 0f;

        // --- biome awareness ---
        Biome currentBiome = Biome.Plains;
        float biomeComfort = 1f;
        Vector3 biomePush = Vector3.Zero;
        if (map is not null && biomes is not null && self.Traits.PreferredBiomes.Count > 0)
        {
            currentBiome = map.BiomeAt(self.Position);
            var preferred = self.Traits.PreferredBiomes;
            biomeComfort = preferred.Contains(currentBiome.ToString()) ? 1f : 0f;

            // If outside a preferred biome, compute a push toward the nearest preferred cell.
            // Scans a coarse grid (every BiomeSearchStep cells) for speed; a full scan is O(map²).
            if (biomeComfort < 1f)
            {
                float bestDist = float.MaxValue;
                int bestCx = 0, bestCz = 0;
                var (sx, sz) = map.WorldToCell(self.Position);
                int searchCells = behavior.BiomeSearchCells;
                int step = behavior.BiomeSearchStep;
                int minCx = Math.Max(0, sx - searchCells);
                int maxCx = Math.Min(map.Width - 1, sx + searchCells);
                int minCz = Math.Max(0, sz - searchCells);
                int maxCz = Math.Min(map.Depth - 1, sz + searchCells);
                for (int cx = minCx; cx <= maxCx; cx += step)
                {
                    for (int cz = minCz; cz <= maxCz; cz += step)
                    {
                        if (!preferred.Contains(map.GetBiome(cx, cz).ToString())) continue;
                        float wx = (cx - map.Width / 2f + 0.5f) * map.CellSize;
                        float wz = (cz - map.Depth / 2f + 0.5f) * map.CellSize;
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
                    float wx = (bestCx - map.Width / 2f + 0.5f) * map.CellSize;
                    float wz = (bestCz - map.Depth / 2f + 0.5f) * map.CellSize;
                    var toTarget = new Vector3(wx - self.Position.X, 0f, wz - self.Position.Z);
                    float len = toTarget.Length();
                    if (len > 1e-6f)
                        biomePush = toTarget / len;
                }
            }
        }

        // --- player awareness (taming channel) ---
        bool hasPlayer = false;
        Vector3 playerPosition = self.Position;
        float playerProximity = 0f;
        bool playerHoldingFood = false;
        if (player is not null && !self.IsPlayer)
        {
            float pd = Vec.HorizDist(self.Position, player.Position);
            if (pd <= radius)
            {
                hasPlayer = true;
                playerPosition = player.Position;
                playerProximity = 1f - pd / radius;
                playerHoldingFood = (player.Movement as PlayerInputMode)?.HoldingFood ?? false;
            }
        }

        // Player threat: delegated to the injected flee strategy so the decision is
        // per-creature-type (sheep fear only when no food is offered; future deer may
        // always flee, sloths may never care). self.FleeStrategy (set by HerdSpawner from
        // CreatureDef.FleeStrategy) overrides the Simulator-wide default when present.
        bool isPlayerThreat = hasPlayer
            && (self.FleeStrategy ?? fleeStrategy).IsPlayerThreat(playerHoldingFood, self.Needs.Affection);

        // Normalized separation time for SeekFlock consideration.
        float separationTime = MathF.Min(1f, self.SeparationTimer / behavior.SeekFlockDelay);

        // Nearest kin flock (for SeekFlock steering). Reuses the kin gate from the flock Join rule.
        bool hasNearbyFlock = false;
        Vector3 nearestFlockAnchor = self.Position;
        if (!hasFlock)
        {
            float bestFlockDist = float.MaxValue;
            foreach (var otherFlock in flocks)
            {
                if (otherFlock.Members.Count == 0) continue;
                float d = Vec.HorizDist(self.Position, otherFlock.Anchor);
                if (d < bestFlockDist
                    && Genetics.Similarity(self, otherFlock.Members[0]) >= behavior.HerdKinThreshold)
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
            HasPlayer = hasPlayer,
            PlayerPosition = playerPosition,
            PlayerProximity = playerProximity,
            PlayerHoldingFood = playerHoldingFood,
            IsPlayerThreat = isPlayerThreat,
            Hunger = self.Needs.Hunger,
            Fatigue = self.Needs.Fatigue,
            Boredom = self.Needs.Boredom,
            Affection = self.Needs.Affection,
            CurrentBiome = currentBiome,
            BiomeComfort = biomeComfort,
            BiomePush = biomePush,
        };
    }
}
