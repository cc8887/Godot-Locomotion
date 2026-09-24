using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingBranchTests
{
    private static string Read(string file)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+file+".json"));
    private static (AlsMantlingMontageProfile Profile,AlsMantlingBranchingRuntime Branch,AlsMontageRuntime Bank) Create()
    {
        var json=Read("refactored_mantle_animation_inputs");
        var profile=AlsMantlingMontageCompiler.Compile(json,Read("refactored_mantle_root_tracks"),Read("refactored_mantle_curves"));
        var branch=AlsMantlingBranchCompiler.Compile(json,profile);return(profile,branch,profile.CreateRuntime(branch));
    }
    private static AlsMantlingBranchInputs Idle=>new(false,"Als.LocomotionMode.Grounded","Als.RotationMode.LookingDirection","Als.Stance.Standing");
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)]
    public void ActualStatesBeginEndAndEachAuthoredConditionStopsTheExactInstance(int condition)
    {
        foreach(var path in Create().Profile.Definitions.Keys)
        {
            var (profile,branch,bank)=Create();var asset=profile.Definitions[path].Asset;
            var inputs=condition switch
            {1=>Idle with{HasInput=true},2=>Idle with{LocomotionMode="als.locomotionmode.inair"},
                3=>Idle with{RotationMode="Als.RotationMode.Aiming"},4=>Idle with{Stance="Als.Stance.Crouching"},_=>Idle};
            branch.Capture(inputs);bank.Begin(new(1,1,1),1f/60);bank.PlayAction(asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
            Assert.Equal("",branch.CommittedAction);var events=new List<AlsMantlingBranchEvent>();var stopped=false;var actionObserved=false;
            for(var frame=2;frame<=240;frame++)
            {
                bank.Begin(new(frame,1,1),1f/60);events.AddRange(branch.CandidateEvents.ToArray());
                if(branch.CandidateAction=="Als.LocomotionAction.Mantling")actionObserved=true;
                if(bank.Candidate.Length>0&&bank.Candidate[0].Interrupted)
                {
                    stopped=true;Assert.Equal(1,bank.Candidate[0].InstanceId);
                    Assert.Equal(path.Contains("_High.")?.4f:.35f,bank.Candidate[0].BlendTime);
                    Assert.Equal(asset.Lifecycle.BlendOutOption,bank.Candidate[0].Settings.BlendOutOption);
                }
                bank.Commit(new(frame,1,1));
            }
            Assert.True(actionObserved);Assert.Equal(condition!=0,stopped);Assert.Empty(bank.Committed.ToArray());Assert.Equal("",branch.CommittedAction);
            Assert.Equal(new[]{(0,true),(0,false),(1,true),(1,false)},events.Select(e=>(e.StateIndex,e.Begin)).ToArray());
        }
    }
    [Fact]
    public void DiscardRestoresActionAndActiveStatesAndRetryReplaysSameEvents()
    {
        var(profile,branch,bank)=Create();branch.Capture(Idle);
        bank.Begin(new(1,1,1),.1f);bank.PlayAction(profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),.1f);var expected=branch.CandidateEvents.ToArray();Assert.Single(expected);
        Assert.Equal("Als.LocomotionAction.Mantling",branch.CandidateAction);bank.Discard();Assert.Equal("",branch.CommittedAction);
        bank.Begin(new(2,1,1),.1f);Assert.Equal(expected,branch.CandidateEvents.ToArray());bank.Commit(new(2,1,1));
        bank.ClearForLifecycle();Assert.Equal("",branch.CommittedAction);
    }
    [Fact]
    public void InterruptedStateWaitsForTerminationAndDoesNotClearAnExternalActionTag()
    {
        var(profile,branch,bank)=Create();branch.Capture(Idle);
        bank.Begin(new(1,1,1),.1f);bank.PlayAction(profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),.1f);bank.StopInstance(1,.4f,AlsActionBlendOption.Cubic);bank.Commit(new(2,1,1));
        branch.Capture(Idle,"Als.LocomotionAction.Rolling");var ends=0;
        for(var frame=3;frame<15;frame++)
        {
            bank.Begin(new(frame,1,1),.1f);
            foreach(var e in branch.CandidateEvents){Assert.False(e.Begin);ends++;Assert.True(bank.Candidate.IsEmpty);}
            bank.Commit(new(frame,1,1));
        }
        Assert.Equal(1,ends);Assert.Equal("Als.LocomotionAction.Rolling",branch.CommittedAction);
    }
    [Fact]
    public void CrossingAnEntireEarlyWindowDoesNotTickTheAlreadyEndedState()
    {
        var(profile,branch,bank)=Create();branch.Capture(Idle with{HasInput=true});
        bank.Begin(new(1,1,1),.1f);bank.PlayAction(profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),4);
        Assert.Equal(4,branch.CandidateEvents.Length);Assert.False(bank.Candidate[0].Interrupted);Assert.Equal("",branch.CandidateAction);
        bank.Discard();Assert.Equal("",branch.CommittedAction);
    }
}
