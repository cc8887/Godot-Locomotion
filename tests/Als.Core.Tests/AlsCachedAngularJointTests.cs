using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCachedAngularJointTests
{
    private static readonly AlsAngularAxisSettings Free=new(AlsAngularMotion.Free,0,false,0,0,0,0);
    private static readonly AlsAngularSolverBody Static=new(AlsQuaternion.Identity,AlsQuaternion.Identity,AlsQuaternion.Identity,default);
    private static readonly AlsAngularSolverBody Dynamic=Static with {InverseMass=new(1,new(.01,.003,.02))};

    [Fact]
    public void LimitUsesFullTensorWhileDriveResponseStaysAlongItsAxis()
    {
        var child=Dynamic with {Predicted=AlsQuaternion.FromAxisAngle(Vector3.UnitX,.6f),
            Connector=AlsQuaternion.FromAxisAngle(Vector3.UnitZ,.4f)};
        var axis=AlsJointAngularKinematics.Evaluate(AlsQuaternion.Identity,child.Predicted*child.Connector).TwistAxis.ToSingle();
        var limit=new AlsAngularAxisSettings(AlsAngularMotion.Limited,.2,true,5000000,5000,0,0);
        var settings=new AlsAngularJointSettings(limit,Free,Free,AlsQuaternion.Identity,ConditionMass:false);
        var solver=new AlsCachedAngularJoint(Static,child,settings,1d/60);
        var p=Vector3.Zero; var c=Vector3.Zero;
        solver.SolveLimits(ref p,ref c);
        Assert.Equal(Vector3.Zero,p); Assert.True(c.Length()> .01f);
        Assert.True(Vector3.Cross(c,axis).Length()> .01f);
        Assert.NotEqual(AlsDoubleVector.Zero,solver.LimitLambda);
        Assert.Equal(AlsDoubleVector.Zero,solver.DriveLambda);

        settings=settings with {X=Free with {DriveStiffness=75,DriveDamping=1.5}};
        solver=new(Static,child,settings,1d/60); c=Vector3.Zero;
        solver.SolveDrives(ref p,ref c);
        Assert.Equal(Vector3.Zero,p); Assert.True(c.Length()>1e-4f);
        Assert.InRange(Vector3.Cross(c,axis).Length(),0,1e-7f);
        Assert.Equal(AlsDoubleVector.Zero,solver.LimitLambda);
        Assert.NotEqual(AlsDoubleVector.Zero,solver.DriveLambda);
    }

    [Fact]
    public void PureDampingOpposesPredictedMotionWithoutPositionSpring()
    {
        var child=Dynamic with {Initial=AlsQuaternion.FromAxisAngle(Vector3.UnitX,-.1f)};
        var damping=Free with {DriveDamping=20};
        var settings=new AlsAngularJointSettings(damping,damping,damping,AlsQuaternion.Identity);
        foreach(var simultaneous in new[]{false,true})
        {
            var solver=new AlsCachedAngularJoint(Static,child,settings with {UseSimd=simultaneous},1d/60);
            var p=Vector3.Zero; var c=Vector3.Zero;
            solver.SolveDrives(ref p,ref c);
            Assert.Equal(simultaneous,solver.SimultaneousDrives);
            Assert.InRange(c.X,-.099f,-.001f); Assert.Equal(0,c.Y); Assert.Equal(0,c.Z);
            Assert.Equal(Vector3.Zero,p);
        }
    }

    [Fact]
    public void ExternalCorrectionIsReadAndChannelsKeepSeparateHistory()
    {
        var axis=new AlsAngularAxisSettings(AlsAngularMotion.Limited,.2,true,5000000,5000,75,1.5);
        var settings=new AlsAngularJointSettings(axis,axis,axis,AlsQuaternion.Identity);
        var solver=new AlsCachedAngularJoint(Static,Dynamic,settings,1d/60);
        var p=Vector3.Zero; var c=Vector3.Zero;
        solver.SolveLimits(ref p,ref c);
        Assert.Equal(AlsDoubleVector.Zero,solver.LimitLambda);
        c.X=.5f; // Another joint/contact changed the body's accumulated DQ.
        solver.SolveLimits(ref p,ref c);
        // Damping also opposes the newly supplied motion; it can carry the
        // correction slightly inside the limit rather than landing on it.
        Assert.InRange(c.X,0,.2f);
        var limits=solver.LimitLambda;
        solver.SolveDrives(ref p,ref c);
        Assert.Equal(limits,solver.LimitLambda);
        Assert.NotEqual(AlsDoubleVector.Zero,solver.DriveLambda);
        var drives=solver.DriveLambda;
        c=Vector3.Zero; solver.SolveLimits(ref p,ref c);
        Assert.Equal(Vector3.Zero,c); Assert.Equal(limits,solver.LimitLambda); Assert.Equal(drives,solver.DriveLambda);
    }

    [Fact]
    public void AllStaticBodiesPreserveDeltasAndUnsupportedOrInvalidInputsAreRejected()
    {
        var axis=new AlsAngularAxisSettings(AlsAngularMotion.Limited,.2,true,100,1,75,1.5);
        var settings=new AlsAngularJointSettings(axis,axis,axis,AlsQuaternion.Identity);
        var solver=new AlsCachedAngularJoint(Static,Static,settings,1d/60);
        var p=Vector3.One;var c=-Vector3.One;
        solver.SolveLimits(ref p,ref c);solver.SolveDrives(ref p,ref c);
        Assert.Equal(Vector3.One,p);Assert.Equal(-Vector3.One,c);
        Assert.Throws<NotSupportedException>(()=>new AlsCachedAngularJoint(Static,Dynamic,settings with {Y=Free},1d/60));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AlsCachedAngularJoint(Static,Dynamic,settings,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AlsCachedAngularJoint(Static,Dynamic,settings with {X=axis with {DriveStiffness=double.NaN}},1d/60));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new AlsCachedAngularJoint(Static,Dynamic,settings,double.MaxValue));
    }

    [Fact]
    public void CacheAndRepeatedSolveAllocateNoManagedMemory()
    {
        var axis=new AlsAngularAxisSettings(AlsAngularMotion.Limited,.2,true,5000000,5000,75,1.5);
        var settings=new AlsAngularJointSettings(axis,axis,axis,AlsQuaternion.Identity);
        var child=Dynamic with {Predicted=AlsQuaternion.FromAxisAngle(Vector3.UnitX,.6f)};
        float total=0;
        for(var i=0;i<256;i++)total+=Run(child,settings);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<2048;i++)total+=Run(child,settings);
        var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,bytes);Assert.True(float.IsFinite(total));
    }
    private static float Run(AlsAngularSolverBody child,AlsAngularJointSettings settings)
    {
        var solver=new AlsCachedAngularJoint(Static,child,settings,1d/60);
        var p=Vector3.Zero;var c=Vector3.Zero;
        for(var i=0;i<8;i++){solver.SolveLimits(ref p,ref c);solver.SolveDrives(ref p,ref c);}
        return c.X;
    }
}
