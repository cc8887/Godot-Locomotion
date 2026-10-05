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
        AddMissingTopLevelCollections(manifest, issues);
        var sections = GetSections(manifest);

        if (manifest.SchemaVersion != options.SupportedSchemaVersion)
        {
            Add(issues, "ALSMANIFEST001", null, "$.schemaVersion", "Unsupported manifest schema.",
                options.SupportedSchemaVersion.ToString(), manifest.SchemaVersion.ToString());
        }

        if (string.IsNullOrWhiteSpace(manifest.ExporterVersion))
        {
            Add(issues, "ALSMANIFEST013", null, "$.exporterVersion", "Exporter version metadata cannot be empty.",
                "non-empty", manifest.ExporterVersion);
        }

        if (manifest.AuditSummary is null)
        {
            Add(issues, "ALSMANIFEST029", null, "$.auditSummary", "Required manifest value cannot be null.");
        }
        else
        {
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

            var fileCount = manifest.Files?.Length ?? 0;
            if (manifest.AuditSummary.FileCount != fileCount)
            {
                Add(issues, "ALSMANIFEST004", null, "$.auditSummary.fileCount", "File count does not match manifest contents.",
                    fileCount.ToString(), manifest.AuditSummary.FileCount.ToString());
            }

            if (manifest.AuditSummary.ErrorCount != 0)
            {
                Add(issues, "ALSMANIFEST005", null, "$.auditSummary.errorCount", "Completed manifest contains exporter errors.",
                    "0", manifest.AuditSummary.ErrorCount.ToString());
            }
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
                var asset = section.Assets[index];
                if (asset is null)
                {
                    continue;
                }
                ValidateAssetReferences(section.Name, index, asset, ids, issues);
                ValidateTypedMetadata(section.Name, index, asset, eventIds, markerIds, issues);
            }
        }

        return issues;
    }

    private static void ValidateCoordinateSystem(AlsManifest manifest, List<AlsValidationIssue> issues)
    {
        var coordinate = manifest.CoordinateSystem;
        if (coordinate is null)
        {
            Add(issues, "ALSMANIFEST029", null, "$.coordinateSystem", "Required manifest value cannot be null.");
            return;
        }
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
            if (asset is null)
            {
                Add(issues, "ALSMANIFEST029", null, path, "Required manifest array element cannot be null.");
                continue;
            }
            var previousAsset = index > 0 ? section.Assets[index - 1] : null;
            if (previousAsset is not null && string.CompareOrdinal(previousAsset.Id, asset.Id) > 0)
            {
                Add(issues, "ALSMANIFEST007", asset.Id, $"$.{section.Name}", "Assets must be sorted by stable ID.");
            }

            if (string.IsNullOrWhiteSpace(asset.Id))
            {
                Add(issues, "ALSMANIFEST029", null, $"{path}.id", "Required asset ID cannot be null or empty.");
                continue;
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
                    if (string.IsNullOrWhiteSpace(asset.ObjectPath))
                    {
                        throw new ArgumentException("Object path is required.");
                    }
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

            if (string.IsNullOrWhiteSpace(asset.PackagePath) ||
                !asset.PackagePath.StartsWith("/Game/", StringComparison.Ordinal) || asset.PackagePath.Contains('\\'))
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
        if (asset.Dependencies is null)
        {
            Add(issues, "ALSMANIFEST029", asset.Id, $"{path}.dependencies", "Required asset collection cannot be null.");
            return;
        }
        for (var dependencyIndex = 0; dependencyIndex < asset.Dependencies.Length; dependencyIndex++)
        {
            var dependency = asset.Dependencies[dependencyIndex];
            if (!ids.ContainsKey(dependency))
            {
                Add(issues, "ALSMANIFEST010", asset.Id, $"{path}.dependencies[{dependencyIndex}]",
                    "Dependency does not resolve to a manifest asset.", null, dependency);
            }
        }

        if (asset.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            Add(issues, "ALSMANIFEST029", asset.Id, $"{path}.metadata", "Required asset metadata cannot be null.");
            return;
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
        if (section is not ("animations" or "montages"))
        {
            return;
        }
        var path = $"$.{section}[{index}].metadata";
        if (asset.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return;
        }
        if (TryFindFirstNull(asset.Metadata, path, out var nullPath))
        {
            Add(issues, "ALSMANIFEST029", asset.Id, nullPath, "Required metadata value cannot be null.");
            return;
        }

        try
        {
            if (section == "animations")
            {
                var metadata = AlsAnimationMetadata.Read(asset.Metadata);
                ValidateAnimationContract(asset, metadata, path, issues);
                ValidateTimeline(metadata.Timeline, metadata.PlayLength, path, asset.Id, eventIds, issues);
                ValidateMarkers(metadata.SyncMarkers, metadata.PlayLength, path, asset.Id, markerIds, issues);
            }
            else if (section == "montages")
            {
                var metadata = AlsMontageMetadata.Read(asset.Metadata);
                ValidateMontageContract(asset, metadata, path, issues);
                ValidateTimeline(metadata.Timeline, metadata.PlayLength, path, asset.Id, eventIds, issues);
                ValidateMontageSections(metadata, path, asset.Id, issues);
            }
        }
        catch (JsonException exception)
        {
            Add(issues, "ALSMANIFEST029", asset.Id, path, "Metadata violates the required runtime shape.", null, exception.Message);
        }
    }

    private static void ValidateAnimationContract(
        AlsManifestAsset asset,
        AlsAnimationMetadata metadata,
        string metadataPath,
        List<AlsValidationIssue> issues)
    {
        var assetPath = metadataPath[..^".metadata".Length];
        if (asset.ClassPath is not "/Script/Engine.AnimSequence")
        {
            Add(issues, "ALSMANIFEST025", asset.Id, $"{assetPath}.classPath",
                "Animation class path must identify an AnimSequence.", "/Script/Engine.AnimSequence", asset.ClassPath);
        }

        if (!float.IsFinite(metadata.PlayLength) || metadata.PlayLength < 0f)
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.playLength",
                "Animation play length must be finite and nonnegative.");
        }
        if (metadata.FrameRateNumerator <= 0)
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.frameRateNumerator",
                "Animation frame-rate numerator must be positive.");
        }
        if (metadata.FrameRateDenominator <= 0)
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.frameRateDenominator",
                "Animation frame-rate denominator must be positive.");
        }
        if (metadata.SampledKeyCount < 0)
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.sampledKeyCount",
                "Animation sampled-key count must be nonnegative.");
        }
        if (!StableIdRegex().IsMatch(metadata.SkeletonId ?? string.Empty))
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.skeletonId",
                "Animation skeleton ID must be a lowercase SHA-1.");
        }
        if (string.IsNullOrWhiteSpace(metadata.SkeletonObjectPath))
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.skeletonObjectPath",
                "Animation skeleton object path is required.");
        }
        if (!string.IsNullOrEmpty(metadata.AdditiveBasePoseId) && !StableIdRegex().IsMatch(metadata.AdditiveBasePoseId))
        {
            Add(issues, "ALSMANIFEST026", asset.Id, $"{metadataPath}.additiveBasePoseId",
                "Animation additive base-pose ID must be empty or a lowercase SHA-1.");
        }
        ValidateStructuredCurves(metadata, metadataPath, asset.Id, issues);
    }

    private static void ValidateStructuredCurves(
        AlsAnimationMetadata metadata,
        string metadataPath,
        string assetId,
        List<AlsValidationIssue> issues)
    {
        var curves = metadata.Curves.RequireStructuredPayload();
        for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
        {
            for (var keyIndex = 0; keyIndex < curves[curveIndex].Keys.Length; keyIndex++)
            {
                var key = curves[curveIndex].Keys[keyIndex];
                var path = $"{metadataPath}.curves[{curveIndex}].keys[{keyIndex}]";
                ValidateCurveScalar(key.TimeSeconds, $"{path}.timeSeconds", assetId, issues);
                ValidateCurveScalar(key.Value, $"{path}.value", assetId, issues);
                ValidateCurveScalar(key.ArriveTangent, $"{path}.arriveTangent", assetId, issues);
                ValidateCurveScalar(key.LeaveTangent, $"{path}.leaveTangent", assetId, issues);
            }
        }
    }

    private static void ValidateCurveScalar(
        double value, string path, string assetId, List<AlsValidationIssue> issues)
    {
        if (!IsFiniteFloat(value))
        {
            Add(issues, "ALSMANIFEST030", assetId, path,
                "Structured curve key scalar must be representable as a finite float.");
        }
    }

    private static void ValidateMontageContract(
        AlsManifestAsset asset,
        AlsMontageMetadata metadata,
        string metadataPath,
        List<AlsValidationIssue> issues)
    {
        var assetPath = metadataPath[..^".metadata".Length];
        if (asset.ClassPath is not "/Script/Engine.AnimMontage")
        {
            Add(issues, "ALSMANIFEST025", asset.Id, $"{assetPath}.classPath",
                "Montage class path must identify an AnimMontage.", "/Script/Engine.AnimMontage", asset.ClassPath);
        }

        ValidateMontageScalar(metadata.PlayLength, false, $"{metadataPath}.playLength", asset.Id, issues);
        ValidateMontageScalar(metadata.BlendInTime, false, $"{metadataPath}.blendInTime", asset.Id, issues);
        ValidateMontageScalar(metadata.BlendOutTime, false, $"{metadataPath}.blendOutTime", asset.Id, issues);
        ValidateMontageScalar(metadata.BlendOutTriggerTime, true, $"{metadataPath}.blendOutTriggerTime", asset.Id, issues);

        for (var slotIndex = 0; slotIndex < metadata.Slots.Length; slotIndex++)
        {
            var slot = metadata.Slots[slotIndex];
            var slotPath = $"{metadataPath}.slots[{slotIndex}]";
            if (string.IsNullOrWhiteSpace(slot.SlotName))
            {
                Add(issues, "ALSMANIFEST028", asset.Id, $"{slotPath}.slotName", "Montage slot name is required.");
            }
            for (var segmentIndex = 0; segmentIndex < slot.Segments.Length; segmentIndex++)
            {
                var segment = slot.Segments[segmentIndex];
                var segmentPath = $"{slotPath}.segments[{segmentIndex}]";
                if (!StableIdRegex().IsMatch(segment.AnimationId ?? string.Empty))
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.animationId",
                        "Montage segment animation ID must be a lowercase SHA-1.");
                }
                if (string.IsNullOrWhiteSpace(segment.AnimationObjectPath))
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.animationObjectPath",
                        "Montage segment animation object path is required.");
                }
                if (!float.IsFinite(segment.StartPosition) || segment.StartPosition < 0f ||
                    segment.StartPosition > metadata.PlayLength)
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.startPosition",
                        "Montage segment start position must be finite and inside the montage.");
                }
                if (!float.IsFinite(segment.AnimationStartTime) || segment.AnimationStartTime < 0f)
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.animationStartTime",
                        "Montage segment animation start time must be finite and nonnegative.");
                }
                if (!float.IsFinite(segment.AnimationEndTime) || segment.AnimationEndTime < 0f ||
                    segment.AnimationEndTime < segment.AnimationStartTime)
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.animationEndTime",
                        "Montage segment animation end time must be finite, nonnegative, and not precede its start.");
                }
                if (!float.IsFinite(segment.PlayRate) || segment.PlayRate <= 0f)
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.playRate",
                        "Montage segment play rate must be finite and positive.");
                }
                if (segment.LoopCount < 1)
                {
                    Add(issues, "ALSMANIFEST028", asset.Id, $"{segmentPath}.loopCount",
                        "Montage segment loop count must be at least one.");
                }
            }
        }
    }

    private static void ValidateMontageScalar(
        float value, bool allowNegative, string path, string assetId, List<AlsValidationIssue> issues)
    {
        if (!float.IsFinite(value) || !allowNegative && value < 0f)
        {
            Add(issues, "ALSMANIFEST027", assetId, path,
                allowNegative ? "Montage scalar must be finite." : "Montage scalar must be finite and nonnegative.");
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
            if (!IsFiniteFloat(value.TimeSeconds) || value.TimeSeconds < 0.0 ||
                !double.IsFinite(sourceLength) || value.TimeSeconds > sourceLength)
            {
                Add(issues, "ALSMANIFEST016", assetId, $"{path}.timeSeconds", "Timeline time is outside the source length.");
            }
            if (!IsFiniteFloat(value.DurationSeconds) || value.DurationSeconds < 0.0 ||
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
                when !IsFiniteFloat(payload.BlendOutSeconds) || payload.BlendOutSeconds < 0.0:
                Add(issues, "ALSMANIFEST021", assetId, $"{path}.payload.blendOutSeconds",
                    "EarlyBlendOut blend duration must be finite and nonnegative.");
                break;
            case AlsRootMotionScaleEventPayloadMetadata payload
                when !IsFiniteFloat(payload.TranslationScale) || payload.TranslationScale < 0.0:
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
            if (!IsFiniteFloat(value.TimeSeconds) || value.TimeSeconds < 0.0 || value.TimeSeconds > sourceLength)
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

    private static bool IsFiniteFloat(double value) =>
        double.IsFinite(value) && value >= -float.MaxValue && value <= float.MaxValue;

    private static bool TryFindFirstNull(JsonElement element, string path, out string nullPath)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            nullPath = path;
            return true;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TryFindFirstNull(property.Value, $"{path}.{property.Name}", out nullPath))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindFirstNull(item, $"{path}[{index}]", out nullPath))
                {
                    return true;
                }
                index++;
            }
        }

        nullPath = string.Empty;
        return false;
    }

    private static void AddMissingTopLevelCollections(AlsManifest manifest, List<AlsValidationIssue> issues)
    {
        foreach (var (path, value) in new (string Path, Array? Value)[]
        {
            ("$.skeletons", manifest.Skeletons),
            ("$.skeletalMeshes", manifest.SkeletalMeshes),
            ("$.staticMeshes", manifest.StaticMeshes),
            ("$.animations", manifest.Animations),
            ("$.montages", manifest.Montages),
            ("$.blendSpaces", manifest.BlendSpaces),
            ("$.aimOffsets", manifest.AimOffsets),
            ("$.materials", manifest.Materials),
            ("$.textures", manifest.Textures),
            ("$.physicsAssets", manifest.PhysicsAssets),
            ("$.curves", manifest.Curves),
            ("$.configAssets", manifest.ConfigAssets),
            ("$.files", manifest.Files),
        })
        {
            if (value is null)
            {
                Add(issues, "ALSMANIFEST029", null, path, "Required manifest collection cannot be null.");
            }
        }
    }

    private static AssetSection[] GetSections(AlsManifest manifest) =>
    [
        new("skeletons", manifest.Skeletons ?? []),
        new("skeletalMeshes", manifest.SkeletalMeshes ?? []),
        new("staticMeshes", manifest.StaticMeshes ?? []),
        new("animations", manifest.Animations ?? []),
        new("montages", manifest.Montages ?? []),
        new("blendSpaces", manifest.BlendSpaces ?? []),
        new("aimOffsets", manifest.AimOffsets ?? []),
        new("materials", manifest.Materials ?? []),
        new("textures", manifest.Textures ?? []),
        new("physicsAssets", manifest.PhysicsAssets ?? []),
        new("curves", manifest.Curves ?? []),
        new("configAssets", manifest.ConfigAssets ?? []),
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
