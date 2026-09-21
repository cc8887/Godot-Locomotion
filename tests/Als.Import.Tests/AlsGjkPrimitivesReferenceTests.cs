using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsGjkPrimitivesReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
    private static AlsDoubleVector V(JsonElement v) => new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    [Fact]
    public void IndexedSimplexActivePointsWeightsAndWitnessesMatchNative()
    {
        using var doc = JsonDocument.Parse(Read("v4_physics_gjk_primitives_reference"));
        Assert.False(doc.RootElement.GetProperty("useGjk2").GetBoolean());
        Assert.Equal((double)1e-6f, doc.RootElement.GetProperty("gjkEpsilon").GetDouble());
        Assert.Equal((double)1e-6f, doc.RootElement.GetProperty("epaEpsilon").GetDouble());
        var cases = doc.RootElement.GetProperty("simplexCases"); Assert.Equal(576, cases.GetArrayLength());
        Span<AlsDoubleVector> p = stackalloc AlsDoubleVector[4];
        Span<AlsDoubleVector> a = stackalloc AlsDoubleVector[4];
        Span<AlsDoubleVector> b = stackalloc AlsDoubleVector[4];
        Span<double> weights = stackalloc double[4]; var maxPoint = 0d; var maxWeight = 0d;
        foreach (var row in cases.EnumerateArray())
        {
            var count = row.GetProperty("inputCount").GetInt32();
            for(var i=0;i<count;i++){p[i]=V(row.GetProperty("points")[i]);a[i]=V(row.GetProperty("witnessA")[i]);b[i]=V(row.GetProperty("witnessB")[i]);}
            var closest = AlsGjkSimplex.Closest(p, ref count, weights, a, b);
            Assert.Equal(row.GetProperty("count").GetInt32(),count);
            var error = System.Math.Sqrt((closest - V(row.GetProperty("closest"))).LengthSquared);
            maxPoint = System.Math.Max(maxPoint,error);
            Assert.True(error < 1e-7,$"count={row.GetProperty("inputCount")} sample={row.GetProperty("sample")} closest error={error:R}");
            for(var i=0;i<count;i++)
            {
                var expected=row.GetProperty("active")[i];
                Assert.Equal(V(expected.GetProperty("point")),p[i]);Assert.Equal(V(expected.GetProperty("a")),a[i]);Assert.Equal(V(expected.GetProperty("b")),b[i]);
                var weightError=System.Math.Abs(weights[i]-expected.GetProperty("weight").GetDouble());
                maxWeight=System.Math.Max(maxWeight,weightError);Assert.True(weightError<1e-12);
            }
        }
        output.WriteLine($"NATIVE_GJK_SIMPLEX cases=576 max_closest_cm={maxPoint:R} max_weight={maxWeight:R}");
    }
    [Fact]
    public void RealCookedFootSupportPointsAndVertexIdentityMatchNative()
    {
        using var doc=JsonDocument.Parse(Read("v4_physics_gjk_primitives_reference"));
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var topologies=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition);var total=0;
        foreach(var row in doc.RootElement.GetProperty("hulls").EnumerateArray())
        {
            var binding=topologies.Single(t=>definition.Bodies[t.Body].Bone==row.GetProperty("bone").GetString());var hull=binding.Topology;
            var vertices=row.GetProperty("vertices");Assert.Equal(hull.VertexCount,vertices.GetArrayLength());
            for(var i=0;i<hull.VertexCount;i++)Assert.Equal(new AlsDoubleVector(hull.VertexAt(i)),V(vertices[i]));
            foreach(var sample in row.GetProperty("supports").EnumerateArray())
            {
                var point=AlsConvexSupport.ZeroMargin(hull,V(sample.GetProperty("direction")),V(sample.GetProperty("scale")),out var vertex);
                Assert.Equal(sample.GetProperty("vertex").GetInt32(),vertex);Assert.Equal(V(sample.GetProperty("point")),point);
                Assert.Equal(17,sample.GetProperty("supportDelta").GetDouble());total++;
            }
        }
        Assert.Equal(1024,total);output.WriteLine($"NATIVE_CONVEX_SUPPORT cases={total} point_and_identity_exact=true");
    }
}
