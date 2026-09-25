using GodotAls.Import.Compilation;
using GodotAls.Core.Locomotion;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStopPoseTests
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),
        p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    private static readonly Lazy<AlsRefactoredStopPoseGraph> Data=new(()=>{var c=Catalog();return new(c,new(MantlingHostFixture.Read("refactored_stance_machines"),c));});
    [Fact]
    public void FullStateTopologyAndMasksMatchOriginal()
    {
        var graph=Data.Value;
        Assert.Equal(new[]{51,49,46,35,22},graph.States.ToArray().Select(s=>s.Read));
        Assert.Equal(new[]{34,21},graph.States.ToArray().Skip(3).Select(s=>s.LeftSelector));
        Assert.Equal(new[]{33,20},graph.States.ToArray().Skip(3).Select(s=>s.RightSelector));
        foreach(var s in graph.States.ToArray().Skip(3))
        { Assert.Equal(79,s.BoneWeights.Length);Assert.Equal(0,s.BoneWeights[0]);Assert.Contains(1f,s.BoneWeights.ToArray()); }
    }

    [Fact]
    public void HipsSelectorsKeepOriginalAsymmetryAndHiddenHistory()
    {
        var g=Data.Value;var machine=new AlsRefactoredStopRuntime(g.Resources);var source=new AlsRefactoredStopSourceRuntime(g);
        var counter=new AlsGraphTraversalCounter(0,0);var frame=0;
        void Step(Vector4 velocity,AlsRefactoredHipsDirection hips,float expectedRight,float expectedLeft)
        {
            var context=new AlsPoseUpdateContext(new(frame,7,1),.8f,.025f,.6f).WithUpdateCounter(counter).WithState(65,2).WithInertialization(118,true);
            machine.Prepare(frame,-.25f,context.Delta,context.Weight,updateCounter:counter);source.Prepare(machine,context,velocity,hips);
            Assert.Equal(expectedRight,source.SelectorWeights(3,true).Y,5);Assert.Equal(expectedLeft,source.SelectorWeights(3,false).Y,5);
            foreach(var leaf in source.EvaluatorUpdates)
            {
                Assert.Equal(0,leaf.Context.RootMotionWeight);Assert.Equal(2,leaf.Context.StateCount);
                Assert.Equal(new AlsActiveAnimationState(53,3),leaf.Context.GetState(1));Assert.Equal(118,leaf.Context.InertializationRequester);
            }
            foreach(var read in source.CacheReads){Assert.Equal(66,read.CachePropertyIndex);Assert.Equal(.6f,read.Context.RootMotionWeight);}
            if(frame==6)Assert.Contains(source.EvaluatorUpdates.ToArray(),u=>u.PropertyIndex==36&&u.Context.Weight==0&&!u.Context.IsActive);
            source.ValidateTraversal(frame,machine);source.Commit(frame);machine.Commit(frame);counter=counter.Next((ulong)++frame);
        }
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.Forward,0,0);
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.RightBackward,.25f,0);
        Step(new(1,0,0,0),AlsRefactoredHipsDirection.LeftBackward,.25f,0); // Hidden lists hold history.
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.RightBackward,.5f,0);
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.RightBackward,.75f,0);
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.RightBackward,1,0);
        Step(new(0,0,1,1),AlsRefactoredHipsDirection.LeftBackward,0,1); // Immediate return/right and left selection.
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ContinuousSourcesKeepDistinctCacheReadsAndTransactionalSelectors(int hz)
    {
        var g=Data.Value;var machine=new AlsRefactoredStopRuntime(g.Resources);var source=new AlsRefactoredStopSourceRuntime(g);
        var counter=new AlsGraphTraversalCounter(0,0);var states=new HashSet<int>();var readers=new HashSet<int>();var leaves=new HashSet<int>();
        var multiReads=0;var zeroTicks=0;
        for(var frame=0;frame<hz*6;frame++)
        {
            var phase=frame/(hz/2);var reset=frame%(hz/2)==0;
            var amount=(phase%4)switch {0=>-.75f,1=>-.25f,2=>.25f,_=>.75f};
            var context=new AlsPoseUpdateContext(new(frame,11,2),frame%9==0?0:.7f,1f/hz,.4f).WithUpdateCounter(counter).WithState(65,2).WithInertialization(118,true);
            if(frame%5==0)context=context.AsInactive();
            var hips=(AlsRefactoredHipsDirection)((frame/3)%6);
            var velocity=frame%17==0?Vector4.Zero:frame%13==0?new Vector4(1,0,0,0):new Vector4(.1f,.2f,.3f,.4f);
            machine.Prepare(frame,amount,context.Delta,context.Weight,reset,counter);source.Prepare(machine,context,velocity,hips);
            states.Add(machine.Candidate.State.CurrentState);if(source.CacheReads.Length>1)multiReads++;
            var expected=source.EvaluatorUpdates.ToArray();var expectedReads=source.CacheReads.ToArray();var inits=source.CacheInitializationReads.ToArray();
            foreach(var read in expectedReads)readers.Add(read.ReadPropertyIndex);
            foreach(var leaf in expected){leaves.Add(leaf.PropertyIndex);if(leaf.Context.Weight==0)zeroTicks++;}
            Assert.Equal(expected.Select(e=>e.PropertyIndex).Distinct().Count(),expected.Length);
            if(reset)Assert.Contains(51,inits);
            source.Cancel();machine.Cancel();machine.Prepare(frame,amount,context.Delta,context.Weight,reset,counter);source.Prepare(machine,context,velocity,hips);
            Assert.Equal(expected,source.EvaluatorUpdates.ToArray());Assert.Equal(expectedReads,source.CacheReads.ToArray());Assert.Equal(inits,source.CacheInitializationReads.ToArray());
            source.ValidateTraversal(frame,machine);source.Commit(frame);machine.Commit(frame);counter=counter.Next((ulong)frame+1);
        }
        Assert.Equal(4,states.Count);Assert.Equal(5,readers.Count);Assert.Equal(12,leaves.Count);Assert.True(multiReads>0);Assert.True(zeroTicks>0);
    }

    [Theory]
    [InlineData("link")] [InlineData("curve")] [InlineData("mask")] [InlineData("branch")]
    [InlineData("mesh")] [InlineData("enum")] [InlineData("time")] [InlineData("order")]
    public void AlteredOriginalPoseGraphIsRejected(string change)
    {
        var root=JsonNode.Parse(Catalog().Read(AlsRefactoredRotatePlayers.Blueprint(false)).GetRawText())!;
        JsonNode Node(int id)=>root["compiled"]!["nodes"]!.AsArray().Single(n=>n!["propertyIndex"]!.GetValue<int>()==id)!;
        JsonNode Runtime(int id)=>Node(id)["runtime"]!;
        switch(change)
        {
            case "link":Runtime(42)["basePose"]!["linkId"]=22;break;
            case "curve":Runtime(43)["curveValues"]![0]=0;break;
            case "mask":Runtime(42)["perBoneBlendWeights"]![0]!["blendWeight"]=1;break;
            case "branch":Runtime(42)["layerSetup"]![0]!["branchFilters"]![0]!["boneName"]="spine_01";break;
            case "mesh":Runtime(42)["bMeshSpaceRotationBlend"]=false;break;
            case "enum":Runtime(33)["enumToPoseIndex"]![5]=0;break;
            case "time":Runtime(33)["blendTime"]![1]=0;break;
            case "order":Runtime(32)["poses"]![0]!["linkId"]=40;break;
        }
        using var doc=JsonDocument.Parse(root.ToJsonString());
        Assert.Throws<ArgumentException>(()=>AlsRefactoredStopPoseGraph.Compile(doc.RootElement,Data.Value.Resources,Data.Value.Evaluators));
    }
}
