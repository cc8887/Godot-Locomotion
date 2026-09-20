namespace GodotAls.Core.Locomotion;

// FCSPose storage semantics for sequential component-space bone controllers.
// Components are calculated lazily; overwriting a parent first restores any
// already-cached descendants to local space. All buffers belong to one owner.
internal sealed class AlsComponentPose
{
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _pose, _oldLocal;
    private readonly bool[] _component, _mask, _modified;
    public AlsComponentPose(ReadOnlySpan<int> parents)
    {
        _parents = parents.ToArray(); _pose = new AlsPrecisePose[parents.Length];
        _oldLocal = new AlsPrecisePose[parents.Length]; _component = new bool[parents.Length];
        _mask = new bool[parents.Length]; _modified = new bool[parents.Length];
    }
    public void Begin(ReadOnlySpan<AlsLocalPose> pose)
    {
        if (pose.Length != _pose.Length) throw new ArgumentException("Component pose layout differs.");
        Array.Clear(_component);
        for (var i = 0; i < pose.Length; i++)
        { _pose[i] = new(pose[i]); _pose[i].Validate(.001); _component[i] = _parents[i] < 0; }
    }
    public AlsPrecisePose Component(int bone)
    {
        if (!_component[bone])
        {
            _pose[bone] = AlsPrecisePose.Compose(_pose[bone], Component(_parents[bone])).Normalized();
            _component[bone] = true;
        }
        return _pose[bone];
    }
    public AlsPrecisePose Local(int bone) => !_component[bone] || _parents[bone] < 0 ? _pose[bone] :
        AlsPrecisePose.Relative(_pose[bone], Component(_parents[bone]));

    public void Apply(ReadOnlySpan<int> bones, ReadOnlySpan<AlsPrecisePose> transforms, float alpha)
    {
        if (bones.Length != transforms.Length || bones.IsEmpty || !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentException("Invalid component control blend.");
        if (alpha <= AlsPoseBlender.WeightThreshold) return;
        Array.Clear(_mask); Array.Clear(_modified);
        var partial = alpha < 1 - AlsPoseBlender.WeightThreshold;
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            if ((uint)bone >= _pose.Length || i > 0 && bone <= bones[i - 1]) throw new ArgumentException("Controller bones must be sorted.");
            transforms[i].Validate(.001);
            if (partial) _oldLocal[bone] = Local(bone);
            _modified[bone] = true; _mask[bone] = _component[bone];
        }
        for (var bone = bones[0]; bone < _pose.Length; bone++)
            if (_component[bone] && _parents[bone] >= 0 && _mask[_parents[bone]]) _mask[bone] = true;
        for (var bone = _pose.Length - 1; bone >= 0; bone--)
            if (_mask[bone] && !_modified[bone]) ToLocal(bone);
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i]; if (_parents[bone] >= 0) _ = Component(_parents[bone]);
            _pose[bone] = transforms[i]; _component[bone] = true;
        }
        if (!partial) return;
        _modified.CopyTo(_mask, 0);
        for (var bone = 0; bone < _pose.Length; bone++)
            if (_parents[bone] >= 0) _mask[bone] |= _mask[_parents[bone]];
        for (var bone = _pose.Length - 1; bone >= 0; bone--) if (_mask[bone]) ToLocal(bone);
        var inverse = 1f - alpha;
        foreach (var bone in bones)
            _pose[bone] = AlsPrecisePose.BlendTransform(_pose[bone], _oldLocal[bone], inverse);
    }
    public void Export(Span<AlsLocalPose> output)
    {
        if (output.Length != _pose.Length) throw new ArgumentException("Component output layout differs.");
        for (var bone = _pose.Length - 1; bone >= 0; bone--)
        {
            var value = _component[bone] && _parents[bone] >= 0
                ? AlsPrecisePose.Relative(_pose[bone], _pose[_parents[bone]]).Normalized() : _pose[bone];
            value.Validate(.001); output[bone] = value.ToSingle();
        }
    }
    private void ToLocal(int bone)
    {
        if (!_component[bone] || _parents[bone] < 0) return;
        _pose[bone] = AlsPrecisePose.Relative(_pose[bone], _pose[_parents[bone]]); _component[bone] = false;
    }
}
