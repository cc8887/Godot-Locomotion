using System.Collections.ObjectModel;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingHostNotifyResources(AlsMontageNotifyBinding Binding,IReadOnlyDictionary<int,AlsMantlingFootstep> Footsteps);

/// <summary>Maps Refactored resources after the host's complete resource inventory.
/// Original-key pose data keeps its source identity; only playback-facing IDs change.
/// No scene writer, second clock, or automatic cross-group gameplay cancellation.</summary>
public sealed class AlsMantlingHostResources
{
    private readonly AlsDynamicMontageAsset[] _turns;
    private readonly AlsAuthoredMontageAsset[] _actions;
    private readonly AlsSequenceMontageAsset[] _sequences;
    public AlsMantlingMontageProfile Profile { get; }
    public IReadOnlyDictionary<int,int> AnimationIds { get; }
    public IReadOnlyDictionary<int,int> ActionIds { get; }
    public IReadOnlyDictionary<int,int> MontageIds { get; }
    public int GroupId { get; }
    public ReadOnlySpan<AlsAuthoredMontageAsset> Actions=>_actions;

    public AlsMantlingHostResources(AlsMantlingMontageProfile source,AlsAnimationSetDefinition host,
        ReadOnlySpan<AlsAuthoredMontageAsset> actions,ReadOnlySpan<AlsDynamicMontageAsset> turns,
        ReadOnlySpan<AlsSequenceMontageAsset> sequences=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(host);
        // Validate the original host before allocating or publishing a composed bank.
        _=new AlsMontageRuntime(turns,actions,sequences);
        var hostAnimationIds=host.Animations.Select(a=>a.Id).ToHashSet();
        var hostMontageIds=host.Montages.Select(m=>m.Id).ToHashSet();
        foreach(var a in actions)
            if(!hostAnimationIds.Contains(a.AnimationId)||!hostMontageIds.Contains(a.MontageId))
                throw new ArgumentException("Host action is absent from its complete animation set.");
        foreach(var t in turns)if(!hostAnimationIds.Contains(t.AnimationId))throw new ArgumentException("Unbound host turn source.");
        foreach(var s in sequences)if(!hostAnimationIds.Contains(s.AnimationId))throw new ArgumentException("Unbound host sequence source.");
        _turns=turns.ToArray();_sequences=sequences.ToArray();var hostActions=actions.ToArray();
        var native=source.Definitions.Values.Select(d=>d.Asset).ToArray();
        var animations=Map(native.Select(a=>a.AnimationId),Next(hostAnimationIds));
        var actionIds=Map(native.Select(a=>a.ActionDefinitionId),Next(hostActions.Select(a=>a.ActionDefinitionId)));
        var montages=Map(native.Select(a=>a.MontageId),Next(hostMontageIds));
        // Native group indexes belong to their skeletons. Allocate one explicit
        // Refactored.Locomotion group after all host physical groups.
        GroupId=Next(hostActions.Select(a=>a.GroupId).Concat(_turns.Select(t=>t.GroupId)).Concat(_sequences.Select(s=>s.GroupId)));
        var definitions=source.Definitions.ToDictionary(item=>item.Key,item=>item.Value with
        {
            Asset=item.Value.Asset with {AnimationId=animations[item.Value.Asset.AnimationId],
                ActionDefinitionId=actionIds[item.Value.Asset.ActionDefinitionId],MontageId=montages[item.Value.Asset.MontageId],GroupId=GroupId}
        },StringComparer.Ordinal);
        Profile=new(definitions,source.Poses,source.Curves,source.GroupName);
        _actions=hostActions.Concat(definitions.Values.Select(d=>d.Asset)).ToArray();
        AnimationIds=new ReadOnlyDictionary<int,int>(animations);ActionIds=new ReadOnlyDictionary<int,int>(actionIds);
        MontageIds=new ReadOnlyDictionary<int,int>(montages);
        _=CreateRuntime();
    }
    public AlsMontageRuntime CreateRuntime(AlsMantlingBranchingRuntime? branching=null)=>new(_turns,_actions,_sequences,branching);
    public AlsMantlingHostNotifyResources BindNotifies(string animationJson,AlsMontageNotifyBinding host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var local=AlsMantlingNotifyCompiler.Compile(animationJson,Profile);
        var hostPolicies=host.Policies.ToArray();
        var eventStart=Next(hostPolicies.Select(p=>p.EventId));
        var objectStart=Next(hostPolicies.SelectMany(p=>new[]{p.NotifyObjectId,p.StateObjectId}));
        var nameStart=Next(hostPolicies.Select(p=>p.NameId));
        var handleStart=Next(host.Ranges.ToArray().Select(r=>r.Handle));
        var policies=hostPolicies.Skip(host.SourcePolicyCount).ToList();
        var definitions=host.Definitions.ToArray().ToList();var ranges=host.Ranges.ToArray().ToList();
        var timeline=new List<AlsTimelineEventDefinition>();
        foreach(var range in host.Ranges)
        for(var i=0;i<range.Count;i++)
        {
            if(!host.TryTimeline(new(host.SourcePolicyCount+range.Offset+i,range.Handle,0,true,false),out var entry))
                throw new ArgumentException("Host notify timeline is incomplete.");
            timeline.Add(entry);
        }
        var definitionStart=definitions.Count;
        foreach(var range in local.Binding.Ranges)
        {
            var handle=checked(handleStart+range.Handle);
            ranges.Add(range with {Handle=handle,Offset=checked(definitionStart+range.Offset)});
            for(var i=0;i<range.Count;i++)
            {
                var index=range.Offset+i;var policy=local.Binding.Policies[index];
                if(!local.Binding.TryTimeline(new(index,range.Handle,0,true,false),out var entry))
                    throw new ArgumentException("Mantle notify timeline is incomplete.");
                var eventId=checked(eventStart+policy.EventId);
                policies.Add(policy with {EventId=eventId,NotifyObjectId=checked(objectStart+policy.NotifyObjectId),NameId=checked(nameStart+policy.NameId)});
                definitions.Add(local.Binding.Definitions[index] with {EventId=eventId});
                timeline.Add(entry with {EventId=eventId,RequiredOccurrenceHandleId=handle});
            }
        }
        var binding=new AlsMontageNotifyBinding(host.Policies[..host.SourcePolicyCount],policies.ToArray(),definitions.ToArray(),timeline.ToArray(),ranges.ToArray());
        return new(binding,new ReadOnlyDictionary<int,AlsMantlingFootstep>(local.Footsteps.Select((f,index)=>(f,index))
            .ToDictionary(p=>checked(eventStart+p.index),p=>p.f)));
    }
    private static Dictionary<int,int> Map(IEnumerable<int> ids,int start)
    {
        var result=new Dictionary<int,int>();
        foreach(var id in ids.Distinct().Order())
        {
            if(id<0)throw new ArgumentException("Negative mantle resource identity.");
            result.Add(id,start);start=checked(start+1);
        }
        return result;
    }
    private static int Next(IEnumerable<int> ids)=>checked(ids.DefaultIfEmpty(-1).Max()+1);
}
