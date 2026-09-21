using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsManifoldRestoreReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void SuccessivePolygonalManifoldsMatchNativeRestorationAndRejection()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_manifold_restore_reference.json")));
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var cases = root.GetProperty("cases"); Assert.Equal(72, cases.GetArrayLength());
        var restored = 0; var rejected = 0; var disabled = 0; float maxPoint = 0, maxPhi = 0;
        var key = new AlsContactPairKey(new(1, 1, 0, 1), new(2, 1, 0, 1));
        foreach (var row in cases.EnumerateArray())
        {
            var cache = new AlsContactManifoldCache(8); var points = new AlsDetectedContact[8];
            foreach (var frame in row.GetProperty("frames").EnumerateArray())
            {
                var step = frame.GetProperty("frame").GetInt32(); var a = Pose(frame.GetProperty("pose0")); var b = Pose(frame.GetProperty("pose1"));
                var success = cache.TryRestore(key, step, a, b, (float)D(row, "tolerance"), points, out var count);
                Assert.True(success == B(frame, "restored"), $"scenario={row.GetProperty("scenario")} count={row.GetProperty("count")} rotated={row.GetProperty("rotated")} frame={step}");
                if (success)
                {
                    restored++; maxPhi = MathF.Max(maxPhi, MathF.Abs(cache.MinimumPhi - (float)D(frame, "minimumPhi")));
                    Assert.InRange(MathF.Abs(cache.MinimumPhi - (float)D(frame, "minimumPhi")), 0, 2e-5f);
                }
                else
                {
                    rejected++; var input = frame.GetProperty("input").EnumerateArray().Select(Contact).ToArray();
                    cache.PrepareNew(key, step, a, b, (float)D(row, "tolerance"), input); input.CopyTo(points, 0); count = input.Length;
                }
                var expected = frame.GetProperty("points"); Assert.Equal(expected.GetArrayLength(), count);
                for (var i = 0; i < count; i++)
                {
                    var p = Contact(expected[i]); Assert.Equal(p.Disabled, points[i].Disabled); if (success && p.Disabled) disabled++;
                    var error = MathF.Max(Vector3.Distance(p.Point0, points[i].Point0), Vector3.Distance(p.Point1, points[i].Point1));
                    maxPoint = MathF.Max(maxPoint, error); Assert.InRange(error, 0, 2e-5f); Assert.Equal(p.Normal1, points[i].Normal1);
                }
                cache.Publish();
            }
        }
        output.WriteLine($"NATIVE_MANIFOLD_RESTORE cases=72 frames={restored + rejected} restored={restored} rejected={rejected} disabled={disabled} max_point_cm={maxPoint:R} max_phi_cm={maxPhi:R}");
        Assert.Equal(576, restored + rejected); Assert.True(restored > 100 && rejected > 100 && disabled > 0);
    }
    private static AlsDetectedContact Contact(JsonElement p) => new(V(p, "point0").ToSingle(), V(p, "point1").ToSingle(), V(p, "normal1").ToSingle(), B(p, "disabled"));
}
