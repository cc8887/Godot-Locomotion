using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsSharedMontageOwnerTests
{
    [Fact]
    public void SameGroupAuthoredAndTransientInstancesInterruptEachOtherWithoutDroppingFades()
    {
        var owner = Owner(); owner.Begin(Id(1),.05f); owner.Play(Command()); owner.Commit(Id(1));
        owner.Begin(Id(2),.05f); owner.PlayAction(0,1); Assert.True(owner.Candidate[0].Interrupted);
        Assert.Equal(2,owner.Candidate.Length); Assert.Equal(2,owner.ActiveActionInstance(0)); owner.Commit(Id(2));
        owner.Begin(Id(3),.05f); Assert.Equal(2,owner.Evaluation.Length); owner.Play(Command());
        Assert.Equal(3,owner.Candidate.Length); Assert.True(owner.Candidate[1].Interrupted);
        Assert.Equal(0,owner.ActiveActionInstance(0));
        Assert.Equal(3,owner.Candidate.ToArray().Select(i=>i.InstanceId).Distinct().Count());
    }

    [Fact]
    public void StoppingNewestDoesNotMakeOlderActiveInstanceTheAssetLookupOwner()
    {
        var owner = Owner(); owner.Begin(Id(1),.05f); owner.PlayAction(0,1); owner.Commit(Id(1));
        owner.Begin(Id(2),.05f); owner.PlayAction(0,1,stopGroup:false); owner.Commit(Id(2));
        owner.Begin(Id(3),.05f); Assert.Equal(2,owner.ActiveActionInstance(0));
        Assert.True(owner.StopInstance(2,.2f,AlsActionBlendOption.HermiteCubic));
        Assert.True(owner.Candidate[0].Playing); Assert.Equal(1,owner.Candidate[0].Blend.DesiredWeight);
        Assert.Equal(0,owner.ActiveActionInstance(0)); // UE ActiveMontagesMap removes, never falls back.
        var state = owner.Candidate.ToArray(); owner.Discard(); owner.Begin(Id(3),.05f);
        Assert.Equal(2,owner.ActiveActionInstance(0)); owner.StopInstance(2,.2f,AlsActionBlendOption.HermiteCubic);
        Assert.Equal(state,owner.Candidate.ToArray());
    }

    [Fact]
    public void DifferentActionDefinitionsForSameMontageShareTheNativeAssetLookup()
    {
        var first = Asset() with { MontageId = 12 }; var second = first with { ActionDefinitionId = 1 };
        var owner = new AlsMontageRuntime([], [first,second]); owner.Begin(Id(1),.01f);
        owner.PlayAction(0,1); owner.PlayAction(1,1,stopGroup:false);
        Assert.Equal(2,owner.ActiveActionInstance(0)); Assert.Equal(2,owner.ActiveActionInstance(1));
        owner.StopInstance(2,.2f,AlsActionBlendOption.HermiteCubic);
        Assert.Equal(0,owner.ActiveActionInstance(0)); Assert.Equal(0,owner.ActiveActionInstance(1));
    }

    [Fact]
    public void InvalidRequestAndStaleInstanceCannotChangeCurrentPlayback()
    {
        var owner = Owner(); owner.Begin(Id(1),.01f); owner.PlayAction(0,1); var state=owner.Candidate.ToArray();
        Assert.False(owner.PlayAction(9,1)); Assert.False(owner.StopInstance(99,.2f,AlsActionBlendOption.Linear));
        Assert.Throws<ArgumentException>(()=>owner.PlayAction(0,float.NaN));
        Assert.Throws<ArgumentException>(()=>owner.StopInstance(1,-1,AlsActionBlendOption.Linear));
        Assert.Equal(state,owner.Candidate.ToArray()); Assert.Equal(0,owner.ActiveActionInstance(-1));
    }

    [Fact]
    public void AuthoredAliasesCannotRedefineThePhysicalAsset()
    {
        var first = Asset() with { MontageId=12 };
        var second = first with { ActionDefinitionId=1,ClipRate=2 };
        Assert.Throws<ArgumentException>(() => new AlsMontageRuntime([], [first,second]));
    }

    [Fact]
    public void ClipMappingAndAuthoredIdentityReachBothPoseAndTraversal()
    {
        var action = Asset() with { ClipStart=.5f,ClipRate=2 };
        var owner = new AlsMontageRuntime([], [action]); owner.Begin(Id(1),.1f); owner.PlayAction(0,1); owner.Commit(Id(1));
        owner.Begin(Id(2),.1f); Assert.Equal(.1f,owner.Candidate[0].Position);
        Assert.Equal(.7f,owner.Evaluation[0].Position); Assert.Equal(0,owner.Evaluation[0].ActionDefinitionId);
        Assert.Equal(0,owner.Traversal[0].ActionDefinitionId); Assert.Equal(.1f,owner.Traversal[0].CurrentPosition);
        Assert.False(owner.Observations[0].Transient);
    }

    [Fact]
    public void SharedPlaybackAndLookupDoNotAllocateAfterCapacityIsWarm()
    {
        var owner=Owner();
        for(var f=1;f<=300;f++) Tick(f);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var f=301;f<=1300;f++) Tick(f);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        void Tick(int frame)
        {
            owner.Begin(Id(frame),.01f);
            if(frame%2==0)owner.PlayAction(0,1); else owner.Play(Command());
            _=owner.ActiveActionInstance(0); owner.Commit(Id(frame));
        }
    }

    private static AlsAuthoredMontageAsset Asset() => new(0,10,AlsMontageSlot.BaseLayer,1,2,0,1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut,.2f,AlsActionBlendOption.HermiteCubic,.2f,AlsActionBlendOption.HermiteCubic,-1));
    private static AlsMontageRuntime Owner() => new([new(10,AlsTurnSlot.Standing,1,2)],[Asset()]);
    private static AlsFrameIdentity Id(int frame) => new(frame,1,1);
    private static AlsTurnMontageCommand Command() => new(10,AlsTurnSlot.Standing,1,0,.2f,.2f,1,0);
}
