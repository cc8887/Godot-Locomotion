using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRestParentTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog = new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        public readonly AlsRefactoredRestSettings Settings;
        public readonly AlsRefactoredStanceCallbacks Standing, Crouching;
        public Fixture() { Settings=new(MantlingHostFixture.Read("refactored_rest_settings"),Catalog);Standing=new(Catalog,false);Crouching=new(Catalog,true); }
        public AlsRefactoredRestParentRuntime Runtime()=>new(Settings,Standing,Crouching);
        public AlsRefactoredStanceCallback Command(AlsRefactoredStanceFunction function,bool crouching=false)=>(crouching?Crouching:Standing).Nodes.ToArray().First(c=>c.Function==function);
    }
    private static readonly Lazy<Fixture> Data=new(()=>new());
    private static AlsRefactoredRestInput Input()=>new(.1f,90,0,false,false,AlsRefactoredRestRotation.ViewDirection,
        AlsRefactoredRestStance.Standing,true,false,1,1,1,new(10,0,0),default,new(10,0,0),default);

    [Fact]
    public void OriginalIdleScopesReinitializeDelayAfterRelevanceGapAndCancel()
    {
        var f=Data.Value;var parent=f.Runtime();var traversal=new AlsRefactoredStandingRestTraversal(new(f.Catalog),f.Standing);
        var counter=new AlsGraphTraversalCounter(0,0);
        for(var frame=0;frame<7;frame++)
        {
            var context=new AlsPoseUpdateContext(new(frame,7,1),1,.1f).WithUpdateCounter(counter);
            var idle=frame!=3;
            void Prepare()
            {
                parent.Prepare(context.Identity,Input() with{Yaw=180});traversal.Begin(context,parent);
                if(idle){traversal.BeginIdle(frame);Assert.Throws<ArgumentException>(()=>traversal.Complete(frame));traversal.CompleteIdle(frame);}
                traversal.Complete(frame);
            }
            Prepare();var expected=parent.Candidate;
            Assert.InRange(MathF.Abs(expected.TurnDelay-(frame<3?(frame+1)*.1f:frame==3?.3f:(frame-3)*.1f)),0,1e-6f);
            traversal.Cancel();parent.Cancel();Prepare();Assert.Equal(expected,parent.Candidate);
            traversal.ValidateCommit(frame);parent.ValidateCommit(frame);traversal.Commit(frame);parent.Commit(frame);counter=counter.Next((ulong)frame+1);
        }
    }

    [Theory]
    [InlineData("catalog")] [InlineData("range")] [InlineData("sequence")] [InlineData("binding")] [InlineData("angle")]
    public void OriginalRestResourcesRejectForeignBindings(string change)
    {
        var f=Data.Value;Assert.Equal(8,f.Settings.Turns.Length);Assert.True(f.Settings.Turns[0].ScalePlayRate);Assert.False(f.Settings.Turns[4].ScalePlayRate);
        var json=JsonNode.Parse(MantlingHostFixture.Read("refactored_rest_settings"))!;
        switch(change)
        {
            case "catalog":json["catalogSha256"]=new string('0',64);break;
            case "range":json["rotate"]!["referenceYawSpeed"]![1]=0;break;
            case "sequence":json["turn"]!["assets"]![0]!["sequence"]=f.Settings.Turns[1].Sequence;break;
            case "binding":json["turn"]!["assets"]![0]!["binding"]="standing_turn90_right";break;
            case "angle":json["turn"]!["assets"]![0]!["animatedTurnAngle"]=0;break;
        }
        Assert.Throws<ArgumentException>(()=>new AlsRefactoredRestSettings(json.ToJsonString(),f.Catalog));
    }
    [Fact]
    public void DynamicTransitionsUseStrictDistanceLeftTieAndTwoRefreshFrameDelay()
    {
        var f=Data.Value;var runtime=f.Runtime();var command=f.Command(AlsRefactoredStanceFunction.RefreshDynamicTransitions);
        var input=Input();
        for(var frame=0;frame<7;frame++)
        {
            var id=new AlsFrameIdentity(frame,7,1);runtime.Prepare(id,input);runtime.Apply(id,command);var expected=runtime.Candidate;
            runtime.Apply(id,f.Command(command.Function,true));Assert.Equal(expected,runtime.Candidate); // Shared Parent updates once across stances.
            Assert.Equal(frame%3==0,runtime.Candidate.QueuedTransition is not null);
            if(runtime.Candidate.QueuedTransition is {} request)
            {
                Assert.Equal(f.Settings.DynamicSequence(false,true),request.Sequence);Assert.False(request.InertialBlendOut);
                runtime.AcceptPlayback(frame,request,false,true);Assert.Same(request,runtime.Candidate.QueuedTransition);
                runtime.AcceptPlayback(frame,request,false);Assert.Null(runtime.Candidate.QueuedTransition);
            }
            var accepted=runtime.Candidate;runtime.Cancel();runtime.Prepare(id,input);runtime.Apply(id,command);
            if(runtime.Candidate.QueuedTransition is {} replay)runtime.AcceptPlayback(frame,replay,false);
            Assert.Equal(accepted,runtime.Candidate);runtime.Commit(frame);
        }
        Assert.Equal(-1,AlsRefactoredRestModel.Dynamic(8,1,1,1,new(8,0,0),default,new(8,0,0),default,false));
        Assert.Equal(3,AlsRefactoredRestModel.Dynamic(8,1,1,1,new(9,0,0),default,new(10,0,0),default,true));
        Assert.Equal(-1,AlsRefactoredRestModel.Dynamic(8,2,1,1,new(10,0,0),default,new(10,0,0),default,false));
    }
    [Theory]
    [InlineData(90,1)] [InlineData(-90,0)] [InlineData(130,3)] [InlineData(-130,2)] [InlineData(175,3)] [InlineData(175.01f,2)] [InlineData(180,2)]
    public void TurnThresholdDelayRemappingAndPlaybackAcknowledgment(float yaw,int index)
    {
        var f=Data.Value;var runtime=f.Runtime();var id=new AlsFrameIdentity(0,7,1);
        var input=Input() with{Yaw=yaw,Delta=1};var refresh=f.Command(AlsRefactoredStanceFunction.RefreshTurnInPlace);
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(default,input));
        runtime.Prepare(id,input);runtime.Apply(id,refresh);var request=Assert.IsType<AlsRefactoredRestPlayback>(runtime.Candidate.QueuedTurn);
        Assert.Equal(f.Settings.Turns[index].Sequence,request.Sequence);Assert.True(request.InertialBlendOut);
        Assert.Equal(1,runtime.Candidate.TurnPlayRate);Assert.Equal(f.Settings.Turns[index].PlayRate*MathF.Abs(yaw/f.Settings.Turns[index].AnimatedAngle),request.CurvePlayRate);
        runtime.AcceptPlayback(0,request,true);Assert.Equal(request.CurvePlayRate,runtime.Candidate.TurnPlayRate);runtime.Commit(0);
        id=new(1,7,1);runtime.Prepare(id,input with{Stance=AlsRefactoredRestStance.Crouching});runtime.Apply(id,refresh);
        var crouch=Assert.IsType<AlsRefactoredRestPlayback>(runtime.Candidate.QueuedTurn);Assert.Equal(f.Settings.Turns[index+4].Sequence,crouch.Sequence);
        Assert.Equal(crouch.PlayRate,crouch.CurvePlayRate);runtime.Cancel();
        runtime.Prepare(id,input with{Yaw=45});runtime.Apply(id,refresh);Assert.Equal(0,runtime.Candidate.TurnDelay);Assert.Null(runtime.Candidate.QueuedTurn);
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RotationUsesPendingSnapAndFrameGuardWithTransactionalRetry(int hz)
    {
        var f=Data.Value;var runtime=f.Runtime();var refresh=f.Command(AlsRefactoredStanceFunction.RefreshRotateInPlace);
        var resources=new AlsRefactoredStandingResources(MantlingHostFixture.Read("refactored_stance_machines"),f.Catalog);
        var machine=new AlsRefactoredStandingRuntime(resources);
        var players=new AlsRefactoredSourcePlayerRuntime(f.Catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),f.Catalog),
            new Dictionary<string,AlsRefactoredTriangulationProfile>(),resources.RotatePlayers.Bind(0));
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        var counter=new AlsGraphTraversalCounter(0,0);var visited=new HashSet<int>();var exits=0;
        var previous=1f;
        for(var frame=0;frame<hz*4;frame++)
        {
            var phase=frame/hz;var id=new AlsFrameIdentity(frame,7,1);
            var input=Input() with{Delta=1f/hz,Yaw=phase==1?50:60,YawSpeed=460,FirstPerson=phase==2,
                Rotation=phase==0||phase==1?AlsRefactoredRestRotation.Aiming:AlsRefactoredRestRotation.ViewDirection,PendingUpdate=frame==0};
            runtime.Prepare(id,input);runtime.Apply(id,refresh);var result=runtime.Candidate;
            Assert.Equal(phase==0,result.Rotate.Right);Assert.False(result.Rotate.Left);
            if(frame==0)Assert.Equal(3,result.Rotate.PlayRate);
            else if(phase!=0)Assert.InRange(result.Rotate.PlayRate,f.Settings.RotateRate.X,previous);
            runtime.Apply(id,refresh);Assert.Equal(result,runtime.Candidate);
            runtime.Cancel();runtime.Prepare(id,input);runtime.Apply(id,refresh);Assert.Equal(result,runtime.Candidate);
            machine.Prepare(frame,new(false,result.Rotate.Left,result.Rotate.Right),clocks,input.Delta,updateCounter:counter);
            visited.Add(machine.Candidate.State.CurrentState);var ticks=new List<AlsRefactoredSourcePlayerInput>();
            for(var n=0;n<machine.Candidate.UpdateCount;n++)
            {
                var state=machine.Candidate.GetUpdate(n);if(state.State<3)continue;
                ticks.Add(resources.RotatePlayers.Input(0,state.State-3,result.Rotate.PlayRate,result.Rotate.Left,result.Rotate.Right,state.Weight,
                    (machine.Candidate.InitializeStates&(1<<state.State))!=0));
            }
            players.Prepare(frame,ticks.ToArray(),input.Delta);
            var next=clocks.ToArray();
            for(var side=0;side<2;side++)if((machine.Candidate.ClearCachedWeightStates&(1<<(3+side)))!=0)next[side]=next[side] with{CachedWeight=0};
            foreach(var tick in players.Ticks)
            {
                players.Evaluate(frame,tick.PlayerId);
                var history=players.Players.ToArray().Single(p=>p.PlayerId==tick.PlayerId);
                next[tick.PlayerId]=new(resources.RotatePlayers.Players[tick.PlayerId].PropertyIndex,tick.Weight,history.Time,tick.Looping);
                Assert.Equal(result.Rotate.Right,tick.Looping);Assert.Equal(79,players.Pose(tick.PlayerId).Length);
            }
            for(var n=0;n<machine.Candidate.TransitionCount;n++)if(resources.Edges[machine.Candidate.GetTransitionIndex(n)].Rule==AlsRefactoredStandingRule.Automatic)exits++;
            players.Commit(frame);machine.Commit(frame);clocks=next;counter=counter.Next((ulong)frame+1);
            runtime.Commit(frame);previous=result.Rotate.PlayRate;
        }
        Assert.Contains(4,visited);Assert.Contains(0,visited);Assert.True(exits>0);
    }
    [Fact]
    public void TurnNeedsSustainedConditionsAndInitializeOnlyResetsDelay()
    {
        var f=Data.Value;var runtime=f.Runtime();var refresh=f.Command(AlsRefactoredStanceFunction.RefreshTurnInPlace);
        var init=f.Command(AlsRefactoredStanceFunction.InitializeTurnInPlace);var id=new AlsFrameIdentity(0,7,1);
        runtime.Prepare(id,Input() with{Yaw=180,Delta=.75f});runtime.Apply(id,refresh);Assert.Null(runtime.Candidate.QueuedTurn);runtime.Commit(0);
        id=new(1,7,1);runtime.Prepare(id,Input() with{Yaw=180,Delta=.001f});runtime.Apply(id,refresh);Assert.NotNull(runtime.Candidate.QueuedTurn);
        var request=runtime.Candidate.QueuedTurn;runtime.Apply(id,init);Assert.Equal(0,runtime.Candidate.TurnDelay);Assert.Same(request,runtime.Candidate.QueuedTurn);runtime.Commit(1);
        id=new(2,7,1);runtime.Prepare(id,Input() with{YawSpeed=50});runtime.Apply(id,refresh);Assert.Equal(0,runtime.Candidate.TurnDelay);
        Assert.Same(request,runtime.Candidate.QueuedTurn); // Invalid conditions do not erase the queued game-thread request.
    }
}
