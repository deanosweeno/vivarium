namespace Vivarium.Core;

/// <summary>
/// Single seam between <see cref="StatKey"/> and the concrete <see cref="CreatureTraits"/>/
/// <see cref="Drives"/> fields it maps to. Genetics code reads/writes stats only through
/// here — <see cref="CreatureTraits"/> and <see cref="Drives"/> stay untouched.
/// </summary>
public static class StatRegistry
{
    private sealed record Entry(
        Func<CreatureTraits, Drives, float> Get,
        Action<CreatureTraits, Drives, float> Set,
        float Min,
        float Max);

    private static readonly Dictionary<StatKey, Entry> Entries = new()
    {
        [StatKey.MaxSpeed] = new((t, _) => t.MaxSpeed, (t, _, v) => t.MaxSpeed = v, 0f, 10f),
        [StatKey.JumpHeight] = new((t, _) => t.JumpHeight, (t, _, v) => t.JumpHeight = v, 0f, 10f),
        [StatKey.Acceleration] = new((t, _) => t.Acceleration, (t, _, v) => t.Acceleration = v, 0f, 20f),
        [StatKey.TurnRate] = new((t, _) => t.TurnRate, (t, _, v) => t.TurnRate = v, 0f, 20f),
        [StatKey.Radius] = new((t, _) => t.Radius, (t, _, v) => t.Radius = v, 0.05f, 5f),
        [StatKey.SprintAcceleration] = new((t, _) => t.SprintAcceleration, (t, _, v) => t.SprintAcceleration = v, 0f, 20f),
        [StatKey.SprintSpeed] = new((t, _) => t.SprintSpeed, (t, _, v) => t.SprintSpeed = v, 0f, 10f),
        [StatKey.GravityScale] = new((t, _) => t.GravityScale, (t, _, v) => t.GravityScale = v, -2f, 2f),
        [StatKey.MaxFlyHeight] = new((t, _) => t.MaxFlyHeight, (t, _, v) => t.MaxFlyHeight = v, 0f, float.MaxValue),
        [StatKey.FatigueGainPerSec] = new((t, _) => t.FatigueGainPerSec, (t, _, v) => t.FatigueGainPerSec = v, 0f, 5f),
        [StatKey.FatigueRecoverPerSec] = new((t, _) => t.FatigueRecoverPerSec, (t, _, v) => t.FatigueRecoverPerSec = v, 0f, 5f),
        [StatKey.GrazeHungerThreshold] = new((t, _) => t.GrazeHungerThreshold, (t, _, v) => t.GrazeHungerThreshold = v, 0f, 1f),
        [StatKey.Curiosity] = new((_, d) => d.Curiosity, (_, d, v) => d.Curiosity = v, 0f, 1f),
        [StatKey.Fear] = new((_, d) => d.Fear, (_, d, v) => d.Fear = v, 0f, 1f),
        [StatKey.Sociability] = new((_, d) => d.Sociability, (_, d, v) => d.Sociability = v, 0f, 1f),
        [StatKey.PlayCuddle] = new((_, d) => d.PlayCuddle, (_, d, v) => d.PlayCuddle = v, 0f, 1f),
        [StatKey.Appetite] = new((_, d) => d.Appetite, (_, d, v) => d.Appetite = v, 0f, 1f),
        [StatKey.Aggression] = new((_, d) => d.Aggression, (_, d, v) => d.Aggression = v, 0f, 1f),
    };

    public static float Get(StatKey key, CreatureTraits traits, Drives drives) =>
        Entries[key].Get(traits, drives);

    public static void Set(StatKey key, CreatureTraits traits, Drives drives, float value)
    {
        var entry = Entries[key];
        var clamped = Math.Clamp(value, entry.Min, entry.Max);
        entry.Set(traits, drives, clamped);
    }

    public static (float Min, float Max) Range(StatKey key)
    {
        var entry = Entries[key];
        return (entry.Min, entry.Max);
    }
}
