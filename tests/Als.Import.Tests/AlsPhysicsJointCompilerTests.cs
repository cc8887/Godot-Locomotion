using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsJointCompilerTests
{
    private static string Source() => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_joint_reference.json"));
    private static AlsRagdollPhysicsDefinition Rig(string name) => AlsPhysicsAssetCompiler.Compile(
        File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json")),
        AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);

    [Theory]
    [InlineData("Mannequin",18)] [InlineData("AnimMan",20)]
    public void EffectiveSettingsRetainNativeUnitsAxisOrderAndFreeRoot(string name,int count)
    {
        var rig = Rig(name); var result = AlsPhysicsJointCompiler.Compile(Source(),rig);
        Assert.Equal(count,result.Length);
        var spine = result.Single(s => rig.Bodies[rig.Joints[s.Index].ChildBody].Bone == "spine_02");
        // Asset panel stiffness=50,damping=5; effective Chaos angular soft values
        // include conversion/scaling. Drive values use a different conversion.
        Assert.Equal(5000000,spine.SwingSoftLimit.Stiffness); Assert.Equal(5000,spine.SwingSoftLimit.Damping);
        Assert.Equal(new AlsDoubleVector(75,75,75),spine.AngularDrive.Stiffness);
        Assert.Equal(new AlsDoubleVector(1.5,1.5,1.5),spine.AngularDrive.Damping);
        Assert.Equal(AlsJointForceMode.Acceleration,spine.AngularDrive.ForceMode);
        Assert.Equal(AlsJointForceMode.Acceleration,spine.AngularSoftForceMode);
        Assert.InRange(spine.AngularLimitsRad.X,10*Math.PI/180-1e-7,10*Math.PI/180+1e-7);
        Assert.InRange(spine.AngularLimitsRad.Y,15*Math.PI/180-1e-7,15*Math.PI/180+1e-7);
        Assert.InRange(spine.AngularLimitsRad.Z,25*Math.PI/180-1e-7,25*Math.PI/180+1e-7);
        Assert.True(spine.Projection.Enabled); Assert.Equal(5,spine.Projection.TeleportDistanceCm);
        Assert.True(spine.AngularDrive.TwistPosition && spine.AngularDrive.TwistVelocity &&
            spine.AngularDrive.SwingPosition && spine.AngularDrive.SwingVelocity);
        var root = result.Single(s => rig.Bodies[rig.Joints[s.Index].ParentBody].Bone == "root");
        Assert.Equal(new(AlsJointMotion.Free,AlsJointMotion.Free,AlsJointMotion.Free),root.LinearMotion);
        Assert.Equal(new(AlsJointMotion.Free,AlsJointMotion.Free,AlsJointMotion.Free),root.AngularMotion);
        Assert.Equal((double)float.MaxValue,root.AngularLimitsRad.X);
        var hand = result.Single(s => rig.Bodies[rig.Joints[s.Index].ChildBody].Bone == "hand_l");
        Assert.Equal(name == "Mannequin" ? AlsJointMotion.Locked : AlsJointMotion.Limited,hand.AngularMotion.X);
        // The cloned JSON remains usable after the compiler disposes its document.
        Assert.True(spine.NativeSettings.GetProperty("bUseLinearSolver").GetBoolean());
    }

    [Fact]
    public void ReorderedNativeRowsStillBindToTheCorrectBodyFrames()
    {
        var rig = Rig("Mannequin"); var baseline = AlsPhysicsJointCompiler.Compile(Source(),rig);
        var source = JsonNode.Parse(Source())!; var rows = source["meshes"]![0]!["joints"]!.AsArray();
        var reversed = rows.Select(j => j!.DeepClone()).Reverse().ToArray(); rows.Clear();
        foreach (var row in reversed) rows.Add(row);
        var compiled = AlsPhysicsJointCompiler.Compile(source.ToJsonString(),rig);
        for (var i = 0; i < compiled.Length; i++)
        {
            Assert.Equal(baseline[i].Index,compiled[i].Index);
            Assert.Equal(baseline[i].AngularMotion,compiled[i].AngularMotion);
            Assert.Equal(baseline[i].AngularLimitsRad,compiled[i].AngularLimitsRad);
        }
    }

    [Theory]
    [InlineData("schema")] [InlineData("units")] [InlineData("engine")]
    [InlineData("asset")] [InlineData("duplicateMesh")] [InlineData("duplicateJoint")]
    [InlineData("missingJoint")] [InlineData("body")] [InlineData("solver")]
    [InlineData("motion")] [InlineData("axisCount")] [InlineData("limit")]
    [InlineData("drive")] [InlineData("target")] [InlineData("forceMode")]
    [InlineData("projection")] [InlineData("missingField")]
    public void InvalidOrUnsupportedJointDataCannotSilentlyFallBack(string mutation)
    {
        var source = JsonNode.Parse(Source())!; var mesh = source["meshes"]![0]!;
        var joints = mesh["joints"]!.AsArray(); var joint = joints[1]!; var s = joint["settings"]!;
        switch (mutation)
        {
            case "schema": source["schemaVersion"] = 2; break;
            case "units": source["coordinates"] = "meters, degrees"; break;
            case "engine": source["engine"] = "5.8.0-other"; break;
            case "asset": mesh["physicsAsset"] = "different"; break;
            case "duplicateMesh": source["meshes"]!.AsArray().Add(mesh.DeepClone()); break;
            case "duplicateJoint": joints.Add(joint.DeepClone()); break;
            case "missingJoint": joints.RemoveAt(0); break;
            case "body": joint["parent"] = "head"; break;
            case "solver": s["bUseLinearSolver"] = false; break;
            case "motion": s["AngularMotionTypes"]![0] = 3; break;
            case "axisCount": s["LinearMotionTypes"]!.AsArray().RemoveAt(2); break;
            case "limit": s["AngularLimits"]![0] = 4; break;
            case "drive": s["AngularDriveDamping"]![1] = -1; break;
            case "target": s["AngularDrivePositionTarget"]![3] = 0; break;
            case "forceMode": s["AngularDriveForceMode"] = 2; break;
            case "projection": s["LinearProjection"] = 1.1; break;
            case "missingField": s.AsObject().Remove("SoftSwingStiffness"); break;
        }
        Assert.Throws<InvalidDataException>(() => AlsPhysicsJointCompiler.Compile(source.ToJsonString(),Rig("Mannequin")));
    }
}
