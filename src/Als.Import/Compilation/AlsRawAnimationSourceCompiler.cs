using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Verifies a source export against its requested graph/manifest closure, then
/// compiles exact raw atoms. No evaluated pose or FBX rotation curve is a production source.</summary>
public static class AlsRawAnimationSourceCompiler
{
    private const string Source = "AnimDataModel.BoneAnimationTracks.InternalTrackData";

    public static AlsRawAnimationSourceBank Compile(string indexJson, AlsAnimationSetDefinition set,
        string bindingDigest, int playerCount, int sampleCount, ReadOnlySpan<int> rootAnimationIds,
        Func<string, byte[]> readAssetBytes)
    {
        ArgumentNullException.ThrowIfNull(set); ArgumentNullException.ThrowIfNull(readAssetBytes);
        Require(!string.IsNullOrWhiteSpace(bindingDigest) && playerCount > 0 && sampleCount > 0 && !rootAnimationIds.IsEmpty,
            "The real source request must include binding identity, player/sample counts and root resources.");
        var roots = rootAnimationIds.ToArray();
        Require(roots.Distinct().Count() == roots.Length, "Duplicate requested root animation.");
        var animationByPath = new Dictionary<string, AlsAnimationDefinition>(StringComparer.Ordinal);
        var animationByAssetId = new Dictionary<string, AlsAnimationDefinition>(StringComparer.Ordinal);
        for (var index = 0; index < set.Animations.Length; index++)
        {
            var animation = set.Animations[index];
            Require(animation.Id == index && IsAssetId(animation.StableId) &&
                animationByPath.TryAdd(animation.ObjectPath, animation) && animationByAssetId.TryAdd(animation.StableId, animation),
                "Ambiguous manifest source identity.");
        }
        foreach (var id in roots) Require((uint)id < (uint)set.Animations.Length, "Invalid requested animation ID.");
        using var document = JsonDocument.Parse(indexJson); var root = document.RootElement;
        Fields(root, ["schemaVersion", "source", "request", "skeletons", "assets"]);
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source,
            "Unsupported raw source provenance/schema.");
        ValidateRequest(root.GetProperty("request"), set, bindingDigest, playerCount, sampleCount, roots);

        var skeletons = new Dictionary<int, AlsRawAnimationSkeletonDefinition>();
        foreach (var value in root.GetProperty("skeletons").EnumerateArray())
        {
            var skeleton = CompileSkeleton(value, set);
            Require(skeletons.TryAdd(skeleton.SkeletonId, skeleton), "Duplicate source skeleton.");
        }
        var sources = new Dictionary<int, AlsRawAnimationSourceDefinition>();
        var declaredOwners = new Dictionary<int, HashSet<int>>();
        foreach (var row in root.GetProperty("assets").EnumerateArray())
        {
            Fields(row, ["assetId", "source", "file", "sha256"], ["dependencyOf"]);
            var assetId = Text(row, "assetId");
            Require(animationByAssetId.TryGetValue(assetId, out var animation) && animation.ObjectPath == Text(row, "source"),
                "Unknown or mismatched raw source resource.");
            var knownAnimation = animation!;
            Require(!sources.ContainsKey(knownAnimation.Id), "Duplicate raw source resource.");
            Require(skeletons.TryGetValue(knownAnimation.SkeletonId, out var skeleton), "Missing raw source skeleton.");
            var file = Text(row, "file");
            // Equality to this canonical relative name also excludes traversal, alternate separators,
            // absolute paths, empty components and ambiguous case before invoking the reader.
            Require(file == "raw_sequences/" + assetId + ".json", "Raw source file must use its canonical asset path.");
            var hash = Text(row, "sha256");
            Require(hash.Length == 64 && hash.All(Uri.IsHexDigit), "Invalid raw source SHA256.");
            var bytes = readAssetBytes(file) ?? throw new ArgumentException("Missing raw source bytes: " + file);
            Require(Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase),
                "Raw source bytes differ from their indexed SHA256: " + file);
            using var assetDocument = JsonDocument.Parse(bytes);
            var source = CompileSource(assetDocument.RootElement, knownAnimation, skeleton!, animationByPath, file, hash);
            sources.Add(knownAnimation.Id, source);
            var owners = new HashSet<int>();
            if (row.TryGetProperty("dependencyOf", out var ownerRows))
            {
                foreach (var ownerRow in ownerRows.EnumerateArray())
                {
                    var ownerId = String(ownerRow);
                    Require(animationByAssetId.TryGetValue(ownerId, out var owner) && owners.Add(owner.Id),
                        "Duplicate or unknown raw source dependency owner.");
                }
                Require(owners.Count != 0, "An exported dependencyOf field must identify its owners.");
            }
            declaredOwners.Add(knownAnimation.Id, owners);
        }
        ValidateClosure(roots, sources, declaredOwners);
        var usedSkeletons = sources.Values.Select(s => s.PoseData.Identity.SkeletonId).ToHashSet();
        Require(usedSkeletons.SetEquals(skeletons.Keys), "Raw source index has missing or unrelated skeletons.");
        return new(set.DefinitionDigest, bindingDigest, playerCount, sampleCount, roots,
            skeletons.Values.OrderBy(s => s.SkeletonId).ToArray(), sources.Values.OrderBy(s => s.PoseData.Identity.AnimationId).ToArray());
    }

    private static void ValidateRequest(JsonElement request, AlsAnimationSetDefinition set, string bindingDigest,
        int playerCount, int sampleCount, int[] roots)
    {
        Fields(request, ["definitionDigest", "bindingDigest", "players", "samples", "rootAssets"]);
        Require(Text(request, "definitionDigest") == set.DefinitionDigest && Text(request, "bindingDigest") == bindingDigest &&
            request.GetProperty("players").GetInt32() == playerCount && request.GetProperty("samples").GetInt32() == sampleCount,
            "Raw source export belongs to another manifest or player/sample binding.");
        var expected = roots.Select(id => set.Animations[id].StableId).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var byId = roots.ToDictionary(id => set.Animations[id].StableId, id => set.Animations[id], StringComparer.Ordinal);
        foreach (var row in request.GetProperty("rootAssets").EnumerateArray())
        {
            Fields(row, ["assetId", "source"]); var id = Text(row, "assetId");
            Require(byId.TryGetValue(id, out var animation) && animation.ObjectPath == Text(row, "source") && seen.Add(id),
                "Raw export request has an unknown, duplicate or mismatched root.");
        }
        Require(seen.SetEquals(expected), "Raw export request is missing a graph source.");
    }

    private static AlsRawAnimationSkeletonDefinition CompileSkeleton(JsonElement value, AlsAnimationSetDefinition set)
    {
        Fields(value, ["source", "rawBoneNames", "rawParents", "logicalBoneNames", "logicalParents", "logicalToPhysical",
            "virtualBones", "referencePose", "translationRetargetModes"]);
        var source = Text(value, "source"); var skeletonId = Array.FindIndex(set.Skeletons, s => s.ObjectPath == source);
        Require(skeletonId >= 0 && set.Skeletons.Count(s => s.ObjectPath == source) == 1, "Unknown or ambiguous native skeleton.");
        var expected = set.Skeletons[skeletonId];
        var rawNames = Strings(value.GetProperty("rawBoneNames")); var rawParents = Integers(value.GetProperty("rawParents"));
        var logicalNames = Strings(value.GetProperty("logicalBoneNames")); var logicalParents = Integers(value.GetProperty("logicalParents"));
        var mapping = Integers(value.GetProperty("logicalToPhysical")); var reference = Poses(value.GetProperty("referencePose"));
        Require(rawNames.Length > 0 && rawNames.Length == expected.PhysicalBones.Length && logicalNames.Length == expected.LogicalBones.Length &&
            rawParents.Length == rawNames.Length && logicalParents.Length == logicalNames.Length && mapping.Length == logicalNames.Length &&
            reference.Length == logicalNames.Length && logicalNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == logicalNames.Length &&
            rawNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == rawNames.Length,
            "Native skeleton arrays differ from the manifest layout.");
        var inverse = new int[rawNames.Length]; Array.Fill(inverse, -1);
        for (var logical = 0; logical < logicalNames.Length; logical++)
        {
            var bone = expected.LogicalBones[logical]; var physical = mapping[logical];
            Require(bone.LogicalId == logical && string.Equals(logicalNames[logical], bone.Name, StringComparison.OrdinalIgnoreCase) &&
                logicalParents[logical] == bone.ParentLogicalId && logicalParents[logical] >= -1 && logicalParents[logical] < logical &&
                physical == expected.LogicalToPhysical[logical] && physical == bone.PhysicalId && physical >= -1,
                "Native logical bone identity, parent or mapping changed.");
            var manifestReference = AlsFbxBonePoseSpace.FromCanonical(new(bone.Translation, bone.Rotation, bone.Scale));
            Require(SamePose(reference[logical], manifestReference, 2e-5f), "Native reference pose differs from its manifest skeleton.");
            if (physical < 0) continue;
            Require(physical < inverse.Length && inverse[physical] < 0, "Native physical mapping is not one-to-one.");
            inverse[physical] = logical;
        }
        for (var physical = 0; physical < rawNames.Length; physical++)
        {
            var logical = inverse[physical]; var bone = expected.PhysicalBones[physical];
            Require(logical >= 0 && bone.PhysicalId == physical && bone.LogicalId == logical && expected.PhysicalToLogical[physical] == logical &&
                string.Equals(rawNames[physical], logicalNames[logical], StringComparison.OrdinalIgnoreCase) &&
                rawParents[physical] == bone.ParentPhysicalId && rawParents[physical] >= -1 && rawParents[physical] < physical,
                "Native raw bone order or parent differs from the manifest.");
        }
        var virtualRows = value.GetProperty("virtualBones"); var virtualBones = new AlsLogicalVirtualBone[virtualRows.GetArrayLength()];
        Require(virtualBones.Length == expected.VirtualBones.Length, "Native virtual-bone definitions are incomplete.");
        for (var index = 0; index < virtualBones.Length; index++)
        {
            var row = virtualRows[index]; Fields(row, ["bone", "source", "target"]);
            var vb = new AlsLogicalVirtualBone(row.GetProperty("bone").GetInt32(), row.GetProperty("source").GetInt32(), row.GetProperty("target").GetInt32());
            var original = expected.VirtualBones[index];
            Require(vb.Bone == original.LogicalBoneId && vb.Source == original.SourceLogicalBoneId && vb.Target == original.TargetLogicalBoneId,
                "Native virtual-bone source/target or generation order changed.");
            virtualBones[index] = vb;
        }
        // This validates the complete physical/virtual topology, including virtual sources.
        _ = new AlsLogicalPoseExpansion(logicalParents, mapping, reference, virtualBones);
        var modeRows = value.GetProperty("translationRetargetModes");
        Require(modeRows.GetArrayLength() == rawNames.Length, "Native retarget policy does not cover each raw bone.");
        var modes = new AlsRawAnimationRetargetMode[rawNames.Length];
        for (var physical = 0; physical < modes.Length; physical++)
        {
            modes[physical] = String(modeRows[physical]) switch
            {
                "Animation" => AlsRawAnimationRetargetMode.Animation,
                "Skeleton" => AlsRawAnimationRetargetMode.Skeleton,
                "AnimationScaled" => AlsRawAnimationRetargetMode.AnimationScaled,
                "AnimationRelative" => AlsRawAnimationRetargetMode.AnimationRelative,
                "OrientAndScale" => AlsRawAnimationRetargetMode.OrientAndScale,
                var mode => throw new ArgumentException("Raw source retarget mode requires an unimplemented native operator: " + mode)
            };
        }
        return new(skeletonId, expected.AssetId, source, rawNames, rawParents, logicalNames, logicalParents,
            mapping, inverse, virtualBones, reference, modes, PrecisePoses(value.GetProperty("referencePose")));
    }

    private static AlsRawAnimationSourceDefinition CompileSource(JsonElement value, AlsAnimationDefinition animation,
        AlsRawAnimationSkeletonDefinition skeleton, Dictionary<string, AlsAnimationDefinition> animationByPath, string file, string hash)
    {
        Fields(value, ["source", "skeletonSource", "frameRateNumerator", "frameRateDenominator", "sampledKeyCount", "playLength", "tracks", "evaluation"]);
        Require(Text(value, "source") == animation.ObjectPath && Text(value, "skeletonSource") == skeleton.Source,
            "Raw sequence file has the wrong resource or skeleton owner.");
        var numerator = value.GetProperty("frameRateNumerator").GetInt32(); var denominator = value.GetProperty("frameRateDenominator").GetInt32();
        var keyCount = value.GetProperty("sampledKeyCount").GetInt32(); var length = value.GetProperty("playLength").GetDouble();
        Require(numerator > 0 && denominator > 0 && keyCount > 0 && numerator == animation.FrameRateNumerator &&
            denominator == animation.FrameRateDenominator && keyCount == animation.SampledKeyCount && double.IsFinite(length) &&
            length >= 0 && (float)length == animation.PlayLength, "Native source timing differs from its manifest.");
        var expectedDuration = (keyCount - 1) * (double)denominator / numerator;
        Require(Math.Abs(length - expectedDuration) <= Math.Max(1e-8, expectedDuration * 1e-6), "Native source timing is inconsistent.");
        var policy = CompilePolicy(value.GetProperty("evaluation"), animation, skeleton, animationByPath);
        var boneIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var logical = 0; logical < skeleton.LogicalBoneCount; logical++) boneIds.Add(skeleton.LogicalBoneNames[logical], logical);
        var virtualIds = skeleton.VirtualBones.ToArray().Select(v => v.Bone).ToArray();
        var virtualIndex = new int[skeleton.LogicalBoneCount]; Array.Fill(virtualIndex, -1);
        for (var index = 0; index < virtualIds.Length; index++) virtualIndex[virtualIds[index]] = index;
        var presence = new bool[skeleton.LogicalBoneCount];
        var physicalKeys = new AlsLocalPose[checked(keyCount * skeleton.PhysicalBoneCount)];
        var virtualKeys = new AlsLocalPose[checked(keyCount * virtualIds.Length)];
        foreach (var track in value.GetProperty("tracks").EnumerateArray())
        {
            Fields(track, ["bone", "positions", "rotations", "scales"]); var bone = Text(track, "bone");
            Require(boneIds.TryGetValue(bone, out var logical) && !presence[logical], "Unknown or duplicate native bone track: " + bone);
            presence[logical] = true;
            var positions = Channel(track, "positions", 3, keyCount, false);
            var rotations = Channel(track, "rotations", 4, keyCount, false);
            var scales = Channel(track, "scales", 3, keyCount, true);
            var physical = skeleton.LogicalToPhysical[logical];
            for (var key = 0; key < keyCount; key++)
            {
                var pose = ConvertPose(positions[Math.Min(key, positions.Length - 1)], rotations[Math.Min(key, rotations.Length - 1)],
                    scales.Length == 0 ? [1, 1, 1] : scales[Math.Min(key, scales.Length - 1)]);
                if (physical >= 0) physicalKeys[key * skeleton.PhysicalBoneCount + physical] = pose;
                else virtualKeys[key * virtualIds.Length + virtualIndex[logical]] = pose;
            }
        }
        var data = new AlsRawAnimationPoseData(new(animation.Id, animation.StableId, animation.ObjectPath, animation.SkeletonId),
            numerator, denominator, keyCount, length, policy.Interpolation, skeleton.LogicalToPhysical, virtualIds, presence, physicalKeys, virtualKeys);
        return new(data, policy, file, hash);
    }

    private static AlsRawAnimationEvaluationPolicy CompilePolicy(JsonElement value, AlsAnimationDefinition animation,
        AlsRawAnimationSkeletonDefinition skeleton, Dictionary<string, AlsAnimationDefinition> animationByPath)
    {
        Fields(value, ["interpolation", "sequencePlayLength", "retargetSource", "retargetSourceAsset", "retargetSourceAssetReferencePose",
            "retargetTransformsSourceName", "retargetTransforms", "enableRootMotion", "forceRootLock", "rootMotionRootLock", "rootLockFirstFrame",
            "useNormalizedRootMotionScale", "additiveType", "basePoseType", "baseAsset", "baseFrame",
            "transformCurveCount", "animatedBoneAttributeCount", "floatCurveCount", "floatCurveNames"]);
        var interpolation = Text(value, "interpolation") switch
        { "Linear" => AlsRawAnimationInterpolation.Linear, "Step" => AlsRawAnimationInterpolation.Step, var mode => throw new ArgumentException("Unsupported raw interpolation: " + mode) };
        var additive = Text(value, "additiveType") switch
        {
            "AAT_None" => AlsRawAnimationAdditiveType.None, "AAT_LocalSpaceBase" => AlsRawAnimationAdditiveType.LocalSpaceBase,
            "AAT_RotationOffsetMeshSpace" => AlsRawAnimationAdditiveType.RotationOffsetMeshSpace,
            var mode => throw new ArgumentException("Unsupported native additive type: " + mode)
        };
        var baseType = Text(value, "basePoseType") switch
        {
            "ABPT_None" => AlsRawAnimationBasePoseType.None, "ABPT_RefPose" => AlsRawAnimationBasePoseType.RefPose,
            "ABPT_AnimScaled" => AlsRawAnimationBasePoseType.AnimScaled, "ABPT_AnimFrame" => AlsRawAnimationBasePoseType.AnimFrame,
            "ABPT_LocalAnimFrame" => AlsRawAnimationBasePoseType.LocalAnimFrame,
            var mode => throw new ArgumentException("Unsupported native additive base type: " + mode)
        };
        var rootLock = Text(value, "rootMotionRootLock") switch
        {
            "RefPose" => AlsRawAnimationRootLock.RefPose, "AnimFirstFrame" => AlsRawAnimationRootLock.AnimFirstFrame,
            "Zero" => AlsRawAnimationRootLock.Zero, var mode => throw new ArgumentException("Unsupported root lock: " + mode)
        };
        var sequenceLength = value.GetProperty("sequencePlayLength").GetDouble();
        var firstFrame = Pose(value.GetProperty("rootLockFirstFrame"));
        var enableRoot = value.GetProperty("enableRootMotion").GetBoolean(); var forceRoot = value.GetProperty("forceRootLock").GetBoolean();
        var normalizeRoot = value.GetProperty("useNormalizedRootMotionScale").GetBoolean(); var baseFrame = value.GetProperty("baseFrame").GetInt32();
        Require((int)interpolation == animation.Interpolation && (int)additive == animation.AdditiveType &&
            (int)baseType == animation.AdditiveBasePoseType && (int)rootLock == animation.RootMotionRootLock &&
            enableRoot == animation.RootMotionEnabled && forceRoot == animation.ForceRootLock && normalizeRoot == animation.UseNormalizedRootMotionScale &&
            baseFrame == animation.AdditiveBasePoseFrame && double.IsFinite(sequenceLength) && sequenceLength >= 0 &&
            sequenceLength == (double)animation.PlayLength,
            "Native raw extraction policy differs from its manifest.");
        var baseAsset = OptionalPath(value, "baseAsset"); var baseId = -1;
        if (baseAsset is not null)
        {
            Require(animationByPath.TryGetValue(baseAsset, out var baseAnimation) && baseAnimation.SkeletonId == animation.SkeletonId,
                "Missing additive base resource or unsupported cross-skeleton remapping.");
            baseId = baseAnimation!.Id;
        }
        Require(baseId == animation.AdditiveBasePoseAnimationId, "Native additive base identity differs from its manifest.");
        if (additive != AlsRawAnimationAdditiveType.None)
        {
            Require(baseType != AlsRawAnimationBasePoseType.None, "Native additive asset has no valid base policy.");
            if (baseType is AlsRawAnimationBasePoseType.AnimScaled or AlsRawAnimationBasePoseType.AnimFrame)
                Require(baseId >= 0, "Native additive evaluation requires a real base asset.");
        }
        var transformCurves = value.GetProperty("transformCurveCount").GetInt32();
        var attributes = value.GetProperty("animatedBoneAttributeCount").GetInt32();
        Require(transformCurves == 0 && attributes == 0,
            "Raw source has transform curves or animated bone attributes whose native evaluation is not implemented.");
        var curveNames = Strings(value.GetProperty("floatCurveNames"));
        var expectedCurves = animation.Curves.Where(c => c.Provenance == AlsCurveProvenance.SourceCurve).Select(c => c.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(value.GetProperty("floatCurveCount").GetInt32() == curveNames.Length &&
            curveNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == curveNames.Length && expectedCurves.SetEquals(curveNames),
            "Native float curve identities differ from the source curves in the manifest.");
        var retargetSource = Text(value, "retargetSource"); var sourceAsset = OptionalPath(value, "retargetSourceAsset");
        var assetReference = Poses(value.GetProperty("retargetSourceAssetReferencePose"));
        var retargetTransforms = Poses(value.GetProperty("retargetTransforms")); var sourceName = Text(value, "retargetTransformsSourceName");
        Require(assetReference.Length == 0 || assetReference.Length >= skeleton.PhysicalBoneCount && assetReference.Length <= skeleton.LogicalBoneCount,
            "Native retarget source asset reference does not cover the physical skeleton.");
        Require(retargetTransforms.Length >= skeleton.PhysicalBoneCount && retargetTransforms.Length <= skeleton.LogicalBoneCount,
            "Native retarget transforms do not cover the physical skeleton.");
        var none = retargetSource.Equals("None", StringComparison.OrdinalIgnoreCase);
        if (none && assetReference.Length != 0)
        {
            var package = animation.ObjectPath[..animation.ObjectPath.LastIndexOf('.')];
            Require(sourceName.Equals(package, StringComparison.OrdinalIgnoreCase) && PosesEqual(retargetTransforms, assetReference),
                "Native retarget getter did not select its stored source asset reference.");
        }
        else
        {
            Require(sourceName.Equals(retargetSource, StringComparison.OrdinalIgnoreCase), "Native retarget transform source name changed.");
            if (none) Require(PosesEqual(retargetTransforms, skeleton.ReferencePose), "Default retarget source differs from skeleton reference.");
        }
        return new(interpolation, sequenceLength, retargetSource, sourceAsset, assetReference, sourceName, retargetTransforms,
            enableRoot, forceRoot, rootLock, firstFrame, normalizeRoot, additive, baseType, baseAsset, baseId, baseFrame, transformCurves, attributes, curveNames,
            PrecisePoses(value.GetProperty("retargetTransforms")), PrecisePose(value.GetProperty("rootLockFirstFrame")));
    }

    private static void ValidateClosure(int[] roots, Dictionary<int, AlsRawAnimationSourceDefinition> sources,
        Dictionary<int, HashSet<int>> declaredOwners)
    {
        var required = new HashSet<int>(); var pending = new Queue<int>(roots);
        while (pending.TryDequeue(out var id))
        {
            if (!required.Add(id)) continue;
            Require(sources.TryGetValue(id, out var source), "Missing root or recursively referenced base source.");
            if (source!.Policy.BaseAnimationId >= 0) pending.Enqueue(source.Policy.BaseAnimationId);
        }
        Require(required.SetEquals(sources.Keys), "Raw source index contains an unrelated resource.");
        var owners = sources.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        foreach (var source in sources.Values)
            if (source.Policy.BaseAnimationId >= 0) owners[source.Policy.BaseAnimationId].Add(source.PoseData.Identity.AnimationId);
        foreach (var (id, expectedOwners) in owners)
            Require(declaredOwners[id].SetEquals(expectedOwners), "Native base dependency ownership is incomplete or fabricated.");
    }

    private static AlsLocalPose[] Poses(JsonElement rows)
    {
        var output = new AlsLocalPose[rows.GetArrayLength()];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = Pose(rows[index]);
        }
        return output;
    }
    private static AlsPrecisePose[] PrecisePoses(JsonElement rows)
    {
        var output = new AlsPrecisePose[rows.GetArrayLength()];
        for (var i = 0; i < output.Length; i++) output[i] = PrecisePose(rows[i]);
        return output;
    }
    private static AlsPrecisePose PrecisePose(JsonElement row)
    {
        Fields(row, ["position", "rotation", "scale"]);
        var p = row.GetProperty("position"); var q = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        Require(p.GetArrayLength() == 3 && q.GetArrayLength() == 4 && s.GetArrayLength() == 3, "Invalid precise transform component count.");
        var result = new AlsPrecisePose(new(p[0].GetDouble() * .01, -p[1].GetDouble() * .01, p[2].GetDouble() * .01),
            new(-q[0].GetDouble(), q[1].GetDouble(), -q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
        result.Validate(1e-4); return result;
    }
    private static AlsLocalPose Pose(JsonElement row)
    {
        Fields(row, ["position", "rotation", "scale"]);
        return ConvertPose(Components(row.GetProperty("position"), 3), Components(row.GetProperty("rotation"), 4), Components(row.GetProperty("scale"), 3));
    }
    private static AlsLocalPose ConvertPose(float[] p, float[] q, float[] s)
    {
        var length = (double)q[0] * q[0] + (double)q[1] * q[1] + (double)q[2] * q[2] + (double)q[3] * q[3];
        Require(Math.Abs(length - 1) <= 1e-4, "Native raw quaternion must remain approximately unit length.");
        // Canonical conversion followed by FBX bone conversion is a Y reflection.
        // Rotation is an axial vector: det(reflection) gives (-X,Y,-Z,W). Keep all
        // binary32 components and sign; normalization/matrix/Euler roundtrips lose source data.
        return new(new Vector3(p[0], -p[1], p[2]) * .01f, new Quaternion(-q[0], q[1], -q[2], q[3]), new Vector3(s[0], s[1], s[2]));
    }
    private static float[][] Channel(JsonElement track, string name, int components, int keyCount, bool allowEmpty)
    {
        var rows = track.GetProperty(name); var count = rows.GetArrayLength();
        Require(count == 1 || count == keyCount || allowEmpty && count == 0, "Invalid native track channel cardinality: " + name);
        var output = new float[count][];
        for (var index = 0; index < count; index++) output[index] = Components(rows[index], components);
        return output;
    }
    private static float[] Components(JsonElement row, int count)
    {
        Require(row.GetArrayLength() == count, "Invalid native transform component count."); var output = new float[count];
        for (var index = 0; index < count; index++)
        { output[index] = row[index].GetSingle(); Require(float.IsFinite(output[index]), "Nonfinite native transform component."); }
        return output;
    }
    private static bool SamePose(in AlsLocalPose a, in AlsLocalPose b, float tolerance) =>
        Vector3.Distance(a.Position, b.Position) <= tolerance && Vector3.Distance(a.Scale, b.Scale) <= tolerance &&
        MathF.Min((a.Rotation - b.Rotation).Length(), (a.Rotation + b.Rotation).Length()) <= tolerance;
    private static bool PosesEqual(ReadOnlySpan<AlsLocalPose> first, ReadOnlySpan<AlsLocalPose> second)
    {
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++) if (first[index] != second[index]) return false;
        return true;
    }
    private static string? OptionalPath(JsonElement value, string property)
    {
        var row = value.GetProperty(property); if (row.ValueKind == JsonValueKind.Null) return null;
        var path = String(row); Require(path.StartsWith("/", StringComparison.Ordinal) && path.Contains('.'), "Invalid native object path."); return path;
    }
    private static string Text(JsonElement value, string property) => String(value.GetProperty(property));
    private static string String(JsonElement value)
    { var text = value.GetString(); Require(!string.IsNullOrWhiteSpace(text), "Empty native source identity or policy text."); return text!; }
    private static string[] Strings(JsonElement rows) => rows.EnumerateArray().Select(String).ToArray();
    private static int[] Integers(JsonElement rows) => rows.EnumerateArray().Select(v => v.GetInt32()).ToArray();
    private static bool IsAssetId(string value) => value.Length == 40 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Fields(JsonElement value, string[] required, string[]? optional = null)
    {
        var allowed = required.Concat(optional ?? []).ToHashSet(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            Require(allowed.Contains(property.Name) && seen.Add(property.Name), "Unknown or duplicate raw source field: " + property.Name);
        Require(required.All(seen.Contains), "Missing required raw source field.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
