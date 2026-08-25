using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Import.Manifest;
using GodotAls.Import.Metadata;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static partial class AlsSkeletonCompiler
{
    public static AlsSkeletonDefinition Compile(AlsManifestAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        AlsSkeletonMetadata metadata;
        try
        {
            metadata = AlsSkeletonMetadata.Read(asset.Metadata);
        }
        catch (JsonException exception)
        {
            throw new AlsCompilationException([
                new AlsValidationIssue("ALSRIG001", asset.Id, "$.metadata", exception.Message),
            ]);
        }

        var issues = new List<AlsValidationIssue>();
        ValidateMetadata(asset.Id, metadata, issues);
        if (issues.Count != 0)
        {
            throw new AlsCompilationException(issues);
        }

        var virtualNames = metadata.VirtualBones
            .Select(value => value.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var logicalToPhysical = Enumerable.Repeat(-1, metadata.Bones.Length).ToArray();
        var physicalToLogical = new List<int>();
        for (var logicalId = 0; logicalId < metadata.Bones.Length; logicalId++)
        {
            if (!virtualNames.Contains(metadata.Bones[logicalId].Name))
            {
                logicalToPhysical[logicalId] = physicalToLogical.Count;
                physicalToLogical.Add(logicalId);
            }
        }

        var logicalBones = new AlsBoneDefinition[metadata.Bones.Length];
        for (var logicalId = 0; logicalId < metadata.Bones.Length; logicalId++)
        {
            var source = metadata.Bones[logicalId];
            var physicalId = logicalToPhysical[logicalId];
            var parentPhysicalId = source.ParentIndex < 0 ? -1 : logicalToPhysical[source.ParentIndex];
            if (physicalId >= 0 && source.ParentIndex >= 0 && parentPhysicalId < 0)
            {
                issues.Add(new AlsValidationIssue("ALSRIG006", asset.Id, $"$.metadata.bones[{logicalId}].parentIndex",
                    "Physical bone cannot have a virtual parent.", null, source.ParentIndex.ToString()));
            }

            logicalBones[logicalId] = new AlsBoneDefinition(
                logicalId,
                physicalId,
                source.Name,
                source.ParentIndex,
                parentPhysicalId,
                AlsCoordinateConverter.PositionCentimetersToMeters(ToVector3(source.Translation)),
                AlsCoordinateConverter.Rotation(ToQuaternion(source.Rotation)),
                AlsCoordinateConverter.Scale(ToVector3(source.Scale)));
        }

        var nameToLogical = logicalBones.ToDictionary(bone => bone.Name, bone => bone.LogicalId, StringComparer.OrdinalIgnoreCase);
        var virtualBones = CompileVirtualBones(asset.Id, metadata.VirtualBones, nameToLogical, issues);
        var sockets = CompileSockets(asset.Id, metadata.Sockets, nameToLogical, issues);
        if (issues.Count != 0)
        {
            throw new AlsCompilationException(issues);
        }

        var physicalBones = physicalToLogical.Select(logicalId => logicalBones[logicalId]).ToArray();
        var required = new AlsRequiredBoneIds(
            Find(nameToLogical, "root"),
            Find(nameToLogical, "pelvis"),
            Find(nameToLogical, "foot_l"),
            Find(nameToLogical, "foot_r"));
        if ((required.Pelvis >= 0 || required.FootLeft >= 0 || required.FootRight >= 0) &&
            (required.Root < 0 || required.Pelvis < 0 || required.FootLeft < 0 || required.FootRight < 0))
        {
            throw new AlsCompilationException([
                new AlsValidationIssue(
                    "ALSRIG010",
                    asset.Id,
                    "$.metadata.bones",
                    "Humanoid skeleton must contain root, pelvis, foot_l, and foot_r.")
            ]);
        }
        return new AlsSkeletonDefinition(
            asset.Id,
            asset.ObjectPath,
            metadata.RestPoseHash,
            AlsCanonicalPoseHash.Create(physicalBones),
            logicalBones,
            physicalBones,
            virtualBones,
            sockets,
            logicalToPhysical,
            physicalToLogical.ToArray(),
            required);
    }

    private static void ValidateMetadata(string assetId, AlsSkeletonMetadata metadata, List<AlsValidationIssue> issues)
    {
        if (metadata.Bones is null || metadata.VirtualBones is null || metadata.Sockets is null)
        {
            issues.Add(new AlsValidationIssue("ALSRIG002", assetId, "$.metadata", "Rig metadata arrays are required."));
            return;
        }

        if (metadata.BoneCount != metadata.Bones.Length)
        {
            issues.Add(new AlsValidationIssue("ALSRIG003", assetId, "$.metadata.boneCount", "Bone count does not match bones[].",
                metadata.Bones.Length.ToString(), metadata.BoneCount.ToString()));
        }

        if (!SourceHashRegex().IsMatch(metadata.RestPoseHash))
        {
            issues.Add(new AlsValidationIssue("ALSRIG004", assetId, "$.metadata.restPoseHash", "Source rest-pose hash is invalid."));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < metadata.Bones.Length; index++)
        {
            var bone = metadata.Bones[index];
            if (string.IsNullOrWhiteSpace(bone.Name) || !names.Add(bone.Name))
            {
                issues.Add(new AlsValidationIssue("ALSRIG005", assetId, $"$.metadata.bones[{index}].name", "Bone name is empty or duplicated."));
            }

            if (bone.ParentIndex < -1 || bone.ParentIndex >= index)
            {
                issues.Add(new AlsValidationIssue("ALSRIG006", assetId, $"$.metadata.bones[{index}].parentIndex", "Bone parent must precede its child."));
            }

            if (bone.Translation?.Length != 3 || bone.Rotation?.Length != 4 || bone.Scale?.Length != 3)
            {
                issues.Add(new AlsValidationIssue("ALSRIG007", assetId, $"$.metadata.bones[{index}]", "Bone transform has an invalid component count."));
            }
        }
    }

    private static AlsVirtualBoneDefinition[] CompileVirtualBones(
        string assetId,
        AlsVirtualBoneMetadata[] metadata,
        Dictionary<string, int> names,
        List<AlsValidationIssue> issues)
    {
        var result = new AlsVirtualBoneDefinition[metadata.Length];
        for (var index = 0; index < metadata.Length; index++)
        {
            var value = metadata[index];
            var logicalId = Resolve(value.Name, $"$.metadata.virtualBones[{index}].name");
            var sourceId = Resolve(value.Source, $"$.metadata.virtualBones[{index}].source");
            var targetId = Resolve(value.Target, $"$.metadata.virtualBones[{index}].target");
            result[index] = new AlsVirtualBoneDefinition(logicalId, sourceId, targetId);

            int Resolve(string name, string path)
            {
                if (names.TryGetValue(name, out var id))
                {
                    return id;
                }

                issues.Add(new AlsValidationIssue("ALSRIG008", assetId, path, "Virtual-bone reference does not resolve.", null, name));
                return -1;
            }
        }

        return result;
    }

    private static AlsSocketDefinition[] CompileSockets(
        string assetId,
        AlsSocketMetadata[] metadata,
        Dictionary<string, int> names,
        List<AlsValidationIssue> issues)
    {
        var result = new AlsSocketDefinition[metadata.Length];
        for (var index = 0; index < metadata.Length; index++)
        {
            var value = metadata[index];
            if (!names.TryGetValue(value.Bone, out var boneId))
            {
                issues.Add(new AlsValidationIssue("ALSRIG009", assetId, $"$.metadata.sockets[{index}].bone",
                    "Socket bone does not resolve.", null, value.Bone));
                boneId = -1;
            }

            result[index] = new AlsSocketDefinition(
                value.Name,
                boneId,
                AlsCoordinateConverter.PositionCentimetersToMeters(ToVector3(value.Translation)),
                AlsCoordinateConverter.Rotation(ToQuaternion(value.Rotation)),
                AlsCoordinateConverter.Scale(ToVector3(value.Scale)));
        }

        return result;
    }

    private static int Find(Dictionary<string, int> names, string name) => names.GetValueOrDefault(name, -1);

    private static Vector3 ToVector3(float[] values) => new(values[0], values[1], values[2]);

    private static Quaternion ToQuaternion(float[] values) => new(values[0], values[1], values[2], values[3]);

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceHashRegex();
}
