using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// One scratch owner per character. Immutable resources may be shared, but node
// clocks and sync/relevance histories are supplied by the enclosing graph owner.
internal sealed class AlsOverlayAnimationSourceSampler
{
    private readonly AlsOverlaySourceProfile _profile;
    private readonly AlsAnimationPoseSourceSampler[] _nodes;
    public AlsOverlayAnimationSourceSampler(AlsOverlaySourceProfile profile, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, ReadOnlySpan<string> curveNames)
    {
        if (bank.BindingDigest != profile.BindingDigest || bank.PlayerCount != AlsOverlaySourceProfile.PlayerCount ||
            bank.SampleCount != AlsOverlaySourceProfile.PlayerCount || !bank.RootAnimationIds.SequenceEqual(profile.AnimationIds))
            throw new ArgumentException("Overlay requires its verified source bank and occurrence layout.");
        _profile = profile;
        var resources = new Dictionary<int, AlsAnimationPoseSourceSampler>();
        foreach (var id in profile.AnimationIds) resources.Add(id, new(bank.GetSource(id), bank, set, curveNames));
        _nodes = new AlsAnimationPoseSourceSampler[AlsOverlaySourceProfile.PlayerCount];
        foreach (var node in profile.Players) _nodes[node.Id] = resources[node.AnimationId];
    }
    public void Sample(int source, double playerSeconds, double aimSweepTime, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if ((uint)source >= _nodes.Length) throw new ArgumentOutOfRangeException(nameof(source));
        var time = _profile.Players[source].ResolveTime(playerSeconds, aimSweepTime);
        _nodes[source].Sample(time, true, false, false, pose, curves);
    }
}
