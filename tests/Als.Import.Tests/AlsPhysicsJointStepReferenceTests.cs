using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsJointStepReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void CoupledPositionAndVelocityIterationsMatchNativeContainer()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_joint_step_reference.json")));
        var root=doc.RootElement;Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.True(B(root,"linearContainerSolver"));var rows=root.GetProperty("cases");Assert.Equal(288,rows.GetArrayLength());
        var ids=new HashSet<string>();float maxPosition=0,maxRotation=0,maxLinearVelocity=0,maxAngularVelocity=0;
        foreach(var row in rows.EnumerateArray())
        {
            var id=$"{row.GetProperty("hz")}/{row.GetProperty("bodyMode")}/{row.GetProperty("angularMode")}/{row.GetProperty("condition")}/{row.GetProperty("rotated")}/{row.GetProperty("simd")}";
            Assert.True(ids.Add(id));var initial=row.GetProperty("initial");var seed=row.GetProperty("seed");
            var parent=Input(row,initial,"parent");var child=Input(row,initial,"child");
            var settings=Settings(row);var solver=new AlsCachedJoint(parent,child,settings,D(row,"dt"));
            Assert.Equal(settings,GodotAls.Import.Compilation.AlsCachedJointSettingsCompiler.Angular(row.GetProperty("jointSettings"),row.GetProperty("solverSettings")));
            var dp=Delta(seed.GetProperty("parent"));var dc=Delta(seed.GetProperty("child"));
            var vp=Velocity(initial.GetProperty("parent"));var vc=Velocity(initial.GetProperty("child"));
            var samples=row.GetProperty("positionSamples");Assert.Equal(8,samples.GetArrayLength());var iteration=0;
            foreach(var sample in samples.EnumerateArray())
            {
                solver.SolvePosition(ref dp,ref dc);
                Check(sample,$"{id} position {iteration++}");
            }
            vp=AlsCachedJoint.AddImplicitVelocity(vp,dp,D(row,"dt"),parent.InverseMass.Mass>0);
            vc=AlsCachedJoint.AddImplicitVelocity(vc,dc,D(row,"dt"),child.InverseMass.Mass>0);
            Check(row.GetProperty("implicit"),id+" implicit");
            samples=row.GetProperty("velocitySamples");Assert.Equal(2,samples.GetArrayLength());iteration=0;
            foreach(var sample in samples.EnumerateArray())
            {solver.SolveVelocity(ref vp,ref vc);Check(sample,$"{id} velocity {iteration++}");}

            void Check(JsonElement sample,string context)
            {
                CheckBody(sample.GetProperty("parent"),parent.Predicted,dp,vp,context+" parent");
                CheckBody(sample.GetProperty("child"),child.Predicted,dc,vc,context+" child");
            }
            void CheckBody(JsonElement expected,AlsPrecisePose predicted,AlsProjectionDelta delta,AlsProjectionVelocity velocity,string context)
            {
                maxPosition=MathF.Max(maxPosition,Near(delta.Position,V(expected,"dp").ToSingle(),2e-5f,context+" DP"));
                maxRotation=MathF.Max(maxRotation,Near(delta.Rotation,V(expected,"dq").ToSingle(),3e-6f,context+" DQ"));
                maxLinearVelocity=MathF.Max(maxLinearVelocity,Near(velocity.Linear,V(expected,"v").ToSingle(),3e-3f,context+" V"));
                maxAngularVelocity=MathF.Max(maxAngularVelocity,Near(velocity.Angular,V(expected,"w").ToSingle(),5e-4f,context+" W"));
                var actual=AlsLockedLinearProjection.Correct(predicted,delta);var pose=Pose(expected.GetProperty("pose"));
                Assert.True((actual.Position-pose.Position).NearlyZero(2e-5),context+" position");
                Assert.InRange(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(actual.Rotation,pose.Rotation))),0,1e-10);
            }
        }
        output.WriteLine($"NATIVE_JOINT_STEP_OK cases=288 max_dp={maxPosition:R} max_dq={maxRotation:R} max_v={maxLinearVelocity:R} max_w={maxAngularVelocity:R}");
    }

    internal static AlsAngularJointSettings Settings(JsonElement row)
    {
        var j=row.GetProperty("jointSettings");var s=row.GetProperty("solverSettings");
        Assert.True(B(s,"bSolvePositionLast"));Assert.True(B(s,"bUsePositionBasedDrives"));
        Assert.Equal(1,D(s,"MinSolverStiffness"));Assert.Equal(1,D(s,"MaxSolverStiffness"));
        Assert.False(B(j,"bAngularSLerpPositionDriveEnabled"));Assert.False(B(j,"bAngularSLerpVelocityDriveEnabled"));
        Assert.Equal(AlsDoubleVector.Zero,V(j,"AngularDriveVelocityTarget"));
        for(var i=0;i<3;i++)Assert.True(V(j,"AngularDriveMaxTorque")[i] is 0 or (double)float.MaxValue);
        Assert.Equal(1,D(j,"ParentInvMassScale"));Assert.False(B(j,"bShockPropagationEnabled"));
        Assert.Equal(0,D(j,"LinearRestitution"));Assert.Equal(0,D(j,"TwistRestitution"));Assert.Equal(0,D(j,"SwingRestitution"));
        for(var i=0;i<3;i++)
        {Assert.Equal(2,j.GetProperty("LinearMotionTypes")[i].GetInt32());Assert.False(j.GetProperty("bLinearPositionDriveEnabled")[i].GetBoolean());Assert.False(j.GetProperty("bLinearVelocityDriveEnabled")[i].GetBoolean());}
        return new(Axis(0),Axis(1),Axis(2),Quaternion(j.GetProperty("AngularDrivePositionTarget")),
            B(j,"bMassConditioningEnabled"),j.GetProperty("AngularSoftForceMode").GetInt32()==0,
            j.GetProperty("AngularDriveForceMode").GetInt32()==0,B(s,"bUseSimd"),D(j,"Stiffness"),D(s,"AngleTolerance"),D(s,"MinParentMassRatio"),D(s,"MaxInertiaRatio"));
        AlsAngularAxisSettings Axis(int i)
        {
            var name=i==0?"Twist":"Swing";var driveIndex=i==0?0:2;
            return new((AlsAngularMotion)j.GetProperty("AngularMotionTypes")[i].GetInt32(),V(j,"AngularLimits")[i],
                B(j,"bSoft"+name+"LimitsEnabled"),D(j,"Soft"+name+"Stiffness"),D(j,"Soft"+name+"Damping"),
                B(j,"bAngular"+name+"PositionDriveEnabled")?V(j,"AngularDriveStiffness")[driveIndex]:0,
                B(j,"bAngular"+name+"VelocityDriveEnabled")?V(j,"AngularDriveDamping")[driveIndex]:0);
        }
    }
    private static AlsJointBodyInput Input(JsonElement row,JsonElement initial,string name)
    {var pose=Pose(initial.GetProperty(name).GetProperty("pose"));return new(pose,pose,Pose(row.GetProperty(name+"Frame")),new(D(row,name+"InverseMass"),V(row,name+"InverseInertia")));}
    private static AlsProjectionDelta Delta(JsonElement e)=>new(V(e,"dp").ToSingle(),V(e,"dq").ToSingle());
    private static AlsProjectionVelocity Velocity(JsonElement e)=>new(V(e,"v").ToSingle(),V(e,"w").ToSingle());
    internal static double D(JsonElement e,string name)=>e.GetProperty(name).GetDouble();
    internal static bool B(JsonElement e,string name)=>e.GetProperty(name).GetBoolean();
    internal static AlsDoubleVector V(JsonElement e,string name)
    {var v=e.GetProperty(name);return new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());}
    internal static AlsQuaternion Quaternion(JsonElement q)=>new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());
    internal static AlsPrecisePose Pose(JsonElement e)=>new(V(e,"position"),Quaternion(e.GetProperty("rotation")),AlsDoubleVector.One);
    private static float Near(Vector3 a,Vector3 b,float tolerance,string context)
    {var error=Vector3.Distance(a,b);Assert.True(error<=tolerance,$"{context}: {a} != {b}, error={error}");return error;}
}
