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
    private const string MannequinAssetId = "86d98d8177feb473c8a5f406c5b42f8c2a2f7b07";

    private static readonly System.Numerics.Matrix4x4 ImportedBoneToTarget = new(
        0f, 0f, -1f, 0f,
        -1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 0f, 1f);

    private static readonly System.Numerics.Matrix4x4 TargetToImportedBone =
        CreateImportedBoneInverse();

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

    public static string ComputeTargetRestPoseHash(
        Skeleton3D skeleton,
        AlsSkeletonDefinition expected)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(expected);
        var actual = ReadTargetRestPose(skeleton);
        if (actual.Length != expected.PhysicalBones.Length)
        {
            return AlsCanonicalPoseHash.Create(actual);
        }

        var normalized = new AlsBoneDefinition[actual.Length];
        for (var index = 0; index < actual.Length; index++)
        {
            var source = actual[index];
            var contract = expected.PhysicalBones[index];
            var rotation = source.Rotation;
            if (System.Numerics.Quaternion.Dot(rotation, contract.Rotation) < 0f)
            {
                rotation = new System.Numerics.Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
            }

            normalized[index] = source with
            {
                Name = string.Equals(source.Name, contract.Name, StringComparison.OrdinalIgnoreCase)
                    ? contract.Name
                    : source.Name,
                Translation = NormalizeVector(source.Translation, contract.Translation),
                Rotation = NormalizeQuaternion(rotation, contract.Rotation),
                Scale = NormalizeVector(source.Scale, contract.Scale),
            };
        }

        return AlsCanonicalPoseHash.Create(normalized);

        static System.Numerics.Vector3 NormalizeVector(
            System.Numerics.Vector3 actual,
            System.Numerics.Vector3 expected) => new(
                NormalizeComponent(actual.X, expected.X),
                NormalizeComponent(actual.Y, expected.Y),
                NormalizeComponent(actual.Z, expected.Z));

        static System.Numerics.Quaternion NormalizeQuaternion(
            System.Numerics.Quaternion actual,
            System.Numerics.Quaternion expected) => new(
                NormalizeComponent(actual.X, expected.X),
                NormalizeComponent(actual.Y, expected.Y),
                NormalizeComponent(actual.Z, expected.Z),
                NormalizeComponent(actual.W, expected.W));

        static float NormalizeComponent(float actual, float expected) =>
            Math.Abs(actual - expected) <= 1.1e-5f ? expected : actual;
    }

    public static string DescribeRestPoseDifferences(
        Skeleton3D skeleton,
        AlsSkeletonDefinition expected)
    {
        var actual = ReadTargetRestPose(skeleton);
        var lines = new List<string>();
        for (var index = 0; index < Math.Min(actual.Length, expected.PhysicalBones.Length); index++)
        {
            var left = expected.PhysicalBones[index];
            var right = actual[index];
            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
                left.ParentPhysicalId != right.ParentPhysicalId ||
                !QuantizedEqual(left.Translation.X, right.Translation.X) ||
                !QuantizedEqual(left.Translation.Y, right.Translation.Y) ||
                !QuantizedEqual(left.Translation.Z, right.Translation.Z) ||
                !QuantizedEqual(left.Rotation.X, right.Rotation.X) ||
                !QuantizedEqual(left.Rotation.Y, right.Rotation.Y) ||
                !QuantizedEqual(left.Rotation.Z, right.Rotation.Z) ||
                !QuantizedEqual(left.Rotation.W, right.Rotation.W) ||
                !QuantizedEqual(left.Scale.X, right.Scale.X) ||
                !QuantizedEqual(left.Scale.Y, right.Scale.Y) ||
                !QuantizedEqual(left.Scale.Z, right.Scale.Z))
            {
                lines.Add(
                    $"bone={left.Name}/{right.Name} parent={left.ParentPhysicalId}/{right.ParentPhysicalId} " +
                    $"expectedT={left.Translation} actualT={right.Translation} " +
                    $"expectedR={left.Rotation} actualR={right.Rotation} " +
                    $"expectedS={left.Scale} actualS={right.Scale}");
                if (lines.Count == 5)
                {
                    break;
                }
            }
        }

        return lines.Count == 0 ? "No transform deltas above tolerance." : string.Join(System.Environment.NewLine, lines);

        static bool QuantizedEqual(float left, float right) =>
            MathF.Round(left, 5, MidpointRounding.ToEven) == MathF.Round(right, 5, MidpointRounding.ToEven);
    }

    private static AlsBoneDefinition[] ReadTargetRestPose(Skeleton3D skeleton)
    {
        var bones = new AlsBoneDefinition[skeleton.GetBoneCount()];
        for (var index = 0; index < bones.Length; index++)
        {
            var rest = skeleton.GetBoneRest(index);
            var importedRotation = rest.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
            var rotationMatrix = System.Numerics.Matrix4x4.CreateFromQuaternion(
                new System.Numerics.Quaternion(
                    importedRotation.X,
                    importedRotation.Y,
                    importedRotation.Z,
                    importedRotation.W));
            var rotation = System.Numerics.Quaternion.Normalize(
                System.Numerics.Quaternion.CreateFromRotationMatrix(
                    TargetToImportedBone * rotationMatrix * ImportedBoneToTarget));
            if (rotation.W < 0f)
            {
                rotation = new System.Numerics.Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
            }

            var importedScale = rest.Basis.Scale;

            bones[index] = new AlsBoneDefinition(
                index,
                index,
                skeleton.GetBoneName(index).ToString(),
                skeleton.GetBoneParent(index),
                skeleton.GetBoneParent(index),
                new System.Numerics.Vector3(-rest.Origin.Y, rest.Origin.Z, -rest.Origin.X),
                rotation,
                new System.Numerics.Vector3(importedScale.Y, importedScale.Z, importedScale.X));
        }

        return bones;
    }

    private static System.Numerics.Matrix4x4 CreateImportedBoneInverse()
    {
        if (!System.Numerics.Matrix4x4.Invert(ImportedBoneToTarget, out var inverse))
        {
            throw new InvalidOperationException("Godot FBX bone conversion basis is not invertible.");
        }

        return inverse;
    }

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
                    CompareSkeleton(
                        resourcePath,
                        skeleton,
                        definition.Skeletons[mesh.SkeletonId],
                        string.Equals(mesh.StableId, MannequinAssetId, StringComparison.Ordinal),
                        errors);
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
                    CompareSkeleton(resourcePath, skeleton, definition.Skeletons[clip.SkeletonId], false, errors);
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

        if (requireCompleteSkeleton)
        {
            var actualHash = ComputeTargetRestPoseHash(skeleton, expected);
            if (!string.Equals(actualHash, expected.TargetPhysicalRestPoseHash, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Skeleton rest-pose hash mismatch: {resourcePath} " +
                    $"expected={expected.TargetPhysicalRestPoseHash} actual={actualHash} " +
                    DescribeRestPoseDifferences(skeleton, expected));
            }
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
