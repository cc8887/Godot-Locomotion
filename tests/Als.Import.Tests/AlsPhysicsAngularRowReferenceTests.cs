using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsAngularRowReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private float _maxDeltaError;
    private double _maxPoseDotError;
    [Fact]
    public void EveryAngularIterationAndStepResetMatchesNativeContainer()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_angular_row_reference.json")));
        var root=doc.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.True(B(root,"linearContainerSolver")); Assert.True(B(root,"simdEnabled"));
        Assert.Equal(8,root.GetProperty("iterations").GetInt32());
        var rows=root.GetProperty("cases"); Assert.Equal(516,rows.GetArrayLength());
        var identities=new HashSet<string>();
        foreach(var row in rows.EnumerateArray())
        {
            var id=$"{row.GetProperty("hz")}/{row.GetProperty("motionCase")}/{row.GetProperty("channelCase")}/{row.GetProperty("fixedParent")}/{row.GetProperty("conditioning")}/{row.GetProperty("rotated")}/control={row.GetProperty("controlCase")}";
            Assert.True(identities.Add(id));
            var settings=new AlsAngularJointSettings(Axis(row,0),Axis(row,1),Axis(row,2),Q(row,"driveTarget"),
                B(row,"conditioning"),B(row,"accelerationMode"),B(row,"accelerationMode"),B(row,"useSimd"),
                D(row,"hardStiffness"),D(row,"angleTolerance"),D(row,"minParentMassRatio"),D(row,"maxInertiaRatio"));
            var parent=Body(row,"parent"); var child=Body(row,"child");
            var solver=new AlsCachedAngularJoint(parent,child,settings,D(row,"dt"));
            var p=V(row,"parentSeedDQ").ToSingle(); var c=V(row,"childSeedDQ").ToSingle();
            var samples=row.GetProperty("samples"); Assert.Equal(8,samples.GetArrayLength());
            var iteration=0;
            foreach(var sample in samples.EnumerateArray())
            {
                solver.SolveLimits(ref p,ref c); solver.SolveDrives(ref p,ref c);
                var context=$"{id} iteration {iteration++}";
                Near(p,V(sample,"parentDQ").ToSingle(),3e-6f,context+" parent DQ");
                Near(c,V(sample,"childDQ").ToSingle(),3e-6f,context+" child DQ");
                Assert.Equal(AlsDoubleVector.Zero,V(sample,"parentDP"));
                Assert.Equal(AlsDoubleVector.Zero,V(sample,"childDP"));
                PoseNear(parent.Predicted,p,Q(sample,"parentPose"),context+" parent pose");
                PoseNear(child.Predicted,c,Q(sample,"childPose"),context+" child pose");
            }
            // A new gather must reset independent drive and limit accumulators.
            solver=new(parent,child,settings,D(row,"dt"));
            Assert.Equal(AlsDoubleVector.Zero,solver.LimitLambda); Assert.Equal(AlsDoubleVector.Zero,solver.DriveLambda);
            p=V(row,"parentSeedDQ").ToSingle(); c=V(row,"childSeedDQ").ToSingle();
            solver.SolveLimits(ref p,ref c); solver.SolveDrives(ref p,ref c);
            Near(p,V(row,"resetParentDQ").ToSingle(),3e-6f,id+" reset parent");
            Near(c,V(row,"resetChildDQ").ToSingle(),3e-6f,id+" reset child");
            Assert.Equal(V(samples[0],"parentDQ"),V(row,"resetParentDQ"));
            Assert.Equal(V(samples[0],"childDQ"),V(row,"resetChildDQ"));
        }
        output.WriteLine($"NATIVE_ANGULAR_ROWS_OK cases={rows.GetArrayLength()} iterations=8 max_dq_error={_maxDeltaError:R} max_pose_dot_error={_maxPoseDotError:R}");
    }

    private static AlsAngularAxisSettings Axis(JsonElement r,int i)=>new(
        (AlsAngularMotion)r.GetProperty("angularMotion")[i].GetInt32(),V(r,"angularLimits")[i],
        B(r,i==0?"softTwist":"softSwing"),V(r,"limitStiffness")[i],V(r,"limitDamping")[i],
        B(r,"drivePositionEnabled")?V(r,"driveStiffness")[i]:0,B(r,"driveVelocityEnabled")?V(r,"driveDamping")[i]:0);
    private static AlsAngularSolverBody Body(JsonElement r,string name)=>new(Q(r,name+"Initial"),Q(r,name+"Predicted"),
        Q(r,name+"Frame"),new(D(r,name+"InverseMass"),V(r,name+"InverseInertia")));
    private static double D(JsonElement r,string name)=>r.GetProperty(name).GetDouble();
    private static bool B(JsonElement r,string name)=>r.GetProperty(name).GetBoolean();
    private static AlsDoubleVector V(JsonElement r,string name)
    {var v=r.GetProperty(name); return new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());}
    private static AlsQuaternion Q(JsonElement r,string name)
    {var q=r.GetProperty(name).GetProperty("rotation"); return new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());}
    private void Near(Vector3 actual,Vector3 expected,float tolerance,string context)
    {
        var error=Vector3.Distance(actual,expected); _maxDeltaError=MathF.Max(_maxDeltaError,error);
        Assert.True(error<=tolerance,$"{context}: {actual} != {expected}");
    }
    private void PoseNear(AlsQuaternion initial,Vector3 delta,AlsQuaternion expected,string context)
    {
        var actual=AlsLockedLinearProjection.Correct(AlsPrecisePose.Identity with {Rotation=initial},new(default,delta)).Rotation;
        var error=System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(actual,expected)));
        _maxPoseDotError=System.Math.Max(_maxPoseDotError,error);
        Assert.True(error<=1e-10,context);
    }
}
