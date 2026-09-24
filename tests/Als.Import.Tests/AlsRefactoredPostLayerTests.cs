using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPostLayerTests
{
    private static AlsRefactoredPostLayerProfile Profile()
    {
        string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
        return AlsRefactoredPostLayerCompiler.Compile(Read("layering_graphs"),Read("layering_inventory"),Read("base_pose_inputs"),Read("head_inputs"));
    }
    private static string[] Names()=>AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(new[]{"PoseStanding","PoseCrouching","Probe"}).ToArray();
    private static AlsLocalPose Float(AlsPrecisePose p)=>new(new((float)p.Position.X,(float)p.Position.Y,(float)p.Position.Z),
        new((float)p.Rotation.X,(float)p.Rotation.Y,(float)p.Rotation.Z,(float)p.Rotation.W),new((float)p.Scale.X,(float)p.Scale.Y,(float)p.Scale.Z));

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void FullLayerAndHeadShareFeedbackAndRollbackAsOneCandidate(int hz)
    {
        var profile=Profile();var names=Names();var clean=profile.CreateRuntime(9,1,names);
        var look=new FaultLook(profile.Head.Look.CreateSampler());var layout=profile.BasePoses[37].Pose;
        var retry=new AlsRefactoredPostLayerRuntime(9,1,profile.Layering,layout.BoneNames,layout.Parents,names,
            layout.ReferencePose.ToArray().Select(Float).ToArray(),profile.Head.Settings,look);
        var source=new Inputs(profile,names);var referenceSource=new Inputs(profile,names);
        var graph=default(AlsAnimationGraphFrame);var expectedAlpha=1f;var faultCount=0;
        for(var frame=1;frame<=hz*2;frame++)
        {
            var id=new AlsFrameIdentity(frame,9,1);graph=graph.Next(id,(ulong)frame);
            var phase=(frame-1)*8/(hz*2);var visited=phase!=5;
            source.Phase=referenceSource.Phase=phase;
            var input=new AlsRefactoredViewInput(frame*.9,25,0,0,0,0,0,0,false,false,
                AlsRotationMode.LookingDirection,false,frame==1,false,0,1f/hz,1f/hz,999,999);
            // Deliberately wrong current-frame curve values above must never
            // replace this stage's committed feedback.
            clean.Prepare(graph,input,referenceSource,visited);
            Assert.Equal(expectedAlpha,clean.CandidateView.HeadBlendAmount);
            var before=(retry.CommittedIdentity,retry.CommittedView,retry.CommittedSpine,retry.CommittedHead);
            if(frame==2||frame==hz+2)
            {
                retry.Prepare(graph,input,source,visited);look.Fail=true;
                Assert.Throws<InvalidOperationException>(()=>retry.Evaluate());
                Assert.Throws<InvalidOperationException>(()=>retry.Commit(id));
                Assert.Equal(before,(retry.CommittedIdentity,retry.CommittedView,retry.CommittedSpine,retry.CommittedHead));
                look.Fail=false;faultCount++;
            }
            retry.Prepare(graph,input,source,visited);
            Assert.Equal(clean.CandidateHead,retry.CandidateHead);
            if(visited)
            {
                clean.Evaluate();retry.Evaluate();
                Assert.Equal(clean.Pose.ToArray(),retry.Pose.ToArray());Assert.Equal(clean.Curves.ToArray(),retry.Curves.ToArray());
                expectedAlpha=1-clean.Curves[Array.IndexOf(names,"ViewBlock")].Value;
                var first=retry.Pose.ToArray();retry.Cancel();retry.Prepare(graph,input,source);retry.Evaluate();
                Assert.Equal(first,retry.Pose.ToArray());
            }
            else Assert.Throws<InvalidOperationException>(()=>retry.Evaluate());
            clean.ValidateCommit(id);retry.ValidateCommit(id);clean.Commit(id);retry.Commit(id);
            Assert.Equal(clean.CommittedHead,retry.CommittedHead);Assert.Equal(clean.CommittedView,retry.CommittedView);
            Assert.Equal(clean.CommittedSpine,retry.CommittedSpine);
        }
        Assert.Equal(2,faultCount);
        Assert.Throws<ArgumentException>(()=>retry.Prepare(graph,default,source));
        Assert.Throws<InvalidOperationException>(()=>retry.Evaluate());
    }

    [Fact]
    public void RejectsPrematureCommitForeignOwnerAndMissingFeedbackChannels()
    {
        var profile=Profile();var names=Names();var runtime=profile.CreateRuntime(9,1,names);var source=new Inputs(profile,names);
        var graph=default(AlsAnimationGraphFrame).Next(new(1,9,1),1);
        var input=new AlsRefactoredViewInput(0,0,0,0,0,0,0,0,false,false,AlsRotationMode.LookingDirection,false,true,false,0,.016f,.016f,0,0);
        runtime.Prepare(graph,input,source);
        Assert.Throws<InvalidOperationException>(()=>runtime.Commit(graph.Identity));
        runtime.Cancel();Assert.Equal(default,runtime.CommittedIdentity);
        Assert.Throws<ArgumentException>(()=>runtime.Prepare(graph with {Identity=new(1,10,1)},input,source));
        Assert.Throws<ArgumentException>(()=>profile.CreateRuntime(9,1,names.Where(n=>n!="PoseAiming").ToArray()));
        runtime.Prepare(graph,input,source);source.Fail=true;
        Assert.Throws<InvalidOperationException>(()=>runtime.Evaluate());source.Fail=false;
        runtime.Prepare(graph,input,source);runtime.Evaluate();runtime.Commit(graph.Identity);
    }

    private sealed class FaultLook(IAlsRefactoredLookPoseSampler inner):IAlsRefactoredLookPoseSampler
    {
        public bool Fail;
        public void Evaluate(float pitch,float time,Span<AlsPrecisePose> pose)
        {inner.Evaluate(pitch,time,pose);if(Fail)throw new InvalidOperationException("Injected Head sampling failure.");}
    }
    private sealed class Inputs:IAlsLayerBlendingSink
    {
        private readonly Dictionary<int,AlsLocalPose[]> _poses;private readonly string[] _names;
        public int Phase;public bool Fail;
        public Inputs(AlsRefactoredPostLayerProfile profile,string[] names)
        {
            _names=names;_poses=profile.BasePoses.ToDictionary(p=>p.Key,p=>
            {var pose=new AlsPrecisePose[79];p.Value.CreateSampler().Evaluate(pose,[]);return pose.Select(Float).ToArray();});
        }
        public void InitializeInput(int index,string name){}
        public void CacheInputBones(int index,string name){}
        public void UpdateInput(int index,string name,in AlsPoseUpdateContext context){}
        public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            if(Fail)throw new InvalidOperationException("Injected upstream sample failure.");
            curves.Clear();if(_poses.TryGetValue(index,out var basis)){basis.CopyTo(pose);return;}
            _poses[name=="Locomotion Input"?37:38].CopyTo(pose);
            if(name!="Locomotion Input")return;
            Set(curves,"ViewBlock",Phase==1?1:Phase==3?.5f:0);Set(curves,"PoseAiming",0);Set(curves,"PoseStanding",1);
            Set(curves,"LayerArmLeft",.6f);Set(curves,"LayerArmRight",.4f);Set(curves,"LayerSpine",.3f);Set(curves,"Probe",Phase);
        }
        private void Set(Span<AlsInertialCurve> curves,string key,float value)=>curves[Array.IndexOf(_names,key)]=new(value);
        public void InitializeSlot(int index,string name){}
        public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>AlsSlotWeights.Passthrough;
        public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context){}
        public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool evaluated,ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)=>throw new InvalidOperationException("No active Montage.");
        public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> contexts){}
    }
}
