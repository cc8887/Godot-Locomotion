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
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RealFootPairsProduceTheNativeInitialManifoldInTheSameOrder(int mode)
    {
        var scaled=mode==1;
        using var doc=JsonDocument.Parse(Read(mode==3?"v4_physics_convex_margin_pair_reference":mode==2?"v4_physics_box_pair_reference":scaled?"v4_physics_scaled_convex_pair_reference":"v4_physics_convex_pair_reference"));var root=doc.RootElement;
        var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan");
        var hulls=AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"),definition).ToDictionary(t=>definition.Bodies[t.Body].Bone,t=>t.Topology);
        if(mode==2)
        {
            var box=new AlsBoxPolygonShape(new(8,5,3));var rows=root.GetProperty("boxVertexPlanes");
            Assert.Equal(8,rows.GetArrayLength());
            for(var v=0;v<8;v++)
            {
                var cachePlanes=box.VertexPlanes(v);Assert.Equal(rows[v].GetProperty("count").GetInt32(),cachePlanes.Count);
                for(var p=0;p<3;p++)Assert.Equal(rows[v].GetProperty("planes")[p].GetInt32(),cachePlanes.PlaneAt(p));
            }
        }
        var work=new AlsConvexManifoldWorkspace();var cache=new AlsGjkCache();var errors=new List<string>();
        Span<AlsDetectedContact> points=stackalloc AlsDetectedContact[4];var index=0;var empty=0;var edge=0;var face0=0;var face1=0;double maxPoint=0,maxNormal=0,maxPhi=0;
        var marginEdges=0;var projectedCulls=0;var pairKinds=new HashSet<string>();
        foreach(var row in root.GetProperty("cases").EnumerateArray())
        {
            cache.Reset();var pose=Pose(row.GetProperty("shape1To0"));
            var result=mode>=2?BoxPair(hulls,row,root,cache,work,points):scaled?AlsScaledConvexManifold.Build(hulls[row.GetProperty("bone0").GetString()!],V(row,"scale0"),hulls[row.GetProperty("bone1").GetString()!],V(row,"scale1"),pose,cache,work,points,
                D(row,"cullDistance"),D(root,"gjkEpsilon"),D(root,"epaEpsilon"),root.GetProperty("minimumFaceSearchDistance").GetSingle(),root.GetProperty("planeNormalEpsilon").GetSingle(),root.GetProperty("forceEdgeZeroCull").GetBoolean()):
                AlsRawConvexManifold.Build(hulls[row.GetProperty("bone0").GetString()!],hulls[row.GetProperty("bone1").GetString()!],pose,cache,work,points,
                D(row,"cullDistance"),D(root,"gjkEpsilon"),D(root,"epaEpsilon"),root.GetProperty("minimumFaceSearchDistance").GetSingle(),root.GetProperty("planeNormalEpsilon").GetSingle(),root.GetProperty("forceEdgeZeroCull").GetBoolean());
            var expected=row.GetProperty("points");
            if(mode==2)
            {
                pairKinds.Add(row.GetProperty("kind0").GetString()+"/"+row.GetProperty("kind1").GetString());
                if(D(row,"margin0")>0||D(row,"margin1")>0)
                {
                    if(result.Count>0&&result.Feature==AlsConvexContactFeature.EdgeEdge)marginEdges++;
                    if(result.Count==0&&result.Feature==AlsConvexContactFeature.None&&result.Plane0>=0)projectedCulls++;
                }
            }
            if(result.Count!=expected.GetArrayLength())errors.Add($"row={index} count={result.Count}/{expected.GetArrayLength()} planes={result.Plane0}/{result.Plane1}");
            else for(var i=0;i<result.Count;i++)
            {
                var p=expected[i];var pe=System.Math.Max(Error(new(points[i].Point0),V(p,"point0")),Error(new(points[i].Point1),V(p,"point1")));
                var ne=Error(new(points[i].Normal1),V(p,"normal1"));maxPoint=System.Math.Max(maxPoint,pe);maxNormal=System.Math.Max(maxNormal,ne);
                var point0in1=(new AlsDoubleVector(points[i].Point0)-pose.Position).Rotate(pose.Rotation.Conjugate());
                var phi=AlsDoubleVector.Dot(point0in1-new AlsDoubleVector(points[i].Point1),new(points[i].Normal1));
                var phiError=System.Math.Abs(phi-D(p,"phi"));maxPhi=System.Math.Max(maxPhi,phiError);
                Assert.Equal(p.GetProperty("phi").GetSingle(),points[i].NativePhi);
                // These fixtures now reproduce the stored float contacts
                // exactly; keep rounding-boundary regressions visible.
                if(pe!=0||ne!=0||phiError>1e-5||p.GetProperty("feature").GetString()!=result.Feature.ToString())
                    errors.Add($"row={index} point={i} pe={pe:R} ne={ne:R} phi={phiError:R} feature={result.Feature}/{p.GetProperty("feature")} planes={result.Plane0}/{result.Plane1}");
            }
            if(result.Count==0)empty++;else if(result.Feature==AlsConvexContactFeature.EdgeEdge)edge++;else if(result.Feature==AlsConvexContactFeature.PlaneVertex)face0++;else face1++;
            index++;
        }
        output.WriteLine($"RAW_CONVEX_MANIFOLD cases={index} empty={empty} edge={edge} reference0={face0} reference1={face1} max_point_cm={maxPoint:R} max_normal={maxNormal:R} max_reconstructed_phi_cm={maxPhi:R}");
        Assert.Equal(mode==3?11664:mode==2?6480:scaled?5184:1296,index);Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors.Take(30)));
        Assert.True(empty>0&&edge>0&&face0>0&&face1>0);
        if(mode==2)
        {
            output.WriteLine($"BOX_MARGIN edges={marginEdges} projected_culls={projectedCulls}");
            Assert.Equal(219,marginEdges);Assert.Equal(3,projectedCulls);Assert.Equal(5,pairKinds.Count);
        }
    }
    private static AlsConvexManifoldResult BoxPair(Dictionary<string,AlsConvexTopology> hulls,JsonElement row,JsonElement root,
        AlsGjkCache cache,AlsConvexManifoldWorkspace work,Span<AlsDetectedContact> points)
    {
        var box0=row.GetProperty("kind0").GetString()=="box";var box1=row.GetProperty("kind1").GetString()=="box";
        var a=new AlsBoxPolygonShape(V(row,"half"),row.GetProperty("margin0").GetSingle());
        var b=new AlsBoxPolygonShape(V(row,"half"),row.GetProperty("margin1").GetSingle());
        if(box0&&box1)return Build(a,b,row,root,cache,work,points);
        if(box0)return Build(a,Convex("1",hulls,row),row,root,cache,work,points);
        if(box1)return Build(Convex("0",hulls,row),b,row,root,cache,work,points);
        return Build(Convex("0",hulls,row),Convex("1",hulls,row),row,root,cache,work,points);
    }
    private static AlsConvexPolygonShape Convex(string side,Dictionary<string,AlsConvexTopology> hulls,JsonElement row)
    {
        var topology=hulls[row.GetProperty("bone"+side).GetString()!];var margin=row.GetProperty("margin"+side).GetSingle();
        return row.GetProperty("kind"+side).GetString()=="raw"?new(topology,margin):new(topology,V(row,"scale"),margin);
    }
    private static AlsConvexManifoldResult Build<TA,TB>(TA a,TB b,JsonElement row,JsonElement root,
        AlsGjkCache cache,AlsConvexManifoldWorkspace work,Span<AlsDetectedContact> points)
        where TA:struct,IAlsPolygonShape where TB:struct,IAlsPolygonShape
        =>AlsPolygonManifold.Build(a,b,Pose(row.GetProperty("shape1To0")),cache,work,points,D(row,"cullDistance"),
            D(root,"gjkEpsilon"),D(root,"epaEpsilon"),root.GetProperty("minimumFaceSearchDistance").GetSingle(),
            root.GetProperty("planeNormalEpsilon").GetSingle(),root.GetProperty("forceEdgeZeroCull").GetBoolean());
    private static double Error(AlsDoubleVector a,AlsDoubleVector b)=>System.Math.Sqrt((a-b).LengthSquared);
}
