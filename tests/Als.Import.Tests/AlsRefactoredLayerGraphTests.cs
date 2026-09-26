using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLayerGraphTests
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    private static AlsLayerBlendingDefinition Compile(string? inventory=null,string? inputs=null)=>
        AlsRefactoredLayerGraphCompiler.Compile(Read("layering_graphs"),inventory??Read("layering_inventory"),inputs??Read("base_pose_inputs"));
    [Fact]
    public void CompleteGraphUsesOriginalIdentitiesInputsAndVirtualBoneFilters()
    {
        var graph=Compile();Assert.Equal(94,graph.Nodes.Length);Assert.Equal(15,graph.RootIndex);
        var nodes=graph.Nodes.ToArray();Assert.Equal(4,nodes.Count(n=>n.Kind==AlsLayerPoseKind.Input));
        Assert.Equal(new[]{"ArmLeft","ArmRight","Curves","Head","Legs","Pelvis","Spine"},nodes.Where(n=>n.Kind==AlsLayerPoseKind.Slot).Select(n=>n.Label).Order());
        var multi=Assert.Single(nodes,n=>n.Kind==AlsLayerPoseKind.NormalizedMultiWayBlend);
        Assert.Equal(new[]{37,38},multi.Inputs);Assert.Equal(new[]{"PoseStanding","PoseCrouching"},multi.Alphas.Select(a=>a.Name));
        Assert.All(multi.Alphas,a=>Assert.Equal(AlsLayerAlphaKind.Curve,a.Kind));
        Assert.Equal(4,nodes.Where(n=>n.Filters is not null).SelectMany(n=>n.Filters!).SelectMany(f=>f).Select(f=>f.Bone).Where(b=>b.StartsWith("VB ",StringComparison.Ordinal)).Distinct().Count());
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void WholeGraphSamplesOriginalBasePosesAndRetriesWithCommittedCurveFeedback(bool overrideSlots)
    {
        var definition=Compile();var resources=AlsRefactoredBasePoseCompiler.Compile(Read("base_pose_inputs"),Read("layering_inventory"),Read("layering_graphs"));
        var basis=resources[37].Pose;
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(new[]{"PoseStanding","PoseCrouching"}).ToArray();
        var rest=basis.ReferencePose.ToArray().Select(FloatPose).ToArray();
        var runtime=new AlsLayerBlendingRuntime(definition,basis.BoneNames.ToArray(),basis.Parents.ToArray(),names,rest);
        var model=new AlsRefactoredLayeringInputModel(names);var sink=new Sink(resources,names){OverrideSlots=overrideSlots};
        var graph=default(AlsAnimationGraphFrame);var previous=default(AlsFrameIdentity);
        var curves=new AlsInertialCurve[names.Length];var pose=new AlsLocalPose[79];
        for(var frame=1;frame<=120;frame++)
        {
            var id=new AlsFrameIdentity(frame,1,1);graph=graph.Next(id,(ulong)frame);
            var feedback=frame==1?Array.Empty<AlsInertialCurve>():curves.ToArray();
            var input=model.Evaluate(id,previous,feedback);sink.Standing=frame%40<20;
            sink.Amount=frame%30<15?0:1;
            Prepare();runtime.Evaluate(pose,curves);var first=pose.ToArray();var firstCurves=curves.ToArray();runtime.Cancel();
            Prepare();runtime.Evaluate(pose,curves);Assert.Equal(first,pose);Assert.Equal(firstCurves,curves);runtime.Commit();
            Assert.True(curves[^2]==new AlsInertialCurve(sink.Standing?1:0),$"frame={frame} standing={curves[^2]}");Assert.Equal(new AlsInertialCurve(sink.Standing?0:1),curves[^1]);
            for(var i=0;i<names.Length;i++)if(names[i].EndsWith("Slot",StringComparison.Ordinal))Assert.Equal(new AlsInertialCurve(overrideSlots?.5f:0),curves[i]);
            previous=id;
            void Prepare()=>runtime.Prepare(new(id,1,1f/60),input,feedback,graph.Initialization,graph.Bones,graph.Evaluation,sink);
        }
        Assert.True(sink.BaseSamples>0);Assert.True(sink.SlotUpdates>0);
        if(overrideSlots)Assert.Equal(7,sink.VisitedSlots.Count);
    }
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void SharedMontageBankDrivesAllRegionSlotsThroughCompleteGraph(int hz)
    {
        var definition=Compile();
        var resources=AlsRefactoredBasePoseCompiler.Compile(Read("base_pose_inputs"),Read("layering_inventory"),Read("layering_graphs"));
        var basis=resources[37].Pose;
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(new[]{"PoseStanding","PoseCrouching"}).ToArray();
        var rest=basis.ReferencePose.ToArray();
        var runtime=new AlsLayerBlendingRuntime(definition,basis.BoneNames.ToArray(),basis.Parents.ToArray(),names,rest.Select(FloatPose).ToArray());
        var inputs=new Sink(resources,names){Standing=true,Amount=1};
        var slots=new AlsRefactoredLayerSlotSink(inputs,inputs,rest,basis.Parents.ToArray(),names.Length);
        var assets=Enumerable.Range(5,7).Select(i=>new AlsSequenceMontageAsset(i,new(i),i,2,0)).ToArray();
        var bank=new AlsMontageRuntime([],sequences:assets);
        var model=new AlsRefactoredLayeringInputModel(names);
        var history=new AlsInertialCurve[names.Length]; var pose=new AlsLocalPose[79];
        var graph=default(AlsAnimationGraphFrame);var previous=default(AlsFrameIdentity);
        ushort visited=0;var fullFrames=0;
        for(var f=1;f<=hz*3;f++)
        {
            var id=new AlsFrameIdentity(f,3,1);graph=graph.Next(id,(ulong)f);
            var feedback=f==1?Array.Empty<AlsInertialCurve>():history.ToArray();
            var input=model.Evaluate(id,previous,feedback);
            if(f==hz)
            {
                Prepare();inputs.FailMontageSample=true;
                Assert.Throws<InvalidOperationException>(()=>runtime.Evaluate(pose,history));
                inputs.FailMontageSample=false;runtime.Cancel();bank.Discard();
                Assert.Throws<InvalidOperationException>(()=>slots.RelevantSlots);slots.End();
            }
            Prepare();runtime.Evaluate(pose,history);
            var expected=pose.ToArray();var expectedCurves=history.ToArray();var mask=slots.RelevantSlots;
            var instances=bank.Candidate.ToArray();
            runtime.Cancel();slots.End();bank.Discard();
            Assert.Throws<InvalidOperationException>(()=>slots.RelevantSlots);
            Prepare();runtime.Evaluate(pose,history);
            Assert.Equal(expected,pose);Assert.Equal(expectedCurves,history);Assert.Equal(mask,slots.RelevantSlots);
            Assert.Equal(instances,bank.Candidate.ToArray());visited|=mask;
            if(bank.Frame.SlotWeights(AlsMontageSlot.Curves).SlotNodeWeight==1)
            {
                fullFrames++;
                for(var i=0;i<names.Length;i++)if(names[i].EndsWith("Slot",StringComparison.Ordinal))Assert.Equal(new AlsInertialCurve(.5f),history[i]);
            }
            runtime.Commit();slots.End();bank.Commit(id);previous=id;
            void Prepare()
            {
                bank.Begin(id,1f/hz);
                if(f==1)foreach(var asset in assets)Assert.True(bank.PlaySequence(new(asset.AnimationId,asset.Slot,1,0,.2f,.2f)));
                slots.Begin(bank.Frame,id);
                Assert.Throws<InvalidOperationException>(()=>slots.Begin(bank.Frame,id));
                Assert.Throws<ArgumentException>(()=>slots.GetSlotWeights(0,"Head",new(new(f,4,1),1,1f/hz)));
                if(f==2)
                {
                    var weights=bank.Frame.SlotWeights(AlsMontageSlot.Head);
                    slots.UpdateSlot(0,"Head",weights,default,new(id,0,1f/hz));
                    Assert.Equal(AlsMontageSlot.Head.Mask,slots.RelevantSlots);
                    slots.End();slots.Begin(bank.Frame,id);Assert.Equal((ushort)0,slots.RelevantSlots);
                }
                runtime.Prepare(new(id,1,1f/hz),input,feedback,graph.Initialization,graph.Bones,graph.Evaluation,slots);
            }
        }
        // Only the original seven regional slots belong to this graph. The
        // character-wide slot registry also contains unrelated action slots.
        var regionalMask = AlsMontageSlot.Head.Mask | AlsMontageSlot.ArmLeft.Mask | AlsMontageSlot.ArmRight.Mask |
            AlsMontageSlot.Spine.Mask | AlsMontageSlot.Pelvis.Mask | AlsMontageSlot.Legs.Mask | AlsMontageSlot.Curves.Mask;
        Assert.Equal((ushort)regionalMask,visited);
        Assert.True(fullFrames>hz);Assert.True(inputs.BaseSamples>0);Assert.True(inputs.MontageSamples>0);
        Assert.Equal(0,inputs.SlotUpdates);Assert.Empty(bank.Committed.ToArray());
    }
    [Theory]
    [InlineData("edge")][InlineData("scale")][InlineData("slot")][InlineData("slotName")][InlineData("rootspace")][InlineData("callback")][InlineData("mask")]
    public void UnsupportedOrMismatchedCompiledPoliciesAreRejected(string change)
    {
        var inventory=JsonNode.Parse(Read("layering_inventory"))!;var nodes=inventory["blueprints"]![2]!["nodes"]!.AsArray();
        JsonNode Node(string kind)=>nodes.First(n=>n!["class"]!.GetValue<string>()==kind&&n["compiledNodeIndex"]!.GetValue<int>()>=0)!;
        var item=Node(change switch {"edge"=>"AnimGraphNode_Root","scale"=>"AnimGraphNode_MultiWayBlend","slot" or "slotName"=>"AnimGraphNode_Slot",_=>"AnimGraphNode_LayeredBoneBlend"});
        if(change=="edge")item["runtime"]!["result"]!["linkId"]=item["propertyIndex"]!.GetValue<int>();
        else if(change=="mask")item["runtime"]!["perBoneBlendWeights"]![0]!["blendWeight"]=.5;
        else foreach(var state in new[]{item["runtime"]!,item["authoredProperties"]!["Node"]!})
        {
            if(change=="scale")state["alphaScaleBias"]!["scale"]=2;
            if(change=="slot")state["bAlwaysUpdateSourcePose"]=true;
            if(change=="slotName")state["slotName"]="UnknownRegion";
            if(change=="rootspace")state["bRootSpaceRotationBlend"]=true;
            if(change=="callback")state["updateFunction"]!["functionName"]="Other";
        }
        var json=inventory.ToJsonString();var inputs=JsonNode.Parse(Read("base_pose_inputs"))!;
        inputs["inventorySha256"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Throws<ArgumentException>(()=>Compile(json,inputs.ToJsonString()));
    }
    private static AlsLocalPose FloatPose(AlsPrecisePose pose)=>new(new((float)pose.Position.X,(float)pose.Position.Y,(float)pose.Position.Z),
        new((float)pose.Rotation.X,(float)pose.Rotation.Y,(float)pose.Rotation.Z,(float)pose.Rotation.W),new((float)pose.Scale.X,(float)pose.Scale.Y,(float)pose.Scale.Z));
    private sealed class Sink : IAlsLayerBlendingSink, IAlsMontagePoseSource
    {
        private readonly Dictionary<int,AlsLocalPose[]> _poses;
        private readonly string[] _names;
        public bool Standing,OverrideSlots,FailMontageSample;public float Amount;public int BaseSamples,SlotUpdates,MontageSamples;
        public HashSet<string> VisitedSlots {get;}=[];
        public Sink(IReadOnlyDictionary<int,AlsRefactoredBasePoseResource> resources,string[] names)
        {
            _names=names;_poses=resources.ToDictionary(p=>p.Key,p=>
            {var pose=new AlsPrecisePose[79];p.Value.CreateSampler().Evaluate(pose,[]);return pose.Select(FloatPose).ToArray();});
        }
        public void InitializeInput(int index,string name){}
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(FailMontageSample)throw new InvalidOperationException("Injected region sample failure.");
            MontageSamples++;curves.Clear();
            for(var i=0;i<pose.Length;i++)
            {
                var p=_poses[38][i];pose[i]=new(new(p.Position.X,p.Position.Y,p.Position.Z),
                    new(p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W),new(p.Scale.X,p.Scale.Y,p.Scale.Z));
            }
            for(var i=0;i<_names.Length;i++)if(_names[i].StartsWith("Layer",StringComparison.Ordinal))
                curves[i]=new(_names[i].EndsWith("Slot",StringComparison.Ordinal)?.5f:Amount);
        }
        public void CacheInputBones(int index,string name){}
        public void UpdateInput(int index,string name,in AlsPoseUpdateContext context){}
        public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            curves.Clear();
            if(_poses.TryGetValue(index,out var basis)){basis.CopyTo(pose);BaseSamples++;return;}
            _poses[name=="Overlay Input"?38:37].CopyTo(pose);
            if(name=="Overlay Input")
            {
                for(var i=0;i<_names.Length;i++)if(_names[i].StartsWith("Layer",StringComparison.Ordinal))curves[i]=new(Amount);
            }
            else {curves[^2]=new(Standing?1:0);curves[^1]=new(Standing?0:1);}
        }
        public void InitializeSlot(int index,string name){}
        public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>OverrideSlots?new(0,1,1):AlsSlotWeights.Passthrough;
        public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)
        {SlotUpdates++;VisitedSlots.Add(name);}
        public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool sourceEvaluated,ReadOnlySpan<AlsLocalPose> sourcePose,
            ReadOnlySpan<AlsInertialCurve> sourceCurves,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            Assert.True(OverrideSlots);Assert.False(sourceEvaluated);_poses[38].CopyTo(pose);curves.Clear();
            for(var i=0;i<_names.Length;i++)if(_names[i].StartsWith("Layer",StringComparison.Ordinal))
                curves[i]=new(_names[i].EndsWith("Slot",StringComparison.Ordinal)?.5f:Amount);
        }
        public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped){}
    }
}
