using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsPreciseOverlayAnimationSourceSampler
{
    private readonly AlsOverlaySourceProfile _profile;
    private readonly AlsPreciseAnimationPoseSourceSampler[] _nodes;
    public AlsPreciseOverlayAnimationSourceSampler(AlsOverlaySourceProfile profile, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curves)
    {
        if (bank.BindingDigest != profile.BindingDigest || bank.PlayerCount != AlsOverlaySourceProfile.PlayerCount ||
            bank.SampleCount != AlsOverlaySourceProfile.PlayerCount || !bank.RootAnimationIds.SequenceEqual(profile.AnimationIds))
            throw new ArgumentException("Precise Overlay requires its verified source bank.");
        _profile = profile; var resources = new Dictionary<int, AlsPreciseAnimationPoseSourceSampler>();
        foreach (var id in profile.AnimationIds) resources.Add(id, new(bank.GetSource(id), bank, set, curves));
        _nodes = new AlsPreciseAnimationPoseSourceSampler[AlsOverlaySourceProfile.PlayerCount];
        foreach (var node in profile.Players) _nodes[node.Id] = resources[node.AnimationId];
    }
    public void Sample(int source, double playerSeconds, double sweep, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if ((uint)source >= _nodes.Length) throw new ArgumentOutOfRangeException(nameof(source));
        _nodes[source].Sample(_profile.Players[source].ResolveTime(playerSeconds, sweep), true, false, false, pose, curves);
    }
}
