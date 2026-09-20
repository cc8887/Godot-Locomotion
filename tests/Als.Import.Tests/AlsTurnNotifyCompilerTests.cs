using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsTurnNotifyCompilerTests
{
    private static readonly Lazy<Fixture> Data = new(Fixture.Create);

    [Fact]
    public void AllOriginalTurnNotifiesAreBoundWithoutReindexingSourcePolicies()
    {
        var f = Data.Value; var binding = f.Compile(Read("v4_turn_notify_inputs.json"));
        Assert.Equal(8, binding.Assets.Length); Assert.Equal(26, binding.Definitions.Length);
        Assert.Equal(f.Binding.CreateCoreView().Sources.NotifyPolicies.ToArray(), binding.Policies[..binding.SourcePolicyCount].ToArray());
        Assert.Equal(26, binding.Policies[binding.SourcePolicyCount..].ToArray().Select(p => p.NotifyObjectId).Distinct().Count());
        Assert.All(binding.Policies[binding.SourcePolicyCount..].ToArray(), p =>
        {
            Assert.True(p.NotifyObjectId >= f.Sources.NotifyObjects.Length); Assert.Equal(-1, p.StateObjectId);
            Assert.Equal(1, p.Chance); Assert.Equal(.5f, p.WeightThreshold); Assert.Equal(AlsTimelineTickMode.Queued, p.TickMode);
        });
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void EveryRealTurnSequenceDispatchesItsAuthoredEventsThroughTheSharedAllocator(int hz)
    {
        var f = Data.Value; var binding = f.Compile(Read("v4_turn_notify_inputs.json")); var total = 0;
        foreach (var asset in binding.Assets.ToArray())
        {
            var montages = new AlsMontageRuntime(f.Turns); var notifies = new AlsTurnNotifyRuntime(binding);
            var state = default(AlsP5SourceEventState); var observed = new List<int>();
            for (var frame = 1; frame < hz * 4; frame++)
            {
                var identity = new AlsFrameIdentity(frame,0,1); var delta = 1f / hz;
                montages.Begin(identity,delta); notifies.Begin(identity,montages.Traversal);
                if (frame == 1) montages.Play(new(asset.Asset.AnimationId,asset.Asset.Slot,1.2f,0,.2f,.2f,1,0));
                notifies.Complete(asset.Asset.Slot == AlsTurnSlot.Standing && montages.SlotWeights(AlsTurnSlot.Standing).SlotNodeWeight > .00001f,
                    asset.Asset.Slot == AlsTurnSlot.Crouching && montages.SlotWeights(AlsTurnSlot.Crouching).SlotNodeWeight > .00001f);
                Assert.True(AlsP5Runtime.TryPrepareSourceEvents(f.Binding.CreateCoreView(),identity,delta,[],0,state,
                    out var candidate,out var events,out var failure,montageBinding:binding,montageNotifies:notifies.Notifies),failure.ToString());
                for (var i = 0; i < events.Count; i++)
                {
                    Assert.Equal(asset.Asset.AnimationId,events[i].SourceAnimationId); Assert.Equal(asset.Handle,events[i].OccurrenceHandleId);
                    Assert.Equal(1,events[i].PlaybackEpoch); Assert.True(events[i].NativeContext.Present); observed.Add(events[i].EventId);
                }
                state = candidate; montages.Commit(identity); notifies.Commit(identity);
            }
            Assert.Equal(binding.Definitions.Slice(asset.Offset,asset.Count).ToArray().Select(d => d.EventId),observed);
            total += observed.Count;
        }
        Assert.Equal(26,total);
    }

    [Theory]
    [InlineData("asset")] [InlineData("order")] [InlineData("time")] [InlineData("class")]
    [InlineData("object")] [InlineData("offset")] [InlineData("threshold")] [InlineData("chance")]
    public void MissingOrStaleNativeMetadataIsRejected(string mutation)
    {
        var data = JsonNode.Parse(Read("v4_turn_notify_inputs.json"))!;
        var row = data["syncAssets"]![0]!["notifies"]![0]!;
        switch (mutation)
        {
            case "asset": data["syncAssets"]!.AsArray().RemoveAt(0); break;
            case "order": row["index"] = 99; break;
            case "time": row["time"] = 0; break;
            case "class": row["class"] = "wrong"; break;
            case "object": row["notifyObject"] = "wrong"; break;
            case "offset": row["triggerOffset"] = .1; break;
            case "threshold": row["weightThreshold"] = .1; break;
            case "chance": row["chance"] = 2; break;
        }
        Assert.ThrowsAny<Exception>(() => Data.Value.Compile(data.ToJsonString()));
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets","config",file));
    private sealed record Fixture(AlsAnimationSetDefinition Set, AlsLocomotionSourceProfile Sources,
        AlsP5CoreRuntimeBindingSnapshot Binding, AlsDynamicMontageAsset[] Turns)
    {
        public AlsTurnNotifyBinding Compile(string json) => AlsTurnNotifyCompiler.Compile(json,Set,Sources,Binding,Turns);
        public static Fixture Create()
        {
            var set = P3RepositoryFixtures.LoadAnimationSet();
            var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"),set);
            var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"),set,locomotion);
            var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"),set);
            var sources = AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"),set,locomotion.SkeletonId);
            var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"),set,sources);
            var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion,pose,p5,sources,inventory);
            var binding = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set,locomotion,pose,p5,layout,sources,inventory);
            return new(set,sources,binding,AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"),set,pose));
        }
    }
}
