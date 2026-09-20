using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public static class AlsMontageNotifyCompiler
{
    public static AlsMontageNotifyBinding Compile(string actionJson, string turnJson, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsP5CoreRuntimeBindingSnapshot binding,
        AlsDynamicMontageAsset[] turns, AlsAuthoredMontageAsset[] actions,
        AlsSequenceMontageAsset[]? dynamicSequences = null, string? dynamicNotifyJson = null)
    {
        var turn = AlsTurnNotifyCompiler.Compile(turnJson,set,sources,binding,turns);
        using var turnDocument = JsonDocument.Parse(turnJson);
        var turnMetadata = AlsLocomotionSourceNotifyCompiler.Compile(turnDocument.RootElement,set);
        using var document = JsonDocument.Parse(actionJson);
        Require(document.RootElement.GetProperty("schemaVersion").GetInt32()==1,"Action notify schema differs.");
        var sequences = AlsLocomotionSourceNotifyCompiler.Compile(document.RootElement.GetProperty("sequences"),set);
        var montages = AlsLocomotionSourceNotifyCompiler.Compile(document.RootElement.GetProperty("montages"),set,true);
        Require(sequences.Ranges.Select(r=>r.AnimationId).Order().SequenceEqual(actions.Select(a=>a.AnimationId).Distinct().Order()) &&
            montages.Ranges.Select(r=>r.AnimationId).Order().SequenceEqual(actions.Select(a=>a.MontageId).Distinct().Order()),
            "Action notify export inventory differs from physical assets.");
        var objects = sources.NotifyObjects.ToList(); var names = sources.NotifyNames.ToList();
        foreach(var value in turnMetadata.Objects) Intern(objects,value,StringComparer.Ordinal);
        foreach(var value in turnMetadata.Names) Intern(names,value,StringComparer.OrdinalIgnoreCase);
        var policies = turn.Policies[turn.SourcePolicyCount..].ToArray().ToList();
        var definitions = turn.Definitions.ToArray().ToList();
        var timeline = new List<AlsTimelineEventDefinition>(); var ranges = new List<AlsMontageNotifyRange>();
        var nextHandle = checked(binding.CreateOccurrenceLayoutView().Entries.ToArray().Max(e=>e.OccurrenceHandleId)+1);
        foreach(var range in turn.Assets)
        {
            nextHandle=System.Math.Max(nextHandle,checked(range.Handle+1));
            ranges.Add(new(-1,range.Asset.AnimationId,range.Asset.Slot,range.Handle,range.Offset,range.Count,range.Asset.Duration,0,1,false));
            for(var i=0;i<range.Count;i++)
            {
                Require(turn.TryTimeline(new(turn.SourcePolicyCount+range.Offset+i,range.Handle,0,true,false),out var item),"Turn timeline differs.");
                timeline.Add(item);
            }
        }
        var existing = binding.CreateCoreView().TimelineDefinitions.ToArray();
        foreach(var action in actions)
        {
            Append(action,montages,action.MontageId,true);
            Append(action,sequences,action.AnimationId,false);
        }
        if (dynamicSequences is not null)
        {
            Require(dynamicNotifyJson is not null && dynamicSequences.Length > 0, "Missing dynamic sequence notify metadata.");
            using var dynamicDocument = JsonDocument.Parse(dynamicNotifyJson!);
            var native = dynamicDocument.RootElement;
            Require(native.GetProperty("schemaVersion").GetInt32() == 1 && native.GetProperty("source").GetString() ==
                "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP", "Foreign dynamic sequence notify metadata.");
            var metadata = AlsLocomotionSourceNotifyCompiler.Compile(native, set);
            Require(metadata.Ranges.Select(r => r.AnimationId).Order().SequenceEqual(dynamicSequences.Select(a => a.AnimationId).Distinct().Order()),
                "Dynamic sequence notify inventory differs.");
            foreach (var asset in dynamicSequences)
            {
                var row = native.GetProperty("syncAssets").EnumerateArray().Single(a => a.GetProperty("path").GetString() == set.Animations[asset.AnimationId].ObjectPath);
                Require(row.GetProperty("rateScale").GetSingle() == 1 && row.GetProperty("length").GetSingle() == asset.Duration,
                    "Dynamic sequence time mapping requires its native length and unit RateScale.");
                Append(new(-1, asset.AnimationId, asset.Slot, asset.GroupId, asset.Duration, 0, 1, default), metadata, asset.AnimationId, false);
            }
        }
        else Require(dynamicNotifyJson is null, "Unbound dynamic sequence notify metadata.");
        return new(turn.Policies[..turn.SourcePolicyCount],policies.ToArray(),definitions.ToArray(),timeline.ToArray(),ranges.ToArray());

        void Append(AlsAuthoredMontageAsset action,AlsLocomotionNotifyMetadata metadata,int sourceId,bool direct)
        {
            var range=metadata.Ranges.Single(r=>r.AnimationId==sourceId); var handle=nextHandle++;
            var kind=direct ? AlsTimelineSourceKind.Montage : AlsTimelineSourceKind.MontageSegmentAnimation;
            ranges.Add(new(action.ActionDefinitionId,action.AnimationId,action.Slot,handle,definitions.Count,range.Count,
                direct ? action.Duration : set.Animations[action.AnimationId].PlayLength,
                direct ? 0 : action.ClipStart,direct ? 1 : action.ClipRate,direct));
            for(var i=0;i<range.Count;i++)
            {
                var index=range.Offset+i; var policy=metadata.Policies[index];
                // Keep explicit class/payload bindings from the authoritative importer.
                // Display names never imply a gameplay action or Overlay behavior.
                var matches=existing.Where(t=>t.SourceActionId==action.ActionDefinitionId &&
                    (action.ActionDefinitionId == -1 || t.SourceKind==kind) &&
                    t.SourceAnimationId==sourceId && t.SourceIndex==policy.SourceIndex && t.EventId==policy.EventId).ToArray();
                Require(matches.Length > 0 || action.ActionDefinitionId == -1 && !direct,
                    "Authored montage lacks its formal timeline payload binding.");
                var entry=matches.Length > 0 ? matches[0] :
                    AlsP5CoreRuntimeBindingCompiler.CompileDynamicSequenceEvent(set.Animations[sourceId], policy.SourceIndex, policy.EventId, handle, existing);
                Require(matches.All(t => t.Kind==entry.Kind && t.Payload==entry.Payload), "Ambiguous dynamic sequence timeline payload.");
                var authored=(direct ? set.Montages[sourceId].Timeline : set.Animations[sourceId].Timeline)
                    .Single(t=>t.SourceIndex==policy.SourceIndex && t.EventId==policy.EventId);
                Require(policy.TickMode==AlsTimelineTickMode.Queued,"Branching points require a separate synchronous execution path.");
                policies.Add(policy with {
                    NotifyObjectId=policy.NotifyObjectId<0 ? -1 : Intern(objects,metadata.Objects[policy.NotifyObjectId],StringComparer.Ordinal),
                    StateObjectId=policy.StateObjectId<0 ? -1 : Intern(objects,metadata.Objects[policy.StateObjectId],StringComparer.Ordinal),
                    NameId=Intern(names,metadata.Names[policy.NameId],StringComparer.OrdinalIgnoreCase),
                });
                definitions.Add(metadata.Definitions[index]);
                // State callbacks receive the event's authored duration, including for
                // a scaled sequence segment. The old action lane stores mapped times.
                timeline.Add(entry with {RequiredOccurrenceHandleId=handle,SourceKind=kind,
                    TimeSeconds=authored.TimeSeconds,DurationSeconds=authored.DurationSeconds});
            }
        }
    }
    private static int Intern(List<string> values,string value,StringComparer comparer)
    { var index=values.FindIndex(v=>comparer.Equals(v,value)); if(index>=0)return index; values.Add(value); return values.Count-1; }
    private static void Require(bool condition,string message) { if(!condition)throw new ArgumentException(message); }
}
