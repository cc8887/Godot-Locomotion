using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStopEvaluationTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog=new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        public readonly AlsRefactoredStopPoseGraph Graph;
        public readonly AlsRefactoredSkeletonCurves Metadata;
        public readonly AlsRefactoredStopPose Pose;
        public Fixture()
        {
            Graph=new(Catalog,new(MantlingHostFixture.Read("refactored_stance_machines"),Catalog));
            Metadata=new(MantlingHostFixture.Read("refactored_skeleton_curves"),Catalog);
            Pose=new(Catalog,Graph,Metadata,["FootLeftLock","FootRightLock","SyntheticBaseOnly"]);
        }
    }
    private static readonly Lazy<Fixture> Data=new(()=>new());

    [Theory]
    [InlineData("catalog")] [InlineData("linked")] [InlineData("duplicate")] [InlineData("missing")]
    public void SkeletonMetadataDoesNotSilentlyDropFilters(string change)
    {
        var root=JsonNode.Parse(MantlingHostFixture.Read("refactored_skeleton_curves"))!;var native=root["nativeText"]!.GetValue<string>();
        Assert.Equal(43,Data.Value.Metadata.Names.Length);
        switch(change)
        {
            case "catalog":root["catalogSha256"]=new string('0',64);break;
            case "linked":native=native.Replace("(\"FootLeftLock\", ())","(\"FootLeftLock\", (LinkedBones=((BoneName=\"thigh_l\"))))",StringComparison.Ordinal);break;
            case "duplicate":native=native.Replace("(\"FootLeftLock\", ())","(\"FootRightLock\", ())",StringComparison.Ordinal);break;
            case "missing":native=native.Replace("CurveMetaData=","MissingMetaData=",StringComparison.Ordinal);break;
        }
        root["nativeText"]=native;
        Assert.Throws<ArgumentException>(()=>new AlsRefactoredSkeletonCurves(root.ToJsonString(),Data.Value.Catalog));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SingleForwardPlantUsesOriginalMaskAndCurveOverride(bool right)
    {
        var f=Data.Value;var machine=new AlsRefactoredStopRuntime(f.Graph.Resources);var source=new AlsRefactoredStopSourceRuntime(f.Graph);
        var context=new AlsPoseUpdateContext(new(0,7,1),1,.2f).WithUpdateCounter(new(0,0));
        machine.Prepare(0,right?.25f:-.25f,.2f,updateCounter:context.UpdateCounter);
        source.Prepare(machine,context,new(1,0,0,0),AlsRefactoredHipsDirection.Forward);
        var basis=f.Catalog.CompileAbsolutePose("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose").ReferencePose.ToArray();
        var leaf=new AlsPrecisePose[79];var leafCurves=new AlsInertialCurve[f.Graph.Evaluators.CurveNames.Length];
        f.Graph.Evaluators.Sample(right?28:41,leaf,leafCurves);
        var expected=new AlsPrecisePose[79];var actual=new AlsPrecisePose[79];var curves=new AlsInertialCurve[f.Pose.CurveNames.Length];
        AlsMeshSpacePoseBlend.Blend(basis,leaf,f.Graph.Evaluators.Parents,f.Graph.States[right?4:3].BoneWeights,new AlsQuaternion[237],expected);
        var sampler=f.Pose.CreateSampler();sampler.Sample(0,machine,source,basis,[new(.2f),new(.3f),new(.7f)],actual,curves);
        for(var b=0;b<79;b++)Near(expected[b],actual[b]);
        Assert.Equal(new AlsInertialCurve(.7f),curves[Index("SyntheticBaseOnly")]);
        Assert.Equal(new AlsInertialCurve(1),curves[Index(right?"FootRightLock":"FootLeftLock")]);
        for(var c=0;c<leafCurves.Length;c++)
        {
            var name=f.Graph.Evaluators.CurveNames[c];if(name==(right?"FootRightLock":"FootLeftLock")||!leafCurves[c].Present)continue;
            Assert.Equal(leafCurves[c],curves[Index(name)]);
        }
        int Index(string n)=>Array.FindIndex(f.Pose.CurveNames.ToArray(),x=>x.Equals(n,StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ContinuousStopPosePreservesUpperBodyAndCandidateRetry(int hz)
    {
        var f=Data.Value;var machine=new AlsRefactoredStopRuntime(f.Graph.Resources);var source=new AlsRefactoredStopSourceRuntime(f.Graph);
        var sampler=f.Pose.CreateSampler();var other=f.Pose.CreateSampler();
        var basis=f.Catalog.CompileAbsolutePose("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose").ReferencePose.ToArray();
        var actual=new AlsPrecisePose[79];var expected=new AlsPrecisePose[79];var curves=new AlsInertialCurve[f.Pose.CurveNames.Length];var expectedCurves=new AlsInertialCurve[curves.Length];
        var counter=new AlsGraphTraversalCounter(0,0);var states=new HashSet<int>();var blends=0;
        for(var frame=0;frame<hz*6;frame++)
        {
            var phase=frame/(hz/2);var reset=frame%(hz/2)==0;var amount=(phase%4)switch {0=>-.75f,1=>-.25f,2=>.25f,_=>.75f};
            var context=new AlsPoseUpdateContext(new(frame,7,1),frame%9==0?0:1,1f/hz).WithUpdateCounter(counter);
            var velocity=frame%11==0?Vector4.Zero:frame%7==0?new Vector4(1,0,0,0):new Vector4(.15f,.1f,.35f,.4f);
            var hips=(AlsRefactoredHipsDirection)((frame/3)%6);
            void Prepare(){machine.Prepare(frame,amount,context.Delta,context.Weight,reset,counter);source.Prepare(machine,context,velocity,hips);}
            Prepare();var state=machine.Candidate.State.CurrentState;states.Add(state);if(machine.Candidate.State.Transitions.Count>0)blends++;
            sampler.Sample(frame,machine,source,basis,[new(.2f),new(.3f),new(.7f)],actual,curves);
            source.Cancel();machine.Cancel();Prepare();other.Sample(frame,machine,source,basis,[new(.2f),new(.3f),new(.7f)],expected,expectedCurves);
            Assert.Equal(expected,actual);Assert.Equal(expectedCurves,curves);
            // Both original branch masks exclude root, pelvis and the upper-body chain.
            for(var b=0;b<79;b++)
            {
                Assert.True(actual[b].Position.IsFinite&&actual[b].Scale.IsFinite);
                Assert.InRange(Math.Abs(actual[b].Rotation.LengthSquared-1),0,1e-10);
                if(state<3||f.Graph.States[state].BoneWeights[b]==0)
                {
                    // The transition stack uses float alpha/complement before
                    // double TRS accumulation; even identical inputs can drift.
                    var position=basis[b].Position;var scale=basis[b].Scale;var stack=machine.Candidate.State.Transitions;
                    for(var edge=0;edge<stack.Count;edge++)
                    {
                        var alpha=stack.GetTransition(edge).Alpha;
                        position=position*(1-alpha)+basis[b].Position*alpha;scale=scale*(1-alpha)+basis[b].Scale*alpha;
                    }
                    Assert.InRange((actual[b].Position-position).LengthSquared,0,1e-20);
                    Assert.InRange((actual[b].Scale-scale).LengthSquared,0,1e-20);
                }
            }
            source.Commit(frame);machine.Commit(frame);counter=counter.Next((ulong)frame+1);
        }
        Assert.Equal(4,states.Count);Assert.True(blends>0);
    }
    private static void Near(AlsPrecisePose expected,AlsPrecisePose actual)
    {
        Assert.InRange((expected.Position-actual.Position).LengthSquared,0,1e-20);
        Assert.InRange((expected.Scale-actual.Scale).LengthSquared,0,1e-20);
        Assert.InRange(1-Math.Abs(AlsQuaternion.Dot(expected.Rotation,actual.Rotation)), -1e-12,1e-12);
    }
}
