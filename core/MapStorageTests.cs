using Xunit;

namespace Vivarium.Core.Tests;

public class MapStorageTests
{
    private static MapData SampleMap()
    {
        var config = new MapGenConfig
        {
            Width = 24,
            Depth = 20,
            CellSize = 1.5f,
            LakeCount = 1,
            LakeRadius = 4,
            RockClusters = 6,
            RockClusterSize = 5,
        };
        return MapGenerator.Generate(config, 99);
    }

    private static void AssertMapsEqual(MapData a, MapData b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Depth, b.Depth);
        Assert.Equal(a.CellSize, b.CellSize, 4);
        Assert.Equal(a.SeaLevel, b.SeaLevel, 4);
        for (int cz = 0; cz < a.Depth; cz++)
        for (int cx = 0; cx < a.Width; cx++)
        {
            Assert.Equal(a.GetCell(cx, cz).Terrain, b.GetCell(cx, cz).Terrain);
            Assert.Equal(a.GetCell(cx, cz).Resource, b.GetCell(cx, cz).Resource);
            Assert.Equal(a.GetCell(cx, cz).Biome, b.GetCell(cx, cz).Biome);
            Assert.Equal(a.GetCell(cx, cz).Height, b.GetCell(cx, cz).Height, 4);
        }
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        var map = SampleMap();
        var restored = MapStorage.Deserialize(MapStorage.Serialize(map));
        AssertMapsEqual(map, restored);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughFile()
    {
        var map = SampleMap();
        string path = Path.Combine(Path.GetTempPath(),
            $"vivarium_map_{Guid.NewGuid():N}.json");
        try
        {
            MapStorage.Save(map, path);
            var restored = MapStorage.Load(path);
            AssertMapsEqual(map, restored);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Deserialize_WrongCellCount_Throws()
    {
        const string json = "{\"Width\":2,\"Depth\":2,\"CellSize\":1.0,\"Cells\":[]}";
        Assert.Throws<InvalidDataException>(() => MapStorage.Deserialize(json));
    }

    [Fact]
    public void Deserialize_PreBiomeMap_LoadsCellsAsPlains()
    {
        // A map JSON written before the Biome field existed: cells have no "Biome".
        const string json =
            "{\"Width\":1,\"Depth\":1,\"CellSize\":1.0," +
            "\"Cells\":[{\"Terrain\":1,\"Resource\":0}]}";
        var map = MapStorage.Deserialize(json);

        Assert.Equal(Terrain.Water, map.GetCell(0, 0).Terrain);
        Assert.Equal(Biome.Plains, map.GetCell(0, 0).Biome); // defaulted, no crash
    }

    [Fact]
    public void Deserialize_PreHeightMap_LoadsCellsAsFlat()
    {
        // A map JSON written before the Height field existed: cells have no "Height".
        const string json =
            "{\"Width\":1,\"Depth\":1,\"CellSize\":1.0," +
            "\"Cells\":[{\"Terrain\":0,\"Resource\":0,\"Biome\":2}]}";
        var map = MapStorage.Deserialize(json);

        Assert.Equal(Biome.Forest, map.GetCell(0, 0).Biome);
        Assert.Equal(0f, map.GetCell(0, 0).Height); // defaulted to flat, no crash
    }

    [Fact]
    public void SerializeDeserialize_PreservesHeight()
    {
        var map = new MapData(2, 1, 1.0f);
        map.SetCell(0, 0, new Cell { Terrain = Terrain.Grass, Height = 3.5f });
        map.SetCell(1, 0, new Cell { Terrain = Terrain.Water, Height = -1.25f });

        var restored = MapStorage.Deserialize(MapStorage.Serialize(map));

        Assert.Equal(3.5f, restored.GetCell(0, 0).Height, 4);
        Assert.Equal(-1.25f, restored.GetCell(1, 0).Height, 4);
    }
}
