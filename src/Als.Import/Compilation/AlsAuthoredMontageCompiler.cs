using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

public static class AlsAuthoredMontageCompiler
{
    public static AlsMontageActionPolicy[] CompileRequests(AlsP5aAnimationRuntimeProfile profile,
        ReadOnlySpan<AlsAuthoredMontageAsset> assets)
    {
        Require(profile.Actions.Length == assets.Length, "Action request inventory differs from physical montages.");
        var result = new List<AlsMontageActionPolicy>();
        foreach (var action in profile.Actions)
        {
            var found = false; AlsAuthoredMontageAsset asset = default;
            foreach (var item in assets)
                if (item.ActionDefinitionId == action.DefinitionId) { asset = item; found = true; break; }
            var sections = action.Sections;
            Require(found && asset.MontageId == action.MontageId && asset.Duration == action.MontageDurationSeconds &&
                asset.Lifecycle == action.Lifecycle && action.LoopPolicy == AlsP5LoopPolicy.Once && sections.Length == 1 &&
                sections[0].ActionDefinitionId == action.DefinitionId && sections[0].SectionId == action.StartSectionId &&
                sections[0].NextSectionId == -1 && sections[0].StartTime == 0 && sections[0].EndTime == asset.Duration,
                "Request section/lifecycle differs from its physical montage.");
            result.Add(new(action.DefinitionId, action.StartSectionId, sections[0].StartTime, action.PlayRate,
                action.BlendSeconds, action.Interruptible));
        }
        var compiled = result.ToArray();
        _ = new AlsMontageActionRuntime(new AlsMontageRuntime([], assets), compiled);
        return compiled;
    }

    public static AlsAuthoredMontageAsset[] Compile(string nativeJson, string skeletonJson,
        AlsAnimationSetDefinition set, AlsP5aAnimationRuntimeProfile actions, int skeletonId)
    {
        using var data = JsonDocument.Parse(nativeJson);
        using var skeleton = JsonDocument.Parse(skeletonJson);
        Require(data.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            skeleton.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            skeleton.RootElement.GetProperty("skeleton").GetString() == set.Skeletons[skeletonId].ObjectPath, "Montage skeleton/provenance differs.");
        var entries = data.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Require(entries.Length == actions.Actions.Length && entries.Select(a => a.GetProperty("path").GetString()).Distinct().Count() == entries.Length,
            "Authored montage inventory differs from action profile.");
        var groups = new Dictionary<string, (int Id, string Name)>(StringComparer.Ordinal);
        foreach (Match group in Regex.Matches(skeleton.RootElement.GetProperty("skeletonText").GetString()!, @"(?m)^   SlotGroups\((\d+)\)=\(([^\r\n]+)\)"))
        {
            var name = Regex.Match(group.Groups[2].Value, "GroupName=\"([^\"]+)\"");
            var slots = Regex.Match(group.Groups[2].Value, "SlotNames=\\((.*)\\)");
            Require(slots.Success, "Malformed skeleton slot group.");
            foreach (Match slot in Regex.Matches(slots.Groups[1].Value, "\"([^\"]+)\""))
                Require(groups.TryAdd(slot.Groups[1].Value, (int.Parse(group.Groups[1].Value, CultureInfo.InvariantCulture),
                    name.Success ? name.Groups[1].Value : "DefaultGroup")), "Duplicate skeleton slot.");
        }
        var result = new List<AlsAuthoredMontageAsset>();
        foreach (var action in actions.Actions)
        {
            var montage = set.Montages[action.MontageId];
            var native = entries.Single(a => a.GetProperty("path").GetString() == montage.ObjectPath);
            Require(montage.Slots.Length == 1 && montage.Slots[0].Segments.Length == 1 && montage.Sections.Length == 1 &&
                montage.Sections[0].StartTime == 0 && montage.Sections[0].NextSectionId == -1 &&
                action.StartSectionId == montage.Sections[0].SectionId && action.LoopPolicy == AlsP5LoopPolicy.Once,
                "This physical montage binding requires one terminal section and one segment.");
            var slot = montage.Slots[0]; var segment = slot.Segments[0]; var animation = set.Animations[segment.AnimationId];
            var sections = action.Sections; var segments = actions.SegmentBindings.Where(s => s.ActionDefinitionId == action.DefinitionId).ToArray();
            Require(sections.Length == 1 && sections[0] == new AlsCompiledActionSectionBinding(action.DefinitionId,
                montage.Sections[0].SectionId, -1, 0, montage.PlayLength) && segments.Length == 1 && segments[0] ==
                new AlsCompiledActionSegmentBinding(action.DefinitionId, slot.SlotId, segment.SegmentId, segment.AnimationId,
                    0, montage.PlayLength, segment.AnimationStartTime, segment.AnimationEndTime, segment.PlayRate, segment.LoopCount),
                "Action section/segment timeline differs from the authored montage.");
            Require(action.SlotId == slot.SlotId && action.MontageDurationSeconds == montage.PlayLength &&
                animation.SkeletonId == skeletonId && animation.AdditiveType == 0 && segment.StartPosition == 0 && segment.LoopCount == 1 &&
                segment.PlayRate > 0 && segment.AnimationStartTime >= 0 && segment.AnimationEndTime <= animation.PlayLength &&
                MathF.Abs((segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate - montage.PlayLength) < 1e-6f,
                "Unsupported or stale montage segment.");
            var runtimeSlot = slot.SlotName switch { "BaseLayer" => AlsMontageSlot.BaseLayer, "Grounded Slot" => AlsMontageSlot.Grounded,
                _ => throw new ArgumentException("Authored action slot requires an explicit graph binding.") };
            Require(groups.TryGetValue(slot.SlotName, out var group) && native.GetProperty("group").GetString() == group.Name &&
                native.GetProperty("slot").GetInt32() == runtimeSlot.Id, "Native montage group/slot differs.");
            Require(native.GetProperty("rateScale").GetSingle() == 1 && native.GetProperty("blendInMode").GetInt32() == 0 &&
                native.GetProperty("blendOutMode").GetInt32() == 0 && !native.GetProperty("blendProfiles").GetBoolean() &&
                !native.GetProperty("customBlendCurves").GetBoolean() && native.GetProperty("hasRootMotion").GetBoolean() == animation.RootMotionEnabled,
                "Unsupported montage rate, blend mode, profile, custom curve or root-motion provenance.");
            Require(native.GetProperty("length").GetSingle() == montage.PlayLength && native.GetProperty("in").GetSingle() == montage.BlendInTime &&
                native.GetProperty("out").GetSingle() == montage.BlendOutTime && native.GetProperty("inOption").GetInt32() == montage.BlendInOption &&
                native.GetProperty("outOption").GetInt32() == montage.BlendOutOption && native.GetProperty("trigger").GetSingle() == montage.BlendOutTriggerTime &&
                native.GetProperty("auto").GetBoolean() == montage.EnableAutoBlendOut, "Montage lifecycle differs from the native asset.");
            var settings = new AlsActionLifecycleSettings(montage.EnableAutoBlendOut ? AlsActionLifecycleMode.MontageAutoBlendOut : AlsActionLifecycleMode.MontageHoldAtEnd,
                montage.BlendInTime, (AlsActionBlendOption)montage.BlendInOption, montage.BlendOutTime, (AlsActionBlendOption)montage.BlendOutOption, montage.BlendOutTriggerTime);
            Require(action.Lifecycle == settings, "Action profile lifecycle differs.");
            result.Add(new(action.DefinitionId, animation.Id, runtimeSlot, group.Id, montage.PlayLength,
                segment.AnimationStartTime, segment.PlayRate, settings, animation.RootMotionEnabled, montage.Id));
        }
        var compiled = result.ToArray(); _ = new AlsMontageRuntime([], compiled); return compiled;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
