using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

[InlineArray(3)] internal struct AlsAimMachines { private AlsAimMachineState _element; }
[InlineArray(7)] internal struct AlsAimEvaluators { private AlsAimEvaluatorState _element; }
[InlineArray(9)] internal struct AlsAimRecordedWeights { private float _element; }
[InlineArray(32)] internal struct AlsAimOperations { private AlsAimEvaluatorOperation _element; }
public enum AlsAimEvaluatorOperationKind { ClearWeight, Initialize, Update }
public readonly record struct AlsAimEvaluatorState(int Initialization, long LastUpdateSerial, bool Updated,
    AlsAimEvaluatorInput Input, float CachedWeight, bool Inactive);
public readonly record struct AlsAimEvaluatorOperation(AlsAimEvaluatorOperationKind Kind, int Evaluator,
    AlsAimEvaluatorState State);

public struct AlsAimFrameState
{
    public AlsFrameIdentity Identity { get; internal set; }
    public long Serial { get; internal set; }
    public AlsAnimationGraphFrame? Traversal { get; internal set; }
    internal AlsAimMachines Machines;
    internal AlsAimEvaluators Evaluators;
    internal AlsAimRecordedWeights Weights;
    public readonly AlsAimMachineState GetMachine(AlsAimMachineKind kind) => (uint)kind < 3
        ? Machines[(int)kind] : throw new ArgumentOutOfRangeException(nameof(kind));
    public readonly AlsAimEvaluatorState GetEvaluator(int index) => (uint)index < 7
        ? Evaluators[index] : throw new ArgumentOutOfRangeException(nameof(index));
    public readonly float GetRecordedWeight(AlsAimMachineKind kind, int state)
    {
        if ((uint)kind > 2 || (uint)state >= (kind == AlsAimMachineKind.Camera ? 5 : 2))
            throw new ArgumentOutOfRangeException(nameof(state));
        return Weights[(int)kind * 2 + state];
    }
}

// The final pose owner prepares this alongside BaseLayer, then commits both only
// after successful pose/curve/event evaluation. Definitions may be shared; this
// scratch and its committed history belong to one character generation.
public sealed class AlsAimFrameRuntime
{
    private readonly AlsAimPoseDefinition _definition;
    private readonly AlsAimStateMachine[] _machines;
    private readonly uint _character, _generation;
    private AlsAimFrameState _committed, _candidate;
    private AlsAimOperations _operations;
    private bool _pending, _ready;
    private int _operationCount;
    public ref readonly AlsAimFrameState Committed => ref _committed;
    public ref readonly AlsAimFrameState Candidate
    {
        get { if (!_ready) throw new InvalidOperationException("Aim candidate is not ready."); return ref _candidate; }
    }
    public int OperationCount { get { _ = Candidate.Identity; return _operationCount; } }
    public AlsAimEvaluatorOperation GetOperation(int index) => _ready && (uint)index < _operationCount
        ? _operations[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public AlsAimFrameRuntime(AlsAimPoseDefinition definition, uint character, uint generation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        _definition = definition; _character = character; _generation = generation;
        _machines = [new(definition, AlsAimMachineKind.Behavior), new(definition, AlsAimMachineKind.Input), new(definition, AlsAimMachineKind.Camera)];
    }

    public void Prepare(in AlsAimingInputState aiming, AlsRotationMode mode, bool hasInput, float delta,
        long serial, bool updateSource, float contextWeight = 1, bool inactive = false,
        AlsAnimationGraphFrame? traversal = null)
    {
        if (_pending) throw new InvalidOperationException("Commit or cancel the pending Aim frame first.");
        var identity = aiming.Identity;
        if(traversal is { } graphFrame)graphFrame.Validate(identity);
        if(_committed.Identity!=default && _committed.Traversal.HasValue!=traversal.HasValue)
            throw new ArgumentException("Aim frame traversal ownership differs.");
        if (identity.CharacterId != _character || identity.SlotGeneration != _generation ||
            _committed.Identity != default && (identity.FrameId <= _committed.Identity.FrameId || serial <= _committed.Serial) ||
            serial < 0 || !float.IsFinite(delta) || delta < 0 || (uint)mode > 2 ||
            !double.IsFinite(aiming.SmoothedAngle.Yaw) || !float.IsFinite(contextWeight) || contextWeight is < 0 or > 1)
            throw new ArgumentException("Invalid Aim frame or character history.");
        _pending = true; _ready = false; _candidate = _committed;
        _candidate.Identity = identity; _candidate.Serial = serial; _candidate.Weights = default; _candidate.Traversal=traversal;
        _operationCount = 0; _operations = default;
        // Cold graph initialization also happens while its update is hidden.
        if (!_candidate.Machines[0].Initialized || traversal is { } frame &&
            !frame.Initialization.MatchesCounter(_committed.Traversal?.Initialization ?? default))
            ApplyEntries(0, _machines[0].Initialize());
        if (updateSource)
        {
            UpdateMachine(0, contextWeight, inactive, aiming, mode, hasInput, delta, serial);
        }
        _ready = true;
    }

    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (!_ready || _candidate.Identity != identity) throw new InvalidOperationException("Wrong or incomplete Aim frame commit.");
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); _committed = _candidate; _ready = false; _pending = false;
    }
    public void Cancel() { _ready = false; _pending = false; _operationCount = 0; }

    private void UpdateMachine(int machine, float weight, bool inactive, in AlsAimingInputState aiming,
        AlsRotationMode mode, bool hasInput, float delta, long serial)
    {
        Span<float> recorded = stackalloc float[_definition.Machines[machine].States.Length];
        recorded.Clear();
        if (_committed.Identity != default && (_candidate.Traversal is { } frame
            ? _committed.Traversal!.Value.Update.WasSynchronizedCounter(frame.Update) : serial - _committed.Serial == 1))
            for (var state = 0; state < recorded.Length; state++) recorded[state] = _committed.Weights[machine * 2 + state];
        var result = _machines[machine].Update(_candidate.Machines[machine], mode, hasInput,
            aiming.SmoothedAngle.Yaw, recorded, weight, delta, serial, inactive, _candidate.Traversal?.Update);
        ApplyEntries(machine, result);
        for (var i = 0; i < result.UpdateCount; i++)
        {
            var update = result.GetUpdate(i); var state = _definition.Machines[machine].States[update.State];
            if (state.ChildMachine >= 0) UpdateMachine(state.ChildMachine, update.Weight, update.Inactive, aiming, mode, hasInput, delta, serial);
            else
            {
                var prior = _candidate.Evaluators[state.Evaluator];
                if (prior.Initialization == 0) throw new InvalidOperationException("Uninitialized Aim source update.");
                var next = prior with { Updated = true, LastUpdateSerial = serial,
                    Input = _definition.Evaluators[state.Evaluator].Evaluate(aiming), CachedWeight = update.Weight, Inactive = update.Inactive };
                _candidate.Evaluators[state.Evaluator] = next; Add(AlsAimEvaluatorOperationKind.Update, state.Evaluator, next);
            }
        }
        for (var state = 0; state < recorded.Length; state++)
            _candidate.Weights[machine * 2 + state] = AlsTransitionStack.Weight(result.State.Transitions, state);
    }

    private void ApplyEntries(int machine, in AlsAimMachineUpdate result)
    {
        _candidate.Machines[machine] = result.State;
        for (var i = 0; i < result.EntryCount; i++)
        {
            var entry = result.GetEntry(i); var state = _definition.Machines[machine].States[entry.State];
            if (state.ChildMachine >= 0)
            {
                // Parent SetState clears the complete baked player closure even
                // when its still-active child does not need reinitialization.
                for (var evaluator = 0; evaluator < _definition.Evaluators.Length; evaluator++)
                    if (_definition.Evaluators[evaluator].Machine == state.ChildMachine) ClearWeight(evaluator);
                if (entry.Initialize) ApplyEntries(state.ChildMachine, _machines[state.ChildMachine].Initialize());
            }
            else
            {
                ClearWeight(state.Evaluator);
                if (entry.Initialize)
                {
                    var previous = _candidate.Evaluators[state.Evaluator];
                    var next = new AlsAimEvaluatorState(checked(previous.Initialization + 1), 0, false, default, 0, false);
                    _candidate.Evaluators[state.Evaluator] = next; Add(AlsAimEvaluatorOperationKind.Initialize, state.Evaluator, next);
                }
            }
        }
    }

    private void ClearWeight(int evaluator)
    {
        var next = _candidate.Evaluators[evaluator] with { CachedWeight = 0 };
        _candidate.Evaluators[evaluator] = next; Add(AlsAimEvaluatorOperationKind.ClearWeight, evaluator, next);
    }
    private void Add(AlsAimEvaluatorOperationKind kind, int evaluator, in AlsAimEvaluatorState state)
    {
        if (_operationCount == 32) throw new InvalidOperationException("Aim operation capacity exceeded.");
        _operations[_operationCount++] = new(kind, evaluator, state);
    }
}
