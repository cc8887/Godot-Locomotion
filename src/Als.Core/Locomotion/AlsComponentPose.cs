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
    public void Begin(ReadOnlySpan<AlsPrecisePose> pose)
    {
        if (pose.Length != _pose.Length) throw new ArgumentException("Component pose layout differs.");
        Array.Clear(_component);
        for (var i = 0; i < pose.Length; i++)
        { _pose[i] = pose[i]; _pose[i].Validate(.001); _component[i] = _parents[i] < 0; }
    }
    public AlsPrecisePose Component(int bone)
    {
        if (!_component[bone])
        {
            _pose[bone] = AlsComponentQuaternion.Normalize(AlsPrecisePose.Compose(_pose[bone], Component(_parents[bone])));
            _component[bone] = true;
        }
        return _pose[bone];
    }
    public AlsPrecisePose Local(int bone) => !_component[bone] || _parents[bone] < 0 ? _pose[bone] :
        AlsPrecisePose.Relative(_pose[bone], Component(_parents[bone]));

    // FCSPose::SetComponentSpaceTransform is a direct write, unlike
    // SafeSetCSBoneTransforms/LocalBlend used by the other bone controllers.
    // It deliberately retains any already-cached descendants.
    public void SetComponent(int bone, in AlsPrecisePose transform)
    {
        if ((uint)bone >= _pose.Length) throw new ArgumentOutOfRangeException(nameof(bone));
        transform.Validate(.001); _pose[bone] = transform; _component[bone] = true;
    }

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
            _pose[bone] = BlendWith(_pose[bone], _oldLocal[bone], inverse);
    }
    // FCSPose calls FTransform::BlendWith, whose inclusive float endpoint
    // differs from Blend's Abs(alpha - 1) check at a rounded complement.
    private static AlsPrecisePose BlendWith(in AlsPrecisePose basis,in AlsPrecisePose other,float alpha)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold)return basis;
        if(alpha>=1f-AlsPoseBlender.WeightThreshold)return other;
        // Native SSE2 VectorDot4 adds X+Z and Y+W before the final sum.
        var sign=AlsComponentQuaternion.Dot(basis.Rotation,other.Rotation)>=0?1d:-1d;
        // FTransform::BlendWith uses VectorLerp: each contribution is rounded
        // before addition. The difference form can move a nearly straight IK
        // chain across its extension branch by one double ULP.
        return new(basis.Position*(1d-alpha)+other.Position*alpha,
            AlsComponentQuaternion.Normalize(basis.Rotation*(sign*(1d-alpha))+other.Rotation*alpha),
            basis.Scale*(1d-alpha)+other.Scale*alpha);
    }
    public void Export(Span<AlsLocalPose> output)
    {
        if (output.Length != _pose.Length) throw new ArgumentException("Component output layout differs.");
        for (var bone = _pose.Length - 1; bone >= 0; bone--)
        {
            var value = _component[bone] && _parents[bone] >= 0
                ? AlsComponentQuaternion.Normalize(AlsPrecisePose.Relative(_pose[bone], _pose[_parents[bone]])) : _pose[bone];
            value.Validate(.001); output[bone] = value.ToSingle();
        }
    }
    public void Export(Span<AlsPrecisePose> output)
    {
        if (output.Length != _pose.Length) throw new ArgumentException("Component output layout differs.");
        for (var bone = _pose.Length - 1; bone >= 0; bone--)
        {
            var value = _component[bone] && _parents[bone] >= 0
                ? AlsComponentQuaternion.Normalize(AlsPrecisePose.Relative(_pose[bone], _pose[_parents[bone]])) : _pose[bone];
            value.Validate(.001); output[bone] = value;
        }
    }
    private void ToLocal(int bone)
    {
        if (!_component[bone] || _parents[bone] < 0) return;
        _pose[bone] = AlsPrecisePose.Relative(_pose[bone], _pose[_parents[bone]]); _component[bone] = false;
    }
}
