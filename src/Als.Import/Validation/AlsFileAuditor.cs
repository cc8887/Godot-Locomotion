using System.Security.Cryptography;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Validation;

public static class AlsFileAuditor
{
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx",
        ".png",
        ".tga",
    };

    public static IReadOnlyList<AlsValidationIssue> Validate(string root, AlsManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<AlsValidationIssue>();
        var fullRoot = Path.GetFullPath(root);
        var declared = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < manifest.Files.Length; index++)
        {
            var file = manifest.Files[index];
            var fieldPath = $"$.files[{index}]";
            if (!TryResolve(fullRoot, file.RelativePath, out var fullPath))
            {
                Add(issues, "ALSFILE002", fieldPath + ".relativePath", "File path is not a safe relative path.", file.RelativePath);
                continue;
            }

            if (!declared.Add(file.RelativePath))
            {
                Add(issues, "ALSFILE003", fieldPath + ".relativePath", "Duplicate file row.", file.RelativePath);
                continue;
            }

            if (!File.Exists(fullPath))
            {
                Add(issues, "ALSFILE004", fieldPath + ".relativePath", "Declared output file is missing.", file.RelativePath);
                continue;
            }

            var info = new FileInfo(fullPath);
            if (info.Length == 0)
            {
                Add(issues, "ALSFILE005", fieldPath + ".size", "Declared output file is empty.", file.RelativePath);
                continue;
            }

            if (info.Length != file.Size)
            {
                issues.Add(new AlsValidationIssue("ALSFILE006", null, fieldPath + ".size", "File size does not match manifest.",
                    file.Size.ToString(), info.Length.ToString()));
            }

            using var stream = File.OpenRead(fullPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
            {
                issues.Add(new AlsValidationIssue("ALSFILE007", null, fieldPath + ".sha256", "File hash does not match manifest.",
                    file.Sha256, actualHash));
            }
        }

        ValidateAssetOutputs(manifest, declared, issues);
        ValidateUndeclaredFiles(fullRoot, declared, issues);
        return issues;
    }

    private static void ValidateAssetOutputs(
        AlsManifest manifest,
        HashSet<string> declared,
        List<AlsValidationIssue> issues)
    {
        foreach (var (section, assets) in GetSections(manifest))
        {
            for (var index = 0; index < assets.Length; index++)
            {
                var outputPath = assets[index].OutputPath;
                if (outputPath is not null && !declared.Contains(outputPath))
                {
                    issues.Add(new AlsValidationIssue("ALSFILE009", assets[index].Id,
                        $"$.{section}[{index}].outputPath", "Asset output is absent from files[].", outputPath, null));
                }
            }
        }
    }

    private static void ValidateUndeclaredFiles(
        string fullRoot,
        HashSet<string> declared,
        List<AlsValidationIssue> issues)
    {
        foreach (var relativeDirectory in new[] { "animations", "meshes", "textures" })
        {
            var directory = Path.Combine(fullRoot, relativeDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!BinaryExtensions.Contains(Path.GetExtension(path)))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(fullRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                if (!declared.Contains(relativePath))
                {
                    Add(issues, "ALSFILE008", "$.files", "Binary output is not declared by the manifest.", relativePath);
                }
            }
        }
    }

    private static bool TryResolve(string fullRoot, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Section, AlsManifestAsset[] Assets)[] GetSections(AlsManifest manifest) =>
    [
        ("skeletons", manifest.Skeletons),
        ("skeletalMeshes", manifest.SkeletalMeshes),
        ("staticMeshes", manifest.StaticMeshes),
        ("animations", manifest.Animations),
        ("montages", manifest.Montages),
        ("blendSpaces", manifest.BlendSpaces),
        ("aimOffsets", manifest.AimOffsets),
        ("materials", manifest.Materials),
        ("textures", manifest.Textures),
        ("physicsAssets", manifest.PhysicsAssets),
        ("curves", manifest.Curves),
        ("configAssets", manifest.ConfigAssets),
    ];

    private static void Add(
        List<AlsValidationIssue> issues,
        string code,
        string fieldPath,
        string message,
        string actual) =>
        issues.Add(new AlsValidationIssue(code, null, fieldPath, message, null, actual));
}
