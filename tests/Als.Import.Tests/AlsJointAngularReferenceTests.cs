using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsJointAngularReferenceTests
{
    [Fact]
    public void AngularKinematicsMatchLiveUeUtilitiesForBothNativeRigs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_joint_reference.json")));
        var count = 0;
        foreach (var mesh in doc.RootElement.GetProperty("meshes").EnumerateArray())
        foreach (var joint in mesh.GetProperty("joints").EnumerateArray())
        foreach (var sample in joint.GetProperty("samples").EnumerateArray())
        {
            var parent = Q(sample.GetProperty("parent")); var child = Q(sample.GetProperty("child"));
            var k = AlsJointAngularKinematics.Evaluate(parent,child);
            QuaternionNear(Q(sample.GetProperty("swing")),k.Swing);
            QuaternionNear(Q(sample.GetProperty("twist")),k.Twist);
            Near(V(sample.GetProperty("angles")),k.Angles);
            Near(V(sample.GetProperty("twistAxis")),k.TwistAxis);
            Near(V(sample.GetProperty("pyramidY")),k.PyramidY);
            Near(V(sample.GetProperty("pyramidZ")),k.PyramidZ);
            Near(V(sample.GetProperty("rotationLockX")),k.LockedX);
            Near(V(sample.GetProperty("rotationLockY")),k.LockedY);
            Near(V(sample.GetProperty("rotationLockZ")),k.LockedZ);
            Near(V(sample.GetProperty("driveError")),AlsJointAngularKinematics.SwingTwistDriveError(parent,child,
                Q(joint.GetProperty("settings").GetProperty("AngularDrivePositionTarget"))));
            count++;
        }
        Assert.Equal(912,count); // 38 joints, 24 orientations each, not 912 distinct orientations.
    }

    private static AlsQuaternion Q(JsonElement e) => new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble(),e[3].GetDouble());
    private static AlsDoubleVector V(JsonElement e) => new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static void Near(AlsDoubleVector expected, AlsDoubleVector actual)
    {
        Assert.InRange(Math.Abs(expected.X-actual.X),0,2e-10);
        Assert.InRange(Math.Abs(expected.Y-actual.Y),0,2e-10);
        Assert.InRange(Math.Abs(expected.Z-actual.Z),0,2e-10);
    }
    private static void QuaternionNear(AlsQuaternion expected, AlsQuaternion actual)
    {
        Near(new(expected.X,expected.Y,expected.Z),new(actual.X,actual.Y,actual.Z));
        Assert.InRange(Math.Abs(expected.W-actual.W),0,2e-10);
    }
}
