using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsShapeBoundsReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void RuntimeCapsuleEndpointReconstructionMatchesBothNativeAssetSnapshots()
    {
        var json=File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_primitive_geometry.json"));
        var runtime=File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_runtime_shapes.json"));
        using var doc=JsonDocument.Parse(json);var count=0;
        foreach(var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
        {
            var compiled=GodotAls.Import.Compilation.AlsPrimitiveGeometryCompiler.Compile(json,runtime,mesh.GetProperty("mesh").GetString()!);
            foreach(var row in compiled)if(row.Capsule is { } capsule)
            {
                var shape=mesh.GetProperty("bodies")[row.Body].GetProperty("runtimeShapes").EnumerateArray()
                    .Single(s=>s.GetProperty("authoredIndex").GetInt32()==row.Shape);
                var expected=V(shape.GetProperty("primitiveGeometry").GetProperty("endpoint1"));
                Assert.Equal(expected,new AlsDoubleVector(capsule.Endpoint0+capsule.Axis*capsule.Height));count++;
            }
        }
        Assert.True(count>20);
        output.WriteLine($"Native asset capsule endpoints exactly matched: {count}");
    }
    [Fact]
    public void NativeShapeBoundsFlagsAndDeferredActivationMatchAcrossHistoryStates()
    {
        using var document=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_shape_bounds_reference.json")));
        var root=document.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("boundsChecksEnabled").GetBoolean());
        Assert.Equal("Native FSingleShapePairCollisionDetector GenerateCollision with deferred narrow phase isolates DoBoundsOverlap; cold/prior-active/gap epochs; actual shape bounds and flags; no contact geometry or solver trajectory parity",
            root.GetProperty("observation").GetString());
        var shapes=root.GetProperty("shapes").EnumerateArray().Select(Geometry).ToArray();
        Assert.Equal(5,shapes.Length);
        var cases=root.GetProperty("cases");Assert.Equal(6300,cases.GetArrayLength());
        var accepted=0;var rejected=0;var restoredDifferences=0;
        var maxBoundsError=0d;var historyCounts=new int[3];bool? cold=null;
        foreach(var row in cases.EnumerateArray())
        {
            var a=shapes[row.GetProperty("shape0").GetInt32()];var b=shapes[row.GetProperty("shape1").GetInt32()];
            var pa=Pose(row.GetProperty("pose0"));var pb=Pose(row.GetProperty("pose1"));
            var wa=a.Transform(pa);var wb=b.Transform(pb);
            maxBoundsError=Math.Max(maxBoundsError,Error(wa.Min,V(row.GetProperty("min0"))));
            maxBoundsError=Math.Max(maxBoundsError,Error(wa.Max,V(row.GetProperty("max0"))));
            maxBoundsError=Math.Max(maxBoundsError,Error(wb.Min,V(row.GetProperty("min1"))));
            maxBoundsError=Math.Max(maxBoundsError,Error(wb.Max,V(row.GetProperty("max1"))));
            var sa=a.Kind==AlsBoundsShapeKind.Sphere;var sb=b.Kind==AlsBoundsShapeKind.Sphere;
            Assert.Equal(!(sa&&sb),row.GetProperty("aabb").GetBoolean());
            Assert.Equal(!sa,row.GetProperty("obb0").GetBoolean());Assert.Equal(!sb,row.GetProperty("obb1").GetBoolean());
            Assert.Equal(sa&&sb,row.GetProperty("distance").GetBoolean());
            Assert.Equal(sa&&sb?a.Radius+b.Radius:0,row.GetProperty("distanceSize").GetSingle());
            var history=row.GetProperty("history").GetInt32();historyCounts[history]++;
            var prior=row.GetProperty("collidedLastStep").GetBoolean();Assert.Equal(history==1,prior);
            var actual=AlsShapeBoundsGeometry.Allows(a,pa,wa,b,pb,wb,row.GetProperty("cull").GetSingle(),prior);
            Assert.True(actual==row.GetProperty("allowed").GetBoolean(),$"shape0={row.GetProperty("shape0")} shape1={row.GetProperty("shape1")} history={history} cull={row.GetProperty("cull")} pose0={row.GetProperty("pose0")} pose1={row.GetProperty("pose1")}");
            if(actual)accepted++;else rejected++;
            if(history==0)cold=actual;
            if(history==1&&actual!=cold)restoredDifferences++;
            if(history==2)Assert.Equal(cold,actual);
        }
        Assert.Equal(new[]{2100,2100,2100},historyCounts);
        Assert.True(accepted>0&&rejected>0&&restoredDifferences>0);
        Assert.InRange(maxBoundsError,0,1e-9);
        output.WriteLine($"Native rows={cases.GetArrayLength()} accepted={accepted} rejected={rejected} priorActivityChangedDecision={restoredDifferences} maxWorldBoundsErrorCm={maxBoundsError:R}");
    }
    private static AlsShapeBoundsGeometry Geometry(JsonElement row)
    {
        var kind=row.GetProperty("kind").GetString();
        var bounds=new AlsContactBounds(V(row.GetProperty("min")),V(row.GetProperty("max")));
        if(kind=="polygon")return AlsShapeBoundsGeometry.Polygon(bounds);
        var a=V(row.GetProperty("endpoint0"));var b=V(row.GetProperty("endpoint1"));var r=row.GetProperty("radius").GetSingle();
        var geometry=kind=="sphere"?AlsShapeBoundsGeometry.Sphere(a,r):AlsShapeBoundsGeometry.Capsule(a,b,r);
        Assert.InRange(Error(bounds.Min,geometry.LocalBounds.Min),0,1e-5);
        Assert.InRange(Error(bounds.Max,geometry.LocalBounds.Max),0,1e-5);
        return geometry;
    }
    private static double Error(AlsDoubleVector a,AlsDoubleVector b)=>Math.Max(Math.Abs(a.X-b.X),Math.Max(Math.Abs(a.Y-b.Y),Math.Abs(a.Z-b.Z)));
    private static AlsDoubleVector V(JsonElement a)=>new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble());
    private static AlsPrecisePose Pose(JsonElement p)
    {var q=p.GetProperty("rotation");return new(V(p.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);}
}
