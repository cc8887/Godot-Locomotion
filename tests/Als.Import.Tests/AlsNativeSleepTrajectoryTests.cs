using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsNativeSleepTrajectoryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void SleepCountersMetricsTrajectoriesAndImpulseWakeMatchNativeScene()
    {
        var reference = File.ReadAllBytes(AlsFootRigCompilerTests.PathInRepository(
            "tests/Als.Import.Tests/Fixtures/Physics/v4_physics_sleep_reference.json"));
        using var runtime = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_sleep_settings.json")));
        Assert.False(runtime.RootElement.TryGetProperty("cases", out _));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(reference)),
            runtime.RootElement.GetProperty("referenceSha256").GetString());
        using var doc = JsonDocument.Parse(reference);
        var root = doc.RootElement; Assert.True(B(root, "sleepEnabled"));
        var cvars = root.GetProperty("sleepSettings");
        Assert.Equal(0, D(cvars, "p.Chaos.Solver.Sleep.PartialIslandSleep"));
        Assert.Equal(0, D(cvars, "p.Chaos.Solver.Sleep.AngularSleepThresholdSize"));
        var rate = (float)D(cvars, "p.Chaos.SmoothedPositionLerpRate");
        var rows = root.GetProperty("cases"); Assert.Equal(144, rows.GetArrayLength());
        var cases = 0; var sleeping = 0; var woken = 0; double maxP = 0, maxAngle = 0, maxSmooth = 0; float maxV = 0, maxW = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var body = row.GetProperty("bodies")[1]; var dt = D(row, "dt");
            Assert.Equal(0, body.GetProperty("sleepType").GetInt32());
            var multiplier = (float)D(body, "sleepThresholdMultiplier");
            var sleep = new AlsSleepBodySettings((float)D(body, "sleepLinearThreshold") * multiplier,
                (float)D(body, "sleepAngularThreshold") * multiplier, body.GetProperty("sleepCounterThreshold").GetInt32());
            var sample0 = row.GetProperty("samples")[0];
            var island = new AlsJointIsland([
                new(Pose(row.GetProperty("bodies")[0].GetProperty("massLocal")), default),
                new(Pose(body.GetProperty("massLocal")), new((float)(1 / D(body, "massKg")), V(body, "bodyConditionedInverseInertia")), D(body, "linearDamping"), D(body, "angularDamping"))],
                [new(0, 1, Pose(row.GetProperty("parentFrame")), Pose(row.GetProperty("childFrame")), Settings(row),
                    AlsCachedJointSettingsCompiler.Projection(row.GetProperty("jointSettings"), row.GetProperty("solverSettings")))],
                [new(Pose(sample0.GetProperty("parent").GetProperty("world")), default), new(Pose(sample0.GetProperty("child").GetProperty("world")), default)],
                row.GetProperty("positionIterations").GetInt32(), row.GetProperty("velocityIterations").GetInt32(), [default, sleep], rate);
            var forces = new AlsBodyStepForces[2];
            for (var frame = 1; frame <= root.GetProperty("stepsPerCase").GetInt32(); frame++)
            {
                var wakeFrame = frame == root.GetProperty("wakeFrame").GetInt32();
                if (wakeFrame && island.IsSleeping) woken++;
                forces[1] = wakeFrame ? new(default, LinearImpulseVelocity: V(root, "wakeImpulseVelocity")) : default;
                // Frame zero teleports the native child and sets V/W to zero.
                // That explicit transform update disables island sleep for the
                // first step. This is input semantics, never sampled awake state.
                island.Step(dt, default, forces, allowSleep: frame != 1);
                var sample = row.GetProperty("samples")[frame]; var expected = sample.GetProperty("child");
                var context = $"case={cases} frame={frame}";
                Assert.True(island.IsSleeping == !B(expected, "awake"), context + " sleep state");
                Assert.True(island.SleepCounter == sample.GetProperty("islandSleepCounter").GetInt32(), context + " island counter");
                var metrics = island.SleepMetricsAt(1);
                Assert.True(metrics.ParticleCounter == expected.GetProperty("particleSleepCounter").GetInt32(), context + " particle counter");
                var smooth = System.Math.Max(System.Math.Sqrt((metrics.Linear - V(expected, "smoothLinear")).LengthSquared),
                    System.Math.Sqrt((metrics.Angular - V(expected, "smoothAngular")).LengthSquared));
                var actual = island.BodyAt(1); var pose = Pose(expected.GetProperty("world"));
                var position = System.Math.Sqrt((actual.Actor.Position - pose.Position).LengthSquared);
                var angle = 2 * System.Math.Acos(System.Math.Clamp(System.Math.Abs(AlsQuaternion.Dot(actual.Actor.Rotation.Normalized(), pose.Rotation.Normalized())), 0, 1));
                var v = Vector3.Distance(actual.Velocity.Linear, V(expected, "linearVelocity").ToSingle());
                var w = Vector3.Distance(actual.Velocity.Angular, V(expected, "angularVelocity").ToSingle());
                maxP = System.Math.Max(maxP, position); maxAngle = System.Math.Max(maxAngle, angle); maxSmooth = System.Math.Max(maxSmooth, smooth);
                maxV = MathF.Max(maxV, v); maxW = MathF.Max(maxW, w);
                Assert.True(position < 2e-5 && angle < 1e-6 && v < 1e-4 && w < 2e-5 && smooth < 1e-4,
                    $"{context} p={position:R} angle={angle:R} v={v:R} w={w:R} smooth={smooth:R}");
                if (island.IsSleeping) sleeping++;
            }
            cases++;
        }
        Assert.True(sleeping > 0 && woken > 0);
        output.WriteLine($"NATIVE_SLEEP_OK cases={cases} frames=60 sleeping={sleeping} woken={woken} max_p={maxP:R} max_angle={maxAngle:R} max_v={maxV:R} max_w={maxW:R} max_smooth={maxSmooth:R}");
    }
}
