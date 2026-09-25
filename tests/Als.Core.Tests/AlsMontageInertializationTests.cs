using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageInertializationTests
{
    private static AlsFrameIdentity Id(int frame)=>new(frame,7,1);
    private static AlsMontageRuntime Owner()=>new([],sequences:[new(10,AlsTurnSlot.Standing,3,1,0),new(11,AlsTurnSlot.Crouching,3,1,0)]);
    private static AlsSequenceMontageCommand Play(int animation=10)=>new(animation,animation==10?AlsTurnSlot.Standing:AlsTurnSlot.Crouching,1,0,.1f,.2f){InertialBlendOut=true};

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void NaturalInertialExitDropsContributionAndFreezesOneGroupRequest(int hz)
    {
        var owner=Owner();owner.Begin(Id(0),0);Assert.True(owner.PlaySequence(Play()));owner.Commit(Id(0));
        var requests=0;var ended=0;
        for(var frame=1;frame<=hz+3;frame++)
        {
            owner.Begin(Id(frame),1f/hz);var state=owner.Candidate.ToArray();var evaluation=owner.Evaluation.ToArray();var traversal=owner.Traversal.ToArray();
            var has=owner.Frame.TryGetInertializationRequest(3,out var request);
            if(has)
            {
                requests++;Assert.Equal(.2f,request.Duration);Assert.True(request.UseBlendMode);
                Assert.Empty(state);Assert.Empty(evaluation);Assert.Contains(traversal,t=>t.Terminated&&!t.Interrupted);
            }
            ended+=traversal.Count(t=>t.Terminated);
            owner.Discard();owner.Begin(Id(frame),1f/hz);
            Assert.Equal(has,owner.Frame.TryGetInertializationRequest(3,out var replay));Assert.Equal(request,replay);
            Assert.Equal(state,owner.Candidate.ToArray());Assert.Equal(evaluation,owner.Evaluation.ToArray());owner.Commit(Id(frame));
        }
        Assert.Equal(1,requests);Assert.Equal(1,ended);
    }

    [Fact]
    public void ExplicitStopsQueueForNextProxyAndLastGroupRequestWins()
    {
        var owner=Owner();owner.Begin(Id(0),0);owner.PlaySequence(Play());owner.Commit(Id(0));
        owner.Begin(Id(1),.2f);var snapshot=owner.Evaluation.ToArray();
        owner.StopSlots([AlsTurnSlot.Standing],.1f);Assert.Equal(0,owner.Candidate[0].Blend.CurrentWeight);Assert.False(owner.Candidate[0].Playing);
        Assert.Equal(snapshot,owner.Evaluation.ToArray());Assert.False(owner.Frame.TryGetInertializationRequest(3,out _));
        owner.PlaySequence(Play(11));owner.StopSlots([AlsTurnSlot.Crouching],.4f);owner.Commit(Id(1));
        owner.Begin(Id(2),0);Assert.Empty(owner.Candidate.ToArray());Assert.True(owner.Frame.TryGetInertializationRequest(3,out var request));
        Assert.Equal(.4f,request.Duration); // Not min(.1,.4).
        owner.Discard();owner.Begin(Id(2),0);Assert.True(owner.Frame.TryGetInertializationRequest(3,out var retry));Assert.Equal(request,retry);owner.Commit(Id(2));
        owner.Begin(Id(3),0);Assert.False(owner.Frame.TryGetInertializationRequest(3,out _));
    }

    [Fact]
    public void IncomingGroupReplacementUsesStandardBlendAndDoesNotEmitOutRequest()
    {
        var owner=Owner();owner.Begin(Id(0),0);owner.PlaySequence(Play());owner.Commit(Id(0));
        owner.Begin(Id(1),.2f);owner.PlaySequence(Play(11) with{BlendInTime=.3f});
        var old=owner.Candidate[0];Assert.Equal(.3f,old.BlendTime);Assert.Equal(1,old.Blend.CurrentWeight);Assert.True(old.Playing);
        owner.Commit(Id(1));owner.Begin(Id(2),.01f);
        Assert.False(owner.Frame.TryGetInertializationRequest(3,out _));Assert.Equal(2,owner.Candidate.Length);
        Assert.InRange(owner.Candidate[0].Blend.CurrentWeight,0.01f,.9999f);
    }

    [Fact]
    public void DiscardAndLifecycleClearDoNotLeakDeferredRequests()
    {
        var owner=Owner();owner.Begin(Id(0),0);owner.PlaySequence(Play());owner.Commit(Id(0));
        owner.Begin(Id(1),.2f);owner.StopSlots([AlsTurnSlot.Standing]);owner.Discard();
        owner.Begin(Id(1),.2f);Assert.Equal(1,owner.Candidate[0].Blend.DesiredWeight);owner.Commit(Id(1));
        owner.Begin(Id(2),0);Assert.False(owner.Frame.TryGetInertializationRequest(3,out _));owner.StopSlots([AlsTurnSlot.Standing]);owner.Commit(Id(2));
        owner.ClearForLifecycle();owner.Begin(Id(3),0);Assert.False(owner.Frame.TryGetInertializationRequest(3,out _));Assert.Empty(owner.Candidate.ToArray());
    }
}
