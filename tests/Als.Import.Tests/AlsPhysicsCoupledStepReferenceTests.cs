using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsCoupledStepReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_physics_coupled_step_reference.json", 6)]
    [InlineData("v4_physics_coupled_shock_reference.json", 5)]
    [InlineData("v4_physics_high_drop_coupled_reference.json", 6)]
    [InlineData("v4_physics_resting_coupled_reference.json", 6)]
    [InlineData("v4_physics_free_coupled_reference.json", 22)]
    [InlineData("v4_physics_window_coupled_reference.json", 8)]
    [InlineData("v4_physics_first_steps_coupled_reference.json", 6)]
    public void FullChainSharedContactJointAndProjectionStagesMatchNativeContainers(string file, int caseCount)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/" + file)));
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(caseCount, cases.GetArrayLength());
        double maxDp = 0, maxDq = 0, maxV = 0, maxW = 0, maxPosition = 0, maxRotation = 0;
        double maxCapturedDp = 0, maxCapturedDq = 0, maxCapturedV = 0, maxCapturedW = 0;
        var shockPairs = 0; var environmentCases = 0;
        var errors = new List<string>(); var stages = 0;
        foreach (var fixture in cases.EnumerateArray())
        {
            var input = fixture.GetProperty("capture").GetProperty("input");
            var bodyRows = input.GetProperty("bodies").EnumerateArray().ToArray(); var count = bodyRows.Length;
            var jointRows = input.GetProperty("joints").EnumerateArray().ToArray();
            var contactRows = input.GetProperty("contacts").EnumerateArray().ToArray();
            if (contactRows.Any(c => new[] { "body0", "body1" }.Any(name =>
                bodyRows[c.GetProperty(name).GetInt32()].GetProperty("name").GetString()!.StartsWith("environment_", StringComparison.Ordinal))))
                environmentCases++;
            var dt = D(input, "dt"); var settings = input.GetProperty("solverSettings");
            var id = $"{input.GetProperty("mesh")}/{input.GetProperty("frame")}/{dt:R}";
            string? firstJointDifference = null;
            var tracedJoints = 0;
            var initial = bodyRows.Select(b => Pose(b.GetProperty("initial"))).ToArray();
            var predicted = bodyRows.Select(b => Pose(b.GetProperty("predicted"))).ToArray();
            var mass = bodyRows.Select(b => new AlsJointInverseMass(D(b, "inverseMass"), V(b, "inverseInertia"))).ToArray();
            var velocity = bodyRows.Select(b => new AlsProjectionVelocity(V(b, "v").ToSingle(), V(b, "w").ToSingle())).ToArray();
            var delta = new AlsProjectionDelta[count];
            var definitions = jointRows.Select(j => AlsCachedJointSettingsCompiler.IslandJoint(
                j.GetProperty("parent").GetInt32(), j.GetProperty("child").GetInt32(),
                Pose(j.GetProperty("parentFrame")), Pose(j.GetProperty("childFrame")), j.GetProperty("jointSettings"), settings)).ToArray();
            var joints = jointRows.Select((j, i) => definitions[i].ConnectivityOnly ? default :
                new AlsCachedJoint(Body(j, "parent"), Body(j, "child"), definitions[i].Angular, dt)).ToArray();
            if (file is "v4_physics_free_coupled_reference.json" or "v4_physics_window_coupled_reference.json")
            {
                Assert.Single(definitions, j => j.ConnectivityOnly);
                Assert.Equal(input.GetProperty("mesh").GetString()!.EndsWith(".AnimMan", StringComparison.Ordinal) ? 20 : 18, joints.Length);
            }
            var contacts = contactRows.Select(c =>
            {
                var a = c.GetProperty("body0").GetInt32(); var b = c.GetProperty("body1").GetInt32(); var m = c.GetProperty("material");
                if (input.TryGetProperty("contactShock", out _) && mass[a].Mass > 0 && mass[b].Mass > 0 &&
                    bodyRows[a].GetProperty("level").GetInt32() != bodyRows[b].GetProperty("level").GetInt32()) shockPairs++;
                var points = c.GetProperty("points").EnumerateArray().Select(p => new AlsContactPointInput(
                    V(p, "arm0").ToSingle(), V(p, "arm1").ToSingle(), V(p, "normal").ToSingle(),
                    V(p, "u").ToSingle(), V(p, "v").ToSingle(), V(p, "error").ToSingle(), (float)D(p, "targetVelocity"),
                    B(p, "disablePosition"), B(p, "disableVelocity"), B(p, "disableFriction"))).ToArray();
                var manifold = new AlsCachedContactManifold(points.Length);
                manifold.GatherRows(points, new((float)D(m, "staticFriction"), (float)D(m, "dynamicFriction"), (float)D(m, "velocityFriction"),
                    (float)D(m, "minFrictionPushOut"), (float)D(m, "stiffness"), (float)D(m, "positionFrictionStiffness"),
                    (float)D(m, "velocityFrictionStiffness")), predicted[a].Rotation, mass[a], predicted[b].Rotation, mass[b], fromNativeGather: true);
                return (a, b, manifold);
            }).ToArray();
            var samples = fixture.GetProperty("nativeSamples"); Assert.Equal(24, samples.GetArrayLength()); var sampleIndex = 0;
            for (var iteration = 0; iteration < 8; iteration++)
            {
                foreach (var c in contacts) { SetShock(c, iteration, 8, "position"); c.manifold.SolvePosition(ref delta[c.a], ref delta[c.b], iteration >= 4); }
                Check("position_contacts", iteration);
                for (var j = 0; j < joints.Length; j++)
                {
                    if (!definitions[j].ConnectivityOnly)
                        joints[j].SolvePosition(ref delta[Index(j, "parent")], ref delta[Index(j, "child")]);
                    if (iteration == 0 && fixture.TryGetProperty("firstIterationJoints", out var trace))
                    {
                        var step = trace[j]; Assert.Equal(j, step.GetProperty("jointIndex").GetInt32());
                        Assert.Equal("position_joints", step.GetProperty("stage").GetString());
                        Assert.Equal(0, step.GetProperty("iteration").GetInt32());
                        double dp = 0, dq = 0;
                        for (var b = 0; b < count; b++)
                        {
                            dp = Math.Max(dp, Vector3.Distance(delta[b].Position, V(step.GetProperty("bodies")[b], "dp").ToSingle()));
                            dq = Math.Max(dq, Vector3.Distance(delta[b].Rotation, V(step.GetProperty("bodies")[b], "dq").ToSingle()));
                        }
                        Assert.InRange(dp, 0, 2e-8); Assert.InRange(dq, 0, 3e-9);
                        tracedJoints++;
                        if (firstJointDifference is null && (dp != 0 || dq != 0))
                            firstJointDifference = $"{id} joint={j} child={bodyRows[Index(j, "child")].GetProperty("name")} dp={dp:R} dq={dq:R}";
                    }
                }
                Check("position_joints", iteration);
            }
            for (var b = 0; b < count; b++) velocity[b] = AlsCachedJoint.AddImplicitVelocity(velocity[b], delta[b], dt, mass[b].Mass > 0);
            Check("implicit", 0);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                foreach (var c in contacts) { SetShock(c, iteration, 2, "velocity"); c.manifold.SolveVelocity(ref velocity[c.a], ref velocity[c.b], (float)dt, iteration >= 1); }
                Check("velocity_contacts", iteration);
                for (var j = 0; j < joints.Length; j++) if (!definitions[j].ConnectivityOnly)
                    joints[j].SolveVelocity(ref velocity[Index(j, "parent")], ref velocity[Index(j, "child")]);
                Check("velocity_joints", iteration);
            }
            Correct(); Check("projection_input", 0);
            var projections = jointRows.Select((j, index) =>
            {
                if (definitions[index].ConnectivityOnly) return default(AlsLockedLinearProjection);
                var p = j.GetProperty("parent").GetInt32(); var c = j.GetProperty("child").GetInt32();
                var cfg = AlsCachedJointSettingsCompiler.Projection(j.GetProperty("jointSettings"), settings);
                return new AlsLockedLinearProjection(predicted[p], predicted[c], Pose(j.GetProperty("parentFrame")),
                    Pose(j.GetProperty("childFrame")), (float)mass[c].Mass, mass[c].Inertia.ToSingle(),
                    (float)D(j.GetProperty("jointSettings"), "Stiffness"), cfg.LinearAlpha, cfg.TeleportDistance, cfg.Enabled);
            }).ToArray();
            for (var j = 0; j < joints.Length; j++)
            {
                if (definitions[j].ConnectivityOnly) continue;
                var child = Index(j, "child"); var added = projections[j].Apply(delta[Index(j, "parent")], ref delta[child], dt, .1f);
                velocity[child] = new(velocity[child].Linear + added.Linear, velocity[child].Angular + added.Angular);
            }
            Check("projection", 0); Correct(); Check("corrected", 0); Assert.Equal(24, sampleIndex);
            if (fixture.TryGetProperty("firstIterationJoints", out var jointTrace))
            {
                Assert.Equal(joints.Length, jointTrace.GetArrayLength()); Assert.Equal(joints.Length, tracedJoints);
                output.WriteLine($"NATIVE_FIRST_JOINT_DIFFERENCE {firstJointDifference ?? id + " exact"}");
            }

            int Index(int j, string name) => jointRows[j].GetProperty(name).GetInt32();
            void SetShock((int a, int b, AlsCachedContactManifold manifold) c, int iteration, int total, string phase)
            {
                if (!input.TryGetProperty("contactShock", out var shock)) return;
                c.manifold.SetShockPropagation(bodyRows[c.a].GetProperty("level").GetInt32(), bodyRows[c.b].GetProperty("level").GetInt32(),
                    iteration >= total - shock.GetProperty(phase + "Iterations").GetInt32() ? (float)D(shock, phase + "Scale") : 1);
            }
            AlsJointBodyInput Body(JsonElement j, string name)
            { var index = j.GetProperty(name).GetInt32(); return new(initial[index], predicted[index], Pose(j.GetProperty(name + "Frame")), mass[index]); }
            void Correct()
            {
                for (var b = 0; b < count; b++) { if (mass[b].Mass > 0) predicted[b] = AlsLockedLinearProjection.Correct(predicted[b], delta[b]); delta[b] = default; }
            }
            void Check(string stage, int iteration)
            {
                var sample = samples[sampleIndex++]; stages++;
                Assert.Equal(stage, sample.GetProperty("stage").GetString()); Assert.Equal(iteration, sample.GetProperty("iteration").GetInt32());
                var states = sample.GetProperty("bodies"); Assert.Equal(count, states.GetArrayLength());
                for (var b = 0; b < count; b++)
                {
                    var e = states[b]; var context = $"{id} {stage}/{iteration} {bodyRows[b].GetProperty("name")}";
                    Compare(Vector3.Distance(delta[b].Position, V(e, "dp").ToSingle()), 2e-5, "DP", ref maxDp);
                    Compare(Vector3.Distance(delta[b].Rotation, V(e, "dq").ToSingle()), 3e-6, "DQ", ref maxDq);
                    Compare(Vector3.Distance(velocity[b].Linear, V(e, "v").ToSingle()), 3e-3, "V", ref maxV);
                    Compare(Vector3.Distance(velocity[b].Angular, V(e, "w").ToSingle()), 5e-4, "W", ref maxW);
                    // Verify the production island's captured stage as well as
                    // this fresh Core replay; neither supplies native outputs.
                    var captured = fixture.GetProperty("capture").GetProperty("coreSamples")[sampleIndex - 1].GetProperty("bodies")[b];
                    Compare(Vector3.Distance(V(captured, "dp").ToSingle(), V(e, "dp").ToSingle()), 2e-5, "captured DP", ref maxCapturedDp);
                    Compare(Vector3.Distance(V(captured, "dq").ToSingle(), V(e, "dq").ToSingle()), 3e-6, "captured DQ", ref maxCapturedDq);
                    Compare(Vector3.Distance(V(captured, "v").ToSingle(), V(e, "v").ToSingle()), 3e-3, "captured V", ref maxCapturedV);
                    Compare(Vector3.Distance(V(captured, "w").ToSingle(), V(e, "w").ToSingle()), 5e-4, "captured W", ref maxCapturedW);
                    var pose = Pose(e.GetProperty("predicted"));
                    Compare((predicted[b].Position - pose.Position).ToSingle().Length(), 2e-5, "P", ref maxPosition);
                    // Captured particle rotations have float-storage norm error.
                    // Normalize both operands when measuring orientation; dot(q,q)
                    // must not report a difference between identical rotations.
                    Compare(System.Math.Abs(1 - System.Math.Abs(AlsQuaternion.Dot(predicted[b].Rotation.Normalized(), pose.Rotation.Normalized()))), 1e-10, "Qdot", ref maxRotation);
                    void Compare(double error, double tolerance, string field, ref double maximum)
                    {
                        maximum = System.Math.Max(maximum, error);
                        if ((!double.IsFinite(error) || error > tolerance) && errors.Count < 30) errors.Add($"{context} {field} error={error:R} tolerance={tolerance:R}");
                    }
                }
            }
        }
        output.WriteLine($"NATIVE_COUPLED_STEP file={file} cases={caseCount} stages={stages} max_dp={maxDp:R} max_dq={maxDq:R} max_v={maxV:R} max_w={maxW:R} max_p={maxPosition:R} max_qdot={maxRotation:R}");
        output.WriteLine($"CAPTURED_COUPLED_STEP shock_pairs={shockPairs} max_dp={maxCapturedDp:R} max_dq={maxCapturedDq:R} max_v={maxCapturedV:R} max_w={maxCapturedW:R}");
        if (file == "v4_physics_coupled_shock_reference.json") Assert.True(shockPairs > 0, "Real captures must exercise dynamic contacts at different graph levels.");
        if (file == "v4_physics_free_coupled_reference.json") Assert.Equal(10, environmentCases);
        if (file == "v4_physics_window_coupled_reference.json") Assert.Equal(caseCount, environmentCases);
        if (file == "v4_physics_first_steps_coupled_reference.json")
        {
            // Keep the independent first-step reference sensitive enough to catch
            // .NET 9 fused Cross rounding. Historical captures remain unchanged;
            // these limits apply to the fresh production-kernel replay above.
            Assert.InRange(maxDp, 0, 1e-8); Assert.InRange(maxW, 0, 4e-7);
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
