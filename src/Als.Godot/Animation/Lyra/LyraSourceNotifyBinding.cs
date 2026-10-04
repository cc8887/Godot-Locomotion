using System.Collections.Immutable;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal enum LyraNotifySourceOwner { Main, Linked }
// Main's enclosing state survives the Linked layer call. This is distinct
// from the provider's local node/epoch and from native notify-state lifetime.
internal readonly record struct LyraNotifyMainStateContext(int Machine,int State,bool Present);
internal readonly record struct LyraNotifyPlayerContext(int PlayerId,LyraNotifySourceOwner Owner,int Node,
    long Epoch,bool Active,bool ScopeFiltered,AlsBlendSpaceNotifyMode Mode,LyraNotifyMainStateContext MainState=default);
internal readonly record struct LyraSourceNotifyTick(LyraNotifyPlayerContext Context,int Order,int Sample,
    int SequenceIndex,float Previous,float Delta,float CurrentTime,float Weight,bool Leader,bool Looping);
internal sealed class LyraSourceNotifyFrame
{
    internal LyraSourceNotifyBinding Owner {get;}
    internal LyraMainLocomotionCandidate? Candidate {get;}
    internal LyraMainDefaultRootFrame? Default {get;}
    public AlsFrameIdentity Identity {get;}
    public ImmutableArray<LyraSourceNotifyTick> Ticks {get;}
    internal LyraSourceNotifyFrame(LyraSourceNotifyBinding owner,LyraMainLocomotionCandidate candidate,
        AlsFrameIdentity identity,ImmutableArray<LyraSourceNotifyTick> ticks)
    {Owner=owner;Candidate=candidate;Identity=identity;Ticks=ticks;}
    internal LyraSourceNotifyFrame(LyraSourceNotifyBinding owner,LyraMainDefaultRootFrame candidate,AlsFrameIdentity identity)
    {Owner=owner;Default=candidate;Identity=identity;Ticks=[];}
}

// Read the prepared common Sync result. It never advances source time, filters
// a queue, consumes RNG, owns notify state or commits an animation frame.
internal sealed class LyraSourceNotifyBinding(LyraMainLocomotionHost host,LyraNotifyCatalog catalog)
{
    public LyraSourceNotifyFrame Capture(LyraMainPoseCandidate candidate)=>candidate.Default is {} empty
        ?new(this,empty,host.PreparedIdentity(empty)):Capture(candidate.Main);
    public LyraSourceNotifyFrame Capture(LyraMainLocomotionCandidate candidate)
    {
        var players=host.PreparedPlayers(candidate);var completed=host.PreparedSamples(candidate);
        var order=host.PreparedTicks(candidate);var contexts=host.PreparedNotifyContexts(candidate,catalog);
        var input=candidate.Sources.Players;var samples=candidate.Sources.Samples;
        if(players.Length!=input.Length||order.Length!=input.Length||contexts.Length!=input.Length)
            throw new InvalidOperationException("Incomplete common Sync notification contexts.");
        var ticks=ImmutableArray.CreateBuilder<LyraSourceNotifyTick>();
        for(var n=0;n<order.Length;n++)
        {
            var i=-1;for(var j=0;j<order.Length;j++)if(order[j].Order==n){if(i>=0)throw new InvalidOperationException("Duplicate Sync tick order.");i=j;}
            if(i<0)throw new InvalidOperationException("Missing Sync tick order.");
            // Completed records are packed by Sync group, while enrollment
            // remains in graph traversal order. Rejoin by player identity.
            var p=players[i];var enrolled=Array.FindIndex(input,v=>v.PlayerId==p.PlayerId);
            if(enrolled<0)throw new InvalidOperationException("Unregistered source notify player.");
            var source=input[enrolled];var context=contexts[enrolled];
            if(order[i].PlayerId!=p.PlayerId||source.PlayerId!=p.PlayerId||source.AssetId!=p.AssetId||source.Epoch!=p.Epoch||context.PlayerId!=p.PlayerId||
                context.Epoch!=p.Epoch||p.SampleCount!=source.SampleCount)
                throw new InvalidOperationException($"Foreign source notify player: completed={p.PlayerId}/{p.AssetId}/{p.Epoch}, enrolled={source.PlayerId}/{source.AssetId}/{source.Epoch}, context={context.PlayerId}/{context.Epoch}.");
            var highest=0;var weight=-1f;
            for(var s=0;s<source.SampleCount;s++)
            {var w=Math.Clamp(samples[source.SampleStart+s].Weight,0,1);if(w>weight){highest=s;weight=w;}}
            for(var s=0;s<source.SampleCount;s++)
            {
                var sample=samples[source.SampleStart+s];var history=completed[p.SampleStart+s];
                if(sample.SampleId!=history.SampleId||history.AnimationId!=host.Resources.Sequences[sample.SequenceIndex].AnimationId||catalog.Sequence(sample.SequenceIndex).Length<=0)
                    throw new InvalidOperationException("Foreign source notify sample.");
                if(source.Kind==AlsAssetSyncKind.BlendSpace&&sample.Weight<=1e-5f||context.Mode==AlsBlendSpaceNotifyMode.None||
                    context.Mode==AlsBlendSpaceNotifyMode.HighestWeightedAnimation&&s!=highest)continue;
                ticks.Add(new(context,n,sample.SampleId,sample.SequenceIndex,history.DeltaPrevious,history.Delta,
                    p.Time,source.Weight,order[i].Leader,source.Looping));
            }
        }
        return new(this,candidate,host.PreparedIdentity(candidate),ticks.ToImmutable());
    }
    public void Validate(LyraSourceNotifyFrame frame)
    {
        if(!ReferenceEquals(frame.Owner,this)||(frame.Default is {} empty?host.PreparedIdentity(empty):host.PreparedIdentity(frame.Candidate
            ??throw new InvalidOperationException("Missing source frame branch.")))!=frame.Identity)
            throw new InvalidOperationException("Foreign or stale source notify frame.");
    }
    public int Extract(LyraSourceNotifyFrame frame,int tickIndex,Span<AlsAssetNotifyOccurrence> output)
    {
        Validate(frame);if((uint)tickIndex>=(uint)frame.Ticks.Length)throw new ArgumentOutOfRangeException(nameof(tickIndex));
        var tick=frame.Ticks[tickIndex];var asset=catalog.Sequence(tick.SequenceIndex);
        if(!AlsTimelineRuntime.TryExtractAssetNotifies(catalog.Definitions.Slice(asset.Offset,asset.Count),asset.Length,
            tick.Previous,tick.Delta,tick.Looping,output,out var count,out var failure))
            throw new InvalidOperationException("Source notify extraction failed: "+failure);
        for(var i=0;i<count;i++)output[i]=output[i] with{DefinitionIndex=output[i].DefinitionIndex+asset.Offset};
        return count;
    }
}
