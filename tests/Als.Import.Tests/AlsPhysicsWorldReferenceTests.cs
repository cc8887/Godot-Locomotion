using System.Text.Json;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

// Validates the independently simulated target, not Core trajectory parity.
public sealed class AlsPhysicsWorldReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_physics_world_reference.json", 4, false)]
    [InlineData("v4_physics_world_low_frequency_reference.json", 2, true)]
    public void CompleteNativeAssetsContactCapturedSceneAndPreserveObservedSleepBudget(string file, int caseCount, bool lowFrequency)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file)));
        var source = File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json"));
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(caseCount, cases.GetArrayLength());
        var ids = new HashSet<string>(); double maxQComponent = 0, maxInertia = 0;
        foreach (var row in cases.EnumerateArray())
        {
            var setup = row.GetProperty("setup"); var mesh = setup.GetProperty("mesh").GetString()!;
            var definition = AlsPhysicsAssetCompiler.Compile(source, mesh);
            var hz = setup.GetProperty("hz").GetInt32(); var steps = setup.GetProperty("steps").GetInt32();
            Assert.True(ids.Add($"{mesh}/{hz}")); Assert.Equal(hz == 120, B(setup, "highDrop"));
            Assert.True(lowFrequency ? hz == 30 : hz is 60 or 120);
            Assert.Equal((double)(1f / hz), D(row, "dtUsed")); Assert.Equal(10 * hz, steps);
            var input = setup.GetProperty("bodies"); var samples = row.GetProperty("samples");
            Assert.Equal(definition.Bodies.Length, input.GetArrayLength()); Assert.Equal(steps + 1, samples.GetArrayLength());
            Assert.Equal(13, setup.GetProperty("environment").GetArrayLength());
            var initial = samples[0].GetProperty("bodies");
            for (var i = 0; i < input.GetArrayLength(); i++)
            {
                Assert.Equal(definition.Bodies[i].Bone, input[i].GetProperty("name").GetString());
                Assert.Equal(input[i].GetProperty("name").GetString(), initial[i].GetProperty("name").GetString());
                var desired = input[i].GetProperty("actor"); var actual = initial[i].GetProperty("world");
                Assert.Equal(V(desired, "position"), V(actual, "position"));
                Assert.Equal(V(input[i], "v"), V(initial[i], "linearVelocity"));
                Assert.InRange((V(input[i], "w") - V(initial[i], "angularVelocity")).LengthSquared, 0, 1e-12);
                for (var axis = 0; axis < 4; axis++) maxQComponent = Math.Max(maxQComponent,
                    Math.Abs(desired.GetProperty("rotation")[axis].GetDouble() - actual.GetProperty("rotation")[axis].GetDouble()));
                if (B(input[i], "dynamic")) maxInertia = Math.Max(maxInertia,
                    Math.Sqrt((V(input[i], "inverseInertia") - V(samples[1].GetProperty("bodies")[i], "conditionedInverseInertia")).LengthSquared));
            }
            var contactSeen = false; var held = 0; var firstHeld = -1;
            for (var frame = 1; frame <= steps; frame++)
            {
                var s = samples[frame]; Assert.Equal(frame, s.GetProperty("frame").GetInt32());
                Assert.Equal(definition.Joints.Length, s.GetProperty("linearJoints").GetInt32());
                Assert.Equal(0, s.GetProperty("nonlinearJoints").GetInt32());
                contactSeen |= s.GetProperty("contactPairs").GetInt32() > 0;
                Assert.Equal(input.GetArrayLength(), s.GetProperty("bodies").GetArrayLength());
                var awake = 0;
                for (var i = 0; i < input.GetArrayLength(); i++)
                {
                    var b = s.GetProperty("bodies")[i];
                    Assert.True(V(b.GetProperty("world"), "position").IsFinite);
                    Assert.True(V(b, "linearVelocity").IsFinite); Assert.True(V(b, "angularVelocity").IsFinite);
                    if (B(input[i], "dynamic") && B(b, "awake")) awake++;
                }
                Assert.Equal(awake, s.GetProperty("awakeBodies").GetInt32());
                if (awake == 0) { if (held == 0) firstHeld = frame; held++; } else { held = 0; firstHeld = -1; }
            }
            Assert.True(contactSeen); Assert.Equal(held, row.GetProperty("heldSleepingFrames").GetInt32());
            Assert.Equal(firstHeld, row.GetProperty("allSleepFrame").GetInt32());
            Assert.Equal(held >= hz, B(row, "oneSecondSleepBudget"));
            // Preserve the native failure as evidence; do not make a ten-second
            // sleep expectation true by filtering out the still-awake asset.
            var nativeFailure = lowFrequency && mesh.EndsWith(".Mannequin", StringComparison.Ordinal);
            Assert.Equal(!nativeFailure, held >= hz);
            double lastV = 0, lastW = 0;
            for (var frame = steps - hz; frame <= steps; frame++)
                foreach (var body in samples[frame].GetProperty("bodies").EnumerateArray())
                {
                    lastV = Math.Max(lastV, Math.Sqrt(V(body, "linearVelocity").LengthSquared));
                    lastW = Math.Max(lastW, Math.Sqrt(V(body, "angularVelocity").LengthSquared));
                }
            Assert.InRange(Math.Abs(lastV - D(row, "lastSecondMaxLinear")), 0, 1e-12);
            Assert.InRange(Math.Abs(lastW - D(row, "lastSecondMaxAngular")), 0, 1e-12);
            if (nativeFailure)
            {
                Assert.Equal(-1, firstHeld); Assert.Equal(0, held);
                Assert.Equal(18, samples[steps].GetProperty("awakeBodies").GetInt32());
                Assert.InRange(lastV, 6.98, 7.00); Assert.InRange(lastW, .40, .41);
            }
            else { Assert.Equal(0, lastV); Assert.Equal(0, lastW); }
            output.WriteLine($"NATIVE_WORLD mesh={mesh.Split('.').Last()} hz={hz} sleep={firstHeld} held={held} joints={definition.Joints.Length}");
        }
        // Native particle quaternion storage is float; positions and linear
        // velocities above are exact. Inertia is measured, not substituted.
        Assert.InRange(maxQComponent, 0, 2e-7);
        output.WriteLine($"INITIAL_BOUNDARY maxQuaternionComponent={maxQComponent:R} maxConditionedInertia={maxInertia:R}; Core trajectory parity not asserted");
    }
}
