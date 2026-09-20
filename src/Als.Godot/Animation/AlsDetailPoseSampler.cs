using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

/// <summary>Four local additive samples at caller-owned times. No clocks, sync, or event dispatch.</summary>
internal sealed class AlsDetailPoseSampler : IDisposable
{
    private readonly AlsDetailPlayerDefinition[][] _players = new AlsDetailPlayerDefinition[7][];
    private readonly Dictionary<int, AlsMovementAnimationSource> _clips = new();
    private readonly AlsLocalPose[] _rest;
    private readonly AlsLocalPose[] _basePose;
    private readonly AlsLocalPose[] _samples;
    private readonly AlsLocalPose[] _stateSamples;
    private readonly AlsInertialCurve[] _stateCurves = new AlsInertialCurve[6];
    private bool _disposed;
    private readonly AlsPrecisePose[] _restPrecise, _basePosePrecise, _samplesPrecise, _stateSamplesPrecise;

    public AlsDetailPoseSampler(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set, AlsLocomotionDetailProfile profile)
    {
        var poseSources = library.MovementSources(set, profile.SkeletonId); var count = poseSources.BoneCount;
        _restPrecise = poseSources.PreciseReferencePose.ToArray(); _basePosePrecise = new AlsPrecisePose[count];
        _samplesPrecise = new AlsPrecisePose[count * 4]; _stateSamplesPrecise = new AlsPrecisePose[count * 6];
        _rest = poseSources.ReferencePose.ToArray(); _basePose = new AlsLocalPose[count];
        _samples = new AlsLocalPose[count * 4]; _stateSamples = new AlsLocalPose[count * 6];
        try
        {
            var players = profile.States.SelectMany(s => s.Players).ToArray();
            var baseId = players.Select(p => p.AdditiveBaseAnimationId).Distinct().Single();
            if (players.Length != 16 || players.Any(p => p.AdditiveBaseFrame != 0))
                throw new InvalidOperationException("Unsupported Detail player/base contract.");
            foreach (var state in profile.States) _players[(int)state.Id] = state.Players.ToArray();
            foreach (var id in players.Select(p => p.AnimationId).Append(baseId).Distinct())
            {
                if (!library.ClipNames.TryGetValue(id, out var name))
                    throw new InvalidOperationException("Detail source is absent from the animation closure.");
                var clip = poseSources.Create(id, rawBonePose: id == baseId);
                _clips.Add(id, clip);
                if (Math.Abs(clip.Length - set.Animations[id].PlayLength) > .00001)
                    throw new InvalidOperationException("Detail source time domain differs.");

            }
            _clips[baseId].Sample(_rest, 0, _basePose);
            _clips[baseId].SamplePrecise(0, _basePosePrecise);
        }
        catch { Dispose(); throw; }
    }

    internal ReadOnlySpan<AlsLocalPose> AdditiveBasePose => _basePose;
    internal ReadOnlySpan<AlsLocalPose> ReferencePose => _rest;
    internal ReadOnlySpan<AlsPrecisePose> PreciseReferencePose => _restPrecise;

    // Time layout is four F/B/L/R occurrences for each of WalkRun, FirstPivot, SecondPivot, RunStart.
    public void ComposeMachine(in AlsDetailMachineState machine, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        ReadOnlySpan<AlsLocalPose> cycle, Span<AlsLocalPose> output)
    {
        var required = ValidateMachine(machine, sourceTimes);
        if (cycle.Length != _rest.Length || output.Length != _rest.Length ||
            cycle.Overlaps(output, out var overlap) && overlap != 0)
            throw new ArgumentException("Invalid Detail machine output buffers.");
        for (var state = 0; state < 6; state++)
            if ((required & (1 << state)) != 0)
                Compose((AlsDetailState)state, StateTimes((AlsDetailState)state, sourceTimes), velocity, cycle,
                    _stateSamples.AsSpan(state * _rest.Length, _rest.Length));
        var transitions = machine.Transitions;
        var initial = transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState;
        for (var bone = 0; bone < output.Length; bone++)
        {
            var result = _stateSamples[initial * output.Length + bone];
            for (var i = 0; i < transitions.Count; i++)
            {
                var edge = transitions.GetTransition(i);
                result = AlsPoseBlender.BlendRaw(result, _stateSamples[edge.To * output.Length + bone], edge.Alpha);
            }
            output[bone] = transitions.Count > 0 ? AlsPoseBlender.Normalize(result) : result;
        }
    }

    public float SampleMachineCurve(in AlsDetailMachineState machine, ReadOnlySpan<float> sourceTimes,
        NVector4 velocity, string name, float cycleValue) => SampleMachineCurveWithPresence(machine, sourceTimes, velocity, name, new(cycleValue)).Value;

    public AlsInertialCurve SampleMachineCurveWithPresence(in AlsDetailMachineState machine, ReadOnlySpan<float> sourceTimes,
        NVector4 velocity, string name, AlsInertialCurve cycleValue)
    {
        var required = ValidateMachine(machine, sourceTimes);
        for (var state = 0; state < 6; state++)
            if ((required & (1 << state)) != 0)
                _stateCurves[state] = SampleCurveWithPresence((AlsDetailState)state, StateTimes((AlsDetailState)state, sourceTimes), velocity, name, cycleValue);
        var transitions = machine.Transitions;
        var initial = transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState;
        var result = _stateCurves[initial];
        for (var i = 0; i < transitions.Count; i++)
        {
            var edge = transitions.GetTransition(i);
            result = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(result, 1 - edge.Alpha), _stateCurves[edge.To], edge.Alpha);
        }
        return result;
    }

    private int ValidateMachine(in AlsDetailMachineState machine, ReadOnlySpan<float> times)
    {
        if (!machine.HasUpdated || times.Length != 16 || (uint)machine.CurrentState >= (uint)AlsDetailState.RunConduit)
            throw new ArgumentException("Invalid Detail machine state or source time layout.");
        for (var state = AlsDetailState.WalkRun; state < AlsDetailState.RunConduit; state++) Validate(state, StateTimes(state, times));
        var transitions = machine.Transitions;
        var required = 1 << (int)machine.CurrentState;
        for (var i = 0; i < transitions.Count; i++)
        {
            var edge = transitions.GetTransition(i);
            if ((uint)edge.From >= 6 || (uint)edge.To >= 6 || !float.IsFinite(edge.Alpha) || edge.Alpha is < 0 or > 1)
                throw new ArgumentException("Invalid Detail machine transition.");
            required |= (1 << edge.From) | (1 << edge.To);
        }
        return required;
    }

    private static ReadOnlySpan<float> StateTimes(AlsDetailState state, ReadOnlySpan<float> times) =>
        state < AlsDetailState.WalkRun ? [] : times.Slice(((int)state - (int)AlsDetailState.WalkRun) * 4, 4);

    public void Compose(AlsDetailState state, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        ReadOnlySpan<AlsLocalPose> cycle, Span<AlsLocalPose> output)
    {
        var players = Validate(state, sourceTimes);
        if (cycle.Length != _rest.Length || output.Length != _rest.Length ||
            (cycle.Overlaps(output, out var offset) && offset != 0))
            throw new ArgumentException("Invalid Detail output buffers.");
        var weights = AlsDetailPoseComposer.SampleWeights(velocity);
        if (players.Length == 0) { cycle.CopyTo(output); return; }
        for (var source = 0; source < 4; source++)
        {
            if (weights[source] == 0) continue;
            var sample = _samples.AsSpan(source * _rest.Length, _rest.Length);
            _clips[players[source].AnimationId].Sample(_rest, sourceTimes[source], sample);
        }
        AlsDetailPoseComposer.Compose(cycle, _samples, _rest, velocity, output);
    }

    public float SampleCurve(AlsDetailState state, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        string name, float cycleValue) => SampleCurveWithPresence(state, sourceTimes, velocity, name, new(cycleValue)).Value;

    public AlsInertialCurve SampleCurveWithPresence(AlsDetailState state, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        string name, AlsInertialCurve cycleValue)
    {
        var players = Validate(state, sourceTimes);
        if (cycleValue.Present && !float.IsFinite(cycleValue.Value)) throw new ArgumentException("Invalid Detail base curve.");
        var weights = AlsDetailPoseComposer.SampleWeights(velocity);
        if (players.Length == 0) return cycleValue;
        var mixed = default(AlsInertialCurve);
        for (var source = 0; source < 4; source++)
        {
            if (weights[source] == 0) continue;
            mixed = AlsStandingCycleCurves.Accumulate(mixed, _clips[players[source].AnimationId].Curve(sourceTimes[source], name), weights[source]);
        }
        return AlsStandingCycleCurves.Accumulate(cycleValue, mixed, 1);
    }

    private AlsDetailPlayerDefinition[] Validate(AlsDetailState state, ReadOnlySpan<float> times)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)state >= (uint)AlsDetailState.RunConduit) throw new ArgumentOutOfRangeException(nameof(state));
        var players = _players[(int)state];
        if (times.Length != players.Length) throw new ArgumentException("Detail requires one time per source occurrence.");
        for (var source = 0; source < times.Length; source++)
            if (!float.IsFinite(times[source]) || times[source] < 0 || times[source] > _clips[players[source].AnimationId].Length)
                throw new ArgumentException("Detail source time is outside the non-looping clip.");
        return players;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var clip in _clips.Values) clip.Dispose();
    }

    public void ComposeMachine(in AlsDetailMachineState machine, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        ReadOnlySpan<AlsPrecisePose> cycle, Span<AlsPrecisePose> output)
    {
        var required = ValidateMachine(machine, sourceTimes);
        if (cycle.Length != _restPrecise.Length || output.Length != _restPrecise.Length ||
            cycle.Overlaps(output, out var overlap) && overlap != 0)
            throw new ArgumentException("Invalid Detail machine output buffers.");
        for (var state = 0; state < 6; state++)
            if ((required & (1 << state)) != 0)
                Compose((AlsDetailState)state, StateTimes((AlsDetailState)state, sourceTimes), velocity, cycle,
                    _stateSamplesPrecise.AsSpan(state * _restPrecise.Length, _restPrecise.Length));
        var transitions = machine.Transitions;
        var initial = transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState;
        for (var bone = 0; bone < output.Length; bone++)
        {
            var result = _stateSamplesPrecise[initial * output.Length + bone];
            for (var i = 0; i < transitions.Count; i++)
            {
                var edge = transitions.GetTransition(i);
                result = AlsPrecisePoseBlender.BlendRaw(result, _stateSamplesPrecise[edge.To * output.Length + bone], edge.Alpha);
            }
            output[bone] = transitions.Count > 0 ? AlsPrecisePoseBlender.Normalize(result) : result;
        }
    }
    public void Compose(AlsDetailState state, ReadOnlySpan<float> sourceTimes, NVector4 velocity,
        ReadOnlySpan<AlsPrecisePose> cycle, Span<AlsPrecisePose> output)
    {
        var players = Validate(state, sourceTimes);
        if (cycle.Length != _restPrecise.Length || output.Length != _restPrecise.Length ||
            (cycle.Overlaps(output, out var offset) && offset != 0))
            throw new ArgumentException("Invalid Detail output buffers.");
        var weights = AlsDetailPoseComposer.SampleWeights(velocity);
        if (players.Length == 0) { cycle.CopyTo(output); return; }
        for (var source = 0; source < 4; source++)
        {
            if (weights[source] == 0) continue;
            var sample = _samplesPrecise.AsSpan(source * _restPrecise.Length, _restPrecise.Length);
            _clips[players[source].AnimationId].Sample(_restPrecise, sourceTimes[source], sample);
        }
        AlsDetailPoseComposer.Compose(cycle, _samplesPrecise, _restPrecise, velocity, output);
    }
}
