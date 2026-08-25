using Godot;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import;

public sealed record AlsImportedResourceAuditReport(
    int FileCount,
    int SkeletalMeshCount,
    int StaticMeshCount,
    int AnimationCount,
    int TextureCount);

public static class AlsImportedResourceAuditor
{
    private const double AnimationLengthTolerance = 1.0 / 30.0;

    public static AlsImportedResourceAuditReport Audit(
        AlsManifest manifest,
        AlsAnimationSetDefinition definition)
    {
        var errors = new List<string>();
        for (var fileIndex = 0; fileIndex < manifest.Files.Length; fileIndex++)
        {
            var file = manifest.Files[fileIndex];
            var resourcePath = ToResourcePath(file.RelativePath);
            if (!ResourceLoader.Exists(resourcePath))
            {
                errors.Add($"Missing imported resource: {resourcePath}");
                continue;
            }

            var resource = ResourceLoader.Load(resourcePath);
            var extension = Path.GetExtension(file.RelativePath);
            if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) && resource is not PackedScene)
            {
                errors.Add($"FBX did not load as PackedScene: {resourcePath}");
            }
            else if ((string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase)) &&
                     resource is not Texture2D)
            {
                errors.Add($"Texture did not load as Texture2D: {resourcePath}");
            }

            if ((fileIndex + 1) % 20 == 0 || fileIndex + 1 == manifest.Files.Length)
            {
                GD.Print($"P2B_IMPORT_PROGRESS resources={fileIndex + 1}/{manifest.Files.Length}");
            }
        }

        AuditSkeletalMeshes(definition, errors);
        AuditStaticMeshes(definition, errors);
        AuditAnimations(definition, errors);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "Imported ALS resource audit failed:" + System.Environment.NewLine +
                string.Join(System.Environment.NewLine, errors.Take(30)));
        }

        return new AlsImportedResourceAuditReport(
            manifest.Files.Length,
            definition.SkeletalMeshes.Length,
            definition.StaticMeshes.Length,
            definition.Animations.Length,
            definition.Textures.Length);
    }

    public static string ToResourcePath(string relativePath) =>
        $"res://assets/generated/als_v4/{relativePath.Replace('\\', '/')}";

    private static void AuditSkeletalMeshes(AlsAnimationSetDefinition definition, List<string> errors)
    {
        foreach (var mesh in definition.SkeletalMeshes)
        {
            var resourcePath = ToResourcePath(mesh.ResourcePath);
            InspectScene(resourcePath, root =>
            {
                var skeleton = FindFirst<Skeleton3D>(root);
                if (skeleton is null)
                {
                    errors.Add($"Skeletal mesh has no Skeleton3D: {resourcePath}");
                }
                else
                {
                    CompareSkeleton(resourcePath, skeleton, definition.Skeletons[mesh.SkeletonId], false, errors);
                }

                if (FindFirst<MeshInstance3D>(root) is null)
                {
                    errors.Add($"Skeletal mesh has no MeshInstance3D: {resourcePath}");
                }
            }, errors);
        }
    }

    private static void AuditStaticMeshes(AlsAnimationSetDefinition definition, List<string> errors)
    {
        foreach (var mesh in definition.StaticMeshes)
        {
            var resourcePath = ToResourcePath(mesh.ResourcePath);
            InspectScene(resourcePath, root =>
            {
                if (FindFirst<MeshInstance3D>(root) is null)
                {
                    errors.Add($"Static mesh has no MeshInstance3D: {resourcePath}");
                }
            }, errors);
        }
    }

    private static void AuditAnimations(AlsAnimationSetDefinition definition, List<string> errors)
    {
        for (var clipIndex = 0; clipIndex < definition.Animations.Length; clipIndex++)
        {
            var clip = definition.Animations[clipIndex];
            var resourcePath = ToResourcePath(clip.ResourcePath);
            InspectScene(resourcePath, root =>
            {
                var skeleton = FindFirst<Skeleton3D>(root);
                var player = FindFirst<AnimationPlayer>(root);
                if (skeleton is null)
                {
                    errors.Add($"Animation has no Skeleton3D: {resourcePath}");
                }
                else
                {
                    CompareSkeleton(resourcePath, skeleton, definition.Skeletons[clip.SkeletonId], true, errors);
                }

                if (player is null)
                {
                    errors.Add($"Animation has no AnimationPlayer: {resourcePath}");
                    return;
                }

                var imported = player.GetAnimationList()
                    .Select(name => player.GetAnimation(name))
                    .FirstOrDefault(animation => animation is not null && animation.GetTrackCount() > 0);
                if (imported is null)
                {
                    errors.Add($"Animation has no non-empty clip: {resourcePath}");
                    return;
                }

                if (Math.Abs(imported.Length - clip.PlayLength) > AnimationLengthTolerance + 1e-6)
                {
                    errors.Add($"Animation length mismatch: {resourcePath} expected={clip.PlayLength} actual={imported.Length}");
                }
            }, errors);

            if ((clipIndex + 1) % 20 == 0 || clipIndex + 1 == definition.Animations.Length)
            {
                GD.Print($"P2B_IMPORT_PROGRESS animations={clipIndex + 1}/{definition.Animations.Length}");
            }
        }
    }

    private static void InspectScene(string resourcePath, Action<Node> inspect, List<string> errors)
    {
        var packedScene = ResourceLoader.Load<PackedScene>(resourcePath);
        if (packedScene is null)
        {
            errors.Add($"Scene could not be loaded: {resourcePath}");
            return;
        }

        var root = packedScene.Instantiate();
        try
        {
            inspect(root);
        }
        finally
        {
            root.Free();
        }
    }

    private static void CompareSkeleton(
        string resourcePath,
        Skeleton3D skeleton,
        AlsSkeletonDefinition expected,
        bool requireCompleteSkeleton,
        List<string> errors)
    {
        if (requireCompleteSkeleton && skeleton.GetBoneCount() != expected.PhysicalBones.Length)
        {
            errors.Add($"Skeleton bone count mismatch: {resourcePath} expected={expected.PhysicalBones.Length} actual={skeleton.GetBoneCount()}");
            return;
        }

        var expectedIndex = 0;
        for (var actualIndex = 0; actualIndex < skeleton.GetBoneCount(); actualIndex++)
        {
            var actualName = skeleton.GetBoneName(actualIndex).ToString();
            while (expectedIndex < expected.PhysicalBones.Length &&
                   !string.Equals(actualName, expected.PhysicalBones[expectedIndex].Name, StringComparison.OrdinalIgnoreCase))
            {
                expectedIndex++;
            }

            if (expectedIndex == expected.PhysicalBones.Length)
            {
                errors.Add($"Skeleton has an unknown or out-of-order bone: {resourcePath} index={actualIndex} actual={actualName}");
                return;
            }

            expectedIndex++;
        }
    }

    public static T? FindFirst<T>(Node root) where T : Node
    {
        if (root is T match)
        {
            return match;
        }

        foreach (var child in root.GetChildren())
        {
            var descendant = FindFirst<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
