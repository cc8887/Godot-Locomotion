using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Events;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

internal static class MantlingHostFixture
{
    internal static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config",name+".json"));
    internal static (AlsAnimationSetDefinition Set,AlsAuthoredMontageAsset[] Actions,AlsDynamicMontageAsset[] Turns,AlsSequenceMontageAsset[] Sequences) Load()
    {
        var set=P3RepositoryFixtures.LoadAnimationSet();
        var locomotion=AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile"),set);
        var pose=AlsPoseProfileCompiler.Compile(Read("p4_pose_profile"),set,locomotion);
        var actionProfile=AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime"),Read("p5_get_up_actions"),set);
        var actions=AlsAuthoredMontageCompiler.Compile(Read("v4_recovery_action_montage_inputs"),Read("v4_turn_montage_inputs"),set,actionProfile,pose.SkeletonId);
        var turns=AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs"),set,pose);
        var sequences=set.Animations.Where(a=>a.Name is "ALS_N_Transition_L" or "ALS_N_Transition_R")
            .Select(a=>new AlsSequenceMontageAsset(a.Id,AlsMontageSlot.Grounded,1,a.PlayLength,a.AdditiveType)).ToArray();
        return(set,actions,turns,sequences);
    }
    internal static AlsMantlingHostResources Bind(AlsMantlingMontageProfile profile)
    {var host=Load();return new(profile,host.Set,host.Actions,host.Turns,host.Sequences);}
    internal static AlsMontageNotifyBinding Notifies()
        =>EventBindings().Notifies;
    internal static (AlsP5CoreRuntimeBindingSnapshot Core,AlsMontageNotifyBinding Notifies) EventBindings()
    {
        var host=Load();var set=host.Set;
        var locomotion=AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile"),set);
        var pose=AlsPoseProfileCompiler.Compile(Read("p4_pose_profile"),set,locomotion);
        var actions=AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime"),Read("p5_get_up_actions"),set);
        var sources=AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph"),set,locomotion.SkeletonId);
        var inventory=AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory"),set,sources);
        var layout=AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion,pose,actions,sources,inventory);
        var binding=AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set,locomotion,pose,actions,layout,sources,inventory);
        return(binding,AlsMontageNotifyCompiler.Compile(Read("v4_recovery_action_notify_inputs"),Read("v4_turn_notify_inputs"),set,sources,binding,host.Turns,host.Actions));
    }
}

public sealed class AlsMantlingHostResourceTests
{
    private static AlsMantlingMontageProfile Source()=>AlsMantlingMontageCompiler.Compile(MantlingHostFixture.Read("refactored_mantle_animation_inputs"),
        MantlingHostFixture.Read("refactored_mantle_root_tracks"),MantlingHostFixture.Read("refactored_mantle_curves"));
    [Fact]
    public void RealRecoveryInventoryAndMantleHaveDisjointStableResourceIds()
    {
        var source=Source();var host=MantlingHostFixture.Load();var mapped=new AlsMantlingHostResources(source,host.Set,host.Actions,host.Turns,host.Sequences);
        Assert.Equal(9,host.Actions.Length);Assert.Equal(15,mapped.Actions.Length);Assert.Equal(3,mapped.AnimationIds.Count);Assert.Equal(6,mapped.ActionIds.Count);
        Assert.All(mapped.AnimationIds.Values,id=>Assert.True(id>host.Set.Animations.Max(a=>a.Id)));
        Assert.All(mapped.MontageIds.Values,id=>Assert.True(id>host.Set.Montages.Max(a=>a.Id)));
        Assert.All(mapped.ActionIds.Values,id=>Assert.True(id>host.Actions.Max(a=>a.ActionDefinitionId)));
        Assert.DoesNotContain(mapped.GroupId,host.Actions.Select(a=>a.GroupId).Concat(host.Turns.Select(t=>t.GroupId)).Concat(host.Sequences.Select(s=>s.GroupId)));
        var repeated=new AlsMantlingHostResources(source,host.Set,host.Actions.Reverse().ToArray(),host.Turns.Reverse().ToArray(),host.Sequences.Reverse().ToArray());
        foreach(var original in source.Definitions.Values)
        {
            var asset=mapped.Profile.Definitions[original.Path].Asset;
            Assert.Equal(asset,repeated.Profile.Definitions[original.Path].Asset);
            Assert.Equal(original.Asset,asset with {AnimationId=original.Asset.AnimationId,ActionDefinitionId=original.Asset.ActionDefinitionId,MontageId=original.Asset.MontageId,GroupId=original.Asset.GroupId});
            Assert.Same(source.Poses[original.SequencePath],mapped.Profile.Poses[original.SequencePath]);
        }
        Assert.Equal(host.Actions,mapped.Actions[..host.Actions.Length].ToArray());
        host.Actions[0]=host.Actions[0] with {Duration=999};Assert.NotEqual(999,mapped.Actions[0].Duration);
    }
    [Fact]
    public void MappedPoseAndCurveSamplingUsesHostIdsButPreservesOriginalValues()
    {
        var source=Source();var mapped=MantlingHostFixture.Bind(source);var original=source.CreatePoseSource();var target=mapped.Profile.CreatePoseSource();
        var a=new AlsPrecisePose[79];var b=new AlsPrecisePose[79];var ca=new AlsInertialCurve[2];var cb=new AlsInertialCurve[2];
        foreach(var definition in source.Definitions.Values)
        for(var step=0;step<=20;step++)
        {
            var asset=definition.Asset;var mappedAsset=mapped.Profile.Definitions[definition.Path].Asset;
            var entry=new AlsMontageEvaluation(1,asset.AnimationId,asset.Slot,asset.Duration*step/20,1,asset.ActionDefinitionId);
            original.Sample(entry,a,ca);target.Sample(entry with {AnimationId=mappedAsset.AnimationId,ActionDefinitionId=mappedAsset.ActionDefinitionId},b,cb);
            Assert.Equal(a,b);Assert.Equal(ca,cb);
        }
        var old=source.Definitions.Values.First().Asset;
        Assert.Throws<ArgumentException>(()=>target.Sample(new(1,old.AnimationId,old.Slot,0,1,old.ActionDefinitionId),b,cb));
    }
    [Fact]
    public void OneBankKeepsHostPlaybackAndRootOwnerWhileMantleReplacesOnlyItsOwnGroup()
    {
        var source=Source();var host=MantlingHostFixture.Load();var mapped=new AlsMantlingHostResources(source,host.Set,host.Actions,host.Turns,host.Sequences);
        var branch=AlsMantlingBranchCompiler.Compile(MantlingHostFixture.Read("refactored_mantle_animation_inputs"),mapped.Profile);
        var bank=mapped.CreateRuntime(branch);var mantle=mapped.Profile.Definitions.Values.ToArray();var roll=host.Actions[0];
        bank.Begin(new(1,1,1),.05f);bank.PlayAction(roll.ActionDefinitionId,1);bank.PlayAction(mantle[0].Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),.05f);Assert.Equal(2,bank.Candidate.Length);Assert.Equal(1,bank.RootMotionRange.InstanceId);
        Assert.All(bank.Candidate.ToArray(),i=>Assert.False(i.Interrupted));Assert.Equal("Als.LocomotionAction.Mantling",branch.CandidateAction);
        bank.PlayAction(mantle[1].Asset.ActionDefinitionId,1);Assert.Equal(3,bank.Candidate.Length);
        Assert.False(bank.Candidate[0].Interrupted);Assert.True(bank.Candidate[1].Interrupted);Assert.False(bank.Candidate[2].Interrupted);
        bank.Discard();Assert.Equal(2,bank.Committed.Length);
        bank.Begin(new(2,1,1),.05f);Assert.All(bank.Candidate.ToArray(),i=>Assert.False(i.Interrupted));bank.Commit(new(2,1,1));
        bank.Begin(new(3,1,1),.05f,true);Assert.All(bank.Candidate.ToArray(),i=>Assert.True(i.Interrupted));Assert.False(bank.RootMotionRange.HasMotion);bank.Commit(new(3,1,1));
        for(var frame=4;frame<20;frame++){bank.Begin(new(frame,1,1),.05f);bank.Commit(new(frame,1,1));}
        Assert.Empty(bank.Committed.ToArray());Assert.Equal("",branch.CommittedAction);
    }
    [Fact]
    public void UnknownHostResourcesAreRejectedBeforeComposition()
    {
        var source=Source();var host=MantlingHostFixture.Load();
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostResources(source,host.Set,[host.Actions[0] with {AnimationId=int.MaxValue}],host.Turns));
        Assert.Throws<ArgumentException>(()=>new AlsMantlingHostResources(source,host.Set,[host.Actions[0] with {MontageId=int.MaxValue}],host.Turns));
    }
    [Fact]
    public void CombinedNotifyNamespacePreservesHostPoliciesAndDispatchesBothGroups()
    {
        var source=Source();var mapped=MantlingHostFixture.Bind(source);var host=MantlingHostFixture.Notifies();
        var result=mapped.BindNotifies(MantlingHostFixture.Read("refactored_mantle_animation_inputs"),host);
        Assert.Equal(host.SourcePolicyCount,result.Binding.SourcePolicyCount);
        Assert.Equal(host.Policies.ToArray(),result.Binding.Policies[..host.Policies.Length].ToArray());
        Assert.Equal(host.Definitions.ToArray(),result.Binding.Definitions[..host.Definitions.Length].ToArray());
        Assert.Equal(host.Ranges.ToArray(),result.Binding.Ranges[..host.Ranges.Length].ToArray());
        Assert.All(result.Footsteps.Keys,id=>Assert.True(id>host.Policies.ToArray().Max(p=>p.EventId)));
        foreach(var range in host.Ranges)
        for(var i=0;i<range.Count;i++)
        {
            var reference=new AlsAssetNotifyReference(host.SourcePolicyCount+range.Offset+i,range.Handle,0,true,false);
            Assert.True(host.TryTimeline(reference,out var expected));Assert.True(result.Binding.TryTimeline(reference,out var actual));Assert.Equal(expected,actual);
        }
        var bank=mapped.CreateRuntime();var queue=new AlsMontageNotifyRuntime(result.Binding);
        bank.Begin(new(1,1,1),.01f);bank.PlayAction(mapped.Actions[0].ActionDefinitionId,1);bank.PlayAction(mapped.Profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),.8f);queue.Begin(new(2,1,1),bank.NotifyTraversal);queue.Complete(20);
        Assert.Contains(queue.DirectNotifies.ToArray(),n=>n.PlaybackEpoch==1);
        Assert.Contains(queue.Notifies.ToArray(),n=>n.PlaybackEpoch==2);
        foreach(var notify in queue.Notifies)
        {
            Assert.True(result.Binding.TryTimeline(notify.Reference,out var entry));
            if(notify.PlaybackEpoch==2)Assert.Contains(entry.EventId,result.Footsteps.Keys);
            else Assert.DoesNotContain(entry.EventId,result.Footsteps.Keys);
        }
    }
}
