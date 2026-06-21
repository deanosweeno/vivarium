using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class MapDataTests
{
    [Fact]
    public void Constructor_FillsAllCellsWithGrass()
    {
        var map = new MapData(4, 4, 1f);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            Assert.Equal(Terrain.Grass, map.GetCell(cx, cz).Terrain);
            Assert.Equal(Resource.None, map.GetCell(cx, cz).Resource);
            Assert.True(map.IsWalkable(cx, cz));
        }
    }

    [Fact]
    public void SetCell_RoundTripsThroughGetCell()
    {
        var map = new MapData(4, 4, 1f);
        map.SetCell(2, 1, new Cell { Terrain = Terrain.Water, Resource = Resource.Food });

        var cell = map.GetCell(2, 1);
        Assert.Equal(Terrain.Water, cell.Terrain);
        Assert.Equal(Resource.Food, cell.Resource);
    }

    [Fact]
    public void IsWalkable_GrassSandMarshAreWalkable()
    {
        var map = new MapData(3, 2, 1f);
        map.SetCell(0, 0, new Cell { Terrain = Terrain.Water });
        map.SetCell(1, 0, new Cell { Terrain = Terrain.Rock });
        map.SetCell(0, 1, new Cell { Terrain = Terrain.Sand });
        map.SetCell(1, 1, new Cell { Terrain = Terrain.Marsh });
        map.SetCell(2, 0, new Cell { Terrain = Terrain.Grass });

        Assert.False(map.IsWalkable(0, 0)); // Water
        Assert.False(map.IsWalkable(1, 0)); // Rock
        Assert.True(map.IsWalkable(0, 1));  // Sand
        Assert.True(map.IsWalkable(1, 1));  // Marsh
        Assert.True(map.IsWalkable(2, 0));  // Grass
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    public void GetCell_OutOfBounds_Throws(int cx, int cz)
    {
        var map = new MapData(4, 4, 1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(cx, cz));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(4, 0)]
    public void SetCell_OutOfBounds_Throws(int cx, int cz)
    {
        var map = new MapData(4, 4, 1f);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => map.SetCell(cx, cz, new Cell()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(7, 7)]
    public void WorldToCell_IsInverseOfCellToWorldCenter(int cx, int cz)
    {
        var map = new MapData(8, 8, 2.5f);
        Vector3 center = map.CellToWorldCenter(cx, cz);
        var (rcx, rcz) = map.WorldToCell(center);

        Assert.Equal(cx, rcx);
        Assert.Equal(cz, rcz);
    }

    [Fact]
    public void CellToWorldCenter_GridIsCenteredOnOrigin()
    {
        // Even-sized grid: the origin sits on the boundary between the two
        // middle cells, so the four central cell centers straddle it.
        var map = new MapData(4, 4, 1f);
        Vector3 c = map.CellToWorldCenter(2, 2);
        Assert.Equal(0.5f, c.X, 3);
        Assert.Equal(0.5f, c.Z, 3);
        Assert.Equal(0f, c.Y, 3);
    }

    [Fact]
    public void InBounds_MatchesGridExtents()
    {
        var map = new MapData(4, 4, 1f);
        Assert.True(map.InBounds(0, 0));
        Assert.True(map.InBounds(3, 3));
        Assert.False(map.InBounds(4, 0));
        Assert.False(map.InBounds(-1, 2));
    }

    [Fact]
    public void HeightAt_CellCenter_ReturnsThatCellsHeight()
    {
        var map = new MapData(4, 4, 1f);
        map.SetCell(1, 2, new Cell { Terrain = Terrain.Grass, Height = 5f });

        Vector3 center = map.CellToWorldCenter(1, 2);
        Assert.Equal(5f, map.HeightAt(center), 3);
    }

    [Fact]
    public void HeightAt_BetweenTwoCells_InterpolatesLinearly()
    {
        var map = new MapData(4, 4, 1f);
        // Two horizontally adjacent cells: heights 0 and 2.
        map.SetCell(1, 1, new Cell { Terrain = Terrain.Grass, Height = 0f });
        map.SetCell(2, 1, new Cell { Terrain = Terrain.Grass, Height = 2f });

        Vector3 a = map.CellToWorldCenter(1, 1);
        Vector3 b = map.CellToWorldCenter(2, 1);
        var mid = new Vector3((a.X + b.X) / 2f, 0f, a.Z);

        Assert.Equal(1f, map.HeightAt(mid), 3); // halfway → ~1
    }

    [Fact]
    public void HeightAt_OutOfBounds_ClampsToEdgeHeight()
    {
        var map = new MapData(2, 2, 1f);
        for (int cz = 0; cz < 2; cz++)
        for (int cx = 0; cx < 2; cx++)
            map.SetCell(cx, cz, new Cell { Terrain = Terrain.Grass, Height = 3f });

        // Far outside the grid → clamps to the (uniform) edge height, never throws.
        Assert.Equal(3f, map.HeightAt(new Vector3(1000f, 0f, 1000f)), 3);
    }
}
