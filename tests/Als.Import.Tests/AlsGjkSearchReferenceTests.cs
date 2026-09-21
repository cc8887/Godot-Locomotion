using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsGjkSearchReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    [Fact]
    public void ContinuousFootBoxSearchAndPersistedSimplexMatchNative()
    {
        using var doc=JsonDocument.Parse(Read("v4_physics_gjk_search_reference"));
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var topologies=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition);
        var sequences=doc.RootElement.GetProperty("sequences");Assert.Equal(48,sequences.GetArrayLength());
        var total=0;var epa=0;var restored=0;var separated=0;var errors=new List<string>();
        double maxPoint=0,maxWeight=0,maxNormal=0,maxDistance=0;
        foreach(var sequence in sequences.EnumerateArray())
        {
            var hull=topologies.Single(t=>definition.Bodies[t.Body].Bone==sequence.GetProperty("bone").GetString()).Topology;
            var shapeA=new AlsGjkConvexShape(hull,AlsDoubleVector.One);
            var shapeB=new AlsGjkBoxShape(V(sequence,"halfB"),sequence.GetProperty("marginB").GetSingle());
            var cache=new AlsGjkCache();var warm=sequence.GetProperty("warmStart").GetBoolean();
            foreach(var row in sequence.GetProperty("frames").EnumerateArray())
            {
                var result=AlsGjkSearch.Run(shapeA,shapeB,Pose(row.GetProperty("bToA")),cache,D(doc.RootElement,"gjkEpsilon"),warm);total++;
                var id=$"bone={sequence.GetProperty("bone")} axis={sequence.GetProperty("axis")} sign={sequence.GetProperty("sign")} margin={shapeB.Margin} warm={warm} frame={row.GetProperty("frame")}";
                if(result.HitIterationLimit)errors.Add(id+" iteration limit");
                if(result.RestoredCount!=row.GetProperty("restoredCount").GetInt32())errors.Add(id+" restored count");
                if(result.RestoredCount>0)restored++;
                if(cache.Count!=row.GetProperty("count").GetInt32()){errors.Add(id+" cache count");continue;}
                for(var i=0;i<cache.Count;i++)
                {
                    var expected=row.GetProperty("cache")[i];var weight=System.Math.Abs(cache.Weights[i]-D(expected,"weight"));maxWeight=System.Math.Max(maxWeight,weight);
                    var p=System.Math.Max(Error(cache.WitnessA[i],V(expected,"a")),Error(cache.WitnessB[i],V(expected,"b")));maxPoint=System.Math.Max(maxPoint,p);
                    if(weight>1e-10||p>1e-8)errors.Add(id+$" cache {i} weight={weight:R} point={p:R}");
                }
                if(result.NeedsEpa)
                {
                    epa++;if(D(row,"penetration") < -1e-5)errors.Add(id+" incorrectly requested EPA for clear separation");
                    continue; // Native final penetration/contact depends on EPA; not claimed here.
                }
                separated++;
                var distance=System.Math.Abs(shapeA.Margin+shapeB.Margin-result.CoreDistance-D(row,"penetration"));maxDistance=System.Math.Max(maxDistance,distance);
                var point=System.Math.Max(Error(result.PointA,V(row,"pointA")),Error(result.PointB,V(row,"pointB")));maxPoint=System.Math.Max(maxPoint,point);
                var normal=System.Math.Max(Error(result.NormalA,V(row,"normalA")),Error(result.NormalB,V(row,"normalB")));maxNormal=System.Math.Max(maxNormal,normal);
                if(distance>1e-8||point>1e-8||normal>1e-8||result.VertexA!=row.GetProperty("vertexA").GetInt32()||result.VertexB!=row.GetProperty("vertexB").GetInt32())
                    errors.Add(id+$" separated distance={distance:R} point={point:R} normal={normal:R} vertex={result.VertexA}/{result.VertexB}");
                Assert.Equal(D(row,"maxSupportDelta"),result.MaxSupportDelta);
            }
        }
        output.WriteLine($"NATIVE_GJK_SEARCH frames={total} no_epa={separated} needs_epa={epa} restored={restored} max_point_cm={maxPoint:R} max_weight={maxWeight:R} max_normal={maxNormal:R} max_distance_cm={maxDistance:R}");
        Assert.Equal(528,total);Assert.True(separated>100&&epa>50&&restored>50);
        Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors.Take(25)));
    }
    private static double Error(AlsDoubleVector a,AlsDoubleVector b)=>System.Math.Sqrt((a-b).LengthSquared);
}
