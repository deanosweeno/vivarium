namespace Vivarium.Core;

/// <summary>A gene's claim on a single stat: which key, what value to set it to.</summary>
public sealed class StatPin
{
    public required StatKey Key { get; init; }
    public required float Value { get; init; }
}
