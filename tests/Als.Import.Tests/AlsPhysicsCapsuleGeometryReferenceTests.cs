using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsCapsuleGeometryReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void GuardedCapsuleFacePointsAndOrderMatchNativeGeometry()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_capsule_geometry_reference.json")));
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var cases = root.GetProperty("cases"); Assert.Equal(744, cases.GetArrayLength());
        Assert.Equal(162, root.GetProperty("syntheticCount").GetInt32());
        var handled = 0; var synthetic = 0; var populated = 0; var pointCounts = new HashSet<int>();
        double maxP = 0, maxN = 0; var errors = new List<string>();
        Span<AlsDetectedContact> actual = stackalloc AlsDetectedContact[3];
        foreach (var row in cases.EnumerateArray())
        {
            var source = row.GetProperty("source"); var isSynthetic = source.GetProperty("mesh").GetString() == "synthetic";
            var supported = AlsCapsuleBoxManifold.TryInteriorFace(D(row, "radius"), D(row, "length"),
                Pose(row.GetProperty("capsulePose")), V(row, "boxHalf"), Pose(row.GetProperty("boxPose")),
                D(row, "cullDistance"), actual, out var count);
            if (isSynthetic) { synthetic++; Assert.True(supported, "Synthetic face interior must be covered."); }
            if (!supported) continue;
            handled++; var points = row.GetProperty("points"); pointCounts.Add(count); if (count > 0) populated++;
            var id = $"{source.GetProperty("mesh")}/{source.GetProperty("frame")}/{source.GetProperty("body0")}/{source.GetProperty("body1")}/cull={D(row, "cullDistance")}";
            if (count != points.GetArrayLength()) { errors.Add($"{id} count={count}, native={points.GetArrayLength()}"); continue; }
            for (var i = 0; i < count; i++)
            {
                var expected = points[i]; Assert.False(B(expected, "disabled"));
                var p0 = Vector3.Distance(actual[i].Point0, V(expected, "point0").ToSingle());
                var p1 = Vector3.Distance(actual[i].Point1, V(expected, "point1").ToSingle());
                var n = Vector3.Distance(actual[i].Normal1, V(expected, "normal1").ToSingle());
                maxP = System.Math.Max(maxP, System.Math.Max(p0, p1)); maxN = System.Math.Max(maxN, n);
                if ((p0 > 2e-4 || p1 > 2e-4 || n > 1e-5) && errors.Count < 30)
                    errors.Add($"{id} point={i} P0={p0:R} P1={p1:R} N={n:R}");
            }
        }
        output.WriteLine($"NATIVE_CAPSULE_GEOMETRY cases={cases.GetArrayLength()} handled={handled} synthetic={synthetic} populated={populated} max_point_cm={maxP:R} max_normal={maxN:R}");
        Assert.Equal(324, synthetic); Assert.True(handled > synthetic); Assert.True(populated > 0);
        Assert.Equal(new[] { 0, 1, 2, 3 }, pointCounts.Order().ToArray());
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(30)));
    }
}
