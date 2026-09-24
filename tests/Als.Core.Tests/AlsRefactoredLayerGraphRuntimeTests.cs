using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredLayerGraphRuntimeTests
{
    private static readonly string[] Names=["LayerHead","LayerHeadSlot"];
    private static readonly AlsLocalPose[] Rest=[new(Vector3.Zero,Quaternion.Identity,Vector3.One),new(Vector3.UnitY,Quaternion.Identity,Vector3.One)];
    private static AlsLayerBlendingDefinition Definition()=>new("Refactored operator integration",13,12,
    [
        new(0,"locomotion",AlsLayerPoseKind.Input,[],[],"Locomotion"),
        new(1,"save-locomotion",AlsLayerPoseKind.SaveCache,[0],[]),new(2,"use-locomotion",AlsLayerPoseKind.UseCache,[1],[]),
        new(3,"overlay",AlsLayerPoseKind.Input,[],[],"Overlay"),
        new(4,"save-overlay",AlsLayerPoseKind.SaveCache,[3],[]),new(5,"use-overlay",AlsLayerPoseKind.UseCache,[4],[]),
        new(6,"head",AlsLayerPoseKind.TwoWayBlend,[2,5],[new(AlsLayerAlphaKind.Property,Name:"HeadBlendAmount")]),
        new(7,"head-filter",AlsLayerPoseKind.LayeredBlend,[2,6],[new(AlsLayerAlphaKind.Constant,1)],Filters:[[new("neck",0)]]),
        new(8,"slot-reset",AlsLayerPoseKind.CurveReset,[5],[],ModifiedCurves:["LayerHeadSlot"],ModifiedValues:[0]),
        new(9,"curves-slot",AlsLayerPoseKind.Slot,[8],[],"Curves"),
        new(10,"curve-accumulate",AlsLayerPoseKind.CurveAccumulate,[2,9],[]),
        new(11,"curve-override",AlsLayerPoseKind.CurveOverride,[7,10],[]),
        new(12,"root",AlsLayerPoseKind.Root,[11],[])
    ],[4,1],AlsLayerPropertySchema.Refactored);
    private static AlsLayerBlendingRuntime Runtime()=>new(Definition(),["root","neck"],[-1,0],Names,Rest);

    [Theory]
    [InlineData(-1f,0f)][InlineData(.25f,2.5f)][InlineData(1f,10f)][InlineData(2f,10f)]
    public void RefactoredPropertiesDriveBoneBranchesWhileCurveTailKeepsBothCaches(float alpha,float x)
    {
        var owner=Runtime();var sink=new Sink();var id=new AlsFrameIdentity(1,5,1);
        var graph=default(AlsAnimationGraphFrame).Next(id,1);var input=new AlsRefactoredLayeringInput {Identity=id,HeadBlendAmount=alpha};
        var pose=new AlsLocalPose[2];var curves=new AlsInertialCurve[2];
        Prepare();owner.Evaluate(pose,curves);
        Assert.Equal(0,pose[0].Position.X);Assert.Equal(x,pose[1].Position.X);
        Assert.Equal(new AlsInertialCurve(0),curves[0]);Assert.Equal(new AlsInertialCurve(0),curves[1]);
        Assert.Equal(1,sink.LocomotionUpdates);Assert.Equal(1,sink.OverlayUpdates);
        Assert.Equal(1,sink.LocomotionEvaluations);Assert.Equal(1,sink.OverlayEvaluations);
        var saved=pose.ToArray();owner.Cancel();sink.Reset();Prepare();owner.Evaluate(pose,curves);Assert.Equal(saved,pose);owner.Commit();
        void Prepare()=>owner.Prepare(new(id,1,1f/60),input,[],graph.Initialization,graph.Bones,graph.Evaluation,sink);
    }
    [Fact]
    public void OverridingCurvesSlotHidesOnlyItsSourceAndUnvisitedFrameDoesNotEvaluate()
    {
        var owner=Runtime();var sink=new Sink {Override=true};var graph=default(AlsAnimationGraphFrame);var previous=default(AlsFrameIdentity);
        var pose=new AlsLocalPose[2];var curves=new AlsInertialCurve[2];
        for(var frame=1;frame<=3;frame++)
        {
            var id=new AlsFrameIdentity(frame,5,1);graph=graph.Next(id,(ulong)frame);sink.Reset();
            owner.Prepare(new(id,1,1f/60),new AlsRefactoredLayeringInput {Identity=id,FeedbackIdentity=previous,HeadBlendAmount=0},
                frame==1?[]:curves,graph.Initialization,graph.Bones,graph.Evaluation,sink,frame!=2);
            if(frame==2)
            {Assert.Equal(0,sink.LocomotionUpdates+sink.OverlayUpdates);Assert.Throws<InvalidOperationException>(()=>owner.Evaluate(pose,curves));}
            else
            {
                owner.Evaluate(pose,curves);Assert.Equal(Rest,pose);Assert.Equal(0,sink.OverlayUpdates+sink.OverlayEvaluations);
                Assert.Equal(1,sink.LocomotionUpdates);Assert.False(sink.SlotSourceEvaluated);
                Assert.Equal(new AlsInertialCurve(1),curves[0]);Assert.Equal(new AlsInertialCurve(.6f),curves[1]);
            }
            owner.Commit();previous=id;
        }
    }
    [Fact]
    public void WrongSchemaAndFailedSourceDoNotReplacePreparedOrCommittedHistory()
    {
        var owner=Runtime();var sink=new Sink();var id=new AlsFrameIdentity(1,5,1);var graph=default(AlsAnimationGraphFrame).Next(id,1);
        var input=new AlsRefactoredLayeringInput {Identity=id,HeadBlendAmount=.5f};var pose=new AlsLocalPose[2];var curves=new AlsInertialCurve[2];
        Assert.Throws<ArgumentException>(()=>owner.Prepare(new(id,1,.1f),new AlsLayeringInput {Identity=id},[],graph.Initialization,graph.Bones,graph.Evaluation,sink));
        Prepare();Assert.Throws<InvalidOperationException>(()=>owner.Prepare(new(id,1,.1f),input with {HeadBlendAmount=1},[],graph.Initialization,graph.Bones,graph.Evaluation,sink));
        owner.Evaluate(pose,curves);Assert.Equal(5,pose[1].Position.X);owner.Cancel();
        sink.Fail=true;Prepare();Assert.Throws<InvalidOperationException>(()=>owner.Evaluate(pose,curves));Assert.Equal(default,owner.Identity);
        sink.Fail=false;Prepare();owner.Evaluate(pose,curves);Assert.Equal(5,pose[1].Position.X);owner.Commit();
        void Prepare()=>owner.Prepare(new(id,1,.1f),input,[],graph.Initialization,graph.Bones,graph.Evaluation,sink);
    }
    private sealed class Sink : IAlsLayerBlendingSink
    {
        public int LocomotionUpdates,OverlayUpdates,LocomotionEvaluations,OverlayEvaluations;
        public bool Override,Fail,SlotSourceEvaluated;
        public void Reset(){LocomotionUpdates=OverlayUpdates=LocomotionEvaluations=OverlayEvaluations=0;}
        public void InitializeInput(int index,string name){}
        public void CacheInputBones(int index,string name){}
        public void UpdateInput(int index,string name,in AlsPoseUpdateContext context){if(name=="Locomotion")LocomotionUpdates++;else OverlayUpdates++;}
        public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            if(Fail)throw new InvalidOperationException("Injected source failure.");
            Rest.CopyTo(pose);curves.Clear();
            if(name=="Locomotion"){LocomotionEvaluations++;curves[0]=new(-1);}
            else {OverlayEvaluations++;pose[1]=pose[1] with {Position=new(10,1,0)};curves[0]=new(1);curves[1]=new(.8f);}
        }
        public void InitializeSlot(int index,string name){}
        public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>Override?new(0,1,1):AlsSlotWeights.Passthrough;
        public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context){}
        public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool sourceEvaluated,
            ReadOnlySpan<AlsLocalPose> sourcePose,ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {SlotSourceEvaluated=sourceEvaluated;Rest.CopyTo(pose);curves[0]=new(2);curves[1]=new(.6f);}
        public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped){}
    }
}
