using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsRootMotionKinematicsTests
{
    [Fact]
    public void AnimationDriveIsPublishedOnlyForTheCurrentPhysicalInput()
    {
        var state = AlsRuntimeState.CreateDefault(); var result = default(AlsFrameResult);
        AlsLocomotionModel.Evaluate(P3TestInput.Grounded() with { CurrentDriveMode = AlsDriveMode.AnimationDriven },
            ref state, ref result, P3TestSettings.Reference);
        Assert.Equal(AlsDriveMode.AnimationDriven,result.RequestedDriveMode);
        AlsLocomotionModel.Evaluate(P3TestInput.Grounded(frameId:2),ref state,ref result,P3TestSettings.Reference);
        Assert.Equal(AlsDriveMode.MotorDriven,result.RequestedDriveMode);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionModel.Evaluate(
            P3TestInput.Grounded(frameId:3) with { CurrentDriveMode=(AlsDriveMode)255 },ref state,ref result,P3TestSettings.Reference));
    }
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(-90)] [InlineData(180)]
    public void MeshRotationAndScaleConvertTranslationWithoutAddingTheMeshOffset(float degrees)
    {
        var actor = new AlsPrecisePose(new(5,6,7),new(Quaternion.CreateFromAxisAngle(Vector3.UnitY,degrees*MathF.PI/180)),AlsDoubleVector.One);
        var mesh = new AlsPrecisePose(new(1,-2,3),new(Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2)),new(2,2,2));
        var result = AlsRootMotionKinematics.ToWorld(new(Vector3.UnitX,Quaternion.Identity),mesh,actor);
        var expected = Vector3.Transform(new Vector3(0,0,-2),actor.Rotation.ToSingle());
        Assert.InRange(Vector3.Distance(result.Translation,expected),0,.000003f);
        Assert.InRange(MathF.Abs(Quaternion.Dot(result.Rotation,Quaternion.Identity)),.999999f,1.000001f);
    }
    [Fact]
    public void RotatingAnOffsetMeshIncludesTheActorPivotDisplacement()
    {
        var mesh = new AlsPrecisePose(new(1,0,0),AlsQuaternion.Identity,AlsDoubleVector.One);
        var delta = new AlsRootMotionDelta(Vector3.Zero,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2));
        var result = AlsRootMotionKinematics.ToWorld(delta,mesh,AlsPrecisePose.Identity);
        Assert.InRange(Vector3.Distance(result.Translation,new(1,0,1)),0,.000001f);
        Assert.InRange(MathF.Abs(Quaternion.Dot(result.Rotation,delta.Rotation)),.999999f,1.000001f);
    }
    [Fact]
    public void HotConversionAllocatesNoManagedMemory()
    {
        var delta=new AlsRootMotionDelta(new(.01f,0,0),Quaternion.Identity);
        for(var i=0;i<100;i++) AlsRootMotionKinematics.ToWorld(delta,AlsPrecisePose.Identity,AlsPrecisePose.Identity);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++) AlsRootMotionKinematics.ToWorld(delta,AlsPrecisePose.Identity,AlsPrecisePose.Identity);
        Assert.Equal(before,GC.GetAllocatedBytesForCurrentThread());
    }
}
