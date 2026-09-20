using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsNativeJointTrajectoryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FullUninterruptedPairTrajectoriesMatchNativeScene()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_awake_solver_reference.json")));
        Assert.False(B(doc.RootElement,"sleepEnabled"));
        var rows=doc.RootElement.GetProperty("cases");Assert.Equal(144,rows.GetArrayLength());
        double maxPosition=0,maxAngle=0;float maxV=0,maxW=0;var caseIndex=0;
        foreach(var row in rows.EnumerateArray())
        {
            var settings=Settings(row);var dt=D(row,"dt");var j=row.GetProperty("jointSettings");
            Assert.Equal(0,D(j,"AngularProjection"));Assert.Equal(1,row.GetProperty("projectionIterations").GetInt32());
            var parent=Pose(row.GetProperty("samples")[0].GetProperty("parent").GetProperty("world"));
            var actor=Pose(row.GetProperty("samples")[0].GetProperty("child").GetProperty("world"));
            var body=row.GetProperty("bodies")[1];var massLocal=Pose(body.GetProperty("massLocal"));
            // Static/kinematic bodies use actor space in Chaos's solver; only
            // the dynamic child's connector is transformed into mass space.
            var pf=Pose(row.GetProperty("parentFrame"));
            var inv=new AlsJointInverseMass((float)(1/D(body,"massKg")),V(body,"bodyConditionedInverseInertia"));
            var solverSettings=row.GetProperty("solverSettings");
            Assert.Equal(settings,AlsCachedJointSettingsCompiler.Angular(j,solverSettings));
            var island=new AlsJointIsland([
                new(Pose(row.GetProperty("bodies")[0].GetProperty("massLocal")),default),
                new(massLocal,inv,D(body,"linearDamping"),D(body,"angularDamping"))],
                [new(0,1,pf,Pose(row.GetProperty("childFrame")),settings,AlsCachedJointSettingsCompiler.Projection(j,solverSettings))],
                [new(parent,default),new(actor,default)],row.GetProperty("positionIterations").GetInt32(),row.GetProperty("velocityIterations").GetInt32());
            for(var frame=1;frame<=12;frame++)
            {
                island.StepForceFree(dt);
                actor=island.BodyAt(1).Actor;var velocity=island.BodyAt(1).Velocity;
                Assert.Equal(new AlsIslandBodyState(parent,default),island.BodyAt(0));
                var expected=row.GetProperty("samples")[frame].GetProperty("child");var pose=Pose(expected.GetProperty("world"));
                Assert.True(B(expected,"awake"));
                var position=System.Math.Sqrt((actor.Position-pose.Position).LengthSquared);
                var angle=2*System.Math.Acos(System.Math.Clamp(System.Math.Abs(AlsQuaternion.Dot(actor.Rotation.Normalized(),pose.Rotation.Normalized())),0,1));
                var v=Vector3.Distance(velocity.Linear,V(expected,"linearVelocity").ToSingle());var w=Vector3.Distance(velocity.Angular,V(expected,"angularVelocity").ToSingle());
                maxPosition=System.Math.Max(maxPosition,position);maxAngle=System.Math.Max(maxAngle,angle);maxV=MathF.Max(maxV,v);maxW=MathF.Max(maxW,w);
                Assert.True(position<2e-5&&angle<1e-6&&v<1e-4&&w<2e-5,
                    $"case={caseIndex} frame={frame} position_cm={position:R} angle_rad={angle:R} v_cmps={v:R} w_radps={w:R}");
            }
            caseIndex++;
        }
        output.WriteLine($"NATIVE_PAIR_TRAJECTORIES_OK cases={caseIndex} frames=12 max_position_cm={maxPosition:R} max_angle_rad={maxAngle:R} max_v_cmps={maxV:R} max_w_radps={maxW:R}");
    }

    [Fact]
    public void AwakeReferenceKeepsAssetInputsAndInitialConditionsOfSleepingBaseline()
    {
        using var baseline=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_joint_solver_reference.json")));
        using var awake=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_awake_solver_reference.json")));
        var originals=baseline.RootElement.GetProperty("cases");var rows=awake.RootElement.GetProperty("cases");
        Assert.Equal(144,rows.GetArrayLength());Assert.Equal(originals.GetArrayLength(),rows.GetArrayLength());
        var originalSleeping=0;
        for(var i=0;i<rows.GetArrayLength();i++)
        {
            foreach(var field in new[]{"mesh","physicsAsset","parent","child","hz","dt","axis","perturbation",
                "fullSpeedDrive","parentFrame","childFrame","bodies","jointSettings","solverSettings",
                "positionIterations","velocityIterations","projectionIterations","jointConditioningMath"})
                Assert.Equal(originals[i].GetProperty(field).GetRawText(),rows[i].GetProperty(field).GetRawText());
            Assert.Equal(originals[i].GetProperty("samples")[0].GetRawText(),rows[i].GetProperty("samples")[0].GetRawText());
            foreach(var sample in originals[i].GetProperty("samples").EnumerateArray())
                if(!B(sample.GetProperty("child"),"awake"))originalSleeping++;
            foreach(var sample in rows[i].GetProperty("samples").EnumerateArray())Assert.True(B(sample.GetProperty("child"),"awake"));
        }
        Assert.True(originalSleeping>0);output.WriteLine($"SLEEP_REFERENCE_RETAINED sleeping_samples={originalSleeping}");
    }
}
