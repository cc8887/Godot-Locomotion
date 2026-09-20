using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

internal readonly record struct AlsStandingUpdateInput(AlsGroundedRuleInput Rules, AlsStandingDetailInputs Detail,
    AlsSourceRotationInput Rotation);

internal struct AlsStandingBaseFrame
{
    public AlsStandingCachedGraphUpdate Update;
    public AlsStopMachineFrame Stop;
    public AlsSourceRotationInput Rotation;
    public float InertializationSeconds;
    public AlsDirectionFeedbackState Feedback;
    public AlsDirectionFeedbackEvents DirectionEvents;
    public bool PivotInput;
    public AlsStandingTurnSlotInput TurnSlot;
    public readonly int TrackedHips => Feedback.TrackedHips;
}

// The current Controller supplies the Standing entry. Main/Slot ownership remains outside this graph.
internal sealed class AlsStandingStateGraph : IAlsStandingCachedGraphSink, IDisposable
{
    private readonly AlsStandingCachedGraphDefinition _definition;
    private readonly AlsStandingCachedGraph _graph;
    private readonly AlsStopMachineGraph _stop;
    private readonly AlsStandingTurnSlot _turnSlot;
    private readonly AlsStandingSlotProfile _slotProfile;
    private readonly AlsLocomotionSourcePlayerBinding[] _rotations;
    private readonly AlsLocomotionSourceSampleBinding[] _samples;
    private readonly AlsMovementAnimationSource[] _rotationClips;
    internal AlsStandingCachedGraphDefinition Definition => _definition;
    internal IEnumerable<string> ModifiedCurveNames => _slotProfile.SourceCurveOverrides.Keys.Append("RotationAmount");

    public AlsStandingStateGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set, int skeleton,
        AlsLocomotionDetailProfile detail, AlsLocomotionSourcePlayerBinding[] players, AlsLocomotionSourceSampleBinding[] samples,
        AlsPoseAnimationProfile? poseProfile)
    {
        var sourceText = Read("v4_locomotion_source_graph.json");
        _slotProfile = AlsStandingSlotCompiler.Compile(sourceText);
        var sources = AlsLocomotionSourceCompiler.Compile(sourceText, set, skeleton);
        var inputText = Read("v4_locomotion_inputs.json");
        var machines = AlsGroundedMachineCompiler.Compile(inputText);
        var cacheText = Read("v4_pose_cache_graph.json");
        var caches = AlsPoseCacheCompiler.Compile(cacheText, set, sources);
        _definition = AlsPoseCacheCompiler.CompileStanding(cacheText, caches, machines, detail);
        _graph = new(_definition);
        _turnSlot = new(library, set, poseProfile);
        _stop = new(library, set, machines.Stop, AlsStopPoseProfileCompiler.Compile(Read("v4_stop_graph.json"), set, skeleton));
        _rotations = new[] { AlsSourceLoopInput.RotateLeft, AlsSourceLoopInput.RotateRight }
            .Select(loop => players.Single(p => p.Domain == AlsLocomotionSourceDomain.Standing && p.LoopInput == loop)).ToArray();
        _samples = _rotations.Select(p => samples[p.SampleStart]).ToArray();
        var poseSources = library.MovementSources(set, skeleton);
        _rotationClips = _samples.Select(s => poseSources.Create(s.AnimationId)).ToArray();
    }

    public AlsStandingBaseFrame Prepare(in AlsStandingBaseFrame previous, in AlsCycleSyncFrame sources,
        in AlsFrameResult result, in AlsStandingMovementInput movement, NVector4 velocity, float gaitCurve,
        float feetPosition, float relevantTimeRemaining, float delta)
    {
        Span<AlsGroundedAutomaticTime> times = stackalloc AlsGroundedAutomaticTime[5];
        var input = Observe(previous, sources, result, movement, gaitCurve, feetPosition, relevantTimeRemaining, 1, times);
        var context = new AlsPoseUpdateContext(result.Identity, 1, delta);
        Span<AlsPoseCacheCall> calls = stackalloc AlsPoseCacheCall[1] { new(_definition.EntryReadIndex, context) };
        var update = _graph.Prepare(previous.Update.State, result.Identity, input.Rules, input.Detail, times, calls, this);
        return Consume(previous, update, input.Rotation, velocity);
    }

    public AlsStandingUpdateInput Observe(in AlsStandingBaseFrame previous, in AlsCycleSyncFrame sources,
        in AlsFrameResult result, in AlsStandingMovementInput movement, float gaitCurve, float feetPosition,
        float relevantTimeRemaining, float mainGroundedRecordedWeight, Span<AlsGroundedAutomaticTime> times)
    {
        if (times.Length != 5 || movement.Identity != result.Identity || !float.IsFinite(mainGroundedRecordedWeight) ||
            mainGroundedRecordedWeight is < 0 or > 1) throw new ArgumentException("Invalid Standing input observation.");
        var rotate = new AlsSourceRotationInput(result.RotateActive != 0 ? result.RotatePlayRate :
            previous.Rotation.Rate > 0 ? previous.Rotation.Rate : 1,
            result.RotateActive != 0 && result.RotateDirection < 0, result.RotateActive != 0 && result.RotateDirection > 0);
        var rules = new AlsGroundedRuleInput(movement.ShouldMove, rotate.Left, rotate.Right, result.ActualStance,
            true, false, 0, feetPosition);
        var detailInputs = new AlsStandingDetailInputs(result.ActualGait, gaitCurve, previous.Feedback.Pivot,
            relevantTimeRemaining, mainGroundedRecordedWeight);
        times.Clear();
        for (var rotation = 0; rotation < 2; rotation++)
        {
            var player = _rotations[rotation];
            for (var i = 0; i < sources.PlayerCount; i++)
                if (sources.Players[i].PlayerId == player.PlayerId)
                {
                    var history = sources.Players[i];
                    times[rotation + 3] = new(true, _samples[rotation].DurationSeconds, history.Time,
                        rotation == 0 ? previous.Rotation.Left : previous.Rotation.Right, true, history.DeltaPrevious, history.Delta);
                    break;
                }
        }
        return new(rules, detailInputs, rotate);
    }

    public AlsStandingBaseFrame Consume(in AlsStandingBaseFrame previous, in AlsStandingCachedGraphUpdate update,
        in AlsSourceRotationInput rotation, NVector4 velocity)
    {
        var stop = update.StopUpdated ? _stop.PrepareSources(previous.Stop, update.Stop,
            previous.TrackedHips, velocity, update.StopContext.Delta, update.StopInitializationCount > 0) :
            update.StopInitializationCount > 0 ? new(update.Stop, default, default) : previous.Stop;
        var duration = update.StandingUpdated ? update.Standing.InertializationSeconds : -1;
        if (update.DetailUpdated && update.Detail.InertializationSeconds >= 0)
            duration = duration < 0 ? update.Detail.InertializationSeconds : MathF.Min(duration, update.Detail.InertializationSeconds);
        return new() { Update = update, Stop = stop, Rotation = rotation,
            InertializationSeconds = duration, Feedback = previous.Feedback, PivotInput = previous.Feedback.Pivot };
    }

    public void Collect(in AlsStandingBaseFrame frame, ref AlsCycleSyncFrame candidate,
        Span<AlsLocomotionSourceUpdate> updates, Span<AlsLocomotionSampleUpdate> samples, ref int playerCount, ref int sampleCount)
    {
        if (!frame.Update.StandingUpdated) return;
        var machine = frame.Update.Standing;
        for (var side = 0; side < 2; side++)
        {
            var player = _rotations[side];
            if ((machine.ClearCachedWeightStates & (1 << (side + 3))) != 0) candidate.CachedWeights[player.PlayerId] = 0;
        }
        for (var i = 0; i < machine.InitializationCount; i++)
        {
            var state = machine.GetInitialization(i);
            if (state < 3) continue;
            var side = state - 3;
            var player = _rotations[side]; var sample = _samples[side];
            candidate.Times[player.PlayerId] = AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, player.StartPosition,
                sample.DurationSeconds, frame.Rotation.Rate, player.PlayRateBasis, sample.AssetRateScale);
            candidate.Epochs[player.PlayerId] = AlsAssetSourceInitialization.NextEpoch(candidate.Epochs[player.PlayerId]);
        }
        for (var i = 0; i < machine.UpdateCount; i++)
        {
            var state = machine.GetUpdate(i);
            if (state.State < 3) continue;
            var side = state.State - 3; var player = _rotations[side]; var sample = _samples[side];
            samples[sampleCount] = new(sample.SampleId, 1, sample.SampleRateScale);
            updates[playerCount++] = new(player.PlayerId, candidate.Epochs[player.PlayerId], candidate.Times[player.PlayerId],
                state.Weight, sampleCount++, 1, state.InertializationSync);
            candidate.CachedWeights[player.PlayerId] = state.Weight;
        }
    }

    public bool IsActive(in AlsStandingBaseFrame frame, in AlsPoseUpdateContext context)
    {
        if (!context.IsActive) return false;
        for (var i = 0; i < context.StateCount; i++)
        {
            var state = context.GetState(i);
            if (state.MachineNodeIndex == _definition.StandingBinding.MachineNodeIndex && state.StateIndex != frame.Update.State.Standing.CurrentState ||
                state.MachineNodeIndex == _definition.StopNodeIndex && state.StateIndex != frame.Update.State.Stop.CurrentState ||
                state.MachineNodeIndex == _definition.DetailBinding.MachineNodeIndex && state.StateIndex != (int)frame.Update.State.Detail.CurrentState)
                return false;
        }
        return true;
    }

    public bool RotationActive(in AlsStandingBaseFrame frame, int playerId) =>
        frame.Update.StandingContext.IsActive && frame.Update.State.Standing.CurrentState == (_rotations[0].PlayerId == playerId ? 3 : 4);

    public void ComposeState(int state, in AlsStandingBaseFrame frame, in AlsCycleSyncFrame sources, NVector4 velocity,
        ReadOnlySpan<AlsLocalPose> idle, ReadOnlySpan<AlsLocalPose> rest, ReadOnlySpan<AlsLocalPose> detail, Span<AlsLocalPose> output)
    {
        switch (state)
        {
            case 0: idle.CopyTo(output); _turnSlot.Compose(frame.TurnSlot, rest, output); break;
            case 1: detail.CopyTo(output); break;
            case 2: _stop.Compose(frame.Stop, velocity, detail, output); break;
            case 3: case 4:
                var side = state - 3;
                _rotationClips[side].Sample(rest, sources.Times[_rotations[side].PlayerId], output); break;
            default: throw new InvalidOperationException("Invalid Standing pose state.");
        }
    }

    public float StateCurve(int state, in AlsStandingBaseFrame frame, in AlsCycleSyncFrame sources, NVector4 velocity,
        string name, float idleValue, float detailValue) =>
        StateCurveWithPresence(state, frame, sources, velocity, name, new(idleValue), new(detailValue)).Value;

    public AlsInertialCurve StateCurveWithPresence(int state, in AlsStandingBaseFrame frame, in AlsCycleSyncFrame sources, NVector4 velocity,
        string name, AlsInertialCurve idleValue, AlsInertialCurve detailValue)
    {
        if (state == 0)
        {
            var idle = _turnSlot.CurveWithPresence(frame.TurnSlot, name,
                _slotProfile.SourceCurveOverrides.TryGetValue(AlsRefactoredV4SourceCurves.V4ProducerName(name), out var sourceOverride) ? new(sourceOverride) : idleValue);
            return name == "RotationAmount" ? AlsStandingCycleCurves.ModifyScale(idle, frame.TurnSlot.RotationScale) : idle;
        }
        if (state == 1) return detailValue;
        if (state == 2) return _stop.SampleCurveWithPresence(frame.Stop, velocity, name, detailValue);
        var side = state - 3;
        var value = _rotationClips[side].Curve(sources.Times[_rotations[side].PlayerId], name);
        return name == "RotationAmount" ? AlsStandingCycleCurves.ModifyScale(value, frame.Rotation.Rate) : value;
    }

    public void UpdateStandingSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
    public void UpdateStopSources(in AlsGroundedMachineUpdate update, in AlsPoseUpdateContext context) { }
    public void UpdateDetailSources(in AlsDetailMachineUpdate update, in AlsPoseUpdateContext context) { }
    public void UpdateCycleSource(in AlsPoseUpdateContext context) { }
    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) { }
    public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }

    public void Dispose() { _turnSlot.Dispose(); foreach (var clip in _rotationClips) clip.Dispose(); }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);

    public void ComposeState(int state, in AlsStandingBaseFrame frame, in AlsCycleSyncFrame sources, NVector4 velocity,
        ReadOnlySpan<AlsPrecisePose> idle, ReadOnlySpan<AlsPrecisePose> rest, ReadOnlySpan<AlsPrecisePose> detail, Span<AlsPrecisePose> output)
    {
        switch (state)
        {
            case 0: idle.CopyTo(output); _turnSlot.Compose(frame.TurnSlot, rest, output); break;
            case 1: detail.CopyTo(output); break;
            case 2: _stop.Compose(frame.Stop, velocity, detail, output); break;
            case 3: case 4:
                var side = state - 3;
                _rotationClips[side].Sample(rest, sources.Times[_rotations[side].PlayerId], output); break;
            default: throw new InvalidOperationException("Invalid Standing pose state.");
        }
    }
}
