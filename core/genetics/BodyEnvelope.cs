namespace Vivarium.Core;

/// <summary>
/// Whole-body metadata that rides alongside a Base gene — the fields of a <see cref="BodyPlan"/>
/// that are not per-part (overall scale + pastel palette + id/name). Kept off <see cref="Gene"/>
/// on purpose: a specialty gene (e.g. "wolf legs") owns no body-wide scale or palette, so this
/// would be dead weight on the unified gene type. The Expressor reads it to assemble the final
/// <see cref="BodyPlan"/>; derived from the source species' <see cref="BodyPlan"/> via
/// <see cref="From"/>.
/// </summary>
public sealed record BodyEnvelope(string Id, string Name, float BaseScale, string PrimaryHex, string SecondaryHex)
{
    public static BodyEnvelope From(BodyPlan plan) =>
        new(plan.Id, plan.Name, plan.BaseScale, plan.PrimaryHex, plan.SecondaryHex);
}
