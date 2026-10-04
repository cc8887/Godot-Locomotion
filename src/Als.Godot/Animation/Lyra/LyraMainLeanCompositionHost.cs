using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainRotationInput(double Pitch, double Yaw, double Roll,
    bool IsFirstUpdate, bool CrouchingAtRotation, bool AdsAtRotation);
internal readonly record struct LyraMainRotationState(double Pitch, double Yaw, double Roll,
    double YawDelta, double YawSpeed, double LeanAngle)
{
    public LyraMainRotationState Prepare(in LyraMainRotationInput input, float delta)
    {
        if (!double.IsFinite(input.Pitch) || !double.IsFinite(input.Yaw) || !double.IsFinite(input.Roll) ||
            !float.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid Main rotation snapshot.");
        // Original UpdateRotationData runs before CharacterStateData. It uses
        // BreakRotator narrows both Yaw pins to float, then the promoted
        // subtract operates on doubles. No unwind; first reset keeps speed.
        var difference = (double)(float)input.Yaw - (double)(float)Yaw;
        var speed = delta != 0 ? difference / (double)delta : 0;
        var angle = speed * (input.CrouchingAtRotation || input.AdsAtRotation ? .025 : .0375);
        if (!double.IsFinite(speed) || !double.IsFinite(angle)) throw new ArgumentException("Nonfinite Main rotation output.");
        return new(input.Pitch, input.Yaw, input.Roll, input.IsFirstUpdate ? 0 : difference,
            speed, input.IsFirstUpdate ? 0 : angle);
    }
}
internal sealed record LyraMainLeanCompositionCandidate(LyraMainRotationState Rotation, LyraMainLeanSourceCandidate Lean);
internal sealed record LyraMainLeanCompositionResolved(LyraMainLeanCompositionCandidate Candidate, LyraMainLeanResolved Lean);

// Original Main rotation/Lean boundary. The enclosing Main owns FirstUpdate
// and state refresh order; caller-provided complete base frames come from its
// linked provider roots. One outer Sync and one outer commit remain required.
internal sealed class LyraMainLeanCompositionHost
{
    internal bool HasPending=>_pending is not null;
    private readonly LyraMainLeanSourceHost _lean;
    private readonly int _curves, _attributes;
    private readonly AlsPrecisePose[][] _additive = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
    private readonly AlsPrecisePose[][] _staged = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
    private readonly int[] _evaluating = new int[3];
    private LyraMainLeanCompositionCandidate? _pending;
    private LyraMainLeanCompositionResolved? _resolved;
    public LyraMainRotationState Rotation { get; private set; }
    public System.Collections.Immutable.ImmutableArray<LyraMainLeanNodeState> LeanStates => _lean.States;
    internal bool InitializeSourceNode(int node,double angle)
    {
        if(_pending is not null)throw new InvalidOperationException("Main Lean phase initialization needs an idle composition.");
        return _lean.InitializeSourceNode(node,angle);
    }

    public LyraMainLeanCompositionHost(LyraLogicalSourceBank bank, AlsAssetSyncSequence[] sequences,
        int playerBase, int assetId, int sequenceBase, long epoch)
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "composition_v3_policy.json"));
        var policy = document.RootElement;
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + dependency.Name)))
                throw new InvalidOperationException("Stale Main composition dependency: " + dependency.Name);
        if (policy.GetProperty("schemaVersion").GetInt32() != 3 || policy.GetProperty("rotationFunction").GetString() != "UpdateRotationData" ||
            policy.GetProperty("standingCoefficient").GetDouble() != .0375 || policy.GetProperty("crouchOrAdsCoefficient").GetDouble() != .025 ||
            policy.GetProperty("yawDifference").GetString() != "BreakRotatorFloatPromotedDoubleSubtract" ||
            policy.GetProperty("breakRotatorYawType").GetString() != "float" ||
            policy.GetProperty("delta").GetString() != "GetDeltaSecondsFloatPromotedDouble" ||
            policy.GetProperty("rotationOrder").GetString() != "beforeUpdateCharacterStateData" ||
            !policy.GetProperty("firstReset").EnumerateArray().Select(v => v.GetString()).SequenceEqual(new[] { "YawDeltaSinceLastUpdate", "AdditiveLeanAngle" }))
            throw new NotSupportedException("Changed original Main rotation policy.");
        var graph = policy.GetProperty("graph").EnumerateArray().ToArray();
        if (graph.Length != 3 || graph.Select(n => n.GetProperty("index").GetInt32()).Distinct().Count() != 3)
            throw new InvalidOperationException("Incomplete Main additive graph.");
        var indices = new[] { 22, 16, 12 }; var layers = new[] { "FullBody_PivotState", "FullBody_CycleState", "FullBody_StartState" };
        for (var i = 0; i < 3; i++)
        {
            var node = graph.Single(n => n.GetProperty("additiveIndex").GetInt32() == indices[i]);
            var settings = node.GetProperty("settings"); var bias = settings.GetProperty("alphaScaleBias");
            var clamp = settings.GetProperty("alphaScaleBiasClamp");
            if (node.GetProperty("layer").GetString() != layers[i] || settings.GetProperty("alpha").GetSingle() != 1 ||
                settings.GetProperty("alphaInputType").GetString() != "Float" || settings.GetProperty("lODThreshold").GetInt32() != -1 ||
                bias.GetProperty("scale").GetSingle() != 1 || bias.GetProperty("bias").GetSingle() != 0 ||
                clamp.GetProperty("bMapRange").GetBoolean() || clamp.GetProperty("bClampResult").GetBoolean() || clamp.GetProperty("bInterpResult").GetBoolean())
                throw new NotSupportedException("Changed Main ApplyAdditive policy.");
        }
        var initial = policy.GetProperty("initial");
        Rotation = new(initial.GetProperty("pitch").GetDouble(), initial.GetProperty("yaw").GetDouble(), initial.GetProperty("roll").GetDouble(),
            initial.GetProperty("yawDelta").GetDouble(), initial.GetProperty("yawSpeed").GetDouble(), initial.GetProperty("angle").GetDouble());
        _lean = new(bank, sequences, playerBase, assetId, sequenceBase, epoch);
        _curves = bank.Curves.Names.Length; _attributes = bank.Curves.Attributes.Layout.Length;
    }
    public LyraMainLeanCompositionCandidate Prepare(in LyraMainRotationInput input, float delta,
        ReadOnlySpan<float> weights, ReadOnlySpan<bool> active, ReadOnlySpan<bool> initialize, ReadOnlySpan<int> order)
    {
        if (_pending is not null) throw new InvalidOperationException("Main composition frame is pending.");
        var rotation = Rotation.Prepare(input, delta);
        return _pending = new(rotation, _lean.Prepare(rotation.LeanAngle, delta, weights, active, initialize, order));
    }
    // Main already computed RotationData before CharacterStateData. Consume
    // that same candidate; a second rotation update would use different history.
    public LyraMainLeanCompositionCandidate PrepareObserved(LyraMainUpdateCandidate main, float delta,
        ReadOnlySpan<float> weights, ReadOnlySpan<bool> active, ReadOnlySpan<bool> initialize, ReadOnlySpan<int> order)
    {
        if (_pending is not null) throw new InvalidOperationException("Main composition frame is pending.");
        return _pending = new(main.State.Rotation, _lean.Prepare(main.State.Rotation.LeanAngle, delta, weights, active, initialize, order));
    }
    public LyraMainLeanSyncInputs CollectAtCommonSync(LyraMainLeanCompositionCandidate candidate, int sampleStart = 0)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Stale Main composition collection.");
        return _lean.CollectAtCommonSync(candidate.Lean, sampleStart);
    }
    public LyraMainLeanCompositionResolved Resolve(LyraMainLeanCompositionCandidate candidate, LyraMainLeanSyncInputs inputs,
        ReadOnlySpan<AlsAssetPlayerHistory> players, ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        if (!ReferenceEquals(candidate, _pending) || _resolved is not null || !ReferenceEquals(candidate.Lean, inputs.Candidate))
            throw new InvalidOperationException("Stale Main composition Sync result.");
        return _resolved = new(candidate, _lean.Resolve(inputs, players, samples));
    }
    public LyraRootMotionAttribute Evaluate(LyraMainLeanCompositionResolved resolved, int node,
        ReadOnlySpan<AlsPrecisePose> basis, ReadOnlySpan<LyraCurveSample> curves, ReadOnlySpan<LyraAttributeSample> attributes,
        LyraRootMotionAttribute rootMotion, Span<AlsPrecisePose> output, Span<LyraCurveSample> outputCurves, Span<LyraAttributeSample> outputAttributes)
    {
        if (!ReferenceEquals(resolved, _resolved) || node is < 0 or > 2 || !resolved.Candidate.Lean.Active[node])
            throw new InvalidOperationException("Main composition needs its active resolved occurrence.");
        LyraPoseBuffers.Validate(basis, output);
        if (curves.Length != _curves || outputCurves.Length != _curves || curves.Overlaps(outputCurves) ||
            attributes.Length != _attributes || outputAttributes.Length != _attributes || attributes.Overlaps(outputAttributes))
            throw new ArgumentException("Incomplete or overlapping Main metadata buffers.");
        foreach (var bone in basis) bone.Validate();
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite Main curve.");
        if (rootMotion.Present) rootMotion.Value.Validate();
        if (Interlocked.CompareExchange(ref _evaluating[node], 1, 0) != 0) throw new InvalidOperationException("Main occurrence is being evaluated.");
        try
        {
            var additive = _additive[node]; var staged = _staged[node];
            _lean.Evaluate(resolved.Lean, node, additive);
            // Original ApplyAdditive uses full-weight ISPC accumulation and
            // normalizes in both AnimationRuntime and the graph node.
            for (var i = 0; i < 81; i++) { staged[i] = AlsPrecisePoseBlender.LocalApplyFullWeightIsPc(basis[i], additive[i]).Normalized(); staged[i].Validate(); }
            // These three Lean resources have no curves/attributes. Preserve
            // the entire provider data, including root presence and identity.
            staged.CopyTo(output); curves.CopyTo(outputCurves); attributes.CopyTo(outputAttributes);
            return rootMotion;
        }
        finally { Volatile.Write(ref _evaluating[node], 0); }
    }
    public void Commit(LyraMainLeanCompositionResolved resolved)
    {
        ValidateCommit(resolved);
        _lean.Commit(resolved.Lean); Rotation = resolved.Candidate.Rotation; _pending = null; _resolved = null;
    }
    public void ValidateCommit(LyraMainLeanCompositionResolved resolved)
    {
        if (!ReferenceEquals(resolved, _resolved) || _evaluating.Any(v => v != 0))
            throw new InvalidOperationException("Stale/repeated or evaluating Main composition commit.");
        _lean.ValidateCommit(resolved.Lean);
    }
    public void Cancel() { _lean.Cancel(); _pending = null; _resolved = null; }
}
