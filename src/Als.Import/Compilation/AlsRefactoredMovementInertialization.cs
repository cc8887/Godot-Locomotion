using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredForwardedInertialRequest(int Target, AlsOverlayInertialRequest Request);

/// <summary>Original node119 after Movement Details. One candidate owns all
/// requests, skipped-path forwarding, relevance and full-precision pose history.</summary>
public sealed class AlsRefactoredMovementInertialization
{
    public const int NodeIndex = 119;
    private readonly AlsRefactoredMovementDetailsPose _profile;
    private AlsInertialization _committed, _updated, _evaluatedState;
    private List<AlsOverlayInertialRequest> _requests = new(), _nextRequests = new();
    private readonly List<AlsRefactoredForwardedInertialRequest> _forwarded = new();
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsGraphTraversalCounter _counter, _nextCounter;
    private AlsFrameIdentity _identity, _lastIdentity;
    private float _pendingDelta, _nextDelta;
    private bool _prepared, _evaluated, _faulted, _hasCommitted;
    public bool IsActive => _prepared && _evaluated && !_faulted ? _evaluatedState.IsActive : _committed.IsActive;
    public int CommittedHistoryCount => _committed.HistoryCount;
    public int PendingRequests => _prepared ? _evaluated && !_faulted ? 0 : _nextRequests.Count : _requests.Count;
    public int ForwardAttempts { get; private set; }
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Inertial pose unavailable.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Inertial curves unavailable.");
    public IReadOnlyList<AlsRefactoredForwardedInertialRequest> ForwardedRequests => _prepared ? _forwarded.AsReadOnly() : throw new InvalidOperationException("No inertial candidate.");
    public AlsRefactoredMovementInertialization(AlsRefactoredAnimationCatalog catalog, AlsRefactoredMovementDetailsPose profile)
    {
        if (catalog.IndexDigest != profile.Graph.Resources.CatalogDigest) throw new ArgumentException("Foreign inertial catalog.");
        var bp = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        var node = bp.GetProperty("compiled").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("propertyIndex").GetInt32() == NodeIndex);
        Expect(node, new { @class = "AnimGraphNode_Inertialization" });
        foreach (var p in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
        {
            Expect(p, new { defaultBlendProfile = "", bResetOnBecomingRelevant = true, bForwardRequestsThroughSkippedCachedPoseNodes = true, tag = "None" });
            if (p.GetProperty("filteredCurves").GetArrayLength() != 0 || p.GetProperty("filteredBones").GetArrayLength() != 0)
                throw new ArgumentException("Unsupported Movement inertial filters.");
            foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(p.GetProperty(cb), new { functionName = "None" });
        }
        Expect(node.GetProperty("runtime").GetProperty("source"), new { linkId = profile.Graph.Resources.MachinePropertyIndex, sourceLinkId = NodeIndex });
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(false);
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(bp.GetProperty("nativeText").GetString()!, blueprint, blueprint + ":AnimGraph"), true);
        var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.').Last());
        var (child, output) = graph.FollowReroutes(authored, "Source");
        var source = bp.GetProperty("compiled").GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("propertyIndex").GetInt32() == profile.Graph.Resources.MachinePropertyIndex);
        if (authored.Kind != "AnimGraphNode_Inertialization" || output.Name != "Pose" ||
            source.GetProperty("path").GetString() != blueprint + ":AnimGraph." + child.Name)
            throw new ArgumentException("Authored Movement inertial source differs.");
        _profile = profile; _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
        _committed = new(_pose.Length, _curves.Length); _updated = new(_pose.Length, _curves.Length); _evaluatedState = new(_pose.Length, _curves.Length);
    }
    public static AlsPoseUpdateContext SourceContext(in AlsPoseUpdateContext context) => context.WithInertialization(NodeIndex, true);
    public void Prepare(in AlsPoseUpdateContext context, AlsRefactoredMovementDetailsRuntime machine,
        AlsRefactoredMovementTraversal traversal, bool initializeInstance = false)
    {
        var frame = context.Identity.FrameId;
        if (_prepared || frame < 0 || context.UpdateCounter is not { HasUpdated: true } || !context.HasSharedContext ||
            !ReferenceEquals(machine.Resources, _profile.Graph.Resources) || !ReferenceEquals(traversal.Details, _profile.Graph) ||
            _hasCommitted && (frame <= _lastIdentity.FrameId || context.Identity.CharacterId != _lastIdentity.CharacterId || context.Identity.SlotGeneration != _lastIdentity.SlotGeneration))
            throw new ArgumentException("Invalid Movement inertial frame.");
        machine.ValidateCommit(frame); traversal.ValidateCommit(frame); var source = traversal.MovementContext;
        if (source.Identity != context.Identity || source.UpdateCounter != context.UpdateCounter || source.Delta != context.Delta ||
            source.InertializationRequester != NodeIndex || source.SkippedUpdateHandler != NodeIndex || initializeInstance && !machine.Candidate.Reinitialized)
            throw new ArgumentException("Movement inertial traversal differs.");
        _updated.CopyFrom(_committed); _nextRequests.Clear(); _forwarded.Clear(); ForwardAttempts = 0;
        var reset = initializeInstance || _counter.HasUpdated && !_counter.WasSynchronizedCounter(context.UpdateCounter.Value);
        if (reset)
        {
            _updated.Reset();
            // Becoming-relevant reset clears snapshots/requests but UE retains
            // time accumulated by previous Update-only frames. Initialize clears it.
            if (!initializeInstance) _updated.Update(_pendingDelta);
        }
        else _nextRequests.AddRange(_requests);
        _nextDelta = (initializeInstance ? 0 : _pendingDelta) + context.Delta;
        _updated.Update(context.Delta);
        if (machine.InertializationRequest is { } incoming && !_nextRequests.Contains(incoming)) _nextRequests.Add(incoming);
        foreach (var request in _nextRequests) _updated.Request(request.Duration);
        foreach (var batch in traversal.SkippedBatches)
        {
            if (batch.Handler != NodeIndex) throw new ArgumentException("Foreign Movement skipped handler.");
            foreach (var path in traversal.SkippedContexts.Slice(batch.First, batch.Count))
                foreach (var request in _nextRequests)
                {
                    if (path.InertializationRequester < 0) continue;
                    ForwardAttempts++;
                    if (path.InertializationRequester != NodeIndex) _forwarded.Add(new(path.InertializationRequester, request));
                    // Requests routed back to this same node are AddUnique no-ops.
                }
        }
        _identity = context.Identity; _nextCounter = context.UpdateCounter.Value; _prepared = true; _evaluated = _faulted = false;
    }
    public void Evaluate(long frame, ReadOnlySpan<AlsPrecisePose> input, ReadOnlySpan<AlsInertialCurve> curves,
        in AlsPrecisePose component, long attachParent = 0, float teleportDistance = 0)
    {
        ValidateCommit(frame); _evaluated = false;
        try
        {
            _evaluatedState.CopyFrom(_updated);
            _evaluatedState.EvaluatePrecisePose(input, curves, component, attachParent, teleportDistance, _pose, _curves);
            _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || _faulted || frame != _identity.FrameId) throw new ArgumentException("Invalid Movement inertial commit."); }
    public void Commit(long frame)
    {
        ValidateCommit(frame);
        if (_evaluated) { (_committed, _evaluatedState) = (_evaluatedState, _committed); _requests.Clear(); _pendingDelta = 0; }
        else { (_committed, _updated) = (_updated, _committed); (_requests, _nextRequests) = (_nextRequests, _requests); _pendingDelta = _nextDelta; }
        _counter = _nextCounter; _lastIdentity = _identity; _hasCommitted = true; Cancel();
    }
    public void Cancel() { _prepared = _evaluated = _faulted = false; }
}
