using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsEpaReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    [Fact]
    public void OverlappingCookedFeetMatchFullNativeGjkEpaContact()
    {
        using var doc=JsonDocument.Parse(Read("v4_physics_gjk_search_reference"));
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var topologies=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition);
        var work=new AlsEpaWorkspace();var contactWork=new AlsEpaWorkspace();var total=0;var bad=0;var errors=new List<string>();var maximumQueue=0;
        double maxPoint=0,maxNormal=0,maxDepth=0;
        foreach(var sequence in doc.RootElement.GetProperty("sequences").EnumerateArray())
        {
            var hull=topologies.Single(t=>definition.Bodies[t.Body].Bone==sequence.GetProperty("bone").GetString()).Topology;
            var a=new AlsGjkConvexShape(hull,AlsDoubleVector.One);var b=new AlsGjkBoxShape(V(sequence,"halfB"),sequence.GetProperty("marginB").GetSingle());
            var cache=new AlsGjkCache();var contactCache=new AlsGjkCache();
            foreach(var row in sequence.GetProperty("frames").EnumerateArray())
            {
                var pose=Pose(row.GetProperty("bToA"));
                var gjk=AlsGjkSearch.Run(a,b,pose,cache,D(doc.RootElement,"gjkEpsilon"),sequence.GetProperty("warmStart").GetBoolean());
                var contact=AlsGjkPenetration.Run(a,b,pose,contactCache,contactWork,D(doc.RootElement,"gjkEpsilon"),D(doc.RootElement,"epaEpsilon"),sequence.GetProperty("warmStart").GetBoolean());
                Assert.InRange(System.Math.Abs(contact.Penetration-D(row,"penetration")),0,1e-8);
                Assert.InRange(Error(contact.PointA,V(row,"pointA")),0,1e-8);Assert.InRange(Error(contact.PointB,V(row,"pointB")),0,1e-8);
                Assert.InRange(Error(contact.NormalA,V(row,"normalA")),0,1e-8);Assert.InRange(Error(contact.NormalB,V(row,"normalB")),0,1e-8);
                Assert.Equal(row.GetProperty("vertexA").GetInt32(),contact.VertexA);Assert.Equal(row.GetProperty("vertexB").GetInt32(),contact.VertexB);
                Assert.Equal(D(row,"maxSupportDelta"),contact.MaxSupportDelta);
                Assert.Equal(cache.WitnessA.ToArray(),contactCache.WitnessA.ToArray());Assert.Equal(cache.WitnessB.ToArray(),contactCache.WitnessB.ToArray());
                Assert.Equal(cache.Weights.ToArray(),contactCache.Weights.ToArray());
                if(!gjk.NeedsEpa)continue;
                var result=AlsEpa.Run(a,b,pose,cache,work,D(doc.RootElement,"epaEpsilon"));total++;
                maximumQueue=System.Math.Max(maximumQueue,work.MaximumQueueCount);
                if(result.Status is not (AlsEpaStatus.Ok or AlsEpaStatus.MaxIterations)){bad++;continue;}
                var normal=result.Normal;var normalB=normal.Rotate(pose.Rotation.Conjugate());
                var pa=result.PointA+normal*a.Margin;
                var pb=(result.PointBInA-normal*b.Margin-pose.Position).Rotate(pose.Rotation.Conjugate());
                var depth=System.Math.Abs(result.Penetration+a.Margin+b.Margin-D(row,"penetration"));
                var point=System.Math.Max(Error(pa,V(row,"pointA")),Error(pb,V(row,"pointB")));
                var ne=System.Math.Max(Error(normal,V(row,"normalA")),Error(normalB,V(row,"normalB")));
                maxDepth=System.Math.Max(maxDepth,depth);maxPoint=System.Math.Max(maxPoint,point);maxNormal=System.Math.Max(maxNormal,ne);
                if(depth>1e-8||point>1e-8||ne>1e-8||result.VertexA!=row.GetProperty("vertexA").GetInt32()||result.VertexB!=row.GetProperty("vertexB").GetInt32())
                    errors.Add($"{sequence.GetProperty("bone")} axis={sequence.GetProperty("axis")} sign={sequence.GetProperty("sign")} margin={b.Margin} warm={sequence.GetProperty("warmStart")} frame={row.GetProperty("frame")} depth={depth:R} point={point:R} normal={ne:R} vertices={result.VertexA}/{result.VertexB}");
            }
        }
        output.WriteLine($"EPA total={total} fallback={bad} max_depth={maxDepth:R} max_point={maxPoint:R} max_normal={maxNormal:R} max_queue={maximumQueue} unified_frames=528");
        Assert.Equal(318,total);Assert.Equal(0,bad);Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors.Take(25)));
    }
    private static double Error(AlsDoubleVector a,AlsDoubleVector b)=>System.Math.Sqrt((a-b).LengthSquared);
}
