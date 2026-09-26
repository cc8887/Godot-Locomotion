using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLocomotionPoseTests
{
    internal static readonly Lazy<AlsRefactoredLocomotionPoseProfile> Data=new(()=>
    {
        var ground=AlsRefactoredGroundedHostTests.Data.Value;
        return new(ground.Standing.Catalog,ground.Standing.Triangles,
            MantlingHostFixture.Read("refactored_locomotion_machines"),ground.CurveNames);
    });

    [Fact]
    public void OriginalClosureIncludesIndependentAirPlayersAndPredictionFrames()
    {
        var p=Data.Value;
        Assert.Equal(79,p.BoneNames.Length);Assert.Equal(13,p.Players.Length);
        Assert.Equal(p.Players[0].Source,p.Players[8].Source);
        Assert.NotEqual(p.Players[0].Group,p.Players[8].Group);
        Assert.Equal(AlsRefactoredLocomotionPoseKind.MeshAdditive,p.Node(24).Kind);
        _=new AlsRefactoredLocomotionPoseRuntime(p,AlsRefactoredGroundedHostTests.Data.Value.Standing.Sync);
    }

    // Parent inputs are controlled here. Grounded, Transition, all air players,
    // Sync, additive sampling and inertia are the real production runtimes.
    private sealed class Sink : IAlsRefactoredLocomotionPoseSink
    {
        internal readonly AlsRefactoredCharacterActionRuntime Owner=AlsRefactoredGroundedHostTests.Data.Value.CreateRuntime(7,1);
        internal AlsRefactoredStandingHostInput Ground;
        public AlsRefactoredAirPoseInput Input {get;set;}
        internal int GroundUpdates,GroundSamples;
        public void Validate(in AlsPoseUpdateContext context)=>Owner.ValidateUpdate(context);
        public void Callback(string function,in AlsPoseUpdateContext context){}
        public void StateCallback(in AlsRefactoredLocomotionCallback callback,in AlsPoseUpdateContext context)=>Owner.StopTransitions();
        public void Notify(in AlsGroundedMachineEvent notification,in AlsPoseUpdateContext context){}
        public void PrepareGrounded(in AlsPoseUpdateContext context,bool initialize)
        {
            GroundUpdates++;Owner.Transition.Prepare(context,initialize);
            var source=Owner.Transition.SourceUpdate;
            if(source.Updated)Owner.Grounded!.Prepare(source.Context,Ground,1,0,false,new(0,0),initialize);
        }
        public void EvaluateGrounded(in AlsPrecisePose component)
        {
            GroundSamples++;var source=Owner.Transition.SourceUpdate;
            if(source.Updated)Owner.Grounded!.Evaluate(component);
            Owner.Transition.Evaluate(source.Updated?Owner.Grounded!.Pose:[],source.Updated?Owner.Grounded!.Curves:[]);
        }
        public ReadOnlySpan<AlsPrecisePose> GroundedPose=>Owner.Transition.Pose;
        public ReadOnlySpan<AlsInertialCurve> GroundedCurves=>Owner.Transition.Curves;
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void RealAirGraphAndGroundedCacheRetryIdentically(int hz)
    {
        var p=Data.Value;var sync=AlsRefactoredGroundedHostTests.Data.Value.Standing.Sync;
        var runtime=new AlsRefactoredLocomotionPoseRuntime(p,sync);var clean=new AlsRefactoredLocomotionPoseRuntime(p,sync);
        var sink=new Sink();var other=new Sink();var counter=new AlsGraphTraversalCounter(0,0);
        var states=new HashSet<int>();var jumps=new HashSet<int>();var players=new HashSet<int>();var cacheReuse=false;
        for(var f=0;f<hz*16;f++)
        {
            var t=f/(float)hz;var phase=(int)(t/4);var local=t%4;
            var air=local is >=.5f and <3;var jump=phase%2==0;
            var input=new AlsRefactoredAirPoseInput(new(air?AlsRefactoredLocomotionMode.InAir:AlsRefactoredLocomotionMode.Grounded,
                AlsRefactoredGroundedStance.Standing,air&&jump,phase>=2,false,false,false,phase%2==0?350:100,phase<2?-1:1),
                air?400-(local-.5f)*1000:-850,air&&local>2.5f?.75f:0,1.3f,new(.15f,-.1f),phase%2==0);
            var id=new AlsFrameIdentity(f,7,1);var ctx=new AlsPoseUpdateContext(id,1,1f/hz).WithUpdateCounter(counter);
            void Prepare(AlsRefactoredLocomotionPoseRuntime r,Sink s)
            {
                s.Ground=AlsRefactoredStandingHostTests.Input(0,hz);s.Input=input;s.GroundUpdates=s.GroundSamples=0;
                s.Owner.BeginGlobal(ctx,s.Ground,f==0);r.Prepare(ctx,s,f==0);r.Evaluate(AlsPrecisePose.Identity);
                Assert.InRange(s.GroundUpdates,0,1);Assert.Equal(s.GroundUpdates,s.GroundSamples);
            }
            Prepare(runtime,sink);states.Add(runtime.MainUpdate.State.CurrentState);
            if(runtime.JumpUpdate is {} nested)jumps.Add(nested.State.CurrentState);
            foreach(var tick in runtime.SourceInputs)players.Add(tick.PlayerId);
            cacheReuse|=runtime.MainUpdate.State.CurrentState==4&&sink.GroundUpdates==1;
            var pose=runtime.Pose.ToArray();var curves=runtime.Curves.ToArray();var clocks=runtime.MainClocks.ToArray();
            foreach(var bone in pose)bone.Validate(.001);
            runtime.Cancel();sink.Owner.Discard();Prepare(runtime,sink);Prepare(clean,other);
            Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());Assert.Equal(clocks,runtime.MainClocks.ToArray());
            Assert.Equal(clean.Pose.ToArray(),pose);Assert.Equal(clean.Curves.ToArray(),curves);
            runtime.ValidateCommit(id);clean.ValidateCommit(id);
            runtime.Commit(id);clean.Commit(id);sink.Owner.PostUpdateActions();other.Owner.PostUpdateActions();sink.Owner.Commit(id);other.Owner.Commit(id);
            counter=counter.Next((ulong)f+1);
        }
        Assert.Equal(new[]{0,1,2,3,4},states.Order());Assert.Equal(new[]{1,2,4},jumps.Order());
        Assert.Equal(Enumerable.Range(0,13),players.Order());Assert.True(cacheReuse);
    }

    [Fact]
    public void FullPredictionUsesOriginalLandingFramesAndForcesBothIkCurves()
    {
        var p=Data.Value;var runtime=new AlsRefactoredLocomotionPoseRuntime(p,AlsRefactoredGroundedHostTests.Data.Value.Standing.Sync);
        var sink=new Sink {Ground=AlsRefactoredStandingHostTests.Input(0,60),Input=new(new(AlsRefactoredLocomotionMode.InAir,
            AlsRefactoredGroundedStance.Standing,false,false,false,false,false,0,0),-750,1,1.2f,default,false)};
        var context=new AlsPoseUpdateContext(new(0,7,1),1,1f/60).WithUpdateCounter(new(0,0));
        sink.Owner.BeginGlobal(context,sink.Ground,true);runtime.Prepare(context,sink,true);runtime.Evaluate(AlsPrecisePose.Identity);
        Assert.Empty(runtime.SourceInputs.ToArray());Assert.Equal(0,sink.GroundUpdates);
        for(var b=0;b<p.BoneNames.Length;b++)Assert.Equal(AlsPrecisePoseBlender.Blend(p.PredictionPoses[0][b],p.PredictionPoses[1][b],.5f),runtime.Pose[b]);
        foreach(var name in new[]{"FootLeftIk","FootRightIk","PoseStanding","PoseInAir"})Assert.Equal(new AlsInertialCurve(1),runtime.Curves[p.CurveNames.IndexOf(name)]);
        var pose=runtime.Pose.ToArray();runtime.Evaluate(AlsPrecisePose.Identity);Assert.Equal(pose,runtime.Pose.ToArray());
        runtime.Cancel();sink.Owner.Discard();
    }

    [Fact]
    public void SparseEvaluationHiddenReentryAndLateFaultKeepSourceHistory()
    {
        var p=Data.Value;var sync=AlsRefactoredGroundedHostTests.Data.Value.Standing.Sync;
        var sparse=new AlsRefactoredLocomotionPoseRuntime(p,sync);var full=new AlsRefactoredLocomotionPoseRuntime(p,sync);
        var a=new Sink();var b=new Sink();var counter=new AlsGraphTraversalCounter(0,0);
        for(var f=0;f<180;f++)
        {
            var id=new AlsFrameIdentity(f,7,1);var context=new AlsPoseUpdateContext(id,1,1f/60).WithUpdateCounter(counter);
            var hidden=f is >=35 and <50;var initialize=f is 0 or 120;
            var input=new AlsRefactoredAirPoseInput(new(f<145?AlsRefactoredLocomotionMode.InAir:AlsRefactoredLocomotionMode.Grounded,
                AlsRefactoredGroundedStance.Standing,true,false,false,false,false,350,-1),-700,0,1.3f,new(.1f,.2f),false);
            void Prepare(AlsRefactoredLocomotionPoseRuntime r,Sink s)
            {s.Ground=AlsRefactoredStandingHostTests.Input(0,60);s.Input=input;s.Owner.BeginGlobal(context,s.Ground,initialize);if(!hidden)r.Prepare(context,s,initialize);}
            Prepare(sparse,a);Prepare(full,b);
            if(!hidden)
            {
                if(f==75)
                {
                    Assert.ThrowsAny<ArgumentException>(()=>sparse.Evaluate(new(new(double.NaN,0,0),AlsQuaternion.Identity,new(1,1,1))));
                    Assert.Throws<InvalidOperationException>(()=>sparse.Commit(id));sparse.Cancel();a.Owner.Discard();Prepare(sparse,a);
                }
                full.Evaluate(AlsPrecisePose.Identity);
                if(f%4==0)sparse.Evaluate(AlsPrecisePose.Identity);
                else Assert.Throws<InvalidOperationException>(()=>sparse.Pose.Length);
                Assert.Equal(full.MainClocks.ToArray(),sparse.MainClocks.ToArray());Assert.Equal(full.JumpClocks.ToArray(),sparse.JumpClocks.ToArray());
                Assert.Equal(full.SourceInputs.ToArray(),sparse.SourceInputs.ToArray());
                Assert.Equal(full.MainUpdate.State.CurrentState,sparse.MainUpdate.State.CurrentState);
                sparse.ValidateCommit(id);full.ValidateCommit(id);sparse.Commit(id);full.Commit(id);
            }
            a.Owner.PostUpdateActions();b.Owner.PostUpdateActions();a.Owner.Commit(id);b.Owner.Commit(id);counter=counter.Next((ulong)f+1);
        }
    }
}
