namespace Vivarium.Core;

/// <summary>The shape applied by a <see cref="ResponseCurve"/>.</summary>
public enum CurveType
{
    /// <summary>y = clamp01(Slope·x + Offset). Identity by default.</summary>
    Linear,
    /// <summary>y = clamp01(Slope·(1−x) + Offset). "Less input = more urge" (low energy → rest).</summary>
    Inverse,
    /// <summary>y = clamp01(x)^Exponent. Slow ramp then steep (or vice-versa). Ignorable-until-it-bites.</summary>
    Power,
    /// <summary>Logistic S-curve around Midpoint with steepness Steepness. A soft threshold.</summary>
    Logistic,
}

/// <summary>
/// Maps one normalized input in [0,1] through a tunable response curve to a score in
/// [0,1]. The data half of "data-defined scoring": a curve is a handful of numbers, so
/// behavior is retuned by editing config, never code. See <see cref="Consideration"/>.
/// </summary>
public sealed class ResponseCurve
{
    public CurveType Type { get; init; } = CurveType.Linear;

    /// <summary>Linear/Inverse slope.</summary>
    public float Slope { get; init; } = 1f;

    /// <summary>Linear/Inverse offset.</summary>
    public float Offset { get; init; } = 0f;

    /// <summary>Power exponent (Power only). &gt;1 = ignore small inputs; &lt;1 = react early.</summary>
    public float Exponent { get; init; } = 1f;

    /// <summary>Logistic midpoint — the input at which the curve crosses 0.5.</summary>
    public float Midpoint { get; init; } = 0.5f;

    /// <summary>Logistic steepness — larger = sharper threshold.</summary>
    public float Steepness { get; init; } = 8f;

    /// <summary>Evaluate the curve at <paramref name="x"/>, returning a value in [0,1].</summary>
    public float Evaluate(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        float y = Type switch
        {
            CurveType.Linear => Slope * x + Offset,
            CurveType.Inverse => Slope * (1f - x) + Offset,
            CurveType.Power => (float)Math.Pow(x, Exponent),
            CurveType.Logistic => 1f / (1f + (float)Math.Exp(-Steepness * (x - Midpoint))),
            _ => x,
        };
        return Math.Clamp(y, 0f, 1f);
    }

    /// <summary>Convenience: identity ramp (y = x).</summary>
    public static ResponseCurve Identity => new() { Type = CurveType.Linear };
}
