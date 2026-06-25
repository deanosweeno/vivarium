namespace Vivarium.Core;

/// <summary>
/// Innate, heritable personality weights for a creature — the first slice of the
/// genotype. Each value is in [0,1] and biases how strongly the Utility AI weights
/// a category of action (see <see cref="UtilityBrain"/> / <see cref="Consideration"/>).
///
/// Drives are stable for a creature's lifetime (the "gene"); dynamic urgency lives in
/// <see cref="CreatureNeeds"/>. A consideration typically multiplies a need/perception
/// curve by a drive: <c>score = curve(need) × drive</c>, so the drive answers "how much
/// does THIS creature care about that at all".
///
/// Seated as data now; only the drives with a v1 action wired are consumed
/// (curiosity, fear, sociability, appetite). PlayCuddle and aggression are stored for
/// the taming/combat pillars so the genotype shape never has to widen later.
/// </summary>
public sealed class Drives
{
    /// <summary>Urge to explore / wander into new space. Feeds the Wander action.</summary>
    public float Curiosity { get; set; } = 0.5f;

    /// <summary>Skittishness. Feeds Flee and (inverted) gates how close it will Approach.</summary>
    public float Fear { get; set; } = 0.5f;

    /// <summary>Desire to interact with the player and other creatures. Feeds Approach.</summary>
    public float Sociability { get; set; } = 0.5f;

    /// <summary>
    /// Interaction-style axis: 0 = prefers calm/cuddle/grooming, 1 = prefers energetic
    /// play. Selects which taming verb lands (consumed when taming lands).
    /// </summary>
    public float PlayCuddle { get; set; } = 0.5f;

    /// <summary>Foraging weight (pairs with the Hunger need). Scales the Forage action.</summary>
    public float Appetite { get; set; } = 0.5f;

    /// <summary>
    /// Boldness in contesting others. For now only modulates Approach vs Flee; its own
    /// contest action arrives with combat.
    /// </summary>
    public float Aggression { get; set; } = 0.5f;

    /// <summary>A neutral creature with every drive at 0.5.</summary>
    public static Drives Default => new();

    public Drives() { }

    /// <summary>Independent copy — mutating one does not affect the other.</summary>
    public Drives(Drives other)
    {
        Curiosity = other.Curiosity;
        Fear = other.Fear;
        Sociability = other.Sociability;
        PlayCuddle = other.PlayCuddle;
        Appetite = other.Appetite;
        Aggression = other.Aggression;
    }

    /// <summary>
    /// Roll a random temperament from the given RNG (each drive uniform in [0,1]).
    /// Deterministic for a seeded RNG, so spawns stay reproducible.
    /// </summary>
    public static Drives Randomized(Random rng) => new()
    {
        Curiosity = (float)rng.NextDouble(),
        Fear = (float)rng.NextDouble(),
        Sociability = (float)rng.NextDouble(),
        PlayCuddle = (float)rng.NextDouble(),
        Appetite = (float)rng.NextDouble(),
        Aggression = (float)rng.NextDouble(),
    };
}
