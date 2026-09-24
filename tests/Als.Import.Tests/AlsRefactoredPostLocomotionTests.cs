using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPostLocomotionTests
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void ActualMantlesDrivePostLocomotionLayeringAndHeadOnOneFrozenBank(int hz)
    {
        var native=AlsMantlingMontageCompiler.Compile(Read("mantle_animation_inputs"),Read("mantle_root_tracks"),Read("mantle_curves"));
        var mapped=MantlingHostFixture.Bind(native);var profile=mapped.Profile;
        var notifyBinding=mapped.BindNotifies(Read("mantle_animation_inputs"),MantlingHostFixture.Notifies());
        var owned=AlsMantlingCurveCompiler.CompileMontages(Read("mantle_montage_curves"),Read("mantle_animation_inputs"));
        var postProfile=AlsRefactoredPostLayerCompiler.Compile(Read("layering_graphs"),Read("layering_inventory"),Read("base_pose_inputs"),Read("head_inputs"));
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray().Concat(new[]{"PoseStanding","PoseCrouching","PoseGrounded"})
            .Concat(owned.Values.SelectMany(s=>s.Names.ToArray())).Distinct(StringComparer.Ordinal).ToArray();
        var layout=postProfile.BasePoses[37].Pose;
        var sampling=AlsMantlingHostPoseProfile.ForNativeSkeleton(profile,names,owned);
        foreach(var p in profile.Poses.Values){Assert.Equal(layout.BoneNames.ToArray(),p.BoneNames.ToArray());Assert.Equal(layout.Parents.ToArray(),p.Parents.ToArray());}
        foreach(var definition in profile.Definitions.Values)
        {
            var inputs=new Inputs(postProfile,names);var sampler=new FaultSampler(sampling.CreatePoseSource());var bank=mapped.CreateRuntime();
            var post=new AlsRefactoredPostLocomotionSink(inputs,sampler,layout.ReferencePose,layout.Parents,names.Length);
            var regions=new AlsRefactoredLayerSlotSink(post,sampler,layout.ReferencePose,layout.Parents,names.Length);
            var graphRuntime=postProfile.CreateRuntime(4,1,names);var graph=default(AlsAnimationGraphFrame);
            var queue=new AlsMontageNotifyRuntime(notifyBinding.Binding);
            var full=0;var partial=0;var passed=0;var footsteps=0;
            for(var frame=1;frame<=hz*3;frame++)
            {
                var id=new AlsFrameIdentity(frame,4,1);graph=graph.Next(id,(ulong)frame);
                if(frame==5)graph=graph with {Initialization=graph.Initialization.Next((ulong)frame)};
                var input=new AlsRefactoredViewInput(frame*.3,20,0,0,0,0,0,0,false,false,AlsRotationMode.LookingDirection,false,frame==1,false,0,1f/hz,1f/hz,0,0);
                bank.Begin(id,1f/hz);
                if(frame==2){bank.PlayAction(mapped.Actions[0].ActionDefinitionId,1);bank.PlayAction(definition.Asset.ActionDefinitionId,1);}
                var weights=bank.SlotWeights(AlsMontageSlot.PostLocomotion);
                if(weights.SourceWeight==0)full++;else if(weights.SlotNodeWeight>0)partial++;else passed++;
                post.Begin(bank.Frame,id);regions.Begin(bank.Frame,id);inputs.ResetCounts();
                if(frame==4)
                {
                    var committed=graphRuntime.CommittedHead;var postIdentity=post.CommittedIdentity;
                    sampler.Fail=true;graphRuntime.Prepare(graph,input,regions);
                    Assert.Throws<InvalidOperationException>(()=>graphRuntime.Evaluate());
                    Assert.Throws<InvalidOperationException>(()=>post.ValidateCommit(id));
                    Assert.Equal(committed,graphRuntime.CommittedHead);Assert.Equal(postIdentity,post.CommittedIdentity);
                    sampler.Fail=false;regions.End();post.Cancel();inputs.ResetCounts();
                    post.Begin(bank.Frame,id);regions.Begin(bank.Frame,id);
                }
                graphRuntime.Prepare(graph,input,regions);graphRuntime.Evaluate();
                if(frame==3)Assert.False(inputs.LastLocomotionContext.IsActive);
                if(frame==5)Assert.True(inputs.LastLocomotionContext.IsActive);
                Assert.Equal(weights.SourceWeight>AlsPoseBlender.WeightThreshold?1:0,inputs.LocomotionUpdates);
                Assert.Equal(weights.SourceWeight>AlsPoseBlender.WeightThreshold?1:0,inputs.LocomotionEvaluations);
                var relevance=(ushort)(post.RelevantSlots|regions.RelevantSlots);
                Assert.Equal(weights.SlotNodeWeight>AlsPoseBlender.WeightThreshold?AlsMontageSlot.PostLocomotion.Mask:(ushort)0,relevance);
                queue.Begin(id,bank.NotifyTraversal);queue.Complete(relevance);
                var notifies=queue.Notifies.ToArray();var direct=queue.DirectNotifies.ToArray();var notifyState=queue.Candidate;
                var pose=graphRuntime.Pose.ToArray();var curves=graphRuntime.Curves.ToArray();var head=graphRuntime.CandidateHead;
                // Discard graph/source relevance candidates while retaining this
                // immutable bank view. Retry does not advance any playback clock.
                graphRuntime.Cancel();regions.End();post.Cancel();queue.Discard();inputs.ResetCounts();
                post.Begin(bank.Frame,id);regions.Begin(bank.Frame,id);graphRuntime.Prepare(graph,input,regions);graphRuntime.Evaluate();
                Assert.Equal(pose,graphRuntime.Pose.ToArray());Assert.Equal(curves,graphRuntime.Curves.ToArray());Assert.Equal(head,graphRuntime.CandidateHead);
                queue.Begin(id,bank.NotifyTraversal);queue.Complete((ushort)(post.RelevantSlots|regions.RelevantSlots));
                Assert.Equal(notifies,queue.Notifies.ToArray());Assert.Equal(direct,queue.DirectNotifies.ToArray());Assert.Equal(notifyState,queue.Candidate);
                foreach(var notify in queue.Notifies)
                {Assert.True(notifyBinding.Binding.TryTimeline(notify.Reference,out var timeline));if(notifyBinding.Footsteps.ContainsKey(timeline.EventId))footsteps++;}
                graphRuntime.ValidateCommit(id);post.ValidateCommit(id);bank.ValidateCommit(id);queue.ValidateCommit(id);
                graphRuntime.Commit(id);post.Commit(id);regions.End();bank.Commit(id);queue.Commit(id);
            }
            Assert.True(full>0);Assert.True(partial>0);Assert.True(passed>0);Assert.True(footsteps>0);
        }
    }
    private sealed class FaultSampler(IAlsMontagePoseSource inner):IAlsMontagePoseSource
    {
        public bool Fail;
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {inner.Sample(entry,pose,curves);if(Fail)throw new InvalidOperationException("Injected native Montage sample failure.");}
    }

    [Fact]
    public void NativeSamplerPreservesAllLogicalBonesAndMontageOwnedCurves()
    {
        var profile=AlsMantlingMontageCompiler.Compile(Read("mantle_animation_inputs"),Read("mantle_root_tracks"),Read("mantle_curves"));
        var owned=AlsMantlingCurveCompiler.CompileMontages(Read("mantle_montage_curves"),Read("mantle_animation_inputs"));
        var names=profile.Curves.Values.First().Names.ToArray().Concat(owned.Values.SelectMany(c=>c.Names.ToArray())).Append("Absent").Distinct().ToArray();
        var sampler=AlsMantlingHostPoseProfile.ForNativeSkeleton(profile,names,owned).CreatePoseSource();var original=profile.CreatePoseSource();
        var pose=new AlsPrecisePose[79];var expected=new AlsPrecisePose[79];var curves=new AlsInertialCurve[names.Length];var originalCurves=new AlsInertialCurve[2];
        foreach(var definition in profile.Definitions.Values)
        for(var step=0;step<=30;step++)
        {
            var a=definition.Asset;var entry=new AlsMontageEvaluation(1,a.AnimationId,a.Slot,a.Duration*step/30,1,a.ActionDefinitionId);
            sampler.Sample(entry,pose,curves);original.Sample(entry,expected,originalCurves);Assert.Equal(expected,pose);
            var own=owned[definition.Path];var values=new AlsInertialCurve[own.Names.Length];own.Sample(entry.Position,values);
            for(var i=0;i<values.Length;i++)Assert.Equal(values[i],curves[Array.IndexOf(names,own.Names[i])]);
            Assert.False(curves[^1].Present);
        }
        Assert.Throws<ArgumentNullException>(()=>AlsMantlingHostPoseProfile.ForNativeSkeleton(profile,names,null!));
    }
    private sealed class Inputs:IAlsLayerBlendingSink
    {
        private readonly Dictionary<int,AlsLocalPose[]> _poses;private readonly string[] _names;
        public int LocomotionUpdates,LocomotionEvaluations;
        public AlsPoseUpdateContext LastLocomotionContext;
        public Inputs(AlsRefactoredPostLayerProfile profile,string[] names)
        {
            _names=names;_poses=profile.BasePoses.ToDictionary(p=>p.Key,p=>{var pose=new AlsPrecisePose[79];p.Value.CreateSampler().Evaluate(pose,[]);
                return pose.Select(v=>new AlsLocalPose(new((float)v.Position.X,(float)v.Position.Y,(float)v.Position.Z),
                    new((float)v.Rotation.X,(float)v.Rotation.Y,(float)v.Rotation.Z,(float)v.Rotation.W),new((float)v.Scale.X,(float)v.Scale.Y,(float)v.Scale.Z))).ToArray();});
        }
        public void ResetCounts(){LocomotionUpdates=LocomotionEvaluations=0;}
        public void InitializeInput(int index,string name){}
        public void CacheInputBones(int index,string name){}
        public void UpdateInput(int index,string name,in AlsPoseUpdateContext context)
        {if(name=="Locomotion Input"){LocomotionUpdates++;LastLocomotionContext=context;}}
        public void EvaluateInput(int index,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
        {
            curves.Clear();if(_poses.TryGetValue(index,out var p)){p.CopyTo(pose);return;}
            _poses[37].CopyTo(pose);if(name!="Locomotion Input")return;
            LocomotionEvaluations++;curves[Array.IndexOf(_names,"PoseStanding")]=new(1);curves[Array.IndexOf(_names,"ViewBlock")]=new(0);
        }
        public void InitializeSlot(int index,string name){}
        public AlsSlotWeights GetSlotWeights(int index,string name,in AlsPoseUpdateContext context)=>throw new InvalidOperationException("Regional Slot decorator required.");
        public void UpdateSlot(int index,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)=>throw new InvalidOperationException();
        public void EvaluateSlot(int index,string name,in AlsSlotWeights weights,bool evaluated,ReadOnlySpan<AlsLocalPose> pose,ReadOnlySpan<AlsInertialCurve> curves,
            Span<AlsLocalPose> output,Span<AlsInertialCurve> result)=>throw new InvalidOperationException();
        public void OnCachedUpdatesSkipped(int index,ReadOnlySpan<AlsPoseUpdateContext> contexts){}
    }
}
