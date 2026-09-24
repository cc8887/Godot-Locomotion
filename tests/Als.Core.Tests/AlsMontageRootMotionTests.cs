using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsMontageRootMotionTests
{
    [Fact]
    public void AssetRateScaleControlsRemainingTimeAndPreservesFinalMotionTick()
    {
        var bank = new AlsMontageRuntime([], [Asset() with { RateScale = 2 }]);
        bank.Begin(Id(1), .05f); bank.PlayAction(0, 1, .51f); bank.Commit(Id(1));
        bank.Begin(Id(2), .05f);
        Assert.Equal(.61f, bank.Candidate[0].Position, 6);
        Assert.Equal(1, bank.Candidate[0].Blend.BlendingOut);
        Assert.Equal(.195f, bank.Candidate[0].BlendTime, 6);
        Assert.True(bank.RootMotionRange.HasMotion);
        Assert.Equal(0, bank.CandidateRootMotionInstance);
        bank.Commit(Id(2)); bank.Begin(Id(3), .05f);
        Assert.False(bank.RootMotionRange.HasMotion);
    }
    private static AlsFrameIdentity Id(long frame) => new(frame,1,1);
    private static AlsAuthoredMontageAsset Asset() => new(0,5,AlsMontageSlot.BaseLayer,2,1,.25f,2,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,.2f,AlsActionBlendOption.HermiteCubic,.2f,AlsActionBlendOption.HermiteCubic,-1),true);
    [Fact]
    public void MotionUsesThePhysicalTickBeforeCommandsAndDoesNotFollowPoseWeights()
    {
        var bank = new AlsMontageRuntime([], [Asset()]); bank.Begin(Id(1),.05f); bank.PlayAction(0,1.25f);
        Assert.False(bank.RootMotionRange.HasMotion); bank.Commit(Id(1));
        bank.Begin(Id(2),.05f); var range = bank.RootMotionRange;
        Assert.Equal(new(Id(2),1,5,.25f,.375f),range); Assert.True(bank.Evaluation[0].Weight < 1);
        bank.PlayAction(0,1); Assert.Equal(2,bank.CandidateRootMotionInstance); Assert.Equal(range,bank.RootMotionRange);
        bank.StopInstance(1,.1f,AlsActionBlendOption.Linear); Assert.Equal(2,bank.CandidateRootMotionInstance);
        bank.Commit(Id(2)); bank.Begin(Id(3),.05f);
        Assert.Equal(2,bank.RootMotionRange.InstanceId); bank.StopInstance(2,.2f,AlsActionBlendOption.Linear);
        Assert.Equal(0,bank.CandidateRootMotionInstance); bank.Commit(Id(3)); bank.Begin(Id(4),.05f);
        Assert.False(bank.RootMotionRange.HasMotion); Assert.True(bank.Evaluation.Length > 0);
    }
    [Fact]
    public void DiscardAndLifecycleRetirementPreserveThenClearMotionOwnership()
    {
        var bank = new AlsMontageRuntime([], [Asset()]); bank.Begin(Id(1),.05f); bank.PlayAction(0,1); bank.Commit(Id(1));
        bank.Begin(Id(2),.05f); var before=bank.RootMotionRange; bank.PlayAction(0,1); bank.Discard();
        Assert.Equal(1,bank.CommittedRootMotionInstance); bank.Begin(Id(2),.05f);
        Assert.Equal(before,bank.RootMotionRange); Assert.Equal(1,bank.CandidateRootMotionInstance);
        bank.Discard(); bank.ClearForLifecycle(); bank.Begin(Id(2),.05f);
        Assert.False(bank.RootMotionRange.HasMotion); bank.PlayAction(0,1); Assert.Equal(2,bank.CandidateRootMotionInstance);
    }
    [Fact]
    public void AutoBlendOutClearsFutureMotionButKeepsThisTickAndNeverResurrectsAFade()
    {
        var bank = new AlsMontageRuntime([], [Asset()]); bank.Begin(Id(1),.05f); bank.PlayAction(0,1,startTime:.79f); bank.Commit(Id(1));
        bank.Begin(Id(2),.05f); Assert.True(bank.RootMotionRange.HasMotion); Assert.Equal(0,bank.CandidateRootMotionInstance);
        bank.Commit(Id(2)); bank.Begin(Id(3),.05f); Assert.False(bank.RootMotionRange.HasMotion); Assert.NotEmpty(bank.Evaluation.ToArray());
    }
    [Theory]
    [InlineData(0,1)] [InlineData(1,0)] [InlineData(.25,.75)] [InlineData(-1,3)]
    public void RootTrackIsSampledInComponentSpaceWithoutPoseRootLock(double start,double end)
    {
        var sampler = Sampler(1,false, new(default,new(Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2)),AlsDoubleVector.One));
        var pose = sampler.Extract(start,end); var distance = System.Math.Clamp(end,0,1)-System.Math.Clamp(start,0,1);
        Assert.InRange(System.Math.Abs(pose.Position.X),0,.000001); Assert.InRange(System.Math.Abs(pose.Position.Y-distance),0,.000001);
    }
    [Theory] [InlineData(true,1)] [InlineData(false,.5)]
    public void AuthoredNormalizedScalePolicyChangesExtractedTranslation(bool normalized,double expected)
    { Assert.Equal(expected,Sampler(2,normalized,AlsPrecisePose.Identity).Extract(0,1).Position.X,6); }
    [Fact]
    public void RootRotationAndTranslationAreRelativeToTheStartTransform()
    {
        var keys = new[] {
            new AlsLocalPose(new(1,2,0),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2),Vector3.One),
            new AlsLocalPose(new(1,4,0),Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI),Vector3.One) };
        var sampler = new AlsRawRootMotionSampler(new(new(0,"asset","/source",0),1,1,2,1,
            AlsRawAnimationInterpolation.Linear,[0],[],[true],keys,[]),AlsPrecisePose.Identity,false);
        var forward = sampler.Extract(0,1); var backward = sampler.Extract(1,0);
        Assert.InRange(Vector3.Distance(forward.Position.ToSingle(),new(2,0,0)),0,.000001f);
        Assert.InRange(MathF.Abs(1-MathF.Abs(Quaternion.Dot(forward.Rotation.ToSingle(),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2)))),0,.000001f);
        var roundTrip = AlsPrecisePose.Compose(backward,forward);
        Assert.InRange(roundTrip.Position.LengthSquared,0,.000000000001);
        Assert.InRange(System.Math.Abs(1-System.Math.Abs(roundTrip.Rotation.W)),0,.000001);
    }
    [Fact]
    public void MotionSamplingHasNoPerFrameAllocation()
    {
        var sampler=Sampler(1,false,AlsPrecisePose.Identity); for(var i=0;i<100;i++)sampler.Extract(.1,.2);
        var before=GC.GetAllocatedBytesForCurrentThread(); for(var i=0;i<1000;i++)sampler.Extract(.1,.2);
        Assert.Equal(before,GC.GetAllocatedBytesForCurrentThread());
    }
    private static AlsRawRootMotionSampler Sampler(float scale,bool normalized,AlsPrecisePose reference)
    {
        var keys = new[] { new AlsLocalPose(Vector3.Zero,Quaternion.Identity,new(scale)),new AlsLocalPose(new(.5f,0,0),Quaternion.Identity,new(scale)),new AlsLocalPose(new(1,0,0),Quaternion.Identity,new(scale)) };
        return new(new(new(0,"asset","/source",0),2,1,3,1,AlsRawAnimationInterpolation.Linear,[0],[],[true],keys,[]),reference,normalized);
    }
}
