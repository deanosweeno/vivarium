using Xunit;

namespace Vivarium.Core.Tests;

public class CraftAndSplicerTests
{
    private const string CreaturesJson = """
        [
          {
            "Id": "sheep", "Name": "Sheep", "BaseScale": 1.0, "PrimaryHex": "#AABBCC", "SecondaryHex": "#112233",
            "Parts": [
              { "Slot": "Core", "Shape": "Sphere" },
              { "Slot": "Head", "Shape": "Sphere" },
              { "Slot": "Locomotion", "Shape": "Capsule" }
            ],
            "Traits": { "MaxSpeed": 0.6 },
            "Drives": { "Sociability": 0.5 }
          }
        ]
        """;

    private const string GenesJson = """
        [
          { "Id": "sheep.mat.core", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "sheep.mat.head", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          {
            "Id": "spec.sheep.wool", "Kind": "Specialty", "Rarity": "Legendary", "Visible": true, "SourceSpecies": "sheep",
            "Parts": [{ "Slot": "Surface", "Shape": "Box" }],
            "Pins": [{ "Key": "Sociability", "Value": 1.0 }]
          }
        ]
        """;

    [Fact]
    public void CraftBase_ThrowsBeforeFullSetCollected()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Parse(GenesJson);
        var pool = new GenePool();
        pool.Add(genes.FindById("sheep.mat.core")!);

        Assert.Throws<InvalidOperationException>(() => Craft.CraftBase("sheep", pool, creatures, genes));
    }

    [Fact]
    public void CraftBase_SucceedsOnceFullSetCollected_ProducesBaseGene()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Parse(GenesJson);
        var pool = new GenePool();
        pool.Add(genes.FindById("sheep.mat.core")!);
        pool.Add(genes.FindById("sheep.mat.head")!);

        var baseGene = Craft.CraftBase("sheep", pool, creatures, genes);

        Assert.Equal(GeneKind.Base, baseGene.Kind);
        Assert.Equal("sheep", baseGene.SourceSpecies);
    }

    [Fact]
    public void CraftBase_UnknownSpecies_Throws()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Empty;
        var pool = new GenePool();

        Assert.Throws<ArgumentException>(() => Craft.CraftBase("wolf", pool, creatures, genes));
    }

    [Fact]
    public void Splice_RejectsSpecialtyCountOverBudget()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var def = creatures.GetDef("sheep")!;
        var baseGene = BaseGene.From(def);
        var wool = GeneCatalog.Parse(GenesJson).FindById("spec.sheep.wool")!;
        var cfg = GeneticsConfig.Default with { DefaultSpliceBudget = 0 };

        Assert.Throws<ArgumentException>(() => Splicer.Splice(baseGene, [wool], cfg));
    }

    [Fact]
    public void Splice_RejectsNonBaseAsBase()
    {
        var wool = GeneCatalog.Parse(GenesJson).FindById("spec.sheep.wool")!;
        Assert.Throws<ArgumentException>(() => Splicer.Splice(wool, [], GeneticsConfig.Default));
    }

    [Fact]
    public void FullLoop_HarvestCraftSplice_ExpressesHybridPhenotype()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Parse(GenesJson);
        var cfg = GeneticsConfig.Default;
        var pool = new GenePool();

        // Harvest until the full common set is collected (deterministic for the seed).
        var rng = new Random(3);
        while (!pool.HasFullSet("sheep", genes))
        {
            foreach (var drop in HarvestTable.Roll("sheep", genes, cfg, rng))
            {
                pool.Add(drop);
            }
        }

        var baseGene = Craft.CraftBase("sheep", pool, creatures, genes);
        var wool = genes.FindById("spec.sheep.wool")!;
        var genome = Splicer.Splice(baseGene, [wool], cfg, BodyEnvelope.From(creatures.GetDef("sheep")!.Body));

        var phenotype = Expressor.Express(genome, new Random(1));

        Assert.Equal(1f, phenotype.Drives.Sociability, precision: 4);
        Assert.Contains(phenotype.Body.Parts, p => p.Slot == PartSlot.Surface);
    }

    [Fact]
    public void FullLoop_IsDeterministic_ForSameSeed()
    {
        Gene RunLoop(int seed)
        {
            var creatures = CreatureCatalog.Parse(CreaturesJson);
            var genes = GeneCatalog.Parse(GenesJson);
            var cfg = GeneticsConfig.Default;
            var pool = new GenePool();
            var rng = new Random(seed);
            while (!pool.HasFullSet("sheep", genes))
            {
                foreach (var drop in HarvestTable.Roll("sheep", genes, cfg, rng))
                {
                    pool.Add(drop);
                }
            }
            return Craft.CraftBase("sheep", pool, creatures, genes);
        }

        var a = RunLoop(11);
        var b = RunLoop(11);
        Assert.Equal(a.Id, b.Id);
    }
}
