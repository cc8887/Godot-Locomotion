using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingNotifyTests
{
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config",name+".json"));
    private static (string Json,AlsMantlingMontageProfile Profile,AlsMantlingNotifyProfile Notifies) Create()
    {
        var json=Read("refactored_mantle_animation_inputs");
        var profile=AlsMantlingMontageCompiler.Compile(json,Read("refactored_mantle_root_tracks"),Read("refactored_mantle_curves"));
        return(json,profile,AlsMantlingNotifyCompiler.Compile(json,profile));
    }
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void AllSixMontagesDeliverTheirActualThreeFootstepsOnPostLocomotion(int hz)
    {
        var(json,profile,compiled)=Create();Assert.Equal(9,compiled.Footsteps.Count);Assert.Equal(18,compiled.Binding.Definitions.Length);
        using var data=JsonDocument.Parse(json);
        foreach(var montage in profile.Definitions.Values)
        {
            var branch=AlsMantlingBranchCompiler.Compile(json,profile);var bank=profile.CreateRuntime(branch);
            var queue=new AlsMontageNotifyRuntime(compiled.Binding);var observed=new List<string>();
            bank.Begin(new(1,1,1),1f/hz);bank.PlayAction(montage.Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
            for(var frame=2;frame<5*hz;frame++)
            {
                var identity=new AlsFrameIdentity(frame,1,1);bank.Begin(identity,1f/hz);queue.Begin(identity,bank.NotifyTraversal);queue.Complete(16);
                Assert.Empty(queue.DirectNotifies.ToArray());
                foreach(var dispatch in queue.Notifies)
                {
                    Assert.Equal(1,dispatch.PlaybackEpoch);Assert.True(compiled.Binding.TryTimeline(dispatch.Reference,out var timeline));
                    Assert.Equal(AlsTimelineEventKind.Footstep,timeline.Kind);Assert.Equal(montage.Asset.ActionDefinitionId,timeline.SourceActionId);
                    var info=compiled.Footsteps[timeline.EventId];Assert.Equal((int)info.Foot,timeline.Payload.EnumValue0);observed.Add(info.ObjectPath);
                }
                bank.Commit(identity);queue.Commit(identity);
            }
            var expected=data.RootElement.GetProperty("sequences").EnumerateArray().Single(s=>s.GetProperty("raw").GetProperty("source").GetString()==montage.SequencePath)
                .GetProperty("notifies").EnumerateArray().Select(e=>e.GetProperty("notifyObject").GetString()).ToArray();
            Assert.Equal(expected,observed);
        }
        var high=compiled.Footsteps.Where(f=>f.ObjectPath.Contains("_High.")).ToArray();
        Assert.Equal(new[]{AlsTimelineFoot.Left,AlsTimelineFoot.Right,AlsTimelineFoot.Left},high.Select(f=>f.Foot));
        Assert.False(high[0].SpawnDecal);Assert.False(high[0].SpawnParticles);Assert.True(high[1].SpawnDecal);Assert.True(high[2].SpawnParticles);
    }
    [Fact]
    public void EarlyTickKeepsEarlierQueuedEventsAndDiscardReplaysMarkerContext()
    {
        var(json,profile,compiled)=Create();var high=profile.Definitions.Values.Single(d=>d.Path.Contains("_High."));
        var branch=AlsMantlingBranchCompiler.Compile(json,profile);branch.Capture(new(true,"Als.LocomotionMode.Grounded","Als.RotationMode.ViewDirection","Als.Stance.Standing"));
        var bank=profile.CreateRuntime(branch);var queue=new AlsMontageNotifyRuntime(compiled.Binding);
        bank.Begin(new(1,1,1),.01f);bank.PlayAction(high.Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        AlsAssetNotifyDispatchInput[] Run()
        {
            bank.Begin(new(2,1,1),2);Assert.True(bank.Candidate[0].Interrupted);Assert.All(bank.NotifyTraversal.ToArray(),t=>Assert.False(t.Interrupted));
            Assert.Single(bank.Traversal.ToArray());Assert.True(bank.Traversal[0].Interrupted);
            Assert.Equal(0,bank.Traversal[0].PreviousPosition);Assert.Equal(bank.Candidate[0].Position,bank.Traversal[0].CurrentPosition);
            Assert.Equal(3,bank.NotifyTraversal.Length);queue.Begin(new(2,1,1),bank.NotifyTraversal);queue.Complete(16);
            var events=queue.Notifies.ToArray();Assert.Equal(3,events.Length);
            Assert.Equal(bank.NotifyTraversal[0].CurrentPosition,events[0].Reference.CurrentTime);Assert.Equal(events[0].Reference.CurrentTime,events[1].Reference.CurrentTime);
            Assert.Equal(bank.NotifyTraversal[2].CurrentPosition,events[2].Reference.CurrentTime);return events;
        }
        var first=Run();bank.Discard();queue.Discard();Assert.Equal(first,Run());bank.Commit(new(2,1,1));queue.Commit(new(2,1,1));
        bank.Begin(new(3,1,1),.05f);queue.Begin(new(3,1,1),bank.NotifyTraversal);queue.Complete(16);Assert.Empty(queue.Notifies.ToArray());
    }
    [Theory]
    [InlineData(true,16)][InlineData(false,0)]
    public void DedicatedServerAndIrrelevantSlotDoNotDispatch(bool server,byte relevant)
    {
        var(json,profile,compiled)=Create();var bank=profile.CreateRuntime();var queue=new AlsMontageNotifyRuntime(compiled.Binding);
        bank.Begin(new(1,1,1),.01f);bank.PlayAction(profile.Definitions.Values.First().Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
        bank.Begin(new(2,1,1),2);queue.Begin(new(2,1,1),bank.NotifyTraversal,server);queue.Complete(relevant);Assert.Empty(queue.Notifies.ToArray());
    }
    [Fact]
    public void DifferentInputBytesCannotReuseTheCompiledPoseIdentity()
    {
        var(json,profile,_)=Create();Assert.Throws<ArgumentException>(()=>AlsMantlingNotifyCompiler.Compile(json+" ",profile));
    }
}
