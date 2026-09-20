using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCachedJointTests
{
    private static readonly AlsPrecisePose Identity=AlsPrecisePose.Identity;
    private static readonly AlsJointInverseMass DynamicMass=new(1,AlsDoubleVector.One);

    [Fact]
    public void LockedAnchorDistributesCorrectionByInverseMassAndConservesLinearMomentum()
    {
        var solver=new AlsCachedLinearJoint(Identity,Identity with {Position=new(2,0,0)},Identity,Identity,
            DynamicMass,DynamicMass with {Mass=2},conditionMass:false);
        var p=default(AlsProjectionDelta);var c=default(AlsProjectionDelta);solver.SolvePositions(ref p,ref c);
        Assert.InRange(p.Position.X,2f/3-1e-6f,2f/3+1e-6f);
        Assert.InRange(c.Position.X,-4f/3-1e-6f,-4f/3+1e-6f);
        Assert.Equal(Vector3.Zero,p.Rotation);Assert.Equal(Vector3.Zero,c.Rotation);
        Assert.InRange((p.Position+c.Position*.5f).Length(),0,1e-6f);
    }

    [Fact]
    public void SimdVelocityGateActivatesAllAxesButScalarGateUsesEachRowsImpulse()
    {
        foreach(var simultaneous in new[]{false,true})
        {
            var solver=new AlsCachedLinearJoint(Identity,Identity with {Position=new(1,0,0)},Identity,Identity,
                default,DynamicMass,simultaneous:simultaneous);
            var dp=default(AlsProjectionDelta);var dc=default(AlsProjectionDelta);
            var vp=default(AlsProjectionVelocity);var vc=new AlsProjectionVelocity(new(1,2,3),Vector3.Zero);
            solver.SolveVelocities(ref vp,ref vc);Assert.Equal(new Vector3(1,2,3),vc.Linear);
            solver.SolvePositions(ref dp,ref dc);solver.SolveVelocities(ref vp,ref vc);
            Assert.Equal(simultaneous?Vector3.Zero:new Vector3(0,2,3),vc.Linear);
        }
    }

    [Fact]
    public void ImplicitVelocityUsesPositionAndRotationDeltaButStaticBodiesDoNotChange()
    {
        var velocity=new AlsProjectionVelocity(Vector3.One,Vector3.One);
        var delta=new AlsProjectionDelta(new(1,2,3),new(.1f,.2f,.3f));
        Assert.Equal(velocity,AlsCachedJoint.AddImplicitVelocity(velocity,delta,.5,false));
        var result=AlsCachedJoint.AddImplicitVelocity(velocity,delta,.5,true);
        Assert.Equal(new Vector3(3,5,7),result.Linear);Assert.Equal(new Vector3(1.2f,1.4f,1.6f),result.Angular);
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsCachedJoint.AddImplicitVelocity(velocity,delta,double.Epsilon,true));
    }

    [Fact]
    public void PredictionPreservesComAndAppliesNativeLinearDragAndQuaternionStorage()
    {
        var mass=Identity with {Position=new(2,3,4)};
        var velocity=new AlsProjectionVelocity(new(2,0,0),new(0,0,1));
        var result=AlsRigidBodyIntegration.Predict(Identity,mass,velocity,1,2,.5);
        Assert.Equal(new Vector3(1,0,0),result.Velocity.Linear);Assert.Equal(Vector3.Zero,result.Velocity.Angular);
        Assert.Equal(new AlsDoubleVector(2.5,3,4),result.MassPose.Position);
        var actor=AlsRigidBodyIntegration.StoreActor(result.MassPose,mass);
        Assert.Equal(new AlsDoubleVector(.5,0,0),actor.Position);
        // A solver's unchanged float quaternion is not renormalized on a zero DQ.
        var nearUnit=Identity with {Rotation=new(0,0,0,.99999999)};
        Assert.Equal(nearUnit,AlsLockedLinearProjection.Correct(nearUnit,default));
    }

    [Fact]
    public void CoupledHotStepAllocatesNothingAndKeepsFixedParent()
    {
        var p=new AlsJointBodyInput(Identity,Identity,Identity,default);
        var q=Identity with {Position=new(.1,.2,.3),Rotation=AlsQuaternion.FromAxisAngle(Vector3.UnitX,.6f)};
        var c=new AlsJointBodyInput(q,q,Identity with {Position=new(-1,0,0)},DynamicMass);
        var axis=new AlsAngularAxisSettings(AlsAngularMotion.Limited,.2,true,5000000,5000,75,1.5);
        var settings=new AlsAngularJointSettings(axis,axis,axis,AlsQuaternion.Identity);
        float sum=0;for(var i=0;i<256;i++)sum+=Run(p,c,settings);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<2048;i++)sum+=Run(p,c,settings);
        var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,bytes);Assert.True(float.IsFinite(sum));
    }
    private static float Run(AlsJointBodyInput p,AlsJointBodyInput c,AlsAngularJointSettings settings)
    {
        var joint=new AlsCachedJoint(p,c,settings,1d/60);
        var dp=default(AlsProjectionDelta);var dc=default(AlsProjectionDelta);
        var vp=default(AlsProjectionVelocity);var vc=default(AlsProjectionVelocity);
        for(var i=0;i<8;i++)joint.SolvePosition(ref dp,ref dc);
        vc=AlsCachedJoint.AddImplicitVelocity(vc,dc,1d/60,true);
        for(var i=0;i<2;i++)joint.SolveVelocity(ref vp,ref vc);
        if(dp!=default||vp!=default)throw new InvalidOperationException("Fixed parent moved.");
        return vc.Angular.X;
    }
}
