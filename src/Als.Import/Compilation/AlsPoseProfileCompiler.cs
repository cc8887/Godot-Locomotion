using System.Text.Json;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsPoseProfileCompiler
{
    private const int SchemaVersion = 1;
    private const int AimAdditiveType = 2;
    private const string AimOffsetObjectPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look.ALS_N_Look";
    private const string AimDownObjectPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep";
    private const string AimForwardObjectPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_F_Sweep.ALS_N_Look_F_Sweep";
    private const string AimUpObjectPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_U_Sweep.ALS_N_Look_U_Sweep";
    private const string TurnInPlaceRoot = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/";
    private static readonly AlsBlendParameterDefinition[] AimParameters =
    [
        new("Pitch", -90f, 90f, 4),
        new("None", 0f, 100f, 4),
        new("None", 0f, 100f, 4),
    ];
    private static readonly float[][] AimSampleCoordinates =
    [
        [-90f, 0f, 0f],
        [0f, 0f, 0f],
        [90f, 0f, 0f],
    ];
    private static readonly IReadOnlyDictionary<(AlsPoseStance Stance, sbyte Direction, short Degrees), string> TurnObjectPaths =
        new Dictionary<(AlsPoseStance, sbyte, short), string>
        {
            [(AlsPoseStance.Standing, -1, 90)] = TurnInPlaceRoot + "ALS_N_TurnIP_L90.ALS_N_TurnIP_L90",
            [(AlsPoseStance.Standing, 1, 90)] = TurnInPlaceRoot + "ALS_N_TurnIP_R90.ALS_N_TurnIP_R90",
            [(AlsPoseStance.Standing, -1, 180)] = TurnInPlaceRoot + "ALS_N_TurnIP_L180.ALS_N_TurnIP_L180",
            [(AlsPoseStance.Standing, 1, 180)] = TurnInPlaceRoot + "ALS_N_TurnIP_R180.ALS_N_TurnIP_R180",
            [(AlsPoseStance.Crouching, -1, 90)] = TurnInPlaceRoot + "ALS_CLF_TurnIP_L90.ALS_CLF_TurnIP_L90",
            [(AlsPoseStance.Crouching, 1, 90)] = TurnInPlaceRoot + "ALS_CLF_TurnIP_R90.ALS_CLF_TurnIP_R90",
            [(AlsPoseStance.Crouching, -1, 180)] = TurnInPlaceRoot + "ALS_CLF_TurnIP_L180.ALS_CLF_TurnIP_L180",
            [(AlsPoseStance.Crouching, 1, 180)] = TurnInPlaceRoot + "ALS_CLF_TurnIP_R180.ALS_CLF_TurnIP_R180",
        };
    private static readonly IReadOnlyDictionary<(AlsPoseStance Stance, sbyte Direction), string> RotateObjectPaths =
        new Dictionary<(AlsPoseStance, sbyte), string>
        {
            [(AlsPoseStance.Standing, -1)] = TurnInPlaceRoot + "ALS_N_Rotate_L90.ALS_N_Rotate_L90",
            [(AlsPoseStance.Standing, 1)] = TurnInPlaceRoot + "ALS_N_Rotate_R90.ALS_N_Rotate_R90",
            [(AlsPoseStance.Crouching, -1)] = TurnInPlaceRoot + "ALS_CLF_Rotate_L90.ALS_CLF_Rotate_L90",
            [(AlsPoseStance.Crouching, 1)] = TurnInPlaceRoot + "ALS_CLF_Rotate_R90.ALS_CLF_Rotate_R90",
        };
    private static readonly string[] RootProperties = ["schemaVersion", "skeleton", "aim", "turns", "rotates", "masks", "feet"];
    private static readonly string[] AimProperties = ["aimOffset", "down", "forward", "up"];
    private static readonly string[] TurnProperties = ["animation", "stance", "direction", "nominalDegrees", "basePlayRate", "blendSeconds", "scaleAngle"];
    private static readonly string[] RotateProperties = ["animation", "stance", "direction"];
    private static readonly string[] MaskProperties = ["kind", "root", "boundaries"];
    private static readonly string[] FeetProperties = ["leftLegRoot", "rightLegRoot", "leftFootRoot", "rightFootRoot", "traceUpMeters", "traceDownMeters", "footHeightMeters", "maxPelvisCorrectionMeters", "pelvisUpHalfLifeSeconds", "pelvisDownHalfLifeSeconds", "positionHalfLifeSeconds", "rotationHalfLifeSeconds", "lockReleaseHalfLifeSeconds", "maxLegReachMeters", "capsuleHalfHeightSource", "maxThighAngleDegrees", "maxFootAngleDegrees", "platformTeleportDistanceMeters", "platformTeleportAngleDegrees", "lockWeightEpsilon", "curves", "ikStateDefaults"];
    private static readonly string[] FootCurveProperties = ["leftLock", "rightLock", "missingLockDefault"];
    private static readonly string[] FootIkDefaultProperties = ["grounded", "jumpStart", "fallLoop", "landRecovery"];

    public static AlsPoseAnimationProfile Compile(
        string json,
        AlsAnimationSetDefinition animationSet) => throw Failure(
            "ALSPOSE051",
            "$.locomotionProfile",
            "Runtime pose compilation requires the three-parameter overload with a locomotion profile.");

    public static AlsPoseAnimationProfile Compile(
        string json,
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotionProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(locomotionProfile);
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
            if (locomotionProfile.SkeletonId != skeletonId)
            {
                throw Failure("ALSPOSE043", "$.skeleton",
                    "Locomotion and pose profiles must target the same skeleton.",
                    skeletonId.ToString(), locomotionProfile.SkeletonId.ToString(), skeletonStableId);
            }
            var (feet, footCurves) = CompileFeet(
                root["feet"], animationSet, skeleton, masks,
                locomotionProfile, turns, rotates);
            return new AlsPoseAnimationProfile(version, skeletonId, aim, turns, rotates, masks, feet)
            {
                FootCurves = footCurves,
            };
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
        var aimOffset = set.AimOffsets[aimOffsetId];
        RequireExactObjectPath(aimOffset.StableId, aimOffset.ObjectPath, AimOffsetObjectPath, "$.aim.aimOffset", "aim offset");
        RequireAimOffsetContract(aimOffset);

        var sweepIds = new HashSet<int>();
        RequireUniqueAnimation(sweepIds, set.Animations[down], "$.aim.down", "aim sweep");
        RequireUniqueAnimation(sweepIds, set.Animations[forward], "$.aim.forward", "aim sweep");
        RequireUniqueAnimation(sweepIds, set.Animations[up], "$.aim.up", "aim sweep");
        RequireExactObjectPath(set.Animations[down], AimDownObjectPath, "$.aim.down", "aim sweep");
        RequireExactObjectPath(set.Animations[forward], AimForwardObjectPath, "$.aim.forward", "aim sweep");
        RequireExactObjectPath(set.Animations[up], AimUpObjectPath, "$.aim.up", "aim sweep");
        RequireAimSampleRole(set, aimOffset, [-90f, 0f, 0f], down, "$.aim.down", AimDownObjectPath);
        RequireAimSampleRole(set, aimOffset, [0f, 0f, 0f], forward, "$.aim.forward", AimForwardObjectPath);
        RequireAimSampleRole(set, aimOffset, [90f, 0f, 0f], up, "$.aim.up", AimUpObjectPath);

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
        var animationIds = new HashSet<int>();
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
            var animation = set.Animations[animationId];
            RequireUniqueAnimation(animationIds, animation, $"{path}.animation", "turn");
            RequireExactObjectPath(animation, TurnObjectPaths[(stance, direction, (short)nominal)], $"{path}.animation", "turn");
            if (!combinations.Add((stance, direction, (short)nominal))) throw Failure("ALSPOSE010", path, "Duplicate turn stance, direction and angle combination.");
            result[index] = new AlsTurnProfile(animationId, CanonicalCurveId(animation, $"{path}.animation"), stance, direction, (short)nominal, rate, blend, 1);
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
        var animationIds = new HashSet<int>();
        for (var index = 0; index < values.Length; index++)
        {
            var path = $"$.rotates[{index}]";
            var source = Object(values[index], path, RotateProperties);
            var animationId = ResolveAnimation(set, source["animation"], $"{path}.animation", skeletonId);
            var stance = Stance(source["stance"], $"{path}.stance");
            var direction = Direction(source["direction"], $"{path}.direction");
            var animation = set.Animations[animationId];
            RequireUniqueAnimation(animationIds, animation, $"{path}.animation", "rotate");
            RequireExactObjectPath(animation, RotateObjectPaths[(stance, direction)], $"{path}.animation", "rotate");
            if (!combinations.Add((stance, direction))) throw Failure("ALSPOSE012", path, "Duplicate rotate stance and direction combination.");
            result[index] = new AlsRotateProfile(animationId, CanonicalCurveId(animation, $"{path}.animation"), stance, direction);
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

    private static (AlsFootPlacementSettings Settings, AlsFootCurveProfile Curves) CompileFeet(
        JsonElement element,
        AlsAnimationSetDefinition set,
        AlsSkeletonDefinition skeleton,
        AlsLayerMaskProfile masks,
        AlsLocomotionAnimationProfile? locomotion,
        AlsTurnProfile[] turns,
        AlsRotateProfile[] rotates)
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
        var capsuleSource = String(
            source["capsuleHalfHeightSource"], "$.feet.capsuleHalfHeightSource");
        if (!string.Equals(capsuleSource, "characterController", StringComparison.Ordinal))
        {
            throw Failure("ALSPOSE044", "$.feet.capsuleHalfHeightSource",
                "Capsule half-height must be supplied by the character controller.",
                "characterController", capsuleSource);
        }
        var settings = new AlsFootPlacementSettings(leftLeg, rightLeg, leftFoot, rightFoot,
            Ranged(source["traceUpMeters"], "$.feet.traceUpMeters", 0f, 5f),
            Ranged(source["traceDownMeters"], "$.feet.traceDownMeters", 0f, 5f),
            Ranged(source["footHeightMeters"], "$.feet.footHeightMeters", 0f, 1f, allowZero: true),
            Ranged(source["maxPelvisCorrectionMeters"], "$.feet.maxPelvisCorrectionMeters", 0f, 2f),
            Ranged(source["positionHalfLifeSeconds"], "$.feet.positionHalfLifeSeconds", 0f, 5f),
            Ranged(source["rotationHalfLifeSeconds"], "$.feet.rotationHalfLifeSeconds", 0f, 5f),
            Ranged(source["lockReleaseHalfLifeSeconds"], "$.feet.lockReleaseHalfLifeSeconds", 0f, 5f))
        {
            PelvisUpHalfLifeSeconds = Ranged(source["pelvisUpHalfLifeSeconds"], "$.feet.pelvisUpHalfLifeSeconds", 0f, 5f, allowZero: true),
            PelvisDownHalfLifeSeconds = Ranged(source["pelvisDownHalfLifeSeconds"], "$.feet.pelvisDownHalfLifeSeconds", 0f, 5f, allowZero: true),
            MaximumLegReachMeters = Ranged(source["maxLegReachMeters"], "$.feet.maxLegReachMeters", 0f, 5f),
            CapsuleHalfHeightSource = AlsCapsuleHalfHeightSource.CharacterController,
            MaximumThighAngleRadians = DegreesToRadians(
                Ranged(source["maxThighAngleDegrees"], "$.feet.maxThighAngleDegrees", 0f, 180f, allowZero: true)),
            MaximumFootAngleRadians = DegreesToRadians(
                Ranged(source["maxFootAngleDegrees"], "$.feet.maxFootAngleDegrees", 0f, 180f, allowZero: true)),
            PlatformTeleportDistanceMeters = Ranged(source["platformTeleportDistanceMeters"], "$.feet.platformTeleportDistanceMeters", 0f, 100f),
            PlatformTeleportAngleRadians = DegreesToRadians(
                Ranged(source["platformTeleportAngleDegrees"], "$.feet.platformTeleportAngleDegrees", 0f, 180f, allowZero: true)),
            LockWeightEpsilon = Ranged(source["lockWeightEpsilon"], "$.feet.lockWeightEpsilon", 0f, 1f),
        };

        var curves = Object(source["curves"], "$.feet.curves", FootCurveProperties);
        var leftCurveName = String(curves["leftLock"], "$.feet.curves.leftLock");
        var rightCurveName = String(curves["rightLock"], "$.feet.curves.rightLock");
        var missingLockDefault = Unit(curves["missingLockDefault"], "$.feet.curves.missingLockDefault");
        var ikDefaults = Object(
            source["ikStateDefaults"], "$.feet.ikStateDefaults", FootIkDefaultProperties);

        var reachable = new List<int>(34);
        var unique = new HashSet<int>();
        if (locomotion is not null)
        {
            Add(locomotion.StandingIdleAnimationId);
            Add(locomotion.CrouchingIdleAnimationId);
            foreach (var sample in locomotion.StandingSamples) Add(sample.AnimationId);
            foreach (var sample in locomotion.CrouchingSamples) Add(sample.AnimationId);
            Add(locomotion.JumpStartAnimationId);
            Add(locomotion.FallLoopAnimationId);
            Add(locomotion.LandAnimationId);
        }
        foreach (var turn in turns) Add(turn.AnimationId);
        foreach (var rotate in rotates) Add(rotate.AnimationId);

        var bindings = new AlsFootCurveBinding[reachable.Count];
        var foundLeft = false;
        var foundRight = false;
        for (var index = 0; index < reachable.Count; index++)
        {
            var animation = set.Animations[reachable[index]];
            var leftCurveId = SourceCurveId(animation, leftCurveName, out var hasLeft);
            var rightCurveId = SourceCurveId(animation, rightCurveName, out var hasRight);
            foundLeft |= hasLeft;
            foundRight |= hasRight;
            bindings[index] = new AlsFootCurveBinding(
                animation.Id,
                leftCurveId,
                rightCurveId,
                hasLeft ? 0f : missingLockDefault,
                hasRight ? 0f : missingLockDefault);
        }
        if (!foundLeft)
        {
            throw Failure("ALSPOSE045", "$.feet.curves.leftLock",
                "Foot lock curve name is absent from every production-reachable animation.",
                actual: leftCurveName);
        }
        if (!foundRight)
        {
            throw Failure("ALSPOSE045", "$.feet.curves.rightLock",
                "Foot lock curve name is absent from every production-reachable animation.",
                actual: rightCurveName);
        }

        var footCurves = new AlsFootCurveProfile(
            Unit(ikDefaults["grounded"], "$.feet.ikStateDefaults.grounded"),
            Unit(ikDefaults["jumpStart"], "$.feet.ikStateDefaults.jumpStart"),
            Unit(ikDefaults["fallLoop"], "$.feet.ikStateDefaults.fallLoop"),
            Unit(ikDefaults["landRecovery"], "$.feet.ikStateDefaults.landRecovery"),
            bindings);
        return (settings, footCurves);

        void Add(int animationId)
        {
            if (unique.Add(animationId)) reachable.Add(animationId);
        }
    }

    private static int SourceCurveId(
        AlsAnimationDefinition animation,
        string sourceName,
        out bool found)
    {
        var id = -1;
        found = false;
        foreach (var curve in animation.Curves)
        {
            if (!string.Equals(curve.SourceName, sourceName, StringComparison.Ordinal)) continue;
            if (found)
            {
                throw Failure("ALSPOSE046", "$.feet.curves",
                    "A production animation contains duplicate source foot curves.",
                    assetId: animation.StableId);
            }
            found = true;
            id = curve.CurveId;
        }
        return id;
    }

    private static float Unit(JsonElement element, string path) =>
        Ranged(element, path, 0f, 1f, allowZero: true);

    private static float DegreesToRadians(float value) => value * MathF.PI / 180f;

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

    private static void RequireUniqueAnimation(HashSet<int> ids, AlsAnimationDefinition animation, string path, string role)
    {
        if (!ids.Add(animation.Id))
            throw Failure("ALSPOSE042", path, $"Duplicate animation ID is not allowed across {role} slots.", "unique animation stable ID", animation.StableId, animation.StableId);
    }

    private static void RequireExactObjectPath(AlsAnimationDefinition animation, string expectedPath, string profilePath, string role) =>
        RequireExactObjectPath(animation.StableId, animation.ObjectPath, expectedPath, profilePath, role);

    private static void RequireExactObjectPath(string stableId, string objectPath, string expectedPath, string profilePath, string role)
    {
        if (!string.Equals(objectPath, expectedPath, StringComparison.Ordinal))
            throw Failure("ALSPOSE043", profilePath, $"The {role} slot must resolve to its exact ALS v4 object path.", expectedPath, objectPath, stableId);
    }

    private static void RequireAimOffsetContract(AlsBlendDefinition aimOffset)
    {
        if (aimOffset.Parameters.Length != AimParameters.Length)
        {
            var index = Math.Min(aimOffset.Parameters.Length, AimParameters.Length);
            throw Failure("ALSPOSE046", $"$.aim.aimOffset.parameters[{index}]",
                "AimOffset must contain exactly three parameter axes.", AimParameters.Length.ToString(),
                aimOffset.Parameters.Length.ToString(), aimOffset.StableId);
        }

        for (var index = 0; index < AimParameters.Length; index++)
        {
            var expected = AimParameters[index];
            var actual = aimOffset.Parameters[index];
            RequireAimParameterField(index, "name", expected.Name, actual.Name, aimOffset.StableId);
            RequireAimParameterField(index, "minimum", expected.Minimum, actual.Minimum, aimOffset.StableId);
            RequireAimParameterField(index, "maximum", expected.Maximum, actual.Maximum, aimOffset.StableId);
            RequireAimParameterField(index, "gridDivisions", expected.GridDivisions, actual.GridDivisions, aimOffset.StableId);
        }

        if (aimOffset.Samples.Length != AimSampleCoordinates.Length)
        {
            var index = Math.Min(aimOffset.Samples.Length, AimSampleCoordinates.Length);
            throw Failure("ALSPOSE047", $"$.aim.aimOffset.samples[{index}].sampleValue",
                "AimOffset must contain exactly the down, forward and up samples.", AimSampleCoordinates.Length.ToString(),
                aimOffset.Samples.Length.ToString(), aimOffset.StableId);
        }

        var coordinateRoles = new HashSet<int>();
        for (var index = 0; index < aimOffset.Samples.Length; index++)
        {
            var sample = aimOffset.Samples[index];
            var coordinateRole = System.Array.FindIndex(AimSampleCoordinates, expected =>
                sample.SampleValue is { Length: 3 } && sample.SampleValue.All(float.IsFinite) &&
                sample.SampleValue.SequenceEqual(expected));
            if (coordinateRole < 0 || !coordinateRoles.Add(coordinateRole))
                throw Failure("ALSPOSE048", $"$.aim.aimOffset.samples[{index}].sampleValue",
                    "AimOffset sample coordinates must be the unique down, forward and up values.",
                    "[-90,0,0], [0,0,0] or [90,0,0]", FormatCoordinate(sample.SampleValue), aimOffset.StableId);
            if (!float.IsFinite(sample.RateScale) || sample.RateScale != 1f)
                throw Failure("ALSPOSE049", $"$.aim.aimOffset.samples[{index}].rateScale",
                    "AimOffset sample rateScale must be finite and exactly 1.", "1", sample.RateScale.ToString(System.Globalization.CultureInfo.InvariantCulture), aimOffset.StableId);
        }
    }

    private static void RequireAimParameterField<T>(int index, string field, T expected, T actual, string assetId)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
            throw Failure("ALSPOSE050", $"$.aim.aimOffset.parameters[{index}].{field}",
                "AimOffset parameter axis differs from the ALS v4 contract.", expected.ToString(), actual.ToString(), assetId);
    }

    private static string FormatCoordinate(float[]? coordinate) => coordinate is null
        ? "null"
        : $"[{string.Join(',', coordinate.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]";

    private static void RequireAimSampleRole(
        AlsAnimationSetDefinition set,
        AlsBlendDefinition aimOffset,
        float[] coordinate,
        int expectedAnimationId,
        string profilePath,
        string expectedObjectPath)
    {
        var matches = aimOffset.Samples.Where(sample =>
            sample.SampleValue is { Length: 3 } && sample.SampleValue.SequenceEqual(coordinate)).ToArray();
        if (matches.Length != 1)
            throw Failure("ALSPOSE044", profilePath,
                "AimOffset must contain exactly one three-dimensional sample at the slot coordinate.",
                expectedObjectPath, set.Animations[expectedAnimationId].StableId, aimOffset.StableId);

        var actualAnimationId = matches[0].AnimationId;
        if (actualAnimationId != expectedAnimationId)
        {
            var actualStableId = (uint)actualAnimationId < (uint)set.Animations.Length
                ? set.Animations[actualAnimationId].StableId
                : actualAnimationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw Failure("ALSPOSE045", profilePath,
                "AimOffset sample coordinate references the wrong animation for this semantic slot.",
                expectedObjectPath, actualStableId, actualStableId);
        }
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
