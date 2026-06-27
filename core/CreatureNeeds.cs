namespace Vivarium.Core;

/// <summary>
/// Dynamic 0–1 need state for a creature — the urgency half of the drive/need pair.
/// Needs rise and fall over time and with action; the matching <see cref="Drives"/>
/// supply the personality weight on top. Rates live in <see cref="BehaviorConfig"/>,
/// so tuning never touches code; this type just holds the values and clamps them.
///
/// v1 only drives behavior off Fatigue (Rest); Hunger and Boredom are updated/seated
/// now but light up when foraging and taming land.
/// </summary>
public sealed class CreatureNeeds
{
    /// <summary>Rises over time, relieved by eating/foraging. 0 = sated, 1 = starving.</summary>
    public float Hunger { get; set; }

    /// <summary>Rises while active, restored by resting. 0 = fresh, 1 = exhausted.</summary>
    public float Fatigue { get; set; }

    /// <summary>Rises while idle/understimulated, relieved by play. 0 = engaged, 1 = bored stiff.</summary>
    public float Boredom { get; set; }

    public CreatureNeeds() { }

    /// <summary>Independent copy.</summary>
    public CreatureNeeds(CreatureNeeds other)
    {
        Hunger = other.Hunger;
        Fatigue = other.Fatigue;
        Boredom = other.Boredom;
    }

    /// <summary>Clamp every need back into [0,1]. Called after each per-tick update.</summary>
    public void Clamp()
    {
        Hunger = Math.Clamp(Hunger, 0f, 1f);
        Fatigue = Math.Clamp(Fatigue, 0f, 1f);
        Boredom = Math.Clamp(Boredom, 0f, 1f);
    }

    /// <summary>Randomize all three needs to 0–1 using the given RNG, so
    /// spawned creatures start out of phase. Deterministic for seeded RNGs.</summary>
    public void Randomize(Random rng)
    {
        Hunger = (float)rng.NextDouble();
        Fatigue = (float)rng.NextDouble();
        Boredom = (float)rng.NextDouble();
    }
}
