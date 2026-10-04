namespace GodotAls.Core.Locomotion;

/// <summary>FAnimNode_CopyBone in ComponentSpace followed by native local alpha blending.
/// Source and target are logical pose channels; neither must belong to the skin.</summary>
public sealed class AlsComponentCopyBoneController
{
    private readonly AlsComponentPose _pose;
    private readonly int _count, _source, _target;
    private readonly bool _translation, _rotation, _scale;

    public AlsComponentCopyBoneController(ReadOnlySpan<int> parents, int source, int target,
        bool copyTranslation, bool copyRotation, bool copyScale)
    {
        if (parents.IsEmpty || (uint)source >= parents.Length || (uint)target >= parents.Length)
            throw new ArgumentException("CopyBone requires a valid logical layout and bone identities.");
        for (var bone = 0; bone < parents.Length; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone)
                throw new ArgumentException("CopyBone requires a parent-first hierarchy.");
        _pose = new(parents); _count = parents.Length; _source = source; _target = target;
        _translation = copyTranslation; _rotation = copyRotation; _scale = copyScale;
    }

    public void Evaluate(ReadOnlySpan<AlsPrecisePose> input, float alpha, Span<AlsPrecisePose> output)
    {
        if (input.Length != _count || output.Length != _count || input.Overlaps(output) ||
            !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentException("Invalid CopyBone pose buffers or alpha.");
        _pose.Begin(input);
        if (alpha <= AlsPoseBlender.WeightThreshold || !(_translation || _rotation || _scale))
        { input.CopyTo(output); return; }
        Evaluate(_pose,alpha);_pose.Export(output);
    }
    internal void Evaluate(AlsComponentPose pose,float alpha)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold||!(_translation||_rotation||_scale))return;
        // Preserve the native access order and target fields which are not selected.
        var source = pose.Component(_source);
        var target = pose.Component(_target);
        var change = new AlsPrecisePose(_translation ? source.Position : target.Position,
            _rotation ? source.Rotation : target.Rotation, _scale ? source.Scale : target.Scale);
        Span<int> bones = stackalloc int[1] { _target };
        Span<AlsPrecisePose> changes = stackalloc AlsPrecisePose[1] { change };
        pose.Apply(bones, changes, alpha);
    }
}
