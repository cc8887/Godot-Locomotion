using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsJointGatherReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void NativeConditionedMassAndIsolatedLinearResponseMatchCapturedInputs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_joint_gather_reference.json")));
        var checkedJoints = 0; double maxMass = 0, maxTensor = 0; float maxFloatTensor = 0, maxDp = 0, maxDq = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("input"); var bodies = input.GetProperty("bodies");
            var joints = input.GetProperty("joints"); var native = row.GetProperty("nativeJoints");
            Assert.Equal(joints.GetArrayLength(), native.GetArrayLength());
            for (var j = 0; j < joints.GetArrayLength(); j++)
            {
                var joint = joints[j]; var expected = native[j]; Assert.Equal(j, expected.GetProperty("jointIndex").GetInt32());
                var parent = bodies[joint.GetProperty("parent").GetInt32()]; var child = bodies[joint.GetProperty("child").GetInt32()];
                var settings = joint.GetProperty("jointSettings"); var solver = input.GetProperty("solverSettings");
                var p = Pose(parent.GetProperty("predicted")); var c = Pose(child.GetProperty("predicted"));
                var pm = new AlsJointInverseMass(D(parent, "inverseMass"), V(parent, "inverseInertia"));
                var cm = new AlsJointInverseMass(D(child, "inverseMass"), V(child, "inverseInertia"));
                var conditioned = AlsJointMassConditioning.Apply(pm, cm,
                    B(settings, "bMassConditioningEnabled") ? D(solver, "MinParentMassRatio") : 0,
                    B(settings, "bMassConditioningEnabled") ? D(solver, "MaxInertiaRatio") : 0);
                CheckMass("parentMass", conditioned.Parent, p.Rotation); CheckMass("childMass", conditioned.Child, c.Rotation);
                var deltaP = default(AlsProjectionDelta); var deltaC = default(AlsProjectionDelta);
                var definition = AlsCachedJointSettingsCompiler.IslandJoint(0, 1,
                    Pose(joint.GetProperty("parentFrame")), Pose(joint.GetProperty("childFrame")), settings, solver);
                if (!definition.ConnectivityOnly)
                {
                    var linear = new AlsCachedLinearJoint(p, c, definition.ParentFrame, definition.ChildFrame, pm, cm,
                        B(settings, "bMassConditioningEnabled"), B(solver, "bUseSimd"), D(settings, "Stiffness"),
                        D(solver, "MinParentMassRatio"), D(solver, "MaxInertiaRatio"));
                    linear.SolvePositions(ref deltaP, ref deltaC);
                }
                var dp = Math.Max(Vector3.Distance(deltaP.Position, V(expected.GetProperty("parentLinearDelta"), "dp").ToSingle()),
                    Vector3.Distance(deltaC.Position, V(expected.GetProperty("childLinearDelta"), "dp").ToSingle()));
                var dq = Math.Max(Vector3.Distance(deltaP.Rotation, V(expected.GetProperty("parentLinearDelta"), "dq").ToSingle()),
                    Vector3.Distance(deltaC.Rotation, V(expected.GetProperty("childLinearDelta"), "dq").ToSingle()));
                maxDp = Math.Max(maxDp, dp); maxDq = Math.Max(maxDq, dq);
                if (dp != 0 || dq != 0) output.WriteLine($"LINEAR_GATHER_DIFF {input.GetProperty("mesh")}/{input.GetProperty("frame")} joint={j} dp={dp:R} dq={dq:R}");
                checkedJoints++;
                void CheckMass(string end, AlsJointInverseMass mass, AlsQuaternion rotation)
                {
                    var value = expected.GetProperty(end); maxMass = Math.Max(maxMass, Math.Abs(D(value, "inverseMass") - mass.Mass));
                    var tensor = AlsJointInertiaTensor.World(rotation, mass); var columns = new[] { tensor.X, tensor.Y, tensor.Z };
                    var actual = value.GetProperty("inverseInertia");
                    for (var axis = 0; axis < 3; axis++)
                    {
                        var e = actual[axis]; var v = new AlsDoubleVector(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
                        maxTensor = Math.Max(maxTensor, Math.Sqrt((columns[axis] - v).LengthSquared));
                        maxFloatTensor = Math.Max(maxFloatTensor, Vector3.Distance(columns[axis].ToSingle(), v.ToSingle()));
                    }
                }
            }
        }
        output.WriteLine($"NATIVE_JOINT_GATHER joints={checkedJoints} mass={maxMass:R} tensor={maxTensor:R} float_tensor={maxFloatTensor:R} dp={maxDp:R} dq={maxDq:R}");
        Assert.Equal(114, checkedJoints); Assert.Equal(0, maxMass); Assert.Equal(0, maxFloatTensor);
        Assert.Equal(0, maxDp); Assert.Equal(0, maxDq);
    }
}
