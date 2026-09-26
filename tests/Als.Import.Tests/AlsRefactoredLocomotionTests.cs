using System.Text.Json.Nodes;
using System.Numerics;
using System.Text;
using System.Security.Cryptography;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLocomotionTests
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),
        p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    private static readonly Lazy<AlsRefactoredLocomotionResources> Main=new(()=>new(MantlingHostFixture.Read("refactored_locomotion_machines"),Catalog()));
    private static readonly Lazy<AlsRefactoredLocomotionResources> Jump=new(()=>new(MantlingHostFixture.Read("refactored_locomotion_machines"),Catalog(),true));
    private static AlsRefactoredLocomotionInput Input(AlsRefactoredLocomotionMode mode=AlsRefactoredLocomotionMode.Grounded,
        bool jumped=false,bool move=false,float foot=0)=>new(mode,AlsRefactoredGroundedStance.Standing,jumped,move,false,false,false,0,foot);
    private static AlsRefactoredLocomotionObservation[] Empty(AlsRefactoredLocomotionResources r)=>r.TimingPlayers.ToArray().Select(p=>new AlsRefactoredLocomotionObservation(p.PropertyIndex,0,0)).ToArray();
    private static readonly Lazy<AlsRefactoredSyncBank> Sync=new(()=>new(MantlingHostFixture.Read("refactored_sync_inputs"),Catalog()));

    [Fact]
    public void OriginalMachinesKeepAliasesStatePlayerOrderAndBlendPolicies()
    {
        var m=Main.Value;var j=Jump.Value;
        Assert.Equal(16,m.Edges.Length);Assert.Equal(5,j.Edges.Length);
        Assert.Equal(new[]{82,80,65,35,29,-1},m.States.ToArray().Select(s=>s.RootPropertyIndex));
        Assert.Equal(new[]{-1,63,59,55,53},j.States.ToArray().Select(s=>s.RootPropertyIndex));
        Assert.Equal(Enumerable.Range(63,16),m.Edges.ToArray().Select(e=>e.RuleCompiledIndex));
        Assert.Equal(11,m.Edges.ToArray().Select(e=>e.RulePropertyIndex).Distinct().Count());
        Assert.Equal(new[]{34,33,28,27},m.TimingPlayers.ToArray().Select(p=>p.PropertyIndex));
        Assert.Equal(new[]{62,61,58,57},j.TimingPlayers.ToArray().Select(p=>p.PropertyIndex));
        Assert.True(m.States[2].AlwaysResetOnEntry);Assert.Equal("StopTransitionAndTurnInPlaceAnimations",m.States[0].ExitFunction);
        Assert.Equal(.8f,m.Edges[7].Seconds);Assert.True(m.Edges[7].QuickFeet);Assert.NotNull(m.QuickFeet);
        Assert.Equal(0,m.Edges[7].TriggerTime);Assert.Equal(-1,m.Edges[11].TriggerTime);Assert.Equal(1,j.Edges[4].Seconds);
        _=new AlsRefactoredLocomotionRuntime(m);_=new AlsRefactoredLocomotionRuntime(j);
    }

    [Fact]
    public void RuleTruthTableKeepsExactTagsInputPlatformAndFootThresholds()
    {
        var m=Main.Value;var j=Jump.Value;
        foreach(var mode in Enum.GetValues<AlsRefactoredLocomotionMode>())
        foreach(var stance in Enum.GetValues<AlsRefactoredGroundedStance>())
        for(var bits=0;bits<32;bits++)
        foreach(var speed in new[]{0f,MathF.BitDecrement(650),650f})
        {
            var input=new AlsRefactoredLocomotionInput(mode,stance,(bits&1)!=0,(bits&2)!=0,(bits&4)!=0,(bits&8)!=0,(bits&16)!=0,speed,0);
            var air=mode==AlsRefactoredLocomotionMode.InAir;var ground=mode==AlsRefactoredLocomotionMode.Grounded;
            bool[] expected=[air&&input.Jumped,air,true,ground,true,ground,stance!=AlsRefactoredGroundedStance.Standing,false,
                input.HasInput||input.RotatingLeft||input.RotatingRight||speed>=650,air&&input.Jumped,air,false,air&&input.Jumped,air,
                input.HasInput||input.HasRelativeLocation,true];
            for(var e=0;e<expected.Length;e++)Assert.Equal(expected[e],m.Edges[e].Rule.Evaluate(input));
            Assert.Equal(ground,m.States[5].EntryRule!.Evaluate(input));
        }
        foreach(var foot in new[]{-1f,-float.Epsilon,0,float.Epsilon,1})
        {
            var input=Input(foot:foot);Assert.Equal(foot>0,j.Edges[0].Rule.Evaluate(input));
            Assert.Equal(foot<=0,j.Edges[1].Rule.Evaluate(input));Assert.True(j.Edges[4].Rule.Evaluate(input));
        }
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void LandConduitUsesGroundedGateAndFinalEdgeInertia(bool platform)
    {
        var r=new AlsRefactoredLocomotionRuntime(Main.Value);var empty=Empty(Main.Value);
        r.Prepare(0,Input(AlsRefactoredLocomotionMode.InAir),empty,1f/60);Assert.Equal(1,r.Candidate.State.CurrentState);r.Commit(0);
        r.Prepare(1,Input(AlsRefactoredLocomotionMode.Other),empty,1f/60);Assert.Equal(1,r.Candidate.State.CurrentState);r.Commit(1);
        r.Prepare(2,Input() with {HasRelativeLocation=platform},empty,1f/60);
        Assert.Equal(platform?4:3,r.Candidate.State.CurrentState);Assert.Equal(platform?14:15,r.Candidate.GetTransitionIndex(0));
        Assert.Equal(.1f,r.InertializationRequest!.Value.Duration);Assert.Equal(0,r.Candidate.InitializeStates&(1<<5));
        Assert.Empty(r.Callbacks.ToArray());
    }

    [Theory]
    [InlineData(-1,1,2)][InlineData(0,1,2)][InlineData(1,2,3)]
    public void JumpUsesPreviousFootCurveAndAutomaticExitEntersOneSecondFlail(int foot,int state,int edge)
    {
        var r=new AlsRefactoredLocomotionRuntime(Jump.Value);var o=Empty(Jump.Value);
        r.Prepare(0,Input(foot:foot),o,1f/60);Assert.Equal(state,r.Candidate.State.CurrentState);
        Assert.Equal(0,r.Candidate.InitializeStates&1);r.Commit(0);
        var index=state==1?0:2;var p=Jump.Value.TimingPlayers[index];
        o[index]=new(p.PropertyIndex,float.Epsilon,p.Length);
        r.Prepare(1,Input(foot:-foot),o,1f/60);var next=r.Candidate;
        Assert.Equal(4,next.State.CurrentState);Assert.Equal(2,next.TransitionCount);
        Assert.Equal(edge,next.GetTransitionIndex(0));Assert.Equal(4,next.GetTransitionIndex(1));
        Assert.Equal(.2f,r.InertializationRequest!.Value.Duration);
        Assert.True(next.State.Transitions.Count>0);Assert.Equal(1,next.State.Transitions.Latest.Duration);
    }

    [Fact]
    public void AutomaticRulesUseStrictMaximumFirstTieAndPreviousCachedTime()
    {
        var r=new AlsRefactoredLocomotionRuntime(Jump.Value);var o=Empty(Jump.Value);
        r.Prepare(0,Input(),o,1f/60);r.Commit(0);
        var a=Jump.Value.TimingPlayers[0];var b=Jump.Value.TimingPlayers[1];
        o[0]=new(a.PropertyIndex,.5f,0);o[1]=new(b.PropertyIndex,.5f,b.Length);
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(1,r.Candidate.State.CurrentState);Assert.Equal(a.PropertyIndex,r.SelectedPlayerProperties[1]);r.Cancel();
        o[1]=o[1] with {CachedWeight=MathF.BitIncrement(.5f)};
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(4,r.Candidate.State.CurrentState);r.Cancel();
        o[0]=new(a.PropertyIndex,0,a.Length);o[1]=new(b.PropertyIndex,0,b.Length);
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(1,r.Candidate.State.CurrentState);Assert.Equal(-1,r.SelectedPlayerProperties[1]);r.Cancel();
        o[0]=new(a.PropertyIndex,float.Epsilon,MathF.BitDecrement(a.Length));
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(1,r.Candidate.State.CurrentState);r.Cancel();
        o[0]=o[0] with {Time=a.Length};
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(4,r.Candidate.State.CurrentState);r.Cancel();
        r.Prepare(1,Input(),o,1f/60,initialize:true);Assert.Equal(1,r.Candidate.State.CurrentState);
    }

    [Fact]
    public void LandingCompletionQuickFeetNotificationAndImmediateJumpKeepPriority()
    {
        var r=new AlsRefactoredLocomotionRuntime(Main.Value);var o=Empty(Main.Value);
        r.Prepare(0,Input(AlsRefactoredLocomotionMode.InAir),o,1f/60);r.Commit(0);
        r.Prepare(1,Input(),o,1f/60);Assert.Equal(3,r.Candidate.State.CurrentState);r.Commit(1);
        var p=Main.Value.TimingPlayers[0];o[0]=new(p.PropertyIndex,1,p.Length);
        r.Prepare(2,Input(AlsRefactoredLocomotionMode.InAir,true),o,1f/60);var next=r.Candidate;
        Assert.Equal(2,next.TransitionCount);Assert.Equal(7,next.GetTransitionIndex(0));Assert.Equal(0,next.GetTransitionIndex(1));
        Assert.Equal(2,next.State.CurrentState);Assert.Equal(1,next.EventCount);
        Assert.Equal(new AlsGroundedMachineEvent(AlsGroundedEventKind.TransitionStarted,7,0),next.GetEvent(0));
        Assert.Single(r.Callbacks.ToArray());Assert.Equal(0,r.Callbacks[0].State);Assert.False(r.Callbacks[0].Entry);
        r.Cancel();r.Prepare(2,Input() with {Stance=AlsRefactoredGroundedStance.Invalid},o,1f/60);
        Assert.Equal(6,r.Candidate.GetTransitionIndex(0));Assert.Equal(0,r.Candidate.EventCount);
    }

    [Fact]
    public void CandidateOwnershipRejectsForeignSourcesFramesAndInputWithoutCommitting()
    {
        var r=new AlsRefactoredLocomotionRuntime(Main.Value);var o=Empty(Main.Value);
        Assert.Throws<ArgumentException>(()=>r.Prepare(0,Input() with {FootPlanted=float.NaN},o,1f/60));
        o[0]=o[0] with {PropertyIndex=-1};Assert.Throws<ArgumentException>(()=>r.Prepare(0,Input(),o,1f/60));o=Empty(Main.Value);
        r.Prepare(0,Input(),o,1f/60);Assert.Throws<ArgumentException>(()=>r.Commit(1));r.Cancel();
        Assert.False(r.CommittedState.HasUpdated);Assert.Throws<InvalidOperationException>(()=>r.Candidate);
        r.Prepare(0,Input(),o,1f/60);r.Commit(0);Assert.Throws<ArgumentException>(()=>r.Prepare(0,Input(),o,1f/60));
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void RealSyncClocksDriveNestedJumpAndBothLandingModesWithAtomicRetry(int hz)
    {
        var main=new AlsRefactoredLocomotionRuntime(Main.Value);var jump=new AlsRefactoredLocomotionRuntime(Jump.Value);
        var definitions=Main.Value.TimingPlayers.ToArray().Concat(Jump.Value.TimingPlayers.ToArray()).ToArray();
        var players=new AlsRefactoredSourcePlayerRuntime(Catalog(),Sync.Value,new Dictionary<string,AlsRefactoredTriangulationProfile>(),
            definitions.Select((p,i)=>new AlsRefactoredSourcePlayerDefinition(i,p.Source,p.Group=="Jump"?7:11,p.StartPosition,p.Loop)));
        var mainPrior=Empty(Main.Value);var jumpPrior=Empty(Jump.Value);var pending=new bool[8];
        var counter=new AlsGraphTraversalCounter(0,0);var mainStates=new HashSet<int>();var jumpStates=new HashSet<int>();
        var seenPlayers=new HashSet<int>();var automatic=new HashSet<int>();var multi=0;var hiddenReset=0;var realSamples=0;
        for(var frame=0;frame<hz*16;frame++)
        {
            var t=frame/(float)hz;var cycle=t%8;
            var air=cycle is >=.5f and <3 or >=5 and <6;
            var input=Input(air?AlsRefactoredLocomotionMode.InAir:AlsRefactoredLocomotionMode.Grounded,
                jumped:cycle<3,move:cycle>=6,foot:t<8?-1:1);
            if(frame==hz*5-1)counter=counter.Next((ulong)frame); // skipped graph update / relevant reentry
            var delta=1f/hz;var outerWeight=frame%31==0?0:1f;
            main.Prepare(frame,input,mainPrior,delta,outerWeight,updateCounter:counter);
            var m=main.Candidate;var mw=Weight(m,2);var jumpActive=mw.HasValue;
            if(jumpActive)jump.Prepare(frame,input,jumpPrior,delta,mw!.Value,
                initialize:(m.InitializeStates&(1<<2))!=0,updateCounter:counter);
            var j=jumpActive?jump.Candidate:default;
            var callbacks=main.Callbacks.ToArray();var request=main.InertializationRequest;
            var nextPending=(bool[])pending.Clone();var mainNext=(AlsRefactoredLocomotionObservation[])mainPrior.Clone();
            var jumpNext=(AlsRefactoredLocomotionObservation[])jumpPrior.Clone();var ticks=new List<AlsRefactoredSourcePlayerInput>();
            for(var i=0;i<definitions.Length;i++)
            {
                var d=definitions[i];var nested=i>=4;var active=!nested||jumpActive;var update=nested?j:m;
                if(!active)continue;
                if((update.ClearCachedWeightStates&(1<<d.State))!=0)
                {
                    if(nested)jumpNext[i-4]=jumpNext[i-4] with {CachedWeight=0};else mainNext[i]=mainNext[i] with {CachedWeight=0};
                }
                if((update.InitializeStates&(1<<d.State))!=0)nextPending[i]=true;
                var stateWeight=Weight(update,d.State);if(!stateWeight.HasValue)continue;
                // Controlled two-way channels. Production traversal/Parent alphas
                // remain the next host task; the clocks below are real Sync ticks.
                var blend=(frame%hz)/(float)hz;var weight=stateWeight.Value*(i%2==0?1-blend:blend);
                if(weight<=1e-5f&&outerWeight!=0)continue;
                ticks.Add(new(i,Vector2.Zero,d.Rate,weight,nextPending[i],d.StartPosition));nextPending[i]=false;
            }
            var batch=ticks.ToArray();players.Prepare(frame,batch,delta,m.Reinitialized);
            var histories=players.Players.ToArray();
            if(frame%7==0)foreach(var tick in batch){players.Evaluate(frame,tick.PlayerId);Assert.Equal(79,players.Pose(tick.PlayerId).Length);realSamples++;}
            players.Cancel();main.Cancel();if(jumpActive)jump.Cancel();
            main.Prepare(frame,input,mainPrior,delta,outerWeight,updateCounter:counter);
            Assert.True(m.State.Matches(main.Candidate.State));Assert.Equal(callbacks,main.Callbacks.ToArray());Assert.Equal(request,main.InertializationRequest);
            if(jumpActive)
            {
                jump.Prepare(frame,input,jumpPrior,delta,mw!.Value,initialize:(m.InitializeStates&(1<<2))!=0,updateCounter:counter);
                Assert.True(j.State.Matches(jump.Candidate.State));jumpStates.Add(j.State.CurrentState);
                if(j.Reinitialized)hiddenReset++;
            }
            players.Prepare(frame,batch,delta,m.Reinitialized);Assert.Equal(histories,players.Players.ToArray());
            foreach(var tick in batch)
            {
                var history=Assert.Single(histories,h=>h.PlayerId==tick.PlayerId);var d=definitions[tick.PlayerId];
                var observation=new AlsRefactoredLocomotionObservation(d.PropertyIndex,tick.Weight,history.Time);
                if(tick.PlayerId<4)mainNext[tick.PlayerId]=observation;else jumpNext[tick.PlayerId-4]=observation;
                seenPlayers.Add(tick.PlayerId);
            }
            mainStates.Add(m.State.CurrentState);if(m.UpdateCount>1)multi++;
            for(var e=0;e<m.TransitionCount;e++)if(Main.Value.Edges[m.GetTransitionIndex(e)].Automatic)automatic.Add(m.GetTransitionIndex(e));
            main.ValidateCommit(frame);if(jumpActive)jump.ValidateCommit(frame);players.ValidateCommit(frame);
            main.Commit(frame);if(jumpActive)jump.Commit(frame);players.Commit(frame);
            mainPrior=mainNext;jumpPrior=jumpNext;pending=nextPending;counter=counter.Next((ulong)frame+1);
        }
        Assert.Equal(new[]{0,1,2,3,4},mainStates.Order());Assert.Contains(1,jumpStates);Assert.Contains(2,jumpStates);Assert.Contains(4,jumpStates);
        Assert.Equal(8,seenPlayers.Count);Assert.Equal(new[]{7,11},automatic.Order());Assert.True(multi>0);Assert.True(hiddenReset>=2);Assert.True(realSamples>0);
        static float? Weight(AlsGroundedMachineUpdate update,int state)
        {for(var u=0;u<update.UpdateCount;u++)if(update.GetUpdate(u).State==state)return update.GetUpdate(u).Weight;return null;}
    }

    [Fact]
    public void BakedPolicyPrioritySourceInventoryAndCatalogMutationsAreRejected()
    {
        _=Main.Value;
        foreach(var change in new[]{"catalog","trigger","reset","edge","source","order"})
        {
            var json=JsonNode.Parse(MantlingHostFixture.Read("refactored_locomotion_machines"))!;
            var machine=json["graphs"]![1]!["bakedMachines"]![0]!;
            switch(change)
            {
                case "catalog":json["catalogSha256"]=new string('0',64);break;
                case "trigger":machine["states"]![3]!["transitions"]![1]!["automaticRuleTriggerTime"]=-1;break;
                case "reset":machine["states"]![2]!["bAlwaysResetOnEntry"]=false;break;
                case "edge":machine["transitions"]![14]!["crossfadeDuration"]=.2;break;
                case "source":machine["states"]![3]!["playerNodeIndices"]![0]=50;break;
                case "order":machine["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"]=64;break;
            }
            Assert.Throws<ArgumentException>(()=>new AlsRefactoredLocomotionResources(json.ToJsonString(),Catalog()));
        }
    }

    [Fact]
    public void AuthoredAliasThresholdCallbacksAndSourcePoliciesCannotDriftBehindValidHashes()
    {
        _=Main.Value;_=Jump.Value;
        foreach(var change in new[]{"alias","threshold","callback","rate","start","sync","initial"})
        {
            var payload=JsonNode.Parse(Catalog().Read(AlsRefactoredLocomotionResources.Source).GetRawText())!;
            var unchanged=payload.ToJsonString();var nodes=payload["compiled"]!["nodes"]!.AsArray();
            JsonNode Node(int id)=>nodes.Single(n=>n!["propertyIndex"]!.GetValue<int>()==id)!;
            switch(change)
            {
                case "alias":
                    var text=payload["nativeText"]!.GetValue<string>();var start=text.IndexOf("AliasedStateNodes=",StringComparison.Ordinal);
                    var end=text.IndexOf('\n',start);var line=text[start..end];
                    payload["nativeText"]=text[..start]+line.Replace("AnimStateNode_2'","AnimStateNode_4'",StringComparison.Ordinal)+text[end..];break;
                case "threshold":payload["nativeText"]=payload["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"650.000000\"","DefaultValue=\"600.000000\"",StringComparison.Ordinal);break;
                case "callback":Node(82)["runtime"]!["stateExitFunction"]!["functionName"]="None";break;
                case "rate":Node(28)["runtime"]!["playRate"]=1;break;
                case "start":Node(62)["runtime"]!["startPosition"]=0;break;
                case "sync":Node(62)["runtime"]!["groupName"]="Movement";break;
                case "initial":Node(64)["runtime"]!["maxTransitionsPerFrame"]=1;break;
            }
            Assert.NotEqual(unchanged,payload.ToJsonString());var bytes=Encoding.UTF8.GetBytes(payload.ToJsonString());
            var index=JsonNode.Parse(MantlingHostFixture.Read("refactored_animation_sources"))!;
            var entry=index["assets"]!.AsArray().Single(n=>n!["source"]!.GetValue<string>()==AlsRefactoredLocomotionResources.Source)!;
            entry["sha256"]=Convert.ToHexString(SHA256.HashData(bytes));var file=entry["file"]!.GetValue<string>();
            var catalog=new AlsRefactoredAnimationCatalog(index.ToJsonString(),p=>p==file?bytes:File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
            var export=JsonNode.Parse(MantlingHostFixture.Read("refactored_locomotion_machines"))!;export["catalogSha256"]=catalog.IndexDigest;
            Assert.Throws<ArgumentException>(()=>new AlsRefactoredLocomotionResources(export.ToJsonString(),catalog,change is "start" or "sync" or "initial"));
        }
    }

    [Fact]
    public void OriginalInputDomainCannotBePassedThroughTheV4OrGroundedEntryPoints()
    {
        var r=new AlsRefactoredLocomotionRuntime(Main.Value);var times=new AlsGroundedAutomaticTime[6];
        Assert.Throws<ArgumentException>(()=>AlsGroundedStateMachine.Update(r.Definition,default,default,times,1,.01f,0));
        Assert.Throws<ArgumentException>(()=>AlsGroundedStateMachine.UpdateRefactoredGrounded(r.Definition,default,default,times,1,.01f,0,x=>x));
        var states=r.Definition.States.ToArray();states[5]=states[5] with {RefactoredLocomotionEntryRule=null};
        Assert.Throws<ArgumentException>(()=>new AlsGroundedMachineDefinition(AlsGroundedMachineKind.RefactoredLocomotion,0,3,true,states,r.Definition.Edges.ToArray()));
    }
}
