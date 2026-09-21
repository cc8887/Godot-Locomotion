using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCachedJointSettingsCompilerTests
{
    [Fact]
    public void BothAssetsKeepTheirFreePelvisRootConnectionAndRejectLostDriveRows()
    {
        using var source = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_joint_reference.json")));
        var solver = Element(Source()["solverSettings"]!); var count = 0;
        foreach (var mesh in source.RootElement.GetProperty("meshes").EnumerateArray())
        foreach (var row in mesh.GetProperty("joints").EnumerateArray())
        {
            var settings = row.GetProperty("settings");
            if (settings.GetProperty("LinearMotionTypes").EnumerateArray().Any(e => e.GetInt32() != 0)) continue;
            Assert.Equal("pelvis", row.GetProperty("child").GetString()); Assert.Equal("root", row.GetProperty("parent").GetString());
            var joint = AlsCachedJointSettingsCompiler.IslandJoint(0, 1, AlsPrecisePose.Identity, AlsPrecisePose.Identity, settings, solver);
            Assert.True(joint.ConnectivityOnly); Assert.Equal(default, joint.Angular); Assert.False(joint.Projection.Enabled); count++;
            var changed = JsonNode.Parse(settings.GetRawText())!; changed["bAngularTwistPositionDriveEnabled"] = true;
            Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.IslandJoint(0, 1, AlsPrecisePose.Identity, AlsPrecisePose.Identity, Element(changed), solver));
        }
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("bAngularSLerpPositionDriveEnabled", "true")]
    [InlineData("bAngularSLerpVelocityDriveEnabled", "true")]
    [InlineData("AngularDriveVelocityTarget", "[1,0,0]")]
    [InlineData("AngularDriveMaxTorque", "[10,0,0]")]
    [InlineData("AngularDriveForceMode", "3")]
    [InlineData("AngularMotionTypes", "[1,1,1,1]")]
    [InlineData("LinearMotionTypes", "[1,2,2]")]
    [InlineData("bLinearPositionDriveEnabled", "[false,true,false]")]
    [InlineData("ParentInvMassScale", "0.5")]
    [InlineData("bShockPropagationEnabled", "true")]
    [InlineData("TwistRestitution", "0.1")]
    public void UnsupportedJointFeaturesAreRejectedInsteadOfDropped(string key, string value)
    {
        var row = Source(); var joint = row["jointSettings"]!;
        joint[key] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.Angular(Element(joint), Element(row["solverSettings"]!)));
    }

    [Theory]
    [InlineData("bEnableDrives", "false")]
    [InlineData("bSolvePositionLast", "false")]
    [InlineData("bUsePositionBasedDrives", "false")]
    [InlineData("NumShockPropagationIterations", "1")]
    [InlineData("MaxSolverStiffness", "0.5")]
    [InlineData("AngularDriveDampingOverride", "0")]
    public void UnsupportedSolverPolicyIsRejectedInsteadOfDropped(string key, string value)
    {
        var row = Source(); var solver = row["solverSettings"]!;
        solver[key] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.Angular(Element(row["jointSettings"]!), Element(solver)));
    }

    [Fact]
    public void AngularOrScalarProjectionMustNotUseTheSimdLinearProjectionPath()
    {
        var row = Source(); var joint = row["jointSettings"]!; var solver = row["solverSettings"]!;
        joint["AngularProjection"] = 1;
        Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.Projection(Element(joint), Element(solver)));
        joint["AngularProjection"] = 0; solver["bUseSimd"] = false;
        Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.Projection(Element(joint), Element(solver)));
    }

    [Fact]
    public void RigidConnectorDropsOnlyRoundoffScaleAndPreservesNativeLocationRotation()
    {
        var pose = new AlsPrecisePose(new(1, 2, 3), AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitY, .4f), new(1.00000002, 1, .99999999));
        Assert.Equal(pose with { Scale = AlsDoubleVector.One }, AlsCachedJointSettingsCompiler.RigidConnector(pose));
        Assert.Throws<InvalidDataException>(() => AlsCachedJointSettingsCompiler.RigidConnector(pose with { Scale = new(1.1, 1, 1) }));
    }

    private static JsonNode Source()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_awake_solver_reference.json")));
        return JsonNode.Parse(document.RootElement.GetProperty("cases")[0].GetRawText())!;
    }
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
