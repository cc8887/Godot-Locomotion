using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original node118, including the RotationYawSpeed exclusion. Update
/// and Evaluate remain separate; update-only frames retain requests and time.</summary>
public sealed class AlsRefactoredStandingInertialization
{
    public const int NodeIndex = 118;
    private readonly AlsRefactoredStandingPose _profile;
    private readonly int[] _unfiltered;
    private readonly int _yaw;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves, _inputCurves, _outputCurves;
    private AlsInertialization _committed, _updated, _evaluatedState;
    private List<AlsOverlayInertialRequest> _requests = new(), _nextRequests = new();
    private readonly List<AlsRefactoredForwardedInertialRequest> _forwarded = new();
    private AlsGraphTraversalCounter _counter, _nextCounter;
    private AlsFrameIdentity _identity, _lastIdentity;
    private AlsRefactoredStandingRuntime? _machineOwner;
    private AlsRefactoredStandingMovementTraversal? _traversalOwner;
    private AlsRefactoredMovementInertialization? _movementOwner;
    private AlsRefactoredStandingPose.Runtime? _poseOwner;
    private float _pendingDelta, _nextDelta;
    private bool _prepared, _evaluated, _faulted, _hasCommitted;
    public bool IsActive => _prepared && _evaluated && !_faulted ? _evaluatedState.IsActive : _committed.IsActive;
    public int CommittedHistoryCount => _committed.HistoryCount;
    public int PendingRequests => _prepared ? _evaluated && !_faulted ? 0 : _nextRequests.Count : _requests.Count;
    public int ForwardAttempts { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Standing inertial pose unavailable.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Standing inertial curves unavailable.");
    public IReadOnlyList<AlsRefactoredForwardedInertialRequest> ForwardedRequests => _prepared ? _forwarded.AsReadOnly() : throw new InvalidOperationException("No Standing inertial candidate.");

    public AlsRefactoredStandingInertialization(AlsRefactoredAnimationCatalog catalog, AlsRefactoredStandingPose profile)
    {
        if (catalog.IndexDigest != profile.Resources.CatalogDigest) throw new ArgumentException("Foreign Standing inertial catalog.");
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false); var bp = catalog.Read(blueprint);
        var nodes = bp.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var node = nodes[NodeIndex]; Expect(node,new { @class = "AnimGraphNode_Inertialization" });
        foreach (var p in new[] {node.GetProperty("runtime"),node.GetProperty("authoredProperties").GetProperty("Node")})
        {
            Expect(p,new { defaultBlendProfile = "", bResetOnBecomingRelevant = true, bForwardRequestsThroughSkippedCachedPoseNodes = true,
                tag = "None", filteredCurves = new[] {"RotationYawSpeed"}, filteredBones = Array.Empty<string>() });
            foreach (var cb in new[] {"initialUpdateFunction","becomeRelevantFunction","updateFunction"}) Expect(p.GetProperty(cb),new {functionName = "None"});
        }
        Expect(node.GetProperty("runtime").GetProperty("source"),new {linkId = 65, sourceLinkId = NodeIndex});
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(bp.GetProperty("nativeText").GetString()!,blueprint,blueprint+":AnimGraph"),true);
        var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
        var (child,pin) = graph.FollowReroutes(authored,"Source");
        if (authored.Kind != "AnimGraphNode_Inertialization" || pin.Name != "Pose" || nodes[65].GetProperty("path").GetString() != blueprint+":AnimGraph."+child.Name)
            throw new ArgumentException("Standing inertial authored source differs.");
        _profile = profile; _yaw = Array.IndexOf(profile.CurveNames.ToArray(),"RotationYawSpeed");
        if (_yaw < 0) throw new ArgumentException("Standing yaw curve missing from layout.");
        _unfiltered = Enumerable.Range(0,profile.CurveNames.Length).Where(c => c != _yaw).ToArray();
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
        _inputCurves = new AlsInertialCurve[_unfiltered.Length]; _outputCurves = new AlsInertialCurve[_unfiltered.Length];
        // A fixed excluded channel cannot influence the other independent curve
        // diffs. Omit it from history entirely and copy its destination unchanged.
        _committed = new(_pose.Length,_unfiltered.Length); _updated = new(_pose.Length,_unfiltered.Length); _evaluatedState = new(_pose.Length,_unfiltered.Length);
    }
    public static AlsPoseUpdateContext SourceContext(in AlsPoseUpdateContext context) => context.WithInertialization(NodeIndex,true);
    public void Prepare(in AlsPoseUpdateContext context, AlsRefactoredStandingRuntime machine,
        AlsRefactoredStandingMovementTraversal traversal, AlsRefactoredMovementInertialization? movement = null, bool initializeInstance = false)
    {
        var frame = context.Identity.FrameId;
        if (_prepared || context.UpdateCounter is not {HasUpdated:true} || !context.HasSharedContext || !ReferenceEquals(machine.Resources,_profile.Resources) ||
            _machineOwner is not null && !ReferenceEquals(_machineOwner,machine) || _traversalOwner is not null && !ReferenceEquals(_traversalOwner,traversal) ||
            _hasCommitted && (frame <= _lastIdentity.FrameId || context.Identity.CharacterId != _lastIdentity.CharacterId || context.Identity.SlotGeneration != _lastIdentity.SlotGeneration))
            throw new ArgumentException("Foreign Standing inertial frame/owner.");
        machine.ValidateCommit(frame); traversal.ValidateContext(SourceContext(context));
        if (initializeInstance && !machine.Candidate.Reinitialized) throw new ArgumentException("Standing initialization mismatch.");
        if (traversal.HasMovement)
        {
            if (movement is null || _movementOwner is not null && !ReferenceEquals(_movementOwner,movement)) throw new ArgumentException("Standing Movement inertia missing/foreign.");
            movement.ValidateContext(context.Identity);
        }
        else if (movement is not null) throw new ArgumentException("Inactive Standing Movement inertia supplied.");
        if (traversal.OuterSkippedContexts.Length > 0 && traversal.OuterSkippedHandler != NodeIndex) throw new ArgumentException("Foreign Standing skipped handler.");
        _updated.CopyFrom(_committed); _nextRequests.Clear(); _forwarded.Clear(); ForwardAttempts = 0;
        var reset = initializeInstance || _counter.HasUpdated && !_counter.WasSynchronizedCounter(context.UpdateCounter.Value);
        if (reset) { _updated.Reset(); if (!initializeInstance) _updated.Update(_pendingDelta); }
        else _nextRequests.AddRange(_requests);
        _nextDelta = (initializeInstance ? 0 : _pendingDelta) + context.Delta; _updated.Update(context.Delta);
        if (machine.InertializationRequest is {} incoming) Add(incoming);
        if (movement is not null) foreach (var forwarded in movement.ForwardedRequests)
        {
            if (forwarded.Target == NodeIndex) Add(forwarded.Request);
            else _forwarded.Add(forwarded); // Still belongs to another ancestor, not this node.
        }
        foreach (var request in _nextRequests) _updated.Request(request.Duration);
        foreach (var path in traversal.OuterSkippedContexts) foreach (var request in _nextRequests)
        {
            if (path.InertializationRequester < 0) continue;
            ForwardAttempts++;
            if (path.InertializationRequester != NodeIndex) _forwarded.Add(new(path.InertializationRequester,request));
        }
        _identity = context.Identity; _nextCounter = context.UpdateCounter.Value; _machineOwner ??= machine; _traversalOwner ??= traversal; _movementOwner ??= movement;
        _prepared = true; _evaluated = _faulted = false;
        void Add(AlsOverlayInertialRequest request) { if (!_nextRequests.Contains(request)) _nextRequests.Add(request); }
    }
    public void Evaluate(long frame, AlsRefactoredStandingPose.Runtime source, in AlsPrecisePose component, long attachParent = 0, float teleportDistance = 0)
    {
        ValidateCommit(frame); _evaluated = false;
        try
        {
            if (!ReferenceEquals(source.Profile,_profile) || _poseOwner is not null && !ReferenceEquals(_poseOwner,source)) throw new ArgumentException("Foreign Standing pose owner.");
            source.ValidateContext(_identity);
            for (var c = 0; c < _unfiltered.Length; c++) _inputCurves[c] = source.Curves[_unfiltered[c]];
            _evaluatedState.CopyFrom(_updated);
            _evaluatedState.EvaluatePrecisePose(source.Pose,_inputCurves,component,attachParent,teleportDistance,_pose,_outputCurves);
            for (var c = 0; c < _unfiltered.Length; c++) _curves[_unfiltered[c]] = _outputCurves[c];
            _curves[_yaw] = source.Curves[_yaw]; _poseOwner ??= source; _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || _faulted || frame != _identity.FrameId) throw new ArgumentException("Invalid Standing inertial commit."); }
    public void Commit(long frame)
    {
        ValidateCommit(frame);
        if (_evaluated) { (_committed,_evaluatedState) = (_evaluatedState,_committed); _requests.Clear(); _pendingDelta = 0; }
        else { (_committed,_updated) = (_updated,_committed); (_requests,_nextRequests) = (_nextRequests,_requests); _pendingDelta = _nextDelta; }
        _counter = _nextCounter; _lastIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = _evaluated = _faulted = false; }
}
