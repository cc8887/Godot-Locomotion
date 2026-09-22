using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsJointMotorTargetReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ActualNativeFlailMotorTargetsUseAuthoredFramesAndGraphicsParentChain()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_motor_targets.json")));
        var total = 0; var checkedTargets = 0; var intermediateParents = 0; double maximum = 0;
        foreach (var rig in document.RootElement.GetProperty("rigs").EnumerateArray())
        {
            var authored = AlsPhysicsAssetCompiler.Compile(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
                "assets/config/v4_physics_asset_inputs.json")), rig.GetProperty("mesh").GetString()!);
            Assert.Equal(authored.PhysicsAsset, rig.GetProperty("physicsAsset").GetString());
            var settings = AlsPhysicsJointCompiler.Compile(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
                "assets/config/v4_physics_joint_reference.json")), authored);
            var inputs = new AlsRagdollMotorInputs(authored, settings, 1.5f, 1.5f);
            var targets = settings.Select(s => s.AngularDrive.Target).ToArray();
            var submitted = new AlsIslandAngularDrive[inputs.OutputCount];
            var posesSeen = new HashSet<string>();
            foreach (var sample in rig.GetProperty("motorSamples").EnumerateArray())
            {
                var bones = sample.GetProperty("bones");
                posesSeen.Add(bones.GetRawText());
                var names = bones.EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToArray();
                var parents = bones.EnumerateArray().Select(b => b.GetProperty("parent").GetInt32()).ToArray();
                Assert.Equal(authored.Bones.Select(b => b.Name), names);
                Assert.Equal(authored.Bones.Select(b => b.Parent), parents);
                var locals = bones.EnumerateArray().Select(b => Transform(b.GetProperty("local"))).ToArray();
                inputs.EvaluateParameters(locals, targets, (float)D(sample, "spring"), (float)D(sample, "damping"), submitted);
                var submittedIndex = 0;
                foreach (var motor in sample.GetProperty("motors").EnumerateArray())
                {
                    total++;
                    // SetAllMotorsAngularDriveParams passes the raw spring and
                    // damping through the engine's 1.5 scale, including disabled
                    // channels. Enable flags remain a separate input to solver.
                    var k = V(motor, "stiffness"); var c = V(motor, "damping");
                    Assert.Equal(AlsDoubleVector.One * (1.5 * D(sample, "spring")), k);
                    Assert.Equal(AlsDoubleVector.One * (1.5 * D(sample, "damping")), c);
                    Assert.Equal(B(motor, "twistPosition") || B(motor, "swingPosition"), B(motor, "orientationEnabled"));
                    if (B(motor, "twistPosition") || B(motor, "swingPosition") || B(motor, "twistVelocity") || B(motor, "swingVelocity"))
                    {
                        var step = submitted[submittedIndex++];
                        Assert.Equal(motor.GetProperty("index").GetInt32(), step.Joint);
                        Assert.Equal(new AlsDoubleVector(B(motor,"twistPosition") ? k.X : 0,
                            B(motor,"swingPosition") ? k.Y : 0, B(motor,"swingPosition") ? k.Z : 0), step.Stiffness);
                        Assert.Equal(new AlsDoubleVector(B(motor,"twistVelocity") ? c.X : 0,
                            B(motor,"swingVelocity") ? c.Y : 0, B(motor,"swingVelocity") ? c.Z : 0), step.Damping);
                        var nativeTarget = Transform(motor.GetProperty("target")).Rotation;
                        Assert.InRange(System.Math.Abs(AlsQuaternion.Dot(step.Target, nativeTarget) - 1), 0, 1e-12);
                        targets[step.Joint] = step.Target;
                    }
                    if (!B(motor, "orientationEnabled")) continue;
                    var joint = rig.GetProperty("joints")[motor.GetProperty("index").GetInt32()];
                    var definition = authored.Joints[motor.GetProperty("index").GetInt32()];
                    Assert.Equal(definition.ChildFrame, Transform(joint.GetProperty("authoredChildFrame")));
                    Assert.Equal(definition.ParentFrame, Transform(joint.GetProperty("authoredParentFrame")));
                    var child = Array.IndexOf(names, joint.GetProperty("childBone").GetString());
                    var parent = Array.IndexOf(names, joint.GetProperty("parentBone").GetString());
                    Assert.True(AlsJointMotorTarget.TryEvaluate(locals, parents, child, parent,
                        Transform(joint.GetProperty("authoredChildFrame")), Transform(joint.GetProperty("authoredParentFrame")), out var actual));
                    var expected = Transform(motor.GetProperty("target")).Rotation;
                    if (AlsQuaternion.Dot(actual, expected) < 0) actual = -actual;
                    var delta = actual + -expected;
                    maximum = System.Math.Max(maximum, System.Math.Max(System.Math.Max(System.Math.Abs(delta.X), System.Math.Abs(delta.Y)),
                        System.Math.Max(System.Math.Abs(delta.Z), System.Math.Abs(delta.W))));
                    checkedTargets++; if (parents[child] != parent) intermediateParents++;
                }
                Assert.Equal(inputs.OutputCount, submittedIndex);
            }
            Assert.True(posesSeen.Count >= 3, "Native Flail sampling did not change its animation pose.");
        }
        output.WriteLine($"NATIVE_MOTOR_TARGETS motors={total} targets={checkedTargets} intermediate_parents={intermediateParents} max_component={maximum:R}");
        Assert.Equal(190, total); Assert.Equal(180, checkedTargets); Assert.Equal(0, intermediateParents);
        Assert.InRange(maximum, 0, 1e-12);
    }
    private static AlsPrecisePose Transform(JsonElement value) => new(V(value, "translation"),
        Quaternion(value.GetProperty("rotation")), V(value, "scale"));
}
