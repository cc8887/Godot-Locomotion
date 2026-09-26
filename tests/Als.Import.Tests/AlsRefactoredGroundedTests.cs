using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredGroundedTests
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),
        p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    private static readonly Lazy<AlsRefactoredGroundedResources> Data=new(()=>new(MantlingHostFixture.Read("refactored_locomotion_machines"),Catalog()));
    private static AlsRefactoredGroundedObservation[] Empty()=>[new(33,0,0),new(31,0,0)];
    private static AlsRefactoredGroundedInput Input(AlsRefactoredGroundedStance stance=AlsRefactoredGroundedStance.Standing,bool move=false,
        float stand=1,float crouch=0,bool roll=false)=>new(stance,move,false,false,roll,stand,crouch);

    [Fact]
    public void OriginalGroundedTopologyCurvesAndCallbacksAreLoaded()
    {
        var r=Data.Value;Assert.Equal(6,r.States.Length);Assert.Equal(20,r.Edges.Length);
        Assert.Equal(new[]{-1,40,37,34,32,30},r.States.ToArray().Select(s=>s.RootPropertyIndex));
        Assert.Equal(new[]{33,31},r.Players.ToArray().Select(p=>p.PropertyIndex));
        Assert.Equal("StopTransitionAndTurnInPlaceAnimations",r.States[3].EntryFunction);
        Assert.Equal("PlayRollToGroundedTransitionAnimation",r.States[5].ExitFunction);
        Assert.Equal(AlsTransitionBlend.Custom,r.Edges[4].Blend);Assert.Equal(.5f,r.Edges[4].Seconds);
        Assert.Equal(AlsTransitionBlend.Cubic,r.Edges[18].Blend);Assert.Equal(.6f,r.Edges[18].Seconds);
        Assert.Equal(.3f,r.Edges[5].Seconds);Assert.True(r.Edges[5].Inertialization);
        Assert.Equal(0,r.StanceCurve.Sample(0));Assert.Equal(1,r.StanceCurve.Sample(1));
        _=new AlsRefactoredGroundedRuntime(r);
    }

    [Fact]
    public void EntryConduitUsesPreviousPoseThenFallbackAndNeverInitializesItsPose()
    {
        foreach(var (input,expected) in new[]{(Input(move:true),1),(Input(move:true,stand:0,crouch:1),2),
            (Input(move:true,stand:0,crouch:0),1)})
        {
            var runtime=new AlsRefactoredGroundedRuntime(Data.Value);
            runtime.Prepare(0,input,Empty(),1f/60);
            var update=runtime.Candidate;Assert.Equal(expected,update.State.CurrentState);Assert.Equal(1,update.TransitionCount);
            Assert.Equal(0,update.State.Transitions.Count);Assert.Equal(1,update.UpdateCount);
            Assert.Equal(expected,update.GetUpdate(0).State);Assert.Equal(0,update.InitializeStates&1);
        }
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void StanceSwitchInterruptionsAndHiddenReentryAreTransactional(int hz)
    {
        var r=Data.Value;var runtime=new AlsRefactoredGroundedRuntime(r);var clean=new AlsRefactoredGroundedRuntime(r);
        var observations=Empty();var seen=new HashSet<int>();var edges=new HashSet<int>();
        var counter=new AlsGraphTraversalCounter(0,1);
        for(var frame=1;frame<=hz*8;frame++)
        {
            counter=counter.Next((ulong)frame);
            if(frame==hz*6)counter=counter.Next((ulong)frame);
            var t=frame/(float)hz;var moving=t<1||t>=4&&t<5;
            var crouch=t is >=1 and <2 or >=3 and <4 or >=4.5f and <5.5f;
            var input=Input(crouch?AlsRefactoredGroundedStance.Crouching:AlsRefactoredGroundedStance.Standing,moving,
                stand:crouch?0:1,crouch:crouch?1:0,roll:t>=6);
            runtime.Prepare(frame,input,observations,1f/hz,updateCounter:counter);
            var expected=runtime.Candidate;var callbacks=runtime.Callbacks.ToArray();var inertia=runtime.InertializationRequest;
            runtime.Cancel();runtime.Prepare(frame,input,observations,1f/hz,updateCounter:counter);
            clean.Prepare(frame,input,observations,1f/hz,updateCounter:counter);
            Assert.True(expected.State.Matches(runtime.Candidate.State));Assert.True(expected.State.Matches(clean.Candidate.State));
            Assert.Equal(callbacks,runtime.Callbacks.ToArray());Assert.Equal(inertia,runtime.InertializationRequest);
            seen.Add(expected.State.CurrentState);for(var e=0;e<expected.TransitionCount;e++)edges.Add(expected.GetTransitionIndex(e));
            for(var i=0;i<2;i++)
            {
                var p=r.Players[i];var reset=(expected.InitializeStates&(1<<p.State))!=0;
                var weight=0f;for(var u=0;u<expected.UpdateCount;u++)if(expected.GetUpdate(u).State==p.State)weight=expected.GetUpdate(u).Weight;
                var time=reset?0:observations[i].Time;
                if(weight>0)time=MathF.Min(p.Length,time+1f/hz*p.Rate);
                observations[i]=new(p.PropertyIndex,weight,time);
            }
            runtime.Commit(frame);clean.Commit(frame);
        }
        Assert.Contains(1,seen);Assert.Contains(2,seen);Assert.Contains(3,seen);Assert.Contains(4,seen);
        Assert.Contains(5,edges);Assert.Contains(7,edges);Assert.Contains(8,edges);Assert.Contains(13,edges);
    }

    [Fact]
    public void OriginalRulesPreserveTagsPoseThresholdsMotionAndAliasSemantics()
    {
        var r=Data.Value;
        foreach(var stance in Enum.GetValues<AlsRefactoredGroundedStance>())
        for(var bits=0;bits<16;bits++)
        foreach(var amount in new[]{0f,MathF.BitDecrement(.5f),.5f,1f})
        {
            var input=new AlsRefactoredGroundedInput(stance,(bits&1)!=0,(bits&2)!=0,(bits&4)!=0,(bits&8)!=0,amount,amount);
            var motion=(bits&7)!=0;var stand=stance is AlsRefactoredGroundedStance.Standing or AlsRefactoredGroundedStance.Invalid;
            var crouch=stance==AlsRefactoredGroundedStance.Crouching;
            bool[] expected=[input.FromRoll&&!motion,amount>=.5f,amount>=.5f,true,
                crouch&&motion,crouch,stand&&motion,stand,false,stand&&motion,crouch&&motion,stand,crouch,
                false,stand&&motion,crouch&&motion,stand,crouch,crouch,stand];
            for(var e=0;e<20;e++)Assert.Equal(expected[e],r.Edges[e].Rule.Evaluate(input));
        }
        Assert.Equal(Enumerable.Range(19,20),r.Edges.ToArray().Select(e=>e.RuleCompiledIndex));
        Assert.Equal(12,r.Edges.ToArray().Select(e=>e.RulePropertyIndex).Distinct().Count());
    }

    [Fact]
    public void EveryBakedEdgeKeepsItsPriorityCallbacksAndSelfTransitionPolicy()
    {
        var r=Data.Value;
        for(var e=0;e<20;e++)
        {
            var runtime=new AlsRefactoredGroundedRuntime(r);var edge=r.Edges[e];var frame=0L;
            if(edge.From!=0)
            {
                var first=edge.From switch {
                    2 or 4=>Input(move:true,stand:0,crouch:1),5=>Input(roll:true),_=>Input(move:true)};
                runtime.Prepare(frame,first,Empty(),1f/60);runtime.Commit(frame++);
                if(edge.From is 3 or 4)
                {
                    runtime.Prepare(frame,Input(edge.From==3?AlsRefactoredGroundedStance.Crouching:AlsRefactoredGroundedStance.Standing),Empty(),1f/60);
                    runtime.Commit(frame++);
                }
                Assert.Equal(edge.From,runtime.CommittedState.CurrentState);
            }
            var input=e switch {
                0=>Input(roll:true),1=>Input(move:true,stand:1,crouch:1),2=>Input(move:true),3=>Input(move:true,stand:0),
                4 or 10 or 15=>Input(AlsRefactoredGroundedStance.Crouching,true),
                5 or 12 or 17 or 18=>Input(AlsRefactoredGroundedStance.Crouching),
                6 or 9 or 14=>Input(move:true),_=>Input()};
            var observations=Empty();
            if(e is 8 or 13)
            {
                var i=e==8?0:1;var player=r.Players[i];observations[i]=new(player.PropertyIndex,1,player.Length);
            }
            runtime.Prepare(frame,input,observations,1f/60);
            var result=runtime.Candidate;
            Assert.Equal(edge.To,result.State.CurrentState);
            if(e==4)
            {
                var crouchWeight=0f;
                for(var u=0;u<result.UpdateCount;u++)if(result.GetUpdate(u).State==2)crouchWeight=result.GetUpdate(u).Weight;
                Assert.Equal(r.StanceCurve.Sample((1f/60)/.5f),crouchWeight);
            }
            if(edge.From==edge.To)
            {
                Assert.Equal(0,result.TransitionCount);Assert.Equal(0,result.InitializeStates);
                Assert.Empty(runtime.Callbacks.ToArray());Assert.Null(runtime.InertializationRequest);
            }
            else
            {
                Assert.Equal(1,result.TransitionCount);Assert.Equal(e,result.GetTransitionIndex(0));
                Assert.Equal(edge.Inertialization,runtime.InertializationRequest.HasValue);
                var expectedCallbacks=new List<AlsRefactoredGroundedCallback>();
                if(edge.From==5)expectedCallbacks.Add(new(5,false,"PlayRollToGroundedTransitionAnimation"));
                if(edge.To is 3 or 4)expectedCallbacks.Add(new(edge.To,true,"StopTransitionAndTurnInPlaceAnimations"));
                Assert.Equal(expectedCallbacks,runtime.Callbacks.ToArray());
            }
            runtime.Cancel();Assert.Throws<InvalidOperationException>(()=>runtime.Candidate);
        }
    }

    [Fact]
    public void AutomaticExitUsesPreviousPositiveWeightAndRejectsForeignObservations()
    {
        var runtime=new AlsRefactoredGroundedRuntime(Data.Value);
        runtime.Prepare(0,Input(move:true),Empty(),1f/60);runtime.Commit(0);
        runtime.Prepare(1,Input(AlsRefactoredGroundedStance.Crouching),Empty(),1f/60);runtime.Commit(1);
        var player=Data.Value.Players[0];var observations=Empty();
        observations[0]=new(player.PropertyIndex,0,player.Length);
        runtime.Prepare(2,Input(AlsRefactoredGroundedStance.Crouching),observations,1f/60);
        Assert.Equal(3,runtime.Candidate.State.CurrentState);runtime.Cancel();
        observations[0]=new(player.PropertyIndex,float.Epsilon,player.Length);
        runtime.Prepare(2,Input(AlsRefactoredGroundedStance.Standing,true),observations,1f/60);
        Assert.Equal(2,runtime.Candidate.State.CurrentState); // completed sequence wins before moving/stance alias
        Assert.Equal(8,runtime.Candidate.GetTransitionIndex(0));runtime.Cancel();
        observations[0]=new(-1,1,player.Length);
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(2,Input(),observations,1f/60));
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(2,Input(stand:float.NaN),Empty(),1f/60));
        runtime.Prepare(2,Input(),Empty(),1f/60);
        Assert.Throws<ArgumentException>(()=>runtime.Commit(3));runtime.Commit(2);
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(2,Input(),Empty(),1f/60));
    }

    [Fact]
    public void SourcePolicyAndAliasChangesAreRejectedEvenWithValidHashes()
    {
        _=Data.Value;
        foreach(var change in new[]{"alias","threshold","rate","callback"})
        {
            var original=Catalog();var payload=JsonNode.Parse(original.Read(AlsRefactoredGroundedResources.Source).GetRawText())!;
            var unchanged=payload.ToJsonString();
            var nodes=payload["compiled"]!["nodes"]!.AsArray();
            JsonNode Node(int id)=>nodes.Single(n=>n!["propertyIndex"]!.GetValue<int>()==id)!;
            switch(change)
            {
                case "alias":
                    var text=payload["nativeText"]!.GetValue<string>();
                    var start=text.IndexOf("AliasedStateNodes=",StringComparison.Ordinal);
                    var end=text.IndexOf('\n',start);var line=text[start..end];
                    payload["nativeText"]=text[..start]+line.Replace("AnimStateNode_2'","AnimStateNode_1'",StringComparison.Ordinal)+text[end..];break;
                case "threshold": payload["nativeText"]=payload["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"0.500000\"","DefaultValue=\"0.600000\"",StringComparison.Ordinal);break;
                case "rate": Node(33)["runtime"]!["playRate"]=1;break;
                case "callback": Node(34)["runtime"]!["stateEntryFunction"]!["functionName"]="None";break;
            }
            Assert.NotEqual(unchanged,payload.ToJsonString());
            var bytes=Encoding.UTF8.GetBytes(payload.ToJsonString());
            var index=JsonNode.Parse(MantlingHostFixture.Read("refactored_animation_sources"))!;
            var entry=index["assets"]!.AsArray().Single(n=>n!["source"]!.GetValue<string>()==AlsRefactoredGroundedResources.Source)!;
            entry["sha256"]=Convert.ToHexString(SHA256.HashData(bytes));var file=entry["file"]!.GetValue<string>();
            var catalog=new AlsRefactoredAnimationCatalog(index.ToJsonString(),p=>p==file?bytes:File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
            var export=JsonNode.Parse(MantlingHostFixture.Read("refactored_locomotion_machines"))!;export["catalogSha256"]=catalog.IndexDigest;
            Assert.Throws<ArgumentException>(()=>new AlsRefactoredGroundedResources(export.ToJsonString(),catalog));
        }
    }

    [Fact]
    public void ExportPolicyMutationsAreRejected()
    {
        _=Data.Value; // A broken baseline must never make negative tests pass.
        var mutations=new Action<JsonNode>[] {
            n=>n["catalogSha256"]="wrong",
            n=>n["graphs"]![0]!["bakedMachines"]![0]!["initialState"]=1,
            n=>n["graphs"]![0]!["bakedMachines"]![0]!["transitions"]![4]!["crossfadeDuration"]=.2,
            n=>n["graphs"]![0]!["bakedMachines"]![0]!["states"]![3]!["transitions"]![0]!["bAutomaticRemainingTimeRule"]=false,
            n=>n["graphs"]![0]!["bakedMachines"]![0]!["states"]![3]!["playerNodeIndices"]![0]=13,
            n=>n["graphs"]![0]!["bakedMachines"]![0]!["states"]![1]!["transitions"]![0]!["canTakeDelegateIndex"]=33,
            n=>n["graphs"]![0]!["curves"]![0]!["verification"]![80]!["value"]=.8,
        };
        foreach(var mutate in mutations){var node=JsonNode.Parse(MantlingHostFixture.Read("refactored_locomotion_machines"))!;mutate(node);
            Assert.ThrowsAny<ArgumentException>(()=>new AlsRefactoredGroundedResources(node.ToJsonString(),Catalog()));}
    }
}
