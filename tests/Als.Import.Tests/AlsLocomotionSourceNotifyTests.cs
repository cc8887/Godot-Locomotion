using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Events;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionSourceNotifyTests
{
    [Fact]
    public void NativeFieldsReachTheBorrowedSourceTablesWithoutChangingAuthoredTimes()
    {
        var root = Read(); var (source, set) = Compile(root); var view = source.CreateCoreView();
        Assert.Equal(45, view.NotifyRanges.Length); Assert.Equal(73, view.NotifyDefinitions.Length);
        Assert.Equal(73, view.NotifyPolicies.Length); Assert.Equal(73, source.NotifyObjects.Length);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyPolicy>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsAssetNotifyRange>());
        var offset = 0;
        for (var sequence = 0; sequence < view.NotifyRanges.Length; sequence++)
        {
            var range = view.NotifyRanges[sequence]; var animation = set.Animations[range.AnimationId];
            Assert.Equal(view.Sequences[sequence].AnimationId, range.AnimationId); Assert.Equal(offset, range.Offset);
            var asset = root["syncAssets"]!.AsArray().Single(a => a!["path"]!.GetValue<string>() == animation.ObjectPath)!;
            Assert.Equal(asset["notifies"]!.AsArray().Count, range.Count);
            for (var i = 0; i < range.Count; i++)
            {
                var row = asset["notifies"]![i]!; var definition = view.NotifyDefinitions[offset]; var policy = view.NotifyPolicies[offset++];
                var imported = animation.Timeline.Single(e => e.SourceIndex == i);
                Assert.Equal(imported.EventId, definition.EventId); Assert.Equal(definition.EventId, policy.EventId);
                Assert.Equal(i, policy.SourceIndex); Assert.Equal(imported.TrackIndex, policy.TrackIndex);
                Assert.Equal(row["triggerTime"]!.GetValue<float>(), definition.TriggerTimeSeconds);
                Assert.Equal(row["endTriggerTime"]!.GetValue<float>(), definition.EndTriggerTimeSeconds);
                Assert.Equal(row["weightThreshold"]!.GetValue<float>(), policy.WeightThreshold);
                Assert.Equal(row["chance"]!.GetValue<float>(), policy.Chance);
                Assert.Equal(row["filterType"]!.GetValue<int>(), (int)policy.FilterType);
                Assert.Equal(row["filterLod"]!.GetValue<int>(), policy.FilterLod);
                Assert.Equal(row["tickMode"]!.GetValue<int>(), (int)policy.TickMode);
                Assert.Equal(row["filterViaRequest"]!.GetValue<bool>(), policy.FilterViaRequest);
                Assert.Equal(row["onDedicatedServer"]!.GetValue<bool>(), policy.OnDedicatedServer);
                Assert.Equal(row["onFollower"]!.GetValue<bool>(), policy.OnFollower);
                Assert.Equal(row["notifyObject"]!.GetValue<string>(), source.NotifyObjects[policy.NotifyObjectId]);
                Assert.Equal(-1, policy.StateObjectId); Assert.Equal(row["name"]!.GetValue<string>(), source.NotifyNames[policy.NameId]);
            }
        }
        Assert.Equal(73, offset);
        var original = source.NotifyObjects[0]; var names = source.NotifyNames; var objects = source.NotifyObjects;
        objects[0] = "modified"; names[0] = "modified";
        Assert.Equal(original, source.NotifyObjects[0]); Assert.DoesNotContain("modified", source.NotifyNames);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("missing-array")]
    [InlineData("missing-field")]
    [InlineData("count")]
    [InlineData("order")]
    [InlineData("time")]
    [InlineData("duration")]
    [InlineData("threshold")]
    [InlineData("track")]
    [InlineData("offset")]
    [InlineData("end")]
    [InlineData("chance")]
    [InlineData("filter")]
    [InlineData("lod")]
    [InlineData("tick")]
    [InlineData("object")]
    [InlineData("class")]
    [InlineData("both-objects")]
    [InlineData("named-only")]
    [InlineData("null")]
    [InlineData("nonfinite")]
    public void MissingStaleOrUnsupportedMetadataIsRejected(string mutation)
    {
        var root = Read(); var asset = Asset(root); var row = asset["notifies"]![0]!.AsObject();
        switch (mutation)
        {
            case "schema": root.AsObject().Remove("notifySchemaVersion"); break;
            case "missing-array": asset.Remove("notifies"); break;
            case "missing-field": row.Remove("onFollower"); break;
            case "count": asset["notifies"]!.AsArray().RemoveAt(0); break;
            case "order": row["index"] = 99; break;
            case "time": row["time"] = .123f; break;
            case "duration": row["duration"] = .1f; break;
            case "threshold": row["weightThreshold"] = .5f; break;
            case "track": row["track"] = 99; break;
            case "offset": row["triggerOffset"] = -.0001f; break;
            case "end": row["endTriggerTime"] = -1; break;
            case "chance": row["chance"] = 1.01f; break;
            case "filter": row["filterType"] = 2; break;
            case "lod": row["filterLod"] = -1; break;
            case "tick": row["tickMode"] = 1; break;
            case "object": row["notifyObject"] = "/Game/Other.Other:Notify"; break;
            case "class": row["class"] = "/Script/Engine.AnimNotify"; break;
            case "both-objects": row["stateObject"] = row["notifyObject"]!.DeepClone(); break;
            case "named-only": row["notifyObject"] = ""; break;
            case "null": row["filterViaRequest"] = null; break;
            case "nonfinite": row["chance"] = double.MaxValue; break;
        }
        Assert.Throws<AlsCompilationException>(() => Compile(root));
    }

    [Fact]
    public void BoundEffectiveWindowMovesTheNotifyWithoutMutatingTheImportedTimeline()
    {
        var root = Read(); var asset = Asset(root); var row = asset["notifies"]![0]!;
        var authored = row["time"]!.GetValue<float>(); var effective = authored + .01f;
        row["triggerOffset"] = .01f; row["triggerTime"] = effective; row["endTriggerTime"] = effective;
        var original = Compile(Read()).Source; var (source, set) = Compile(root); var view = source.CreateCoreView();
        var animationId = set.Animations.Single(a => a.ObjectPath == asset["path"]!.GetValue<string>()).Id;
        var range = view.NotifyRanges.ToArray().Single(r => r.AnimationId == animationId);
        var definitions = view.NotifyDefinitions.Slice(range.Offset, range.Count);
        Assert.Equal(authored, set.Animations[range.AnimationId].Timeline.Single(e => e.SourceIndex == 0).TimeSeconds);
        Assert.Equal(effective, definitions[0].TriggerTimeSeconds); Assert.NotEqual(original.Digest, source.Digest);
        Span<AlsAssetNotifyOccurrence> output = stackalloc AlsAssetNotifyOccurrence[8];
        Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies(definitions, set.Animations[range.AnimationId].PlayLength,
            authored - .001f, .002f, false, output, out var before, out _));
        Assert.Equal(0, before);
        Assert.True(AlsTimelineRuntime.TryExtractAssetNotifies(definitions, set.Animations[range.AnimationId].PlayLength,
            effective - .001f, .002f, false, output, out var after, out _));
        Assert.Equal(1, after); Assert.Equal(definitions[0].EventId, output[0].EventId);
    }

    [Fact]
    public void StateEndIncludesStartOffsetAndSharedObjectIsNotSplitBySourceIndex()
    {
        var root = Read(); var asset = Asset(root); var set = P3RepositoryFixtures.LoadAnimationSet();
        var animation = set.Animations.Single(a => a.ObjectPath == asset["path"]!.GetValue<string>());
        var timeline = animation.Timeline;
        const string stateClass = "/Script/AlsGodotExporter.AlsNotifyWindowProbeState";
        var stateObject = animation.ObjectPath + ":SharedState";
        for (var i = 0; i < 2; i++)
        {
            var index = Array.FindIndex(timeline, e => e.SourceIndex == i); var e = timeline[index];
            timeline[index] = e with { SourceClassPath = stateClass, DurationSeconds = .1f, Kind = AlsCompiledTimelineEventKind.Generic, Payload = default };
            var row = asset["notifies"]![i]!; var trigger = e.TimeSeconds - .0001f;
            row["notifyObject"] = ""; row["stateObject"] = stateObject; row["class"] = stateClass;
            row["stateBehaviorFlags"] = 1;
            row["duration"] = .1f; row["triggerOffset"] = -.0001f; row["endTriggerOffset"] = .0002f;
            row["triggerTime"] = trigger; row["endTriggerTime"] = trigger + .1f + .0002f;
            row["chance"] = 0; row["onFollower"] = true; row["filterType"] = 1; row["filterLod"] = 2;
        }
        set.Animations[animation.Id] = animation with { Timeline = timeline };
        using var document = JsonDocument.Parse(root.ToJsonString());
        var compiled = AlsLocomotionSourceNotifyCompiler.Compile(document.RootElement, set);
        var range = compiled.Ranges.Single(r => r.AnimationId == animation.Id);
        var first = compiled.Policies[range.Offset]; var second = compiled.Policies[range.Offset + 1];
        Assert.Equal(-1, first.NotifyObjectId); Assert.True(first.StateObjectId >= 0);
        Assert.Equal(first.StateObjectId, second.StateObjectId); Assert.NotEqual(first.EventId, second.EventId);
        Assert.Equal(0, first.Chance); Assert.True(first.OnFollower); Assert.Equal(AlsAssetNotifyFilterType.Lod, first.FilterType);
        Assert.Equal(2, first.FilterLod);
        Assert.Equal((byte)1, first.StateBehaviorFlags);
        Assert.Equal(compiled.Definitions[range.Offset].TriggerTimeSeconds + .1f + .0002f,
            compiled.Definitions[range.Offset].EndTriggerTimeSeconds);
        foreach (var flags in new int?[] { null, 2, 256 })
        {
            if (flags.HasValue) asset["notifies"]![0]!["stateBehaviorFlags"] = flags.Value;
            else asset["notifies"]![0]!.AsObject().Remove("stateBehaviorFlags");
            using var invalid = JsonDocument.Parse(root.ToJsonString());
            Assert.ThrowsAny<Exception>(() => AlsLocomotionSourceNotifyCompiler.Compile(invalid.RootElement, set));
        }
    }

    private static JsonNode Read() => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
        "assets", "config", "v4_locomotion_source_graph.json")))!;
    private static JsonObject Asset(JsonNode root) => root["syncAssets"]!.AsArray().First(a => a!["notifies"]!.AsArray().Count > 0)!.AsObject();
    private static (AlsLocomotionSourceProfile Source, AlsAnimationSetDefinition Set) Compile(JsonNode root)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet(); var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        return (AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, locomotion.SkeletonId), set);
    }
}
