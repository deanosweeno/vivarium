namespace Vivarium.Core;

/// <summary>
/// One value per tunable field on <see cref="CreatureTraits"/> and <see cref="Drives"/>.
/// Resolved to a concrete getter/setter via <see cref="StatRegistry"/>.
/// </summary>
public enum StatKey
{
    MaxSpeed,
    JumpHeight,
    Acceleration,
    TurnRate,
    Radius,
    GravityScale,
    MaxFlyHeight,
    FatigueGainPerSec,
    FatigueRecoverPerSec,
    GrazeHungerThreshold,
    Curiosity,
    Fear,
    Sociability,
    PlayCuddle,
    Appetite,
    Aggression,
}
