using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Source evaluation only. Seven evaluator clocks/relevance remain with the Aim
// frame owner; these immutable assets and per-character scratch do not own time.
internal sealed class AlsAimAnimationSourceSampler : IAlsAimPoseSource
{
    private readonly AlsAimSamplingProfile _profile;
    private readonly AlsAnimationPoseSourceSampler[] _samples;
    private readonly AlsLocalPose[] _poses;
    private readonly AlsInertialCurve[] _curves;
    private readonly int _bones, _curveCount;
    private int _sampling;
    public AlsAimAnimationSourceSampler(AlsAimSamplingProfile profile, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        if (bank.BindingDigest != profile.BindingDigest || bank.PlayerCount != AlsAimSamplingProfile.PlayerCount ||
            bank.SampleCount != AlsAimSamplingProfile.SampleCount || !bank.RootAnimationIds.SequenceEqual(profile.AnimationIds))
            throw new ArgumentException("Aim sampling requires its verified source closure.");
        _profile = profile; _bones = bank.GetSkeleton(profile.SkeletonId).LogicalBoneCount; _curveCount = curveNames.Length;
        _samples = new AlsAnimationPoseSourceSampler[3]; _poses = new AlsLocalPose[_bones * 3]; _curves = new AlsInertialCurve[_curveCount * 3];
        for (var i = 0; i < 3; i++)
        {
            var source = bank.GetSource(profile.AnimationIds[i]); var policy = source.Policy;
            if (policy.AdditiveType != AlsRawAnimationAdditiveType.RotationOffsetMeshSpace || policy.SequencePlayLength != 1 ||
                policy.BasePoseType != AlsRawAnimationBasePoseType.AnimFrame || policy.BaseFrame != 0 || policy.BaseAnimationId != profile.BaseAnimationId)
                throw new ArgumentException("Aim raw source no longer matches its mesh-space additive policy.");
            _samples[i] = new(source, bank, set, curveNames);
        }
    }

    public void Sample(int evaluator, in AlsAimEvaluatorInput input, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if ((uint)evaluator >= AlsAimSamplingProfile.PlayerCount || input.NormalizedTime != (evaluator >= 2))
            throw new ArgumentException("Aim evaluator source or time domain differs.");
        Sample(input, pose, curves);
    }

    public void Sample(in AlsAimEvaluatorInput input, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (pose.Length != _bones || curves.Length != _curveCount || !float.IsFinite(input.Time) || !float.IsFinite(input.Position))
            throw new ArgumentException("Invalid Aim source sample.");
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0) throw new InvalidOperationException("Aim source scratch is already in use.");
        try
        {
            // All three native sequences are exactly one second. Sequence input
            // is seconds; BlendSpace input is normalized, then converted to each
            // sample's seconds. Both clamp, with no clock advancement on teleport.
            var time = System.Math.Clamp(input.Time, 0, 1);
            if (!input.NormalizedTime) { _samples[0].Sample(time, true, false, false, pose, curves); return; }
            Span<float> weights = stackalloc float[3]; Span<int> order = stackalloc int[3];
            var count = _profile.Runtime.Evaluate(input.Position, weights, order);
            for (var i = 0; i < count; i++)
            {
                var sample = order[i];
                _samples[sample].Sample(time, true, false, false, _poses.AsSpan(sample * _bones, _bones),
                    _curves.AsSpan(sample * _curveCount, _curveCount));
            }
            for (var bone = 0; bone < _bones; bone++)
            {
                var result = AlsPoseBlender.Scale(_poses[order[0] * _bones + bone], weights[order[0]]);
                for (var i = 1; i < count; i++) result = AlsPoseBlender.Accumulate(result, _poses[order[i] * _bones + bone], weights[order[i]]);
                pose[bone] = AlsPoseBlender.Normalize(result);
            }
            curves.Clear();
            for (var curve = 0; curve < _curveCount; curve++)
            {
                var value = 0f; var present = false;
                for (var i = 0; i < count; i++)
                {
                    var sample = order[i]; var source = _curves[sample * _curveCount + curve];
                    if (!source.Present) continue;
                    value += source.Value * weights[sample]; present = true;
                }
                if (present) curves[curve] = new(value);
            }
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
}
