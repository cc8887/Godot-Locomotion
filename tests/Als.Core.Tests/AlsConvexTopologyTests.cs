using System.Numerics;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsConvexTopologyTests
{
    private static (Vector3[] Vertices, AlsConvexPlane[] Planes, int[] Indices) Box()
    {
        var vertices = Enumerable.Range(0, 8).Select(i => new Vector3((i & 1) == 0 ? -1 : 1,
            (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)).ToArray();
        var normals = new[] { -Vector3.UnitX, -Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        return (vertices, normals.Select((n, i) => new AlsConvexPlane(n, n, i * 4, 4)).ToArray(),
            [0,4,6,2, 0,1,5,4, 0,2,3,1, 1,3,7,5, 2,6,7,3, 4,5,7,6]);
    }
    [Fact]
    public void NativeFaceOrderSurvivesAndCallerCannotMutateStoredGeometry()
    {
        var (v, p, i) = Box(); var hull = new AlsConvexTopology(v, p, i, .1f);
        v[0] = new(99); p[0] = default; i[0] = 7;
        Assert.Equal(new Vector3(-1), hull.VertexAt(0)); Assert.Equal(-Vector3.UnitX, hull.PlaneAt(0).Normal);
        Assert.Equal(new[] { 0, 4, 6, 2 }, hull.FaceVertices(0).ToArray()); Assert.Equal(.1f, hull.Margin);
        Assert.True(hull.HasClosedOrientedEdges);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BrokenTopologyAndNonfiniteGeometryAreRejected(int fault)
    {
        var (v, p, i) = Box();
        switch (fault)
        {
            case 0: i[1] = i[0]; break;
            case 2: Array.Reverse(i, 0, 4); break;
            case 3: v[0] = new(float.NaN); break;
            case 4: p[0] = p[0] with { Normal = Vector3.UnitX }; break;
        }
        Assert.Throws<ArgumentException>(() => new AlsConvexTopology(v, p, i, 0));
    }
    [Fact]
    public void UnpairedNativeEdgesAreReportedRatherThanReconstructed()
    {
        var (v, p, i) = Box();
        var hull = new AlsConvexTopology(v, p[..5], i[..20], 0);
        Assert.False(hull.HasClosedOrientedEdges);
        Assert.Equal(5, hull.PlaneCount);
    }
    [Fact]
    public void NativeMergedFaceMayContainRecessedVerticesWithoutProjection()
    {
        var (v, p, i) = Box(); v[0] += new Vector3(.05f);
        var hull = new AlsConvexTopology(v, p, i, 0);
        Assert.Equal(v[0], hull.VertexAt(0));
        v[0] = new(.1f);
        Assert.Throws<ArgumentException>(() => new AlsConvexTopology(v, p, i, 0));
    }
}
