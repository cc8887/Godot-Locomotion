using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsProjectionReferenceTests
{
    [Fact]
    public void ProjectionDeltasVelocitiesAndPosesMatchNativeContainerSolver()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_projection_reference.json")));
        var root=doc.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("exponentialRotationIntegration").GetBoolean());
        Assert.True(root.GetProperty("linearContainerSolver").GetBoolean());
        var velocityAlpha=F(root,"velocityProjectionAlpha");Assert.Equal(AlsLockedLinearProjection.ReferenceVelocityAlpha,velocityAlpha);
        var rows=root.GetProperty("cases");Assert.Equal(262,rows.GetArrayLength());
        var identities=new HashSet<string>();
        foreach(var row in rows.EnumerateArray())
        {
            var id=$"{row.GetProperty("hz")}/{row.GetProperty("axis")}/{row.GetProperty("offsetCm")}/{row.GetProperty("rotated")}/{row.GetProperty("parentCorrection")}/{row.GetProperty("mode")}";
            Assert.True(identities.Add(id));
            var initial=Pose(row.GetProperty("childPose"));
            var projection=new AlsLockedLinearProjection(Pose(row.GetProperty("parentPose")),initial,
                Pose(row.GetProperty("parentFrame")),Pose(row.GetProperty("childFrame")),F(row,"childInverseMass"),
                V(row,"childInverseInertia"),F(row,"stiffness"),F(row,"linearAlpha"),F(row,"teleportDistanceCm"),
                row.GetProperty("enabled").GetBoolean());
            var parent=new AlsProjectionDelta(V(row,"parentDP"),V(row,"parentDQ"));
            var child=default(AlsProjectionDelta);
            var velocity=projection.Apply(parent,ref child,row.GetProperty("dt").GetDouble(),velocityAlpha);
            Near(parent.Position,V(row,"resultParentDP"),1e-7f,id+" parent position");
            Near(parent.Rotation,V(row,"resultParentDQ"),1e-7f,id+" parent rotation");
            Near(child.Position,V(row,"resultChildDP"),2e-5f,id+" position delta");
            Near(child.Rotation,V(row,"resultChildDQ"),2e-6f,id+" rotation delta");
            Near(new Vector3(-1,-2,3)+velocity.Linear,V(row,"resultChildV"),2e-4f,id+" velocity");
            Near(new Vector3(.1f,.2f,.3f)+velocity.Angular,V(row,"resultChildW"),2e-5f,id+" angular velocity");
            var actual=AlsLockedLinearProjection.Correct(initial,child);
            var expected=Pose(row.GetProperty("resultChildPose"));
            Near(actual.Position.ToSingle(),expected.Position.ToSingle(),3e-5f,id+" corrected position");
            Assert.InRange(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(actual.Rotation,expected.Rotation))),0,1e-10);
        }
    }
    private static float F(JsonElement e,string name)=>e.GetProperty(name).GetSingle();
    private static Vector3 V(JsonElement e,string name)
    {var v=e.GetProperty(name);return new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());}
    private static AlsPrecisePose Pose(JsonElement e)
    {
        var p=e.GetProperty("position");var q=e.GetProperty("rotation");
        return new(new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble()),
            new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);
    }
    private static void Near(Vector3 actual,Vector3 expected,float tolerance,string context)=>
        Assert.True(Vector3.Distance(actual,expected)<=tolerance,$"{context}: {actual} != {expected}");
}
