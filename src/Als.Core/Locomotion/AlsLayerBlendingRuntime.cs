using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

/// <summary>Callbacks operate on source-owner candidates only. Input and Slot names are native labels.
/// Evaluate never advances clocks. The owner commits or cancels its candidates together with this runtime.</summary>
public interface IAlsLayerBlendingSink
{
    void InitializeInput(int nodeIndex, string name);
    void CacheInputBones(int nodeIndex, string name);
    void UpdateInput(int nodeIndex, string name, in AlsPoseUpdateContext context);
    void EvaluateInput(int nodeIndex, string name, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
    void InitializeSlot(int nodeIndex, string name);
    AlsSlotWeights GetSlotWeights(int nodeIndex, string name, in AlsPoseUpdateContext context);
    void UpdateSlot(int nodeIndex, string name, in AlsSlotWeights weights,
        in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context);
    // Spans retain their full layout when the source is not evaluated. Their placeholder atoms
    // must not contribute to the result in that case; only the Slot owner's active output does.
    void EvaluateSlot(int nodeIndex, string name, in AlsSlotWeights weights, bool sourceEvaluated,
        ReadOnlySpan<AlsLocalPose> sourcePose, ReadOnlySpan<AlsInertialCurve> sourceCurves,
        Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
    void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped);
}

/// <summary>Native LayerBlending traversal with deferred SaveCachedPose updates and scoped evaluation.
/// Only explicit UseCachedPose nodes share evaluation; ordinary nodes execute on every visited pose link.</summary>
public sealed class AlsLayerBlendingRuntime : IAlsPoseCacheUpdateSink, IAlsPoseCachePoseSink
{
    private sealed class Plan
    {
        public readonly int[] CurveIndices, StaticSources;
        public readonly float[] StaticWeights;
        public Plan(AlsLayerPoseNode node, string[] names, int[] parents, string[] curves)
        {
            CurveIndices = node.Alphas.Select(a => a.Kind == AlsLayerAlphaKind.Curve ? Array.IndexOf(curves, a.Name) : -1).ToArray();
            foreach (var alpha in node.Alphas)
                if (alpha.Kind == AlsLayerAlphaKind.Property) _ = default(AlsLayeringInput).GetValue(alpha.Name);
            if (node.Kind != AlsLayerPoseKind.LayeredBlend) { StaticSources = []; StaticWeights = []; return; }
            foreach (var layer in node.Filters!)
            foreach (var filter in layer)
                if (!names.Contains(filter.Bone, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("LayerBlending requires its complete bone layout, including virtual branch bones: " + filter.Bone);
            StaticSources = new int[names.Length]; StaticWeights = new float[names.Length];
            AlsLayeredBonePoseBlend.BuildWeights(names, parents, node.Filters!, StaticSources, StaticWeights);
        }
    }
    private sealed class NodeState
    {
        public readonly float[] Alphas, BoneWeights, MaxPoseWeights;
        public readonly int[] BoneSources;
        public AlsSlotWeights Slot;
        public NodeState(AlsLayerPoseNode node, int bones)
        {
            Alphas = new float[node.Alphas.Length];
            var layered = node.Kind == AlsLayerPoseKind.LayeredBlend;
            BoneWeights = new float[layered ? bones : 0]; BoneSources = new int[BoneWeights.Length];
            MaxPoseWeights = new float[layered ? node.Alphas.Length : 0];
        }
        public void CopyFrom(NodeState source)
        {
            source.Alphas.CopyTo(Alphas, 0); source.BoneWeights.CopyTo(BoneWeights, 0);
            source.BoneSources.CopyTo(BoneSources, 0); source.MaxPoseWeights.CopyTo(MaxPoseWeights, 0); Slot = source.Slot;
        }
    }
    private sealed class Bank
    {
        public readonly NodeState?[] States;
        public AlsFrameIdentity Identity;
        public AlsGraphTraversalCounter Initialization, Bones, Evaluation;
        public int SourceUpdates;
        public Bank(AlsLayerBlendingDefinition definition, int bones)
        {
            States = new NodeState[definition.Caches.NodeCount];
            foreach (var node in definition.Nodes) States[node.Index] = new(node, bones);
        }
        public void CopyFrom(Bank source)
        {
            for (var i = 0; i < States.Length; i++) if (States[i] is { } state) state.CopyFrom(source.States[i]!);
            Identity = source.Identity; Initialization = source.Initialization; Bones = source.Bones; Evaluation = source.Evaluation;
            SourceUpdates = source.SourceUpdates;
        }
    }

    private readonly AlsLayerBlendingDefinition _definition;
    private readonly AlsLayerPoseNode[] _nodes;
    private readonly Plan?[] _plans;
    private readonly int[] _parents;
    private readonly string[] _curveNames;
    private readonly AlsLocalPose[] _referencePose, _workPose;
    private readonly AlsInertialCurve[] _previousCurves, _workCurves;
    private readonly Quaternion[] _rotationScratch;
    private readonly int _boneCount, _curveCount, _frameWidth, _frameCapacity;
    private readonly AlsPoseCacheTraversal _updates;
    private Bank _committed, _candidate;
    private AlsPoseCacheEvaluation _committedCache, _candidateCache;
    private IAlsLayerBlendingSink? _sink;
    private AlsLayeringInput _input;
    private AlsPoseCacheScope _scope;
    private int _sourceEvaluationDepth;
    private bool _prepared, _evaluated, _busy, _scopeOpen, _unvisited;

    public bool HasCandidate => _prepared;
    public AlsFrameIdentity Identity => _prepared ? _candidate.Identity : _committed.Identity;
    public int SourceUpdateCount => (_prepared ? _candidate : _committed).SourceUpdates;
    public int SourceEvaluationCount => (_prepared ? _candidateCache : _committedCache).SourceEvaluations;
    public ReadOnlySpan<string> CurveNames => _curveNames;

    public AlsLayerBlendingRuntime(AlsLayerBlendingDefinition definition, ReadOnlySpan<string> boneNames,
        ReadOnlySpan<int> parents, ReadOnlySpan<string> curveNames, ReadOnlySpan<AlsLocalPose> referencePose)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (boneNames.IsEmpty || parents.Length != boneNames.Length || referencePose.Length != boneNames.Length)
            throw new ArgumentException("LayerBlending requires complete parent-first bones and their actual reference pose.");
        var names = boneNames.ToArray(); _parents = parents.ToArray(); _curveNames = curveNames.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
            _curveNames.Any(string.IsNullOrWhiteSpace) || _curveNames.Distinct(StringComparer.Ordinal).Count() != _curveNames.Length)
            throw new ArgumentException("Invalid or duplicate LayerBlending bone/curve names.");
        for (var bone = 0; bone < parents.Length; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone) throw new ArgumentException("LayerBlending requires parent-first bones.");
        ValidatePayload(referencePose, []);
        _definition = definition; _nodes = definition.Nodes.ToArray(); _boneCount = names.Length; _curveCount = curveNames.Length;
        _referencePose = referencePose.ToArray(); _previousCurves = new AlsInertialCurve[_curveCount];
        _plans = new Plan[definition.Caches.NodeCount];
        foreach (var node in _nodes) _plans[node.Index] = new(node, names, _parents, _curveNames);
        _frameWidth = System.Math.Max(2, _nodes.Max(n => n.Inputs.Length)); _frameCapacity = checked(_nodes.Length + 2);
        _workPose = new AlsLocalPose[checked(_frameCapacity * _frameWidth * _boneCount)];
        _workCurves = new AlsInertialCurve[checked(_frameCapacity * _frameWidth * _curveCount)];
        _rotationScratch = new Quaternion[checked(_boneCount * 3)];
        _committed = new(definition, _boneCount); _candidate = new(definition, _boneCount);
        _committedCache = NewCache(); _candidateCache = NewCache();
        // Every ordinary link may be visited; a Save source is drained at most once.
        _updates = new(definition.Caches, checked(_nodes.Length * _frameWidth + definition.Caches.Reads.Length + 1));
    }

    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput input,
        ReadOnlySpan<AlsInertialCurve> previousCommittedCurves, AlsGraphTraversalCounter initialization,
        AlsGraphTraversalCounter bones, AlsGraphTraversalCounter evaluation, IAlsLayerBlendingSink sink,
        bool updateSource = true)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_prepared || _busy || context.Identity.SlotGeneration == 0 || input.Identity != context.Identity ||
            !initialization.HasUpdated || !bones.HasUpdated || !evaluation.HasUpdated)
            throw new InvalidOperationException("Invalid LayerBlending candidate or traversal counters.");
        ValidateFeedback(context.Identity, input, previousCommittedCurves);
        _candidateCache.BeginCandidate(context.Identity, _committedCache);
        _candidate.CopyFrom(_committed); _candidate.Identity = context.Identity; _candidate.Evaluation = evaluation;
        _candidate.SourceUpdates = 0;
        _input = input; _sink = sink; _prepared = true; _evaluated = false; _busy = true; _unvisited=!updateSource;
        _previousCurves.AsSpan().Clear(); previousCommittedCurves.CopyTo(_previousCurves);
        try
        {
            if (!_candidate.Initialization.MatchesCounter(initialization))
            {
                _candidate.Initialization = initialization;
                InitializeNode(_definition.RootIndex);
            }
            if (!_candidate.Bones.MatchesAll(bones))
            {
                _candidate.Bones = bones;
                CacheNodeBones(_definition.RootIndex);
            }
            if(updateSource)
            {
                _updates.Begin(context.Identity);
                UpdateNode(_definition.RootIndex, context);
                _updates.Drain(this);
                _candidate.SourceUpdates = _updates.SourceUpdateCount;
            }
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void Evaluate(Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (!_prepared || _unvisited || _busy || pose.Length != _boneCount || curves.Length != _curveCount)
            throw new InvalidOperationException("LayerBlending is not prepared or the output layout differs.");
        _busy = true;
        try
        {
            _scope = _candidateCache.PushScope(); _scopeOpen = true;
            EvaluateNode(_definition.RootIndex, pose, curves, 0);
            ValidatePayload(pose, curves);
            _candidateCache.PopScope(_scope); _scopeOpen = false; _evaluated = true;
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void Commit()
    {
        ValidateCommit();
        (_committed, _candidate) = (_candidate, _committed);
        (_committedCache, _candidateCache) = (_candidateCache, _committedCache);
        _prepared = _evaluated = false; _sink = null;
    }

    public void ValidateCommit()
    {
        if (!_prepared || !(_evaluated || _unvisited) || _busy || _scopeOpen || _candidateCache.IsFaulted)
            throw new InvalidOperationException("LayerBlending requires a successfully evaluated, closed candidate before commit.");
    }

    public void Cancel()
    {
        if (_busy) throw new InvalidOperationException("Cannot cancel LayerBlending inside a source callback.");
        Abort();
    }

    private void Abort()
    {
        if (_scopeOpen)
        {
            try { _candidateCache.PopScope(_scope); }
            catch { _candidateCache = NewCache(); }
            _scopeOpen = false;
        }
        if (_candidateCache.IsFaulted) _candidateCache = NewCache();
        _prepared = _evaluated = false; _sink = null;
    }
    private AlsPoseCacheEvaluation NewCache() => new(_definition.Caches, _boneCount, _curveCount, 1);

    private void InitializeNode(int index)
    {
        var node = _definition.Node(index);
        if (node.Kind == AlsLayerPoseKind.UseCache)
        { _candidateCache.Initialize(index, _candidate.Initialization, this); return; }
        if (node.Kind == AlsLayerPoseKind.Input) { _sink!.InitializeInput(index, node.Label); return; }
        foreach (var child in node.Inputs) InitializeNode(child);
        if (node.Kind == AlsLayerPoseKind.Slot)
        { _candidate.States[index]!.Slot = default; _sink!.InitializeSlot(index, node.Label); }
        if (node.Kind == AlsLayerPoseKind.TwoWayBlend) _candidate.States[index]!.Alphas[0] = 0;
    }
    private void CacheNodeBones(int index)
    {
        var node = _definition.Node(index);
        if (node.Kind == AlsLayerPoseKind.UseCache)
        { _candidateCache.CacheBones(index, _candidate.Bones, this); return; }
        if (node.Kind == AlsLayerPoseKind.Input) { _sink!.CacheInputBones(index, node.Label); return; }
        foreach (var child in node.Inputs) CacheNodeBones(child);
    }

    private void UpdateNode(int index, in AlsPoseUpdateContext context)
    {
        var node = _definition.Node(index); var state = _candidate.States[index]!;
        switch (node.Kind)
        {
            case AlsLayerPoseKind.Input: _sink!.UpdateInput(index, node.Label, context); return;
            case AlsLayerPoseKind.UseCache: _updates.Use(index, context); return;
            case AlsLayerPoseKind.Root:
            case AlsLayerPoseKind.SaveCache: UpdateNode(node.Inputs[0], context); return;
            case AlsLayerPoseKind.DynamicLocalAdditive:
            case AlsLayerPoseKind.DynamicMeshAdditive:
                UpdateNode(node.Inputs[0], context); UpdateNode(node.Inputs[1], context); return;
            case AlsLayerPoseKind.ApplyLocalAdditive:
            case AlsLayerPoseKind.ApplyMeshAdditive:
                UpdateNode(node.Inputs[0], context);
                state.Alphas[0] = System.Math.Clamp(ReadAlpha(node, 0), 0, 1);
                if (Relevant(state.Alphas[0])) UpdateNode(node.Inputs[1], context.WithWeight(context.Weight * state.Alphas[0]));
                return;
            case AlsLayerPoseKind.TwoWayBlend:
                var alpha = state.Alphas[0] = System.Math.Clamp(ReadAlpha(node, 0), 0, 1);
                if (!Relevant(alpha)) UpdateNode(node.Inputs[0], context);
                else if (Full(alpha)) UpdateNode(node.Inputs[1], context);
                else
                {
                    UpdateNode(node.Inputs[0], context.WithWeight(context.Weight * (1 - alpha)));
                    UpdateNode(node.Inputs[1], context.WithWeight(context.Weight * alpha));
                }
                return;
            case AlsLayerPoseKind.Slot:
                var weights = _sink!.GetSlotWeights(index, node.Label, context); weights.Validate();
                var source = AlsSlotSourceUpdate.Resolve(state.Slot.SourceWeight, weights, context, alwaysUpdateSource: false);
                state.Slot = weights;
                _sink.UpdateSlot(index, node.Label, weights, source, context);
                if (source.Updated) UpdateNode(node.Inputs[0], source.Context);
                return;
            case AlsLayerPoseKind.LayeredBlend:
                var plan = _plans[index]!;
                for (var layer = 0; layer < state.Alphas.Length; layer++) state.Alphas[layer] = ReadAlpha(node, layer);
                AlsLayeredBonePoseBlend.UpdateWeights(plan.StaticSources, plan.StaticWeights, state.Alphas,
                    state.BoneSources, state.BoneWeights, state.MaxPoseWeights);
                var rootWeight = Relevant(state.BoneWeights[0]) ? state.BoneWeights[0] : 0;
                var rootSource = rootWeight > 0 ? state.BoneSources[0] : -1;
                for (var layer = 0; layer < state.Alphas.Length; layer++)
                    if (Relevant(state.Alphas[layer]))
                        UpdateNode(node.Inputs[layer + 1], context.WithWeight(context.Weight * state.Alphas[layer], layer == rootSource ? rootWeight : 0));
                // Native only attenuates the base root motion when its remaining weight is below relevance.
                var baseRootWeight = 1 - rootWeight;
                UpdateNode(node.Inputs[0], baseRootWeight < AlsPoseBlender.WeightThreshold ? context.WithWeight(context.Weight, baseRootWeight) : context);
                return;
            default: throw new InvalidOperationException("Unsupported LayerBlending node.");
        }
    }

    private float ReadAlpha(AlsLayerPoseNode node, int alphaIndex)
    {
        var alpha = node.Alphas[alphaIndex]; var curve = _plans[node.Index]!.CurveIndices[alphaIndex];
        var value = alpha.Kind switch
        {
            AlsLayerAlphaKind.Constant => alpha.Value,
            AlsLayerAlphaKind.Property => _input.GetValue(alpha.Name),
            AlsLayerAlphaKind.Curve => curve >= 0 && _previousCurves[curve].Present ? _previousCurves[curve].Value : 0,
            _ => throw new InvalidOperationException("Unsupported LayerBlending alpha."),
        };
        var result = (float)value;
        if (!float.IsFinite(result)) throw new ArgumentException("LayerBlending alpha does not fit its native float pin.");
        return result;
    }

    private void EvaluateNode(int index, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves, int depth)
    {
        if ((uint)depth >= _frameCapacity) throw new InvalidOperationException("LayerBlending evaluation depth exceeded.");
        var node = _definition.Node(index); var state = _candidate.States[index]!;
        if (node.Kind == AlsLayerPoseKind.Input)
        {
            _sink!.EvaluateInput(index, node.Label, pose, curves); ValidatePayload(pose, curves); return;
        }
        if (node.Kind == AlsLayerPoseKind.UseCache)
        {
            var previousDepth = _sourceEvaluationDepth; _sourceEvaluationDepth = depth + 1;
            try { _candidateCache.Evaluate(index, _candidate.Evaluation, _scope, this, pose, curves); }
            finally { _sourceEvaluationDepth = previousDepth; }
            return;
        }
        if (node.Kind is AlsLayerPoseKind.Root or AlsLayerPoseKind.SaveCache)
        { EvaluateNode(node.Inputs[0], pose, curves, depth + 1); return; }
        var aPose = WorkPose(depth, 0); var aCurves = WorkCurves(depth, 0);
        var bPose = WorkPose(depth, 1); var bCurves = WorkCurves(depth, 1);
        switch (node.Kind)
        {
            case AlsLayerPoseKind.DynamicLocalAdditive:
            case AlsLayerPoseKind.DynamicMeshAdditive:
                EvaluateNode(node.Inputs[0], aPose, aCurves, depth + 1);
                EvaluateNode(node.Inputs[1], bPose, bCurves, depth + 1);
                if (node.Kind == AlsLayerPoseKind.DynamicMeshAdditive)
                    AlsMeshSpaceAdditivePose.Difference(bPose, aPose, _parents, _rotationScratch.AsSpan(0, _boneCount * 2), pose);
                else for (var bone = 0; bone < _boneCount; bone++) pose[bone] = AlsLocalAdditivePose.Difference(bPose[bone], aPose[bone]);
                AlsLayeringCurves.Difference(bCurves, aCurves, curves);
                return;
            case AlsLayerPoseKind.ApplyLocalAdditive:
            case AlsLayerPoseKind.ApplyMeshAdditive:
                EvaluateNode(node.Inputs[0], pose, curves, depth + 1);
                if (!Relevant(state.Alphas[0])) return;
                EvaluateNode(node.Inputs[1], bPose, bCurves, depth + 1);
                if (node.Kind == AlsLayerPoseKind.ApplyMeshAdditive)
                    AlsMeshSpaceAdditivePose.Apply(pose, bPose, _parents, _rotationScratch.AsSpan(0, _boneCount * 2), pose, state.Alphas[0]);
                else for (var bone = 0; bone < _boneCount; bone++) pose[bone] = AlsLocalAdditivePose.Apply(pose[bone], bPose[bone], state.Alphas[0]);
                AlsLayeringCurves.Apply(curves, bCurves, state.Alphas[0], curves);
                return;
            case AlsLayerPoseKind.TwoWayBlend:
                var alpha = state.Alphas[0];
                if (!Relevant(alpha)) { EvaluateNode(node.Inputs[0], pose, curves, depth + 1); return; }
                if (Full(alpha)) { EvaluateNode(node.Inputs[1], pose, curves, depth + 1); return; }
                EvaluateNode(node.Inputs[0], pose, curves, depth + 1);
                EvaluateNode(node.Inputs[1], bPose, bCurves, depth + 1);
                for (var bone = 0; bone < _boneCount; bone++) pose[bone] = AlsPoseBlender.Blend(pose[bone], bPose[bone], alpha);
                for (var curve = 0; curve < _curveCount; curve++) curves[curve] = AlsStandingCycleCurves.Lerp(curves[curve], bCurves[curve], alpha);
                return;
            case AlsLayerPoseKind.Slot:
                if (!Relevant(state.Slot.SlotNodeWeight))
                { EvaluateNode(node.Inputs[0], pose, curves, depth + 1); return; }
                var sourceEvaluated = Relevant(state.Slot.SourceWeight);
                if (sourceEvaluated) EvaluateNode(node.Inputs[0], aPose, aCurves, depth + 1);
                else { _referencePose.CopyTo(aPose); aCurves.Clear(); }
                _sink!.EvaluateSlot(index, node.Label, state.Slot, sourceEvaluated, aPose, aCurves, pose, curves);
                ValidatePayload(pose, curves);
                return;
            case AlsLayerPoseKind.LayeredBlend:
                EvaluateNode(node.Inputs[0], aPose, aCurves, depth + 1);
                var any = false;
                for (var layer = 0; layer < state.Alphas.Length; layer++)
                {
                    var layerPose = WorkPose(depth, layer + 1); var layerCurves = WorkCurves(depth, layer + 1);
                    if (Relevant(state.Alphas[layer]))
                    { any = true; EvaluateNode(node.Inputs[layer + 1], layerPose, layerCurves, depth + 1); }
                    else { _referencePose.CopyTo(layerPose); layerCurves.Clear(); }
                }
                if (!any) { aPose.CopyTo(pose); aCurves.CopyTo(curves); return; }
                var layerPoses = _workPose.AsSpan((depth * _frameWidth + 1) * _boneCount, state.Alphas.Length * _boneCount);
                var layerCurveBank = _workCurves.AsSpan((depth * _frameWidth + 1) * _curveCount, state.Alphas.Length * _curveCount);
                if (node.MeshSpaceRotation)
                    AlsMeshSpacePoseBlend.BlendLayers(aPose, layerPoses, _parents, state.BoneSources, state.BoneWeights, _rotationScratch, pose);
                else AlsLayeredBonePoseBlend.BlendLocal(aPose, layerPoses, state.BoneSources, state.BoneWeights, pose);
                AlsLayeringCurves.BlendLayers(aCurves, layerCurveBank, state.MaxPoseWeights, node.CurveBlendMode, curves);
                return;
            default: throw new InvalidOperationException("Unsupported LayerBlending evaluation node.");
        }
    }
    private Span<AlsLocalPose> WorkPose(int depth, int child) => _workPose.AsSpan((depth * _frameWidth + child) * _boneCount, _boneCount);
    private Span<AlsInertialCurve> WorkCurves(int depth, int child) => _workCurves.AsSpan((depth * _frameWidth + child) * _curveCount, _curveCount);
    private static bool Relevant(float weight) => weight > AlsPoseBlender.WeightThreshold;
    private static bool Full(float weight) => weight >= 1 - AlsPoseBlender.WeightThreshold;

    private void ValidateFeedback(AlsFrameIdentity identity, in AlsLayeringInput input, ReadOnlySpan<AlsInertialCurve> curves)
    {
        var previous = input.FeedbackIdentity;
        if (previous.SlotGeneration == 0 ? previous != default || !curves.IsEmpty :
            previous.CharacterId != identity.CharacterId || previous.SlotGeneration != identity.SlotGeneration ||
            previous.FrameId >= identity.FrameId || curves.Length != _curveCount)
            throw new ArgumentException("LayerBlending requires the previous committed final curve bank of this character generation.");
        if (_committed.Identity.SlotGeneration != 0 && previous != _committed.Identity)
            throw new ArgumentException("LayerBlending feedback does not identify its last committed frame.");
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Non-finite stored final curve.");
        if (!double.IsFinite(input.EnableAimOffset) || !double.IsFinite(input.BasePoseNormal) || !double.IsFinite(input.BasePoseCrouching) ||
            !double.IsFinite(input.SpineAdditive) || !double.IsFinite(input.HeadAdditive) || !double.IsFinite(input.LeftArmAdditive) ||
            !double.IsFinite(input.RightArmAdditive) || !double.IsFinite(input.LeftHand) || !double.IsFinite(input.RightHand) ||
            !double.IsFinite(input.LeftHandIk) || !double.IsFinite(input.RightHandIk) || !double.IsFinite(input.LeftArmLocalSpace) ||
            !double.IsFinite(input.LeftArmMeshSpace) || !double.IsFinite(input.RightArmLocalSpace) || !double.IsFinite(input.RightArmMeshSpace))
            throw new ArgumentException("Non-finite LayerBlending input property.");
    }
    private static void ValidatePayload(ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        foreach (var bone in pose)
            if (!Finite(bone.Position) || !Finite(bone.Scale) || !float.IsFinite(bone.Rotation.X) || !float.IsFinite(bone.Rotation.Y) ||
                !float.IsFinite(bone.Rotation.Z) || !float.IsFinite(bone.Rotation.W) || !float.IsFinite(bone.Rotation.LengthSquared()) ||
                bone.Rotation.LengthSquared() < 1e-8f) throw new ArgumentException("Invalid LayerBlending pose atom.");
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Non-finite LayerBlending curve payload.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    void IAlsPoseCacheUpdateSink.UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context) =>
        UpdateNode(_definition.Node(cacheNodeIndex).Inputs[0], context);
    void IAlsPoseCacheUpdateSink.OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        _sink!.OnCachedUpdatesSkipped(handlerNodeIndex, skipped);
    void IAlsPoseCachePoseSink.InitializeSource(int cacheNodeIndex) => InitializeNode(_definition.Node(cacheNodeIndex).Inputs[0]);
    void IAlsPoseCachePoseSink.CacheSourceBones(int cacheNodeIndex) => CacheNodeBones(_definition.Node(cacheNodeIndex).Inputs[0]);
    void IAlsPoseCachePoseSink.EvaluateSource(int cacheNodeIndex, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves) =>
        EvaluateNode(_definition.Node(cacheNodeIndex).Inputs[0], bones, curves, _sourceEvaluationDepth);
}
