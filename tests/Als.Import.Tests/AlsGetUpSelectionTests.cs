using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Import.Tests;

public sealed class AlsGetUpSelectionTests
{
    [Fact]
    public void NativeOverrideNotifyUsesExactMontageAndActionIdentityAndResetsOnEnd()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var actions = AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Read("p5_get_up_actions.json"), set);
        var profile = AlsOverlayOverrideNotifyCompiler.Compile(Read("v4_get_up_selection_inputs.json"),
            Read("v4_recovery_action_notify_inputs.json"), set, actions);
        var root = JsonNode.Parse(Read("v4_recovery_action_notify_inputs.json"))!;
        var covered = 0;
        foreach (var row in root["montages"]!["syncAssets"]!.AsArray())
        {
            var action = actions.Actions.Single(a => set.Montages[a.MontageId].ObjectPath == row!["path"]!.GetValue<string>());
            foreach (var notify in row!["notifies"]!.AsArray().Where(n => n!["overlayOverrideState"] is not null))
            {
                var timeline = set.Montages[action.MontageId].Timeline.Single(t => t.SourceIndex == notify!["index"]!.GetValue<int>());
                var item = new AlsAnimationEvent(timeline.EventId, action.MontageId, action.DefinitionId, 1, 1, 0, 1, 1, 0, 0, 1,
                    AlsTimelineEventKind.Generic, AlsAnimationEventPhase.Begin, default);
                var events = new AlsEventBuffer(); Assert.True(events.TryAdd(item));
                Assert.Equal(3, profile.Advance(0, events));
                events = new(); Assert.True(events.TryAdd(item with { SourceActionId = -1 }));
                Assert.Equal(0, profile.Advance(0, events));
                events = new(); Assert.True(events.TryAdd(item with { Phase = AlsAnimationEventPhase.Tick }));
                Assert.Equal(3, profile.Advance(3, events));
                events = new(); Assert.True(events.TryAdd(item with { Phase = AlsAnimationEventPhase.End }));
                Assert.Equal(0, profile.Advance(3, events)); covered++;
            }
        }
        Assert.Equal(6, covered);
    }
    [Fact]
    public void BindsAllNativeOverlayAndFacingResultsToDistinctMontages()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var actions = AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Read("p5_get_up_actions.json"), set);
        var selection = AlsGetUpSelectionCompiler.Compile(Read("v4_get_up_selection_inputs.json"), set, actions);
        // Independent native function results, including less obvious cases:
        // Injured/Barrel LH, HandsTied/Box 2H, two-handed pistol still RH.
        var suffixes = new[] { "Default", "Default", "Default", "LH", "2H", "RH", "RH", "RH", "LH", "LH", "RH", "2H", "LH" };
        for (var overlay = 0; overlay < suffixes.Length; overlay++)
            foreach (var facingUp in new[] { false, true })
            {
                var id = selection.Select((AlsOverlayKind)overlay, facingUp);
                var montage = set.Montages[actions.Actions.Single(a => a.DefinitionId == id).MontageId];
                Assert.EndsWith($".ALS_CLF_GetUp_{(facingUp ? "Back" : "Front")}_Montage_{suffixes[overlay]}", montage.ObjectPath);
            }
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Select((AlsOverlayKind)255, false));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign_overlay")]
    [InlineData("roll")]
    public void RejectsIncompleteOrForeignBindings(string mutation)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var actions = AlsRecoveryActionProfileCompiler.Compile(Read("p5a_animation_runtime.json"), Read("p5_get_up_actions.json"), set);
        var root = JsonNode.Parse(Read("v4_get_up_selection_inputs.json"))!;
        var cases = root["cases"]!.AsArray();
        if (mutation == "missing") cases.RemoveAt(0);
        if (mutation == "duplicate") cases.Add(cases[0]!.DeepClone());
        if (mutation == "foreign_overlay") cases[0]!["overlay"] = 13;
        if (mutation == "roll") cases[0]!["montage"] = set.Montages[actions.Actions[0].MontageId].ObjectPath;
        Assert.Throws<ArgumentException>(() => AlsGetUpSelectionCompiler.Compile(root.ToJsonString(), set, actions));
    }
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", file));
}
