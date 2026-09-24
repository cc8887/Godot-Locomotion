using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsRecoveryActionProfileTests
{
    [Fact]
    public void DefaultGetUpsExtendOnePhysicalBankAndUseNativeRateAndNotifyBindings()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var profile = AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Read("p5_get_up_actions.json"), set);
        Assert.Equal(3, profile.Actions.Length); Assert.Equal(0, profile.DemoCases.RollActionDefinitionId);
        var sources = AlsLocomotionSourceCompiler.CompileWithMovement(Read("v4_main_movement_graph.json"), set, locomotion.SkeletonId);
        var inventory = AlsP5SourceInventoryCompiler.Compile(Read("v4_anim_graph_inventory.json"), set, sources);
        var layout = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(locomotion, pose, profile, sources, inventory);
        var binding = AlsP5CoreRuntimeBindingCompiler.CompileSourceAware(set, locomotion, pose, profile, layout, sources, inventory);
        var assets = AlsAuthoredMontageCompiler.Compile(Read("v4_recovery_action_montage_inputs.json"), Read("v4_turn_montage_inputs.json"), set, profile, pose.SkeletonId);
        var turns = AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"), set, pose);
        var notifies = AlsMontageNotifyCompiler.Compile(Read("v4_recovery_action_notify_inputs.json"), Read("v4_turn_notify_inputs.json"), set, sources, binding, turns, assets);
        _ = AlsMontageActionPlaybackCompiler.Compile(set, assets, notifies);
        var policies = AlsAuthoredMontageCompiler.CompileRequests(profile, assets);
        Assert.Equal(1, assets[0].RateScale);
        foreach (var asset in assets.Skip(1))
        {
            Assert.Equal(1.2f, asset.RateScale); Assert.Equal(0, asset.Lifecycle.BlendInSeconds);
            Assert.True(asset.RootMotionEnabled); Assert.Equal(assets[0].GroupId, asset.GroupId);
            Assert.Equal(1, policies.Single(p => p.DefinitionId == asset.ActionDefinitionId).PlayRate);
            Assert.Contains(notifies.Ranges.ToArray(), r => r.ActionDefinitionId == asset.ActionDefinitionId && r.Direct && r.Count > 0);
            var owner = new AlsMontageRuntime(turns, assets);
            owner.Begin(new(1, 1, 1), .1f); owner.PlayAction(asset.ActionDefinitionId, 1); owner.Commit(new(1, 1, 1));
            owner.Begin(new(2, 1, 1), .1f);
            Assert.Equal(.12f, owner.Candidate[0].Position, 6);
            Assert.Equal(1, owner.Evaluation[0].Weight);
            Assert.True(owner.RootMotionRange.HasMotion);
        }
        Assert.ThrowsAny<Exception>(() => AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"),
            "[" + System.Text.Json.Nodes.JsonNode.Parse(Read("p5a_animation_runtime.json"))!["actions"]![0]!.ToJsonString() + "]", set));
    }
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", file));
}
