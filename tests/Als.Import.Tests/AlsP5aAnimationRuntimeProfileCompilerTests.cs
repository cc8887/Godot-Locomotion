using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsP5aAnimationRuntimeProfileCompilerTests
{
    private const string LeftTransition = "3e23712571d6bbea8744fc94dad0904a6a0a0b5d";
    private const string RightTransition = "97d46bf9858376893c1c34a128c27044b4467d82";
    private const string BasePose = "621a81bf492cb9120b45cfd91b685854afb7dc75";
    private const string RollMontage = "2d9341182885d90ad666fff32c025937438b1827";
    private const string RollSequence = "39eecd72ffdddb8ba0eb2bb0683f1c958d68fdd9";

    [Fact]
    public void RepositoryProfileCompilesToStrictNumericImmutableRuntimeData()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = Compile(ReadProfile(), set);

        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal(Enumerable.Range(0, 6), profile.EventSemantics.Select(value => value.SemanticId));
        Assert.Equal(Enum.GetValues<AlsCompiledTimelineEventKind>(),
            profile.EventSemantics.Select(value => value.Kind));
        Assert.Equal(1f, profile.AllowTransitions.MissingValue);
        Assert.Equal(0f, profile.AllowTransitions.ClampMinimum);
        Assert.Equal(1f, profile.AllowTransitions.ClampMaximum);
        Assert.Equal(AlsP5CurveCombineMode.AdditiveToDefault, profile.AllowTransitions.CombineMode);
        Assert.Equal(set.Animations.Length, profile.AllowTransitions.AnimationCurveIds.Length);
        Assert.Single(profile.SyncGroups);
        Assert.Equal(17, profile.SyncGroups[0].Members.Length);
        Assert.Equal(4, profile.DynamicTransition.Slots.Length);
        Assert.Single(profile.Actions);
        Assert.Single(profile.Actions[0].Sections);
        Assert.Equal(0, profile.Actions[0].Sections[0].SectionId);
        Assert.Equal(-1, profile.Actions[0].Sections[0].NextSectionId);
        Assert.Single(profile.SegmentBindings);
        Assert.NotEmpty(profile.TimelineEntries);
        Assert.Equal(set.AssetIndex.GetMontageId(RollMontage), profile.Actions[0].MontageId);
        Assert.Equal(set.AssetIndex.GetAnimationId(RollSequence), profile.SegmentBindings[0].AnimationId);
        Assert.Equal(set.Montages[profile.Actions[0].MontageId].PlayLength,
            profile.Actions[0].MontageDurationSeconds);
        var montageEntries = profile.TimelineEntries
            .Where(value => value.SourceKind == AlsCompiledActionTimelineSourceKind.Montage).ToArray();
        Assert.Equal(set.Montages[profile.Actions[0].MontageId].Timeline.Length, montageEntries.Length);
        Assert.All(montageEntries, value =>
        {
            Assert.Equal(profile.Actions[0].DefinitionId, value.ActionDefinitionId);
            Assert.Equal(profile.Actions[0].MontageId, value.SourceAssetId);
            Assert.Equal(-1, value.SegmentBindingIndex);
            Assert.Equal(0, value.BoundaryOrdinal);
        });
        Assert.Equal(profile.TimelineEntries.OrderBy(value => value.ActionDefinitionId)
            .ThenBy(value => value.TimeSeconds)
            .ThenBy(value => value.SourceKind)
            .ThenBy(value => value.SegmentBindingIndex)
            .ThenBy(value => value.EventId)
            .ThenBy(value => value.SourceIndex)
            .ThenBy(value => value.BoundaryOrdinal)
            .ThenBy(value => value.TrackIndex), profile.TimelineEntries);
        Assert.All(profile.TimelineEntries, value =>
            Assert.Equal(value.SourceKind == AlsCompiledActionTimelineSourceKind.Montage
                    ? set.Montages[value.SourceAssetId].Timeline.Single(source => source.EventId == value.EventId).SourceIndex
                    : set.Animations[value.SourceAssetId].Timeline.Single(source => source.EventId == value.EventId).SourceIndex,
                value.SourceIndex));
        Assert.DoesNotContain(profile.GetType().Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(AlsP5aAnimationRuntimeProfile).Namespace &&
                               (type.Name.StartsWith("AlsP5", StringComparison.Ordinal) ||
                                type.Name.StartsWith("AlsCompiledAction", StringComparison.Ordinal)))
                .SelectMany(type => type.GetProperties()),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void RejectsInvalidJsonSchemaUnknownMissingAndDuplicatePropertiesWithPaths()
    {
        AssertIssue("{", "$");
        AssertMutation(root => root["schemaVersion"] = 2, "$.schemaVersion");
        AssertMutation(root => root["unknown"] = true, "$.unknown");
        AssertMutation(root => root.Remove("actions"), "$.actions");
        var duplicate = ReadProfile().Replace("\"schemaVersion\": 1", "\"schemaVersion\":1,\"schemaVersion\":1",
            StringComparison.Ordinal);
        AssertIssue(duplicate, "$.schemaVersion");
        AssertMutation(root => root["actions"]![0]!["priority"] = "100", "$.actions[0].priority");
    }

    [Fact]
    public void RequiresExactSixEventSemanticsAndFrozenCurvePolicy()
    {
        AssertMutation(root => root["eventSemantics"]!.AsArray().RemoveAt(5), "$.eventSemantics");
        AssertMutation(root => root["eventSemantics"]![1]!["kind"] = "Generic", "$.eventSemantics[1].kind");
        AssertMutation(root => root["eventSemantics"]![1]!["semanticId"] = 0, "$.eventSemantics[1].semanticId");
        AssertMutation(root => root["curveSemantics"]!["allowTransitions"]!["sourceName"] = "AllowTransitions",
            "$.curveSemantics.allowTransitions.sourceName");
        AssertMutation(root => root["curveSemantics"]!["allowTransitions"]!["blendMode"] = "Replace",
            "$.curveSemantics.allowTransitions.blendMode");
        AssertMutation(root => root["curveSemantics"]!["allowTransitions"]!["missingValue"] = 0,
            "$.curveSemantics.allowTransitions.missingValue");
        AssertMutation(root => root["curveSemantics"]!["allowTransitions"]!["clampMaximum"] = 2,
            "$.curveSemantics.allowTransitions.clampMaximum");

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var compiled = Compile(ReadProfile(), set);
        for (var animationId = 0; animationId < set.Animations.Length; animationId++)
        {
            var matches = set.Animations[animationId].Curves
                .Where(value => value.SourceName == "Enable_Transition").ToArray();
            Assert.Equal(matches.Length == 0 ? -1 : matches[0].CurveId,
                compiled.AllowTransitions.AnimationCurveIds[animationId]);
        }
        var duplicateCurveId = Array.FindIndex(set.Animations,
            value => value.Curves.Any(curve => curve.SourceName == "Enable_Transition"));
        var animations = set.Animations.ToArray();
        var curve = animations[duplicateCurveId].Curves.Single(value => value.SourceName == "Enable_Transition");
        animations[duplicateCurveId] = animations[duplicateCurveId] with
        {
            Curves = animations[duplicateCurveId].Curves.Append(curve with { CurveId = curve.CurveId + 100 }).ToArray(),
        };
        AssertIssue(ReadProfile(), "$.curveSemantics.allowTransitions", set with { Animations = animations });
    }

    [Fact]
    public void RequiresOneGroundedGroupExactMembersPoliciesSkeletonAndMarkerPairs()
    {
        AssertMutation(root => root["syncGroups"]![0]!["name"] = "grounded", "$.syncGroups[0].name");
        AssertMutation(root => root["syncGroups"]![0]!["leftMarker"] = "left", "$.syncGroups[0].leftMarker");
        AssertMutation(root => root["syncGroups"]![0]!["rightMarker"] = "right", "$.syncGroups[0].rightMarker");
        AssertMutation(root => root["syncGroups"] = new JsonArray(), "$.syncGroups");
        AssertMutation(root => root["syncGroups"]!.AsArray().Add(root["syncGroups"]![0]!.DeepClone()), "$.syncGroups");
        AssertMutation(root => root["syncGroups"]![0]!["members"]![0]!["loopPolicy"] = "Once",
            "$.syncGroups[0].members[0].loopPolicy");
        AssertMutation(root => root["syncGroups"]![0]!["members"]![0]!["canLead"] = false,
            "$.syncGroups[0].members[0].canLead");
        AssertMutation(root => root["syncGroups"]![0]!["members"]![0]!["animation"] = new string('f', 40),
            "$.syncGroups[0].members[0].animation");
        AssertMutation(root =>
        {
            var members = root["syncGroups"]![0]!["members"]!.AsArray();
            var first = members[0]!.DeepClone();
            var second = members[1]!.DeepClone();
            members[0] = second;
            members[1] = first;
        }, "$.syncGroups[0].members");
        AssertMutation(root => root["syncGroups"]![0]!["members"]![1]!["animation"] =
            root["syncGroups"]![0]!["members"]![0]!["animation"]!.DeepClone(),
            "$.syncGroups[0].members[1].animation");

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var firstId = set.AssetIndex.GetAnimationId(
            JsonNode.Parse(ReadProfile())!["syncGroups"]![0]!["members"]![0]!["animation"]!.GetValue<string>());
        var animations = set.Animations.ToArray();
        animations[firstId] = animations[firstId] with { SkeletonId = animations[firstId].SkeletonId + 1 };
        AssertIssue(ReadProfile(), "$.syncGroups[0].members", set with { Animations = animations });

        animations = set.Animations.ToArray();
        animations[firstId] = animations[firstId] with { SyncMarkers = [] };
        AssertIssue(ReadProfile(), "$.syncGroups[0].members[0]", set with { Animations = animations });
        animations = set.Animations.ToArray();
        var marker = animations[firstId].SyncMarkers.First(value => value.Name == "Left");
        animations[firstId] = animations[firstId] with { SyncMarkers = animations[firstId].SyncMarkers.Append(marker).ToArray() };
        AssertIssue(ReadProfile(), "$.syncGroups[0].members[0]", set with { Animations = animations });
    }

    [Fact]
    public void CompilesOnlyExactFourFiniteAdditiveTransitionBindings()
    {
        AssertMutation(root => root["dynamicTransition"]!["slots"]!.AsArray().RemoveAt(3),
            "$.dynamicTransition.slots");
        AssertMutation(root => root["dynamicTransition"]!["slots"]![1]!["foot"] = "Left",
            "$.dynamicTransition.slots[1]");
        AssertMutation(root => root["dynamicTransition"]!["distanceMeters"] = 0,
            "$.dynamicTransition.distanceMeters");
        AssertMutation(root => root["dynamicTransition"]!["playRate"] = 1e100,
            "$.dynamicTransition.playRate");
        AssertMutation(root => root["dynamicTransition"]!["cooldownFrames"] = -1,
            "$.dynamicTransition.cooldownFrames");

        var set = P3RepositoryFixtures.LoadAnimationSet();
        foreach (var stableId in new[] { LeftTransition, RightTransition })
        {
            var id = set.AssetIndex.GetAnimationId(stableId);
            var animations = set.Animations.ToArray();
            animations[id] = animations[id] with { AdditiveType = 0 };
            AssertIssue(ReadProfile(), "$.dynamicTransition.slots", set with { Animations = animations });
            animations = set.Animations.ToArray();
            animations[id] = animations[id] with { AdditiveBasePoseAnimationId = id };
            AssertIssue(ReadProfile(), "$.dynamicTransition.slots", set with { Animations = animations });
        }
        var baseId = set.AssetIndex.GetAnimationId(BasePose);
        var crossSkeletonBase = set.Animations.ToArray();
        crossSkeletonBase[baseId] = crossSkeletonBase[baseId] with
        {
            SkeletonId = crossSkeletonBase[baseId].SkeletonId + 1,
        };
        AssertIssue(ReadProfile(), "$.dynamicTransition.slots", set with { Animations = crossSkeletonBase });
        var additiveBase = set.Animations.ToArray();
        additiveBase[baseId] = additiveBase[baseId] with { AdditiveType = 1 };
        AssertIssue(ReadProfile(), "$.dynamicTransition.slots", set with { Animations = additiveBase });
        var compiled = Compile(ReadProfile(), set);
        Assert.All(compiled.DynamicTransition.Slots, slot =>
            Assert.Equal(set.AssetIndex.GetAnimationId(BasePose), slot.AdditiveBaseAnimationId));
    }

    [Fact]
    public void RejectsMalformedActionIdentitySettingsAndDemoReferences()
    {
        AssertMutation(root => root["actions"]!.AsArray().Add(root["actions"]![0]!.DeepClone()),
            "$.actions[1].name");
        AssertMutation(root => root["actions"]![0]!["montage"] = new string('f', 40),
            "$.actions[0].montage");
        AssertMutation(root => root["actions"]![0]!["slot"] = "Missing", "$.actions[0].slot");
        AssertMutation(root => root["actions"]![0]!["startSection"] = "Missing", "$.actions[0].startSection");
        AssertMutation(root => root["actions"]![0]!["priority"] = -1, "$.actions[0].priority");
        AssertMutation(root => root["actions"]![0]!["playRate"] = 0, "$.actions[0].playRate");
        AssertMutation(root => root["actions"]![0]!["blendSeconds"] = -1, "$.actions[0].blendSeconds");
        AssertMutation(root => root["actions"]![0]!["loopPolicy"] = "Forever", "$.actions[0].loopPolicy");
        AssertMutation(root => root["demoCases"]!["transitionFoot"] = "Missing", "$.demoCases.transitionFoot");
        AssertMutation(root => root["demoCases"]!["transitionStance"] = "Missing", "$.demoCases.transitionStance");
        AssertMutation(root => root["demoCases"]!["rollAction"] = "Missing", "$.demoCases.rollAction");
    }

    [Fact]
    public void ValidatesMontageSectionsSlotsSegmentsRangesFormulaAndRollSequenceContract()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var montageId = set.AssetIndex.GetMontageId(RollMontage);
        var montage = set.Montages[montageId];
        AssertMontageRejected(set, montage with { Slots = montage.Slots.Append(montage.Slots[0]).ToArray() }, "$.actions[0].slot");
        AssertMontageRejected(set, montage with { Sections = [montage.Sections[0] with { NextSectionId = 0 }] }, "$.actions[0].startSection");
        var twoSections = montage with
        {
            Sections =
            [
                new AlsMontageSectionDefinition(0, "Default", 1, 0f),
                new AlsMontageSectionDefinition(1, "Follow", -1, 0.75f),
            ],
        };
        var loopJson = JsonNode.Parse(ReadProfile())!.AsObject();
        loopJson["actions"]![0]!["loopPolicy"] = "Loop";
        AssertIssue(loopJson.ToJsonString(), "$.actions[0].startSection",
            ReplaceMontage(set, twoSections));
        var nonStartCycle = montage with
        {
            Sections =
            [
                new AlsMontageSectionDefinition(0, "Default", 1, 0f),
                new AlsMontageSectionDefinition(1, "Middle", 2, 0.5f),
                new AlsMontageSectionDefinition(2, "Cycle", 1, 1f),
            ],
        };
        AssertIssue(loopJson.ToJsonString(), "$.actions[0].startSection",
            ReplaceMontage(set, nonStartCycle));
        AssertMontageRejected(set, montage with
        {
            Sections =
            [
                new AlsMontageSectionDefinition(0, "Default", -1, 0f),
                new AlsMontageSectionDefinition(1, "Uncovered", -1, montage.PlayLength),
            ],
        }, "$.actions[0]");
        var slot = montage.Slots[0];
        AssertMontageRejected(set, montage with { Slots = [slot with { Segments = [] }] }, "$.actions[0]");
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationId = int.MaxValue });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationEndTime = 0f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { StartPosition = -0.1f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { StartPosition = montage.PlayLength + 0.1f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationStartTime = -0.1f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationEndTime = 2f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationEndTime = float.NaN });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { PlayRate = 0f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { LoopCount = 0 });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { AnimationEndTime = 1.25f });
        AssertSegmentRejected(set, montage, slot.Segments[0] with { LoopCount = int.MaxValue });

        var emptyTimeline = set.Animations.ToArray();
        var rollSequenceId = set.AssetIndex.GetAnimationId(RollSequence);
        emptyTimeline[rollSequenceId] = emptyTimeline[rollSequenceId] with { Timeline = [] };
        AssertIssue(ReadProfile(), "$.actions[0]", ReplaceMontage(
            set with { Animations = emptyTimeline },
            montage with
            {
                Slots = [slot with { Segments = [slot.Segments[0] with { LoopCount = int.MaxValue }] }],
            }));

        var invalidDuration = set.Animations.ToArray();
        invalidDuration[rollSequenceId] = invalidDuration[rollSequenceId] with { PlayLength = float.NaN };
        AssertIssue(ReadProfile(), "$.actions[0]", set with { Animations = invalidDuration });

        var sequenceId = set.AssetIndex.GetAnimationId(RollSequence);
        var animations = set.Animations.ToArray();
        animations[sequenceId] = animations[sequenceId] with { AdditiveType = 1 };
        AssertIssue(ReadProfile(), "$.actions[0]", set with { Animations = animations });
        animations = set.Animations.ToArray();
        animations[sequenceId] = animations[sequenceId] with { SkeletonId = animations[sequenceId].SkeletonId + 1 };
        AssertIssue(ReadProfile(), "$.actions[0]", set with { Animations = animations });
    }

    [Fact]
    public void FlattensClippedMultiLoopSequenceEventsInMontageCoordinatesWithStableOrdinals()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var montageId = set.AssetIndex.GetMontageId(RollMontage);
        var sequenceId = set.AssetIndex.GetAnimationId(RollSequence);
        var montage = set.Montages[montageId];
        var segment = montage.Slots[0].Segments[0] with
        {
            StartPosition = 0f,
            AnimationStartTime = 0.5f,
            AnimationEndTime = 1f,
            PlayRate = 1f,
            LoopCount = 2,
        };
        var montages = set.Montages.ToArray();
        montages[montageId] = montage with
        {
            PlayLength = 1f,
            Sections = [new AlsMontageSectionDefinition(0, "Default", -1, 0f)],
            Slots = [montage.Slots[0] with { Segments = [segment] }],
        };
        var generic = set.Animations[sequenceId].Timeline[0];
        var animations = set.Animations.ToArray();
        animations[sequenceId] = animations[sequenceId] with
        {
            Timeline =
            [
                generic with { EventId = 1000, TimeSeconds = 0.5f, DurationSeconds = 0f, SourceIndex = 0 },
                generic with { EventId = 1001, TimeSeconds = 1f, DurationSeconds = 0f, SourceIndex = 1 },
                generic with { EventId = 1002, TimeSeconds = 0.25f, DurationSeconds = 1f, SourceIndex = 2 },
            ],
        };

        var profile = Compile(ReadProfile(), set with { Animations = animations, Montages = montages });
        var sequenceEntries = profile.TimelineEntries
            .Where(value => value.SourceKind == AlsCompiledActionTimelineSourceKind.Sequence).ToArray();

        Assert.Equal(6, sequenceEntries.Length);
        Assert.Equal(new[] { 1, 1, 2, 1, 2, 2 }, sequenceEntries.Select(value => value.BoundaryOrdinal));
        Assert.Equal(new[] { 0f, 0f, 0.5f, 0.5f, 0.5f, 1f }, sequenceEntries.Select(value => value.TimeSeconds));
        Assert.Equal(new[] { 0f, 0.5f, 0f, 0f, 0.5f, 0f }, sequenceEntries.Select(value => value.DurationSeconds));
        Assert.All(sequenceEntries, value => Assert.Equal(0, value.SegmentBindingIndex));
        Assert.Equal(1f, profile.SegmentBindings[0].MontageEndTime);
    }

    [Fact]
    public void ValidatesSegmentSpanUsingDoublePrecisionOperands()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var montageId = set.AssetIndex.GetMontageId(RollMontage);
        var montage = set.Montages[montageId];
        var segment = montage.Slots[0].Segments[0] with
        {
            StartPosition = 0.1f,
            AnimationStartTime = 0.1f,
            AnimationEndTime = 1f,
            PlayRate = 1f,
            LoopCount = 1,
        };
        var replacement = montage with
        {
            PlayLength = 1f,
            Sections = [montage.Sections[0] with { StartTime = 0.1f }],
            Slots = [montage.Slots[0] with { Segments = [segment] }],
        };

        var profile = Compile(ReadProfile(), ReplaceMontage(set, replacement));

        Assert.Equal(0.1f, profile.SegmentBindings[0].MontageStartTime);
        Assert.Equal(1f, profile.SegmentBindings[0].MontageEndTime);
    }

    [Fact]
    public void HandlesEmptyHugeLoopsInConstantTimeAndBoundsNonemptyExpansion()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var montageId = set.AssetIndex.GetMontageId(RollMontage);
        var sequenceId = set.AssetIndex.GetAnimationId(RollSequence);
        var montage = set.Montages[montageId];
        const int hugeLoopCount = int.MaxValue - 1;
        var segment = montage.Slots[0].Segments[0] with
        {
            AnimationStartTime = 0f,
            AnimationEndTime = 1f,
            PlayRate = 2147483648f,
            LoopCount = hugeLoopCount,
        };
        var replacement = montage with
        {
            PlayLength = 1f,
            Slots = [montage.Slots[0] with { Segments = [segment] }],
        };
        var emptyAnimations = set.Animations.ToArray();
        emptyAnimations[sequenceId] = emptyAnimations[sequenceId] with { Timeline = [] };

        var empty = Compile(ReadProfile(), ReplaceMontage(
            set with { Animations = emptyAnimations }, replacement));

        Assert.DoesNotContain(empty.TimelineEntries,
            value => value.SourceKind == AlsCompiledActionTimelineSourceKind.Sequence);

        var oneEventAnimations = emptyAnimations.ToArray();
        oneEventAnimations[sequenceId] = oneEventAnimations[sequenceId] with
        {
            Timeline = [set.Animations[sequenceId].Timeline[0]],
        };
        AssertIssue(ReadProfile(), "$.actions[0]", ReplaceMontage(
            set with { Animations = oneEventAnimations }, replacement));
    }

    [Fact]
    public void UsesExactInstantRangeAndDoesNotExtendStateNearTheSourceEnd()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var montageId = set.AssetIndex.GetMontageId(RollMontage);
        var sequenceId = set.AssetIndex.GetAnimationId(RollSequence);
        var montage = set.Montages[montageId];
        const float sourceStart = 1e-7f;
        const float sourceEnd = 2e-7f;
        var mappedLength = sourceEnd - sourceStart;
        var segment = montage.Slots[0].Segments[0] with
        {
            StartPosition = 0f,
            AnimationStartTime = sourceStart,
            AnimationEndTime = sourceEnd,
            PlayRate = 1f,
            LoopCount = 1,
        };
        var montages = set.Montages.ToArray();
        montages[montageId] = montage with
        {
            PlayLength = mappedLength,
            Sections = [new AlsMontageSectionDefinition(0, "Default", -1, 0f)],
            Slots = [montage.Slots[0] with { Segments = [segment] }],
        };
        var source = set.Animations[sequenceId].Timeline[0];
        var animations = set.Animations.ToArray();
        animations[sequenceId] = animations[sequenceId] with
        {
            Timeline =
            [
                source with { EventId = 1000, TimeSeconds = float.BitDecrement(sourceStart), DurationSeconds = 0f },
                source with { EventId = 1001, TimeSeconds = sourceStart, DurationSeconds = 0f },
                source with { EventId = 1002, TimeSeconds = sourceEnd, DurationSeconds = 0f },
                source with { EventId = 1003, TimeSeconds = float.BitIncrement(sourceEnd), DurationSeconds = 0f },
                source with { EventId = 1004, TimeSeconds = 0f, DurationSeconds = float.BitDecrement(sourceEnd) },
            ],
        };

        var profile = Compile(ReadProfile(), set with { Animations = animations, Montages = montages });
        var entries = profile.TimelineEntries.Where(value => value.SourceKind ==
            AlsCompiledActionTimelineSourceKind.Sequence).ToArray();

        Assert.Equal(new[] { 1001, 1004, 1002 }, entries.Select(value => value.EventId));
        Assert.Equal(0f, entries.Single(value => value.EventId == 1001).TimeSeconds);
        Assert.Equal(mappedLength, entries.Single(value => value.EventId == 1002).TimeSeconds);
        Assert.True(entries.Single(value => value.EventId == 1004).DurationSeconds < mappedLength);
    }

    [Fact]
    public void KeepsActionsContiguousAndUsesFrozenTimelineTieKeys()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var second = root["actions"]![0]!.DeepClone();
        second!["name"] = "RollAlternate";
        root["actions"]!.AsArray().Add(second);
        var set = P3RepositoryFixtures.LoadAnimationSet();

        var profile = Compile(root.ToJsonString(), set);

        Assert.Equal(2, profile.Actions.Length);
        Assert.Equal(new[] { 0, 1 }, profile.TimelineEntries
            .Select(value => value.ActionDefinitionId).Distinct());
        Assert.Equal(profile.TimelineEntries.OrderBy(value => value.ActionDefinitionId)
            .ThenBy(value => value.TimeSeconds)
            .ThenBy(value => value.SourceKind)
            .ThenBy(value => value.SegmentBindingIndex)
            .ThenBy(value => value.EventId)
            .ThenBy(value => value.SourceIndex)
            .ThenBy(value => value.BoundaryOrdinal)
            .ThenBy(value => value.TrackIndex), profile.TimelineEntries);
        Assert.All(profile.TimelineEntries.Where(value => value.SourceKind ==
            AlsCompiledActionTimelineSourceKind.Montage), value =>
            Assert.Equal(profile.Actions[value.ActionDefinitionId].MontageId, value.SourceAssetId));
    }

    [Fact]
    public void AppliesTimelineBudgetCumulativelyAcrossActionsBeforeFlattening()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var second = root["actions"]![0]!.DeepClone();
        second!["name"] = "RollAlternate";
        root["actions"]!.AsArray().Add(second);
        var set = P3RepositoryFixtures.LoadAnimationSet();

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsP5aAnimationRuntimeProfileCompiler.Compile(root.ToJsonString(), set, 7));

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath.StartsWith("$.actions[1]", StringComparison.Ordinal) &&
            issue.Message.Contains("timeline", StringComparison.OrdinalIgnoreCase));
        var boundary = AlsP5aAnimationRuntimeProfileCompiler.Compile(root.ToJsonString(), set, 8);
        Assert.Equal(8, boundary.TimelineEntries.Length);
        Assert.Equal(new[] { 0, 1 }, boundary.TimelineEntries
            .Select(value => value.ActionDefinitionId).Distinct());
    }

    [Fact]
    public void ReturnedArraysAreDefensiveAndCompilationIsStable()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var first = Compile(ReadProfile(), set);
        var eventCopy = first.EventSemantics;
        eventCopy[0] = eventCopy[0] with { SemanticId = 99 };
        var curveCopy = first.AllowTransitions.AnimationCurveIds;
        curveCopy[0] = 999;
        var groupCopy = first.SyncGroups;
        groupCopy[0] = groupCopy[0] with { GroupId = 99 };
        var memberCopy = first.SyncGroups[0].Members;
        memberCopy[0] = memberCopy[0] with { AnimationId = -1 };
        var slotCopy = first.DynamicTransition.Slots;
        slotCopy[0] = slotCopy[0] with { AnimationId = -1 };
        var actionCopy = first.Actions;
        actionCopy[0] = actionCopy[0] with { DefinitionId = 99 };
        var sectionCopy = first.Actions[0].Sections;
        sectionCopy[0] = sectionCopy[0] with { SectionId = 99 };
        var segmentCopy = first.SegmentBindings;
        segmentCopy[0] = segmentCopy[0] with { SegmentId = 99 };
        var timelineCopy = first.TimelineEntries;
        timelineCopy[0] = timelineCopy[0] with { BoundaryOrdinal = 999 };
        var second = Compile(ReadProfile(), set);

        Assert.Equal(0, first.EventSemantics[0].SemanticId);
        Assert.NotEqual(999, first.AllowTransitions.AnimationCurveIds[0]);
        Assert.Equal(0, first.SyncGroups[0].GroupId);
        Assert.True(first.SyncGroups[0].Members[0].AnimationId >= 0);
        Assert.True(first.DynamicTransition.Slots[0].AnimationId >= 0);
        Assert.Equal(0, first.Actions[0].DefinitionId);
        Assert.Equal(0, first.Actions[0].Sections[0].SectionId);
        Assert.Equal(0, first.SegmentBindings[0].SegmentId);
        Assert.NotEqual(999, first.TimelineEntries[0].BoundaryOrdinal);
        Assert.Equal(first.EventSemantics, second.EventSemantics);
        Assert.Equal(first.AllowTransitions.AnimationCurveIds, second.AllowTransitions.AnimationCurveIds);
        Assert.Equal(first.SyncGroups.Select(value => value.GroupId),
            second.SyncGroups.Select(value => value.GroupId));
        Assert.Equal(first.SyncGroups[0].Members, second.SyncGroups[0].Members);
        Assert.Equal(first.DynamicTransition.Slots, second.DynamicTransition.Slots);
        Assert.Equal(first.Actions.Select(value => (
                value.DefinitionId, value.MontageId, value.MontageDurationSeconds, value.SlotId,
                value.StartSectionId, value.Priority, value.Interruptible, value.PlayRate,
                value.BlendSeconds, value.LoopPolicy)),
            second.Actions.Select(value => (
                value.DefinitionId, value.MontageId, value.MontageDurationSeconds, value.SlotId,
                value.StartSectionId, value.Priority, value.Interruptible, value.PlayRate,
                value.BlendSeconds, value.LoopPolicy)));
        Assert.Equal(first.Actions[0].Sections, second.Actions[0].Sections);
        Assert.Equal(first.SegmentBindings, second.SegmentBindings);
        Assert.Equal(first.TimelineEntries, second.TimelineEntries);
    }

    private static AlsP5aAnimationRuntimeProfile Compile(string json, AlsAnimationSetDefinition set) =>
        AlsP5aAnimationRuntimeProfileCompiler.Compile(json, set);

    private static string ReadProfile() => File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "assets", "config", "p5a_animation_runtime.json"));

    private static void AssertMutation(Action<JsonObject> mutate, string path)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        mutate(root);
        AssertIssue(root.ToJsonString(), path);
    }

    private static void AssertIssue(string json, string path, AlsAnimationSetDefinition? set = null)
    {
        var exception = Assert.Throws<AlsCompilationException>(() =>
            Compile(json, set ?? P3RepositoryFixtures.LoadAnimationSet()));
        Assert.Contains(exception.Issues, issue => issue.FieldPath == path || issue.FieldPath.StartsWith(path, StringComparison.Ordinal));
    }

    private static void AssertMontageRejected(
        AlsAnimationSetDefinition set, AlsMontageDefinition replacement, string path)
    {
        var montages = set.Montages.ToArray();
        montages[replacement.Id] = replacement;
        AssertIssue(ReadProfile(), path, set with { Montages = montages });
    }

    private static AlsAnimationSetDefinition ReplaceMontage(
        AlsAnimationSetDefinition set, AlsMontageDefinition replacement)
    {
        var montages = set.Montages.ToArray();
        montages[replacement.Id] = replacement;
        return set with { Montages = montages };
    }

    private static void AssertSegmentRejected(
        AlsAnimationSetDefinition set, AlsMontageDefinition montage, AlsMontageSegmentDefinition segment)
    {
        var slot = montage.Slots[0] with { Segments = [segment] };
        AssertMontageRejected(set, montage with { Slots = [slot] }, "$.actions[0]");
    }
}
