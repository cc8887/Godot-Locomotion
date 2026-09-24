using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredMontageSlotTests
{
    [Fact]
    public void AllSlotsSharePhysicalBankWithoutPoseOrGroupAliasing()
    {
        var assets=Enumerable.Range(0,AlsMontageSlot.Count).Select(i=>new AlsSequenceMontageAsset(20+i,new(i),i,4,0))
            .Append(new(100,AlsMontageSlot.ArmRight,AlsMontageSlot.ArmRight.Id,4,0)).ToArray();
        var owner=new AlsMontageRuntime([],sequences:assets);
        owner.Begin(Id(1),.3f);
        foreach(var asset in assets.Take(AlsMontageSlot.Count))Assert.True(owner.PlaySequence(Command(asset.AnimationId,asset.Slot)));
        Assert.Empty(owner.Evaluation.ToArray());Assert.Equal(AlsMontageSlot.Count,owner.Candidate.Length);
        Assert.Equal(AlsMontageSlot.Count,owner.Candidate.ToArray().Select(i=>i.InstanceId).Distinct().Count());owner.Commit(Id(1));
        owner.Begin(Id(2),.3f);var frozen=owner.Evaluation.ToArray();Assert.Equal(AlsMontageSlot.Count,frozen.Length);
        var mixer=new AlsMontageSlotPose([AlsPrecisePose.Identity],[-1],1);var sampler=new Samples();
        var pose=new AlsPrecisePose[1];var curves=new AlsInertialCurve[1];
        foreach(var slot in Enumerable.Range(0,AlsMontageSlot.Count).Select(i=>new AlsMontageSlot(i)))
        {
            Assert.Equal(new AlsSlotWeights(0,1,1),owner.SlotWeights(slot));
            mixer.Evaluate(owner.Frame,Id(2),slot,[],[],pose,curves,sampler);
            Assert.Equal(slot.Id,pose[0].Position.X);Assert.Equal(new AlsInertialCurve(20+slot.Id),curves[0]);
        }
        Assert.True(owner.PlaySequence(Command(100,AlsMontageSlot.ArmRight)));
        Assert.Equal(frozen,owner.Evaluation.ToArray());
        Assert.Single(owner.Candidate.ToArray(),i=>i.Interrupted&&i.AnimationId==28);
        Assert.Equal(AlsMontageSlot.Count-1,owner.Candidate.ToArray().Count(i=>i.AnimationId!=100&&!i.Interrupted));
        var candidate=owner.Candidate.ToArray();owner.Discard();owner.Begin(Id(2),.3f);
        Assert.True(owner.PlaySequence(Command(100,AlsMontageSlot.ArmRight)));Assert.Equal(candidate,owner.Candidate.ToArray());owner.Commit(Id(2));
    }
    [Fact]
    public void OriginalIdsAndNamedRegionMappingRemainDistinctAndBounded()
    {
        Assert.Equal(2,AlsMontageSlot.BaseLayer.Id);Assert.Equal(3,AlsMontageSlot.Grounded.Id);Assert.Equal(4,AlsMontageSlot.PostLocomotion.Id);
        var names=new[]{"Head","Spine","ArmLeft","ArmRight","Pelvis","Legs","Curves"};
        Assert.Equal(Enumerable.Range(5,7),names.Select(n=>AlsMontageSlot.FromRefactoredLayerName(n).Id));
        Assert.Throws<ArgumentException>(()=>AlsMontageSlot.FromRefactoredLayerName("Other"));
        Assert.Equal(12,AlsMontageSlot.Transition.Id);
        foreach(var id in new[]{-1,AlsMontageSlot.Count,32})
        {
            var slot=new AlsMontageSlot(id);Assert.False(slot.IsValid);
            Assert.Throws<ArgumentOutOfRangeException>(()=>slot.Mask);
            Assert.Throws<ArgumentException>(()=>new AlsMontageRuntime([],sequences:[new(1,slot,0,1,0)]));
        }
        var queue=new AlsMontageNotifyRuntime(new([],[],[],[],[]));queue.Begin(Id(1),[]);
        Assert.Throws<InvalidOperationException>(()=>queue.Complete(1<<AlsMontageSlot.Count));queue.Discard();
    }
    private static AlsFrameIdentity Id(int frame)=>new(frame,2,1);
    private static AlsSequenceMontageCommand Command(int animation,AlsMontageSlot slot)=>new(animation,slot,1,0,.2f,.2f);
    private sealed class Samples:IAlsMontagePoseSource
    {
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {pose[0]=new(new(entry.Slot.Id,0,0),AlsQuaternion.Identity,AlsDoubleVector.One);curves[0]=new(entry.AnimationId);}
    }
}
