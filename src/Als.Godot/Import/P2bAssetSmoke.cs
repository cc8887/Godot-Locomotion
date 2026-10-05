using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Import.Compilation;

namespace GodotAls.Import;

public partial class P2bAssetSmoke : Node
{
    private const int MaximumSampledClips = 6;

    public override void _Ready()
    {
        try
        {
            RunSmoke();
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void RunSmoke()
    {
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var representative = definition.SkeletalMeshes
            .Select(mesh => new
            {
                Mesh = mesh,
                Skeleton = (uint)mesh.SkeletonId < (uint)definition.Skeletons.Length
                    ? definition.Skeletons[mesh.SkeletonId]
                    : null,
            })
            .Where(candidate => candidate.Skeleton is not null && candidate.Mesh.MaterialIds.Length > 0)
            .Select(candidate => new
            {
                candidate.Mesh,
                Skeleton = candidate.Skeleton!,
                CompatibleClips = definition.Animations
                    .Where(animation => animation.SkeletonId == candidate.Mesh.SkeletonId &&
                        animation.PlayLength > 0 && animation.SampledKeyCount > 0)
                    .OrderByDescending(animation => animation.Timeline.Length)
                    .ThenByDescending(animation => animation.SampledKeyCount)
                    .ThenBy(animation => animation.StableId, StringComparer.Ordinal)
                    .ToArray(),
            })
            .Where(candidate => candidate.CompatibleClips.Length > 0)
            .OrderByDescending(candidate => !candidate.Mesh.Overlay && !candidate.Mesh.Prop)
            .ThenByDescending(candidate => candidate.Skeleton.PhysicalBones.Length)
            .ThenByDescending(candidate => candidate.CompatibleClips.Length)
            .ThenByDescending(candidate => candidate.Mesh.MaterialSlotCount)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No materialized skeletal mesh has a compatible animation in the current export.");

        var targetScene = LoadScene(representative.Mesh.ResourcePath, representative.Mesh.Name);
        var sampledClips = representative.CompatibleClips.Take(MaximumSampledClips).ToArray();
        if (representative.Skeleton.PhysicalBones.Length == 0)
        {
            throw new InvalidOperationException($"Skeleton has no physical bones: {representative.Skeleton.ObjectPath}");
        }

        var materialBuilder = new AlsMaterialBuilder(definition);
        var poseChanges = 0;
        var materialChecks = 0;

        foreach (var clip in sampledClips)
        {
            var source = LoadScene(clip.ResourcePath, clip.Name);
            using var bound = AlsAnimationBinder.Bind(targetScene, source, clip, representative.Skeleton);
            AddChild(bound.Root);
            var poseBoneNames = Enumerable.Range(0, bound.Skeleton.GetBoneCount())
                .Select(index => bound.Skeleton.GetBoneName(index).ToString())
                .ToArray();
            var materialReport = materialBuilder.ApplyToRoot(
                bound.Root,
                representative.Mesh.StableId,
                representative.Mesh.MaterialIds);
            if (materialReport.AppliedCount == 0 || materialReport.UnresolvedCount != 0)
            {
                throw new InvalidOperationException(
                    $"Representative mesh material reconstruction failed for {clip.Name}: " +
                    $"applied={materialReport.AppliedCount} unresolved={materialReport.UnresolvedCount}.");
            }
            materialChecks += materialReport.AppliedCount;

            var initialPose = AlsPoseDigest.CapturePoses(bound.Skeleton, poseBoneNames);
            var poseChanged = false;
            var frameCount = Math.Min(60, Math.Max(1, (int)Math.Ceiling(clip.PlayLength * 30.0)));
            for (var frame = 1; frame <= frameCount; frame++)
            {
                bound.Player.Advance(1.0 / 30.0);
                var currentPose = AlsPoseDigest.CapturePoses(bound.Skeleton, poseBoneNames);
                poseChanged |= AlsPoseDigest.HasChanged(initialPose, currentPose);
            }
            poseChanges += poseChanged ? 1 : 0;
        }

        var targetRoot = targetScene.Instantiate();
        AddChild(targetRoot);
        try
        {
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException("Representative skeletal mesh scene has no Skeleton3D.");
            var importedRestPoseHash = AlsImportedResourceAuditor.ComputeTargetRestPoseHash(skeleton, representative.Skeleton);
            if (!string.Equals(
                    importedRestPoseHash,
                    representative.Skeleton.TargetPhysicalRestPoseHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Representative skeleton rest-pose hash mismatch: expected={representative.Skeleton.TargetPhysicalRestPoseHash} " +
                    $"actual={importedRestPoseHash}{System.Environment.NewLine}" +
                    AlsImportedResourceAuditor.DescribeRestPoseDifferences(skeleton, representative.Skeleton));
            }
            if (poseChanges == 0 || materialChecks == 0)
            {
                throw new InvalidOperationException("Compatible animation or material smoke did not exercise imported data.");
            }

            GD.Print(
                $"P2B_ASSET_SMOKE_OK bones={skeleton.GetBoneCount()} " +
                $"compatible_clips={representative.CompatibleClips.Length} sampled_clips={sampledClips.Length} " +
                $"pose_changes={poseChanges} material_checks={materialChecks}");
        }
        finally
        {
            targetRoot.Free();
        }
    }

    private static PackedScene LoadScene(string resourcePath, string assetName) =>
        ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(resourcePath))
        ?? throw new InvalidOperationException($"Unable to load representative scene: {assetName}");
}
