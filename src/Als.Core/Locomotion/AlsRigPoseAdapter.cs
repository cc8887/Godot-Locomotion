namespace GodotAls.Core.Locomotion;

// Target mapping is independent of Rig topology. Scratch belongs to one host;
// export materializes all globals before requesting normalized local caches.
public sealed class AlsRigPoseAdapter
{
    private readonly string?[] _mapping;
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _global, _result;
    public int BoneCount => _mapping.Length;
    public AlsRigPoseAdapter(IReadOnlyList<string?> mapping, IReadOnlyList<int> parents)
    {
        ArgumentNullException.ThrowIfNull(mapping); ArgumentNullException.ThrowIfNull(parents);
        if (mapping.Count == 0 || mapping.Count != parents.Count) throw new ArgumentException("Incomplete Rig output mapping.");
        _mapping = mapping.ToArray(); _parents = parents.ToArray();
        for (int b = 0; b < _mapping.Length; b++)
            if (_parents[b] < -1 || _parents[b] >= b || _mapping[b] is { } name && string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Invalid Rig output hierarchy or element name.");
        _global = new AlsPrecisePose[BoneCount]; _result = new AlsPrecisePose[BoneCount];
    }
    public void ExportLocalPose(AlsRigHierarchy hierarchy, ReadOnlySpan<AlsPrecisePose> source, float alpha, Span<AlsPrecisePose> output)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        if (source.Length != BoneCount || output.Length != BoneCount || !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentException("Invalid Rig output pose/alpha.");
        for (int b = 0; b < BoneCount; b++) source[b].Validate();
        if (alpha <= AlsPoseBlender.WeightThreshold) { source.CopyTo(output); return; }
        for (int b = 0; b < BoneCount; b++)
            _global[b] = _mapping[b] is { } name ? hierarchy.Get(name) :
                (_parents[b] < 0 ? source[b] : AlsPrecisePose.Compose(source[b], _global[_parents[b]])).Normalized();
        for (int b = 0; b < BoneCount; b++)
        {
            // Unmapped virtual bones keep the imported local pose. Mapped
            // bones use their Rig parent, including differing target parents.
            var rig = _mapping[b] is { } name ? hierarchy.Get(name, local: true) : source[b];
            _result[b] = alpha >= 1f - AlsPoseBlender.WeightThreshold ? rig :
                AlsPrecisePoseBlender.BlendAdditiveTarget(source[b], rig, alpha);
        }
        // Preserve output on failure and support overlapping input/output spans.
        _result.AsSpan().CopyTo(output);
    }
}
