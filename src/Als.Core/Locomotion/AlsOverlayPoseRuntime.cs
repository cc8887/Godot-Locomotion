using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsOverlayPoseContext(AlsFrameIdentity Identity, long Serial, float Delta, float Weight,
    AlsLocalPose Component, long AttachParent, float TeleportDistance, bool Inactive = false);
public readonly record struct AlsOverlaySourceUpdate(int Source, int Initialization, float Weight, bool Inactive, bool InertializationSync);
public readonly record struct AlsOverlayInertialRequest(int NodeIndex, float Duration, bool UseBlendMode, AlsTransitionBlend Blend);

// All callbacks operate on the enclosing owner's candidate. Source clocks and
// cross-graph sync are owned by that transaction; pose evaluation never ticks them.
public interface IAlsOverlayPoseSink
{
    void InitializeSource(int source, int initialization);
    void UpdateSource(in AlsOverlaySourceUpdate update);
    void EvaluateSource(int source, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
    void RequestInertialization(in AlsOverlayInertialRequest request);
    void QueueTransitionNotify(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify);
}
public interface IAlsPreciseOverlayPoseSink : IAlsOverlayPoseSink
{
    void EvaluatePreciseSource(int source, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);
}

// One exclusive owner per character. All node histories, nested state machines
// and the outer inertializer commit together, after successful pose evaluation
// or an explicitly unvisited initialization-only traversal.
public sealed class AlsOverlayPoseRuntime
{
    private struct NodeState
    {
        public int Initialization;
        public long Serial;
        public bool Updated, InterpolationInitialized, ARelevant, BRelevant;
        public float Interpolation, Alpha;
        public Vector4 Weights;
        public AlsOverlayBlendListState List;
    }
    private sealed class Bank
    {
        public readonly NodeState[] Nodes;
        public readonly AlsOverlayMachineState[] Machines = new AlsOverlayMachineState[5];
        public readonly AlsInertialization Inertial;
        public bool Initialized;
        public AlsFrameIdentity Identity;
        public long Serial;
        public AlsAnimationGraphFrame? Traversal;
        public Bank(int capacity, int bones, int curves, float units, Vector3? fallbackAxis)
        { Nodes = new NodeState[capacity]; Inertial = new(bones, curves, units, fallbackAxis); }
        public void CopyFrom(Bank other)
        {
            other.Nodes.CopyTo(Nodes, 0); other.Machines.CopyTo(Machines, 0); Inertial.CopyFrom(other.Inertial);
            Initialized = other.Initialized; Identity = other.Identity; Serial = other.Serial; Traversal=other.Traversal;
        }
    }
    private readonly AlsOverlayPoseDefinition _definition;
    private readonly AlsOverlayStateMachine[] _machines;
    private readonly int[][] _valueCurves, _writeCurves;
    private readonly string[] _curveNames;
    private readonly AlsInertialCurve[] _feedback, _curves;
    private readonly AlsPrecisePose[] _reference, _poses;
    private readonly AlsLocalPose[] _singleScratch, _preSingle;
    private readonly AlsQuaternion[] _rotationScratch, _inertialRotations;
    private readonly bool[] _stateCached;
    private readonly int _bones, _curveCount;
    private readonly int _preInertial;
    private readonly uint _character, _generation;
    private Bank _committed, _candidate;
    private IAlsOverlayPoseSink? _sink;
    private AlsOverlayPoseInput _input;
    private AlsOverlayStateInput _stateInput;
    private AlsOverlayPoseContext _context;
    private bool _prepared, _evaluated, _busy, _unvisited;
    public AlsFrameIdentity CommittedIdentity => _committed.Identity;
    public AlsAnimationGraphFrame? CommittedTraversal => _committed.Traversal;
    public int CommittedInertiaHistoryCount => _committed.Inertial.HistoryCount;
    public bool HasCandidate => _prepared;
    public int SourceUpdates { get; private set; }
    public int SourceEvaluations { get; private set; }
    public int NotifyCount { get; private set; }
    public int InertialRequestCount { get; private set; }
    public AlsOverlayPoseDefinition Definition => _definition;
    public ReadOnlySpan<AlsLocalPose> PreInertialPose => _evaluated ? _preSingle : throw new InvalidOperationException("Overlay has not evaluated.");
    public ReadOnlySpan<AlsPrecisePose> PreInertialPrecisePose => _evaluated ? Pose(_preInertial) : throw new InvalidOperationException("Overlay has not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> PreInertialCurves => _evaluated ? Curves(_preInertial) : throw new InvalidOperationException("Overlay has not evaluated.");
    public AlsOverlayMachineState Machine(AlsOverlayMachineKind kind) => (_prepared ? _candidate : _committed).Machines[(int)kind];
    public float NodeAlpha(int index) => (_prepared ? _candidate : _committed).Nodes[_definition.Node(index).Index].Alpha;
    public int SourceInitialization(int index) => (_prepared ? _candidate : _committed).Nodes[_definition.Node(index).Index].Initialization;

    public AlsOverlayPoseRuntime(AlsOverlayPoseDefinition definition, ReadOnlySpan<string> curveNames,
        ReadOnlySpan<AlsLocalPose> referencePose, uint character, uint generation, float unitsPerCentimeter = 1,
        Vector3? rotationFallbackAxis = null, ReadOnlySpan<AlsPrecisePose> preciseReference = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (referencePose.Length != definition.States.QuickFeet.BoneNames.Length || generation == 0)
            throw new ArgumentException("Overlay requires its complete logical skeleton and character generation.");
        _definition = definition; _bones = referencePose.Length; _curveCount = curveNames.Length;
        _preInertial = definition.Nodes.ToArray().Single(n => n.Kind == AlsOverlayPoseKind.Inertialization).Inputs[0];
        _character = character; _generation = generation;
        if (!preciseReference.IsEmpty && preciseReference.Length != _bones) throw new ArgumentException("Precise Overlay reference layout differs.");
        _reference = new AlsPrecisePose[_bones];
        for (var bone = 0; bone < _bones; bone++)
        {
            _reference[bone] = preciseReference.IsEmpty ? new(referencePose[bone]) : preciseReference[bone];
            _reference[bone].Validate(.001);
            var projection = _reference[bone].ToSingle();
            if (Vector3.Distance(projection.Position, referencePose[bone].Position) > 1e-5f ||
                Vector3.Distance(projection.Scale, referencePose[bone].Scale) > 1e-5f ||
                (projection.Rotation - referencePose[bone].Rotation).Length() > 1e-6f)
                throw new ArgumentException("Precise Overlay reference differs from its logical skeleton.");
        }
        _curveNames = curveNames.ToArray();
        if (_curveNames.Any(string.IsNullOrEmpty) || _curveNames.Distinct(StringComparer.Ordinal).Count() != _curveCount)
            throw new ArgumentException("Duplicate Overlay curve names.");
        ValidatePayload(referencePose, []);
        _valueCurves = new int[definition.Capacity][]; _writeCurves = new int[definition.Capacity][];
        foreach (var node in definition.Nodes)
        {
            _valueCurves[node.Index] = node.Values.ToArray().Select(v => v.Kind == AlsOverlayValueKind.Curve ? Array.IndexOf(_curveNames, v.Curve) : -1).ToArray();
            _writeCurves[node.Index] = node.CurveNames.ToArray().Select(c => Array.IndexOf(_curveNames, c)).ToArray();
            if (_writeCurves[node.Index].Any(i => i < 0)) throw new ArgumentException("Overlay output layout omits a ModifyCurve name.");
        }
        _feedback = new AlsInertialCurve[_curveCount]; _stateCached = new bool[definition.Capacity];
        _poses = new AlsPrecisePose[checked(definition.Capacity * _bones)]; _curves = new AlsInertialCurve[checked(definition.Capacity * _curveCount)];
        _singleScratch = new AlsLocalPose[_bones]; _preSingle = new AlsLocalPose[_bones];
        _rotationScratch = new AlsQuaternion[_bones * 2]; _inertialRotations = new AlsQuaternion[_bones];
        _machines = Enumerable.Range(0, 5).Select(i => new AlsOverlayStateMachine(definition.States, (AlsOverlayMachineKind)i)).ToArray();
        _committed = new(definition.Capacity, _bones, _curveCount, unitsPerCentimeter, rotationFallbackAxis);
        _candidate = new(definition.Capacity, _bones, _curveCount, unitsPerCentimeter, rotationFallbackAxis);
    }

    public void Prepare(in AlsOverlayPoseContext context, in AlsOverlayStateInput stateInput, in AlsOverlayPoseInput input,
        ReadOnlySpan<AlsInertialCurve> previousCommittedCurves, IAlsOverlayPoseSink sink,
        AlsAnimationGraphFrame? traversal = null, bool updateSource = true)
    {
        ArgumentNullException.ThrowIfNull(sink); stateInput.Validate(); input.Validate();
        if(traversal is { } graphFrame)graphFrame.Validate(context.Identity);
        if(!updateSource && !traversal.HasValue || _committed.Initialized && _committed.Traversal.HasValue!=traversal.HasValue)
            throw new ArgumentException("Overlay graph traversal ownership differs.");
        if (_prepared || _busy || context.Identity.CharacterId != _character || context.Identity.SlotGeneration != _generation || context.Serial < 0 ||
            !float.IsFinite(context.Delta) || context.Delta < 0 || !float.IsFinite(context.Weight) || context.Weight < 0 ||
            previousCommittedCurves.Length != _curveCount || !float.IsFinite(context.TeleportDistance) || context.TeleportDistance < 0 ||
            input.Aiming != (stateInput.RotationMode == AlsRotationMode.Aiming) ||
            _committed.Initialized && (context.Serial <= _committed.Serial || context.Identity.FrameId <= _committed.Identity.FrameId))
            throw new ArgumentException($"Invalid Overlay candidate or frame history: frame={context.Identity.FrameId} character={context.Identity.CharacterId}/{_character} generation={context.Identity.SlotGeneration}/{_generation} prepared={_prepared} busy={_busy} delta={context.Delta:R} weight={context.Weight:R} curves={previousCommittedCurves.Length}/{_curveCount} aiming={input.Aiming}/{stateInput.RotationMode} previous={_committed.Identity.FrameId} serial={context.Serial}/{_committed.Serial}.");
        ValidatePayload([], previousCommittedCurves); ValidatePose(context.Component);
        _candidate.CopyFrom(_committed); _candidate.Identity = context.Identity; _candidate.Serial = context.Serial; _candidate.Traversal=traversal;
        _context = context; _input = input; _sink = sink; previousCommittedCurves.CopyTo(_feedback);
        // These conditions must see the same committed final bank as TwoWay.
        _stateInput = stateInput with { EnableTransition = Feedback("Enable_Transition"), RotationAmount = Feedback("RotationAmount") };
        _prepared = _busy = true; _evaluated = false; _unvisited=!updateSource;
        SourceUpdates = SourceEvaluations = NotifyCount = InertialRequestCount = 0;
        try
        {
            if (!_candidate.Initialized || traversal is { } frame &&
                !frame.Initialization.MatchesCounter(_committed.Traversal?.Initialization ?? default))
            { InitializeNode(_definition.RootIndex); _candidate.Initialized = true; }
            // All Overlay bone/profile indices are bound to the immutable logical
            // skeleton at construction. CacheBones changes no clocks or node inputs.
            if(updateSource)UpdateNode(_definition.RootIndex, context.Weight, context.Inactive, false);
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void Evaluate(Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (!_prepared || _unvisited || _evaluated || _busy || pose.Length != _bones || curves.Length != _curveCount)
            throw new InvalidOperationException("Overlay requires one evaluation of a prepared candidate.");
        _busy = true; Array.Clear(_stateCached);
        try
        {
            EvaluateNode(_definition.RootIndex);
            for (var bone = 0; bone < _bones; bone++)
            { _singleScratch[bone] = Pose(_definition.RootIndex)[bone].ToSingle(); _preSingle[bone] = Pose(_preInertial)[bone].ToSingle(); }
            ValidatePayload(_singleScratch, Curves(_definition.RootIndex));
            _singleScratch.CopyTo(pose); Curves(_definition.RootIndex).CopyTo(curves); _evaluated = true;
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }
    public void Commit()
    {
        ValidateCommit();
        (_committed, _candidate) = (_candidate, _committed); Abort();
    }
    public void ValidateCommit()
    {
        if (!_prepared || !(_evaluated || _unvisited) || _busy) throw new InvalidOperationException("Overlay needs an evaluated or unvisited lifecycle candidate before commit.");
    }
    public void Cancel() { if (_busy) throw new InvalidOperationException("Cannot cancel Overlay in a callback."); Abort(); }
    private void Abort() { _prepared = _evaluated = false; _sink = null; }

    private void InitializeNode(int index)
    {
        var node = _definition.Node(index); var initialization = checked(_candidate.Nodes[index].Initialization + 1);
        _candidate.Nodes[index] = new() { Initialization = initialization };
        if (node.Kind == AlsOverlayPoseKind.Source) { _sink!.InitializeSource(node.Source, initialization); return; }
        if (node.Kind == AlsOverlayPoseKind.Machine)
        {
            var update = _machines[node.Machine].Initialize(); _candidate.Machines[node.Machine] = update.State;
            InitializeNode(_definition.States.Machines[node.Machine].States[update.State.CurrentState].RootIndex); return;
        }
        if (node.Kind == AlsOverlayPoseKind.Inertialization) _candidate.Inertial.Reset();
        foreach (var input in node.Inputs) InitializeNode(input);
    }

    private void UpdateNode(int index, float weight, bool inactive, bool inertialSync)
    {
        var node = _definition.Node(index); ref var state = ref _candidate.Nodes[index];
        if (state.Initialization == 0) throw new InvalidOperationException("Overlay node was not initialized.");
        state.Updated = true; state.Serial = _context.Serial;
        switch (node.Kind)
        {
            case AlsOverlayPoseKind.Source:
                _sink!.UpdateSource(new(node.Source, state.Initialization, weight, inactive, inertialSync)); SourceUpdates++; return;
            case AlsOverlayPoseKind.Machine:
                var update = _machines[node.Machine].Update(_candidate.Machines[node.Machine], _stateInput, weight, _context.Delta, _context.Serial, inactive,
                    _candidate.Traversal?.Update);
                _candidate.Machines[node.Machine] = update.State;
                var machine = _definition.States.Machines[node.Machine];
                for (var i = 0; i < update.EntryCount; i++) { var entry = update.GetEntry(i); if (entry.Initialize) InitializeNode(machine.States[entry.State].RootIndex); }
                for (var i = 0; i < update.NotifyCount; i++) { _sink!.QueueTransitionNotify((AlsOverlayMachineKind)node.Machine, update.GetNotify(i)); NotifyCount++; }
                for (var i = 0; i < update.TransitionCount; i++)
                { var step = update.GetTransition(i); if (step.Inertialization) Request(new(index, step.Duration, false, AlsTransitionBlend.Linear)); }
                for (var i = 0; i < update.UpdateCount; i++)
                { var child = update.GetUpdate(i); UpdateNode(machine.States[child.State].RootIndex, child.Weight, inactive || child.Inactive, inertialSync || update.InertializationSync); }
                return;
            case AlsOverlayPoseKind.Inertialization:
                _candidate.Inertial.Update(_context.Delta); UpdateNode(node.Inputs[0], weight, inactive, inertialSync); return;
            case AlsOverlayPoseKind.Root:
            case AlsOverlayPoseKind.StateRoot:
            case AlsOverlayPoseKind.ModifyCurve:
                UpdateNode(node.Inputs[0], weight, inactive, inertialSync); return;
            case AlsOverlayPoseKind.TwoWay:
                state.Alpha = AlsOverlayPoseWeights.Alpha(Value(node, 0), node.Alpha, _context.Delta, ref state.InterpolationInitialized, ref state.Interpolation);
                var a = state.Alpha < 1 - AlsPoseBlender.WeightThreshold; var b = state.Alpha > AlsPoseBlender.WeightThreshold;
                if (node.ResetChild)
                { if (a && !state.ARelevant) InitializeNode(node.Inputs[0]); if (b && !state.BRelevant) InitializeNode(node.Inputs[1]); }
                state.ARelevant = a; state.BRelevant = b;
                if (a) UpdateNode(node.Inputs[0], weight * (b ? 1 - state.Alpha : 1), inactive, inertialSync);
                if (b) UpdateNode(node.Inputs[1], weight * (a ? state.Alpha : 1), inactive, inertialSync);
                return;
            case AlsOverlayPoseKind.LocalAdditive:
            case AlsOverlayPoseKind.MeshAdditive:
                state.Alpha = AlsOverlayPoseWeights.Alpha(Value(node, 0), node.Alpha, _context.Delta, ref state.InterpolationInitialized, ref state.Interpolation);
                UpdateNode(node.Inputs[0], weight, inactive, inertialSync);
                if (state.Alpha > AlsPoseBlender.WeightThreshold) UpdateNode(node.Inputs[1], weight * state.Alpha, inactive, inertialSync);
                return;
            case AlsOverlayPoseKind.MultiWay:
                var desired = Vector4.Zero; for (var i = 0; i < node.Values.Length; i++) desired[i] = Value(node, i);
                state.Weights = AlsOverlayPoseWeights.MultiWay(desired, node.Inputs.Length);
                for (var i = 0; i < node.Inputs.Length; i++) if (state.Weights[i] > AlsPoseBlender.WeightThreshold) UpdateNode(node.Inputs[i], weight * state.Weights[i], inactive, inertialSync);
                return;
            case AlsOverlayPoseKind.BlendList:
                var selected = node.Values[0].Kind == AlsOverlayValueKind.OverrideState ? _input.OverrideState : _input.Aiming ? 1 : 0;
                var list = AlsOverlayPoseWeights.BlendList(state.List, selected, node.BlendTimes, node.Blend, node.InertialTransition, node.ResetChild, _context.Delta);
                // UE updates the old child at zero before initializing the new one.
                if (list.InertialSeconds >= 0) Request(new(index, list.InertialSeconds, true, node.Blend));
                if (list.ZeroWeightPreviousChild >= 0) UpdateNode(node.Inputs[list.ZeroWeightPreviousChild], 0, inactive, inertialSync);
                if (list.InitializeChild >= 0) InitializeNode(node.Inputs[list.InitializeChild]);
                state.List = list.State; state.Weights = list.State.Weights;
                for (var i = 0; i < node.Inputs.Length; i++) if (state.Weights[i] > AlsPoseBlender.WeightThreshold)
                    UpdateNode(node.Inputs[i], weight * state.Weights[i], inactive || i != state.List.ActiveChild, inertialSync || list.InertialSeconds >= 0);
                return;
            default: throw new InvalidOperationException("Unknown Overlay update operation.");
        }
    }

    private void EvaluateNode(int index)
    {
        var node = _definition.Node(index); ref var state = ref _candidate.Nodes[index];
        if (!state.Updated || state.Serial != _context.Serial) throw new InvalidOperationException("Overlay pose has no matching update.");
        var output = Pose(index); var curves = Curves(index);
        switch (node.Kind)
        {
            case AlsOverlayPoseKind.Source:
                if (_sink is IAlsPreciseOverlayPoseSink precise) precise.EvaluatePreciseSource(node.Source, output, curves);
                else
                {
                    _sink!.EvaluateSource(node.Source, _singleScratch, curves); ValidatePayload(_singleScratch, curves);
                    for (var bone = 0; bone < _bones; bone++) output[bone] = new(_singleScratch[bone]);
                }
                foreach (var atom in output) atom.Validate(.001);
                foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Invalid precise source curve.");
                SourceEvaluations++; return;
            case AlsOverlayPoseKind.Machine: EvaluateMachine(node); return;
            case AlsOverlayPoseKind.StateRoot:
                if (_stateCached[index]) return;
                CopyChild(node.Inputs[0], index); _stateCached[index] = true; return;
            case AlsOverlayPoseKind.Root: CopyChild(node.Inputs[0], index); return;
            case AlsOverlayPoseKind.Inertialization:
                CopyChild(node.Inputs[0], index);
                for (var bone = 0; bone < _bones; bone++)
                { _singleScratch[bone] = output[bone].ToSingle(); _inertialRotations[bone] = output[bone].Rotation; }
                _candidate.Inertial.EvaluatePrecise(_singleScratch, curves, _context.Component, _context.AttachParent,
                    _context.TeleportDistance, _singleScratch, curves, _inertialRotations, new(_context.Component.Rotation), _inertialRotations);
                for (var bone = 0; bone < _bones; bone++)
                    output[bone] = new(new(_singleScratch[bone].Position), _inertialRotations[bone], new(_singleScratch[bone].Scale));
                return;
            case AlsOverlayPoseKind.ModifyCurve:
                CopyChild(node.Inputs[0], index);
                for (var i = 0; i < node.CurveNames.Length; i++)
                { var c = _writeCurves[index][i]; curves[c] = AlsStandingCycleCurves.ModifyBlend(curves[c], Value(node, i), Value(node, node.Values.Length - 1)); }
                return;
            case AlsOverlayPoseKind.TwoWay:
                if (!state.BRelevant) { CopyChild(node.Inputs[0], index); return; }
                if (!state.ARelevant) { CopyChild(node.Inputs[1], index); return; }
                EvaluateNode(node.Inputs[0]); EvaluateNode(node.Inputs[1]); BlendTwo(node.Inputs[0], node.Inputs[1], index, state.Alpha); return;
            case AlsOverlayPoseKind.LocalAdditive:
            case AlsOverlayPoseKind.MeshAdditive:
                CopyChild(node.Inputs[0], index);
                if (state.Alpha <= AlsPoseBlender.WeightThreshold) return;
                EvaluateNode(node.Inputs[1]); var additive = Pose(node.Inputs[1]); var additiveCurves = Curves(node.Inputs[1]);
                if (node.Kind == AlsOverlayPoseKind.MeshAdditive)
                    AlsPrecisePoseBlender.MeshApply(output, additive, _definition.States.QuickFeet.Parents, _rotationScratch, output, state.Alpha);
                else for (var bone = 0; bone < _bones; bone++) output[bone] = AlsPrecisePoseBlender.LocalApply(output[bone], additive[bone], state.Alpha);
                for (var c = 0; c < _curveCount; c++) curves[c] = AlsStandingCycleCurves.Accumulate(curves[c], additiveCurves[c], state.Alpha);
                return;
            case AlsOverlayPoseKind.MultiWay:
            case AlsOverlayPoseKind.BlendList:
                EvaluateWeighted(node, state.Weights); return;
            default: throw new InvalidOperationException("Unknown Overlay evaluation operation.");
        }
    }
    private void EvaluateWeighted(AlsOverlayPoseNode node, Vector4 weights)
    {
        var first = -1; var second = -1; var count = 0;
        for (var i = 0; i < node.Inputs.Length; i++) if (weights[i] > AlsPoseBlender.WeightThreshold)
        { if (count == 0) first = i; if (count == 1) second = i; count++; }
        if (count == 0) { _reference.CopyTo(Pose(node.Index)); Curves(node.Index).Clear(); return; }
        if (node.Kind == AlsOverlayPoseKind.BlendList)
        {
            if (count == 1 && weights[first] >= 1 - AlsPoseBlender.WeightThreshold) { CopyChild(node.Inputs[first], node.Index); return; }
            if (count == 2 && MathF.Abs(weights[first] + weights[second] - 1) <= 1e-8f)
            {
                EvaluateNode(node.Inputs[first]); EvaluateNode(node.Inputs[second]);
                BlendTwo(node.Inputs[first], node.Inputs[second], node.Index, 1 - weights[first]); return;
            }
        }
        var output = Pose(node.Index); var curves = Curves(node.Index); var initialized = false;
        for (var i = 0; i < node.Inputs.Length; i++)
        {
            var weight = weights[i]; if (weight <= AlsPoseBlender.WeightThreshold) continue;
            var child = node.Inputs[i]; EvaluateNode(child); var source = Pose(child); var sourceCurves = Curves(child);
            for (var bone = 0; bone < _bones; bone++) output[bone] = initialized ? AlsPrecisePoseBlender.Accumulate(output[bone], source[bone], weight) : AlsPrecisePoseBlender.Scale(source[bone], weight);
            for (var c = 0; c < _curveCount; c++) curves[c] = initialized ? AlsStandingCycleCurves.Accumulate(curves[c], sourceCurves[c], weight) : AlsStandingCycleCurves.Scale(sourceCurves[c], weight);
            initialized = true;
        }
        for (var bone = 0; bone < _bones; bone++) output[bone] = output[bone].Normalized();
    }
    private void EvaluateMachine(AlsOverlayPoseNode node)
    {
        var state = _candidate.Machines[node.Machine]; var machine = _definition.States.Machines[node.Machine];
        if (state.Transitions.Count == 0) { CopyChild(machine.States[state.CurrentState].RootIndex, node.Index); return; }
        var output = Pose(node.Index); var curves = Curves(node.Index);
        for (var i = 0; i < state.Transitions.Count; i++)
        {
            var transition = state.Transitions.GetTransition(i); var edge = machine.Edges[state.GetActivePath(i).Edge];
            if (i == 0) CopyChild(machine.States[transition.From].RootIndex, node.Index);
            var target = machine.States[transition.To].RootIndex; EvaluateNode(target);
            var to = Pose(target); var toCurves = Curves(target);
            for (var bone = 0; bone < _bones; bone++)
            {
                var weights = edge.QuickFeet ? _definition.States.QuickFeet.Weights(bone, transition.Alpha) : new Vector2(transition.Alpha, 1 - transition.Alpha);
                output[bone] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(output[bone], weights.Y), to[bone], weights.X);
            }
            for (var c = 0; c < _curveCount; c++) curves[c] = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(curves[c], 1 - transition.Alpha), toCurves[c], transition.Alpha);
        }
        for (var bone = 0; bone < _bones; bone++) output[bone] = output[bone].Normalized();
    }
    private void BlendTwo(int first, int second, int output, float alpha)
    {
        var a = Pose(first); var b = Pose(second); var result = Pose(output);
        for (var bone = 0; bone < _bones; bone++) result[bone] = AlsPrecisePoseBlender.Blend(a[bone], b[bone], alpha);
        var ac = Curves(first); var bc = Curves(second); var curves = Curves(output);
        for (var c = 0; c < _curveCount; c++) curves[c] = AlsStandingCycleCurves.Lerp(ac[c], bc[c], alpha);
    }
    private void CopyChild(int child, int parent) { EvaluateNode(child); Pose(child).CopyTo(Pose(parent)); Curves(child).CopyTo(Curves(parent)); }
    private float Value(AlsOverlayPoseNode node, int slot)
    {
        var index = _valueCurves[node.Index][slot];
        return node.Values[slot].Kind == AlsOverlayValueKind.Curve ? index >= 0 && _feedback[index].Present ? _feedback[index].Value : 0 : _input.Read(node.Values[slot]);
    }
    private float Feedback(string name) { var index = Array.IndexOf(_curveNames, name); return index >= 0 && _feedback[index].Present ? _feedback[index].Value : 0; }
    private void Request(in AlsOverlayInertialRequest request)
    { _candidate.Inertial.Request(request.Duration); _sink!.RequestInertialization(request); InertialRequestCount++; }
    private Span<AlsPrecisePose> Pose(int index) => _poses.AsSpan(index * _bones, _bones);
    private Span<AlsInertialCurve> Curves(int index) => _curves.AsSpan(index * _curveCount, _curveCount);
    private static void ValidatePayload(ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        foreach (var bone in pose) ValidatePose(bone);
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite Overlay output curve.");
    }
    private static void ValidatePose(in AlsLocalPose pose)
    {
        if (!float.IsFinite(pose.Position.LengthSquared()) || !float.IsFinite(pose.Scale.LengthSquared()) ||
            !float.IsFinite(pose.Rotation.LengthSquared()) || MathF.Abs(pose.Rotation.LengthSquared() - 1) > .001f)
            throw new ArgumentException("Invalid Overlay pose atom.");
    }
}
