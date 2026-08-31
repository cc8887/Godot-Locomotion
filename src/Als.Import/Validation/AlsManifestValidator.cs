using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Import.Manifest;
using GodotAls.Import.Metadata;

namespace GodotAls.Import.Validation;

public static partial class AlsManifestValidator
{
    public static IReadOnlyList<AlsValidationIssue> Validate(
        AlsManifest manifest,
        AlsManifestValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        options ??= new AlsManifestValidationOptions();
        var issues = new List<AlsValidationIssue>();
        var sections = GetSections(manifest);

        if (manifest.SchemaVersion != options.SupportedSchemaVersion)
        {
            Add(issues, "ALSMANIFEST001", null, "$.schemaVersion", "Unsupported manifest schema.",
                options.SupportedSchemaVersion.ToString(), manifest.SchemaVersion.ToString());
        }

        if (!string.Equals(manifest.ExporterVersion, options.SupportedExporterVersion, StringComparison.Ordinal))
        {
            Add(issues, "ALSMANIFEST013", null, "$.exporterVersion", "Unsupported manifest exporter version.",
                options.SupportedExporterVersion, manifest.ExporterVersion);
        }

        if (options.RequireCompleteAudit && !string.Equals(manifest.AuditSummary.Status, "complete", StringComparison.Ordinal))
        {
            Add(issues, "ALSMANIFEST002", null, "$.auditSummary.status", "Manifest audit is not complete.",
                "complete", manifest.AuditSummary.Status);
        }

        var assetCount = sections.Sum(section => section.Assets.Length);
        if (manifest.AuditSummary.AssetCount != assetCount)
        {
            Add(issues, "ALSMANIFEST003", null, "$.auditSummary.assetCount", "Asset count does not match manifest contents.",
                assetCount.ToString(), manifest.AuditSummary.AssetCount.ToString());
        }

        if (manifest.AuditSummary.FileCount != manifest.Files.Length)
        {
            Add(issues, "ALSMANIFEST004", null, "$.auditSummary.fileCount", "File count does not match manifest contents.",
                manifest.Files.Length.ToString(), manifest.AuditSummary.FileCount.ToString());
        }

        if (manifest.AuditSummary.ErrorCount != 0)
        {
            Add(issues, "ALSMANIFEST005", null, "$.auditSummary.errorCount", "Completed manifest contains exporter errors.",
                "0", manifest.AuditSummary.ErrorCount.ToString());
        }

        ValidateCoordinateSystem(manifest, issues);
        var ids = new Dictionary<string, AssetLocation>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            ValidateSection(section, ids, issues);
        }

        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var markerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            for (var index = 0; index < section.Assets.Length; index++)
            {
                ValidateAssetReferences(section.Name, index, section.Assets[index], ids, issues);
                ValidateTypedMetadata(section.Name, index, section.Assets[index], eventIds, markerIds, issues);
            }
        }

        return issues;
    }

    private static void ValidateCoordinateSystem(AlsManifest manifest, List<AlsValidationIssue> issues)
    {
        var coordinate = manifest.CoordinateSystem;
        if (!string.Equals(coordinate.SourceHandedness, "left", StringComparison.Ordinal) ||
            !string.Equals(coordinate.SourceUpAxis, "Z", StringComparison.Ordinal) ||
            !string.Equals(coordinate.TargetHandedness, "right", StringComparison.Ordinal) ||
            !string.Equals(coordinate.TargetUpAxis, "Y", StringComparison.Ordinal))
        {
            Add(issues, "ALSMANIFEST006", null, "$.coordinateSystem", "Unsupported coordinate-system contract.");
        }
        if (Math.Abs(manifest.UnitScale - 0.01) > 1e-12)
        {
            Add(issues, "ALSMANIFEST006", null, "$.unitScale", "Unit scale must be exactly 0.01 meters per centimeter.",
                "0.01", manifest.UnitScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void ValidateSection(
        AssetSection section,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        for (var index = 0; index < section.Assets.Length; index++)
        {
            var asset = section.Assets[index];
            var path = $"$.{section.Name}[{index}]";
            if (index > 0 && string.CompareOrdinal(section.Assets[index - 1].Id, asset.Id) > 0)
            {
                Add(issues, "ALSMANIFEST007", asset.Id, $"$.{section.Name}", "Assets must be sorted by stable ID.");
            }

            if (!ids.TryAdd(asset.Id, new AssetLocation(section.Name, index)))
            {
                Add(issues, "ALSMANIFEST008", asset.Id, $"{path}.id", "Duplicate stable asset ID.");
            }

            if (!StableIdRegex().IsMatch(asset.Id))
            {
                Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.id", "Stable ID must be 40 lowercase hexadecimal characters.");
            }
            else
            {
                try
                {
                    var expected = AlsStableAssetId.Create(asset.ObjectPath);
                    if (!string.Equals(expected, asset.Id, StringComparison.Ordinal))
                    {
                        Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.id", "Stable ID does not match object path.", expected, asset.Id);
                    }
                }
                catch (ArgumentException)
                {
                    Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.objectPath", "Object path is not canonical.");
                }
            }

            if (!asset.PackagePath.StartsWith("/Game/", StringComparison.Ordinal) || asset.PackagePath.Contains('\\'))
            {
                Add(issues, "ALSMANIFEST009", asset.Id, $"{path}.packagePath", "Package path is not canonical.");
            }

            if (asset.OutputPath is not null && !IsSafeRelativePath(asset.OutputPath))
            {
                Add(issues, "ALSMANIFEST011", asset.Id, $"{path}.outputPath", "Output path is not a safe canonical relative path.", null, asset.OutputPath);
            }
        }
    }

    private static void ValidateAssetReferences(
        string section,
        int index,
        AlsManifestAsset asset,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        var path = $"$.{section}[{index}]";
        for (var dependencyIndex = 0; dependencyIndex < asset.Dependencies.Length; dependencyIndex++)
        {
            var dependency = asset.Dependencies[dependencyIndex];
            if (!ids.ContainsKey(dependency))
            {
                Add(issues, "ALSMANIFEST010", asset.Id, $"{path}.dependencies[{dependencyIndex}]",
                    "Dependency does not resolve to a manifest asset.", null, dependency);
            }
        }

        ValidateMetadataReferences(asset.Metadata, $"{path}.metadata", asset.Id, ids, issues);
    }

    private static void ValidateMetadataReferences(
        JsonElement element,
        string path,
        string assetId,
        Dictionary<string, AssetLocation> ids,
        List<AlsValidationIssue> issues)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                if ((property.Name == "id" || property.Name.EndsWith("Id", StringComparison.Ordinal)) &&
                    property.Name is not ("stableEventId" or "stableMarkerId") &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var reference = property.Value.GetString();
                    if (!string.IsNullOrEmpty(reference) && !ids.ContainsKey(reference))
                    {
                        Add(issues, "ALSMANIFEST012", assetId, propertyPath,
                            "Metadata reference does not resolve to a manifest asset.", null, reference);
                    }
                }
                else
                {
                    ValidateMetadataReferences(property.Value, propertyPath, assetId, ids, issues);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                ValidateMetadataReferences(item, $"{path}[{index}]", assetId, ids, issues);
                index++;
            }
        }
    }

    private static void ValidateTypedMetadata(
        string section,
        int index,
        AlsManifestAsset asset,
        HashSet<string> eventIds,
        HashSet<string> markerIds,
        List<AlsValidationIssue> issues)
    {
        if (section == "animations")
        {
            var path = $"$.animations[{index}].metadata";
            var metadata = AlsAnimationMetadata.Read(asset.Metadata);
            ValidateTimeline(metadata.Timeline, metadata.PlayLength, path, asset.Id, eventIds, issues);
            ValidateMarkers(metadata.SyncMarkers, metadata.PlayLength, path, asset.Id, markerIds, issues);
        }
        else if (section == "montages")
        {
            var path = $"$.montages[{index}].metadata";
            var metadata = AlsMontageMetadata.Read(asset.Metadata);
            ValidateTimeline(metadata.Timeline, metadata.PlayLength, path, asset.Id, eventIds, issues);
            ValidateMontageSections(metadata, path, asset.Id, issues);
        }
    }

    private static void ValidateTimeline(
        AlsTimelineEventMetadata[] timeline,
        double sourceLength,
        string metadataPath,
        string assetId,
        HashSet<string> eventIds,
        List<AlsValidationIssue> issues)
    {
        var sourceIndices = new HashSet<int>();
        for (var index = 0; index < timeline.Length; index++)
        {
            var value = timeline[index];
            var path = $"{metadataPath}.timeline[{index}]";
            if (!StableIdRegex().IsMatch(value.StableEventId) || !eventIds.Add(value.StableEventId))
            {
                Add(issues, "ALSMANIFEST014", assetId, $"{path}.stableEventId",
                    "Timeline stable event ID must be a unique lowercase SHA-1.");
            }
            if (string.IsNullOrWhiteSpace(value.SourceClassPath))
            {
                Add(issues, "ALSMANIFEST015", assetId, $"{path}.sourceClassPath", "Timeline source class path is required.");
            }
            if (string.IsNullOrWhiteSpace(value.DisplayName))
            {
                Add(issues, "ALSMANIFEST015", assetId, $"{path}.displayName", "Timeline display name is required.");
            }
            if (!double.IsFinite(value.TimeSeconds) || value.TimeSeconds < 0.0 ||
                !double.IsFinite(sourceLength) || value.TimeSeconds > sourceLength)
            {
                Add(issues, "ALSMANIFEST016", assetId, $"{path}.timeSeconds", "Timeline time is outside the source length.");
            }
            if (!double.IsFinite(value.DurationSeconds) || value.DurationSeconds < 0.0 ||
                !double.IsFinite(value.TimeSeconds + value.DurationSeconds) ||
                value.TimeSeconds + value.DurationSeconds > sourceLength)
            {
                Add(issues, "ALSMANIFEST016", assetId, $"{path}.durationSeconds", "Timeline state end is outside the source length.");
            }
            if (!double.IsFinite(value.TriggerWeightThreshold) ||
                value.TriggerWeightThreshold < 0.0 || value.TriggerWeightThreshold > 1.0)
            {
                Add(issues, "ALSMANIFEST017", assetId, $"{path}.triggerWeightThreshold",
                    "Timeline trigger threshold must be in [0, 1].");
            }
            if (value.TickMode is not ("Queued" or "BranchingPoint"))
            {
                Add(issues, "ALSMANIFEST018", assetId, $"{path}.tickMode", "Timeline tick mode is unsupported.");
            }
            if (value.SourceIndex < 0 || !sourceIndices.Add(value.SourceIndex))
            {
                Add(issues, "ALSMANIFEST019", assetId, $"{path}.sourceIndex",
                    "Timeline source index must be nonnegative and unique within its source asset.");
            }
            if (value.TrackIndex < 0)
            {
                Add(issues, "ALSMANIFEST020", assetId, $"{path}.trackIndex", "Timeline track index must be nonnegative.");
            }

            switch (value.Payload)
            {
            case AlsEarlyBlendOutEventPayloadMetadata payload
                when !double.IsFinite(payload.BlendOutSeconds) || payload.BlendOutSeconds < 0.0:
                Add(issues, "ALSMANIFEST021", assetId, $"{path}.payload.blendOutSeconds",
                    "EarlyBlendOut blend duration must be finite and nonnegative.");
                break;
            case AlsRootMotionScaleEventPayloadMetadata payload
                when !double.IsFinite(payload.TranslationScale) || payload.TranslationScale < 0.0:
                Add(issues, "ALSMANIFEST021", assetId, $"{path}.payload.translationScale",
                    "RootMotionScale translation scale must be finite and nonnegative.");
                break;
            }
        }
    }

    private static void ValidateMarkers(
        AlsAnimationSyncMarkerMetadata[] markers,
        double sourceLength,
        string metadataPath,
        string assetId,
        HashSet<string> markerIds,
        List<AlsValidationIssue> issues)
    {
        var sourceIndices = new HashSet<int>();
        for (var index = 0; index < markers.Length; index++)
        {
            var value = markers[index];
            var path = $"{metadataPath}.syncMarkers[{index}]";
            if (!StableIdRegex().IsMatch(value.StableMarkerId) || !markerIds.Add(value.StableMarkerId))
            {
                Add(issues, "ALSMANIFEST022", assetId, $"{path}.stableMarkerId",
                    "Sync marker stable ID must be a unique lowercase SHA-1.");
            }
            if (string.IsNullOrWhiteSpace(value.Name))
            {
                Add(issues, "ALSMANIFEST023", assetId, $"{path}.name", "Sync marker name is required.");
            }
            if (!double.IsFinite(value.TimeSeconds) || value.TimeSeconds < 0.0 || value.TimeSeconds > sourceLength)
            {
                Add(issues, "ALSMANIFEST023", assetId, $"{path}.timeSeconds", "Sync marker time is outside the source length.");
            }
            if (value.SourceIndex < 0 || !sourceIndices.Add(value.SourceIndex))
            {
                Add(issues, "ALSMANIFEST023", assetId, $"{path}.sourceIndex",
                    "Sync marker source index must be nonnegative and unique within its source asset.");
            }
            if (value.TrackIndex < 0)
            {
                Add(issues, "ALSMANIFEST023", assetId, $"{path}.trackIndex", "Sync marker track index must be nonnegative.");
            }
        }
    }

    private static void ValidateMontageSections(
        AlsMontageMetadata metadata,
        string metadataPath,
        string assetId,
        List<AlsValidationIssue> issues)
    {
        var names = metadata.Sections.Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        double? previousStart = null;
        for (var index = 0; index < metadata.Sections.Length; index++)
        {
            var value = metadata.Sections[index];
            var path = $"{metadataPath}.sections[{index}]";
            if (string.IsNullOrWhiteSpace(value.Name) || !seenNames.Add(value.Name))
            {
                Add(issues, "ALSMANIFEST024", assetId, $"{path}.name", "Montage section name must be nonempty and unique.");
            }
            if (!float.IsFinite(value.StartTime) || value.StartTime < 0f || value.StartTime > metadata.PlayLength ||
                previousStart is not null && value.StartTime <= previousStart.Value)
            {
                Add(issues, "ALSMANIFEST024", assetId, $"{path}.startTime",
                    "Montage section start times must be finite, strictly increasing, and inside the montage.");
            }
            previousStart = value.StartTime;
            if (value.NextSection == "None" || value.NextSection.Length != 0 && !names.Contains(value.NextSection))
            {
                Add(issues, "ALSMANIFEST024", assetId, $"{path}.nextSection",
                    "Montage nextSection must be empty or resolve within the montage.");
            }
        }
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        !path.Contains('\\') &&
        !path.Split('/').Any(segment => segment is "" or "." or "..");

    private static AssetSection[] GetSections(AlsManifest manifest) =>
    [
        new("skeletons", manifest.Skeletons),
        new("skeletalMeshes", manifest.SkeletalMeshes),
        new("staticMeshes", manifest.StaticMeshes),
        new("animations", manifest.Animations),
        new("montages", manifest.Montages),
        new("blendSpaces", manifest.BlendSpaces),
        new("aimOffsets", manifest.AimOffsets),
        new("materials", manifest.Materials),
        new("textures", manifest.Textures),
        new("physicsAssets", manifest.PhysicsAssets),
        new("curves", manifest.Curves),
        new("configAssets", manifest.ConfigAssets),
    ];

    private static void Add(
        List<AlsValidationIssue> issues,
        string code,
        string? assetId,
        string fieldPath,
        string message,
        string? expected = null,
        string? actual = null) =>
        issues.Add(new AlsValidationIssue(code, assetId, fieldPath, message, expected, actual));

    private readonly record struct AssetSection(string Name, AlsManifestAsset[] Assets);

    private readonly record struct AssetLocation(string Section, int Index);

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
