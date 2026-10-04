using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraFootPlantRigPoseCandidate(LyraFootPlantRigPoseHost Owner,
    LyraFootPlantRigUpdateCandidate Update);

// One character owns the node, immediate VM, hierarchy and private dynamics.
// All evaluation attempts start at the prepared state. Only the enclosing
// character commit publishes the solved candidate; this owner never ticks Sync.
internal sealed class LyraFootPlantRigPoseHost
{
    private const string Root = "res://assets/generated/lyra_als/";
    private readonly LyraFootPlantRigUpdateHost _node;
    private readonly LyraRigCompiledTraversal _traversal;
    private readonly LyraFootPlantRigOutputTransfer _adapter;
    private readonly string?[] _mapping;
    private readonly LyraCompositionPoseBuffer _output;
    private readonly LyraFootPlantRigReferenceProfile? _reference;
    private readonly bool _initializeVmDuringEvaluation;
    private bool _referenceSetterInvalid, _constructionPending;
    private bool _preparedReferenceInvalid, _preparedConstructionPending, _evaluatedConstructionPending;
    private bool _initializationCallbackPending, _preparedInitializationCallbackPending;
    private bool _evaluatedInitializationCallbackPending, _evaluatedReferenceInvalid;
    private LyraFootPlantRigExecutor _committed;
    private LyraFootPlantRigExecutor? _prepared, _evaluated;
    private LyraFootPlantRigPoseCandidate? _pending;
    private bool _failed;
    public LyraFootPlantRigUpdateState State => _node.State;
    public float CandidateAlpha => _pending is null || _failed ? throw new InvalidOperationException("No Rig candidate.") : _pending.Update.Updated.Alpha;
    private static JsonDocument Load(string name) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + name + ".json"));
    public LyraFootPlantRigPoseHost(LyraLogicalSourceBank bank, long epoch,
        LyraFootPlantRigReference reference = LyraFootPlantRigReference.AuthoredRig,
        bool initializeVmDuringEvaluation = false)
    {
        _initializeVmDuringEvaluation = initializeVmDuringEvaluation;
        using var policy = Load("rig_pose_v1_policy"); using var p = Load("rig_traversal_v1_program");
        using var g = Load("footplant_rig_graph_v1"); using var s = Load("rig_control_settings_v1");
        using var c = Load("logical_controls/calibration");
        if (policy.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new NotSupportedException("Changed Rig pose policy.");
        foreach (var d in policy.RootElement.GetProperty("dependencies").EnumerateObject())
            if (d.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)))
                throw new InvalidOperationException("Stale Rig pose dependency: " + d.Name);
        var descriptor = policy.RootElement.GetProperty("descriptor");
        _mapping = descriptor.GetProperty("mapping").EnumerateArray().Select(v => v.GetProperty("rigBone").GetString() is { Length: > 0 } name ? name : null).ToArray();
        var bones = g.RootElement.GetProperty("hierarchy").EnumerateArray().Where(v => v.GetProperty("type").GetString()!.Contains("BONE"))
            .Select(v => v.GetProperty("name").GetString()!).ToArray();
        var names = c.RootElement.GetProperty("layout").GetProperty("logicalBoneNames");
        for (int b = 0; b < 81; b++)
            if (bank.Bone(names[b].GetString()!) != b || bank.Parents[b] != descriptor.GetProperty("mapping")[b].GetProperty("parent").GetInt32() ||
                _mapping[b] != bones.FirstOrDefault(n => StringComparer.OrdinalIgnoreCase.Equals(n, names[b].GetString())))
                throw new NotSupportedException("Foreign ALS Rig target mapping.");
        var program = p.RootElement.Clone(); var graph = g.RootElement.Clone();
        _traversal = new(program); _adapter = new(descriptor); _output = new(bank);
        _committed = new(program, graph, new(graph, program.GetProperty("initial").GetProperty("hierarchy"), s.RootElement.GetProperty("controls")));
        if (reference == LyraFootPlantRigReference.AlsCompactReference)
        {
            _reference = LyraFootPlantRigReferenceProfile.Load(bank);
            _reference.Apply(_committed.Hierarchy);
        }
        else if (reference != LyraFootPlantRigReference.AuthoredRig)
            throw new ArgumentOutOfRangeException(nameof(reference));
        _node = new(LyraFootPlantRigNodeContract.Load(), epoch);
    }
    public LyraFootPlantRigPoseCandidate Prepare(long epoch, long frame, float delta, bool visited, bool initialize,
        bool crouching, bool moving, bool resolvedBool)
    {
        if (_pending is not null) throw new InvalidOperationException("Rig pose frame is pending.");
        var update = _node.Prepare(epoch, frame, delta, visited, initialize, crouching, moving, resolvedBool);
        _pending = new(this, update); _failed = false; _evaluated = null;
        try
        {
            _prepared = _committed.Clone();
            _preparedReferenceInvalid = _referenceSetterInvalid;
            _preparedConstructionPending = _constructionPending;
            _preparedInitializationCallbackPending = _initializationCallbackPending;
            if (initialize)
            {
                _prepared.Reset(); _prepared.Memory.Write(2, "isCrouching", update.Updated.Crouching);
                _prepared.Memory.Write(2, "isMoving2D", update.Updated.Moving);
                _prepared.BeginExecution(0, null); _traversal.Execute("Construction", _prepared);
                // Native VM initialization invalidates RefPoseSetterHash. The
                // next visited Update binds the compact reference again and
                // requests another Construction before Evaluate imports pose.
                _preparedReferenceInvalid = _reference is not null;
                _preparedConstructionPending = false;
                _preparedInitializationCallbackPending = _initializeVmDuringEvaluation;
            }
            if (visited)
            {
                _prepared.Memory.Write(2, "isCrouching", crouching); _prepared.Memory.Write(2, "isMoving2D", moving);
                if (_preparedReferenceInvalid)
                {
                    _reference!.Apply(_prepared.Hierarchy);
                    _preparedReferenceInvalid = false;
                    _preparedConstructionPending = true;
                }
            }
            return _pending;
        }
        catch { Cancel(); throw; }
    }
    private void Validate(LyraFootPlantRigPoseCandidate candidate)
    { if (_failed || !ReferenceEquals(candidate, _pending) || !ReferenceEquals(candidate.Owner, this)) throw new InvalidOperationException("Foreign, stale or failed Rig pose candidate."); }
    public LyraLayerPoseInput Evaluate(LyraFootPlantRigPoseCandidate candidate, scoped in LyraLayerPoseInput source, ILyraFootPlantRigCollision collision)
    {
        Validate(candidate); if (!candidate.Update.Visited) throw new InvalidOperationException("Hidden Rig evaluation.");
        ArgumentNullException.ThrowIfNull(collision); _evaluated = null;
        try
        {
            _output.Validate(source); var next = _prepared!.Clone(); var alpha = candidate.Update.Updated.Alpha;
            bool constructionExecuted = _preparedConstructionPending;
            if (constructionExecuted)
            {
                var current = next.Hierarchy.CaptureConstructionPose(initial: false);
                _ = next.Hierarchy.CaptureConstructionPose(initial: true);
                next.Hierarchy.Reset();
                next.BeginExecution(0, null); _traversal.Execute("Construction", next);
                next.Hierarchy.RestoreConstructionPose(current);
            }
            if (alpha > 1e-5f)
            {
                next.Hierarchy.ImportAdapterLocalPose(_mapping, source.Pose);
                next.BeginExecution(constructionExecuted ? 0 : candidate.Update.Updated.RigDelta, collision); _traversal.Execute("Forwards Solve", next);
            }
            _adapter.Export(next.Hierarchy, source.Pose, alpha, _output.Pose);
            LyraFootPlantRigOutputTransfer.CopyChannels(source, alpha, _output);
            _node.CompleteEvaluation(candidate.Update, constructionExecuted);
            _evaluatedReferenceInvalid = _preparedReferenceInvalid;
            _evaluatedInitializationCallbackPending = _preparedInitializationCallbackPending;
            if (_preparedInitializationCallbackPending && (constructionExecuted || alpha > 1e-5f))
            {
                // Full Main initializes the VM during its first Evaluate.
                // OnInitialized invalidates the node's reference setter after
                // Update has already bound it. The next visited Update requests
                // Construction again. Standalone pre-initialized nodes retain
                // their existing CacheBones lifecycle.
                _evaluatedReferenceInvalid = _reference is not null;
                _evaluatedInitializationCallbackPending = false;
            }
            _evaluatedConstructionPending = false; _evaluated = next; return _output.Input;
        }
        catch { _failed = true; throw; }
    }
    public void ValidateCommit(LyraFootPlantRigPoseCandidate candidate, bool updateOnly)
    { Validate(candidate); if (updateOnly ? _evaluated is not null : _evaluated is null) throw new InvalidOperationException("Incomplete Rig pose transaction."); _node.ValidateCommit(candidate.Update, updateOnly); }
    public void Commit(LyraFootPlantRigPoseCandidate candidate, bool updateOnly)
    {
        ValidateCommit(candidate, updateOnly); _node.Commit(candidate.Update, updateOnly);
        _committed = _evaluated ?? _prepared!;
        _referenceSetterInvalid = _evaluated is null ? _preparedReferenceInvalid : _evaluatedReferenceInvalid;
        _initializationCallbackPending = _evaluated is null ? _preparedInitializationCallbackPending : _evaluatedInitializationCallbackPending;
        _constructionPending = _evaluated is null ? _preparedConstructionPending : _evaluatedConstructionPending;
        _pending = null; _prepared = _evaluated = null;
    }
    public void Cancel()
    { if (_pending is { } candidate) _node.Cancel(candidate.Update); _pending = null; _prepared = _evaluated = null; _failed = false; }
}
