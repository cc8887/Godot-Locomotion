using System.Text.Json;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsPoseProfileCompiler
{
    private const int SchemaVersion = 1;
    private const int AimAdditiveType = 2;
    private static readonly string[] RootProperties = ["schemaVersion", "skeleton", "aim", "turns", "rotates", "masks", "feet"];
    private static readonly string[] AimProperties = ["aimOffset", "down", "forward", "up"];
    private static readonly string[] TurnProperties = ["animation", "stance", "direction", "nominalDegrees", "basePlayRate", "blendSeconds", "scaleAngle"];
    private static readonly string[] RotateProperties = ["animation", "stance", "direction"];
    private static readonly string[] MaskProperties = ["kind", "root", "boundaries"];
    private static readonly string[] FeetProperties = ["leftLegRoot", "rightLegRoot", "leftFootRoot", "rightFootRoot", "traceUpMeters", "traceDownMeters", "footHeightMeters", "maxPelvisCorrectionMeters", "positionHalfLifeSeconds", "rotationHalfLifeSeconds", "lockReleaseHalfLifeSeconds"];

    public static AlsPoseAnimationProfile Compile(string json, AlsAnimationSetDefinition animationSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(animationSet);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException exception)
        {
            throw Failure("ALSPOSE001", "$", $"Invalid pose profile JSON: {exception.Message}");
        }

        using (document)
        {
            var root = Object(document.RootElement, "$", RootProperties);
            var version = Integer(root["schemaVersion"], "$.schemaVersion");
            if (version != SchemaVersion)
            {
                throw Failure("ALSPOSE002", "$.schemaVersion", "Unsupported pose profile schema version.", "1", version.ToString());
            }

            var skeletonStableId = String(root["skeleton"], "$.skeleton");
            int skeletonId;
            try { skeletonId = animationSet.AssetIndex.GetSkeletonId(skeletonStableId); }
            catch (KeyNotFoundException) { throw Failure("ALSPOSE003", "$.skeleton", "Missing skeleton stable ID.", assetId: skeletonStableId); }
            var skeleton = animationSet.Skeletons[skeletonId];
            var aim = CompileAim(root["aim"], animationSet, skeletonId);
            var turns = CompileTurns(root["turns"], animationSet, skeletonId);
            var rotates = CompileRotates(root["rotates"], animationSet, skeletonId);
            var masks = CompileMasks(root["masks"], skeleton);
            var feet = CompileFeet(root["feet"], skeleton, masks);
            return new AlsPoseAnimationProfile(version, skeletonId, aim, turns, rotates, masks, feet);
        }
    }

    private static AlsAimProfile CompileAim(JsonElement element, AlsAnimationSetDefinition set, int skeletonId)
    {
        var source = Object(element, "$.aim", AimProperties);
        var aimStableId = String(source["aimOffset"], "$.aim.aimOffset");
        int aimOffsetId;
        try { aimOffsetId = set.AssetIndex.GetAimOffsetId(aimStableId); }
        catch (KeyNotFoundException) { throw Failure("ALSPOSE004", "$.aim.aimOffset", "Missing aim offset stable ID.", assetId: aimStableId); }

        var down = ResolveAnimation(set, source["down"], "$.aim.down", skeletonId);
        var forward = ResolveAnimation(set, source["forward"], "$.aim.forward", skeletonId);
        var up = ResolveAnimation(set, source["up"], "$.aim.up", skeletonId);
        var ids = new[] { down, forward, up };
        var aimOffset = set.AimOffsets[aimOffsetId];
        var sampleIds = aimOffset.Samples.Select(value => value.AnimationId).OrderBy(value => value).ToArray();
        if (!sampleIds.SequenceEqual(ids.OrderBy(value => value)))
        {
            throw Failure("ALSPOSE005", "$.aim.aimOffset", "Aim offset samples do not exactly match down, forward and up sweeps.", assetId: aimStableId);
        }

        var first = set.Animations[down];
        if (first.AdditiveType != AimAdditiveType ||
            (uint)first.AdditiveBasePoseAnimationId >= (uint)set.Animations.Length ||
            set.Animations[first.AdditiveBasePoseAnimationId].AdditiveType != 0)
        {
            throw Failure("ALSPOSE006", "$.aim.down", "Aim sweep has an incompatible additive contract.", assetId: first.StableId);
        }
        foreach (var (id, path) in new[] { (down, "$.aim.down"), (forward, "$.aim.forward"), (up, "$.aim.up") })
        {
            var animation = set.Animations[id];
            if (animation.AdditiveType != first.AdditiveType ||
                animation.AdditiveBasePoseType != first.AdditiveBasePoseType ||
                animation.AdditiveBasePoseFrame != first.AdditiveBasePoseFrame ||
                animation.AdditiveBasePoseAnimationId != first.AdditiveBasePoseAnimationId)
            {
                throw Failure("ALSPOSE006", path, "Aim sweeps must share one compatible additive base.", assetId: animation.StableId);
            }
        }
        RequireSkeleton(set.Animations[first.AdditiveBasePoseAnimationId], skeletonId, "$.aim.down");
        return new AlsAimProfile(aimOffsetId, down, forward, up, first.AdditiveBasePoseAnimationId);
    }

    private static AlsTurnProfile[] CompileTurns(JsonElement element, AlsAnimationSetDefinition set, int skeletonId)
    {
        var values = Array(element, "$.turns");
        if (values.Length != 8) throw Failure("ALSPOSE007", "$.turns", "Exactly eight turn profiles are required.", "8", values.Length.ToString());
        var result = new AlsTurnProfile[values.Length];
        var combinations = new HashSet<(AlsPoseStance, sbyte, short)>();
        for (var index = 0; index < values.Length; index++)
        {
            var path = $"$.turns[{index}]";
            var source = Object(values[index], path, TurnProperties);
            var animationId = ResolveAnimation(set, source["animation"], $"{path}.animation", skeletonId);
            var stance = Stance(source["stance"], $"{path}.stance");
            var direction = Direction(source["direction"], $"{path}.direction");
            var nominal = Integer(source["nominalDegrees"], $"{path}.nominalDegrees");
            if (nominal is not (90 or 180)) throw Failure("ALSPOSE008", $"{path}.nominalDegrees", "Nominal turn must be 90 or 180 degrees.");
            var rate = Finite(source["basePlayRate"], $"{path}.basePlayRate");
            var blend = Finite(source["blendSeconds"], $"{path}.blendSeconds");
            var scale = Boolean(source["scaleAngle"], $"{path}.scaleAngle");
            if (rate != 1.2f || blend != 0.2f || !scale) throw Failure("ALSPOSE009", path, "Turn settings must use basePlayRate 1.2, blendSeconds 0.2 and scaleAngle true.");
            if (!combinations.Add((stance, direction, (short)nominal))) throw Failure("ALSPOSE010", path, "Duplicate turn stance, direction and angle combination.");
            result[index] = new AlsTurnProfile(animationId, CanonicalCurveId(set.Animations[animationId], $"{path}.animation"), stance, direction, (short)nominal, rate, blend, 1);
        }
        RequireCompleteTurns(combinations);
        return result;
    }

    private static AlsRotateProfile[] CompileRotates(JsonElement element, AlsAnimationSetDefinition set, int skeletonId)
    {
        var values = Array(element, "$.rotates");
        if (values.Length != 4) throw Failure("ALSPOSE011", "$.rotates", "Exactly four rotate profiles are required.", "4", values.Length.ToString());
        var result = new AlsRotateProfile[values.Length];
        var combinations = new HashSet<(AlsPoseStance, sbyte)>();
        for (var index = 0; index < values.Length; index++)
        {
            var path = $"$.rotates[{index}]";
            var source = Object(values[index], path, RotateProperties);
            var animationId = ResolveAnimation(set, source["animation"], $"{path}.animation", skeletonId);
            var stance = Stance(source["stance"], $"{path}.stance");
            var direction = Direction(source["direction"], $"{path}.direction");
            if (!combinations.Add((stance, direction))) throw Failure("ALSPOSE012", path, "Duplicate rotate stance and direction combination.");
            result[index] = new AlsRotateProfile(animationId, CanonicalCurveId(set.Animations[animationId], $"{path}.animation"), stance, direction);
        }
        foreach (var stance in Enum.GetValues<AlsPoseStance>()) foreach (var direction in new sbyte[] { -1, 1 })
            if (!combinations.Contains((stance, direction))) throw Failure("ALSPOSE013", "$.rotates", "Rotate profiles do not cover every stance and direction.");
        return result;
    }

    private static AlsLayerMaskProfile CompileMasks(JsonElement element, AlsSkeletonDefinition skeleton)
    {
        var values = Array(element, "$.masks");
        if (values.Length != 11) throw Failure("ALSPOSE014", "$.masks", "Exactly eleven mask roots are required.", "11", values.Length.ToString());
        var entries = new AlsBoneMaskProfile[values.Length];
        var kinds = new HashSet<AlsPoseMaskKind>();
        var claimed = new HashSet<int>();
        var roots = new Dictionary<AlsPoseMaskKind, int>();
        for (var index = 0; index < values.Length; index++)
        {
            var path = $"$.masks[{index}]";
            var source = Object(values[index], path, MaskProperties);
            var kind = MaskKind(source["kind"], $"{path}.kind");
            if (!kinds.Add(kind)) throw Failure("ALSPOSE015", $"{path}.kind", "Duplicate mask kind.");
            var rootName = String(source["root"], $"{path}.root");
            if (!string.Equals(rootName, ExpectedRootName(kind), StringComparison.Ordinal))
                throw Failure("ALSPOSE041", $"{path}.root", "Mask root does not match the canonical P4 logical bone name.", ExpectedRootName(kind), rootName, skeleton.AssetId);
            var rootId = ExactBoneId(skeleton, rootName, $"{path}.root");
            roots[kind] = rootId;
            var boundaryElements = Array(source["boundaries"], $"{path}.boundaries");
            var boundaries = new HashSet<int>();
            for (var boundaryIndex = 0; boundaryIndex < boundaryElements.Length; boundaryIndex++)
            {
                var boundaryPath = $"{path}.boundaries[{boundaryIndex}]";
                var boundaryId = ExactBoneId(skeleton, String(boundaryElements[boundaryIndex], boundaryPath), boundaryPath);
                if (!IsDescendant(skeleton, boundaryId, rootId) || boundaryId == rootId)
                    throw Failure("ALSPOSE016", boundaryPath, "Mask boundary must be a strict descendant of its root.", assetId: skeleton.AssetId);
                if (!boundaries.Add(boundaryId)) throw Failure("ALSPOSE017", boundaryPath, "Duplicate mask boundary bone.");
            }
            var boneIds = skeleton.LogicalBones.Where(bone => IsDescendant(skeleton, bone.LogicalId, rootId) &&
                    !boundaries.Any(boundary => IsDescendant(skeleton, bone.LogicalId, boundary)))
                .Select(bone => bone.LogicalId).OrderBy(value => value).ToArray();
            if (boneIds.Length == 0) throw Failure("ALSPOSE018", path, "Mask expansion is empty.");
            foreach (var boneId in boneIds)
                if (!claimed.Add(boneId)) throw Failure("ALSPOSE019", "$.masks", "Expanded masks contain a duplicate logical bone.", actual: skeleton.LogicalBones[boneId].Name);
            entries[index] = new AlsBoneMaskProfile(kind, rootId, boneIds);
        }
        if (kinds.Count != Enum.GetValues<AlsPoseMaskKind>().Length) throw Failure("ALSPOSE020", "$.masks", "Mask kinds are incomplete.");
        RequirePairedRoots(skeleton, roots, AlsPoseMaskKind.LeftArm, AlsPoseMaskKind.RightArm);
        RequirePairedRoots(skeleton, roots, AlsPoseMaskKind.LeftHand, AlsPoseMaskKind.RightHand);
        RequirePairedRoots(skeleton, roots, AlsPoseMaskKind.LeftLeg, AlsPoseMaskKind.RightLeg);
        RequirePairedRoots(skeleton, roots, AlsPoseMaskKind.LeftFoot, AlsPoseMaskKind.RightFoot);
        return new AlsLayerMaskProfile(entries);
    }

    private static AlsFootPlacementSettings CompileFeet(JsonElement element, AlsSkeletonDefinition skeleton, AlsLayerMaskProfile masks)
    {
        var source = Object(element, "$.feet", FeetProperties);
        var leftLeg = ExactBoneId(skeleton, String(source["leftLegRoot"], "$.feet.leftLegRoot"), "$.feet.leftLegRoot");
        var rightLeg = ExactBoneId(skeleton, String(source["rightLegRoot"], "$.feet.rightLegRoot"), "$.feet.rightLegRoot");
        var leftFoot = ExactBoneId(skeleton, String(source["leftFootRoot"], "$.feet.leftFootRoot"), "$.feet.leftFootRoot");
        var rightFoot = ExactBoneId(skeleton, String(source["rightFootRoot"], "$.feet.rightFootRoot"), "$.feet.rightFootRoot");
        RequirePair(skeleton, leftLeg, rightLeg, "$.feet");
        RequirePair(skeleton, leftFoot, rightFoot, "$.feet");
        RequireFootMask(masks, AlsPoseMaskKind.LeftLeg, leftLeg, "$.feet.leftLegRoot");
        RequireFootMask(masks, AlsPoseMaskKind.RightLeg, rightLeg, "$.feet.rightLegRoot");
        RequireFootMask(masks, AlsPoseMaskKind.LeftFoot, leftFoot, "$.feet.leftFootRoot");
        RequireFootMask(masks, AlsPoseMaskKind.RightFoot, rightFoot, "$.feet.rightFootRoot");
        return new AlsFootPlacementSettings(leftLeg, rightLeg, leftFoot, rightFoot,
            Ranged(source["traceUpMeters"], "$.feet.traceUpMeters", 0f, 5f),
            Ranged(source["traceDownMeters"], "$.feet.traceDownMeters", 0f, 5f),
            Ranged(source["footHeightMeters"], "$.feet.footHeightMeters", 0f, 1f, allowZero: true),
            Ranged(source["maxPelvisCorrectionMeters"], "$.feet.maxPelvisCorrectionMeters", 0f, 2f),
            Ranged(source["positionHalfLifeSeconds"], "$.feet.positionHalfLifeSeconds", 0f, 5f),
            Ranged(source["rotationHalfLifeSeconds"], "$.feet.rotationHalfLifeSeconds", 0f, 5f),
            Ranged(source["lockReleaseHalfLifeSeconds"], "$.feet.lockReleaseHalfLifeSeconds", 0f, 5f));
    }

    private static Dictionary<string, JsonElement> Object(JsonElement element, string path, string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Failure("ALSPOSE021", path, "Expected a JSON object.");
        var expected = new HashSet<string>(required, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name)) throw Failure("ALSPOSE022", $"{path}.{property.Name}", "Unknown property in pose profile.");
            if (!values.TryAdd(property.Name, property.Value)) throw Failure("ALSPOSE023", $"{path}.{property.Name}", "Duplicate JSON property is not allowed.");
        }
        foreach (var name in required) if (!values.ContainsKey(name)) throw Failure("ALSPOSE024", $"{path}.{name}", "Required property is missing.");
        return values;
    }

    private static JsonElement[] Array(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array) throw Failure("ALSPOSE025", path, "Expected a JSON array.");
        return element.EnumerateArray().ToArray();
    }

    private static string String(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())) throw Failure("ALSPOSE026", path, "Expected a non-empty string.");
        return element.GetString()!;
    }

    private static int Integer(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value)) throw Failure("ALSPOSE027", path, "Expected an integer.");
        return value;
    }

    private static float Finite(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) || !float.IsFinite(value)) throw Failure("ALSPOSE028", path, "Expected a finite single-precision value.");
        return value;
    }

    private static bool Boolean(JsonElement element, string path)
    {
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Failure("ALSPOSE029", path, "Expected a boolean.");
        return element.GetBoolean();
    }

    private static float Ranged(JsonElement element, string path, float minimum, float maximum, bool allowZero = false)
    {
        var value = Finite(element, path);
        if ((allowZero ? value < minimum : value <= minimum) || value > maximum) throw Failure("ALSPOSE030", path, "Foot setting is outside its supported range.", $"{minimum}..{maximum}", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return value;
    }

    private static int ResolveAnimation(AlsAnimationSetDefinition set, JsonElement element, string path, int skeletonId)
    {
        var stableId = String(element, path);
        if (!set.AssetIndex.TryGetAnimationId(stableId, out var id)) throw Failure("ALSPOSE031", path, "Missing animation stable ID without fallback.", assetId: stableId);
        RequireSkeleton(set.Animations[id], skeletonId, path);
        return id;
    }

    private static void RequireSkeleton(AlsAnimationDefinition animation, int skeletonId, string path)
    {
        if (animation.SkeletonId != skeletonId) throw Failure("ALSPOSE032", path, "Pose animation targets the wrong skeleton.", skeletonId.ToString(), animation.SkeletonId.ToString(), animation.StableId);
    }

    private static int CanonicalCurveId(AlsAnimationDefinition animation, string path)
    {
        var matches = animation.Curves.Where(value => value.CanonicalKind == AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond).ToArray();
        if (matches.Length != 1) throw Failure("ALSPOSE033", path, "Turn/rotate animation must contain exactly one canonical rotation yaw curve.", "1", matches.Length.ToString(), animation.StableId);
        return matches[0].CurveId;
    }

    private static AlsPoseStance Stance(JsonElement element, string path) => String(element, path) switch
    {
        "standing" => AlsPoseStance.Standing,
        "crouching" => AlsPoseStance.Crouching,
        _ => throw Failure("ALSPOSE034", path, "Stance must be 'standing' or 'crouching'."),
    };

    private static sbyte Direction(JsonElement element, string path)
    {
        var value = Integer(element, path);
        if (value is not (-1 or 1)) throw Failure("ALSPOSE035", path, "Direction must be -1 or 1.");
        return (sbyte)value;
    }

    private static AlsPoseMaskKind MaskKind(JsonElement element, string path) => String(element, path) switch
    {
        "upperBody" => AlsPoseMaskKind.UpperBody, "head" => AlsPoseMaskKind.Head,
        "leftArm" => AlsPoseMaskKind.LeftArm, "rightArm" => AlsPoseMaskKind.RightArm,
        "leftHand" => AlsPoseMaskKind.LeftHand, "rightHand" => AlsPoseMaskKind.RightHand,
        "pelvis" => AlsPoseMaskKind.Pelvis, "leftLeg" => AlsPoseMaskKind.LeftLeg,
        "rightLeg" => AlsPoseMaskKind.RightLeg, "leftFoot" => AlsPoseMaskKind.LeftFoot,
        "rightFoot" => AlsPoseMaskKind.RightFoot,
        _ => throw Failure("ALSPOSE036", path, "Unknown pose mask kind."),
    };

    private static string ExpectedRootName(AlsPoseMaskKind kind) => kind switch
    {
        AlsPoseMaskKind.UpperBody => "spine_01",
        AlsPoseMaskKind.Head => "neck_01",
        AlsPoseMaskKind.LeftArm => "clavicle_l",
        AlsPoseMaskKind.RightArm => "clavicle_r",
        AlsPoseMaskKind.LeftHand => "Hand_L",
        AlsPoseMaskKind.RightHand => "hand_r",
        AlsPoseMaskKind.Pelvis => "Pelvis",
        AlsPoseMaskKind.LeftLeg => "Thigh_L",
        AlsPoseMaskKind.RightLeg => "Thigh_R",
        AlsPoseMaskKind.LeftFoot => "Foot_L",
        AlsPoseMaskKind.RightFoot => "Foot_R",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static int ExactBoneId(AlsSkeletonDefinition skeleton, string name, string path)
    {
        var matches = skeleton.LogicalBones.Where(value => string.Equals(value.Name, name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw Failure("ALSPOSE037", path, "Bone root must resolve by exact canonical logical name.", "one exact bone", name, skeleton.AssetId);
        return matches[0].LogicalId;
    }

    private static bool IsDescendant(AlsSkeletonDefinition skeleton, int boneId, int ancestorId)
    {
        for (var current = boneId; current >= 0; current = skeleton.LogicalBones[current].ParentLogicalId)
            if (current == ancestorId) return true;
        return false;
    }

    private static void RequireCompleteTurns(HashSet<(AlsPoseStance Stance, sbyte Direction, short Angle)> combinations)
    {
        foreach (var stance in Enum.GetValues<AlsPoseStance>()) foreach (var direction in new sbyte[] { -1, 1 }) foreach (var angle in new short[] { 90, 180 })
            if (!combinations.Contains((stance, direction, angle))) throw Failure("ALSPOSE038", "$.turns", "Turn profiles do not cover every stance, direction and angle.");
    }

    private static void RequirePairedRoots(AlsSkeletonDefinition skeleton, IReadOnlyDictionary<AlsPoseMaskKind, int> roots, AlsPoseMaskKind left, AlsPoseMaskKind right) =>
        RequirePair(skeleton, roots[left], roots[right], "$.masks");

    private static void RequirePair(AlsSkeletonDefinition skeleton, int leftId, int rightId, string path)
    {
        var left = skeleton.LogicalBones[leftId];
        var right = skeleton.LogicalBones[rightId];
        if (leftId == rightId || !SideNormalized(left.Name, "_l").Equals(SideNormalized(right.Name, "_r"), StringComparison.OrdinalIgnoreCase))
            throw Failure("ALSPOSE039", path, "Left/right bone roots are asymmetric.", assetId: skeleton.AssetId);
        var leftParent = left.ParentLogicalId < 0 ? "" : skeleton.LogicalBones[left.ParentLogicalId].Name;
        var rightParent = right.ParentLogicalId < 0 ? "" : skeleton.LogicalBones[right.ParentLogicalId].Name;
        if (!SideNormalized(leftParent, "_l").Equals(SideNormalized(rightParent, "_r"), StringComparison.OrdinalIgnoreCase))
            throw Failure("ALSPOSE039", path, "Left/right bone root parents are asymmetric.", assetId: skeleton.AssetId);
    }

    private static string SideNormalized(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;

    private static void RequireFootMask(AlsLayerMaskProfile masks, AlsPoseMaskKind kind, int rootId, string path)
    {
        var entry = masks.Entries.Single(value => value.Kind == kind);
        if (entry.RootBoneId != rootId) throw Failure("ALSPOSE040", path, "Foot setting root must match the compiled mask root.");
    }

    private static AlsCompilationException Failure(string code, string path, string message, string? expected = null, string? actual = null, string? assetId = null) =>
        new([new AlsValidationIssue(code, assetId, path, message, expected, actual)]);
}
