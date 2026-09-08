using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsP5aAnimationRuntimeProfileCompiler
{
    private const int SchemaVersion = 1;
    private const int MaximumExpandedTimelineEntries = 1_000_000;
    private const float Tolerance = 1e-8f;
    private const string LeftTransitionId = "3e23712571d6bbea8744fc94dad0904a6a0a0b5d";
    private const string RightTransitionId = "97d46bf9858376893c1c34a128c27044b4467d82";
    private const string TransitionBaseId = "621a81bf492cb9120b45cfd91b685854afb7dc75";

    private static readonly string[] MemberIds =
    [
        "21c24bd7df5192db2e2a860457f2b7b0681de41d",
        "245ea51e30449a60b7d2b783ec0f6d24ec3bacc0",
        "32fe18c71ccb860fe35c01d6b2b10fa2e4d98297",
        "44a7f89b2c1dac832ca63753c131a037420f9d7e",
        "572c3c83c9007964c233db4c7288ae38e20c3dec",
        "6124eafdcbeaaf04bca366add34c821faa0e4963",
        "859f8a49c55747e7382a1ae15970b23cc12f3f85",
        "8ae1b9703a7d0144e570247d88629530b376885f",
        "8bd6ad52ad04a2ad23b47187886c630701df5260",
        "8eb8837c9f32973628b83ed2b98f7d68a0e82aa9",
        "945bdda63e8a379694c792c3545fe11dd166b724",
        "a4c6e0e455e7be7355cdd7c3ce49272d07773b18",
        "b07a51bbab122c81679ac30d3f2f78f45ac14dc8",
        "db60b2c35ce5ef5216c782fc1f33549cbcf8278d",
        "eb84a748fee4615754ce3cbcd3c259b33918b935",
        "f9ec8804e251f03c57e04dde4db9bd921457bae4",
        "fc2d3a4142a1bd82d20877d806c783ff56dfe688",
    ];

    public static AlsP5aAnimationRuntimeProfile Compile(
        string json,
        AlsAnimationSetDefinition animationSet) =>
        Compile(json, animationSet, MaximumExpandedTimelineEntries);

    internal static AlsP5aAnimationRuntimeProfile Compile(
        string json,
        AlsAnimationSetDefinition animationSet,
        int maximumExpandedTimelineEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumExpandedTimelineEntries);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException exception)
        {
            throw Failure("ALSP5A001", "$", $"Invalid P5A profile JSON: {exception.Message}");
        }

        using (document)
        {
            var root = Object(document.RootElement, "$",
                "schemaVersion", "eventSemantics", "curveSemantics", "syncGroups",
                "dynamicTransition", "actions", "demoCases");
            var version = Integer(root["schemaVersion"], "$.schemaVersion");
            if (version != SchemaVersion)
            {
                throw Failure("ALSP5A002", "$.schemaVersion", "Unsupported P5A profile schema.");
            }

            var eventSemantics = CompileEventSemantics(root["eventSemantics"]);
            var allowTransitions = CompileCurveSemantic(root["curveSemantics"], animationSet);
            var syncGroups = CompileSyncGroups(root["syncGroups"], animationSet, out var skeletonId);
            var dynamicTransition = CompileTransitions(root["dynamicTransition"], animationSet, skeletonId);
            var (actions, sections, segments, timeline) = CompileActions(
                root["actions"], animationSet, skeletonId, maximumExpandedTimelineEntries);
            var demo = CompileDemo(root["demoCases"], dynamicTransition, actions,
                root["actions"]);

            return new AlsP5aAnimationRuntimeProfile(
                version,
                eventSemantics,
                allowTransitions,
                syncGroups,
                dynamicTransition,
                actions.Select((action, index) => action with { Sections = sections[index] }).ToArray(),
                segments,
                timeline,
                demo);
        }
    }

    private static AlsCompiledEventSemantic[] CompileEventSemantics(JsonElement element)
    {
        var values = Array(element, "$.eventSemantics");
        var expectedKinds = Enum.GetValues<AlsCompiledTimelineEventKind>();
        if (values.Length != expectedKinds.Length)
        {
            throw Failure("ALSP5A003", "$.eventSemantics", "Exactly six event semantics are required.");
        }

        var result = new AlsCompiledEventSemantic[expectedKinds.Length];
        var kinds = new HashSet<AlsCompiledTimelineEventKind>();
        var ids = new HashSet<int>();
        for (var index = 0; index < values.Length; index++)
        {
            var path = $"$.eventSemantics[{index}]";
            var source = Object(values[index], path, "kind", "semanticId");
            var kind = EventKind(String(source["kind"], $"{path}.kind"), $"{path}.kind");
            var semanticId = Integer(source["semanticId"], $"{path}.semanticId");
            if (!kinds.Add(kind) || kind != expectedKinds[index])
            {
                throw Failure("ALSP5A004", $"{path}.kind", "Event kinds must be unique and in frozen ordinal order.");
            }
            if (!ids.Add(semanticId) || semanticId != index)
            {
                throw Failure("ALSP5A005", $"{path}.semanticId", "Semantic IDs must be unique contiguous ordinals.");
            }
            result[index] = new AlsCompiledEventSemantic(kind, semanticId);
        }
        return result;
    }

    private static AlsCompiledCurveSemantic CompileCurveSemantic(
        JsonElement element,
        AlsAnimationSetDefinition set)
    {
        var curveSemantics = Object(element, "$.curveSemantics", "allowTransitions");
        var path = "$.curveSemantics.allowTransitions";
        var source = Object(curveSemantics["allowTransitions"], path,
            "sourceName", "missingValue", "blendMode", "clampMinimum", "clampMaximum");
        RequireToken(source["sourceName"], $"{path}.sourceName", "Enable_Transition");
        RequireToken(source["blendMode"], $"{path}.blendMode", "AdditiveToDefault");
        var missing = FiniteSingle(source["missingValue"], $"{path}.missingValue");
        var minimum = FiniteSingle(source["clampMinimum"], $"{path}.clampMinimum");
        var maximum = FiniteSingle(source["clampMaximum"], $"{path}.clampMaximum");
        if (missing != 1f || minimum != 0f || maximum != 1f)
        {
            var invalidPath = missing != 1f ? $"{path}.missingValue" :
                minimum != 0f ? $"{path}.clampMinimum" : $"{path}.clampMaximum";
            throw Failure("ALSP5A006", invalidPath, "The transition curve numeric policy is frozen to 1/[0,1].");
        }

        var bindings = new int[set.Animations.Length];
        for (var animationId = 0; animationId < set.Animations.Length; animationId++)
        {
            var matches = set.Animations[animationId].Curves
                .Where(value => string.Equals(value.SourceName, "Enable_Transition", StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw Failure("ALSP5A007", path,
                    "An animation contains duplicate Enable_Transition source curves.");
            }
            bindings[animationId] = matches.Length == 0 ? -1 : matches[0].CurveId;
        }
        return new AlsCompiledCurveSemantic(1f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f, bindings);
    }

    private static AlsCompiledSyncGroup[] CompileSyncGroups(
        JsonElement element,
        AlsAnimationSetDefinition set,
        out int skeletonId)
    {
        var groups = Array(element, "$.syncGroups");
        if (groups.Length != 1)
        {
            throw Failure("ALSP5A008", "$.syncGroups", "P5A requires exactly one Sync group.");
        }
        var groupPath = "$.syncGroups[0]";
        var group = Object(groups[0], groupPath, "name", "leftMarker", "rightMarker", "members");
        RequireToken(group["name"], $"{groupPath}.name", "Grounded");
        RequireToken(group["leftMarker"], $"{groupPath}.leftMarker", "Left");
        RequireToken(group["rightMarker"], $"{groupPath}.rightMarker", "Right");
        var members = Array(group["members"], $"{groupPath}.members");
        if (members.Length != MemberIds.Length)
        {
            throw Failure("ALSP5A009", $"{groupPath}.members", "Grounded requires the exact 17-member list.");
        }

        var result = new AlsCompiledSyncMember[members.Length];
        skeletonId = -1;
        for (var index = 0; index < members.Length; index++)
        {
            var path = $"{groupPath}.members[{index}]";
            var source = Object(members[index], path, "animation", "loopPolicy", "canLead");
            var stableId = String(source["animation"], $"{path}.animation");
            if (!string.Equals(stableId, MemberIds[index], StringComparison.Ordinal))
            {
                throw Failure("ALSP5A010", $"{path}.animation",
                    "Grounded members must use the literal frozen ordinal list.");
            }
            RequireToken(source["loopPolicy"], $"{path}.loopPolicy", "Loop");
            if (!Boolean(source["canLead"], $"{path}.canLead"))
            {
                throw Failure("ALSP5A011", $"{path}.canLead", "Every Grounded member must be leader-capable.");
            }
            var animationId = ResolveAnimation(set, stableId, $"{path}.animation");
            var animation = set.Animations[animationId];
            if (!float.IsFinite(animation.PlayLength) || animation.PlayLength <= 0f)
            {
                throw Failure("ALSP5A013", $"{path}.animation", "Grounded member duration must be finite and positive.");
            }
            if (skeletonId < 0) skeletonId = animation.SkeletonId;
            else if (animation.SkeletonId != skeletonId)
            {
                throw Failure("ALSP5A014", $"{path}.animation", "Grounded members must use one skeleton.");
            }
            var left = animation.SyncMarkers.Where(value => value.Name == "Left").ToArray();
            var right = animation.SyncMarkers.Where(value => value.Name == "Right").ToArray();
            if (left.Length != 1 || right.Length != 1 || left[0].MarkerId == right[0].MarkerId)
            {
                throw Failure("ALSP5A015", path, "Grounded member requires one unique Left/Right marker pair.");
            }
            result[index] = new AlsCompiledSyncMember(
                index, animationId, animation.PlayLength, left[0].MarkerId, right[0].MarkerId,
                AlsP5LoopPolicy.Loop, true);
        }
        return [new AlsCompiledSyncGroup(0, result)];
    }

    private static AlsCompiledDynamicTransition CompileTransitions(
        JsonElement element,
        AlsAnimationSetDefinition set,
        int skeletonId)
    {
        const string path = "$.dynamicTransition";
        var source = Object(element, path,
            "distanceMeters", "blendSeconds", "playRate", "cooldownFrames", "slots");
        var distance = Positive(source["distanceMeters"], $"{path}.distanceMeters");
        var blend = Positive(source["blendSeconds"], $"{path}.blendSeconds");
        var rate = Positive(source["playRate"], $"{path}.playRate");
        var cooldown = NonnegativeInteger(source["cooldownFrames"], $"{path}.cooldownFrames");
        var slots = Array(source["slots"], $"{path}.slots");
        if (slots.Length != 4)
        {
            throw Failure("ALSP5A016", $"{path}.slots", "Exactly four transition stance/foot slots are required.");
        }

        var expectedKeys = new[]
        {
            (AlsP5TransitionStance.Standing, AlsP5TransitionFoot.Left, LeftTransitionId),
            (AlsP5TransitionStance.Standing, AlsP5TransitionFoot.Right, RightTransitionId),
            (AlsP5TransitionStance.Crouching, AlsP5TransitionFoot.Left, LeftTransitionId),
            (AlsP5TransitionStance.Crouching, AlsP5TransitionFoot.Right, RightTransitionId),
        };
        var baseId = ResolveAnimation(set, TransitionBaseId, $"{path}.slots");
        if (set.Animations[baseId].SkeletonId != skeletonId)
        {
            throw Failure("ALSP5A018", $"{path}.slots",
                "Transition additive base is cross-skeleton.");
        }
        if (set.Animations[baseId].AdditiveType != 0)
        {
            throw Failure("ALSP5A019", $"{path}.slots",
                "Transition additive base must be non-additive.");
        }
        var result = new AlsCompiledTransitionSlot[slots.Length];
        var keys = new HashSet<(AlsP5TransitionStance, AlsP5TransitionFoot)>();
        for (var index = 0; index < slots.Length; index++)
        {
            var slotPath = $"{path}.slots[{index}]";
            var slot = Object(slots[index], slotPath, "stance", "foot", "animation");
            var stance = Stance(String(slot["stance"], $"{slotPath}.stance"), $"{slotPath}.stance");
            var foot = Foot(String(slot["foot"], $"{slotPath}.foot"), $"{slotPath}.foot");
            var stableId = String(slot["animation"], $"{slotPath}.animation");
            if (!keys.Add((stance, foot)) || stance != expectedKeys[index].Item1 ||
                foot != expectedKeys[index].Item2 || stableId != expectedKeys[index].Item3)
            {
                throw Failure("ALSP5A017", slotPath, "Transition slots must match the frozen four-key mapping.");
            }
            var animationId = ResolveAnimation(set, stableId, $"{slotPath}.animation");
            var animation = set.Animations[animationId];
            if (animation.SkeletonId != skeletonId)
            {
                throw Failure("ALSP5A018", $"{slotPath}.animation", "Transition clip is cross-skeleton.");
            }
            if (animation.AdditiveType != 2 || animation.AdditiveBasePoseType != 3 ||
                animation.AdditiveBasePoseAnimationId != baseId)
            {
                throw Failure("ALSP5A019", $"{path}.slots", "Transition clip has an invalid additive base contract.");
            }
            result[index] = new AlsCompiledTransitionSlot(index, stance, foot, animationId, baseId);
        }
        return new AlsCompiledDynamicTransition(distance, blend, rate, cooldown, result);
    }

    private static (
        AlsCompiledActionDefinition[] Actions,
        AlsCompiledActionSectionBinding[][] Sections,
        AlsCompiledActionSegmentBinding[] Segments,
        AlsCompiledActionTimelineEntry[] Timeline) CompileActions(
            JsonElement element,
            AlsAnimationSetDefinition set,
            int skeletonId,
            int maximumExpandedTimelineEntries)
    {
        var values = Array(element, "$.actions");
        if (values.Length == 0)
        {
            throw Failure("ALSP5A020", "$.actions", "At least one Action is required.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var actions = new AlsCompiledActionDefinition[values.Length];
        var sections = new AlsCompiledActionSectionBinding[values.Length][];
        var montages = new AlsMontageDefinition[values.Length];
        var segmentsByAction = new AlsCompiledActionSegmentBinding[values.Length][];
        var firstBindingIndices = new int[values.Length];
        var segmentList = new List<AlsCompiledActionSegmentBinding>();
        long expandedTimelineEntries = 0;

        for (var actionIndex = 0; actionIndex < values.Length; actionIndex++)
        {
            var path = $"$.actions[{actionIndex}]";
            var source = Object(values[actionIndex], path,
                "name", "montage", "slot", "startSection", "priority", "interruptible",
                "playRate", "blendSeconds", "loopPolicy");
            var name = NonemptyString(source["name"], $"{path}.name");
            if (!names.Add(name))
            {
                throw Failure("ALSP5A021", $"{path}.name", "Action names must be unique.");
            }
            var montageStableId = NonemptyString(source["montage"], $"{path}.montage");
            var montageId = ResolveMontage(set, montageStableId, $"{path}.montage");
            var montage = set.Montages[montageId];
            montages[actionIndex] = montage;
            if (!float.IsFinite(montage.PlayLength) || montage.PlayLength <= 0f)
            {
                throw Failure("ALSP5A022", path, "Action Montage duration must be finite and positive.");
            }
            var slotName = NonemptyString(source["slot"], $"{path}.slot");
            var matchingSlots = montage.Slots.Where(value => value.SlotName == slotName).ToArray();
            if (montage.Slots.Length != 1 || matchingSlots.Length != 1)
            {
                throw Failure("ALSP5A023", $"{path}.slot", "Action Montage must have exactly one selected slot.");
            }
            var startName = NonemptyString(source["startSection"], $"{path}.startSection");
            var matchingSections = montage.Sections.Where(value => value.Name == startName).ToArray();
            if (matchingSections.Length != 1)
            {
                throw Failure("ALSP5A024", $"{path}.startSection", "Action start section is missing or duplicate.");
            }
            var priority = NonnegativeInteger(source["priority"], $"{path}.priority");
            var interruptible = Boolean(source["interruptible"], $"{path}.interruptible");
            var playRate = Positive(source["playRate"], $"{path}.playRate");
            var blendSeconds = Positive(source["blendSeconds"], $"{path}.blendSeconds");
            var loopPolicy = LoopPolicy(String(source["loopPolicy"], $"{path}.loopPolicy"),
                $"{path}.loopPolicy");

            var sectionBindings = CompileSections(montage, actionIndex, path);
            ValidateSectionGraph(sectionBindings, matchingSections[0].SectionId, loopPolicy,
                $"{path}.startSection");
            sections[actionIndex] = sectionBindings;

            var firstBindingIndex = segmentList.Count;
            firstBindingIndices[actionIndex] = firstBindingIndex;
            var actionSegments = CompileSegments(
                matchingSlots[0], montage, actionIndex, path, set, skeletonId,
                ref expandedTimelineEntries, maximumExpandedTimelineEntries);
            ValidateSectionCoverage(sectionBindings, actionSegments, path);
            segmentsByAction[actionIndex] = actionSegments;
            segmentList.AddRange(actionSegments);

            AddTimelineBudget(
                ref expandedTimelineEntries,
                montage.Timeline.Length,
                maximumExpandedTimelineEntries,
                path);

            actions[actionIndex] = new AlsCompiledActionDefinition(
                actionIndex, montageId, montage.PlayLength, matchingSlots[0].SlotId,
                matchingSections[0].SectionId, priority, interruptible, playRate, blendSeconds,
                loopPolicy, [], CompileLifecycle(montage, path));
        }

        var timeline = new List<AlsCompiledActionTimelineEntry>((int)expandedTimelineEntries);
        for (var actionIndex = 0; actionIndex < values.Length; actionIndex++)
        {
            var montage = montages[actionIndex];
            foreach (var value in montage.Timeline)
            {
                timeline.Add(ToTimelineEntry(
                    actionIndex, value, AlsCompiledActionTimelineSourceKind.Montage,
                    actions[actionIndex].MontageId, -1, value.TimeSeconds, value.DurationSeconds, 0));
            }
            var actionSegments = segmentsByAction[actionIndex];
            var firstBindingIndex = firstBindingIndices[actionIndex];
            var path = $"$.actions[{actionIndex}]";
            for (var segmentOffset = 0; segmentOffset < actionSegments.Length; segmentOffset++)
            {
                var bindingIndex = firstBindingIndex + segmentOffset;
                FlattenSequenceTimeline(
                    actionIndex, bindingIndex, actionSegments[segmentOffset],
                    set.Animations[actionSegments[segmentOffset].AnimationId], timeline, path);
            }
        }

        return (actions, sections, segmentList.ToArray(), timeline
            .OrderBy(value => value.ActionDefinitionId)
            .ThenBy(value => value.TimeSeconds)
            .ThenBy(value => value.SourceKind)
            .ThenBy(value => value.SegmentBindingIndex)
            .ThenBy(value => value.EventId)
            .ThenBy(value => value.SourceIndex)
            .ThenBy(value => value.BoundaryOrdinal)
            .ThenBy(value => value.TrackIndex)
            .ToArray());
    }

    private static AlsActionLifecycleSettings CompileLifecycle(AlsMontageDefinition montage, string path)
    {
        if (!float.IsFinite(montage.BlendInTime) || montage.BlendInTime < 0f ||
            !float.IsFinite(montage.BlendOutTime) || montage.BlendOutTime < 0f ||
            !float.IsFinite(montage.BlendOutTriggerTime) ||
            montage.BlendInOption is < 0 or > 2 || montage.BlendOutOption is < 0 or > 2)
        {
            throw Failure("ALSP5A041", path, "Action Montage lifecycle settings are invalid or unsupported.");
        }
        return new AlsActionLifecycleSettings(
            montage.EnableAutoBlendOut ? AlsActionLifecycleMode.MontageAutoBlendOut : AlsActionLifecycleMode.MontageHoldAtEnd,
            montage.BlendInTime, (AlsActionBlendOption)montage.BlendInOption,
            montage.BlendOutTime, (AlsActionBlendOption)montage.BlendOutOption, montage.BlendOutTriggerTime);
    }

    private static AlsCompiledActionSectionBinding[] CompileSections(
        AlsMontageDefinition montage,
        int actionId,
        string path)
    {
        if (montage.Sections.Length == 0)
        {
            throw Failure("ALSP5A025", path, "Action Montage has no sections.");
        }
        var result = new AlsCompiledActionSectionBinding[montage.Sections.Length];
        for (var index = 0; index < montage.Sections.Length; index++)
        {
            var section = montage.Sections[index];
            var end = index + 1 < montage.Sections.Length ? montage.Sections[index + 1].StartTime : montage.PlayLength;
            if (section.SectionId != index || !float.IsFinite(section.StartTime) ||
                !float.IsFinite(end) || section.StartTime < 0f || end <= section.StartTime ||
                end > montage.PlayLength + Tolerance ||
                section.NextSectionId < -1 || section.NextSectionId >= montage.Sections.Length)
            {
                throw Failure("ALSP5A026", path, "Montage sections are invalid, unordered, or out of range.");
            }
            result[index] = new AlsCompiledActionSectionBinding(
                actionId, section.SectionId, section.NextSectionId, section.StartTime, end);
        }
        return result;
    }

    private static void ValidateSectionGraph(
        AlsCompiledActionSectionBinding[] sections,
        int startSectionId,
        AlsP5LoopPolicy loopPolicy,
        string path)
    {
        var seen = new HashSet<int>();
        var current = startSectionId;
        for (var step = 0; step <= sections.Length; step++)
        {
            if (current < 0)
            {
                if (loopPolicy == AlsP5LoopPolicy.Loop || seen.Count != sections.Length)
                {
                    throw Failure("ALSP5A027", path, "Action section graph terminates or leaves sections unreachable.");
                }
                return;
            }
            if (!seen.Add(current))
            {
                if (loopPolicy != AlsP5LoopPolicy.Loop || current != startSectionId || seen.Count != sections.Length)
                {
                    throw Failure("ALSP5A028", path, "Action section graph contains an invalid cycle.");
                }
                return;
            }
            current = sections[current].NextSectionId;
        }
        throw Failure("ALSP5A028", path, "Action section graph contains an invalid cycle.");
    }

    private static AlsCompiledActionSegmentBinding[] CompileSegments(
        AlsMontageSlotDefinition slot,
        AlsMontageDefinition montage,
        int actionId,
        string path,
        AlsAnimationSetDefinition set,
        int skeletonId,
        ref long expandedTimelineEntries,
        int maximumExpandedTimelineEntries)
    {
        if (slot.Segments.Length == 0)
        {
            throw Failure("ALSP5A029", path, "Action slot has no segments.");
        }
        var result = new AlsCompiledActionSegmentBinding[slot.Segments.Length];
        for (var index = 0; index < slot.Segments.Length; index++)
        {
            var segment = slot.Segments[index];
            var segmentPath = $"{path}.segments[{index}]";
            if (segment.SegmentId != index || (uint)segment.AnimationId >= (uint)set.Animations.Length)
            {
                throw Failure("ALSP5A030", path, "Action segment references an unresolved Sequence.");
            }
            var animation = set.Animations[segment.AnimationId];
            if (animation.SkeletonId != skeletonId)
            {
                throw Failure("ALSP5A031", path, "Action segment Sequence is cross-skeleton.");
            }
            if (animation.AdditiveType != 0)
            {
                throw Failure("ALSP5A032", path, "Action segment Sequence must be non-additive.");
            }
            if (!float.IsFinite(animation.PlayLength) || animation.PlayLength <= 0f)
            {
                throw Failure("ALSP5A033", segmentPath,
                    "Action segment Sequence duration must be finite and positive.");
            }
            if (!float.IsFinite(segment.StartPosition) || segment.StartPosition < 0f ||
                !float.IsFinite(segment.AnimationStartTime) || segment.AnimationStartTime < 0f ||
                !float.IsFinite(segment.AnimationEndTime) ||
                segment.AnimationEndTime <= segment.AnimationStartTime ||
                segment.AnimationEndTime > animation.PlayLength + Tolerance ||
                !float.IsFinite(segment.PlayRate) || segment.PlayRate <= 0f || segment.LoopCount <= 0)
            {
                throw Failure("ALSP5A033", segmentPath, "Action segment range/rate/loop data is invalid.");
            }
            if (segment.LoopCount == int.MaxValue)
            {
                throw Failure("ALSP5A034", segmentPath,
                    "Action segment BoundaryOrdinal cannot be incremented safely.");
            }
            long estimatedEntries;
            try { estimatedEntries = checked((long)segment.LoopCount * animation.Timeline.Length); }
            catch (OverflowException)
            {
                throw Failure("ALSP5A034", segmentPath,
                    "Action segment timeline expansion overflowed Int64.");
            }
            AddTimelineBudget(
                ref expandedTimelineEntries,
                estimatedEntries,
                maximumExpandedTimelineEntries,
                segmentPath);
            var mappedDuration = (double)segment.LoopCount *
                ((double)segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate;
            var exportedEnd = index + 1 < slot.Segments.Length
                ? slot.Segments[index + 1].StartPosition
                : montage.PlayLength;
            if (!double.IsFinite(mappedDuration) || !float.IsFinite(exportedEnd) ||
                exportedEnd <= segment.StartPosition || exportedEnd > montage.PlayLength + Tolerance ||
                Math.Abs(((double)exportedEnd - segment.StartPosition) - mappedDuration) > 1e-8)
            {
                throw Failure("ALSP5A035", segmentPath, "Action segment Montage/source mapping formula is invalid.");
            }
            result[index] = new AlsCompiledActionSegmentBinding(
                actionId, slot.SlotId, segment.SegmentId, segment.AnimationId,
                segment.StartPosition, exportedEnd, segment.AnimationStartTime,
                segment.AnimationEndTime, segment.PlayRate, segment.LoopCount);
        }
        return result;
    }

    private static void AddTimelineBudget(
        ref long expandedTimelineEntries,
        long additionalEntries,
        int maximumExpandedTimelineEntries,
        string path)
    {
        long total;
        try { total = checked(expandedTimelineEntries + additionalEntries); }
        catch (OverflowException)
        {
            throw Failure("ALSP5A034", path, "Action timeline expansion overflowed Int64.");
        }
        if (total > maximumExpandedTimelineEntries)
        {
            throw Failure("ALSP5A034", path,
                $"Compiled profile timeline expansion exceeds {maximumExpandedTimelineEntries} entries.");
        }
        expandedTimelineEntries = total;
    }

    private static void ValidateSectionCoverage(
        AlsCompiledActionSectionBinding[] sections,
        AlsCompiledActionSegmentBinding[] segments,
        string path)
    {
        foreach (var section in sections)
        {
            var cursor = section.StartTime;
            foreach (var segment in segments.OrderBy(value => value.MontageStartTime))
            {
                if (segment.MontageEndTime <= cursor + Tolerance) continue;
                if (segment.MontageStartTime > cursor + Tolerance) break;
                cursor = Math.Max(cursor, segment.MontageEndTime);
                if (cursor >= section.EndTime - Tolerance) break;
            }
            if (cursor < section.EndTime - Tolerance)
            {
                throw Failure("ALSP5A036", path, "Action section is not completely covered by resolvable segments.");
            }
        }
    }

    private static void FlattenSequenceTimeline(
        int actionId,
        int bindingIndex,
        AlsCompiledActionSegmentBinding segment,
        AlsAnimationDefinition animation,
        List<AlsCompiledActionTimelineEntry> output,
        string path)
    {
        if (animation.Timeline.Length == 0)
        {
            return;
        }
        var sourceRange = (double)segment.AnimationEndTime - segment.AnimationStartTime;
        var loopDuration = sourceRange / segment.PlayRate;
        for (var iteration = 0; iteration < segment.LoopCount; iteration++)
        {
            int ordinal;
            try { ordinal = checked(iteration + 1); }
            catch (OverflowException)
            {
                throw Failure("ALSP5A037", path, "Action segment BoundaryOrdinal overflowed Int32.");
            }
            foreach (var value in animation.Timeline)
            {
                if (value.DurationSeconds <= 0f)
                {
                    if (value.TimeSeconds < segment.AnimationStartTime ||
                        value.TimeSeconds > segment.AnimationEndTime) continue;
                    var time = MapTime(segment, loopDuration, iteration, value.TimeSeconds);
                    if (iteration == segment.LoopCount - 1 &&
                        value.TimeSeconds == segment.AnimationEndTime)
                    {
                        time = segment.MontageEndTime;
                    }
                    output.Add(ToTimelineEntry(
                        actionId, value, AlsCompiledActionTimelineSourceKind.Sequence,
                        animation.Id, bindingIndex, time, 0f, ordinal));
                    continue;
                }

                var begin = Math.Max((double)value.TimeSeconds, segment.AnimationStartTime);
                var end = Math.Min((double)value.TimeSeconds + value.DurationSeconds,
                    segment.AnimationEndTime);
                if (end <= begin) continue;
                var mappedBegin = MapTime(segment, loopDuration, iteration, begin);
                var mappedEnd = MapTime(segment, loopDuration, iteration, end);
                if (iteration == segment.LoopCount - 1 &&
                    end == segment.AnimationEndTime)
                {
                    mappedEnd = segment.MontageEndTime;
                }
                output.Add(ToTimelineEntry(
                    actionId, value, AlsCompiledActionTimelineSourceKind.Sequence,
                    animation.Id, bindingIndex, mappedBegin, mappedEnd - mappedBegin, ordinal));
            }
        }
    }

    private static float MapTime(
        AlsCompiledActionSegmentBinding segment,
        double loopDuration,
        int iteration,
        double sourceTime)
    {
        var mapped = segment.MontageStartTime + iteration * loopDuration +
            (sourceTime - segment.AnimationStartTime) / segment.PlayRate;
        if (!double.IsFinite(mapped) || mapped < segment.MontageStartTime - 1e-8 ||
            mapped > segment.MontageEndTime + 1e-8)
        {
            throw Failure("ALSP5A038", "$.actions", "Action timeline mapping produced an invalid Montage phase.");
        }
        return (float)mapped;
    }

    private static AlsCompiledActionTimelineEntry ToTimelineEntry(
        int actionId,
        AlsCompiledTimelineEventDefinition value,
        AlsCompiledActionTimelineSourceKind sourceKind,
        int sourceAssetId,
        int segmentBindingIndex,
        float time,
        float duration,
        int ordinal) =>
        new(actionId, value.EventId, value.Kind, sourceKind, sourceAssetId,
            segmentBindingIndex, value.SourceIndex, value.TrackIndex,
            time, duration, value.TriggerWeightThreshold,
            value.TickMode, value.Payload, ordinal);

    private static AlsCompiledDemoCases CompileDemo(
        JsonElement element,
        AlsCompiledDynamicTransition transitions,
        AlsCompiledActionDefinition[] actions,
        JsonElement actionsJson)
    {
        const string path = "$.demoCases";
        var source = Object(element, path, "transitionStance", "transitionFoot", "rollAction");
        var stance = Stance(String(source["transitionStance"], $"{path}.transitionStance"),
            $"{path}.transitionStance");
        var foot = Foot(String(source["transitionFoot"], $"{path}.transitionFoot"),
            $"{path}.transitionFoot");
        var matchingSlots = transitions.Slots.Where(value => value.Stance == stance && value.Foot == foot).ToArray();
        if (matchingSlots.Length != 1)
        {
            throw Failure("ALSP5A039", $"{path}.transitionStance", "Demo transition key is not declared.");
        }
        var actionName = String(source["rollAction"], $"{path}.rollAction");
        var actionSources = Array(actionsJson, "$.actions");
        var matches = actionSources.Select((value, index) => (value, index))
            .Where(pair => String(Object(pair.value, $"$.actions[{pair.index}]",
                    "name", "montage", "slot", "startSection", "priority", "interruptible",
                    "playRate", "blendSeconds", "loopPolicy")["name"], $"$.actions[{pair.index}].name") == actionName)
            .Select(pair => pair.index).ToArray();
        if (matches.Length != 1 || matches[0] >= actions.Length)
        {
            throw Failure("ALSP5A040", $"{path}.rollAction", "Demo Action reference is unresolved.");
        }
        return new AlsCompiledDemoCases(matchingSlots[0].SlotIndex, matches[0]);
    }

    private static Dictionary<string, JsonElement> Object(
        JsonElement element,
        string path,
        params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Failure("ALSP5A041", path, "Expected a JSON object.");
        }
        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (!expectedSet.Contains(property.Name))
            {
                throw Failure("ALSP5A042", propertyPath, "Unknown P5A profile property.");
            }
            if (!result.TryAdd(property.Name, property.Value))
            {
                throw Failure("ALSP5A043", propertyPath, "Duplicate JSON property is not allowed.");
            }
        }
        foreach (var name in expected)
        {
            if (!result.ContainsKey(name))
            {
                throw Failure("ALSP5A044", $"{path}.{name}", "Required P5A profile property is missing.");
            }
        }
        return result;
    }

    private static JsonElement[] Array(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Failure("ALSP5A045", path, "Expected a JSON array.");
        }
        return element.EnumerateArray().ToArray();
    }

    private static string String(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value)
        {
            throw Failure("ALSP5A046", path, "Expected a string.");
        }
        return value;
    }

    private static string NonemptyString(JsonElement element, string path)
    {
        var value = String(element, path);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Failure("ALSP5A047", path, "Expected a non-empty string.");
        }
        return value;
    }

    private static bool Boolean(JsonElement element, string path)
    {
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Failure("ALSP5A048", path, "Expected a boolean.");
        }
        return element.GetBoolean();
    }

    private static int Integer(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Failure("ALSP5A049", path, "Expected an Int32 value.");
        }
        return value;
    }

    private static int NonnegativeInteger(JsonElement element, string path)
    {
        var value = Integer(element, path);
        if (value < 0) throw Failure("ALSP5A050", path, "Expected a nonnegative integer.");
        return value;
    }

    private static float FiniteSingle(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) ||
            !float.IsFinite(value))
        {
            throw Failure("ALSP5A051", path, "Expected a finite single-precision value.");
        }
        return value;
    }

    private static float Positive(JsonElement element, string path)
    {
        var value = FiniteSingle(element, path);
        if (value <= 0f) throw Failure("ALSP5A052", path, "Expected a positive value.");
        return value;
    }

    private static void RequireToken(JsonElement element, string path, string expected)
    {
        if (!string.Equals(String(element, path), expected, StringComparison.Ordinal))
        {
            throw Failure("ALSP5A053", path, $"Expected exact token '{expected}'.");
        }
    }

    private static AlsCompiledTimelineEventKind EventKind(string value, string path) => value switch
    {
        "Generic" => AlsCompiledTimelineEventKind.Generic,
        "Footstep" => AlsCompiledTimelineEventKind.Footstep,
        "SetAction" => AlsCompiledTimelineEventKind.SetAction,
        "SetGroundedEntry" => AlsCompiledTimelineEventKind.SetGroundedEntry,
        "EarlyBlendOut" => AlsCompiledTimelineEventKind.EarlyBlendOut,
        "RootMotionScale" => AlsCompiledTimelineEventKind.RootMotionScale,
        _ => throw Failure("ALSP5A054", path, "Unsupported event semantic kind."),
    };

    private static AlsP5TransitionStance Stance(string value, string path) => value switch
    {
        "Standing" => AlsP5TransitionStance.Standing,
        "Crouching" => AlsP5TransitionStance.Crouching,
        _ => throw Failure("ALSP5A055", path, "Unsupported transition stance."),
    };

    private static AlsP5TransitionFoot Foot(string value, string path) => value switch
    {
        "Left" => AlsP5TransitionFoot.Left,
        "Right" => AlsP5TransitionFoot.Right,
        _ => throw Failure("ALSP5A056", path, "Unsupported transition foot."),
    };

    private static AlsP5LoopPolicy LoopPolicy(string value, string path) => value switch
    {
        "Once" => AlsP5LoopPolicy.Once,
        "Loop" => AlsP5LoopPolicy.Loop,
        _ => throw Failure("ALSP5A057", path, "Unsupported loop policy."),
    };

    private static int ResolveAnimation(AlsAnimationSetDefinition set, string stableId, string path)
    {
        try { return set.AssetIndex.GetAnimationId(stableId); }
        catch (KeyNotFoundException)
        {
            throw Failure("ALSP5A058", path, "Profile references an unknown animation stable ID.");
        }
    }

    private static int ResolveMontage(AlsAnimationSetDefinition set, string stableId, string path)
    {
        try { return set.AssetIndex.GetMontageId(stableId); }
        catch (KeyNotFoundException)
        {
            throw Failure("ALSP5A059", path, "Profile references an unknown Montage stable ID.");
        }
    }

    private static AlsCompilationException Failure(string code, string path, string message) =>
        new([new AlsValidationIssue(code, null, path, message, null, null)]);
}
