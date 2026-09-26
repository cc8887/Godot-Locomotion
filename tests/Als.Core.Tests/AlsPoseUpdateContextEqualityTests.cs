using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsPoseUpdateContextEqualityTests
{
    [Fact]
    public void ContextKeysIncludeAncestorsMessagesAndTraversalWithoutBoxingInlineArrays()
    {
        var root=new AlsPoseUpdateContext(new AlsFrameIdentity(7,19,1),.8f,1f/60).WithUpdateCounter(new(2,7));
        var first=root.WithState(65,1).WithState(117,4,true).WithInertialization(119,true);
        var copy=root.WithState(65,1).WithState(117,4,true).WithInertialization(119,true);
        Assert.Equal(first,copy);Assert.Equal(first.GetHashCode(),copy.GetHashCode());
        var keys=new HashSet<AlsPoseUpdateContext>{first,copy,first.AsInactive(),first.WithWeight(.5f),
            root.WithState(65,2).WithState(117,4,true).WithInertialization(119,true),first.WithInertialization(118,false)};
        Assert.Equal(5,keys.Count);
        var full=root;for(var i=0;i<16;i++)full=full.WithState(i,i);
        Assert.Equal(full,full.WithWeight(.8f));Assert.NotEqual(full,root);
    }

    [Fact]
    public void MachineSnapshotComparisonIncludesActiveBlendClocks()
    {
        var definition=new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main,0,1,false,
            [new(false,AlsGroundedCondition.Never,0,1,-1,-1,-1),new(false,AlsGroundedCondition.Never,1,0,-1,-1,-1)],
            [new(0,1,AlsGroundedCondition.ShouldMove,0,.3f,AlsTransitionBlend.Linear,false,-1,-1,-1)]);
        var initial=AlsGroundedStateMachine.Initialize(definition).State;
        var rule=new AlsGroundedRuleInput(true,false,false,AlsStance.Standing,true,false,0,0);
        var first=AlsGroundedStateMachine.Update(definition,initial,rule,new AlsGroundedAutomaticTime[2],1,.01f,0).State;
        var repeat=AlsGroundedStateMachine.Update(definition,initial,rule,new AlsGroundedAutomaticTime[2],1,.01f,0).State;
        var different=AlsGroundedStateMachine.Update(definition,initial,rule,new AlsGroundedAutomaticTime[2],1,.02f,0).State;
        Assert.True(first.Matches(repeat));Assert.False(first.Matches(initial));Assert.False(first.Matches(different));
    }
}
