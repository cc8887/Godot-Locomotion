using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsManifoldCaptureReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_physics_manifold_capture_reference.json", 43, 37, 140)]
    [InlineData("v4_physics_left_manifold_capture_reference.json", 44, 35, 136)]
    public void ActualRetainedHandManifoldsMatchNativeRestoreDecisionsAndGeometry(string file, int caseCount, int expectedRestored, int expectedPublished)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/" + file)));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_runtime_shapes.json")))),
            doc.RootElement.GetProperty("provenance").GetProperty("runtimeShapesSha256").GetString());
        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(caseCount, cases.Length);
        var restored = 0; var rejected = 0; var pointCount = 0; var published = 0;
        foreach (var row in cases)
        {
            var input = row.GetProperty("input"); var retained = input.GetProperty("retained");
            var id = $"{input.GetProperty("mesh")}/{input.GetProperty("frame")}";
            var key = input.GetProperty("key").Deserialize<AlsContactPairKey>(JsonOptions);
            Assert.Equal(key, retained.GetProperty("key").Deserialize<AlsContactPairKey>(JsonOptions));
            var epoch = input.GetProperty("epoch").GetInt64();
            Assert.Equal(epoch - 1, retained.GetProperty("epoch").GetInt64());
            var previous = retained.GetProperty("points").EnumerateArray().ToArray();
            var initial = new AlsDetectedContact[previous.Length];
            for (var i = 0; i < previous.Length; i++)
            {
                var p = previous[i];
                // All captured hand points are enabled, retain initial point0, and
                // restoration replaces point1/Phi. Seeding via PrepareNew therefore
                // reproduces every field consumed by this path without a mutable
                // production cache-import API. Disabled points are outside this fixture.
                Assert.False(B(p, "disabled")); Assert.Equal(F(p, "initial0"), F(p, "point0"));
                initial[i] = new(F(p, "initial0"), F(p, "initial1"), F(p, "normal1")) { NativePhi = (float)D(p, "phi") };
            }
            var q = retained.GetProperty("rotationDelta");
            var reference0 = AlsPrecisePose.Identity with { Position = new(F(retained, "positionDelta")) };
            var reference1 = AlsPrecisePose.Identity with
            { Rotation = new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()) };
            var tolerance = (float)D(retained, "tolerance");
            Assert.Equal(tolerance, (float)D(row, "tolerance"));
            var cache = new AlsContactManifoldCache(8); var points = new AlsDetectedContact[8];
            cache.PrepareNew(key, epoch - 1, reference0, reference1, tolerance, initial); cache.Publish();
            var success = cache.TryRestore(key, epoch, Pose(input.GetProperty("pose0")), Pose(input.GetProperty("pose1")),
                tolerance, points, out var count);
            Assert.True(success == B(row, "restored"), id);
            Assert.Equal(B(input, "capturedRestored"), success && B(row, "withinCullDistance"));
            if (!success) { rejected++; Assert.False(cache.Pending); continue; }
            restored++; Assert.Equal((float)D(row, "minimumPhi"), cache.MinimumPhi);
            Assert.Equal(cache.MinimumPhi <= D(input, "cullDistance"), B(row, "withinCullDistance"));
            var expected = row.GetProperty("points"); var captured = input.GetProperty("capturedDetected");
            Assert.Equal(expected.GetArrayLength(), count); Assert.Equal(count, captured.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                var e = expected[i]; var c = captured[i];
                Assert.Equal(F(e, "point0"), points[i].Point0); Assert.Equal(F(e, "point1"), points[i].Point1);
                Assert.Equal(F(e, "normal1"), points[i].Normal1); Assert.Equal((float)D(e, "phi"), points[i].NativePhi);
                Assert.Equal(B(e, "disabled"), points[i].Disabled);
                Assert.Equal(F(e, "point0"), F(c, "point0")); Assert.Equal(F(e, "point1"), F(c, "point1"));
                Assert.Equal(F(e, "normal1"), F(c, "normal1")); Assert.Equal(B(e, "disabled"), B(c, "disabled"));
                pointCount++;
            }
            // A following capture observes the actual published Godot Phi as well.
            var next = cases.Where(n => n.GetProperty("input").GetProperty("mesh").GetString() == input.GetProperty("mesh").GetString()
                && n.GetProperty("input").GetProperty("epoch").GetInt64() == epoch + 1).ToArray();
            if (next.Length == 0) continue;
            Assert.Single(next); var nextPoints = next[0].GetProperty("input").GetProperty("retained").GetProperty("points");
            Assert.Equal(count, nextPoints.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                Assert.Equal(points[i].Point1, F(nextPoints[i], "point1"));
                Assert.Equal(points[i].NativePhi, (float)D(nextPoints[i], "phi"));
                Assert.Equal(F(previous[i], "initial0"), F(nextPoints[i], "initial0"));
                Assert.Equal(F(previous[i], "initial1"), F(nextPoints[i], "initial1")); published++;
            }
        }
        output.WriteLine($"ACTUAL_MANIFOLD restored={restored} rejected={rejected} exactPoints={pointCount} publishedPoints={published}");
        Assert.Equal(expectedRestored, restored); Assert.Equal(caseCount - expectedRestored, rejected);
        Assert.Equal(expectedPublished, published);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static Vector3 F(JsonElement p, string name) => V(p, name).ToSingle();
}
