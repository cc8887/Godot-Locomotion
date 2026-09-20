using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMontageNotifyCompilerTests
{
    private static readonly Lazy<Fixture> Data=new(Fixture.Create);

    [Fact]
    public void ActionPlaybackUsesTheSequenceOccurrenceFromTheCurrentNotifyLayout()
    {
        var f = Data.Value; var original = f.Compile(Read("v4_action_notify_inputs.json"));
        var remappedRanges = original.Ranges.ToArray().Select(r => r with { Handle = r.Handle + 1000 }).ToArray();
        var timelines = new List<AlsTimelineEventDefinition>();
        foreach (var range in original.Ranges)
            for (var i = 0; i < range.Count; i++)
            {
                Assert.True(original.TryTimeline(new(original.SourcePolicyCount + range.Offset + i, range.Handle, 0, true, false), out var entry));
                timelines.Add(entry with { RequiredOccurrenceHandleId = range.Handle + 1000 });
            }
        var remapped = new AlsMontageNotifyBinding(original.Policies[..original.SourcePolicyCount],
            original.Policies[original.SourcePolicyCount..], original.Definitions, timelines.ToArray(), remappedRanges);
        var roll = Assert.Single(f.Actions); var section = Assert.Single(f.Set.Montages[roll.MontageId].Sections);
        var bank = new AlsMontageRuntime(f.Turns, f.Actions);
        var actions = new AlsMontageActionRuntime(bank, [new(roll.ActionDefinitionId, section.SectionId, 0, 1, .2f, true)]);
        var id = new AlsFrameIdentity(1, 1, 1); actions.Begin(id, .05f);
        actions.ApplyRequest(new(1, AlsActionCommand.Start, roll.ActionDefinitionId, section.SectionId, 100, 1)); actions.Complete();
        var before = AlsMontageActionPlaybackCompiler.Compile(f.Set, f.Actions, original).ReadOwned(actions, id, roll.Slot);
        var after = AlsMontageActionPlaybackCompiler.Compile(f.Set, f.Actions, remapped).ReadOwned(actions, id, roll.Slot);
        var sequence = original.Ranges.ToArray().Single(r => !r.Direct && r.ActionDefinitionId == roll.ActionDefinitionId);
        Assert.Equal(sequence.Handle, before.OccurrenceHandleId);
        Assert.Equal(before with { OccurrenceHandleId = before.OccurrenceHandleId + 1000 }, after);
        Assert.Equal(section.SectionId, after.SectionId);
        Assert.Equal(f.Set.Montages[roll.MontageId].Slots[0].Segments[0].SegmentId, after.SegmentId);
    }

    [Fact]
    public void ActionPlaybackRejectsMissingNotifyBindingOrStalePhysicalMapping()
    {
        var f = Data.Value; var binding = f.Compile(Read("v4_action_notify_inputs.json")); var roll = Assert.Single(f.Actions);
        Assert.Throws<ArgumentException>(() => AlsMontageActionPlaybackCompiler.Compile(f.Set, f.Actions, new([], [], [], [], [])));
        Assert.Throws<ArgumentException>(() => AlsMontageActionPlaybackCompiler.Compile(f.Set, [roll with { ClipStart = roll.ClipStart + .01f }], binding));
    }

    [Fact]
    public void StopThenOverlayPreservesRepeatedInstancesAndSameGroupOrderOnRetry()
    {
        var f = Data.Value;
        var stop = AlsStopTransitionCompiler.Compile(Read("v4_overlay_transition_inputs.json"), f.Set,
            AlsGroundedMachineCompiler.CompileGrounded(Read("v4_grounded_dependencies.json")));
        var assets = AlsStopTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), f.Set, stop);
        var owner = new AlsMontageRuntime(f.Turns, f.Actions, f.Transitions.Concat(assets).ToArray());
        var left = stop.Bindings[0]; var right = stop.Bindings[1]; var overlay = f.Transitions[0];
        var first = new AlsFrameIdentity(1, 11, 2); owner.Begin(first, 1f / 60);
        Assert.True(owner.PlaySequence(new(overlay.AnimationId, overlay.Slot, 1.5f, .3f, .2f, .2f)));
        owner.Commit(first); var committed = owner.Committed.ToArray();
        var id = new AlsFrameIdentity(2, 11, 2); Prepare();
        var candidate = owner.Candidate.ToArray(); var frozen = owner.Evaluation.ToArray();
        Assert.Equal(5, candidate.Length); Assert.Equal(5, candidate.Select(i => i.InstanceId).Distinct().Count());
        Assert.All(candidate[..4], i => Assert.True(i.Interrupted));
        Assert.False(candidate[4].Interrupted); Assert.Equal(overlay.AnimationId, candidate[4].AnimationId);
        owner.Discard(); Assert.Equal(committed, owner.Committed.ToArray()); Prepare();
        Assert.Equal(candidate, owner.Candidate.ToArray()); Assert.Equal(frozen, owner.Evaluation.ToArray());
        owner.Commit(id);
        void Prepare()
        {
            owner.Begin(id, 1f / 60); var before = owner.Evaluation.ToArray();
            foreach (var binding in new[] { left, left, right })
                Assert.True(owner.PlaySequence(new(binding.AnimationId, stop.Slot, binding.PlayRate, binding.StartTime,
                    binding.BlendIn, binding.BlendOut)));
            Assert.True(owner.PlaySequence(new(overlay.AnimationId, overlay.Slot, 1.5f, .3f, .2f, .2f)));
            Assert.Equal(before, owner.Evaluation.ToArray());
        }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void StopMontagesUseFormalPayloadsAndDeferredClockWithExactRetry(int hz)
    {
        var f = Data.Value;
        var stop = AlsStopTransitionCompiler.Compile(Read("v4_overlay_transition_inputs.json"), f.Set,
            AlsGroundedMachineCompiler.CompileGrounded(Read("v4_grounded_dependencies.json")));
        var assets = AlsStopTransitionCompiler.CompileAssets(Read("v4_turn_montage_inputs.json"), f.Set, stop);
        var dynamicAssets = f.Transitions.Concat(assets).ToArray();
        var binding = AlsMontageNotifyCompiler.Compile(Read("v4_action_notify_inputs.json"), Read("v4_turn_notify_inputs.json"),
            f.Set, f.Sources, f.Binding, f.Turns, f.Actions, dynamicAssets,
            AlsStopTransitionCompiler.MergeNotifyMetadata(Read("v4_transition_notify_inputs.json"), Read("v4_stop_notify_inputs.json")));
        Assert.Equal(14, binding.Ranges.Length); Assert.Equal(38, binding.Definitions.Length);
        foreach (var command in stop.Bindings)
        {
            var owner = new AlsMontageRuntime(f.Turns, f.Actions, dynamicAssets);
            var notify = new AlsMontageNotifyRuntime(binding); var committed = default(AlsP5SourceEventState);
            var observed = new List<int>(); var delta = 1f / hz;
            for (var frame = 1; frame <= hz * 2; frame++)
            {
                var id = new AlsFrameIdentity(frame, 11, 2); Prepare();
                var instances = owner.Candidate.ToArray(); var frozen = owner.Evaluation.ToArray();
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(), id, delta, [], 0, committed,
                    out var next, out var expected, out var failure, montageBinding: binding, montageNotifies: notify.Notifies), failure.ToString());
                owner.Discard(); notify.Discard(); Prepare();
                Assert.Equal(instances, owner.Candidate.ToArray()); Assert.Equal(frozen, owner.Evaluation.ToArray());
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(), id, delta, [], 0, committed,
                    out var retry, out var events, out failure, montageBinding: binding, montageNotifies: notify.Notifies), failure.ToString());
                Assert.Equal(next, retry); Assert.Equal(expected.Count, events.Count);
                for (var i = 0; i < events.Count; i++)
                {
                    Assert.Equal(expected[i], events[i]); Assert.Equal(command.AnimationId, events[i].SourceAnimationId);
                    // The current authoritative manifest intentionally binds these
                    // Blueprint notifies as Generic; audio behavior is deferred.
                    Assert.Equal(AlsTimelineEventKind.Generic, events[i].Kind);
                    Assert.Equal(0, events[i].Payload.SemanticId); observed.Add(events[i].EventId);
                }
                owner.Commit(id); notify.Commit(id); committed = next;
                void Prepare()
                {
                    owner.Begin(id, delta); notify.Begin(id, owner.Traversal); notify.Complete(8);
                    if (frame == 1)
                    {
                        var before = owner.Evaluation.ToArray();
                        Assert.True(owner.PlaySequence(new(command.AnimationId, stop.Slot, command.PlayRate, command.StartTime,
                            command.BlendIn, command.BlendOut)));
                        Assert.Equal(before, owner.Evaluation.ToArray());
                        Assert.Equal(.4f, owner.Candidate[0].Position);
                    }
                }
            }
            Assert.Equal(f.Set.Animations[command.AnimationId].Timeline.Select(t => t.EventId), observed);
        }
    }

    [Fact]
    public void AdditiveTransitionsAppendTheirFourExactNotifyBindings()
    {
        var f = Data.Value; var original = f.Compile(Read("v4_action_notify_inputs.json")); var combined = f.CompileTransitions(Read("v4_transition_notify_inputs.json"));
        Assert.Equal(12, combined.Ranges.Length); Assert.Equal(34, combined.Definitions.Length);
        Assert.True(combined.Ranges[..original.Ranges.Length].SequenceEqual(original.Ranges));
        Assert.True(combined.Policies[..original.Policies.Length].SequenceEqual(original.Policies));
        foreach (var range in combined.Ranges[original.Ranges.Length..])
        {
            Assert.Equal(-1, range.ActionDefinitionId); Assert.Equal(AlsMontageSlot.Grounded, range.Slot); Assert.False(range.Direct); Assert.Equal(2, range.Count);
            Assert.Equal(.7954649925231934f, combined.Definitions[range.Offset].TriggerTimeSeconds);
            Assert.Equal(1.3316349983215332f, combined.Definitions[range.Offset + 1].TriggerTimeSeconds);
            Assert.Equal(.3f, combined.Policies[combined.SourcePolicyCount + range.Offset].WeightThreshold);
            Assert.True(combined.TryTimeline(new(combined.SourcePolicyCount + range.Offset, range.Handle, 0, true, false), out var entry));
            Assert.Equal(AlsTimelineSourceKind.MontageSegmentAnimation, entry.SourceKind); Assert.Equal(range.AnimationId, entry.SourceAnimationId);
        }
    }

    [Theory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    public void GroundedTransitionEventsFollowPhysicalInstanceAndRetry(int hz, bool hidden)
    {
        var f = Data.Value; var binding = f.CompileTransitions(Read("v4_transition_notify_inputs.json"));
        foreach (var asset in f.Transitions)
        {
            var owner = new AlsMontageRuntime(f.Turns, f.Actions, f.Transitions); var notify = new AlsMontageNotifyRuntime(binding);
            var current = default(AlsP5SourceEventState); var triggers = new List<int>(); var delta = 1f / hz;
            for (var frame = 1; frame <= hz * 3; frame++)
            {
                var id = new AlsFrameIdentity(frame, 11, 2); Prepare();
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(), id, delta, [], 0, current,
                    out var candidate, out var events, out var failure, montageBinding: binding, montageNotifies: notify.Notifies), failure.ToString());
                var saved = events; var notifyState = notify.Candidate; owner.Discard(); notify.Discard(); Prepare();
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(), id, delta, [], 0, current,
                    out var retry, out events, out failure, montageBinding: binding, montageNotifies: notify.Notifies), failure.ToString());
                Assert.Equal(candidate.NextInstanceId, retry.NextInstanceId); Assert.Equal(notifyState, notify.Candidate); Assert.Equal(saved.Count, events.Count);
                for (var e = 0; e < events.Count; e++)
                {
                    Assert.Equal(saved[e], events[e]); Assert.Equal(asset.AnimationId, events[e].SourceAnimationId);
                    Assert.Equal(AlsAnimationEventPhase.Trigger, events[e].Phase); triggers.Add(events[e].EventId);
                }
                owner.ValidateCommit(id); notify.ValidateCommit(id); owner.Commit(id); notify.Commit(id); current = candidate;
                void Prepare()
                {
                    owner.Begin(id, delta); notify.Begin(id, owner.Traversal);
                    if (frame == 1) Assert.True(owner.PlaySequence(new(asset.AnimationId, asset.Slot, 1.75f, .3f, .2f, .2f)));
                    notify.Complete(hidden ? (byte)0 : (byte)8);
                }
            }
            Assert.Equal(hidden ? [] : f.Set.Animations[asset.AnimationId].Timeline.Select(t => t.EventId).ToArray(), triggers);
        }
    }

    [Theory]
    [InlineData("rate")] [InlineData("length")] [InlineData("missing")]
    public void RejectsStaleTransitionPlaybackAndNotifyMetadata(string mutation)
    {
        var data = JsonNode.Parse(Read("v4_transition_notify_inputs.json"))!;
        if (mutation == "missing") data["syncAssets"]!.AsArray().RemoveAt(0);
        else data["syncAssets"]![0]![mutation == "rate" ? "rateScale" : "length"] = 2;
        Assert.Throws<ArgumentException>(() => Data.Value.CompileTransitions(data.ToJsonString()));
    }

    [Fact]
    public void DefaultRollHasOneMovementActionStateAndThreeSequenceNotifies()
    {
        var f=Data.Value; var binding=f.Compile(Read("v4_action_notify_inputs.json"));
        Assert.Equal(10,binding.Ranges.Length); Assert.Equal(30,binding.Definitions.Length);
        Assert.Equal(f.Binding.CreateCoreView().Sources.NotifyPolicies.ToArray(),binding.Policies[..binding.SourcePolicyCount].ToArray());
        var state=Assert.Single(binding.Ranges.ToArray(),r=>r.Direct); Assert.Equal(1,state.Count);
        var sequence=Assert.Single(binding.Ranges.ToArray(),r=>r.ActionDefinitionId>=0 && !r.Direct); Assert.Equal(3,sequence.Count);
        var index=binding.SourcePolicyCount+state.Offset;
        Assert.True(binding.Policies[index].StateObjectId>=0); Assert.Equal(0,binding.Policies[index].StateBehaviorFlags);
        Assert.True(binding.TryTimeline(new(index,state.Handle,0,true,false),out var timeline));
        Assert.Equal(AlsTimelineEventKind.SetAction,timeline.Kind); Assert.Equal(f.Actions[0].ActionDefinitionId,timeline.SourceActionId);
        Assert.True(binding.Definitions[state.Offset].TriggerTimeSeconds<0);
        Assert.Equal(.9300273656845093f,binding.Definitions[state.Offset].EndTriggerTimeSeconds);
    }

    [Theory]
    [InlineData(30,false)] [InlineData(60,false)] [InlineData(120,false)]
    [InlineData(30,true)] [InlineData(60,true)] [InlineData(120,true)]
    public void RealRollRequestAndStateLifecycleCommitTogetherWithIdenticalRetry(int hz,bool replace)
    {
        var f=Data.Value; var binding=f.Compile(Read("v4_action_notify_inputs.json"));
        var bank=new AlsMontageRuntime(f.Turns,f.Actions); var actions=new AlsMontageActionRuntime(bank,f.Requests);
        var notify=new AlsMontageNotifyRuntime(binding); var current=default(AlsP5SourceEventState);
        var begins=0; var ends=0; var ticks=0; var instants=new List<int>(); var retries=0;
        var policy=f.Requests[0]; var delta=1f/hz;
        for(var frame=1;frame<=hz*4;frame++)
        {
            var id=new AlsFrameIdentity(frame,0,1); Prepare();
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(),id,delta,[],0,current,
                out var candidate,out var events,out var failure,montageBinding:binding,
                montageNotifies:notify.Notifies,montageDirectNotifies:notify.DirectNotifies),failure.ToString());
            var saved=events; var state=notify.Candidate; var instanceStates=bank.Candidate.ToArray();
            actions.Discard(); notify.Discard(); Prepare(); retries++;
            Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(),id,delta,[],0,current,
                out var retry,out events,out failure,montageBinding:binding,
                montageNotifies:notify.Notifies,montageDirectNotifies:notify.DirectNotifies),failure.ToString());
            Assert.Equal(candidate.NextInstanceId,retry.NextInstanceId); Assert.Equal(candidate.ActiveCount,retry.ActiveCount);
            Assert.Equal(state,notify.Candidate); Assert.Equal(instanceStates,bank.Candidate.ToArray()); Assert.Equal(saved.Count,events.Count);
            for(var i=0;i<events.Count;i++)
            {
                Assert.Equal(saved[i],events[i]); var e=events[i]; Assert.Equal(policy.DefinitionId,e.SourceActionId);
                if(e.Phase==AlsAnimationEventPhase.Begin)begins++;
                if(e.Phase==AlsAnimationEventPhase.End)ends++;
                if(e.Phase==AlsAnimationEventPhase.Tick)ticks++;
                if(e.Phase==AlsAnimationEventPhase.Trigger)instants.Add(e.EventId);
            }
            current=candidate; actions.ValidateCommit(id); notify.ValidateCommit(id); actions.Commit(id); notify.Commit(id);
            void Prepare()
            {
                actions.Begin(id,delta); notify.Begin(id,bank.Traversal);
                actions.ApplyRequest(frame==1 || replace && frame==hz/2 ?
                    new(frame,AlsActionCommand.Start,policy.DefinitionId,policy.StartSectionId,100,1):AlsActionRequest.None);
                actions.Complete(); notify.Complete(bank.SlotWeights(AlsMontageSlot.BaseLayer).SlotNodeWeight>.00001f ? (byte)4:(byte)0);
            }
        }
        // Native default State behavior merges overlapping plays of the same notify object.
        Assert.Equal(1,begins); Assert.Equal(1,ends); Assert.True(ticks>hz/2);
        var sequence=binding.Ranges.ToArray().Single(r=>r.ActionDefinitionId>=0 && !r.Direct);
        var expected=binding.Definitions.Slice(sequence.Offset,sequence.Count).ToArray();
        // Playback starts after the first tick. A half-second replacement samples
        // .4667 at 30 Hz, but .4833/.4917 at 60/120 Hz before the replacement command.
        var oldEvents=replace ? expected.Where(e=>e.TriggerTimeSeconds<=(hz/2-1)*delta).Select(e=>e.EventId) : [];
        Assert.Equal(oldEvents.Concat(expected.Select(e=>e.EventId)),instants);
        Assert.Equal(hz*4,retries); Assert.Equal(0,current.ActiveCount);
    }

    [Theory]
    [InlineData("count")] [InlineData("class")] [InlineData("end")] [InlineData("flags")] [InlineData("object")] [InlineData("branching")]
    public void StaleOrUnsupportedStateMetadataCannotBeBound(string mutation)
    {
        var root=JsonNode.Parse(Read("v4_action_notify_inputs.json"))!; var rows=root["montages"]!["syncAssets"]![0]!["notifies"]!.AsArray();
        var row=rows[0]!;
        switch(mutation)
        {
            case "count": rows.Clear(); break;
            case "class": row["class"]="wrong"; break;
            case "end": row["endTriggerTime"]=.93; break;
            case "flags": row["stateBehaviorFlags"]=2; break;
            case "object": row["stateObject"]="foreign"; break;
            case "branching": row["tickMode"]=1; break;
        }
        Assert.ThrowsAny<Exception>(()=>Data.Value.Compile(root.ToJsonString()));
    }

    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets","config",name));
    private sealed record Fixture(AlsAnimationSetDefinition Set,AlsLocomotionSourceProfile Sources,
        AlsP5CoreRuntimeBindingSnapshot Binding,AlsDynamicMontageAsset[] Turns,AlsAuthoredMontageAsset[] Actions,AlsMontageActionPolicy[] Requests)
    {
        public AlsMontageNotifyBinding Compile(string json)=>AlsMontageNotifyCompiler.Compile(json,Read("v4_turn_notify_inputs.json"),Set,Sources,Binding,Turns,Actions);
        public AlsSequenceMontageAsset[] Transitions => Set.Animations.Where(a => a.Name is "ALS_N_Transition_L" or "ALS_N_Transition_R")
            .Select(a => new AlsSequenceMontageAsset(a.Id, AlsMontageSlot.Grounded, 1, a.PlayLength, a.AdditiveType)).ToArray();
        public AlsMontageNotifyBinding CompileTransitions(string json) => AlsMontageNotifyCompiler.Compile(Read("v4_action_notify_inputs.json"), Read("v4_turn_notify_inputs.json"),
            Set, Sources, Binding, Turns, Actions, Transitions, json);
        public static Fixture Create()
        {
            var set=P3RepositoryFixtures.LoadAnimationSet();
            var locomotion=AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"),set);
            var pose=AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"),set,locomotion);
            var p5=AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"),set);
            var sources=AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"),set,locomotion.SkeletonId);
            var inventory=AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"),set,sources);
            var layout=AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion,pose,p5,sources,inventory);
            var binding=AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set,locomotion,pose,p5,layout,sources,inventory);
            var actions=AlsAuthoredMontageCompiler.Compile(Read("v4_action_montage_inputs.json"),Read("v4_turn_montage_inputs.json"),set,p5,pose.SkeletonId);
            return new(set,sources,binding,AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"),set,pose),
                actions,AlsAuthoredMontageCompiler.CompileRequests(p5,actions));
        }
    }
}
