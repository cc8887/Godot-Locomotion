using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsBoxGeometryReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void InteriorBoxFacePointsAndDiagonalOrderMatchNative()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_box_geometry_reference.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(432, cases.GetArrayLength());
        Span<AlsDetectedContact> actual = stackalloc AlsDetectedContact[4];
        var handled = 0; var populated = 0; double maxP = 0, maxN = 0; var errors = new List<string>();
        foreach (var row in cases.EnumerateArray())
        {
            var supported = AlsBoxFaceManifold.TryInteriorFace(V(row, "half0"), Pose(row.GetProperty("pose0")),
                V(row, "half1"), Pose(row.GetProperty("pose1")), D(row, "cullDistance"), actual, out var count);
            Assert.Equal(row.GetProperty("interior").GetBoolean(), supported);
            if (!supported) continue;
            handled++; if (count > 0) populated++;
            var expected = row.GetProperty("points");
            var id = $"face={D(row, "face")} tilt={D(row, "tilt")} gap={D(row, "gap")} cull={D(row, "cullDistance")}";
            if (count != expected.GetArrayLength()) { errors.Add($"{id} count={count} native={expected.GetArrayLength()}"); continue; }
            for (var i = 0; i < count; i++)
            {
                var p0 = Vector3.Distance(actual[i].Point0, V(expected[i], "point0").ToSingle());
                var p1 = Vector3.Distance(actual[i].Point1, V(expected[i], "point1").ToSingle());
                var n = Vector3.Distance(actual[i].Normal1, V(expected[i], "normal1").ToSingle());
                maxP = System.Math.Max(maxP, System.Math.Max(p0, p1)); maxN = System.Math.Max(maxN, n);
                if (p0 > 2e-4 || p1 > 2e-4 || n > 1e-5) errors.Add($"{id} point={i} P0={p0:R} P1={p1:R} N={n:R}");
            }
        }
        output.WriteLine($"NATIVE_BOX_GEOMETRY cases={cases.GetArrayLength()} handled={handled} populated={populated} max_point_cm={maxP:R} max_normal={maxN:R}");
        Assert.Equal(216, handled); Assert.True(populated > 0);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(25)));
    }
}
