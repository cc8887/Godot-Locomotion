using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

/// <summary>Five native Lean additives, sampled only at borrowed source seconds.</summary>
internal sealed class AlsLeanPoseSampler : IDisposable
{
    private readonly AlsLeanSamplingProfile _profile;
    private readonly AlsMovementAnimationSource?[] _clips = new AlsMovementAnimationSource?[6];
    private readonly float[] _durations = new float[5];
    private readonly float[] _weights = new float[5];
    private readonly int[] _order = new int[5];
    private readonly AlsLocalPose[] _rest, _basePose, _samples;
    private readonly AlsPrecisePose[] _preciseSamples;

    public AlsLeanPoseSampler(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsLocomotionSourceProfile sources, AlsLeanSamplingProfile profile)
        : this(library,set,sources.Samples,profile) { }

    public AlsLeanPoseSampler(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        ReadOnlySpan<AlsLocomotionSourceSample> sources, AlsLeanSamplingProfile profile)
    {
        _profile = profile;
        var poseSources = library.MovementSources(set, profile.SkeletonId); var count = poseSources.BoneCount;
        _rest = poseSources.ReferencePose.ToArray(); _basePose = new AlsLocalPose[count]; _samples = new AlsLocalPose[count * 5];
        _preciseSamples=new AlsPrecisePose[count*5];
        try
        {
            var samples = sources;
            for (var i = 0; i < 6; i++)
            {
                var id = i < 5 ? samples[profile.SampleStart + i].AnimationId : profile.BaseAnimationId;
                var animation = set.Animations[id];
                if (animation.SkeletonId != profile.SkeletonId || !library.ClipNames.TryGetValue(id, out var name))
                    throw new InvalidOperationException("Lean source missing from the animation closure.");
                _clips[i] = poseSources.Create(id, rawBonePose: i == 5);
                if (Math.Abs(_clips[i]!.Length - animation.PlayLength) > .000001)
                    throw new InvalidOperationException("Lean source time domain differs.");
                if (i < 5) _durations[i] = samples[profile.SampleStart + i].DurationSeconds;
            }
            _clips[5]!.Sample(_rest, 0, _basePose);
        }
        catch { Dispose(); throw; }
    }

    internal ReadOnlySpan<AlsLocalPose> AdditiveBasePose => _basePose;

    public void Compose(ReadOnlySpan<float> sourceSeconds, NVector2 input, ReadOnlySpan<AlsLocalPose> basis, Span<AlsLocalPose> output, float alpha=1)
    {
        ValidateTimes(sourceSeconds);
        if (basis.Length != _rest.Length || output.Length != _rest.Length ||
            basis.Overlaps(output, out var offset) && offset != 0) throw new ArgumentException("Invalid Lean pose output.");
        var count = _profile.Runtime.Evaluate(input, _weights, _order);
        foreach (var i in _order.AsSpan(0, count))
        {
            var sample = _samples.AsSpan(i * _rest.Length, _rest.Length);
            _clips[i]!.SampleSourceSeconds(_rest, sourceSeconds[i], _durations[i], sample);
        }
        AlsLeanBlendSpace.Apply(basis, _samples, _weights, _order.AsSpan(0, count), output,alpha);
    }

    public float Curve(ReadOnlySpan<float> sourceSeconds, NVector2 input, string name, float basis)
        => ComposeCurve(sourceSeconds, input, name, new(basis)).Value;

    public void ComposePrecise(ReadOnlySpan<float> sourceSeconds,NVector2 input,ReadOnlySpan<AlsPrecisePose> basis,
        Span<AlsPrecisePose> output,float alpha=1)
    {
        ValidateTimes(sourceSeconds);
        if(basis.Length!=_rest.Length || output.Length!=_rest.Length)throw new ArgumentException("Invalid precise Lean output.");
        var count=_profile.Runtime.Evaluate(input,_weights,_order);
        foreach(var i in _order.AsSpan(0,count))
            _clips[i]!.SamplePrecise(sourceSeconds[i],_preciseSamples.AsSpan(i*_rest.Length,_rest.Length));
        AlsLeanBlendSpace.Apply(basis,_preciseSamples,_weights,_order.AsSpan(0,count),output,alpha);
    }

    public AlsInertialCurve ComposeCurve(ReadOnlySpan<float> sourceSeconds, NVector2 input, string name, AlsInertialCurve basis)
    {
        ValidateTimes(sourceSeconds);
        if (basis.Present && !float.IsFinite(basis.Value)) throw new ArgumentException("Invalid Lean base curve.");
        var count = _profile.Runtime.Evaluate(input, _weights, _order);
        var mixed = 0f; var present = false;
        foreach (var sample in _order.AsSpan(0, count))
        {
            var value = SourceCurve(sample, sourceSeconds[sample], name);
            if (!value.Present) continue;
            mixed += value.Value * _weights[sample]; present = true;
        }
        return present ? new((basis.Present ? basis.Value : 0) + mixed) : basis;
    }

    private AlsInertialCurve SourceCurve(int sample, float time, string name) =>
        _clips[sample]!.Curve(time, name);

    private void ValidateTimes(ReadOnlySpan<float> seconds)
    {
        if (_clips[5] is null) throw new ObjectDisposedException(nameof(AlsLeanPoseSampler));
        if (seconds.Length != 5) throw new ArgumentException("Lean requires five source sample times.");
        for (var i = 0; i < 5; i++)
            if (!float.IsFinite(seconds[i]) || seconds[i] < 0 || seconds[i] > _durations[i])
                throw new ArgumentException("Invalid borrowed Lean source time.");
    }

    public void Dispose()
    {
        for (var i = 0; i < _clips.Length; i++) { _clips[i]?.Dispose(); _clips[i] = null; }
    }
}
