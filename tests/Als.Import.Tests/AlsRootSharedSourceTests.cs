using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsRootSharedSourceTests
{
    private sealed record Fixture(AlsRootSharedSourceProfile Root, AlsOverlaySharedSourceProfile Overlay, AlsP5CoreRuntimeBindingSnapshot Binding);
    private static readonly Lazy<Fixture> Data = new(Create);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
    private static Fixture Create()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(Read("p5a_animation_runtime.json"), set);
        var movement = AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"), set, locomotion.SkeletonId);
        var overlay = AlsOverlaySourceCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), set);
        var clocks = AlsOverlaySyncCompiler.Compile(Read("v4_overlay_sync_inputs.json"), Read("v4_overlay_inputs.json"), overlay, set);
        var shared = AlsOverlaySharedSourceCompiler.Compile(movement, overlay, clocks, Read("v4_overlay_inputs.json"), Read("v4_overlay_notify_inputs.json"), set);
        var ragdoll = AlsRagdollPoseCompiler.Compile(Read("v4_ragdoll_inputs.json"), Read("v4_layering_inputs.json"), set);
        var frame = AlsRagdollFrameCompiler.Compile(Read("v4_ragdoll_inputs.json"), Read("v4_layering_inputs.json"), Read("v4_movement_runtime_inputs.json"), ragdoll, set);
        var root = AlsRootSharedSourceCompiler.Compile(shared, ragdoll, frame, set);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, root.Sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, pose, p5, root.Sources, inventory);
        return new(root, shared, AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, pose, p5, layout, root.Sources, inventory));
    }
    [Fact]
    public void AppendsOneAuthorityWithoutRenumberingOrDuplicatingTheAsset()
    {
        var f = Data.Value; var source = f.Root.Sources.CreateCoreView(); var previous = f.Overlay.Sources.CreateCoreView();
        Assert.Equal(224, source.Players.Length); Assert.Equal(258, source.Samples.Length);
        Assert.True(source.Players[..223].SequenceEqual(previous.Players)); Assert.True(source.Samples[..257].SequenceEqual(previous.Samples));
        Assert.True(source.SyncPlayers[..223].SequenceEqual(previous.SyncPlayers)); Assert.True(source.Sequences.SequenceEqual(previous.Sequences));
        Assert.Equal(f.Overlay.Overlay.ToArray(), f.Root.Overlay.Overlay.ToArray());
        var ragdoll = source.Players[f.Root.RagdollPlayerId];
        Assert.Equal(16, ragdoll.CompiledNodeIndex); Assert.Equal(AlsLocomotionSourceDomain.Ragdoll, ragdoll.Domain);
        Assert.Equal(AlsSourceRateInput.FlailRate, ragdoll.PlayRateInput); Assert.Equal(-1, ragdoll.SyncGroupId);
        var sample = source.Samples[f.Root.RagdollSampleId];
        var jump = source.Players.ToArray().Single(p => p.Domain == AlsLocomotionSourceDomain.Jump && sourceSampleAnimation(p.SampleStart) == sample.AnimationId);
        Assert.NotEqual(jump.PlayerId, ragdoll.PlayerId); Assert.NotEqual(jump.CompiledNodeIndex, ragdoll.CompiledNodeIndex);
        var layout = f.Binding.CreateSourceOccurrenceView();
        var ragEntry = layout.Entries[layout.Mappings[sample.SampleId].OccurrenceHandleId];
        var jumpEntry = layout.Entries[layout.Mappings[jump.SampleStart].OccurrenceHandleId];
        Assert.NotEqual(jumpEntry.AuthorityGroupId, ragEntry.AuthorityGroupId);
        Assert.DoesNotContain(16, f.Binding.UnboundNativeSourceIndices.ToArray());
        int sourceSampleAnimation(int id) => f.Root.Sources.RuntimeSamples[id].AnimationId;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void MissingOrNonfiniteFlailInputCannotPublishTicks(bool nonfinite)
    {
        var f = Data.Value; var source = f.Root.Sources.CreateCoreView();
        AlsLocomotionSourceUpdate[] updates = [new(f.Root.RagdollPlayerId, 1, 0, .3f, 0, 1)];
        AlsLocomotionSampleUpdate[] samples = [new(f.Root.RagdollSampleId, 1, 1)];
        var ticks = new AlsAssetSyncPlayer[1]; var output = new AlsAssetSyncSample[1]; var groups = new int[1];
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, 1,
            ticks, output, groups, out _, flailRate: nonfinite ? float.NaN : null));
        Assert.Equal(default, ticks[0]);
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(source, source.Stamp, updates, samples, 1,
            ticks, output, groups, out _, flailRate: .37f));
        Assert.Equal(.37f, ticks[0].PlayRate); Assert.Equal(-1, groups[0]);
        Assert.Equal(f.Root.RagdollPlayerId, ticks[0].PlayerId);
    }
    [Fact]
    public void FlailRateCannotBeAppliedToAnotherDomainOrSynchronizedPlayer()
    {
        var p = Data.Value.Root.Sources.RuntimePlayers[^1];
        Assert.True(AlsLocomotionSourceRuntime.HasValidInputPolicy(p));
        Assert.False(AlsLocomotionSourceRuntime.HasValidInputPolicy(p with { Domain = AlsLocomotionSourceDomain.Jump }));
        Assert.False(AlsLocomotionSourceRuntime.HasValidInputPolicy(p with { SyncGroupId = 0 }));
        Assert.False(AlsLocomotionSourceRuntime.HasValidInputPolicy(p with { Loop = false }));
    }
}
