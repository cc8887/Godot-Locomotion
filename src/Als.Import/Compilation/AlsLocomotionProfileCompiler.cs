using System.Text.Json;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsLocomotionProfileCompiler
{
    private const int SchemaVersion = 2;
    private const int SupportedLeanAdditiveType = 1;

    private static readonly string[] RequiredProperties =
    [
        "schemaVersion",
        "presentation",
        "mannequin",
        "standingIdle",
        "crouchingIdle",
        "standingSamples",
        "crouchingSamples",
        "jumpStart",
        "fallLoop",
        "land",
        "leanAdditive",
    ];

    private static readonly HashSet<string> RequiredPropertySet =
        new(RequiredProperties.Append("standingWalkRun"), StringComparer.Ordinal);

    private static readonly string[] PresentationProperties =
    [
        "translationMeters",
        "yawRadians",
    ];

    private static readonly HashSet<string> PresentationPropertySet =
        new(PresentationProperties, StringComparer.Ordinal);

    private static readonly HashSet<string> SamplePropertySet =
        new(["animation", "x", "y", "rateScale"], StringComparer.Ordinal);

    public static AlsLocomotionAnimationProfile Compile(
        string json,
        AlsAnimationSetDefinition animationSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(animationSet);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException exception)
        {
            throw Failure("ALSPROFILE001", "$", $"Invalid profile JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Failure("ALSPROFILE002", "$", "The locomotion profile must be a JSON object.");
            }

            var properties = ValidateObject(root, RequiredPropertySet, "$", RequiredProperties);
            var version = ReadInteger(properties["schemaVersion"], "$.schemaVersion");
            if (version != SchemaVersion)
            {
                throw Failure(
                    "ALSPROFILE003", "$.schemaVersion", "Unsupported profile schema version.",
                    SchemaVersion.ToString(), version.ToString());
            }

            var presentation = ReadPresentation(properties["presentation"], "$.presentation");
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            var mannequinStableId = ReadStableId(properties["mannequin"], "$.mannequin", stableIds);
            var standingIdleStableId = ReadStableId(
                properties["standingIdle"], "$.standingIdle", stableIds);
            var crouchingIdleStableId = ReadStableId(
                properties["crouchingIdle"], "$.crouchingIdle", stableIds);
            var standingSources = ReadSamples(properties["standingSamples"], "$.standingSamples", stableIds);
            var crouchingSources = ReadSamples(properties["crouchingSamples"], "$.crouchingSamples", stableIds);
            var jumpStartStableId = ReadStableId(properties["jumpStart"], "$.jumpStart", stableIds);
            var fallLoopStableId = ReadStableId(properties["fallLoop"], "$.fallLoop", stableIds);
            var landStableId = ReadStableId(properties["land"], "$.land", stableIds);
            var leanAdditiveStableId = ReadStableId(
                properties["leanAdditive"], "$.leanAdditive", stableIds);

            var mannequinId = ResolveSkeletalMesh(animationSet, mannequinStableId, "$.mannequin");
            var mannequin = animationSet.SkeletalMeshes[mannequinId];
            var skeletonId = mannequin.SkeletonId;
            var standingIdleId = ResolveAnimation(
                animationSet, standingIdleStableId, "$.standingIdle", skeletonId);
            var crouchingIdleId = ResolveAnimation(
                animationSet, crouchingIdleStableId, "$.crouchingIdle", skeletonId);
            var standingSamples = CompileSamples(
                animationSet, standingSources, "$.standingSamples", skeletonId);
            var crouchingSamples = CompileSamples(
                animationSet, crouchingSources, "$.crouchingSamples", skeletonId);
            var jumpStartId = ResolveAnimation(
                animationSet, jumpStartStableId, "$.jumpStart", skeletonId);
            var fallLoopId = ResolveAnimation(
                animationSet, fallLoopStableId, "$.fallLoop", skeletonId);
            var landId = ResolveAnimation(animationSet, landStableId, "$.land", skeletonId);
            var (leanSamples, leanBasePoseId) = CompileLeanAdditive(
                animationSet, leanAdditiveStableId, skeletonId);
            var standingWalkRun = properties.TryGetValue("standingWalkRun", out var cycleSources)
                ? CompileStandingWalkRun(cycleSources, animationSet, skeletonId)
                : [];

            var allAnimationIds = new[]
                {
                    standingIdleId,
                    crouchingIdleId,
                }
                .Concat(standingSamples.Select(sample => sample.AnimationId))
                .Concat(crouchingSamples.Select(sample => sample.AnimationId))
                .Concat([jumpStartId, fallLoopId, landId])
                .Concat(leanSamples.Select(sample => sample.AnimationId))
                .Append(leanBasePoseId)
                .Concat(standingWalkRun.SelectMany(value => new[] { value.WalkPoseId, value.WalkId, value.RunPoseId, value.RunId }))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            return new AlsLocomotionAnimationProfile(
                skeletonId,
                mannequinId,
                presentation,
                standingIdleId,
                crouchingIdleId,
                standingSamples,
                crouchingSamples,
                jumpStartId,
                fallLoopId,
                landId,
                leanSamples,
                leanBasePoseId,
                allAnimationIds) { StandingWalkRun = standingWalkRun };
        }
    }

    private static AlsStandingWalkRunDefinition[] CompileStandingWalkRun(JsonElement sources,
        AlsAnimationSetDefinition set, int skeletonId)
    {
        if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() != 6)
            throw Failure("ALSPROFILE027", "$.standingWalkRun", "Expected F, B, LF, LB, RF, RB blend space IDs.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return sources.EnumerateArray().Select((source, index) =>
        {
            var path = $"$.standingWalkRun[{index}]";
            var stableId = ReadStableId(source, path, seen);
            AlsBlendDefinition blend;
            try { blend = set.BlendSpaces[set.AssetIndex.GetBlendSpaceId(stableId)]; }
            catch (KeyNotFoundException) { throw Failure("ALSPROFILE028", path, "Missing WalkRun blend space."); }
            if (blend.Samples.Length != 4) throw Failure("ALSPROFILE029", path, "WalkRun must have four corners.");
            var ids = new int[4];
            Array.Fill(ids, -1);
            foreach (var sample in blend.Samples)
            {
                if (sample.SampleValue.Length < 2 || sample.SampleValue[0] is not (0 or 1) ||
                    sample.SampleValue[1] is not (0 or 1) || sample.RateScale != 1)
                    throw Failure("ALSPROFILE030", path, "Unsupported Stride/WalkRun coordinate or rate.");
                var corner = (int)sample.SampleValue[0] + 2 * (int)sample.SampleValue[1];
                if (ids[corner] >= 0) throw Failure("ALSPROFILE031", path, "Duplicate WalkRun corner.");
                RequireSkeleton(set.Animations[sample.AnimationId], skeletonId, path);
                ids[corner] = sample.AnimationId;
            }
            return new AlsStandingWalkRunDefinition(ids[0], ids[1], ids[2], ids[3]);
        }).ToArray();
    }

    private static Dictionary<string, JsonElement> ValidateObject(
        JsonElement element,
        HashSet<string> expectedProperties,
        string path,
        IReadOnlyCollection<string> requiredProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Failure("ALSPROFILE004", path, "Expected a JSON object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (!expectedProperties.Contains(property.Name))
            {
                throw Failure("ALSPROFILE005", propertyPath, "Unknown property in locomotion profile.");
            }
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Failure("ALSPROFILE006", propertyPath, "Duplicate JSON property is not allowed.");
            }
        }

        foreach (var required in requiredProperties)
        {
            if (!properties.ContainsKey(required))
            {
                throw Failure("ALSPROFILE007", $"{path}.{required}", "Required property is missing.");
            }
        }
        return properties;
    }

    private static int ReadInteger(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw Failure("ALSPROFILE008", path, "Expected an integer value.");
        }
        return value;
    }

    private static string ReadStableId(
        JsonElement element,
        string path,
        HashSet<string> stableIds)
    {
        if (element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw Failure("ALSPROFILE009", path, "Expected a non-empty stable ID string.");
        }

        var stableId = element.GetString()!;
        if (!stableIds.Add(stableId))
        {
            throw Failure(
                "ALSPROFILE010", path, "Duplicate stable ID in locomotion profile.",
                "unique stable ID", stableId, stableId);
        }
        return stableId;
    }

    private static AlsPresentationDefinition ReadPresentation(JsonElement element, string path)
    {
        var properties = ValidateObject(
            element, PresentationPropertySet, path, PresentationProperties);
        var translationPath = $"{path}.translationMeters";
        var translationElement = properties["translationMeters"];
        if (translationElement.ValueKind != JsonValueKind.Array)
        {
            throw Failure("ALSPROFILE025", translationPath, "Expected a translation array.");
        }

        var translationValues = translationElement.EnumerateArray().ToArray();
        if (translationValues.Length != 3)
        {
            throw Failure(
                "ALSPROFILE026", translationPath, "Expected exactly three translation values.",
                "3", translationValues.Length.ToString());
        }

        return new AlsPresentationDefinition(
            new System.Numerics.Vector3(
                ReadFiniteSingle(translationValues[0], $"{translationPath}[0]"),
                ReadFiniteSingle(translationValues[1], $"{translationPath}[1]"),
                ReadFiniteSingle(translationValues[2], $"{translationPath}[2]")),
            ReadFiniteSingle(properties["yawRadians"], $"{path}.yawRadians"));
    }

    private static ProfileSampleSource[] ReadSamples(
        JsonElement element,
        string path,
        HashSet<string> stableIds)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Failure("ALSPROFILE011", path, "Expected a locomotion sample array.");
        }

        var samples = element.EnumerateArray().Select((sample, index) =>
        {
            var samplePath = $"{path}[{index}]";
            var properties = ValidateObject(
                sample, SamplePropertySet, samplePath, SamplePropertySet);
            return new ProfileSampleSource(
                ReadStableId(properties["animation"], $"{samplePath}.animation", stableIds),
                ReadFiniteSingle(properties["x"], $"{samplePath}.x"),
                ReadFiniteSingle(properties["y"], $"{samplePath}.y"),
                ReadPositiveSingle(properties["rateScale"], $"{samplePath}.rateScale"));
        }).ToArray();

        if (samples.Length == 0)
        {
            throw Failure("ALSPROFILE012", path, "Locomotion sample grid cannot be empty.");
        }
        return samples;
    }

    private static float ReadFiniteSingle(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetSingle(out var value) ||
            !float.IsFinite(value))
        {
            throw Failure("ALSPROFILE013", path, "Expected a finite single-precision value.");
        }
        return value;
    }

    private static float ReadPositiveSingle(JsonElement element, string path)
    {
        var value = ReadFiniteSingle(element, path);
        if (value <= 0f)
        {
            throw Failure("ALSPROFILE014", path, "Sample rate scale must be positive.");
        }
        return value;
    }

    private static int ResolveSkeletalMesh(
        AlsAnimationSetDefinition animationSet,
        string stableId,
        string path)
    {
        try
        {
            return animationSet.AssetIndex.GetSkeletalMeshId(stableId);
        }
        catch (KeyNotFoundException)
        {
            throw Failure(
                "ALSPROFILE015", path, "Profile references a missing stable ID without fallback.",
                "known skeletal mesh stable ID", stableId, stableId);
        }
    }

    private static int ResolveAnimation(
        AlsAnimationSetDefinition animationSet,
        string stableId,
        string path,
        int skeletonId)
    {
        if (!animationSet.AssetIndex.TryGetAnimationId(stableId, out var animationId))
        {
            throw Failure(
                "ALSPROFILE016", path, "Profile references a missing stable ID without fallback.",
                "known animation stable ID", stableId, stableId);
        }

        RequireSkeleton(animationSet.Animations[animationId], skeletonId, path);
        return animationId;
    }

    private static AlsLocomotionAnimationSample[] CompileSamples(
        AlsAnimationSetDefinition animationSet,
        ProfileSampleSource[] sources,
        string path,
        int skeletonId) =>
        sources.Select((source, index) => new AlsLocomotionAnimationSample(
            ResolveAnimation(
                animationSet, source.StableId, $"{path}[{index}].animation", skeletonId),
            source.X,
            source.Y,
            source.RateScale)).ToArray();

    private static (AlsLocomotionAnimationSample[] Samples, int BasePoseId) CompileLeanAdditive(
        AlsAnimationSetDefinition animationSet,
        string stableId,
        int skeletonId)
    {
        int blendId;
        try
        {
            blendId = animationSet.AssetIndex.GetBlendSpaceId(stableId);
        }
        catch (KeyNotFoundException)
        {
            throw Failure(
                "ALSPROFILE017", "$.leanAdditive",
                "Profile references a missing stable ID without fallback.",
                "known blend space stable ID", stableId, stableId);
        }

        var blend = animationSet.BlendSpaces[blendId];
        if (blend.Samples.Length == 0)
        {
            throw Failure("ALSPROFILE018", "$.leanAdditive", "Lean additive grid cannot be empty.");
        }

        var compiled = new AlsLocomotionAnimationSample[blend.Samples.Length];
        var basePoseId = -1;
        var basePoseType = -1;
        var basePoseFrame = -1;
        for (var index = 0; index < blend.Samples.Length; index++)
        {
            var sample = blend.Samples[index];
            var path = $"$.leanAdditive.samples[{index}]";
            if ((uint)sample.AnimationId >= (uint)animationSet.Animations.Length)
            {
                throw Failure("ALSPROFILE019", path, "Lean additive sample has a missing animation ID.");
            }

            var animation = animationSet.Animations[sample.AnimationId];
            RequireSkeleton(animation, skeletonId, path);
            if (animation.AdditiveType != SupportedLeanAdditiveType)
            {
                throw Failure(
                    "ALSPROFILE020", path,
                    "Lean sample uses an unsupported additive type.",
                    SupportedLeanAdditiveType.ToString(), animation.AdditiveType.ToString(), animation.StableId);
            }
            if ((uint)animation.AdditiveBasePoseAnimationId >= (uint)animationSet.Animations.Length)
            {
                throw Failure("ALSPROFILE021", path, "Lean additive base pose is missing.", assetId: animation.StableId);
            }

            if (basePoseId < 0)
            {
                basePoseId = animation.AdditiveBasePoseAnimationId;
                basePoseType = animation.AdditiveBasePoseType;
                basePoseFrame = animation.AdditiveBasePoseFrame;
            }
            else if (animation.AdditiveBasePoseAnimationId != basePoseId ||
                     animation.AdditiveBasePoseType != basePoseType)
            {
                throw Failure("ALSPROFILE022", path, "Lean additive base pose mismatch.", assetId: animation.StableId);
            }
            else if (animation.AdditiveBasePoseFrame != basePoseFrame)
            {
                throw Failure(
                    "ALSPROFILE022", path, "Lean additive base pose frame mismatch.",
                    basePoseFrame.ToString(), animation.AdditiveBasePoseFrame.ToString(), animation.StableId);
            }

            RequireSkeleton(animationSet.Animations[animation.AdditiveBasePoseAnimationId], skeletonId, path);
            if (sample.SampleValue.Length < 2 ||
                !float.IsFinite(sample.SampleValue[0]) ||
                !float.IsFinite(sample.SampleValue[1]) ||
                !float.IsFinite(sample.RateScale) ||
                sample.RateScale <= 0f)
            {
                throw Failure("ALSPROFILE023", path, "Lean additive sample coordinates or rate are invalid.");
            }

            compiled[index] = new AlsLocomotionAnimationSample(
                sample.AnimationId,
                sample.SampleValue[0],
                sample.SampleValue[1],
                sample.RateScale);
        }
        return (compiled, basePoseId);
    }

    private static void RequireSkeleton(
        AlsAnimationDefinition animation,
        int skeletonId,
        string path)
    {
        if (animation.SkeletonId != skeletonId)
        {
            throw Failure(
                "ALSPROFILE024", path, "Profile contains a cross-skeleton animation.",
                skeletonId.ToString(), animation.SkeletonId.ToString(), animation.StableId);
        }
    }

    private static AlsCompilationException Failure(
        string code,
        string path,
        string message,
        string? expected = null,
        string? actual = null,
        string? assetId = null) =>
        new([new AlsValidationIssue(code, assetId, path, message, expected, actual)]);

    private sealed record ProfileSampleSource(string StableId, float X, float Y, float RateScale);
}
