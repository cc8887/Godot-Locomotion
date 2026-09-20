using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed class AlsMontageRootMotionReader
{
    private readonly Dictionary<int, AlsRawRootMotionSampler> _sources = new();
    public AlsMontageRootMotionReader(AlsMovementGraphDefinition definition)
    {
        foreach (var asset in definition.AuthoredMontageAssets)
        {
            if (!asset.RootMotionEnabled || _sources.ContainsKey(asset.AnimationId)) continue;
            var source = definition.RawSources.GetSource(asset.AnimationId);
            var policy = source.Policy;
            if (!policy.EnableRootMotion || policy.AdditiveType != AlsRawAnimationAdditiveType.None || policy.TransformCurveCount != 0)
                throw new ArgumentException("Unsupported or stale montage motion source.");
            var skeleton = definition.RawSources.GetSkeleton(source.PoseData.Identity.SkeletonId);
            _sources.Add(asset.AnimationId, new(source.PoseData, skeleton.PreciseReferencePose[0], policy.UseNormalizedRootMotionScale));
        }
    }
    internal AlsPrecisePose ExtractFbx(in AlsMontageRootMotionRange range) => !range.HasMotion ? AlsPrecisePose.Identity
        : _sources[range.AnimationId].Extract(range.StartSeconds, range.EndSeconds);
    public AlsRootMotionDelta Read(in AlsMontageRootMotionRange range)
    {
        var pose = ExtractFbx(range); var p = pose.Position; var q = pose.Rotation.Normalized();
        // FBX bone axes -> canonical Godot component axes. This is mesh-local
        // motion, not world/actor displacement; the motor must apply the actual
        // mesh-to-character transform before collision movement.
        return new(new((float)-p.Y, (float)p.Z, (float)-p.X), new((float)-q.Y, (float)q.Z, (float)-q.X, (float)q.W));
    }
}
