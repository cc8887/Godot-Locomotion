using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsFaceClipReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void SelectedFaceClippingReductionAndOrderMatchNativeInitialManifolds()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_face_clip_reference.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(648, cases.GetArrayLength());
        Span<AlsDetectedContact> actual = stackalloc AlsDetectedContact[4];
        var compared = 0; var clipped = 0; var reduced = 0; var maximum = 0; var rejected = 0;
        double maxError = 0; var errors = new List<string>();
        static AlsDoubleVector Vector(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        foreach (var row in cases.EnumerateArray())
        {
            // This gate explicitly excludes native GJK edge/other-face decisions.
            if (!row.GetProperty("planeCase").GetBoolean()) { rejected++; continue; }
            var reference = row.GetProperty("referenceFace").EnumerateArray().Select(Vector).ToArray();
            var incident = row.GetProperty("incidentFace").EnumerateArray().Select(Vector).ToArray();
            var pose = Pose(row.GetProperty("incidentToReference")); var normal = V(row, "referenceNormal");
            var count = AlsConvexFaceManifold.Build(reference, incident, pose, normal, V(row, "referencePoint"),
                normal, false, actual, out var clipCount);
            compared++; if (D(row, "offset") > 0) clipped++; if (clipCount > 4) reduced++;
            maximum = Math.Max(maximum, clipCount);
            var expected = row.GetProperty("points");
            var id = $"sides={D(row, "sides")} face={D(row, "face")} tilt={D(row, "tilt")} offset={D(row, "offset")} gap={D(row, "gap")}";
            if (count != expected.GetArrayLength()) { errors.Add($"{id} count={count} native={expected.GetArrayLength()}"); continue; }
            for (var i = 0; i < count; i++)
            {
                var error = Math.Max(Vector3.Distance(actual[i].Point0, V(expected[i], "point0").ToSingle()),
                    Vector3.Distance(actual[i].Point1, V(expected[i], "point1").ToSingle()));
                maxError = Math.Max(maxError, error);
                if (error > 2e-4) errors.Add($"{id} index={i} difference_cm={error:R}");
                Assert.True(Vector3.Distance(actual[i].Normal1, V(expected[i], "normal1").ToSingle()) < 2e-5);
            }
            // Same selected features with shape identity reversed (not a new GJK decision).
            var reversedNormal = (normal * -1).Rotate(pose.Rotation.Conjugate());
            count = AlsConvexFaceManifold.Build(reference, incident, pose, normal, V(row, "referencePoint"),
                reversedNormal, true, actual, out _);
            for (var i = 0; i < count; i++)
                if (Vector3.Distance(actual[i].Point0, V(expected[i], "point1").ToSingle()) > 2e-4 ||
                    Vector3.Distance(actual[i].Point1, V(expected[i], "point0").ToSingle()) > 2e-4)
                    errors.Add($"{id} reversed index={i}");
        }
        output.WriteLine($"NATIVE_FACE_CLIP cases={cases.GetArrayLength()} compared={compared} excluded_gjk={rejected} offset_cases={clipped} reduced={reduced} max_clipped={maximum} max_point_cm={maxError:R}");
        Assert.True(compared > 200 && clipped > 100 && reduced > 50 && maximum > 4);
        Assert.Equal(32, maximum);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Take(40)));
    }
}
