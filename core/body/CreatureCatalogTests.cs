using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the creature body-plan catalog: JSON parsing of slots/shapes/roles/vectors,
/// forgiving handling of bad/missing data, and determinism (same JSON ⇒ identical plan).
/// All headless — no Godot. Mirrors <see cref="FoodTests"/>'s catalog tests.
/// </summary>
public class CreatureCatalogTests
{
    private const string Json = """
        [
          {
            "Id": "sprout",
            "Name": "Sprout",
            "BaseScale": 1.0,
            "PrimaryHex": "#A8D8B0",
            "SecondaryHex": "#7FB89A",
            "Parts": [
              { "Slot": "Core", "Shape": "Capsule", "Size": [0.5, 0.7, 0.5], "Socket": [0, 0.45, 0], "Tint": "#A8D8B0", "Role": "Body" },
              { "Slot": "Head", "Shape": "Sphere", "Size": [0.42, 0.42, 0.42], "Socket": [0, 0.95, 0.06], "Tint": "#B6E2BC", "Role": "Head" },
              { "Slot": "Locomotion", "Shape": "Capsule", "Size": [0.13, 0.34, 0.13], "Socket": [-0.18, 0.16, 0.02], "Role": "Limb", "Phase": 0.0, "Freq": 7.0 },
              { "Slot": "Locomotion", "Shape": "Capsule", "Size": [0.13, 0.34, 0.13], "Socket": [0.18, 0.16, 0.02], "Role": "Limb", "Phase": 3.1416, "Freq": 7.0 }
            ]
          }
        ]
        """;

    [Fact]
    public void Parses_PlanFields()
    {
        var plan = CreatureCatalog.Parse(Json).Get("sprout");
        Assert.NotNull(plan);
        Assert.Equal("Sprout", plan!.Name);
        Assert.Equal(1.0f, plan.BaseScale, 5);
        Assert.Equal("#A8D8B0", plan.PrimaryHex);
        Assert.Equal(4, plan.Parts.Count);
    }

    [Fact]
    public void Parses_PartEnumsAndVectors()
    {
        var plan = CreatureCatalog.Parse(Json).Get("sprout")!;

        var core = plan.Parts[0];
        Assert.Equal(PartSlot.Core, core.Slot);
        Assert.Equal(ShapePrimitive.Capsule, core.Shape);
        Assert.Equal(AnimRole.Body, core.Role);
        Assert.Equal(new Vector3(0.5f, 0.7f, 0.5f), core.Size);
        Assert.Equal(new Vector3(0f, 0.45f, 0f), core.Socket);

        // Paired legs share a slot but carry opposite phases.
        var legL = plan.Parts[2];
        var legR = plan.Parts[3];
        Assert.Equal(PartSlot.Locomotion, legL.Slot);
        Assert.Equal(AnimRole.Limb, legL.Role);
        Assert.Equal(0f, legL.Phase, 4);
        Assert.Equal(3.1416f, legR.Phase, 4);
        Assert.Equal(7.0f, legR.Freq, 4);
    }

    [Fact]
    public void UnknownId_ReturnsNull()
    {
        Assert.Null(CreatureCatalog.Parse(Json).Get("nope"));
        Assert.Null(CreatureCatalog.Empty.Get("sprout"));
    }

    [Fact]
    public void EntryWithoutId_IsSkipped()
    {
        var cat = CreatureCatalog.Parse("""[ { "Name": "ghost" }, { "Id": "real" } ]""");
        Assert.Single(cat.Ids);
        Assert.NotNull(cat.Get("real"));
    }

    [Fact]
    public void MissingFields_FallBackToDefaults()
    {
        var plan = CreatureCatalog.Parse("""[ { "Id": "bare", "Parts": [ { "Slot": "Core" } ] } ]""").Get("bare")!;
        Assert.Equal("bare", plan.Name);          // Name defaults to Id
        Assert.Equal(1f, plan.BaseScale, 5);
        var part = Assert.Single(plan.Parts);
        Assert.Equal(ShapePrimitive.Sphere, part.Shape);
        Assert.Equal(Vector3.One, part.Size);     // absent Size → fallback
        Assert.Equal(Vector3.Zero, part.Socket);
        Assert.Equal(AnimRole.Static, part.Role);
    }

    [Fact]
    public void Parse_IsDeterministic()
    {
        var a = CreatureCatalog.Parse(Json).Get("sprout")!;
        var b = CreatureCatalog.Parse(Json).Get("sprout")!;
        Assert.Equal(a.Parts.Count, b.Parts.Count);
        for (int i = 0; i < a.Parts.Count; i++)
        {
            Assert.Equal(a.Parts[i].Slot, b.Parts[i].Slot);
            Assert.Equal(a.Parts[i].Socket, b.Parts[i].Socket);
            Assert.Equal(a.Parts[i].Phase, b.Parts[i].Phase, 5);
        }
    }
}
