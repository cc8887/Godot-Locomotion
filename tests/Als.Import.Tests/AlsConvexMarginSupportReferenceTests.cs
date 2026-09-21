using System.Text.Json;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsConvexMarginSupportReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    [Fact]
    public void AllCookedFootVerticesAndDirectionsMatchNativeMarginCore()
    {
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var hulls=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition)
            .ToDictionary(h=>definition.Bodies[h.Body].Bone,h=>h.Topology);
        using var doc=JsonDocument.Parse(Read("v4_physics_convex_margin_support"));
        var errors=new List<string>();var count=0;var unchanged=0;double maxPoint=0,maxDelta=0;
        foreach(var h in doc.RootElement.GetProperty("hulls").EnumerateArray())
        {
            var hull=hulls[h.GetProperty("bone").GetString()!];
            var vertices=h.GetProperty("vertices");
            Assert.Equal(hull.VertexCount,vertices.GetArrayLength());
            for(var i=0;i<hull.VertexCount;i++)Assert.Equal(new GodotAls.Core.Locomotion.AlsDoubleVector(hull.VertexAt(i)),
                new GodotAls.Core.Locomotion.AlsDoubleVector(vertices[i][0].GetDouble(),vertices[i][1].GetDouble(),vertices[i][2].GetDouble()));
            foreach(var row in h.GetProperty("cases").EnumerateArray())
            {
                var expectedVertex=row.GetProperty("vertex").GetInt32();var vertex=expectedVertex;double delta=17;
                var point=row.GetProperty("directVertex").GetBoolean()
                    ?AlsConvexMarginSupport.AdjustedVertex(hull,vertex,D(row,"margin"),V(row,"scale"),row.GetProperty("scaled").GetBoolean(),ref delta)
                    :AlsConvexMarginSupport.Support(hull,V(row,"direction"),D(row,"margin"),V(row,"scale"),row.GetProperty("scaled").GetBoolean(),ref delta,out vertex);
                var pe=System.Math.Sqrt((point-V(row,"point")).LengthSquared);var de=System.Math.Abs(delta-D(row,"delta"));
                maxPoint=System.Math.Max(maxPoint,pe);maxDelta=System.Math.Max(maxDelta,de);
                if(delta==17)unchanged++;
                if(vertex!=expectedVertex||pe!=0||de!=0)
                    errors.Add($"case={count} scaled={row.GetProperty("scaled")} margin={D(row,"margin")} vertex={vertex}/{expectedVertex} point={pe:R} delta={de:R}");
                count++;
            }
        }
        output.WriteLine($"MARGIN_SUPPORT cases={count} unchanged_delta={unchanged} max_point_cm={maxPoint:R} max_delta_cm={maxDelta:R}");
        Assert.Equal(8960,count);Assert.True(unchanged>0);Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors.Take(30)));
    }
}
