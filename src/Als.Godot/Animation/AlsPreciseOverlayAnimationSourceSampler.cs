using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsPreciseOverlayAnimationSourceSampler
{
    private readonly AlsOverlaySourceProfile _profile;
    private readonly AlsPreciseAnimationPoseSourceSampler[] _nodes;
    private readonly bool[] _variationPlayers;
    private readonly (int Bone, float Gain)[] _variationBones;
    private readonly AlsPrecisePose[] _variationReference;
    public AlsPreciseOverlayAnimationSourceSampler(AlsOverlaySourceProfile profile, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curves,
        IReadOnlyDictionary<string, float>? secondaryMotionRotationGains = null)
    {
        if (bank.BindingDigest != profile.BindingDigest || bank.PlayerCount != AlsOverlaySourceProfile.PlayerCount ||
            bank.SampleCount != AlsOverlaySourceProfile.PlayerCount || !bank.RootAnimationIds.SequenceEqual(profile.AnimationIds))
            throw new ArgumentException("Precise Overlay requires its verified source bank.");
        _profile = profile; var resources = new Dictionary<int, AlsPreciseAnimationPoseSourceSampler>();
        foreach (var id in profile.AnimationIds) resources.Add(id, new(bank.GetSource(id), bank, set, curves));
        _nodes = new AlsPreciseAnimationPoseSourceSampler[AlsOverlaySourceProfile.PlayerCount];
        foreach (var node in profile.Players) _nodes[node.Id] = resources[node.AnimationId];
        _variationPlayers = new bool[_nodes.Length];
        _variationBones = [];
        _variationReference = [];
        if (secondaryMotionRotationGains is { Count: > 0 })
        {
            var skeleton = bank.GetSkeleton(profile.SkeletonId);
            var names = skeleton.LogicalBoneNames.ToArray();
            _variationBones = secondaryMotionRotationGains.Select(pair =>
            {
                var bone = Array.IndexOf(names, pair.Key);
                if (bone < 0 || !float.IsFinite(pair.Value) || pair.Value is < 0 or > 4)
                    throw new ArgumentException("Invalid SecondaryMotion rotation gain: " + pair.Key);
                return (Bone: bone, Gain: pair.Value);
            }).ToArray();
            foreach (var node in profile.Players)
                _variationPlayers[node.Id] = set.Animations[node.AnimationId].Name == "ALS_N_SecondaryMotion" &&
                    (node.NodePath.Contains(".Default.", StringComparison.Ordinal) ||
                     node.NodePath.Contains(".Feminine.", StringComparison.Ordinal) ||
                     node.NodePath.Contains(".Masculine.", StringComparison.Ordinal));
            var source = Array.FindIndex(_variationPlayers, value => value);
            if (source < 0) throw new ArgumentException("No basic Overlay SecondaryMotion source.");
            _variationReference = new AlsPrecisePose[skeleton.LogicalBoneCount];
            _nodes[source].Sample(0, true, false, false, _variationReference, new AlsInertialCurve[curves.Length]);
        }
    }
    public void Sample(int source, double playerSeconds, double sweep, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if ((uint)source >= _nodes.Length) throw new ArgumentOutOfRangeException(nameof(source));
        _nodes[source].Sample(_profile.Players[source].ResolveTime(playerSeconds, sweep), true, false, false, pose, curves);
        if (_variationPlayers[source])
            foreach (var (bone, gain) in _variationBones)
                pose[bone] = AlsPoseRotationVariation.Amplify(pose[bone], _variationReference[bone], gain);
    }
}
