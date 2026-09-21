using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsNativeCapsuleMixedTraceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FailedChainInputsMatchNativeAndPreserveTheRecordedRuntimeRegression()
    {
        string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
        using var doc = JsonDocument.Parse(Read("v4_physics_native_capsule_mixed_reference"));
        var definition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"), AlsPhysicsAssetCompiler.MeshRoot + "AnimMan.AnimMan");
        var hulls = AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology"), definition).ToDictionary(t => definition.Bodies[t.Body].Bone, t => t.Topology);
        var root = doc.RootElement; var counts = new int[2]; var active = new int[2]; var totals = new int[2];
        var errors = new List<string>(); var points = new AlsDetectedContact[3]; var work = new AlsCapsuleManifoldWorkspace();
        double maxCorePoint = 0, maxCoreNormal = 0, maxCorePhi = 0, maxCapturedPoint = 0, maxCapturedNormal = 0, maxCapturedPhi = 0;
        Assert.Equal(726, root.GetProperty("traceCount").GetInt32());
        Assert.Equal(366, root.GetProperty("skippedOtherCount").GetInt32());
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var source = row.GetProperty("source"); var swapped = B(row, "swapped");
            var g0 = source.GetProperty(swapped ? "geometry1" : "geometry0"); var g1 = source.GetProperty(swapped ? "geometry0" : "geometry1");
            var p0 = Pose(row.GetProperty("pose0")); var p1 = Pose(row.GetProperty("pose1"));
            var kind = row.GetProperty("kind").GetString() == "capsule_pair" ? 0 : 1;
            static AlsCapsuleGeometry Capsule(JsonElement g) => new(V(g, "endpoint0").ToSingle(), V(g, "axis").ToSingle(), (float)D(g, "length"), (float)D(g, "radius"));
            int count;
            if (kind == 0) count = AlsCapsuleCapsuleManifold.Build(Capsule(g0), p0, B(g0, "dynamic"), Capsule(g1), p1, B(g1, "dynamic"), (float)D(row, "cullDistance"), points);
            else
            {
                var hull = hulls[source.GetProperty(swapped ? "body0" : "body1").GetString()!]; var scale = V(g1, "scale");
                var convex = scale == AlsDoubleVector.One ? new AlsConvexPolygonShape(hull) : new AlsConvexPolygonShape(hull, scale);
                count = AlsCapsuleConvexManifold.Build(Capsule(g0), AlsPrecisePose.Relative(p0, p1), convex, work, points, D(row, "cullDistance"));
            }
            var native = row.GetProperty("points"); var captured = source.GetProperty("contacts");
            Assert.Equal((double)source.GetProperty("cullDistance").GetSingle(), D(row, "cullDistance"));
            if (count != native.GetArrayLength() || captured.GetArrayLength() != count)
                errors.Add($"{counts.Sum()}: counts Core={count} native={native.GetArrayLength()} captured={captured.GetArrayLength()}");
            else for (var i = 0; i < count; i++)
            {
                var n = native[i]; var c = captured[i]; var actual = points[i];
                var a = V(n, "point0").ToSingle(); var b = V(n, "point1").ToSingle(); var normal = V(n, "normal1"); var phi = n.GetProperty("phi").GetSingle();
                maxCorePoint = Math.Max(maxCorePoint, Math.Max(Vector3.Distance(a, actual.Point0), Vector3.Distance(b, actual.Point1)));
                maxCoreNormal = Math.Max(maxCoreNormal, Vector3.Distance(normal.ToSingle(), actual.Normal1));
                maxCorePhi = Math.Max(maxCorePhi, Math.Abs(phi - actual.NativePhi!.Value));
                if (swapped) { (a, b) = (b, a); normal = (normal.Rotate(p1.Rotation) * -1).Rotate(p0.Rotation.Conjugate()); }
                maxCapturedPoint = Math.Max(maxCapturedPoint, Math.Max(Vector3.Distance(a, V(c, "localPoint0").ToSingle()), Vector3.Distance(b, V(c, "localPoint1").ToSingle())));
                maxCapturedNormal = Math.Max(maxCapturedNormal, Vector3.Distance(normal.ToSingle(), V(c, "localNormal1").ToSingle()));
                maxCapturedPhi = Math.Max(maxCapturedPhi, Math.Abs(phi - c.GetProperty("nativePhi").GetSingle()));
            }
            counts[kind]++; if (count > 0) active[kind]++; totals[kind] += count;
        }
        output.WriteLine($"MIXED_TRACE counts={string.Join(',', counts)} active={string.Join(',', active)} points={string.Join(',', totals)} core={maxCorePoint:R}/{maxCoreNormal:R}/{maxCorePhi:R} captured={maxCapturedPoint:R}/{maxCapturedNormal:R}/{maxCapturedPhi:R}");
        Assert.Equal(new[] { 246, 114 }, counts); Assert.Equal(new[] { 30, 0 }, active); Assert.Equal(new[] { 36, 0 }, totals);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(20)));
        Assert.All(new[] { maxCorePoint, maxCoreNormal, maxCorePhi }, value => Assert.Equal(0, value));
        // This immutable source was captured before the Cross runtime fix.
        // It must retain evidence of the old mismatch, not silently be refreshed.
        Assert.True(maxCapturedPoint > 0 && maxCapturedNormal > 0);
        Assert.Equal(1.9073486328125e-6, maxCapturedPhi);
    }
}
