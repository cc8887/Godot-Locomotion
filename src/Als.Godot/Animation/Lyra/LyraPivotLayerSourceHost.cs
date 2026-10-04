using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraPivotLayerSourceCandidate(LyraPivotMachineCandidate Machine,
    LyraHipFireSourceState HipFire, AlsAssetSyncPlayer[] Players, int[] Groups, AlsAssetSyncSample[] Samples,
    float BlendWeight, float HipFireWeight, bool HipFireTicked);
internal sealed record LyraPivotLayerResolvedSources(AlsAssetPlayerHistory PivotOutput,
    LyraPivotSourceState Pivot, LyraHipFireSourceState HipFire, AlsAssetPlayerHistory[] MachineOutputs);

// The original outer blend updates HipFire before PivotSM. Both use the same
// enclosing Sync pass; only the machine source joins Locomotion.
internal sealed class LyraPivotLayerSourceHost
{
    private readonly LyraPivotLayerGraph _graph;
    private readonly LyraPivotMachineRuntime _machine;
    private readonly Func<bool, int> _hipAsset;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly ulong[] _masks;
    private readonly int _playerBase;
    private readonly long _epoch;
    private LyraPivotLayerSourceCandidate? _pending;
    public LyraPivotMachineRuntime Machine => _machine;
    public LyraHipFireSourceState HipFire { get; private set; } = new(-1, 0, AlsAssetMarkerRecord.Invalid);
    public float BlendWeight { get; private set; }
    public float HipFireWeight { get; private set; }

    public LyraPivotLayerSourceHost(LyraPivotLayerGraph graph, LyraPivotMachineRuntime machine,
        int playerBase, long epoch, Func<bool, int> hipAsset, AlsAssetSyncSequence[] sequences, ulong[] masks)
    {
        if (playerBase < 0 || epoch <= 0 || sequences.Length != masks.Length)
            throw new ArgumentException("Invalid Pivot layer owner.");
        _graph=graph; _machine=machine; _playerBase=playerBase; _epoch=epoch;
        _hipAsset=hipAsset; _sequences=sequences; _masks=masks;
        BlendWeight=graph.InitialBlendWeight;
    }

    internal bool InitializeSourceNode(int node)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot phase initialization needs an idle layer.");
        if(node==_graph.PivotA.Index || node==_graph.PivotB.Index)
            _machine.InitializeSource(node==_graph.PivotA.Index?0:1);
        else if(node==_graph.HipFire.Index)
            HipFire = HipFire with { Marker = HipFire.Marker with { PreviousIndex=-2, NextIndex=-2, Initialized=false }, ResetPending=true };
        else return false;
        return true;
    }
    public LyraPivotLayerSourceCandidate Prepare(in LyraPivotInput input, float delta, float weight,
        double hipWeight, bool active, bool reset, int sampleStart=0)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot layer candidate is pending.");
        if (!double.IsFinite(hipWeight) || !float.IsFinite((float)hipWeight) || sampleStart<0)
            throw new ArgumentException("Invalid Pivot layer context.");
        try
        {
            var blend=active ? (float)hipWeight : BlendWeight;
            var hip=reset ? HipFire with { Marker=HipFire.Marker with { PreviousIndex=-2,NextIndex=-2,Initialized=false },ResetPending=true } : HipFire;
            var hipTicked=active && blend>1e-5f;
            var count=hipTicked ? 1 : 0;
            var players=new AlsAssetSyncPlayer[count]; var groups=new int[count]; var samples=new AlsAssetSyncSample[count];
            var cached=HipFireWeight;
            if (hipTicked)
            {
                var id=_hipAsset(input.Crouching); cached=weight*blend;
                if ((uint)id>=_sequences.Length) throw new InvalidOperationException("Missing Pivot HipFire source.");
                var marker=hip.AssetId==id ? hip.Marker : hip.Marker with { PreviousIndex=-2,NextIndex=-2,Initialized=false };
                var tick=LyraEvaluatorSourceTick.Prepare(_graph.HipFire,checked(_playerBase+_graph.HipFire.Index),id,_epoch,
                    hip.Time,_graph.HipFire.Settings.GetProperty("explicitTime").GetSingle(),_sequences[id],delta,
                    cached,sampleStart,_masks[id],reset||hip.ResetPending,markerRecord:marker);
                players[0]=tick.Player; groups[0]=-1; samples[0]=new(tick.Player.PlayerId,id,1);
                hip=hip with { AssetId=id,Time=tick.Preparation.Time,Marker=marker,ResetPending=false };
            }
            var machine=_machine.Prepare(input,delta,new(active,weight,reset),sampleStart+(hipTicked ? 1 : 0));
            if (active && machine.Sources.Sources[machine.State.Current].Ticked)
            {
                var source=machine.Sources.Sources[machine.State.Current];
                var index=hipTicked ? 1 : 0;
                Array.Resize(ref players,index+1); Array.Resize(ref groups,index+1); Array.Resize(ref samples,index+1);
                players[index]=source.Tick.Player; groups[index]=0;
                samples[index]=new(source.Tick.Player.PlayerId,source.State.AssetId,1);
            }
            return _pending=new(machine,hip,players,groups,samples,blend,cached,hipTicked);
        }
        catch { _machine.Cancel(); throw; }
    }

    internal LyraPivotLayerResolvedSources Resolve(LyraPivotLayerSourceCandidate candidate,
        ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Rejected stale Pivot layer candidate.");
        var ticked=candidate.Machine.Sources.Sources[candidate.Machine.State.Current].Ticked;
        var machineOutputs=new AlsAssetPlayerHistory[ticked ? 1 : 0];
        var found=false; var hipFound=false; var cursor=0; AlsAssetPlayerHistory pivot=default,hip=default;
        foreach (var output in outputs)
        {
            if (output.SampleStart!=cursor || output.SampleCount<=0)
                throw new InvalidOperationException("Invalid common Pivot Sync ranges.");
            cursor=checked(cursor+output.SampleCount);
            if (output.PlayerId==_playerBase+_graph.PivotA.Index || output.PlayerId==_playerBase+_graph.PivotB.Index)
            {
                if (found || !ticked) throw new InvalidOperationException("Duplicate or unticked Pivot result.");
                var source=candidate.Machine.Sources.Sources[candidate.Machine.State.Current];
                // Batch results are packed by group, rather than input arrival.
                // Restore the captured input range only after checking identity.
                if (output.PlayerId!=source.Tick.Player.PlayerId) throw new InvalidOperationException("Inactive Pivot source returned a result.");
                pivot=output with { SampleStart=source.Tick.Player.SampleStart };
                machineOutputs[0]=pivot; found=true;
            }
            if (output.PlayerId==_playerBase+_graph.HipFire.Index)
            { if (hipFound) throw new InvalidOperationException("Duplicate Pivot HipFire result."); hip=output; hipFound=true; }
        }
        if (found!=ticked || hipFound!=candidate.HipFireTicked)
            throw new InvalidOperationException("Incomplete Pivot layer Sync results.");
        _machine.ValidateCommit(candidate.Machine,machineOutputs);
        var h=candidate.HipFire;
        if (hipFound)
        {
            var sequence=_sequences[h.AssetId];
            if (hip.AssetId!=h.AssetId || hip.Epoch!=_epoch || hip.SampleCount!=1 ||
                hip.IsNonLoopingEvaluator==_graph.HipFire.Looping || !float.IsFinite(hip.Time) ||
                hip.Time<0 || hip.Time>sequence.DurationSeconds || !float.IsFinite(hip.DeltaPrevious) || !float.IsFinite(hip.Delta) ||
                !float.IsFinite(hip.Marker.PreviousDistance) || !float.IsFinite(hip.Marker.NextDistance) ||
                hip.Marker.Initialized && (hip.Marker.PreviousIndex < -1 || hip.Marker.NextIndex < -1 ||
                    hip.Marker.PreviousIndex>=sequence.MarkerCount || hip.Marker.NextIndex>=sequence.MarkerCount))
                throw new InvalidOperationException("Rejected foreign Pivot HipFire result.");
            h=h with { Time=hip.Time,Marker=hip.Marker,DeltaPrevious=hip.DeltaPrevious,Delta=hip.Delta };
        }
        var state=candidate.Machine.Sources.Sources[candidate.Machine.State.Current].State;
        if (found) state=state with { Time=pivot.Time,Marker=pivot.Marker,DeltaPrevious=pivot.DeltaPrevious,Delta=pivot.Delta };
        return new(pivot,state,h,machineOutputs);
    }

    public void Commit(LyraPivotLayerSourceCandidate candidate,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        var resolved=Resolve(candidate,outputs);
        _machine.Commit(candidate.Machine,resolved.MachineOutputs);
        HipFire=resolved.HipFire; BlendWeight=candidate.BlendWeight; HipFireWeight=candidate.HipFireWeight; _pending=null;
    }
    public void Cancel() { _machine.Cancel(); _pending=null; }
}
