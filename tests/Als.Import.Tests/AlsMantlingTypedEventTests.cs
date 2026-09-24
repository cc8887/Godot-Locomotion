using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingTypedEventTests
{
    [Fact]
    public void SourceGraphAndMontageEventsUseTheSameProxyQueueWithoutIdentityCollision()
    {
        var json=MantlingHostFixture.Read("refactored_mantle_animation_inputs");
        var resources=MantlingHostFixture.Bind(AlsMantlingMontageCompiler.Compile(json,
            MantlingHostFixture.Read("refactored_mantle_root_tracks"),MantlingHostFixture.Read("refactored_mantle_curves")));
        var host=MantlingHostFixture.EventBindings();var binding=host.Core.CreateCoreView();var mapped=resources.BindNotifies(json,host.Notifies);
        var ranges=binding.Sources.NotifyRanges.ToArray();var players=binding.Sources.Players.ToArray();
        var sample=binding.Sources.Samples.ToArray().First(s=>ranges[s.SequenceIndex].Count>0&&
            players[s.PlayerId].Kind==AlsLocomotionSourceKind.Sequence);
        var player=binding.Sources.Players[sample.PlayerId];
        AlsP5SourceNotifyTick[] ticks=[new(sample.SampleId,1,0,sample.DurationSeconds,sample.DurationSeconds,1,true,player.Loop)];
        var bank=resources.CreateRuntime();var queue=new AlsMontageNotifyRuntime(mapped.Binding);
        bank.Begin(new(1,7,1),.01f);bank.PlayAction(resources.Profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,7,1));
        var id=new AlsFrameIdentity(2,7,1);bank.Begin(id,.8f);queue.Begin(id,bank.NotifyTraversal);queue.Complete(16);
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(binding,id,.8f,ticks,1,default,out _,out var source,out var failure),failure.ToString());
        Assert.True(AlsP5Runtime.TryPrepareSourceEvents(binding,id,.8f,ticks,1,default,out _,out var combined,out failure,
            montageBinding:mapped.Binding,montageNotifies:queue.Notifies,montageDirectNotifies:queue.DirectNotifies),failure.ToString());
        Assert.True(source.Count>0&&combined.Count>source.Count);
        var sourceCount=0;var mantleCount=0;
        for(var i=0;i<combined.Count;i++)
        {
            var item=combined[i];
            if(mapped.TryResolveFootstep(item,out _)){mantleCount++;Assert.NotEqual(sample.AnimationId,item.SourceAnimationId);}
            else {Assert.Equal(sample.AnimationId,item.SourceAnimationId);sourceCount++;}
        }
        Assert.Equal(source.Count,sourceCount);Assert.True(mantleCount>0);
    }
    [Theory]
    [InlineData(30,"natural")][InlineData(60,"natural")][InlineData(120,"natural")]
    [InlineData(30,"replace")][InlineData(60,"replace")][InlineData(120,"replace")]
    [InlineData(30,"hidden")][InlineData(60,"hidden")][InlineData(120,"hidden")]
    [InlineData(30,"ragdoll")][InlineData(60,"ragdoll")][InlineData(120,"ragdoll")]
    public void SharedProxyDispatchKeepsMappedProvenanceAndRetriesAtomically(int hz,string scenario)
    {
        var json=MantlingHostFixture.Read("refactored_mantle_animation_inputs");
        var resources=MantlingHostFixture.Bind(AlsMantlingMontageCompiler.Compile(json,
            MantlingHostFixture.Read("refactored_mantle_root_tracks"),MantlingHostFixture.Read("refactored_mantle_curves")));
        var host=MantlingHostFixture.EventBindings();var mapped=resources.BindNotifies(json,host.Notifies);
        var count=0;var rollStates=0;var hiddenFrames=0;var replacedEvents=0;
        foreach(var definition in resources.Profile.Definitions.Values)
        {
            var branch=AlsMantlingBranchCompiler.Compile(json,resources.Profile);var bank=resources.CreateRuntime(branch);
            var queue=new AlsMontageNotifyRuntime(mapped.Binding);var committed=default(AlsP5SourceEventState);
            for(var frame=1;frame<=hz*5;frame++)
            {
                var identity=new AlsFrameIdentity(frame,7,1);var delta=1f/hz;
                Prepare();
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(host.Core.CreateCoreView(),identity,delta,[],0,committed,
                    out var candidate,out var events,out var failure,montageBinding:mapped.Binding,
                    montageNotifies:queue.Notifies,montageDirectNotifies:queue.DirectNotifies),failure.ToString());
                var saved=events;var savedQueue=queue.Candidate;var savedInstances=bank.Candidate.ToArray();
                bank.Discard();queue.Discard();Prepare();
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(host.Core.CreateCoreView(),identity,delta,[],0,committed,
                    out var retry,out events,out failure,montageBinding:mapped.Binding,
                    montageNotifies:queue.Notifies,montageDirectNotifies:queue.DirectNotifies),failure.ToString());
                Assert.Equal(candidate.RandomSeed,retry.RandomSeed);Assert.Equal(candidate.NextInstanceId,retry.NextInstanceId);
                Assert.Equal(candidate.ActiveCount,retry.ActiveCount);Assert.Equal(savedQueue,queue.Candidate);
                Assert.Equal(savedInstances,bank.Candidate.ToArray());Assert.Equal(saved.Count,events.Count);
                for(var i=0;i<events.Count;i++)
                {
                    var item=events[i];Assert.Equal(saved[i],item);
                    if(item.SourceActionId==resources.Actions[0].ActionDefinitionId)
                    {Assert.False(mapped.TryResolveFootstep(item,out _));if(item.Phase!=AlsAnimationEventPhase.Trigger)rollStates++;continue;}
                    Assert.True(mapped.TryResolveFootstep(item,out var settings));Assert.NotNull(settings);
                    Assert.Equal(definition.Asset.ActionDefinitionId,item.SourceActionId);Assert.Equal(definition.Asset.AnimationId,item.SourceAnimationId);
                    Assert.Equal((int)settings.Foot,item.Payload.EnumValue0);Assert.True(item.NativeContext.Present);
                    Assert.Contains(queue.Notifies.ToArray(),n=>n.PlaybackEpoch==item.PlaybackEpoch&&
                        n.Reference.OccurrenceHandleId==item.OccurrenceHandleId&&n.Reference.CurrentTime==item.NativeContext.CurrentAnimationTime);
                    if(item.PlaybackEpoch==3)replacedEvents++;
                    Assert.False(mapped.TryResolveFootstep(item with {SourceActionId=int.MaxValue},out _));
                    Assert.False(mapped.TryResolveFootstep(item with {SourceAnimationId=int.MaxValue},out _));
                    Assert.False(mapped.TryResolveFootstep(item with {OccurrenceHandleId=int.MaxValue},out _));
                    Assert.False(mapped.TryResolveFootstep(item with {Phase=AlsAnimationEventPhase.End},out _));
                    Assert.False(mapped.TryResolveFootstep(item with {Payload=item.Payload with {EnumValue0=-1}},out _));count++;
                }
                bank.ValidateCommit(identity);queue.ValidateCommit(identity);bank.Commit(identity);queue.Commit(identity);committed=candidate;
                void Prepare()
                {
                    bank.Begin(identity,delta,scenario=="ragdoll"&&frame==hz);
                    queue.Begin(identity,bank.NotifyTraversal);
                    if(frame==1)
                    {bank.PlayAction(resources.Actions[0].ActionDefinitionId,1);bank.PlayAction(definition.Asset.ActionDefinitionId,1);}
                    if(scenario=="replace"&&frame==hz/2)bank.PlayAction(definition.Asset.ActionDefinitionId,1);
                    var visible=scenario!="hidden"||frame<hz/3||frame>hz*2;
                    if(!visible)hiddenFrames++;
                    queue.Complete(visible?(byte)20:(byte)4);
                }
            }
            Assert.Equal(0,committed.ActiveCount);Assert.Empty(bank.Committed.ToArray());Assert.Equal("",branch.CommittedAction);
        }
        Assert.True(rollStates>0);
        if(scenario=="natural")Assert.Equal(18,count);
        if(scenario=="replace")Assert.True(replacedEvents>0&&count>=18);
        if(scenario=="hidden")Assert.True(hiddenFrames>0&&count<18);
        if(scenario=="ragdoll")Assert.True(count<18);
    }
}
