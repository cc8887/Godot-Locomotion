namespace GodotAls.Core.Locomotion;

/// <summary>Raw binary32 keys expanded into native double transforms. Reference transforms
/// are retained at their original precision; no float pose is an intermediate sample.</summary>
public sealed class AlsPreciseRawSequenceSampler
{
    private readonly AlsRawAnimationPoseData _data;
    private readonly int[] _parents, _rawSources;
    private readonly AlsLogicalVirtualBone[] _virtualBones;
    private readonly AlsPrecisePose[] _reference, _first, _second, _components;
    private int _sampling;
    private readonly AlsRawFrameTimeRounding _rounding;

    public AlsPreciseRawSequenceSampler(AlsRawAnimationPoseData data, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<AlsLogicalVirtualBone> virtualBones,
        AlsRawFrameTimeRounding rounding = AlsRawFrameTimeRounding.OptimizedCancellation)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (rounding is not (AlsRawFrameTimeRounding.OptimizedCancellation or AlsRawFrameTimeRounding.RoundSubframe))
            throw new ArgumentOutOfRangeException(nameof(rounding));
        _rounding = rounding;
        if (reference.Length != data.LogicalBoneCount || virtualBones.Length != data.VirtualBoneCount)
            throw new ArgumentException("Precise raw reference layout differs.");
        var single = new AlsLocalPose[reference.Length];
        for (var bone = 0; bone < reference.Length; bone++) { reference[bone].Validate(); single[bone] = reference[bone].ToSingle(); }
        _ = new AlsLogicalPoseExpansion(parents, data.LogicalToPhysical, single, virtualBones);
        _data = data; _parents = parents.ToArray(); _reference = reference.ToArray(); _virtualBones = virtualBones.ToArray();
        _first = new AlsPrecisePose[reference.Length]; _second = new AlsPrecisePose[reference.Length]; _components = new AlsPrecisePose[reference.Length];
        _rawSources = new int[virtualBones.Length];
        for (var i = 0; i < virtualBones.Length; i++)
        {
            var vb = virtualBones[i];
            if (vb.Bone != data.VirtualLogicalIndices[i]) throw new ArgumentException("Precise virtual track order differs.");
            _rawSources[i] = vb.Source;
            foreach (var other in virtualBones) if (other.Bone == vb.Source) { _rawSources[i] = other.Target; break; }
        }
    }
    public AlsRawPoseKeySelection Sample(double seconds, Span<AlsPrecisePose> output)
    {
        if (output.Length != _reference.Length) throw new ArgumentException("Precise raw output layout differs.");
        var keys = AlsRawSequencePoseSampler.SelectKeys(_data, seconds, _rounding);
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0) throw new InvalidOperationException("Precise source scratch is already in use.");
        try
        {
            Expand(keys.FirstKey, _first);
            if (!keys.Interpolate) _first.CopyTo(output);
            else
            {
                Expand(keys.SecondKey, _second);
                for (var bone = 0; bone < output.Length; bone++) output[bone] = AlsPrecisePose.BlendTransform(_first[bone], _second[bone], keys.Alpha);
            }
            return keys;
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
    private void Expand(int key, Span<AlsPrecisePose> output)
    {
        _reference.CopyTo(output);
        var physical = _data.GetPhysicalKey(key); var explicitVirtual = _data.GetVirtualKey(key);
        for (var i = 0; i < physical.Length; i++)
        {
            var logical = _data.PhysicalToLogical[i];
            if (_data.LogicalTrackPresence[logical]) output[logical] = new(physical[i]);
        }
        for (var i = 0; i < _virtualBones.Length; i++)
            if (_data.VirtualTrackPresence[i]) output[_virtualBones[i].Bone] = new(explicitVirtual[i]);
        for (var bone = 0; bone < output.Length; bone++)
            _components[bone] = _parents[bone] < 0 ? output[bone] : AlsPrecisePose.Compose(output[bone], _components[_parents[bone]]).Normalized();
        for (var i = 0; i < _virtualBones.Length; i++)
            if (!_data.VirtualTrackPresence[i]) output[_virtualBones[i].Bone] = AlsPrecisePose.Relative(_components[_virtualBones[i].Target], _components[_rawSources[i]]);
    }
}
