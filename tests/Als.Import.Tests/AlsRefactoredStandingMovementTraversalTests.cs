using GodotAls.Core.Locomotion;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStandingMovementTraversalTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog;
        public readonly AlsRefactoredStandingResources Standing;
        public readonly AlsRefactoredMovementDetailsPoseGraph Details;
        public readonly AlsRefactoredDirectionSourceProfile Direction;
        public readonly AlsRefactoredDirectionPose DirectionPose;
        public readonly AlsRefactoredMovementCacheProfile Movement;
        public readonly AlsRefactoredStopPoseGraph Stop;
        public readonly AlsRefactoredSyncBank Bank;
        public readonly IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> Triangles;
        public readonly AlsRefactoredMovementSettings Settings;
        public readonly AlsRefactoredSkeletonCurves Metadata;
        public Fixture()
        {
            var index=MantlingHostFixture.Read("refactored_animation_sources");
            byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
            Catalog=new(index,Read);var machines=MantlingHostFixture.Read("refactored_stance_machines");
            Standing=new(machines,Catalog);Details=new(Catalog,new(machines,Catalog));
            Direction=new(Catalog,new(Catalog,new(machines,Catalog,false)));
            Triangles=AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"),index,Read);
            DirectionPose=new(Catalog,Direction,Triangles);Movement=new(Catalog,Triangles);Stop=new(Catalog,new(machines,Catalog));
            Bank=new(MantlingHostFixture.Read("refactored_sync_inputs"),Catalog);Settings=new(MantlingHostFixture.Read("refactored_movement_settings"),Catalog);
            Metadata=new(MantlingHostFixture.Read("refactored_skeleton_curves"),Catalog);
        }
        public AlsRefactoredSourcePlayerRuntime Players()=>new(Catalog,Bank,Triangles,Details.Players.Bind(0,
            new Dictionary<string,int>{{"Movement",0},{"Run Start",1},{"First Pivot",2},{"Second Pivot",3}}));
    }
    private static readonly Lazy<Fixture> Data=new(()=>new());

    [Fact]
    public void ActualTurnSlotExitDrivesOuterStandingInertia()
    {
        var f=Data.Value;var settings=new AlsRefactoredRestSettings(MantlingHostFixture.Read("refactored_rest_settings"),f.Catalog);
        var paths=settings.Turns.ToArray().Select(t=>t.Sequence).Concat(Enumerable.Range(0,4).Select(i=>settings.DynamicSequence(i>=2,i%2==0))).ToArray();
        var montages=new AlsRefactoredRestMontages(f.Catalog,settings,MantlingHostFixture.Read("refactored_slot_inventory"),
            paths.Select((p,i)=>(p,i)).ToDictionary(v=>v.p,v=>v.i),5);
        var restGraph=new AlsRefactoredStandingRestGraph(f.Catalog);var callbacks=new AlsRefactoredStanceCallbacks(f.Catalog,false);
        var initialRest=new AlsRefactoredStandingRestPose(f.Catalog,restGraph,f.Metadata,[]);
        var montagePose=new AlsRefactoredRestMontagePose(f.Catalog,montages,initialRest.CurveNames);
        var rest=new AlsRefactoredStandingRestPose(f.Catalog,restGraph,f.Metadata,montagePose.CurveNames.ToArray());
        var movement=new AlsRefactoredMovementCacheRuntime(f.Movement,f.DirectionPose.BoneNames,f.DirectionPose.CurveNames);
        var details=new AlsRefactoredMovementDetailsPose(f.Catalog,f.Details,f.Movement,movement.CurveNames);
        var stop=new AlsRefactoredStopPose(f.Catalog,f.Stop,f.Metadata,details.CurveNames);
        var profile=new AlsRefactoredStandingPose(f.Standing,rest,details,stop);
        var rotate=new AlsRefactoredSourcePlayerRuntime(f.Catalog,f.Bank,f.Triangles,f.Standing.RotatePlayers.Bind(0));
        var output=profile.CreateRuntime(rotate,0);var inertia=new AlsRefactoredStandingInertialization(f.Catalog,profile);
        var standing=new AlsRefactoredStandingRuntime(f.Standing);
        var graph=new AlsRefactoredStandingMovementTraversal(f.Catalog,f.Standing,f.Details,f.Direction,0);
        var parent=new AlsRefactoredMovementParentRuntime(f.Details.Callbacks,f.Settings);
        var restParent=new AlsRefactoredRestParentRuntime(settings,callbacks);var traversal=new AlsRefactoredStandingRestTraversal(restGraph,callbacks);
        var bank=new AlsMontageRuntime([],sequences:montages.Assets);
        var input=new AlsRefactoredRestInput(1,-90,0,false,false,AlsRefactoredRestRotation.ViewDirection,
            AlsRefactoredRestStance.Standing,true,false,1,0,0,default,default,default,default);
        var id=new AlsFrameIdentity(0,19,1);restParent.Prepare(id,input);
        restParent.Apply(id,callbacks.Nodes.ToArray().Single(c=>c.Function==AlsRefactoredStanceFunction.RefreshTurnInPlace));
        bank.Begin(id,0);montages.PlayQueued(bank,restParent,id);Assert.Single(bank.Candidate.ToArray());bank.Commit(id);restParent.Commit(0);
        var reference=f.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        var mixer=new AlsMontageSlotPose(reference.ReferencePose,montagePose.Parents,montagePose.CurveNames.Length);var sampler=montagePose.CreateSampler();
        var basis=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[rest.CurveNames.Length];rest.SampleIdleSource(basis,baseCurves);
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[baseCurves.Length];
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        var init=new AlsGraphTraversalCounter(0,0);var counter=init;var exits=0;
        for(var frame=1;frame<=180;frame++)
        {
            id=new(frame,19,1);var context=new AlsPoseUpdateContext(id,1,1f/60).WithUpdateCounter(counter).WithInertialization(118,true);
            bank.Begin(id,context.Delta);restParent.Prepare(id,input with{Delta=context.Delta,Yaw=0});
            traversal.Begin(context,restParent);traversal.BeginIdle(frame);traversal.CompleteIdle(frame);traversal.Complete(frame);
            standing.Prepare(frame,new(false,false,false),clocks,context.Delta,updateCounter:counter);
            parent.Prepare(id,new AlsRefactoredMovementInput(default,default,AlsQuaternion.Identity,0,1,0,0,1000,800,"Als.Gait.Running",false,false,context.Delta,1,0,0,0));
            graph.Prepare(context,[],[],init,parent,new(1,1,1,0));
            mixer.Evaluate(bank.Frame,id,AlsTurnSlot.Standing,basis,baseCurves,pose,curves,sampler);
            output.Begin(id,standing);output.CaptureIdleSlot(pose,curves,restParent.Candidate.TurnPlayRate);output.Evaluate(frame);
            var request=montages.StandingSlotRequest(bank.Frame,id,traversal);
            inertia.Prepare(context,standing,graph,slotRequest:request);Assert.Equal(request is null?0:1,inertia.PendingRequests);
            inertia.Evaluate(frame,output,AlsPrecisePose.Identity);
            if(request is not null)
            {
                exits++;Assert.True(inertia.IsActive);Assert.Equal(basis,pose);
                Assert.False(output.Pose.ToArray().SequenceEqual(inertia.Pose.ToArray()));
                var yaw=Array.IndexOf(profile.CurveNames.ToArray(),"RotationYawSpeed");Assert.Equal(output.Curves[yaw],inertia.Curves[yaw]);
                var expected=inertia.Pose.ToArray();inertia.Cancel();inertia.Prepare(context,standing,graph,slotRequest:request);
                inertia.Evaluate(frame,output,AlsPrecisePose.Identity);Assert.Equal(expected,inertia.Pose.ToArray());
            }
            inertia.Commit(frame);output.Commit(frame);graph.Commit(frame);parent.Commit(frame);standing.Commit(frame);
            traversal.Commit(frame);restParent.Commit(frame);bank.Commit(id);counter=counter.Next((ulong)frame+1);
        }
        Assert.Equal(1,exits);Assert.False(inertia.IsActive);
    }

    [Fact]
    public void StandingPoseRequiresEveryCurrentSourceAndRejectsForeignOwners()
    {
        var f=Data.Value;var movement=new AlsRefactoredMovementCacheRuntime(f.Movement,f.DirectionPose.BoneNames,f.DirectionPose.CurveNames);
        var details=new AlsRefactoredMovementDetailsPose(f.Catalog,f.Details,f.Movement,movement.CurveNames);
        var stop=new AlsRefactoredStopPose(f.Catalog,f.Stop,f.Metadata,details.CurveNames);
        var rest=new AlsRefactoredStandingRestPose(f.Catalog,new(f.Catalog),f.Metadata,["OnlyInSlot"]);
        var profile=new AlsRefactoredStandingPose(f.Standing,rest,details,stop);
        var rotate=new AlsRefactoredSourcePlayerRuntime(f.Catalog,f.Bank,f.Triangles,f.Standing.RotatePlayers.Bind(0));
        var output=profile.CreateRuntime(rotate,0);var machine=new AlsRefactoredStandingRuntime(f.Standing);
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        var counter=new AlsGraphTraversalCounter(0,0);var identity=new GodotAls.Core.Contracts.AlsFrameIdentity(0,19,1);
        machine.Prepare(0,new(false,false,false),clocks,.01f,updateCounter:counter);output.Begin(identity,machine);
        Assert.Throws<ArgumentException>(()=>output.Commit(0));Assert.Throws<InvalidOperationException>(()=>output.Evaluate(0));
        Assert.Throws<ArgumentException>(()=>output.CaptureRotate(true,1));
        var idle=new AlsPrecisePose[79];var curves=new AlsInertialCurve[rest.CurveNames.Length];rest.SampleIdleSource(idle,curves);
        curves[Array.IndexOf(rest.CurveNames.ToArray(),"OnlyInSlot")]=new(.375f);
        Assert.Throws<ArgumentException>(()=>output.CaptureIdleSlot(idle,curves,float.NaN));
        Assert.Throws<ArgumentException>(()=>output.Commit(0));
        output.CaptureIdleSlot(idle,curves,1);Assert.Throws<ArgumentException>(()=>output.CaptureIdleSlot(idle,curves,1));
        output.Evaluate(0);Assert.Equal(idle,output.Pose.ToArray());
        Assert.Equal(new AlsInertialCurve(.375f),output.Curves[Array.IndexOf(profile.CurveNames.ToArray(),"OnlyInSlot")]);
        var expected=output.Curves.ToArray();output.Cancel();output.Begin(identity,machine);
        Assert.Throws<InvalidOperationException>(()=>output.Evaluate(0)); // Ready bits from the cancelled frame must not leak.
        output.CaptureIdleSlot(idle,curves,1);output.Evaluate(0);Assert.Equal(expected,output.Curves.ToArray());
        output.Commit(0);machine.Commit(0);
        var foreign=new AlsRefactoredStandingRuntime(f.Standing);foreign.Prepare(1,new(false,false,false),clocks,.01f,updateCounter:counter.Next(1));
        Assert.Throws<ArgumentException>(()=>output.Begin(new(1,19,1),foreign));
        machine.Prepare(1,new(true,false,false),clocks,.01f,updateCounter:counter.Next(1));
        Assert.Throws<ArgumentException>(()=>output.Begin(new(1,20,1),machine));
        output.Begin(new(1,19,1),machine);Assert.True(output.NeedsState(0));Assert.True(output.NeedsState(1));
        output.CaptureIdleSlot(idle,curves,1);Assert.Throws<InvalidOperationException>(()=>output.Evaluate(1));
        // A cancelled or replaced source candidate invalidates pose collection.
        machine.Cancel();Assert.Throws<ArgumentException>(()=>output.Evaluate(1));output.Cancel();
    }

    [Fact]
    public void DeferredInitializationAndFailedCompletionDoNotCommitPartialHistory()
    {
        var f=Data.Value;var parent=new AlsRefactoredMovementParentRuntime(f.Details.Callbacks,f.Settings);
        var graph=new AlsRefactoredStandingMovementTraversal(f.Catalog,f.Standing,f.Details,f.Direction,0);var players=f.Players();
        var init=new AlsGraphTraversalCounter(0,0);var counter=init;
        var input=new AlsRefactoredMovementInput(new(100,0,0),new(100,0,0),AlsQuaternion.Identity,100,1,0,0,1000,800,"Als.Gait.Running",false,false,.02f,1,0,0,0);
        var zero=new AlsPoseUpdateContext(new(0,9,1),1,.02f).WithUpdateCounter(counter).WithInertialization(118,true);
        parent.Prepare(zero.Identity,input);graph.Prepare(zero,[],[55],init,parent,new(1,1,1,0),true);
        Assert.False(graph.HasMovement);Assert.Empty(graph.CacheUpdates.ToArray());graph.Commit(0);parent.Commit(0);
        counter=counter.Next(1);var context=new AlsPoseUpdateContext(new(1,9,1),1,.02f).WithUpdateCounter(counter).WithInertialization(118,true);
        var first=context.WithState(65,1);var second=context.WithState(65,2);
        AlsRefactoredMovementCacheRead[] reads=[new(55,66,first),new(51,66,second)];
        void Prepare(){parent.Prepare(context.Identity,input);parent.RefreshGrounded(1);graph.Prepare(context,reads,[],init,parent,new(1,1,1,0));}
        Prepare();Assert.Equal(first,graph.CacheUpdates[0].Context);Assert.Equal(new[]{second},graph.OuterSkippedContexts.ToArray());
        Assert.Equal(118,graph.OuterSkippedHandler);Assert.True(graph.DetailsMachine.Candidate.Reinitialized);
        Assert.Throws<ArgumentException>(()=>graph.Commit(1)); // Real source times have not been captured.
        var expected=graph.Movement.SourceInputs.ToArray();graph.Cancel();parent.Cancel();Prepare();
        Assert.Equal(expected,graph.Movement.SourceInputs.ToArray());Assert.True(graph.DetailsMachine.Candidate.Reinitialized);
        players.Prepare(1,graph.Movement.SourceInputs,.02f);graph.DetailsSources.CaptureSourceTimes(1,players);
        graph.ValidateCommit(1);parent.ValidateCommit(1);players.ValidateCommit(1);graph.Commit(1);parent.Commit(1);players.Commit(1);
        counter=counter.Next(2);context=new AlsPoseUpdateContext(new(2,9,1),1,.02f).WithUpdateCounter(counter);
        parent.Prepare(context.Identity,input);
        Assert.Throws<ArgumentException>(()=>graph.Prepare(context,[new(55,67,context)],[],init,parent,new(1,1,1,0)));
        Assert.Throws<ArgumentException>(()=>graph.Commit(2));parent.Cancel();
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OneDrainFeedsActualMovementDetailsInertiaIntoStoppingPose(int hz)
    {
        var f=Data.Value;var standing=new AlsRefactoredStandingRuntime(f.Standing);var stop=new AlsRefactoredStopRuntime(f.Stop.Resources);
        var stopSources=new AlsRefactoredStopSourceRuntime(f.Stop);var parent=new AlsRefactoredMovementParentRuntime(f.Details.Callbacks,f.Settings);
        var graph=new AlsRefactoredStandingMovementTraversal(f.Catalog,f.Standing,f.Details,f.Direction,0);
        Assert.Equal(new[]{66,67,133,136,138,137,132,134,135},graph.Caches.UpdateOrder.ToArray());Assert.Equal(38,graph.Caches.Reads.Length);
        var players=f.Players();var direction=f.DirectionPose.CreateSampler();
        var movement=new AlsRefactoredMovementCacheRuntime(f.Movement,f.DirectionPose.BoneNames,f.DirectionPose.CurveNames);
        var detailsPose=new AlsRefactoredMovementDetailsPose(f.Catalog,f.Details,f.Movement,movement.CurveNames);var detailsSampler=detailsPose.CreateSampler();
        var inertia=new AlsRefactoredMovementInertialization(f.Catalog,detailsPose);
        var stopPose=new AlsRefactoredStopPose(f.Catalog,f.Stop,f.Metadata,detailsPose.CurveNames);var stopSampler=stopPose.CreateSampler();
        var rest=new AlsRefactoredStandingRestPose(f.Catalog,new(f.Catalog),f.Metadata,[]);
        var rotate=new AlsRefactoredSourcePlayerRuntime(f.Catalog,f.Bank,f.Triangles,f.Standing.RotatePlayers.Bind(0));
        var standingPoseProfile=new AlsRefactoredStandingPose(f.Standing,rest,detailsPose,stopPose);
        var standingPose=standingPoseProfile.CreateRuntime(rotate,0);
        var standingInertia=new AlsRefactoredStandingInertialization(f.Catalog,standingPoseProfile);
        var idlePose=new AlsPrecisePose[79];var idleCurves=new AlsInertialCurve[rest.CurveNames.Length];rest.SampleIdleSource(idlePose,idleCurves);
        var basePose=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[f.DirectionPose.CurveNames.Length];
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[detailsPose.CurveNames.Length];
        var finalPose=new AlsPrecisePose[79];var finalCurves=new AlsInertialCurve[stopPose.CurveNames.Length];
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        var init=new AlsGraphTraversalCounter(0,0);var counter=init;var overlap=0;var stopped=0;var idle=0;var caches=new HashSet<int>();
        var states=new HashSet<int>();var inertialRequests=0;var multiEdges=0;var smoothed=0;var deferred=0;
        for(var frame=0;frame<hz*8;frame++)
        {
            if(frame==hz*7)counter=counter.Next((ulong)frame).Next((ulong)frame);
            var local=(float)(frame%hz)/hz;var moving=frame<hz*4&&(local<.45f||local>=.6f&&local<.78f);var cycle=frame/hz;
            var angle=cycle%2==0?55f:-55f;var velocity=moving?new AlsDoubleVector(170,cycle%2==0?100:-100,0):AlsDoubleVector.Zero;
            var input=new AlsRefactoredMovementInput(velocity,new(200,100,0),AlsQuaternion.Identity,moving?200:0,1,angle,0,1000,800,"Als.Gait.Running",false,frame==0,1f/hz,1,0,0,.2f);
            var context=new AlsPoseUpdateContext(new(frame,19,1),1,input.Delta,.4f).WithUpdateCounter(counter).WithInertialization(118,true);
            var rotatingLeft=cycle==5;var rotatingRight=cycle==6;
            var evaluateOuter=frame<hz*6||frame>=hz*6+3;
            var hasStop=false;var reads=new List<AlsRefactoredMovementCacheRead>();var initialReads=new List<int>();
            void Prepare()
            {
                reads.Clear();initialReads.Clear();hasStop=false;
                parent.Prepare(context.Identity,input);parent.RefreshGrounded(frame);
                standing.Prepare(frame,new(moving,rotatingLeft,rotatingRight),clocks,input.Delta,updateCounter:counter);
                var update=standing.Candidate;
                var rotateTicks=new List<AlsRefactoredSourcePlayerInput>();
                var initialized=Enumerable.Range(0,update.InitializationCount).Select(update.GetInitialization).ToArray();
                if(initialized.Contains(1))initialReads.Add(55);
                for(var i=0;i<update.UpdateCount;i++)
                {
                    var state=update.GetUpdate(i);var path=context.WithWeight(state.Weight).WithState(65,state.State,state.InertializationSync);
                    if(state.State>=3)rotateTicks.Add(f.Standing.RotatePlayers.Input(0,state.State-3,1.5f,rotatingLeft,rotatingRight,state.Weight,initialized.Contains(state.State)));
                    if(state.State==1)reads.Add(new(55,66,path));
                    if(state.State!=2)continue;
                    hasStop=true;var foot=(cycle%4)switch{0=>-.75f,1=>-.25f,2=>.25f,_=>.75f};
                    stop.Prepare(frame,foot,input.Delta,state.Weight,initialized.Contains(2),counter);
                    // Stop's local inputs precede the deferred cache66 Parent callbacks.
                    stopSources.Prepare(stop,path,parent.MovementCandidate.VelocityBlend,parent.Candidate.HipsDirection);
                    reads.AddRange(stopSources.CacheReads.ToArray());initialReads.AddRange(stopSources.CacheInitializationReads.ToArray());
                }
                graph.Prepare(context,reads.ToArray(),initialReads.ToArray(),init,parent,new(1,1,1,0));
                rotate.Prepare(frame,rotateTicks.ToArray(),input.Delta);
                if(graph.HasMovement)
                {
                var chosen=reads.Aggregate((a,b)=>b.Context.Weight>a.Context.Weight?b:a).Context;
                Assert.Equal(chosen,graph.CacheUpdates[0].Context);Assert.Equal(119,graph.DetailsContext.InertializationRequester);
                players.Prepare(frame,graph.Movement.SourceInputs,input.Delta);graph.DetailsSources.CaptureSourceTimes(frame,players);
                inertia.Prepare(graph.DetailsContext,graph.DetailsMachine,graph.Movement);
                direction.Sample(frame,graph.Movement.Direction,graph.Movement.Sources,players,basePose,baseCurves);
                movement.Prepare(frame,1,parent.MovementCandidate.Lean,input.Delta,graph.Movement.InitializeMovement);movement.Evaluate(frame,basePose,baseCurves);
                detailsSampler.Sample(frame,graph.DetailsMachine,graph.DetailsSources,players,movement,pose,curves);
                inertia.Evaluate(frame,pose,curves,AlsPrecisePose.Identity);
                if(hasStop)stopSampler.Sample(frame,stop,stopSources,inertia,finalPose,finalCurves);
                }
                standingPose.Begin(context.Identity,standing);
                Assert.Throws<InvalidOperationException>(()=>standingPose.Evaluate(frame));
                if(standingPose.NeedsState(0))standingPose.CaptureIdleSlot(idlePose,idleCurves,1);
                if(standingPose.NeedsState(1))standingPose.CaptureMovement(inertia);
                if(standingPose.NeedsState(2))standingPose.CaptureStop(stop,stopSources,inertia);
                for(var side=0;side<2;side++)if(standingPose.NeedsState(3+side))
                {rotate.Evaluate(frame,side);standingPose.CaptureRotate(side==0,1.5f);}
                standingPose.Evaluate(frame);
                standingInertia.Prepare(context,standing,graph,graph.HasMovement?inertia:null);
                if(evaluateOuter)standingInertia.Evaluate(frame,standingPose,AlsPrecisePose.Identity);
            }
            Prepare();var updated=graph.HasMovement;var expected=hasStop?finalPose.ToArray():[];var expectedCurves=hasStop?finalCurves.ToArray():[];
            var completePose=standingPose.Pose.ToArray();var completeCurves=standingPose.Curves.ToArray();
            for(var s=0;s<5;s++)if(standingPose.NeedsState(s))states.Add(s);
            Assert.Equal(Enumerable.Range(0,5).Count(standingPose.NeedsState),standingPose.StateEvaluations);
            if(standing.InertializationRequest.HasValue)inertialRequests++;
            if(standing.Candidate.State.Transitions.Count>1)multiEdges++;
            var outerPose=evaluateOuter?standingInertia.Pose.ToArray():[];
            var outerCurves=evaluateOuter?standingInertia.Curves.ToArray():[];
            var pending=standingInertia.PendingRequests;
            if(!evaluateOuter&&pending>0)deferred++;
            if(evaluateOuter)
            {
                var yaw=Array.IndexOf(standingPoseProfile.CurveNames.ToArray(),"RotationYawSpeed");
                Assert.Equal(completeCurves[yaw],outerCurves[yaw]);
                if(standingInertia.IsActive&&!completePose.SequenceEqual(outerPose))smoothed++;
            }
            if(standing.Candidate.State.Transitions.Count==0)
            {
                var s=standing.Candidate.State.CurrentState;
                if(s==1)Assert.Equal(inertia.Pose.ToArray(),completePose);
                if(s==2)Assert.Equal(finalPose,completePose);
                if(s>=3)Assert.Equal(rotate.Pose(s-3).ToArray(),completePose);
                if(s==0)Assert.Equal(idlePose,completePose);
            }
            var nextClocks=clocks.ToArray();
            for(var side=0;side<2;side++)if((standing.Candidate.ClearCachedWeightStates&(1<<(3+side)))!=0)nextClocks[side]=nextClocks[side] with{CachedWeight=0};
            foreach(var tick in rotate.Ticks)
            {
                var history=rotate.Players.ToArray().Single(p=>p.PlayerId==tick.PlayerId);
                nextClocks[tick.PlayerId]=new(f.Standing.RotatePlayers.Players[tick.PlayerId].PropertyIndex,tick.Weight,history.Time,tick.Looping);
            }
            var cacheUpdates=graph.CacheUpdates.ToArray();var ticks=updated?graph.Movement.SourceInputs.ToArray():[];var skipped=graph.OuterSkippedContexts.ToArray();
            if(hasStop)stopped++;if(!updated)idle++;if(reads.Any(r=>r.ReadPropertyIndex==55)&&hasStop)overlap++;
            foreach(var c in cacheUpdates)caches.Add(c.PropertyIndex);
            Assert.Equal(cacheUpdates.Length,cacheUpdates.Select(c=>c.PropertyIndex).Distinct().Count());Assert.Equal(ticks.Length,ticks.Select(t=>t.PlayerId).Distinct().Count());
            if(frame==hz*6+3)
            {
                Assert.Throws<ArgumentException>(()=>standingInertia.Evaluate(frame,standingPose,default));
                Assert.Throws<ArgumentException>(()=>standingInertia.Commit(frame));
            }
            standingInertia.Cancel();standingPose.Cancel();rotate.Cancel();graph.Cancel();parent.Cancel();standing.Cancel();stop.Cancel();stopSources.Cancel();players.Cancel();movement.Cancel();inertia.Cancel();
            Prepare();Assert.Equal(updated,graph.HasMovement);Assert.Equal(cacheUpdates,graph.CacheUpdates.ToArray());Assert.Equal(skipped,graph.OuterSkippedContexts.ToArray());
            Assert.Equal(completePose,standingPose.Pose.ToArray());Assert.Equal(completeCurves,standingPose.Curves.ToArray());
            standingPose.Evaluate(frame);Assert.Equal(completePose,standingPose.Pose.ToArray());Assert.Equal(completeCurves,standingPose.Curves.ToArray());
            Assert.Equal(pending,standingInertia.PendingRequests);
            if(evaluateOuter)
            {
                Assert.Equal(outerPose,standingInertia.Pose.ToArray());Assert.Equal(outerCurves,standingInertia.Curves.ToArray());
                standingInertia.Evaluate(frame,standingPose,AlsPrecisePose.Identity);
                Assert.Equal(outerPose,standingInertia.Pose.ToArray());Assert.Equal(outerCurves,standingInertia.Curves.ToArray());
            }
            if(updated)Assert.Equal(ticks,graph.Movement.SourceInputs.ToArray());
            if(hasStop){Assert.Equal(expected,finalPose);Assert.Equal(expectedCurves,finalCurves);stop.ValidateCommit(frame);stopSources.ValidateCommit(frame);}
            graph.ValidateCommit(frame);parent.ValidateCommit(frame);standing.ValidateCommit(frame);
            if(updated){players.ValidateCommit(frame);movement.ValidateCommit(frame);inertia.ValidateCommit(frame);}
            standingPose.ValidateCommit(frame);rotate.ValidateCommit(frame);standingInertia.ValidateCommit(frame);
            standingInertia.Commit(frame);standingPose.Commit(frame);rotate.Commit(frame);graph.Commit(frame);parent.Commit(frame);standing.Commit(frame);
            if(frame==hz*7)Assert.Equal(1,standingInertia.CommittedHistoryCount);
            if(hasStop){stop.Commit(frame);stopSources.Commit(frame);}if(updated){players.Commit(frame);movement.Commit(frame);inertia.Commit(frame);}
            clocks=nextClocks;counter=counter.Next((ulong)frame+1);
        }
        Assert.True(stopped>0,"No Stop pose samples.");Assert.True(overlap>0,"Move/Stop never overlapped.");Assert.True(idle>0,"No cache-inactive frames.");Assert.Contains(66,caches);Assert.Contains(67,caches);
        Assert.Equal(5,states.Count);Assert.True(inertialRequests>0);Assert.True(multiEdges>0);
        Assert.True(smoothed>0,"Standing inertia never changed a pose.");Assert.Equal(3,deferred);
    }
}
