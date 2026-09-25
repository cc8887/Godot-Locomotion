using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRestMontageTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog=new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        public readonly AlsRefactoredRestSettings Settings;
        public readonly AlsRefactoredRestMontages Montages;
        public readonly AlsRefactoredRestMontagePose Pose;
        public readonly AlsRefactoredStandingRestPose Rest;
        public readonly AlsRefactoredStanceCallbacks Callbacks;
        public Fixture()
        {
            Settings=new(MantlingHostFixture.Read("refactored_rest_settings"),Catalog);Callbacks=new(Catalog,false);
            var paths=Settings.Turns.ToArray().Select(t=>t.Sequence).Concat(Enumerable.Range(0,4).Select(i=>Settings.DynamicSequence(i>=2,i%2==0))).ToArray();
            Montages=new(Catalog,Settings,MantlingHostFixture.Read("refactored_slot_inventory"),paths.Select((p,i)=>(p,i)).ToDictionary(v=>v.p,v=>v.i+17),5);
            var metadata=new AlsRefactoredSkeletonCurves(MantlingHostFixture.Read("refactored_skeleton_curves"),Catalog);var graph=new AlsRefactoredStandingRestGraph(Catalog);
            var initial=new AlsRefactoredStandingRestPose(Catalog,graph,metadata,[]);Pose=new(Catalog,Montages,initial.CurveNames);
            Rest=new(Catalog,graph,metadata,Pose.CurveNames.ToArray());
        }
        public AlsRefactoredStanceCallback Dynamic=>Callbacks.Nodes.ToArray().Single(c=>c.Function==AlsRefactoredStanceFunction.RefreshDynamicTransitions);
        public AlsRefactoredStanceCallback Turn=>Callbacks.Nodes.ToArray().Single(c=>c.Function==AlsRefactoredStanceFunction.RefreshTurnInPlace);
    }
    private static readonly Lazy<Fixture> Data=new(()=>new());
    [Fact]
    public void WeaponMainThreadNotifyOverwritesRetainedDynamicRequestAfterStopConsumption()
    {
        var f=Data.Value;
        var profiles=Enum.GetValues<AlsRefactoredWeaponKind>().Select(k=>new AlsRefactoredWeaponNotifyProfile(f.Catalog,
            new(f.Catalog,new(MantlingHostFixture.Read("refactored_weapon_machines"),f.Catalog,k)))).ToArray();
        var paths=profiles.SelectMany(p=>p.Bindings.ToArray()).Select(b=>b.Sequence).Distinct().Order().ToArray();
        var weapons=new AlsRefactoredTransitionMontages(f.Catalog,MantlingHostFixture.Read("refactored_slot_inventory"),profiles,
            paths.Select((p,i)=>(p,i)).ToDictionary(v=>v.p,v=>v.i+70),5);
        var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets.ToArray().Concat(weapons.Assets.ToArray()).ToArray());
        var queue=new AlsTransitionQueueRuntime(bank,9,1);var parent=new AlsRefactoredRestParentRuntime(f.Settings,f.Montages,bank,queue,f.Callbacks);
        var id=new AlsFrameIdentity(0,9,1);queue.Begin(id);parent.Prepare(id,Input(8));bank.Begin(id,0);
        queue.Discard();Assert.Throws<ArgumentException>(()=>parent.Apply(id,f.Dynamic));
        Assert.Equal(0,parent.Candidate.DynamicFrameDelay);queue.Begin(id);
        parent.Apply(id,f.Dynamic);queue.QueueStop();var dynamicRequest=queue.Candidate.Play;
        f.Montages.PostUpdate(bank,parent,queue,id);Assert.Equal(dynamicRequest,queue.Candidate.Play);Assert.Empty(bank.Candidate.ToArray());
        var command=weapons.Command(profiles[0].Bindings[0]);
        Assert.False(queue.PlayImmediate(command,"Als.Stance.Standing",true,true));Assert.Equal(dynamicRequest,queue.Candidate.Play);
        Assert.True(queue.PlayImmediate(command,"Als.Stance.Standing",false,true));Assert.Null(queue.Candidate.Play);
        Assert.Equal(command.AnimationId,Assert.Single(bank.Candidate.ToArray()).AnimationId);
        queue.Commit(id);parent.Commit(0);bank.Commit(id);
        id=new(1,9,1);queue.Begin(id);parent.Prepare(id,Input(8));bank.Begin(id,.016f);parent.Apply(id,f.Dynamic);
        Assert.Equal(1,parent.Candidate.DynamicFrameDelay);Assert.Null(queue.Candidate.Play);
        f.Montages.PostUpdate(bank,parent,queue,id);Assert.Single(bank.Candidate.ToArray());Assert.Equal(command.AnimationId,bank.Candidate[0].AnimationId);
    }
    [Theory]
    [InlineData(false,false)] [InlineData(true,false)] [InlineData(false,true)] [InlineData(true,true)]
    public void SharedTransitionQueuePreservesCallOrderAndNativePostUpdateOrder(bool dynamicLast,bool stopQueued)
    {
        var f=Data.Value;var json=MantlingHostFixture.Read("refactored_stance_machines");
        var standing=new AlsRefactoredStandingResources(json,f.Catalog);var stopResources=new AlsRefactoredStopResources(json,f.Catalog);
        var actions=new AlsRefactoredStandingActions(f.Catalog,standing,stopResources,f.Montages,60,61);
        var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets.ToArray().Concat(actions.Assets.ToArray()).ToArray());
        var queue=new AlsTransitionQueueRuntime(bank,9,1);var parent=new AlsRefactoredRestParentRuntime(f.Settings,f.Montages,bank,queue,f.Callbacks);
        var stop=new AlsRefactoredStopRuntime(stopResources);var id=new AlsFrameIdentity(0,9,1);
        var input=Input(0) with{LeftLock=1,LeftTarget=new(20,0,0)};
        Assert.Throws<ArgumentException>(()=>parent.Prepare(id,input)); // The shared queue owns the frame boundary.
        void Prepare()
        {
            queue.Begin(id);parent.Prepare(id,input);stop.Prepare(id.FrameId,-.75f,.016f);bank.Begin(id,0);
            if(!dynamicLast)parent.Apply(id,f.Dynamic);
            actions.QueueStopState(queue,bank,id,stop);
            if(dynamicLast)parent.Apply(id,f.Dynamic);
            parent.Apply(id,f.Turn);
            if(stopQueued)queue.QueueStop(.15f);
        }
        Prepare();Assert.Null(parent.Candidate.QueuedTransition);Assert.Equal(2,parent.Candidate.DynamicFrameDelay);
        var expected=dynamicLast?f.Montages.Assets[8].AnimationId:60;
        Assert.Equal(expected,queue.Candidate.Play!.Value.AnimationId);
        Assert.Throws<ArgumentException>(()=>f.Montages.PlayQueued(bank,parent,id));
        f.Montages.PostUpdate(bank,parent,queue,id);
        var state=parent.Candidate;var queueState=queue.Candidate;var instances=bank.Candidate.ToArray();
        if(stopQueued)
        {
            Assert.Empty(instances);Assert.NotNull(state.QueuedTurn);Assert.Equal(expected,queueState.Play!.Value.AnimationId);Assert.False(queueState.Stop);
        }
        else
        {
            Assert.Equal(2,instances.Length);Assert.Equal(expected,instances[0].AnimationId);Assert.Equal(AlsMontageSlot.Transition,instances[0].Slot);
            Assert.Equal((AlsMontageSlot)AlsTurnSlot.Standing,instances[1].Slot);Assert.Null(state.QueuedTurn);Assert.Null(queueState.Play);
        }
        parent.Cancel();queue.Discard();stop.Cancel();bank.Discard();Prepare();f.Montages.PostUpdate(bank,parent,queue,id);
        Assert.Equal(state,parent.Candidate);Assert.Equal(queueState,queue.Candidate);Assert.Equal(instances,bank.Candidate.ToArray());
        parent.ValidateCommit(0);queue.ValidateCommit(id);stop.ValidateCommit(0);bank.ValidateCommit(id);
        parent.Commit(0);queue.Commit(id);stop.Commit(0);bank.Commit(id);
        if(stopQueued)
        {
            id=new(1,9,1);queue.Begin(id);parent.Prepare(id,input with{TransitionsAllowed=false});bank.Begin(id,.016f);
            parent.Apply(id,f.Dynamic);parent.Apply(id,f.Turn);f.Montages.PostUpdate(bank,parent,queue,id);
            Assert.Equal(2,bank.Candidate.Length);Assert.Equal(expected,bank.Candidate[0].AnimationId);
            Assert.Equal((AlsMontageSlot)AlsTurnSlot.Standing,bank.Candidate[1].Slot);Assert.Null(queue.Candidate.Play);Assert.Null(parent.Candidate.QueuedTurn);
        }
    }
    [Theory]
    [InlineData(-.75f,0)] [InlineData(.75f,1)] [InlineData(-.25f,0)] [InlineData(.25f,1)]
    public void ActualStopCallbacksQueueOriginalSequenceAndMovementStopsSharedGroup(float foot,int side)
    {
        var f=Data.Value;var json=MantlingHostFixture.Read("refactored_stance_machines");
        var standingResources=new AlsRefactoredStandingResources(json,f.Catalog);var stopResources=new AlsRefactoredStopResources(json,f.Catalog);
        var actions=new AlsRefactoredStandingActions(f.Catalog,standingResources,stopResources,f.Montages,60,61);
        var assets=f.Montages.Assets.ToArray().Concat(actions.Assets.ToArray()).ToArray();var bank=new AlsMontageRuntime([],sequences:assets);
        var queue=new AlsTransitionQueueRuntime(bank,9,1);var stop=new AlsRefactoredStopRuntime(stopResources);
        var standing=new AlsRefactoredStandingRuntime(standingResources);var id=new AlsFrameIdentity(0,9,1);
        var counter=new AlsGraphTraversalCounter(0,0);AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        queue.Begin(id);stop.Prepare(0,foot,.016f,updateCounter:counter);bank.Begin(id,0);
        Assert.True(actions.QueueStopState(queue,bank,id,stop));Assert.Empty(bank.Candidate.ToArray());
        var request=queue.Candidate.Play!.Value;Assert.Equal(60+side,request.AnimationId);Assert.Equal(.4f,request.StartTime);Assert.Equal(1.5f,request.PlayRate);
        var foreign=new AlsMontageRuntime([],sequences:assets);
        Assert.Throws<ArgumentException>(()=>actions.QueueStopState(queue,foreign,id,stop));Assert.Equal(request,queue.Candidate.Play);
        queue.Discard();stop.Cancel();queue.Begin(id);stop.Prepare(0,foot,.016f,updateCounter:counter);
        actions.QueueStopState(queue,bank,id,stop);Assert.Equal(request,queue.Candidate.Play);Assert.True(queue.PlayQueued());
        Assert.Equal(.4f,Assert.Single(bank.Candidate.ToArray()).Position);
        standing.Prepare(0,new(false,false,false),clocks,.016f,updateCounter:counter);Assert.Equal(0,actions.QueueStanding(queue,id,standing));
        queue.Commit(id);stop.Commit(0);standing.Commit(0);bank.Commit(id);
        id=new(1,9,1);counter=counter.Next(1);queue.Begin(id);bank.Begin(id,.1f);
        stop.Prepare(1,foot,.1f,updateCounter:counter);Assert.False(actions.QueueStopState(queue,bank,id,stop));
        standing.Prepare(1,new(true,false,false),clocks,.1f,updateCounter:counter);Assert.Equal(2,actions.QueueStanding(queue,id,standing));
        Assert.True(queue.Candidate.Stop);Assert.Equal(-1,queue.Candidate.StopDuration);
        // A new worker request is retained when movement's stop takes precedence.
        queue.QueuePlay(request,"Als.Stance.Standing",true);Assert.False(queue.PlayQueued());queue.StopQueued();
        Assert.Equal(0,Assert.Single(bank.Candidate.ToArray()).Blend.DesiredWeight);Assert.Equal(request,queue.Candidate.Play);
        queue.Commit(id);stop.Commit(1);standing.Commit(1);bank.Commit(id);
        id=new(2,9,1);queue.Begin(id);bank.Begin(id,.016f);Assert.True(queue.PlayQueued());
        Assert.Equal(request.AnimationId,bank.Candidate[^1].AnimationId);Assert.Equal(.4f,bank.Candidate[^1].Position);
        queue.Commit(id);bank.Commit(id);
        var sources=actions.Assets.ToArray().Select(a=>f.Catalog.CompileAdditivePose(actions.SourcePath(a.AnimationId))).ToArray();
        var rest=new AlsRefactoredStandingRestPose(f.Catalog,f.Rest.Graph,new(MantlingHostFixture.Read("refactored_skeleton_curves"),f.Catalog),
            sources.SelectMany(s=>s.CurveNames.ToArray()).Distinct(StringComparer.OrdinalIgnoreCase));
        var profile=new AlsRefactoredTransitionPose(f.Catalog,actions,rest.BoneNames,rest.Parents,rest.CurveNames);var sampler=profile.CreateSampler();
        var reference=f.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        var mixer=new AlsMontageSlotPose(reference.ReferencePose,rest.Parents,rest.CurveNames.Length);
        var basis=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[rest.CurveNames.Length];rest.SampleIdleSource(basis,baseCurves);
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[baseCurves.Length];var changed=false;
        for(var frame=3;frame<123;frame++)
        {
            id=new(frame,9,1);bank.Begin(id,1f/60);
            mixer.Evaluate(bank.Frame,id,AlsMontageSlot.Transition,basis,baseCurves,pose,curves,sampler);
            var expected=pose.ToArray();var expectedCurves=curves.ToArray();changed|=!basis.SequenceEqual(pose);
            bank.Discard();bank.Begin(id,1f/60);mixer.Evaluate(bank.Frame,id,AlsMontageSlot.Transition,basis,baseCurves,pose,curves,sampler);
            Assert.Equal(expected,pose);Assert.Equal(expectedCurves,curves);bank.Commit(id);
        }
        Assert.True(changed);Assert.Empty(bank.Committed.ToArray());Assert.Equal(basis,pose);Assert.Equal(baseCurves,curves);
    }
    [Theory]
    [InlineData("name")] [InlineData("mode")] [InlineData("alpha")] [InlineData("value")]
    [InlineData("link")] [InlineData("map")] [InlineData("callback")]
    public void FinalStandingCurveRejectsChangedOriginalPolicy(string change)
    {
        var payload=JsonNode.Parse(Data.Value.Catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)).GetRawText())!;
        var node=payload["compiled"]!["nodes"]!.AsArray().Single(n=>(int)n!["propertyIndex"]! ==68)!;
        var runtime=node["runtime"]!;
        switch(change)
        {
            case "name":runtime["curveNames"]![0]="PoseCrouching";break;
            case "mode":runtime["applyMode"]="Scale";break;
            case "alpha":runtime["alpha"]=.5f;break;
            case "value":runtime["curveValues"]![0]=0;break;
            case "link":runtime["sourcePose"]!["linkId"]=65;break;
            case "map":runtime["curveMap"]!["Extra"]=1;break;
            case "callback":runtime["updateFunction"]!["functionName"]="Unexpected";break;
        }
        using var document=JsonDocument.Parse(payload.ToJsonString());
        Assert.Throws<ArgumentException>(()=>AlsRefactoredStandingOutput.ValidateNode(document.RootElement));
    }
    private static AlsRefactoredRestInput Input(int asset)
    {
        var turn=asset<8;var crouch=turn?asset>=4:asset>=10;var left=asset%2==0;
        return new(1,turn?(left?-1:1)*(asset%4<2?90:135):0,0,false,false,AlsRefactoredRestRotation.ViewDirection,
            crouch?AlsRefactoredRestStance.Crouching:AlsRefactoredRestStance.Standing,true,false,1,turn?0:1,turn?0:1,
            new(!turn&&left?20:0,0,0),default,new(!turn&&!left?20:0,0,0),default);
    }
    [Fact]
    public void BothQueuesUseNativeOrderAndForeignBankFailsBeforeConsumption()
    {
        var f=Data.Value;var parent=new AlsRefactoredRestParentRuntime(f.Settings,f.Callbacks);var id=new AlsFrameIdentity(0,9,1);
        parent.Prepare(id,Input(0) with{LeftLock=1,LeftTarget=new(20,0,0)});parent.Apply(id,f.Dynamic);parent.Apply(id,f.Turn);
        var before=parent.Candidate;var missing=new AlsMontageRuntime([],sequences:f.Montages.Assets[8..]);missing.Begin(id,0);
        Assert.Throws<ArgumentException>(()=>f.Montages.PlayQueued(missing,parent,id));Assert.Empty(missing.Candidate.ToArray());Assert.Equal(before,parent.Candidate);
        var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets);bank.Begin(id,0);f.Montages.PlayQueued(bank,parent,id,true);
        Assert.Empty(bank.Candidate.ToArray());Assert.Equal(before,parent.Candidate);
        f.Montages.PlayQueued(bank,parent,id);Assert.Equal(2,bank.Candidate.Length);
        Assert.Equal(AlsMontageSlot.Transition,bank.Candidate[0].Slot);Assert.Equal(0,bank.Candidate[0].Blend.DesiredWeight);
        Assert.Equal((AlsMontageSlot)AlsTurnSlot.Standing,bank.Candidate[1].Slot);Assert.True(bank.Candidate[1].InertialBlendOut);
        Assert.All(bank.Candidate.ToArray(),c=>Assert.Equal(5,c.GroupId));Assert.Null(parent.Candidate.QueuedTransition);Assert.Null(parent.Candidate.QueuedTurn);
        var expected=bank.Candidate.ToArray();bank.Discard();parent.Cancel();parent.Prepare(id,Input(0) with{LeftLock=1,LeftTarget=new(20,0,0)});
        parent.Apply(id,f.Dynamic);parent.Apply(id,f.Turn);bank.Begin(id,0);f.Montages.PlayQueued(bank,parent,id);Assert.Equal(expected,bank.Candidate.ToArray());
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void IdleSlotSuppressesSourceAndPreservesTransactionalWeightHistory(int hz)
    {
        var f=Data.Value;var slot=new AlsRefactoredStandingIdleSlot(f.Catalog,f.Rest.Graph,f.Rest,f.Pose);
        var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets);var id=new AlsFrameIdentity(0,9,1);
        bank.Begin(id,0);var asset=f.Montages.Assets[0];
        bank.PlaySequence(new(asset.AnimationId,asset.Slot,1,0,.2f,.2f){InertialBlendOut=true});bank.Commit(id);
        var reference=f.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        var mixer=new AlsMontageSlotPose(reference.ReferencePose,f.Pose.Parents,f.Pose.CurveNames.Length);var sampler=f.Pose.CreateSampler();
        var basis=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[f.Rest.CurveNames.Length];f.Rest.SampleIdleSource(basis,baseCurves);
        var expected=new AlsPrecisePose[79];var expectedCurves=new AlsInertialCurve[baseCurves.Length];
        var counter=new AlsGraphTraversalCounter(0,0);var previous=0f;var suppressed=0;var inactive=0;var evaluated=0;
        for(var frame=1;frame<=hz*3;frame++)
        {
            id=new(frame,9,1);bank.Begin(id,1f/hz);
            var context=new AlsPoseUpdateContext(id,.4f,1f/hz,.7f).WithUpdateCounter(counter).WithState(65,0).WithInertialization(118,true);
            var weights=bank.Frame.SlotWeights(asset.Slot);var initialize=frame==3;
            slot.Prepare(bank.Frame,context,initialize);var source=slot.SourceUpdate;
            Assert.Equal(weights.SourceWeight>AlsPoseBlender.WeightThreshold,source.Updated);
            if(source.Updated)
            {
                Assert.Equal(context.Weight*MathF.Max(2*AlsPoseBlender.WeightThreshold,weights.SourceWeight),source.Context.Weight);
                Assert.Equal(!((initialize?0:previous)>weights.SourceWeight||weights.SlotNodeWeight>=1-AlsPoseBlender.WeightThreshold),source.Context.IsActive);
                Assert.Equal(.7f,source.Context.RootMotionWeight);Assert.Equal(118,source.Context.InertializationRequester);Assert.Equal(context.GetState(0),source.Context.GetState(0));
                if(!source.Context.IsActive)inactive++;
            }
            else suppressed++;
            // Update-only commits are valid, but never expose a previous pose.
            Assert.Throws<InvalidOperationException>(()=>slot.Pose.ToArray());
            if(frame%7!=0)
            {
                slot.Evaluate();Assert.Equal(source.Updated,slot.SourceEvaluated);
                mixer.Evaluate(bank.Frame,id,asset.Slot,basis,baseCurves,expected,expectedCurves,sampler);
                Assert.Equal(expected,slot.Pose.ToArray());Assert.Equal(expectedCurves,slot.Curves.ToArray());evaluated++;
                slot.Evaluate();Assert.Equal(expected,slot.Pose.ToArray());
            }
            slot.Cancel();slot.Prepare(bank.Frame,context,initialize);Assert.Equal(source,slot.SourceUpdate);
            slot.Commit(id);previous=weights.SourceWeight;bank.Commit(id);counter=counter.Next((ulong)frame+1);
        }
        Assert.True(suppressed>0);Assert.True(inactive>0);Assert.True(evaluated>hz);
        id=new(hz*3+1,9,1);bank.Begin(id,0);
        var wrong=new AlsPoseUpdateContext(new(id.FrameId,10,1),1,0).WithUpdateCounter(counter);
        Assert.Throws<ArgumentException>(()=>slot.Prepare(bank.Frame,wrong));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SlotForwardsOnlyDuringActualIdleUpdateAndMissedRequestsExpire(bool idle)
    {
        var f=Data.Value;var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets);
        var parent=new AlsRefactoredRestParentRuntime(f.Settings,f.Callbacks);
        var traversal=new AlsRefactoredStandingRestTraversal(new(f.Catalog),f.Callbacks);
        var id=new AlsFrameIdentity(0,9,1);parent.Prepare(id,Input(0));parent.Apply(id,f.Turn);
        bank.Begin(id,0);f.Montages.PlayQueued(bank,parent,id);bank.Commit(id);parent.Commit(0);
        id=new(1,9,1);bank.Begin(id,.25f);f.Montages.Stop(bank,id,.2f);bank.Commit(id);
        var counter=new AlsGraphTraversalCounter(0,0);
        for(var frame=2;frame<=3;frame++)
        {
            id=new(frame,9,1);bank.Begin(id,.016f);parent.Prepare(id,Input(0) with{Yaw=0});
            var context=new AlsPoseUpdateContext(id,1,.016f).WithUpdateCounter(counter);
            traversal.Begin(context,parent);
            if(idle||frame==3){traversal.BeginIdle(frame);traversal.CompleteIdle(frame);}
            traversal.Complete(frame);
            var request=f.Montages.StandingSlotRequest(bank.Frame,id,traversal);
            Assert.Equal(frame==2,bank.Frame.TryGetInertializationRequest(5,out _));
            Assert.Equal(frame==2&&idle,request is not null);
            if(request is not null)
            {
                Assert.Equal(id,request.Identity);Assert.Equal(f.Settings.CatalogDigest,request.CatalogDigest);
                Assert.Equal(.2f,request.Request.Duration);
            }
            var foreign=new AlsFrameIdentity(frame,10,1);
            Assert.Throws<ArgumentException>(()=>f.Montages.StandingSlotRequest(bank.Frame,foreign,traversal));
            traversal.Commit(frame);parent.Commit(frame);bank.Commit(id);counter=counter.Next((ulong)frame+1);
        }
    }
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void EveryOriginalRestAssetPlaysSamplesAndEndsThroughSharedBank(int hz)
    {
        var f=Data.Value;Assert.Equal(f.Pose.CurveNames.ToArray(),f.Rest.CurveNames.ToArray());Assert.Equal(1,f.Montages.NativeGroupIndex);
        var reference=f.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        var sampler=f.Pose.CreateSampler();var mixer=new AlsMontageSlotPose(reference.ReferencePose,f.Pose.Parents,f.Pose.CurveNames.Length);
        var basis=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[f.Pose.CurveNames.Length];f.Rest.SampleIdleSource(basis,baseCurves);
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[baseCurves.Length];var requests=0;var samples=0;
        for(var asset=0;asset<12;asset++)
        {
            var bank=new AlsMontageRuntime([],sequences:f.Montages.Assets);var parent=new AlsRefactoredRestParentRuntime(f.Settings,f.Callbacks);
            var id=new AlsFrameIdentity(0,9,1);parent.Prepare(id,Input(asset));parent.Apply(id,f.Dynamic);parent.Apply(id,f.Turn);bank.Begin(id,0);
            f.Montages.PlayQueued(bank,parent,id);var definition=f.Montages.Assets[asset];Assert.Equal(definition.AnimationId,Assert.Single(bank.Candidate.ToArray()).AnimationId);
            var rate=parent.Candidate.TurnPlayRate;bank.Commit(id);parent.Commit(0);var evaluated=0;
            for(var frame=1;frame<=hz*3;frame++)
            {
                id=new(frame,9,1);bank.Begin(id,1f/hz);
                mixer.Evaluate(bank.Frame,id,definition.Slot,basis,baseCurves,pose,curves,sampler);
                var raw=pose.ToArray();var rawCurves=curves.ToArray();var entries=bank.Evaluation.ToArray();
                if(entries.Length>0){evaluated++;samples++;}
                if(definition.Slot.Id==0)f.Rest.FinishIdleSlot(pose,curves,rate,pose,curves);
                var has=bank.Frame.TryGetInertializationRequest(5,out var request);
                if(has){requests++;Assert.True(asset<8);Assert.Empty(entries);Assert.Equal(f.Settings.TurnBlend,request.Duration);Assert.Equal(basis,raw);}
                bank.Discard();bank.Begin(id,1f/hz);Assert.Equal(entries,bank.Evaluation.ToArray());
                mixer.Evaluate(bank.Frame,id,definition.Slot,basis,baseCurves,pose,curves,sampler);Assert.Equal(raw,pose);Assert.Equal(rawCurves,curves);
                Assert.Equal(has,bank.Frame.TryGetInertializationRequest(5,out var retry));Assert.Equal(request,retry);bank.Commit(id);
            }
            Assert.True(evaluated>0);Assert.Empty(bank.Committed.ToArray());
        }
        Assert.Equal(8,requests);Assert.True(samples>hz*12);
    }
}
