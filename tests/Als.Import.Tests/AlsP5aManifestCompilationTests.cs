using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsP5aManifestCompilationTests
{
    [Fact]
    public void CompilesDeterministicGlobalTimelineAndMarkerIdsAcrossAuthoredReorder()
    {
        var originalRoot = ValidRoot();
        var reorderedRoot = ValidRoot();
        Reverse((JsonArray)reorderedRoot["animations"]![0]!["metadata"]!["timeline"]!);
        Reverse((JsonArray)reorderedRoot["animations"]![0]!["metadata"]!["syncMarkers"]!);

        var original = Compile(originalRoot);
        var reordered = Compile(reorderedRoot);
        var events = original.Animations.SelectMany(value => value.Timeline)
            .Concat(original.Montages.SelectMany(value => value.Timeline)).ToArray();
        var markers = original.Animations.SelectMany(value => value.SyncMarkers).ToArray();

        Assert.Equal(original.DefinitionDigest, reordered.DefinitionDigest);
        Assert.Equal(events.OrderBy(value => value.StableEventId, StringComparer.Ordinal)
            .Select((value, index) => (value.StableEventId, EventId: index)),
            events.OrderBy(value => value.StableEventId, StringComparer.Ordinal)
                .Select(value => (value.StableEventId, value.EventId)));
        Assert.Equal(markers.OrderBy(value => value.StableMarkerId, StringComparer.Ordinal)
            .Select((value, index) => (value.StableMarkerId, MarkerId: index)),
            markers.OrderBy(value => value.StableMarkerId, StringComparer.Ordinal)
                .Select(value => (value.StableMarkerId, value.MarkerId)));
        Assert.Equal(events.Length, events.Select(value => value.EventId).Distinct().Count());
        Assert.Equal(markers.Length, markers.Select(value => value.MarkerId).Distinct().Count());
        Assert.Equal(original.Animations[0].Timeline.Select(value => value.StableEventId),
            reordered.Animations[0].Timeline.Select(value => value.StableEventId));
        Assert.Equal(original.Animations[0].SyncMarkers.Select(value => value.StableMarkerId),
            reordered.Animations[0].SyncMarkers.Select(value => value.StableMarkerId));
        Assert.All(events, value => Assert.Equal(value.EventId,
            original.AssetIndex.GetEventId(value.StableEventId)));
        Assert.All(markers, value => Assert.Equal(value.MarkerId,
            original.AssetIndex.GetMarkerId(value.StableMarkerId)));
    }

    [Fact]
    public void RecomputesEventStableIdBeforeAllocatingGlobalIds()
    {
        var root = ValidRoot();
        root["animations"]![0]!["metadata"]!["timeline"]![0]!["stableEventId"] = new string('0', 40);

        AssertIssue(root, "$.animations[0].metadata.timeline[0].stableEventId");
    }

    [Fact]
    public void RecomputesMarkerStableIdBeforeAllocatingGlobalIds()
    {
        var root = ValidRoot();
        root["animations"]![0]!["metadata"]!["syncMarkers"]![0]!["stableMarkerId"] = new string('0', 40);

        AssertIssue(root, "$.animations[0].metadata.syncMarkers[0].stableMarkerId");
    }

    [Fact]
    public void StableIdOracleUsesExactUtf8Preimages()
    {
        Assert.Equal("3216797275ae130fec949cafe7508f708072e4ac", Sha1(
            "67aa33bdcab9e580ed7bec894c9858bb5cf30764|timeline|0|/Script/Engine.AnimNotify"));
        Assert.Equal("fddebb8f82275366fd0698e3269b7b82b6debfc2", Sha1(
            "67aa33bdcab9e580ed7bec894c9858bb5cf30764|marker|0|Left"));
    }

    [Fact]
    public void RejectsKindPayloadMismatch()
    {
        var root = ValidRoot();
        var value = root["animations"]![0]!["metadata"]!["timeline"]![1]!;
        value["kind"] = "Generic";

        AssertManifestRejected(root);
    }

    [Fact]
    public void PreservesDistinctAuthoredEventsWithEqualPayload()
    {
        var root = ValidRoot();
        var timeline = (JsonArray)root["animations"]![0]!["metadata"]!["timeline"]!;
        var duplicatePayload = timeline[0]!.DeepClone();
        duplicatePayload["sourceIndex"] = 9;
        duplicatePayload["timeSeconds"] = 0.15;
        duplicatePayload["stableEventId"] = Sha1(
            $"{root["animations"]![0]!["id"]!.GetValue<string>()}|timeline|9|{duplicatePayload["sourceClassPath"]!.GetValue<string>()}");
        timeline.Add(duplicatePayload);

        var definition = Compile(root);
        var generic = definition.Animations[0].Timeline
            .Where(value => value.Kind is AlsCompiledTimelineEventKind.Generic).ToArray();

        Assert.Equal(2, generic.Length);
        Assert.NotEqual(generic[0].EventId, generic[1].EventId);
        Assert.Equal(generic[0].Payload, generic[1].Payload);
    }

    [Fact]
    public void CompilesSortedTimelineAndIntegerMontageBindings()
    {
        var definition = Compile(ValidRoot());
        var animation = Assert.Single(definition.Animations);
        var montage = Assert.Single(definition.Montages);

        Assert.Equal(new[] { 0.1f, 0.2f, 0.25f, 0.3f, 0.4f },
            animation.Timeline.Select(value => value.TimeSeconds));
        Assert.Equal(0, montage.Sections[0].SectionId);
        Assert.Equal(-1, montage.Sections[0].NextSectionId);
        Assert.Equal(0, montage.Slots[0].SlotId);
        Assert.Equal(0, montage.Slots[0].Segments[0].SegmentId);
        Assert.Equal(animation.Id, montage.Slots[0].Segments[0].AnimationId);
        Assert.Single(montage.Timeline);
        Assert.Equal(montage.Id, montage.Timeline[0].SourceAssetId);
    }

    [Fact]
    public void CompiledArraysAreImmutableSnapshots()
    {
        var definition = Compile(ValidRoot());
        var timeline = definition.Animations[0].Timeline;
        var markers = definition.Animations[0].SyncMarkers;
        var sections = definition.Montages[0].Sections;
        var slots = definition.Montages[0].Slots;
        var segments = slots[0].Segments;

        timeline[0] = timeline[^1];
        markers[0] = markers[^1];
        sections[0] = sections[0] with { SectionId = 99 };
        slots[0] = slots[0] with { SlotId = 99 };
        segments[0] = segments[0] with { SegmentId = 99 };

        Assert.NotEqual(timeline[0], definition.Animations[0].Timeline[0]);
        Assert.NotEqual(markers[0], definition.Animations[0].SyncMarkers[0]);
        Assert.Equal(0, definition.Montages[0].Sections[0].SectionId);
        Assert.Equal(0, definition.Montages[0].Slots[0].SlotId);
        Assert.Equal(0, definition.Montages[0].Slots[0].Segments[0].SegmentId);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("descending")]
    public void RejectsDuplicateOrDescendingMontageSectionTimes(string mutation)
    {
        var root = RootWithTwoSections();
        var sections = root["montages"]![0]!["metadata"]!["sections"]!;
        sections[1]!["startTime"] = mutation == "duplicate" ? 0.0 : -0.1;

        AssertIssue(root, "$.montages[0].metadata.sections[1].startTime");
    }

    [Fact]
    public void ResolvesSectionLinksAndMapsOnlyEmptyTerminalLinkToMinusOne()
    {
        var definition = Compile(RootWithTwoSections());
        var sections = definition.Montages[0].Sections;

        Assert.Equal(1, sections[0].NextSectionId);
        Assert.Equal(-1, sections[1].NextSectionId);
    }

    [Fact]
    public void RejectsUnknownNonEmptySectionLink()
    {
        var root = ValidRoot();
        root["montages"]![0]!["metadata"]!["sections"]![0]!["nextSection"] = "Missing";

        AssertIssue(root, "$.montages[0].metadata.sections[0].nextSection");
    }

    [Fact]
    public void RejectsUnresolvedSegmentAnimationReference()
    {
        var root = ValidRoot();
        root["montages"]![0]!["metadata"]!["slots"]![0]!["segments"]![0]!["animationId"] = new string('f', 40);

        AssertIssue(root, "$.montages[0].metadata.slots[0].segments[0].animationId");
    }

    [Theory]
    [InlineData("zero-rate")]
    [InlineData("non-finite-rate")]
    [InlineData("zero-loops")]
    [InlineData("empty-range")]
    [InlineData("range-past-clip")]
    [InlineData("mapped-past-montage")]
    public void RejectsInvalidMontageSegmentPlayback(string mutation)
    {
        var root = ValidRoot();
        var segment = root["montages"]![0]!["metadata"]!["slots"]![0]!["segments"]![0]!;
        switch (mutation)
        {
            case "zero-rate": segment["playRate"] = 0.0; break;
            case "non-finite-rate": segment["playRate"] = JsonNode.Parse("1e39"); break;
            case "zero-loops": segment["loopCount"] = 0; break;
            case "empty-range": segment["animationEndTime"] = 0.0; break;
            case "range-past-clip": segment["animationEndTime"] = 1.1; break;
            case "mapped-past-montage": segment["loopCount"] = 2; break;
        }

        AssertManifestRejected(root);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void RejectsMontageTimelineOutsideMontageBounds(double time)
    {
        var root = ValidRoot();
        root["montages"]![0]!["metadata"]!["timeline"]![0]!["timeSeconds"] = time;

        AssertIssue(root, "$.montages[0].metadata.timeline[0].timeSeconds");
    }

    [Fact]
    public void PayloadRoundTripPreservesEveryP5aField()
    {
        var original = Compile(ValidRoot());
        var json = AlsAnimationSetPayload.Serialize(original);
        var restored = AlsAnimationSetPayload.Deserialize(json);

        Assert.Equal(original.DefinitionDigest, restored.DefinitionDigest);
        Assert.Equal(original.Animations[0].Curves[0].PreInfinity, restored.Animations[0].Curves[0].PreInfinity);
        Assert.Equal(original.Animations[0].Curves[0].PostInfinity, restored.Animations[0].Curves[0].PostInfinity);
        Assert.Equal(original.Animations[0].Timeline, restored.Animations[0].Timeline);
        Assert.Equal(original.Animations[0].SyncMarkers, restored.Animations[0].SyncMarkers);
        Assert.Equal(original.Montages[0].Timeline, restored.Montages[0].Timeline);
        Assert.Equal(original.Montages[0].Sections, restored.Montages[0].Sections);
        Assert.Equal(original.Montages[0].Slots[0].SlotId, restored.Montages[0].Slots[0].SlotId);
        Assert.Equal(original.Montages[0].Slots[0].SlotName, restored.Montages[0].Slots[0].SlotName);
        Assert.Equal(original.Montages[0].Slots[0].Segments, restored.Montages[0].Slots[0].Segments);

        var events = restored.Animations.SelectMany(value => value.Timeline)
            .Concat(restored.Montages.SelectMany(value => value.Timeline));
        Assert.All(events, value => Assert.Equal(value.EventId,
            restored.AssetIndex.GetEventId(value.StableEventId)));
        Assert.All(restored.Animations.SelectMany(value => value.SyncMarkers),
            value => Assert.Equal(value.MarkerId, restored.AssetIndex.GetMarkerId(value.StableMarkerId)));
    }

    [Theory]
    [InlineData("animation-count")]
    [InlineData("animation-large")]
    [InlineData("start-negative")]
    [InlineData("start-past-montage")]
    [InlineData("source-start-negative")]
    [InlineData("source-start-past-clip")]
    [InlineData("source-end-past-clip")]
    [InlineData("source-reversed")]
    [InlineData("mapped-past-montage")]
    [InlineData("mapped-float-overflow")]
    public void PayloadRejectsInvalidMontageSegmentReferencesAndRanges(string mutation)
    {
        var root = SerializedPayloadRoot();
        var segment = root["montages"]![0]!["slots"]![0]!["segments"]![0]!.AsObject();
        switch (mutation)
        {
            case "animation-count":
                segment["animationId"] = root["animations"]!.AsArray().Count;
                break;
            case "animation-large":
                segment["animationId"] = 999;
                break;
            case "start-negative":
                segment["startPosition"] = -0.01f;
                break;
            case "start-past-montage":
                segment["startPosition"] = 1.01f;
                break;
            case "source-start-negative":
                segment["animationStartTime"] = -0.01f;
                break;
            case "source-start-past-clip":
                segment["animationStartTime"] = 1.01f;
                segment["animationEndTime"] = 1.02f;
                break;
            case "source-end-past-clip":
                segment["animationEndTime"] = 1.01f;
                break;
            case "source-reversed":
                segment["animationStartTime"] = 0.75f;
                segment["animationEndTime"] = 0.25f;
                break;
            case "mapped-past-montage":
                segment["startPosition"] = 0.5f;
                break;
            case "mapped-float-overflow":
                segment["loopCount"] = int.MaxValue;
                segment["playRate"] = float.Epsilon;
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        AssertPayloadRejected(root, "$.montages[0].slots[0].segments[0]");
    }

    [Theory]
    [InlineData("event-forged")]
    [InlineData("event-uppercase")]
    [InlineData("event-short")]
    [InlineData("event-owner-mismatch")]
    [InlineData("event-preimage-mismatch")]
    [InlineData("montage-event-owner-mismatch")]
    [InlineData("marker-forged")]
    [InlineData("marker-uppercase")]
    [InlineData("marker-short")]
    [InlineData("marker-owner-mismatch")]
    public void PayloadRejectsNonCanonicalStableTimelineAndMarkerIds(string mutation)
    {
        var root = SerializedPayloadRoot();
        var animation = root["animations"]![0]!.AsObject();
        var timelineValue = animation["timeline"]![0]!.AsObject();
        var markerValue = animation["syncMarkers"]![0]!.AsObject();
        switch (mutation)
        {
            case "event-forged":
                timelineValue["stableEventId"] = new string('0', 40);
                break;
            case "event-uppercase":
                timelineValue["stableEventId"] = timelineValue["stableEventId"]!.GetValue<string>().ToUpperInvariant();
                break;
            case "event-short":
                timelineValue["stableEventId"] = new string('a', 39);
                break;
            case "event-owner-mismatch":
                timelineValue["stableEventId"] = Sha1(
                    $"{new string('0', 40)}|timeline|{timelineValue["sourceIndex"]!.GetValue<int>()}|{timelineValue["sourceClassPath"]!.GetValue<string>()}");
                break;
            case "event-preimage-mismatch":
                timelineValue["sourceIndex"] = timelineValue["sourceIndex"]!.GetValue<int>() + 100;
                break;
            case "montage-event-owner-mismatch":
                var montageTimelineValue = root["montages"]![0]!["timeline"]![0]!.AsObject();
                montageTimelineValue["stableEventId"] = Sha1(
                    $"{animation["stableId"]!.GetValue<string>()}|timeline|{montageTimelineValue["sourceIndex"]!.GetValue<int>()}|{montageTimelineValue["sourceClassPath"]!.GetValue<string>()}");
                break;
            case "marker-forged":
                markerValue["stableMarkerId"] = new string('0', 40);
                break;
            case "marker-uppercase":
                markerValue["stableMarkerId"] = markerValue["stableMarkerId"]!.GetValue<string>().ToUpperInvariant();
                break;
            case "marker-short":
                markerValue["stableMarkerId"] = new string('a', 39);
                break;
            case "marker-owner-mismatch":
                markerValue["stableMarkerId"] = Sha1(
                    $"{new string('0', 40)}|marker|{markerValue["sourceIndex"]!.GetValue<int>()}|{markerValue["name"]!.GetValue<string>()}");
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        var expectedPath = mutation switch
        {
            "montage-event-owner-mismatch" => "$.montages[0].timeline[0]",
            _ when mutation.StartsWith("event", StringComparison.Ordinal) => "$.animations[0].timeline[0]",
            _ => "$.animations[0].syncMarkers[0]",
        };
        AssertPayloadRejected(root, expectedPath);
    }

    [Theory]
    [InlineData("within-sequence")]
    [InlineData("sequence-to-montage")]
    public void PayloadRejectsEventIdsThatAreNotGlobalStableIdOrdinals(string mutation)
    {
        var root = SerializedPayloadRoot();
        var animationTimeline = root["animations"]![0]!["timeline"]!.AsArray();
        var left = animationTimeline[0]!.AsObject();
        var right = mutation is "within-sequence"
            ? animationTimeline[1]!.AsObject()
            : root["montages"]![0]!["timeline"]![0]!.AsObject();
        SwapIntegerProperty(left, right, "eventId");

        AssertPayloadRejected(root, "timeline");
    }

    [Fact]
    public void PayloadRejectsMarkerIdsThatAreNotGlobalStableIdOrdinals()
    {
        var root = SerializedPayloadRoot();
        var markers = root["animations"]![0]!["syncMarkers"]!.AsArray();
        SwapIntegerProperty(markers[0]!.AsObject(), markers[1]!.AsObject(), "markerId");

        AssertPayloadRejected(root, "syncMarkers");
    }

    [Theory]
    [InlineData("event-id")]
    [InlineData("event-stable")]
    [InlineData("marker-id")]
    [InlineData("marker-stable")]
    public void PayloadContinuesToRejectDuplicateTimelineAndMarkerIdentities(string mutation)
    {
        var root = SerializedPayloadRoot();
        var timeline = root["animations"]![0]!["timeline"]!.AsArray();
        var markers = root["animations"]![0]!["syncMarkers"]!.AsArray();
        switch (mutation)
        {
            case "event-id":
                timeline[1]!["eventId"] = timeline[0]!["eventId"]!.GetValue<int>();
                break;
            case "event-stable":
                timeline[1]!["stableEventId"] = timeline[0]!["stableEventId"]!.GetValue<string>();
                break;
            case "marker-id":
                markers[1]!["markerId"] = markers[0]!["markerId"]!.GetValue<int>();
                break;
            case "marker-stable":
                markers[1]!["stableMarkerId"] = markers[0]!["stableMarkerId"]!.GetValue<string>();
                break;
            default:
                throw new InvalidOperationException(mutation);
        }

        AssertPayloadRejected(root, mutation.StartsWith("event", StringComparison.Ordinal)
            ? "timeline" : "syncMarkers");
    }

    [Fact]
    public void PayloadDeserializeRejectsValuesOutsideTheCompiledEventKind()
    {
        var definition = Compile(ValidRoot());
        var root = JsonNode.Parse(AlsAnimationSetPayload.Serialize(definition))!.AsObject();
        var generic = root["animations"]![0]!["timeline"]!.AsArray()
            .Single(value => value!["kind"]!.GetValue<int>() == (int)AlsCompiledTimelineEventKind.Generic)!;
        generic["payload"]!["foot"] = (int)AlsCompiledTimelineFoot.Right;

        var exception = Assert.Throws<InvalidDataException>(() =>
            AlsAnimationSetPayload.Deserialize(root.ToJsonString()));
        Assert.Contains("payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefinitionDigestIsIndependentOfPayloadJsonPropertyOrder()
    {
        var original = Compile(ValidRoot());
        var root = JsonNode.Parse(AlsAnimationSetPayload.Serialize(original))!.AsObject();
        var reordered = new JsonObject();
        foreach (var property in root.Reverse())
        {
            reordered.Add(property.Key, property.Value?.DeepClone());
        }

        var restored = AlsAnimationSetPayload.Deserialize(reordered.ToJsonString());

        Assert.Equal(original.DefinitionDigest, AlsAnimationSetPayload.ComputeDefinitionDigest(restored));
    }

    [Fact]
    public void EveryTimelineMarkerAndMontageFieldChangesDefinitionDigest()
    {
        var original = Compile(RootWithTwoSections());
        var eventValue = original.Animations[0].Timeline[2];
        var marker = original.Animations[0].SyncMarkers[0];
        var section = original.Montages[0].Sections[0];
        var slot = original.Montages[0].Slots[0];
        var segment = slot.Segments[0];
        var mutations = new List<(string Name, Func<AlsAnimationSetDefinition, AlsAnimationSetDefinition> Mutate)>
        {
            ("event.id", value => ReplaceEvent(value, eventValue with { EventId = eventValue.EventId + 100 })),
            ("event.stable", value => ReplaceEvent(value, eventValue with { StableEventId = new string('f', 40) })),
            ("event.kind", value => ReplaceEvent(value, eventValue with { Kind = AlsCompiledTimelineEventKind.Generic })),
            ("event.sourceAsset", value => ReplaceEvent(value, eventValue with { SourceAssetId = eventValue.SourceAssetId + 1 })),
            ("event.class", value => ReplaceEvent(value, eventValue with { SourceClassPath = eventValue.SourceClassPath + "_Changed" })),
            ("event.display", value => ReplaceEvent(value, eventValue with { DisplayName = eventValue.DisplayName + " Changed" })),
            ("event.time", value => ReplaceEvent(value, eventValue with { TimeSeconds = NextFloat(eventValue.TimeSeconds) })),
            ("event.duration", value => ReplaceEvent(value, eventValue with { DurationSeconds = -0.0f })),
            ("event.threshold", value => ReplaceEvent(value, eventValue with { TriggerWeightThreshold = NextFloat(eventValue.TriggerWeightThreshold) })),
            ("event.tick", value => ReplaceEvent(value, eventValue with { TickMode = AlsCompiledTimelineTickMode.Queued })),
            ("event.sourceIndex", value => ReplaceEvent(value, eventValue with { SourceIndex = eventValue.SourceIndex + 10 })),
            ("event.trackIndex", value => ReplaceEvent(value, eventValue with { TrackIndex = eventValue.TrackIndex + 10 })),
            ("marker.id", value => ReplaceMarker(value, marker with { MarkerId = marker.MarkerId + 100 })),
            ("marker.stable", value => ReplaceMarker(value, marker with { StableMarkerId = new string('e', 40) })),
            ("marker.name", value => ReplaceMarker(value, marker with { Name = marker.Name + "Changed" })),
            ("marker.time", value => ReplaceMarker(value, marker with { TimeSeconds = NextFloat(marker.TimeSeconds) })),
            ("marker.source", value => ReplaceMarker(value, marker with { SourceIndex = marker.SourceIndex + 10 })),
            ("marker.track", value => ReplaceMarker(value, marker with { TrackIndex = marker.TrackIndex + 10 })),
            ("section.id", value => ReplaceSection(value, section with { SectionId = section.SectionId + 10 })),
            ("section.name", value => ReplaceSection(value, section with { Name = section.Name + "Changed" })),
            ("section.next", value => ReplaceSection(value, section with { NextSectionId = -1 })),
            ("section.start", value => ReplaceSection(value, section with { StartTime = -0.0f })),
            ("slot.id", value => ReplaceSlot(value, slot with { SlotId = slot.SlotId + 10 })),
            ("slot.name", value => ReplaceSlot(value, slot with { SlotName = slot.SlotName + "Changed" })),
            ("segment.id", value => ReplaceSegment(value, segment with { SegmentId = segment.SegmentId + 10 })),
            ("segment.animation", value => ReplaceSegment(value, segment with { AnimationId = segment.AnimationId + 10 })),
            ("segment.start", value => ReplaceSegment(value, segment with { StartPosition = -0.0f })),
            ("segment.animationStart", value => ReplaceSegment(value, segment with { AnimationStartTime = -0.0f })),
            ("segment.animationEnd", value => ReplaceSegment(value, segment with { AnimationEndTime = NextFloat(segment.AnimationEndTime) })),
            ("segment.rate", value => ReplaceSegment(value, segment with { PlayRate = NextFloat(segment.PlayRate) })),
            ("segment.loops", value => ReplaceSegment(value, segment with { LoopCount = segment.LoopCount + 1 })),
            ("montage.timeline", value => value with { Montages = [value.Montages[0] with { Timeline = [] }] }),
        };

        AssertDigestMutations(original, mutations);
    }

    [Fact]
    public void EveryCompiledPayloadSourceFieldChangesDefinitionDigest()
    {
        var original = Compile(ValidRoot());
        var events = original.Animations[0].Timeline.Concat(original.Montages[0].Timeline).ToArray();
        var early = events.Single(value => value.Kind is AlsCompiledTimelineEventKind.EarlyBlendOut);
        var foot = events.Single(value => value.Kind is AlsCompiledTimelineEventKind.Footstep);
        var action = events.Single(value => value.Kind is AlsCompiledTimelineEventKind.SetAction);
        var grounded = events.Single(value => value.Kind is AlsCompiledTimelineEventKind.SetGroundedEntry);
        var rootMotion = events.Single(value => value.Kind is AlsCompiledTimelineEventKind.RootMotionScale);
        var mutations = new List<(string, Func<AlsAnimationSetDefinition, AlsAnimationSetDefinition>)>
        {
            ("foot", value => ReplaceAnyEvent(value, foot with { Payload = foot.Payload with { Foot = AlsCompiledTimelineFoot.Right } })),
            ("action", value => ReplaceAnyEvent(value, action with { Payload = action.Payload with { Action = AlsCompiledTimelineAction.Mantling } })),
            ("grounded", value => ReplaceAnyEvent(value, grounded with { Payload = grounded.Payload with { GroundedEntryMode = AlsCompiledTimelineGroundedEntryMode.None } })),
            ("blend", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { BlendOutSeconds = NextFloat(early.Payload.BlendOutSeconds) } })),
            ("checkInput", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { CheckInput = !early.Payload.CheckInput } })),
            ("checkLocomotion", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { CheckLocomotionMode = !early.Payload.CheckLocomotionMode } })),
            ("locomotion", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { LocomotionMode = AlsCompiledTimelineLocomotionMode.InAir } })),
            ("checkRotation", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { CheckRotationMode = !early.Payload.CheckRotationMode } })),
            ("rotation", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { RotationMode = AlsCompiledTimelineRotationMode.Aiming } })),
            ("checkStance", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { CheckStance = !early.Payload.CheckStance } })),
            ("stance", value => ReplaceAnyEvent(value, early with { Payload = early.Payload with { Stance = AlsCompiledTimelineStance.Crouching } })),
            ("translation", value => ReplaceAnyEvent(value, rootMotion with { Payload = rootMotion.Payload with { TranslationScale = -0.0f } })),
        };

        AssertDigestMutations(original, mutations);
    }

    [Fact]
    public void DefinitionDigestDistinguishesSignedZeroAndDistinctNanBits()
    {
        var original = Compile(ValidRoot());
        var zeroEvent = original.Animations[0].Timeline.First(value => value.DurationSeconds == 0f);
        var positiveZero = ReplaceAnyEvent(CloneDefinition(original), zeroEvent with { DurationSeconds = 0.0f });
        var negativeZero = ReplaceAnyEvent(CloneDefinition(original), zeroEvent with { DurationSeconds = -0.0f });

        Assert.NotEqual(
            AlsAnimationSetPayload.ComputeDefinitionDigest(positiveZero),
            AlsAnimationSetPayload.ComputeDefinitionDigest(negativeZero));

        var firstNan = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001));
        var secondNan = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002));
        Assert.True(float.IsNaN(firstNan));
        Assert.True(float.IsNaN(secondNan));
        var firstNanDefinition = ReplaceFirstCurveKeyValue(CloneDefinition(original), firstNan);
        var secondNanDefinition = ReplaceFirstCurveKeyValue(CloneDefinition(original), secondNan);

        Assert.NotEqual(
            AlsAnimationSetPayload.ComputeDefinitionDigest(firstNanDefinition),
            AlsAnimationSetPayload.ComputeDefinitionDigest(secondNanDefinition));
    }

    private static AlsAnimationSetDefinition ReplaceEvent(
        AlsAnimationSetDefinition definition, AlsCompiledTimelineEventDefinition replacement)
    {
        var clip = definition.Animations[0];
        return definition with
        {
            Animations = [clip with { Timeline = clip.Timeline.Select(value =>
                value.StableEventId == replacement.StableEventId || value.EventId == replacement.EventId ||
                value.EventId == replacement.EventId - 100
                    ? replacement : value).ToArray() }],
        };
    }

    private static AlsAnimationSetDefinition ReplaceAnyEvent(
        AlsAnimationSetDefinition definition, AlsCompiledTimelineEventDefinition replacement)
    {
        for (var index = 0; index < definition.Animations.Length; index++)
        {
            var clip = definition.Animations[index];
            if (clip.Timeline.Any(value => value.EventId == replacement.EventId))
            {
                var animations = definition.Animations.ToArray();
                animations[index] = clip with { Timeline = clip.Timeline.Select(value =>
                    value.EventId == replacement.EventId ? replacement : value).ToArray() };
                return definition with { Animations = animations };
            }
        }
        var montages = definition.Montages.ToArray();
        montages[0] = montages[0] with { Timeline = montages[0].Timeline.Select(value =>
            value.EventId == replacement.EventId ? replacement : value).ToArray() };
        return definition with { Montages = montages };
    }

    private static AlsAnimationSetDefinition ReplaceFirstCurveKeyValue(
        AlsAnimationSetDefinition definition, float replacement)
    {
        var animations = definition.Animations.ToArray();
        var animation = animations[0];
        var curves = animation.Curves;
        var curve = curves[0];
        var keys = curve.Keys;
        keys[0] = keys[0] with { Value = replacement };
        curves[0] = curve with { Keys = keys };
        animations[0] = animation with { Curves = curves };
        return definition with { Animations = animations };
    }

    private static AlsAnimationSetDefinition ReplaceMarker(
        AlsAnimationSetDefinition definition, AlsAnimationSyncMarkerDefinition replacement)
    {
        var clip = definition.Animations[0];
        return definition with { Animations = [clip with { SyncMarkers = clip.SyncMarkers.Select(value =>
            value.StableMarkerId == replacement.StableMarkerId || value.MarkerId == replacement.MarkerId ||
            value.MarkerId == replacement.MarkerId - 100
                ? replacement : value).ToArray() }] };
    }

    private static AlsAnimationSetDefinition ReplaceSection(
        AlsAnimationSetDefinition definition, AlsMontageSectionDefinition replacement)
    {
        var montage = definition.Montages[0];
        return definition with { Montages = [montage with { Sections = montage.Sections.Select(value =>
            value.SectionId == replacement.SectionId || value.SectionId == replacement.SectionId - 10
                ? replacement : value).ToArray() }] };
    }

    private static AlsAnimationSetDefinition ReplaceSlot(
        AlsAnimationSetDefinition definition, AlsMontageSlotDefinition replacement)
    {
        var montage = definition.Montages[0];
        return definition with { Montages = [montage with { Slots = montage.Slots.Select(value =>
            value.SlotId == replacement.SlotId || value.SlotId == replacement.SlotId - 10
                ? replacement : value).ToArray() }] };
    }

    private static AlsAnimationSetDefinition ReplaceSegment(
        AlsAnimationSetDefinition definition, AlsMontageSegmentDefinition replacement)
    {
        var montage = definition.Montages[0];
        var slot = montage.Slots[0];
        var changed = slot with { Segments = slot.Segments.Select(value =>
            value.SegmentId == replacement.SegmentId || value.SegmentId == replacement.SegmentId - 10
                ? replacement : value).ToArray() };
        return definition with { Montages = [montage with { Slots = [changed] }] };
    }

    private static void AssertDigestMutations(
        AlsAnimationSetDefinition original,
        IEnumerable<(string Name, Func<AlsAnimationSetDefinition, AlsAnimationSetDefinition> Mutate)> mutations)
    {
        var originalDigest = AlsAnimationSetPayload.ComputeDefinitionDigest(original);
        foreach (var (name, mutate) in mutations)
        {
            var fresh = CloneDefinition(original);
            var before = JsonNode.Parse(AlsAnimationSetPayload.Serialize(fresh));
            var changed = mutate(fresh);
            var after = JsonNode.Parse(AlsAnimationSetPayload.Serialize(changed));
            Assert.False(string.Equals(originalDigest,
                AlsAnimationSetPayload.ComputeDefinitionDigest(changed), StringComparison.Ordinal), name);
            Assert.Equal(originalDigest, AlsAnimationSetPayload.ComputeDefinitionDigest(original));
            Assert.Equal(originalDigest, AlsAnimationSetPayload.ComputeDefinitionDigest(fresh));
            var differenceCount = CountJsonDifferences(before, after);
            Assert.True(differenceCount == 1, $"{name} changed {differenceCount} serialized fields.");
        }
    }

    private static AlsAnimationSetDefinition CloneDefinition(AlsAnimationSetDefinition definition) =>
        AlsAnimationSetPayload.Deserialize(AlsAnimationSetPayload.Serialize(definition));

    private static int CountJsonDifferences(JsonNode? left, JsonNode? right)
    {
        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            return leftObject.Select(value => value.Key).Union(rightObject.Select(value => value.Key), StringComparer.Ordinal)
                .Sum(key => CountJsonDifferences(leftObject[key], rightObject[key]));
        }
        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            var shared = Math.Min(leftArray.Count, rightArray.Count);
            return Enumerable.Range(0, shared).Sum(index => CountJsonDifferences(leftArray[index], rightArray[index])) +
                Math.Abs(leftArray.Count - rightArray.Count);
        }
        return JsonNode.DeepEquals(left, right) ? 0 : 1;
    }

    private static JsonObject SerializedPayloadRoot() =>
        JsonNode.Parse(AlsAnimationSetPayload.Serialize(Compile(ValidRoot())))!.AsObject();

    private static void AssertPayloadRejected(JsonObject root, string expectedPath)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            AlsAnimationSetPayload.Deserialize(root.ToJsonString()));
        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
    }

    private static void SwapIntegerProperty(JsonObject left, JsonObject right, string propertyName)
    {
        var value = left[propertyName]!.GetValue<int>();
        left[propertyName] = right[propertyName]!.GetValue<int>();
        right[propertyName] = value;
    }

    private static JsonObject RootWithTwoSections()
    {
        var root = ValidRoot();
        root["montages"]![0]!["metadata"]!["sections"] = JsonNode.Parse("""
            [
              { "name": "Default", "nextSection": "Follow", "startTime": 0.0 },
              { "name": "Follow", "nextSection": "", "startTime": 0.5 }
            ]
            """);
        return root;
    }

    private static JsonObject ValidRoot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "p5a_typed_timeline_manifest.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var completeFixture = JsonNode.Parse(File.ReadAllText(AlsManifestSerializerTests.FixturePath()))!.AsObject();
        root["skeletons"]![0]!["metadata"] = completeFixture["skeletons"]![0]!["metadata"]!.DeepClone();
        root["animations"]![0]!["metadata"]!["curves"] =
            completeFixture["animations"]![0]!["metadata"]!["curves"]!.DeepClone();
        foreach (var sectionName in new[] { "animations", "montages" })
        {
            foreach (var assetNode in (JsonArray)root[sectionName]!)
            {
                var asset = assetNode!.AsObject();
                var assetId = asset["id"]!.GetValue<string>();
                foreach (var eventNode in (JsonArray)asset["metadata"]!["timeline"]!)
                {
                    var value = eventNode!.AsObject();
                    value["stableEventId"] = Sha1(
                        $"{assetId}|timeline|{value["sourceIndex"]!.GetValue<int>()}|{value["sourceClassPath"]!.GetValue<string>()}");
                }
            }
        }
        foreach (var assetNode in (JsonArray)root["animations"]!)
        {
            var asset = assetNode!.AsObject();
            var assetId = asset["id"]!.GetValue<string>();
            foreach (var markerNode in (JsonArray)asset["metadata"]!["syncMarkers"]!)
            {
                var value = markerNode!.AsObject();
                value["stableMarkerId"] = Sha1(
                    $"{assetId}|marker|{value["sourceIndex"]!.GetValue<int>()}|{value["name"]!.GetValue<string>()}");
            }
        }
        return root;
    }

    private static AlsAnimationSetDefinition Compile(JsonObject root) =>
        AlsAnimationSetCompiler.Compile(AlsManifestSerializer.Deserialize(root.ToJsonString()));

    private static void AssertIssue(JsonObject root, string path)
    {
        var exception = Assert.Throws<AlsCompilationException>(() => Compile(root));
        Assert.Contains(exception.Issues, value => value.FieldPath == path);
    }

    private static void AssertManifestRejected(JsonObject root)
    {
        var exception = Record.Exception(() => Compile(root));
        Assert.True(exception is JsonException or AlsCompilationException,
            $"Expected manifest rejection, got: {exception}");
    }

    private static string Sha1(string value) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static float NextFloat(float value) =>
        BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + 1);

    private static void Reverse(JsonArray values)
    {
        var reversed = values.Select(value => value!.DeepClone()).Reverse().ToArray();
        values.Clear();
        foreach (var value in reversed)
        {
            values.Add(value);
        }
    }
}
