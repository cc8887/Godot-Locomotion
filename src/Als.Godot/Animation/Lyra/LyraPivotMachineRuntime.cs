using System.Collections.Immutable;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraPivotMachineVisit(bool Active, float Weight, bool Reinitialize);
internal readonly record struct LyraPivotMachineState(int Current, float Elapsed, long Frame,
    long LastVisited, bool Initialized, bool FirstUpdate);
internal readonly record struct LyraPivotMachineInertia(float Duration, string Profile, int BlendMode);
internal sealed record LyraPivotMachineCandidate(LyraPivotMachineState Before, LyraPivotMachineState State,
    LyraPivotPairCandidate Sources, ImmutableArray<int> Initializations,
    bool Active, float Weight, bool InertialScope, bool AutomaticallyInitialized, bool Transitioned,
    bool FirstUpdate, ImmutableArray<LyraPivotMachineInertia> Requests, ImmutableArray<float> Inertia);

// Original two-state inertial machine. It selects and initializes child graphs;
// source visits and weights are outputs, never authored scheduling inputs.
internal sealed class LyraPivotMachineRuntime
{
    private readonly LyraPivotSourcePair _sources;
    private readonly LyraPivotMachineInertia[] _requests;
    private LyraPivotMachineCandidate? _pending;
    public LyraPivotMachineState State { get; private set; } = new(-1,0,0,-1,false,true);
    public LyraPivotSharedState Shared => _sources.Shared;
    internal LyraPivotSharedState PreparedShared => _pending?.Sources.Shared ?? Shared;
    public LyraPivotSourceState Source(int index) => _sources.Source(index);
    internal void InitializeSource(int index)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot source initialization needs an idle machine.");
        _sources.InitializeSource(index);
    }
    private bool _startupSourcesPending;
    internal void InitializeBeforeFirstFrame()
    {
        if(_pending is not null||State.Frame!=0)throw new InvalidOperationException("Pivot startup needs a fresh idle graph.");
        State=new(0,0,0,-1,true,true);_startupSourcesPending=true;
    }

    public LyraPivotMachineRuntime(LyraPivotLayerGraph graph, LyraPivotSourcePair sources)
    {
        _sources=sources;
        _requests=graph.Machine.GetProperty("transitions").EnumerateArray().Select(t=>
        {
            if (t.GetProperty("blendMode").GetString()!="HermiteCubic" ||
                t.GetProperty("customCurve").GetString()!="" ||
                t.GetProperty("startNotify").GetInt32()!=-1 || t.GetProperty("endNotify").GetInt32()!=-1 ||
                t.GetProperty("interruptNotify").GetInt32()!=-1)
                throw new NotSupportedException("Changed original Pivot transition requests.");
            return new LyraPivotMachineInertia(t.GetProperty("crossfadeDuration").GetSingle(),
                t.GetProperty("blendProfile").GetString()!,2);
        }).ToArray();
        if (graph.Machine.GetProperty("states").EnumerateArray().Any(s=>
            s.GetProperty("startNotify").GetInt32()!=-1 || s.GetProperty("endNotify").GetInt32()!=-1 ||
            s.GetProperty("fullyBlendedNotify").GetInt32()!=-1 || s.GetProperty("entryRuleNodeIndex").GetInt32()!=-1))
            throw new NotSupportedException("Changed Pivot state callbacks/notifies.");
    }

    public LyraPivotMachineCandidate Prepare(in LyraPivotInput input, float delta,
        LyraPivotMachineVisit visit, int sampleStart=0)
    {
        if (_pending is not null) throw new InvalidOperationException("Pivot machine candidate is pending.");
        if (!float.IsFinite(delta) || delta<0 || !float.IsFinite(visit.Weight) || visit.Weight<0 || sampleStart<0)
            throw new ArgumentException("Invalid Pivot machine context.");
        var state=State with { Frame=checked(State.Frame+1) };
        var initializations=new int[2];
        void Initialize()
        {
            state=state with { Current=0,Elapsed=0,Initialized=true,FirstUpdate=true };
            initializations[0]++;
        }
        if (!state.Initialized || visit.Reinitialize) Initialize();
        else if(_startupSourcesPending)initializations[0]++;
        // Capture occurs after external Initialize, before Update's relevance reset.
        var before=state;
        var automatic=false; var transitioned=false; var first=false; var inertial=false;
        var requests=ImmutableArray.CreateBuilder<LyraPivotMachineInertia>();
        if (visit.Active)
        {
            if (!state.FirstUpdate && state.LastVisited>=0 && state.Frame-state.LastVisited>1)
            { Initialize(); automatic=true; }
            first=state.FirstUpdate;
            var shared=Shared with { LastPivotTime=input.LastPivotTime };
            if (LyraPivotSourcePair.Rule(input,shared))
            {
                requests.Add(_requests[state.Current]);
                state=state with { Current=1-state.Current,Elapsed=0 };
                initializations[state.Current]++;
                transitioned=true;
            }
            // UE skips the first transition's blend record, not rule selection
            // or its request. Subsequent inertial transitions visit only target.
            inertial=transitioned && !first;
            state=state with { FirstUpdate=false,LastVisited=state.Frame,Elapsed=state.Elapsed+delta };
        }
        var visits=new LyraPivotVisit[2];
        for (var i=0;i<2;i++) visits[i]=new(visit.Active && state.Current==i,visit.Weight,
            initializations[i]>0,initializations[i]>0);
        var order=visit.Active && state.Current==1 ? new[]{1,0} : new[]{0,1};
        try
        {
            var pair=_sources.Prepare(input,delta,visits,order,sampleStart,inertial);
            var durations=requests.Select(r=>r.Duration).Concat(pair.Inertia).ToImmutableArray();
            return _pending=new(before,state,pair,initializations.ToImmutableArray(),visit.Active,visit.Weight,
                inertial,automatic,transitioned,first,requests.ToImmutable(),durations);
        }
        catch { Cancel(); throw; }
    }

    internal void ValidateCommit(LyraPivotMachineCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        if (!ReferenceEquals(candidate,_pending)) throw new InvalidOperationException("Rejected stale Pivot machine candidate.");
        _sources.ValidateCommit(candidate.Sources,outputs);
    }
    public void Commit(LyraPivotMachineCandidate candidate, ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        ValidateCommit(candidate,outputs);
        _sources.Commit(candidate.Sources,outputs); State=candidate.State; _pending=null;_startupSourcesPending=false;
    }
    public void Cancel() { _sources.Cancel(); _pending=null; }
}
