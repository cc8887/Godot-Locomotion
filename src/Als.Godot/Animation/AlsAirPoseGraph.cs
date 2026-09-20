using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Fall / parent Jump contents. Source clocks and final state-machine inertialization belong to Main Movement.
internal sealed class AlsAirPoseGraph : IDisposable
{
    private readonly AlsAirPoseProfile _profile;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[7];
    private readonly float[] _lengths = new float[7];
    private readonly int[] _ids;
    private readonly int _playerCount, _sampleCount;
    private readonly AlsLeanPoseSampler?[] _leans = new AlsLeanPoseSampler?[2];
    private AlsJumpPoseGraph? _jump;
    private readonly AlsLocalPose[] _other, _prediction;
    private readonly AlsPrecisePose[] _otherPrecise, _predictionPrecise;

    public AlsAirPoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsAirPoseProfile profile)
    {
        var poseSources = library.MovementSources(set, profile.SkeletonId);
        _profile = profile; _playerCount = sources.Players.Length; _sampleCount = sources.Samples.Length;
        if (profile.SkeletonId != sources.SkeletonId || set.Skeletons[profile.SkeletonId].LogicalBones.Length != poseSources.BoneCount ||
            profile.Fall.Jump || !profile.Jump.Jump) throw new ArgumentException("Air pose profile/source layout differs.");
        _ids = [profile.Fall.Loop, profile.Fall.Fast, profile.Fall.Flail, profile.Fall.Heavy, profile.Fall.Light, profile.Jump.Heavy, profile.Jump.Light];
        _otherPrecise = new AlsPrecisePose[poseSources.BoneCount]; _predictionPrecise = new AlsPrecisePose[_otherPrecise.Length];
        _other = new AlsLocalPose[poseSources.BoneCount]; _prediction = new AlsLocalPose[_other.Length];
        try
        {
            for (var i = 0; i < _ids.Length; i++)
            {
                var animation = set.Animations[sources.Samples[sources.Players[_ids[i]].SampleStart].AnimationId];
                _lengths[i] = animation.PlayLength;
                _clips[i] = poseSources.Create(animation.Id);
            }
            _leans[0] = new(library, set, sources, profile.Fall.Lean);
            _leans[1] = new(library, set, sources, profile.Jump.Lean);
            _jump = new(library, set, sources, profile.NestedJump.Pose);
        }
        catch { Dispose(); throw; }
    }

    public void Compose(int state, in AlsAirPoseInputs inputs, in AlsGroundedMachineState jumpState,
        in AlsJumpBlendInputs jumpInputs, ReadOnlySpan<float> playerTimes, ReadOnlySpan<float> sampleTimes,
        ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        Validate(state, inputs, playerTimes, sampleTimes);
        if (rest.Length != _other.Length || output.Length != rest.Length || rest.Overlaps(output))
            throw new ArgumentException("Invalid air pose buffers.");
        var alpha = inputs.Prediction.PoseAlpha;
        if (alpha < 1)
        {
            if (state == 2) _jump!.Compose(jumpState, jumpInputs, playerTimes, rest, output);
            else
            {
                var flail = inputs.Flail.PoseAlpha;
                if (flail < 1) Pair(0, inputs.Fast.PoseAlpha, playerTimes, rest, output);
                if (flail == 1) Sample(2, playerTimes, rest, output);
                else if (flail > 0)
                {
                    Sample(2, playerTimes, rest, _other); Blend(output, _other, flail);
                }
            }
            if (inputs.AdditiveActive && inputs.LeanHasSamples)
                _leans[state - 1]!.Compose(LeanTimes(state, sampleTimes), inputs.Lean, output, output);
        }
        if (alpha == 1) Pair(state == 1 ? 3 : 5, inputs.PredictionLightPoseAlpha, playerTimes, rest, output);
        else if (alpha > 0)
        {
            Pair(state == 1 ? 3 : 5, inputs.PredictionLightPoseAlpha, playerTimes, rest, _prediction);
            Blend(output, _prediction, alpha);
        }
    }

    public AlsInertialCurve Curve(int state, in AlsAirPoseInputs inputs, in AlsGroundedMachineState jumpState,
        in AlsJumpBlendInputs jumpInputs, ReadOnlySpan<float> playerTimes, ReadOnlySpan<float> sampleTimes, string name)
    {
        Validate(state, inputs, playerTimes, sampleTimes);
        var alpha = inputs.Prediction.PoseAlpha; var value = default(AlsInertialCurve);
        if (alpha < 1)
        {
            if (state == 2) value = _jump!.Curve(jumpState, jumpInputs, playerTimes, name);
            else
            {
                var flail = inputs.Flail.PoseAlpha;
                if (flail < 1) value = PairCurve(0, inputs.Fast.PoseAlpha, playerTimes, name);
                if (flail > 0) value = flail == 1 ? SourceCurve(2, playerTimes, name) :
                    AlsStandingCycleCurves.Lerp(value, SourceCurve(2, playerTimes, name), flail);
            }
            if (inputs.AdditiveActive && inputs.LeanHasSamples)
                value = _leans[state - 1]!.ComposeCurve(LeanTimes(state, sampleTimes), inputs.Lean, name, value);
        }
        if (alpha > 0)
        {
            var prediction = PairCurve(state == 1 ? 3 : 5, inputs.PredictionLightPoseAlpha, playerTimes, name);
            value = alpha == 1 ? prediction : AlsStandingCycleCurves.Lerp(value, prediction, alpha);
        }
        return name is "BasePose_N" or "Weight_InAir" ? AlsStandingCycleCurves.ModifyBlend(value, 1, 1) : value;
    }

    private void Pair(int first, float alpha, ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output)
    {
        Sample(first + (alpha == 1 ? 1 : 0), times, rest, output);
        if (alpha is <= 0 or >= 1) return;
        Sample(first + 1, times, rest, _other); Blend(output, _other, alpha);
    }
    private static void Blend(Span<AlsLocalPose> first, ReadOnlySpan<AlsLocalPose> second, float alpha)
    { for (var i = 0; i < first.Length; i++) first[i] = AlsPoseBlender.Blend(first[i], second[i], alpha); }
    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, source < 3 ? times[_ids[source]] : 0, _lengths[source], output);
    private AlsInertialCurve PairCurve(int first, float alpha, ReadOnlySpan<float> times, string name) =>
        alpha == 0 ? SourceCurve(first, times, name) : alpha == 1 ? SourceCurve(first + 1, times, name) :
            AlsStandingCycleCurves.Lerp(SourceCurve(first, times, name), SourceCurve(first + 1, times, name), alpha);
    private AlsInertialCurve SourceCurve(int source, ReadOnlySpan<float> times, string name) =>
        _clips[source]!.Curve(source < 3 ? times[_ids[source]] : 0, name);
    private ReadOnlySpan<float> LeanTimes(int state, ReadOnlySpan<float> times) =>
        times.Slice((state == 1 ? _profile.Fall.Lean : _profile.Jump.Lean).SampleStart, 5);
    private void Validate(int state, in AlsAirPoseInputs inputs, ReadOnlySpan<float> playerTimes, ReadOnlySpan<float> sampleTimes)
    {
        if (_jump is null) throw new ObjectDisposedException(nameof(AlsAirPoseGraph));
        if (state is not (1 or 2) || playerTimes.Length != _playerCount || sampleTimes.Length != _sampleCount ||
            !float.IsFinite(inputs.Prediction.Alpha) || !float.IsFinite(inputs.Flail.Alpha) || !float.IsFinite(inputs.Fast.Alpha) ||
            !float.IsFinite(inputs.PredictionLight) || !float.IsFinite(inputs.Lean.LengthSquared()) ||
            inputs.Prediction.Alpha is < 0 or > 1 || inputs.Flail.Alpha is < 0 or > 1 || inputs.Fast.Alpha is < 0 or > 1 ||
            inputs.PredictionLight is < 0 or > 1) throw new ArgumentException("Invalid air pose state, inputs or clocks.");
        if (state == 1 && inputs.Prediction.PoseAlpha < 1)
            for (var i = 0; i < 3; i++)
                if (!float.IsFinite(playerTimes[_ids[i]]) || playerTimes[_ids[i]] < 0 || playerTimes[_ids[i]] > _lengths[i])
                    throw new ArgumentException("Invalid Fall source time.");
    }
    public void Dispose()
    {
        foreach (var clip in _clips) clip?.Dispose();
        foreach (var lean in _leans) lean?.Dispose();
        _jump?.Dispose(); _jump = null;
    }

    public void Compose(int state, in AlsAirPoseInputs inputs, in AlsGroundedMachineState jumpState,
        in AlsJumpBlendInputs jumpInputs, ReadOnlySpan<float> playerTimes, ReadOnlySpan<float> sampleTimes,
        ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        Validate(state, inputs, playerTimes, sampleTimes);
        if (rest.Length != _otherPrecise.Length || output.Length != rest.Length || rest.Overlaps(output))
            throw new ArgumentException("Invalid air pose buffers.");
        var alpha = inputs.Prediction.PoseAlpha;
        if (alpha < 1)
        {
            if (state == 2) _jump!.Compose(jumpState, jumpInputs, playerTimes, rest, output);
            else
            {
                var flail = inputs.Flail.PoseAlpha;
                if (flail < 1) Pair(0, inputs.Fast.PoseAlpha, playerTimes, rest, output);
                if (flail == 1) Sample(2, playerTimes, rest, output);
                else if (flail > 0)
                {
                    Sample(2, playerTimes, rest, _otherPrecise); Blend(output, _otherPrecise, flail);
                }
            }
            if (inputs.AdditiveActive && inputs.LeanHasSamples)
                _leans[state - 1]!.ComposePrecise(LeanTimes(state, sampleTimes), inputs.Lean, output, output);
        }
        if (alpha == 1) Pair(state == 1 ? 3 : 5, inputs.PredictionLightPoseAlpha, playerTimes, rest, output);
        else if (alpha > 0)
        {
            Pair(state == 1 ? 3 : 5, inputs.PredictionLightPoseAlpha, playerTimes, rest, _predictionPrecise);
            Blend(output, _predictionPrecise, alpha);
        }
    }
    private void Pair(int first, float alpha, ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output)
    {
        Sample(first + (alpha == 1 ? 1 : 0), times, rest, output);
        if (alpha is <= 0 or >= 1) return;
        Sample(first + 1, times, rest, _otherPrecise); Blend(output, _otherPrecise, alpha);
    }
    private static void Blend(Span<AlsPrecisePose> first, ReadOnlySpan<AlsPrecisePose> second, float alpha)
    { for (var i = 0; i < first.Length; i++) first[i] = AlsPrecisePoseBlender.Blend(first[i], second[i], alpha); }
    private void Sample(int source, ReadOnlySpan<float> times, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> output) =>
        _clips[source]!.SampleSourceSeconds(rest, source < 3 ? times[_ids[source]] : 0, _lengths[source], output);

}
