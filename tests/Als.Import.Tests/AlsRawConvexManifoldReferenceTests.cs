using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsRawConvexManifoldReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealFootPairsProduceTheNativeInitialManifoldInTheSameOrder(bool scaled)
    {
        using var doc=JsonDocument.Parse(Read(scaled?"v4_physics_scaled_convex_pair_reference":"v4_physics_convex_pair_reference"));var root=doc.RootElement;
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var hulls=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition).ToDictionary(t=>definition.Bodies[t.Body].Bone,t=>t.Topology);
        var work=new AlsConvexManifoldWorkspace();var cache=new AlsGjkCache();var errors=new List<string>();
        Span<AlsDetectedContact> points=stackalloc AlsDetectedContact[4];var index=0;var empty=0;var edge=0;var face0=0;var face1=0;double maxPoint=0,maxNormal=0,maxPhi=0;
        foreach(var row in root.GetProperty("cases").EnumerateArray())
        {
            cache.Reset();var pose=Pose(row.GetProperty("shape1To0"));
            var result=scaled?AlsScaledConvexManifold.Build(hulls[row.GetProperty("bone0").GetString()!],V(row,"scale0"),hulls[row.GetProperty("bone1").GetString()!],V(row,"scale1"),pose,cache,work,points,
                D(row,"cullDistance"),D(root,"gjkEpsilon"),D(root,"epaEpsilon"),root.GetProperty("minimumFaceSearchDistance").GetSingle(),root.GetProperty("planeNormalEpsilon").GetSingle(),root.GetProperty("forceEdgeZeroCull").GetBoolean()):
                AlsRawConvexManifold.Build(hulls[row.GetProperty("bone0").GetString()!],hulls[row.GetProperty("bone1").GetString()!],pose,cache,work,points,
                D(row,"cullDistance"),D(root,"gjkEpsilon"),D(root,"epaEpsilon"),root.GetProperty("minimumFaceSearchDistance").GetSingle(),root.GetProperty("planeNormalEpsilon").GetSingle(),root.GetProperty("forceEdgeZeroCull").GetBoolean());
            var expected=row.GetProperty("points");
            if(result.Count!=expected.GetArrayLength())errors.Add($"row={index} count={result.Count}/{expected.GetArrayLength()} planes={result.Plane0}/{result.Plane1}");
            else for(var i=0;i<result.Count;i++)
            {
                var p=expected[i];var pe=System.Math.Max(Error(new(points[i].Point0),V(p,"point0")),Error(new(points[i].Point1),V(p,"point1")));
                var ne=Error(new(points[i].Normal1),V(p,"normal1"));maxPoint=System.Math.Max(maxPoint,pe);maxNormal=System.Math.Max(maxNormal,ne);
                var point0in1=(new AlsDoubleVector(points[i].Point0)-pose.Position).Rotate(pose.Rotation.Conjugate());
                var phi=AlsDoubleVector.Dot(point0in1-new AlsDoubleVector(points[i].Point1),new(points[i].Normal1));
                var phiError=System.Math.Abs(phi-D(p,"phi"));maxPhi=System.Math.Max(maxPhi,phiError);
                if(pe>1e-5||ne>1e-6||phiError>1e-5||p.GetProperty("feature").GetString()!=result.Feature.ToString())
                    errors.Add($"row={index} point={i} pe={pe:R} ne={ne:R} phi={phiError:R} feature={result.Feature}/{p.GetProperty("feature")} planes={result.Plane0}/{result.Plane1}");
            }
            if(result.Count==0)empty++;else if(result.Feature==AlsConvexContactFeature.EdgeEdge)edge++;else if(result.Feature==AlsConvexContactFeature.PlaneVertex)face0++;else face1++;
            index++;
        }
        output.WriteLine($"RAW_CONVEX_MANIFOLD cases={index} empty={empty} edge={edge} reference0={face0} reference1={face1} max_point_cm={maxPoint:R} max_normal={maxNormal:R} max_reconstructed_phi_cm={maxPhi:R}");
        Assert.Equal(scaled?5184:1296,index);Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors.Take(30)));
        Assert.True(empty>0&&edge>0&&face0>0&&face1>0);
    }
    private static double Error(AlsDoubleVector a,AlsDoubleVector b)=>System.Math.Sqrt((a-b).LengthSquared);
}
