using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageWeightDivisionTests
{
    private sealed class IdentitySource:IAlsMontagePoseSource
    {
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)=>pose.Fill(AlsPrecisePose.Identity);
    }
    [Fact]
    public void OverlappingSlotMatchesNativeIndividuallyDividedWeights()
    {
        // Original weapon Root/DefaultSlot trace: weights 1 and .75, total 1.75.
        // Native double transforms retain the sum of separately divided floats.
        var slot=new AlsMontageSlot(0);
        var lifecycle=new AlsActionLifecycleSettings(AlsActionLifecycleMode.MontageAutoBlendOut,0,AlsActionBlendOption.Linear,4,AlsActionBlendOption.Linear,-1);
        var owner=new AlsMontageRuntime([], [new(0,0,slot,0,10,0,1,lifecycle),new(1,1,slot,0,10,0,1,lifecycle)]);
        var first=new AlsFrameIdentity(0,1,1);owner.Begin(first,0);owner.PlayAction(0,1);owner.PlayAction(1,1,stopGroup:false);owner.Commit(first);
        var second=new AlsFrameIdentity(1,1,1);owner.Begin(second,0);var fading=owner.Candidate.ToArray().Single(i=>i.ActionDefinitionId==1);
        owner.StopInstance(fading.InstanceId,4,AlsActionBlendOption.Linear);owner.Commit(second);
        var third=new AlsFrameIdentity(2,1,1);owner.Begin(third,1);
        Assert.Equal(new float[]{1,.75f},owner.Evaluation.ToArray().Select(e=>e.Weight).ToArray());
        var output=new AlsPrecisePose[1];var mixer=new AlsMontageSlotPose([AlsPrecisePose.Identity],[-1],0,AlsMontageWeightNormalization.IndividualDivision);
        mixer.Evaluate(owner.Frame,third,slot,[AlsPrecisePose.Identity],[],output,[],new IdentitySource());
        Assert.Equal(1.0000000298023224,output[0].Scale.X);
        Assert.Equal(1.0000000298023224,output[0].Scale.Y);
        Assert.Equal(1.0000000298023224,output[0].Scale.Z);
    }
}
