using GodotAls.Core.Locomotion;
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
        var basePose=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[f.DirectionPose.CurveNames.Length];
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[detailsPose.CurveNames.Length];
        var finalPose=new AlsPrecisePose[79];var finalCurves=new AlsInertialCurve[stopPose.CurveNames.Length];
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        var init=new AlsGraphTraversalCounter(0,0);var counter=init;var overlap=0;var stopped=0;var idle=0;var caches=new HashSet<int>();
        for(var frame=0;frame<hz*5;frame++)
        {
            var local=(float)(frame%hz)/hz;var moving=frame<hz*4&&(local<.45f||local>=.6f&&local<.78f);var cycle=frame/hz;
            var angle=cycle%2==0?55f:-55f;var velocity=moving?new AlsDoubleVector(170,cycle%2==0?100:-100,0):AlsDoubleVector.Zero;
            var input=new AlsRefactoredMovementInput(velocity,new(200,100,0),AlsQuaternion.Identity,moving?200:0,1,angle,0,1000,800,"Als.Gait.Running",false,frame==0,1f/hz,1,0,0,.2f);
            var context=new AlsPoseUpdateContext(new(frame,19,1),1,input.Delta,.4f).WithUpdateCounter(counter).WithInertialization(118,true);
            var hasStop=false;var reads=new List<AlsRefactoredMovementCacheRead>();var initialReads=new List<int>();
            void Prepare()
            {
                reads.Clear();initialReads.Clear();hasStop=false;
                parent.Prepare(context.Identity,input);parent.RefreshGrounded(frame);
                standing.Prepare(frame,new(moving,false,false),clocks,input.Delta,updateCounter:counter);
                var update=standing.Candidate;
                var initialized=Enumerable.Range(0,update.InitializationCount).Select(update.GetInitialization).ToArray();
                if(initialized.Contains(1))initialReads.Add(55);
                for(var i=0;i<update.UpdateCount;i++)
                {
                    var state=update.GetUpdate(i);var path=context.WithWeight(state.Weight).WithState(65,state.State,state.InertializationSync);
                    if(state.State==1)reads.Add(new(55,66,path));
                    if(state.State!=2)continue;
                    hasStop=true;var foot=(cycle%4)switch{0=>-.75f,1=>-.25f,2=>.25f,_=>.75f};
                    stop.Prepare(frame,foot,input.Delta,state.Weight,initialized.Contains(2),counter);
                    // Stop's local inputs precede the deferred cache66 Parent callbacks.
                    stopSources.Prepare(stop,path,parent.MovementCandidate.VelocityBlend,parent.Candidate.HipsDirection);
                    reads.AddRange(stopSources.CacheReads.ToArray());initialReads.AddRange(stopSources.CacheInitializationReads.ToArray());
                }
                graph.Prepare(context,reads.ToArray(),initialReads.ToArray(),init,parent,new(1,1,1,0));
                if(!graph.HasMovement)return;
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
            Prepare();var updated=graph.HasMovement;var expected=hasStop?finalPose.ToArray():[];var expectedCurves=hasStop?finalCurves.ToArray():[];
            var cacheUpdates=graph.CacheUpdates.ToArray();var ticks=updated?graph.Movement.SourceInputs.ToArray():[];var skipped=graph.OuterSkippedContexts.ToArray();
            if(hasStop)stopped++;if(!updated)idle++;if(reads.Any(r=>r.ReadPropertyIndex==55)&&hasStop)overlap++;
            foreach(var c in cacheUpdates)caches.Add(c.PropertyIndex);
            Assert.Equal(cacheUpdates.Length,cacheUpdates.Select(c=>c.PropertyIndex).Distinct().Count());Assert.Equal(ticks.Length,ticks.Select(t=>t.PlayerId).Distinct().Count());
            graph.Cancel();parent.Cancel();standing.Cancel();stop.Cancel();stopSources.Cancel();players.Cancel();movement.Cancel();inertia.Cancel();
            Prepare();Assert.Equal(updated,graph.HasMovement);Assert.Equal(cacheUpdates,graph.CacheUpdates.ToArray());Assert.Equal(skipped,graph.OuterSkippedContexts.ToArray());
            if(updated)Assert.Equal(ticks,graph.Movement.SourceInputs.ToArray());
            if(hasStop){Assert.Equal(expected,finalPose);Assert.Equal(expectedCurves,finalCurves);stop.ValidateCommit(frame);stopSources.ValidateCommit(frame);}
            graph.ValidateCommit(frame);parent.ValidateCommit(frame);standing.ValidateCommit(frame);
            if(updated){players.ValidateCommit(frame);movement.ValidateCommit(frame);inertia.ValidateCommit(frame);}
            graph.Commit(frame);parent.Commit(frame);standing.Commit(frame);
            if(hasStop){stop.Commit(frame);stopSources.Commit(frame);}if(updated){players.Commit(frame);movement.Commit(frame);inertia.Commit(frame);}
            counter=counter.Next((ulong)frame+1);
        }
        Assert.True(stopped>0,"No Stop pose samples.");Assert.True(overlap>0,"Move/Stop never overlapped.");Assert.True(idle>0,"No cache-inactive frames.");Assert.Contains(66,caches);Assert.Contains(67,caches);
    }
}
