namespace Vivarium.Core;

/// <summary>
/// Shared personality flavor-match for the pet verbs. Maps a creature's <see cref="Drives.PlayCuddle"/>
/// temperament (0 = cuddle/calm, 1 = energetic play) to how well a given interaction fits it, then
/// scales the bond gain by that match — floored so a mismatched pick still helps. One place owns the
/// cozy "bonus, never punishment" invariant: the returned multiplier is always in
/// <c>[FlavorMismatchFloor, 1]</c>, so applied bond is never zero and never negative.
/// </summary>
internal static class FlavorMatch
{
    /// <summary>Bond multiplier for an interaction whose own flavor strength is <paramref name="match"/>
    /// in [0,1] (1 = perfect fit). Lerps from the config floor up to 1.</summary>
    public static float Multiplier(float match, BehaviorConfig cfg)
    {
        match = Math.Clamp(match, 0f, 1f);
        return cfg.FlavorMismatchFloor + (1f - cfg.FlavorMismatchFloor) * match;
    }
}
