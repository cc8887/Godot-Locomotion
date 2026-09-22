using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsJointFrameCompilerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
    private static AlsRagdollPhysicsDefinition Authored(string name = "AnimMan") => AlsPhysicsAssetCompiler.Compile(
        Read("v4_physics_asset_inputs"), AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);

    [Fact]
    public void CreationScaleReconstructsEveryIndependentlyObservedConnector()
    {
        var json = Read("v4_physics_joint_frame_inputs"); using var doc = JsonDocument.Parse(json);
        var count = 0; var changed = 0;
        foreach (var rig in doc.RootElement.GetProperty("rigs").EnumerateArray())
        {
            var authored = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"), rig.GetProperty("mesh").GetString()!);
            var result = AlsPhysicsJointFrameCompiler.Compile(json, authored);
            var previousChanged = changed;
            Assert.Same(authored.Bodies, result.Bodies);
            foreach (var joint in result.Joints)
            {
                var native = rig.GetProperty("joints")[joint.Index];
                Check(joint.ChildFrame, authored.Joints[joint.Index].ChildFrame, "Child");
                Check(joint.ParentFrame, authored.Joints[joint.Index].ParentFrame, "Parent");
                void Check(AlsPrecisePose actual, AlsPrecisePose original, string end)
                {
                    Assert.Equal(AlsPhysicsJointFrameCompiler.Pose(native.GetProperty("instance" + end + "Frame")), actual);
                    var solver = AlsPhysicsJointFrameCompiler.Pose(native.GetProperty("actual" + end + "Frame"));
                    Assert.Equal(solver.Position, actual.Position); Assert.Equal(solver.Rotation, actual.Rotation);
                    if (actual.Position != original.Position) changed++;
                    count++;
                }
            }
            if (changed != previousChanged)
                Assert.Throws<InvalidDataException>(() => AlsPhysicsJointFrameCompiler.Compile(json, result));
            else
                Assert.Equal(result.Joints, AlsPhysicsJointFrameCompiler.Compile(json, result).Joints);
        }
        output.WriteLine($"NATIVE_JOINT_FRAMES connectors={count} corrected={changed}");
        Assert.Equal(76, count); Assert.Equal(8, changed);
    }

    [Fact]
    public void RebuiltFramesProduceNativeWholeBodyConditionedInertia()
    {
        using var doc = JsonDocument.Parse(Read("v4_physics_world_step_window"));
        using var inertiaDoc = JsonDocument.Parse(Read("v4_physics_inertia_reference"));
        var count = 0; float maxDifference = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var authored = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"), row.GetProperty("setup").GetProperty("mesh").GetString()!);
            var definition = AlsPhysicsJointFrameCompiler.Compile(Read("v4_physics_joint_frame_inputs"), authored);
            var joints = AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference"), definition);
            var inertia = AlsBodyInertiaCompiler.Compile(Read("v4_physics_inertia_reference"), definition, joints);
            var native = row.GetProperty("samples")[1].GetProperty("stepObservations")[2].GetProperty("bodies");
            foreach (var body in definition.Bodies.Where(b => b.PhysicsType != 1))
            {
                var raw = inertia[body.Index].RawInverseInertia;
                var value = native[body.Index].GetProperty("conditionedInverseInertia");
                var expected = new Vector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
                var actual = inertia[body.Index].ConditionedInverseInertia;
                var difference = Vector3.Distance(expected, actual); maxDifference = Math.Max(maxDifference, difference);
                if (difference != 0)
                {
                    var reference = inertiaDoc.RootElement.GetProperty("rigs").EnumerateArray().Single(r =>
                        r.GetProperty("mesh").GetString() == definition.Mesh && r.GetProperty("isolatedChild").GetString() == "")
                        .GetProperty("bodies").EnumerateArray().Single(b => b.GetProperty("bone").GetString() == body.Bone);
                    static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
                    output.WriteLine($"INERTIA_DIFF {definition.Mesh.Split('.').Last()}/{body.Bone} actual={actual} native={expected} raw={raw} native_raw={V(reference.GetProperty("inverseInertia"))} extents={inertia[body.Index].ExtentsCm.ToSingle()} native_extents={V(reference.GetProperty("constraintExtents"))} scale={inertia[body.Index].InverseInertiaScale} native_scale={V(reference.GetProperty("actualScale"))}");
                }
                count++;
            }
        }
        output.WriteLine($"NATIVE_JOINT_FRAME_INERTIA bodies={count} max_difference={maxDifference:R}");
        Assert.Equal(38, count); Assert.Equal(0, maxDifference);
    }

    [Theory]
    [InlineData("schema")] [InlineData("units")] [InlineData("asset")] [InlineData("mass")]
    [InlineData("topology")] [InlineData("frame")] [InlineData("defaultScale")] [InlineData("instanceScale")]
    [InlineData("componentScale")]
    public void StaleOrUnsupportedCreationInputsAreRejected(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_physics_joint_frame_inputs"))!; var rig = root["rigs"]![1]!;
        switch (mutation)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "units": root["coordinates"] = "meters"; break;
            case "asset": rig["physicsAsset"] = "/Game/Wrong"; break;
            case "mass": rig["bodies"]![0]!["nativeMassKg"] = 99; break;
            case "topology": rig["joints"]![0]!["parentBone"] = "Wrong"; break;
            case "frame": rig["joints"]![0]!["authoredParentFrame"]!["translation"]![0] = 99; break;
            case "defaultScale": rig["bodies"]![0]!["defaultScale"]![0] = 0; break;
            case "instanceScale": rig["bodies"]![0]!["instanceScale"]![0] = 2; break;
            case "componentScale": rig["componentScale"]![0] = 2; break;
        }
        Assert.Throws<InvalidDataException>(() => AlsPhysicsJointFrameCompiler.Compile(root.ToJsonString(), Authored()));
    }
}
