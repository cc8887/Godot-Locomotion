using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsMotionWarpingRuntimeTests
{
    private sealed class Source:IAlsMotionWarpingRootSource
    {
        public bool Fail;
        public AlsPrecisePose ExtractRange(int asset,float previous,float current)=>Fail?throw new InvalidOperationException("Injected source failure."):AlsPrecisePose.Identity;
    }
    private static AlsMotionWarpingRuntime Runtime(Source? source=null)=>new(42,1,
        ImmutableArray.Create(new AlsMotionWarpingWindow(17,0,.4f,false,AlsPrecisePose.Identity,AlsPrecisePose.Identity)),source??new());
    private static AlsMotionWarpingCandidate Begin(AlsMotionWarpingRuntime runtime,long frame,float previous,float current,
        int asset=17,bool present=true,double location=0,ReadOnlySpan<AlsMotionWarpingTargetRequest> targets=default)=>
        runtime.Begin(new(frame,42,1),present,AlsPrecisePose.Identity,new(asset,previous,current,1,1,.1f),
            new(new(location,0,90),AlsQuaternion.Identity,AlsDoubleVector.One),
            new(new(location,0,0),AlsQuaternion.Identity,AlsDoubleVector.One),AlsPrecisePose.Identity,targets);
    private static AlsMotionWarpingTargetRequest Target(double x)=>new(AlsMotionWarpingTargetOperation.Set,new("Align",new(new(x,0,0),AlsQuaternion.Identity,AlsDoubleVector.One)));

    [Fact]
    public void CancelledTargetChangeCannotMoveOrResetCommittedOrigin()
    {
        var runtime=Runtime();var first=Begin(runtime,0,0,.1f,targets:[Target(200)]);Assert.Equal(50,first.Warped.Position.X);runtime.Commit(first);
        var changed=Begin(runtime,1,.1f,.2f,location:50,targets:[Target(400)]);Assert.Equal(.1f,changed.Modifiers[0].ActualStart);runtime.Cancel();
        Assert.Throws<InvalidOperationException>(()=>runtime.Commit(changed));
        var old=Begin(runtime,1,.1f,.2f,location:50);Assert.Equal(50,old.Warped.Position.X);Assert.Equal(0,old.Modifiers[0].ActualStart);runtime.Cancel();
        var retry=Begin(runtime,1,.1f,.2f,location:50,targets:[Target(400)]);Assert.Equal(changed.Warped,retry.Warped);Assert.Equal(changed.Modifiers.ToArray(),retry.Modifiers.ToArray());runtime.Commit(retry);
    }

    [Fact]
    public void LateTargetDoesNotReactivateDisabledWindow()
    {
        var runtime=Runtime();var first=Begin(runtime,0,0,.1f);Assert.Equal(AlsMotionWarpingModifierState.Disabled,first.Modifiers[0].State);runtime.Commit(first);
        var second=Begin(runtime,1,.1f,.2f,targets:[Target(200)]);Assert.Equal(AlsMotionWarpingModifierState.Disabled,second.Modifiers[0].State);Assert.Equal(AlsPrecisePose.Identity,second.Warped);runtime.Commit(second);
        var clear=Begin(runtime,2,0,0,asset:-1);Assert.Empty(clear.Modifiers);runtime.Commit(clear);
        var newWindow=Begin(runtime,3,0,.1f);Assert.Equal(2,newWindow.Modifiers[0].Id);Assert.Equal(50,newWindow.Warped.Position.X);
    }

    [Fact]
    public void NoExtractedRootSkipsComponentUpdateAndRetainsModifierHistory()
    {
        var runtime=Runtime();var first=Begin(runtime,0,0,.1f,targets:[Target(200)]);runtime.Commit(first);
        var absent=Begin(runtime,1,0,0,asset:-1,present:false);Assert.Equal(first.Modifiers.ToArray(),absent.Modifiers.ToArray());runtime.Commit(absent);
        var actuallyCalled=Begin(runtime,2,0,0,asset:-1);Assert.Empty(actuallyCalled.Modifiers);
    }

    [Fact]
    public void SourceFailureCannotConsumeModifierSerialOrLeavePendingFrame()
    {
        var source=new Source{Fail=true};var runtime=Runtime(source);
        Assert.Throws<InvalidOperationException>(()=>Begin(runtime,0,0,.1f,targets:[Target(200)]));Assert.Empty(runtime.Committed);
        source.Fail=false;var current=Begin(runtime,0,0,.1f,targets:[Target(200)]);Assert.Equal(1,current.Modifiers[0].Id);runtime.Commit(current);
    }

    [Fact]
    public void ForeignAndReplayedCandidatesCannotPublishHistory()
    {
        var first=Runtime();var second=Runtime();var a=Begin(first,0,0,.1f,targets:[Target(200)]);var b=Begin(second,0,0,.1f,targets:[Target(200)]);
        Assert.Throws<InvalidOperationException>(()=>first.Commit(b));Assert.Throws<InvalidOperationException>(()=>Begin(first,1,.1f,.2f));
        first.Commit(a);Assert.Throws<InvalidOperationException>(()=>first.Commit(a));Assert.Throws<InvalidOperationException>(()=>Begin(first,0,0,.1f));Assert.Single(first.Committed);
    }
}
