using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredHeadRuntimeTests
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void ActualLookSourcesResumeInitializeRetryAndPreserveBaseCurves(int hz)
    {
        var inputs=Read("head_inputs");var graphs=Read("layering_graphs");
        var profile=AlsRefactoredHeadGraphCompiler.Compile(graphs,Read("layering_inventory"),inputs);
        var resource=profile.Look;
        var settings=profile.Settings;
        var sampler=new Samples(resource.CreateSampler());
        var owner=new AlsRefactoredHeadRuntime(9,1,settings,resource.Parents,sampler);
        var baseResources=AlsRefactoredBasePoseCompiler.Compile(Read("base_pose_inputs"),Read("layering_inventory"),graphs);
        var basis=new AlsPrecisePose[79];baseResources[37].CreateSampler().Evaluate(basis,[]);
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[3];
        AlsInertialCurve[] baseCurves=[new(.4f),default,new(0)];
        var view=AlsRefactoredViewState.Initial;var spine=AlsRefactoredSpineState.Initial;
        var graph=default(AlsAnimationGraphFrame);var initializes=0;var moved=false;
        for(var frame=1;frame<=hz*3;frame++)
        {
            var identity=new AlsFrameIdentity(frame,9,1);graph=graph.Next(identity,(ulong)frame);
            if(frame==hz*3)graph=graph with {Initialization=graph.Initialization.Next((ulong)frame)};
            var phase=(frame-1)*6/(hz*3);var visible=phase!=3;
            var input=new AlsRefactoredViewInput(frame*.7,20,0,0,0,0,0,0,false,false,
                AlsRotationMode.LookingDirection,false,false,false,0,1f/hz,1f/hz,phase==1?1:phase==5?.5f:0,0);
            var parent=AlsRefactoredViewModel.RefreshView(input,view,spine);
            var committed=owner.Committed;
            if(frame==hz+2)
            {
                owner.Prepare(graph,input,parent.View,visible);sampler.Fail=true;
                Assert.Throws<InvalidOperationException>(()=>owner.Evaluate(basis,baseCurves,pose,curves));
                Assert.Throws<InvalidOperationException>(()=>owner.Commit(identity));
                Assert.Equal(committed,owner.Committed);owner.Cancel();sampler.Fail=false;
            }
            owner.Prepare(graph,input,parent.View,visible);var candidate=owner.Candidate;
            if(candidate.Initialized)
            {
                initializes++;Assert.Equal(0,candidate.Head.YawVelocity);
                Assert.Equal(Math.Clamp(parent.View.YawAngle,-175,175),candidate.Head.Yaw);
            }
            if(!candidate.HeadUpdated)Assert.Equal(committed.Head,candidate.Head);
            if(visible)
            {
                owner.Evaluate(basis,baseCurves,pose,curves);var first=pose.ToArray();
                owner.Evaluate(basis,baseCurves,pose,curves);
                Assert.Equal(first,pose);Assert.Equal(baseCurves,curves);Assert.Equal(candidate,owner.Candidate);
                if(phase==1)Assert.Equal(basis,pose);
                else moved|=!basis.SequenceEqual(pose);
                owner.Cancel();owner.Prepare(graph,input,parent.View,visible);
                owner.Evaluate(basis,baseCurves,pose,curves);Assert.Equal(first,pose);
            }
            else Assert.Throws<InvalidOperationException>(()=>owner.Evaluate(basis,baseCurves,pose,curves));
            Assert.Equal(candidate,owner.Candidate);owner.Commit(identity);
            Assert.Equal(candidate,owner.Committed);(view,spine)=parent;
        }
        Assert.Equal(4,initializes);Assert.True(moved);Assert.True(sampler.Calls>0);
        Assert.Throws<ArgumentException>(()=>owner.Prepare(graph,default,view));
    }

    [Theory]
    [InlineData("edge")][InlineData("callback")][InlineData("teleport")][InlineData("rootSpace")][InlineData("alpha")]
    public void UnsupportedHeadGraphChangesAreRejected(string mutation)
    {
        var inventory=JsonNode.Parse(Read("layering_inventory"))!;
        var nodes=inventory["blueprints"]![3]!["nodes"]!.AsArray();
        JsonNode Node(int property)=>nodes.Single(n=>n!["propertyIndex"]!.GetValue<int>()==property)!;
        if(mutation=="edge")Node(3)["runtime"]!["source"]!["linkId"]=1;
        if(mutation=="callback")Node(3)["runtime"]!["callSite"]="OnUpdate";
        if(mutation=="teleport")Node(5)["runtime"]!["bTeleportToNormalizedTime"]=false;
        if(mutation=="rootSpace")Node(2)["runtime"]!["bRootSpaceAdditive"]=true;
        if(mutation=="alpha")Node(2)["runtime"]!["alphaScaleBias"]!["scale"]=.5f;
        Assert.Throws<ArgumentException>(()=>AlsRefactoredHeadGraphCompiler.Compile(Read("layering_graphs"),inventory.ToJsonString(),Read("head_inputs")));
    }

    private sealed class Samples(IAlsRefactoredLookPoseSampler inner):IAlsRefactoredLookPoseSampler
    {
        public bool Fail;public int Calls;
        public void Evaluate(float pitch,float time,Span<AlsPrecisePose> output)
        {Calls++;inner.Evaluate(pitch,time,output);if(Fail)throw new InvalidOperationException("Injected Look sample failure.");}
    }
}
