using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

// State contents only. The Main Movement owner supplies source clocks, captured inputs and cached Grounded pose.
internal sealed class AlsLandingPoseGraph : IDisposable
{
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[5];
    private readonly int[] _ids, _parents;
    private readonly float[] _lengths = new float[4];
    private readonly AlsLocalPose[] _rest, _reference, _a, _b;
    private readonly NQuaternion[] _rotations;
    private readonly int _sourceCount;
    private readonly AlsPrecisePose[] _restPrecise, _aPrecise, _bPrecise;
    private readonly AlsQuaternion[] _rotationsPrecise;

    public AlsLandingPoseGraph(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsLandingPoseProfile profile)
    {
        _ids = [profile.Light, profile.Heavy, profile.MovingLight, profile.MovingHeavy];
        var poseSources = library.MovementSources(set, profile.SkeletonId);
        _sourceCount = sources.Players.Length; var count = poseSources.BoneCount;
        if (profile.SkeletonId != sources.SkeletonId) throw new ArgumentException("Landing pose skeleton differs.");
        _restPrecise = poseSources.PreciseReferencePose.ToArray(); _aPrecise = new AlsPrecisePose[count]; _bPrecise = new AlsPrecisePose[count]; _rotationsPrecise = new AlsQuaternion[count * 2];
        _rest = poseSources.ReferencePose.ToArray(); _reference = new AlsLocalPose[count]; _a = new AlsLocalPose[count]; _b = new AlsLocalPose[count];
        _rotations = new NQuaternion[count * 2]; _parents = poseSources.Parents.ToArray();
        try
        {
            var players = sources.Players; var samples = sources.Samples;
            for (var i = 0; i < 5; i++)
            {
                var animation = set.Animations[i == 4 ? profile.AdditiveBaseAnimationId : samples[players[_ids[i]].SampleStart].AnimationId];
                _clips[i] = poseSources.Create(animation.Id, rawBonePose: i == 4);
                if (i < 4) _lengths[i] = animation.PlayLength;
            }
            _clips[4]!.Sample(_rest, 0, _reference);
        }
        catch { Dispose(); throw; }
    }
    public void Compose(int state, in AlsLandingBlendInputs inputs, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsLocalPose> grounded, Span<AlsLocalPose> output)
    {
        var alpha = Validate(state, inputs, times);
        if (output.Length != _rest.Length || grounded.Length != _rest.Length || grounded.Overlaps(output))
            throw new ArgumentException("Invalid landing pose buffers.");
        if (state == 6 && !inputs.MovingAdditiveActive) { grounded.CopyTo(output); return; }
        var slot = state == 3 ? 0 : 2;
        Sample(slot + (alpha == 1 ? 1 : 0), times, _a);
        if (alpha is > 0 and < 1)
        {
            Sample(slot + 1, times, _b);
            for (var i = 0; i < _a.Length; i++) _a[i] = AlsPoseBlender.Blend(_a[i], _b[i], alpha);
        }
        if (state == 3) _a.CopyTo(output);
        else AlsMeshSpaceAdditivePose.Apply(grounded, _a, _parents, _rotations, output);
    }
    public AlsInertialCurve Curve(int state, in AlsLandingBlendInputs inputs, ReadOnlySpan<float> times, string name, AlsInertialCurve grounded)
    {
        var alpha = Validate(state, inputs, times);
        var slot = state == 3 ? 0 : 2;
        var value = state == 6 && !inputs.MovingAdditiveActive ? default : alpha == 0 ? SourceCurve(slot, times, name) : alpha == 1 ? SourceCurve(slot + 1, times, name) :
            AlsStandingCycleCurves.Lerp(SourceCurve(slot, times, name), SourceCurve(slot + 1, times, name), alpha);
        if (state == 6) value = AlsStandingCycleCurves.Accumulate(grounded, value, 1);
        var producerName = AlsRefactoredV4SourceCurves.V4ProducerName(name);
        return name is "Enable_FootIK_L" or "Enable_FootIK_R" or "BasePose_N" || state == 3 && producerName is "FootLock_L" or "FootLock_R"
            ? AlsStandingCycleCurves.ModifyBlend(value, 1, 1) : value;
    }
    private void Sample(int slot, ReadOnlySpan<float> times, AlsLocalPose[] output)
    {
        _clips[slot]!.SampleSourceSeconds(_rest, times[_ids[slot]], _lengths[slot], output);
    }
    private AlsInertialCurve SourceCurve(int slot, ReadOnlySpan<float> times, string name) =>
        _clips[slot]!.Curve(times[_ids[slot]], name);
    private float Validate(int state, in AlsLandingBlendInputs inputs, ReadOnlySpan<float> times)
    {
        var alpha = inputs.PoseAlpha(state);
        if (times.Length != _sourceCount) throw new ArgumentException("Landing source time layout differs.");
        var slot = state == 3 ? 0 : 2;
        for (var i = slot; i < slot + 2; i++)
            if (!float.IsFinite(times[_ids[i]]) || times[_ids[i]] < 0 || times[_ids[i]] > _lengths[i]) throw new ArgumentException("Invalid landing source time.");
        return alpha;
    }
    public void Dispose() { foreach (var clip in _clips) clip?.Dispose(); }

    public void Compose(int state, in AlsLandingBlendInputs inputs, ReadOnlySpan<float> times,
        ReadOnlySpan<AlsPrecisePose> grounded, Span<AlsPrecisePose> output)
    {
        var alpha = Validate(state, inputs, times);
        if (output.Length != _restPrecise.Length || grounded.Length != _restPrecise.Length || grounded.Overlaps(output))
            throw new ArgumentException("Invalid landing pose buffers.");
        if (state == 6 && !inputs.MovingAdditiveActive) { grounded.CopyTo(output); return; }
        var slot = state == 3 ? 0 : 2;
        Sample(slot + (alpha == 1 ? 1 : 0), times, _aPrecise);
        if (alpha is > 0 and < 1)
        {
            Sample(slot + 1, times, _bPrecise);
            for (var i = 0; i < _aPrecise.Length; i++) _aPrecise[i] = AlsPrecisePoseBlender.Blend(_aPrecise[i], _bPrecise[i], alpha);
        }
        if (state == 3) _aPrecise.CopyTo(output);
        else AlsPrecisePoseBlender.MeshApply(grounded, _aPrecise, _parents, _rotationsPrecise, output, 1);
    }
    private void Sample(int slot, ReadOnlySpan<float> times, AlsPrecisePose[] output)
    {
        _clips[slot]!.SampleSourceSeconds(_restPrecise, times[_ids[slot]], _lengths[slot], output);
    }
}
