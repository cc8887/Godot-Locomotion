using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public static class AlsTurnNotifyCompiler
{
    public static AlsTurnNotifyBinding Compile(string json, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsP5CoreRuntimeBindingSnapshot binding, AlsDynamicMontageAsset[] turns)
    {
        using var document = JsonDocument.Parse(json);
        var metadata = AlsLocomotionSourceNotifyCompiler.Compile(document.RootElement, set);
        if (turns.Length != 8 || !metadata.Ranges.Select(r => r.AnimationId).Order().SequenceEqual(turns.Select(t => t.AnimationId).Order()))
            throw new ArgumentException("Notify export does not contain exactly the eight turn sequences.");
        var core = binding.CreateCoreView();
        if (core.Sources.Stamp != sources.RuntimeStamp) throw new ArgumentException("Source notify binding differs.");
        var objects = sources.NotifyObjects.ToList(); var names = sources.NotifyNames.ToList();
        var objectIds = metadata.Objects.Select(value => Intern(objects, value, StringComparer.Ordinal)).ToArray();
        var nameIds = metadata.Names.Select(value => Intern(names, value, StringComparer.OrdinalIgnoreCase)).ToArray();
        var policies = metadata.Policies.Select(p => p with
        {
            NotifyObjectId = p.NotifyObjectId < 0 ? -1 : objectIds[p.NotifyObjectId],
            StateObjectId = p.StateObjectId < 0 ? -1 : objectIds[p.StateObjectId], NameId = nameIds[p.NameId],
        }).ToArray();
        var existingTimeline = core.TimelineDefinitions.ToArray();
        var firstHandle = checked(binding.CreateOccurrenceLayoutView().Entries.ToArray().Max(e => e.OccurrenceHandleId) + 1);
        var assets = new AlsTurnNotifyAsset[metadata.Ranges.Length];
        var timeline = new AlsTimelineEventDefinition[metadata.Definitions.Length];
        for (var i = 0; i < assets.Length; i++)
        {
            var range = metadata.Ranges[i]; var asset = turns.Single(t => t.AnimationId == range.AnimationId);
            assets[i] = new(asset, checked(firstHandle + i), range.Offset, range.Count);
            var authored = set.Animations[asset.AnimationId].Timeline;
            for (var n = 0; n < range.Count; n++)
            {
                var entry = authored.Single(t => t.SourceIndex == policies[range.Offset + n].SourceIndex);
                var compiled = existingTimeline.First(t => t.SourceAnimationId == asset.AnimationId &&
                    t.EventId == entry.EventId && t.SourceIndex == entry.SourceIndex && t.SourceActionId == -1);
                timeline[range.Offset + n] = compiled with { RequiredOccurrenceHandleId = assets[i].Handle,
                    SourceKind = GodotAls.Core.Contracts.AlsTimelineSourceKind.MontageSegmentAnimation };
            }
        }
        return new(core.Sources.NotifyPolicies, policies, metadata.Definitions, timeline, assets);
    }

    private static int Intern(List<string> values, string value, StringComparer comparer)
    {
        var index = values.FindIndex(v => comparer.Equals(v, value));
        if (index >= 0) return index;
        values.Add(value); return values.Count - 1;
    }
}
