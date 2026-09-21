using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsRawGatherReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FailedChainRawInputsRegenerateNativeContactRows()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_raw_gather_reference.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        var frames = new HashSet<string>(); var ids = new HashSet<string>();
        var count = 0; var anchored = 0; var fresh = 0; var global = 0;
        float maxCore = 0, maxCaptured = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var frame = $"{row.GetProperty("mesh")}/{row.GetProperty("frame")}"; frames.Add(frame);
            var id = $"{frame}/{row.GetProperty("pair")}"; Assert.True(ids.Add(id));
            var capture = row.GetProperty("capture"); var g = capture.GetProperty("gather"); var s = g.GetProperty("settings");
            var settings = new AlsContactGatherSettings(F(s, "dt"), F(s, "restitution"), F(s, "restitutionThreshold"),
                F(s, "maxPushOutVelocity"), F(s, "maxDepenetrationVelocity"), B(s, "perContactInitialPhi"), B(s, "initialManifold"), F(s, "minInitialPhi"));
            if (!settings.PerContactInitialPhi) global++;
            var b0 = Body(g.GetProperty("body0")); var b1 = Body(g.GetProperty("body1"));
            var geometry = g.GetProperty("points"); var native = row.GetProperty("nativePoints"); var captured = capture.GetProperty("points");
            Assert.Equal(geometry.GetArrayLength(), native.GetArrayLength()); Assert.Equal(native.GetArrayLength(), captured.GetArrayLength());
            for (var i = 0; i < geometry.GetArrayLength(); i++)
            {
                var r = geometry[i]; var raw = new AlsContactGeometry(Vec(r, "point0"), Vec(r, "point1"), Vec(r, "normal1"),
                    Vec(r, "anchor0"), Vec(r, "anchor1"), B(r, "hasAnchor"), B(r, "initialContact"), F(r, "initialPhi"), F(r, "targetPhi"),
                    B(r, "disablePosition"), B(r, "disableVelocity"), B(r, "disableFriction"));
                if (raw.HasAnchor) anchored++; else fresh++;
                var actual = AlsContactGather.Gather(raw, b0, b1, settings); var expected = Point(native[i]);
                maxCore = MathF.Max(maxCore, Difference(actual.Point, actual.InitialPhi, expected, F(native[i], "initialPhi")));
                maxCaptured = MathF.Max(maxCaptured, Difference(Point(captured[i]), F(captured[i], "initialPhi"), expected, F(native[i], "initialPhi")));
                count++;
            }
        }
        output.WriteLine($"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} frames={frames.Count} pairs={ids.Count} points={count} anchored={anchored} fresh={fresh} global={global} maxCore={maxCore:R} maxCaptured={maxCaptured:R}");
        Assert.Equal(6, frames.Count); Assert.Equal(156, ids.Count); Assert.Equal(369, count);
        Assert.Equal(369, anchored); Assert.Equal(0, fresh); Assert.Equal(27, global);
        // Current Core must reproduce native floats on both .NET 8 and .NET 9.
        // The immutable pre-fix Godot capture retains the actual regression.
        Assert.Equal(0, maxCore); Assert.Equal(1.0662403e-6f, maxCaptured);
    }
    private static float Difference(AlsContactPointInput a, float phiA, AlsContactPointInput b, float phiB)
    {
        Assert.Equal(b.DisablePosition, a.DisablePosition); Assert.Equal(b.DisableVelocity, a.DisableVelocity); Assert.Equal(b.DisableFriction, a.DisableFriction);
        return new[] { Vector3.Distance(a.Arm0, b.Arm0), Vector3.Distance(a.Arm1, b.Arm1), Vector3.Distance(a.Normal, b.Normal),
            Vector3.Distance(a.TangentU, b.TangentU), Vector3.Distance(a.TangentV, b.TangentV), Vector3.Distance(a.Error, b.Error),
            MathF.Abs(a.TargetVelocity - b.TargetVelocity), MathF.Abs(phiA - phiB) }.Max();
    }
    private static AlsContactPointInput Point(JsonElement e) => new(Vec(e, "arm0"), Vec(e, "arm1"), Vec(e, "normal"), Vec(e, "u"), Vec(e, "v"),
        Vec(e, "error"), F(e, "targetVelocity"), B(e, "disablePosition"), B(e, "disableVelocity"), B(e, "disableFriction"));
    private static AlsContactGatherBody Body(JsonElement e) => new(Pose(e.GetProperty("shapeWorld")), V(e, "centerOfMass"), F(e, "inverseMass"), new(Vec(e, "v"), Vec(e, "w")));
    private static Vector3 Vec(JsonElement e, string name) => V(e, name).ToSingle();
    private static float F(JsonElement e, string name) => (float)D(e, name);
}
