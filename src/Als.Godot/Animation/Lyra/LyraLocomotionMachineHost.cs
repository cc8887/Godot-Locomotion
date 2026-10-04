using System.Collections.Immutable;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLocomotionMachineVisit(bool Visited, float Weight, bool Initialize, bool Active = true);
internal sealed record LyraLocomotionMachineCandidate(long Frame, int BeforeState, float BeforeElapsed,
    LyraLocomotionTransition? Selected, ImmutableArray<LyraStateSourceUpdate> Updates,
    ImmutableArray<int> Initializations, ImmutableArray<int> ClearWeights, ImmutableArray<float> PreviousWeights,
    bool AutomaticallyInitialized, bool FirstUpdate, bool Visited, float Weight);

// Owns the original selection/weight stack transaction. The actual source
// scope consumes Updates and initialization/weight-clear instructions. Idle
// and Air are real states here; the host never replaces their poses by Idle.
internal sealed class LyraLocomotionMachineHost
{
    private readonly LyraCompiledMachine _definition;
    private readonly LyraLocomotionMachine _committedSelector, _selector;
    private readonly LyraLocomotionPoseState _committedPose, _pose;
    private LyraLocomotionMachineCandidate? _pending;
    private long _frame, _lastVisited = -1;
    private bool _initialized, _first = true;
    private ImmutableArray<float> _recordedWeights = Enumerable.Repeat(0f,12).ToImmutableArray();
    public int State => _committedPose.State;
    public float Elapsed => _committedPose.Elapsed;
    public AlsTransitionStackState Stack => _committedPose.Stack;
    public float Weight(int state) => _committedPose.Weight(state);

    public LyraLocomotionMachineHost(LyraRuntimeGraphCatalog catalog, int curveCount)
    {
        _definition = catalog.Locomotion;
        _committedSelector = new(catalog); _selector = new(catalog);
        _committedPose = new(catalog, curveCount); _pose = new(catalog, curveCount);
    }
    internal void InitializeBeforeFirstFrame()
    {
        if(_pending is not null||_frame!=0||_initialized)throw new InvalidOperationException("Main machine startup initialization is not idle/fresh.");
        InitializeGraph();
    }
    internal void InitializeGraph()
    {
        if(_pending is not null)throw new InvalidOperationException("Main graph initialization requires an idle machine.");
        _committedSelector.Reset();_committedPose.Reset();_selector.CopyFrom(_committedSelector);_pose.CopyFrom(_committedPose);
        _initialized=true;_first=true;_lastVisited=-1;
        _recordedWeights=Enumerable.Repeat(0f,12).ToImmutableArray();
    }
    public LyraLocomotionMachineCandidate Prepare(in LyraLocomotionRuleInputs inputs, float delta,
        LyraLocomotionMachineVisit visit)
    {
        if (_pending is not null) throw new InvalidOperationException("Locomotion machine frame is pending.");
        if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(visit.Weight) || visit.Weight < 0)
            throw new ArgumentException("Invalid locomotion machine context.");
        _selector.CopyFrom(_committedSelector); _pose.CopyFrom(_committedPose);
        var frame = checked(_frame + 1); var initializations = new int[12]; var cleared = new List<int>();
        var first = _first;
        void Initialize()
        { _selector.Reset(); _pose.ResetMachine(); initializations[_definition.InitialState]++; cleared.Add(_definition.InitialState); first = true; }
        if (!_initialized || visit.Initialize) Initialize();
        var before = _pose.State; var elapsed = _pose.Elapsed;
        var previousWeights = _recordedWeights;
        var automatic = visit.Visited && !first && _lastVisited >= 0 && frame - _lastVisited > 1;
        if (automatic) Initialize();
        LyraLocomotionTransition? selected = null; var updates = ImmutableArray<LyraStateSourceUpdate>.Empty;
        if (visit.Visited)
        {
            selected = _selector.Update(inputs,delta,visit.Active);
            if (selected is not null)
            {
                if (!(_pose.Weight(selected.Next)>0) || _definition.States[selected.Next].AlwaysResetOnEntry)
                    initializations[selected.Next]++;
                cleared.Add(selected.Next);
            }
            _pose.Prepare(selected,delta);
            updates = _pose.Updates.ToArray().Select(u=>u with { Weight=u.Weight*visit.Weight,Active=u.Active && visit.Active }).ToImmutableArray();
            if (_selector.State != _pose.State || _selector.Elapsed != _pose.Elapsed)
                throw new InvalidOperationException("Locomotion selection and pose histories disagree.");
        }
        return _pending = new(frame,before,elapsed,selected,updates,initializations.ToImmutableArray(),cleared.ToImmutableArray(),
            previousWeights,automatic,first,visit.Visited,visit.Weight);
    }
    internal int PreparedState(LyraLocomotionMachineCandidate c) { Validate(c); return _pose.State; }
    internal float PreparedElapsed(LyraLocomotionMachineCandidate c) { Validate(c); return _pose.Elapsed; }
    internal AlsTransitionStackState PreparedStack(LyraLocomotionMachineCandidate c) { Validate(c); return _pose.Stack; }
    internal float PreparedWeight(LyraLocomotionMachineCandidate c,int state) { Validate(c); return _pose.Weight(state); }
    public void EvaluateMachine(LyraLocomotionMachineCandidate c,LyraStatePoseEvaluator evaluate,
        Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        Validate(c); if (!c.Visited) throw new InvalidOperationException("Hidden locomotion machine has no pose.");
        _pose.EvaluateMachine(evaluate,pose,curves);
    }
    internal void Validate(LyraLocomotionMachineCandidate c)
    { if (!ReferenceEquals(_pending,c)) throw new InvalidOperationException("Stale locomotion machine candidate."); }
    public void Commit(LyraLocomotionMachineCandidate c)
    {
        Validate(c); _committedSelector.CopyFrom(_selector); _committedPose.CopyFrom(_pose);
        _frame=c.Frame; _initialized=true; _first=c.FirstUpdate && !c.Visited;
        if (c.Visited) _lastVisited=c.Frame;
        _recordedWeights=Enumerable.Range(0,12).Select(s=>c.Visited ? _pose.Weight(s) : 0).ToImmutableArray();
        _pending=null;
    }
    public void Cancel() { _pending=null; }
}
