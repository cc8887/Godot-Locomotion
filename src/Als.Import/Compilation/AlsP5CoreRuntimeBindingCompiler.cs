using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;
using CoreOccurrenceEntry = GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry;
using CoreOccurrenceKind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;

namespace GodotAls.Import.Compilation;

public static class AlsP5CoreRuntimeBindingCompiler
{
    private const int Version = 1;
    private const int MaximumExpandedTimelineEntries = 1_000_000;

    private static readonly string[] CanonicalBaseAnimationStableIds =
    [
        "621a81bf492cb9120b45cfd91b685854afb7dc75",
        "6124eafdcbeaaf04bca366add34c821faa0e4963", "44a7f89b2c1dac832ca63753c131a037420f9d7e",
        "fc2d3a4142a1bd82d20877d806c783ff56dfe688", "32fe18c71ccb860fe35c01d6b2b10fa2e4d98297",
        "a4c6e0e455e7be7355cdd7c3ce49272d07773b18", "eb84a748fee4615754ce3cbcd3c259b33918b935",
        "572c3c83c9007964c233db4c7288ae38e20c3dec", "8ae1b9703a7d0144e570247d88629530b376885f",
        "945bdda63e8a379694c792c3545fe11dd166b724", "859f8a49c55747e7382a1ae15970b23cc12f3f85",
        "b07a51bbab122c81679ac30d3f2f78f45ac14dc8", "245ea51e30449a60b7d2b783ec0f6d24ec3bacc0",
        "8bd6ad52ad04a2ad23b47187886c630701df5260", "146fff5000e151a3790ba5aca8a5bfee4363e909",
        "8eb8837c9f32973628b83ed2b98f7d68a0e82aa9", "21c24bd7df5192db2e2a860457f2b7b0681de41d",
        "db60b2c35ce5ef5216c782fc1f33549cbcf8278d", "f9ec8804e251f03c57e04dde4db9bd921457bae4",
        "0b439429cf3a3692eaea77a81b275ae96eab937f", "8b1315b68336709c4a00bcd18871f0b3ba44308a",
        "8e310a7484f5a1fda4a549e1b80fd0405b1d4f35",
    ];

    private static readonly (float X, float Y)[] CanonicalStandingCoordinates =
    [
        (0f, .5f), (-.353553f, .353553f), (.353553f, .353553f), (0f, -.5f),
        (-.353553f, -.353553f), (.353553f, -.353553f), (0f, 1f),
        (-.707107f, .707107f), (.707107f, .707107f), (0f, -1f),
        (-.707107f, -.707107f), (.707107f, -.707107f), (0f, 1.5f),
    ];

    private static readonly (float X, float Y)[] CanonicalCrouchingCoordinates =
        [(0f, 1f), (-1f, 0f), (1f, 0f), (0f, -1f)];

    private static readonly string[] CanonicalTurnStableIds =
    [
        "a73b6e3c8aac55396058a7cb7c65b1afe6a539fa", "f699b8f36ff252d0e0e07c50e59b7493eb5a0e8b",
        "a3c722afa7c416d15d8c61ed614d4def5b5b758c", "59f469970639c759780278d7ffc8ea717e715207",
        "ddfa1a6737182bddb3134e78d0460c3ef49f93e1", "c13f1b18a24a33a01827ee93b9b1bdcd64295a0d",
        "a20c9d988632a569157541f0d6fd8c309ac8ae09", "92e0d292db977093c0ad02b5490c241609032e38",
    ];

    private static readonly string[] CanonicalRotateStableIds =
    [
        "4677f1239a2057a65a5daeaf6ebbbf4f6a997060", "9a04c94cbdf6a02f426768f49c789cb8341e253a",
        "310b992c1a30d10675842924f95353905f659382", "3c285f6df704e8e73cdfd0fd9bb147c1e5338ef1",
    ];

    public static AlsP5CoreRuntimeBindingSnapshot Compile(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5aAnimationRuntimeProfile p5a,
        AlsP5OccurrenceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(locomotion);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(p5a);
        ArgumentNullException.ThrowIfNull(layout);

        ValidateDefinitionDigest(animationSet);
        ValidateAuthoritativeAnimationSet(animationSet);
        var animations = animationSet.Animations;
        ValidateAnimationTable(animations);
        ValidateExactLayout(locomotion, pose, p5a, layout);
        var (occurrenceEntries, importEntries) = CompileOccurrenceEntries(layout);
        var baseAnimationIds = BaseAnimationIds(locomotion);
        var turns = pose.Turns;
        var rotates = pose.Rotates;
        var eventSemantics = CompileEventSemantics(p5a.EventSemantics);

        ValidateSkeletonAndGraphInputs(animationSet, locomotion, pose, animations,
            baseAnimationIds, turns, rotates, out var skeleton, out var logicalRoot,
            out var physicalRoot, out var standingSamples, out var crouchingSamples,
            out var leanSamples, out var allAnimationIds, out var maskHeaders,
            out var logicalBoneIds, out var normalizedAnimationIds);

        CompileCurves(animations, p5a.AllowTransitions,
            out var curveKeys, out var curveBindings, out var curveBindingIdentities,
            out var animationCurveRanges, out var allowTransitionsPolicy,
            out var allowTransitionsBindingIndices);
        CompileFootCurves(pose.FootCurves, animations, locomotion.SkeletonId,
            out var footCurveBindings);

        ValidateRuntimeProfile(p5a, animationSet.Montages, animations,
            locomotion.SkeletonId, occurrenceEntries, importEntries);
        ValidateOccurrenceClosure(occurrenceEntries, baseAnimationIds.Length,
            turns.Length, rotates.Length, p5a.Actions.Length, p5a.SegmentBindings.Length);
        var generalTimeline = CompileGeneralTimeline(
            animations, baseAnimationIds, turns, rotates, p5a.DynamicTransition,
            occurrenceEntries, eventSemantics);
        CompileSync(p5a, layout, animations, locomotion.SkeletonId, baseAnimationIds, occurrenceEntries,
            out var syncMarkers, out var syncGroup, out var syncMembers,
            out var syncOccurrences);
        var dynamicTransition = CompileTransition(
            p5a.DynamicTransition, animations, locomotion.SkeletonId, occurrenceEntries);
        ValidateActionProvenance(animationSet, p5a);
        CompileActions(p5a, occurrenceEntries, eventSemantics, generalTimeline,
            out var timelineDefinitions, out var actionDefinitions,
            out var actionSections, out var actionSegments, out var actionTimelineRanges);

        var bindingDigest = ComputeBindingDigest(
            layout.Digest, curveKeys, curveBindings, curveBindingIdentities,
            animationCurveRanges, allowTransitionsPolicy, allowTransitionsBindingIndices,
            footCurveBindings, pose.FootCurves.GroundedIkWeight,
            pose.FootCurves.JumpStartIkWeight, pose.FootCurves.FallLoopIkWeight,
            pose.FootCurves.LandRecoveryIkWeight, timelineDefinitions, syncMarkers,
            syncGroup, syncMembers, syncOccurrences, dynamicTransition,
            actionDefinitions, actionSections, actionSegments, actionTimelineRanges);
        var graphDigest = ComputeGraphDigest(
            locomotion.SkeletonId, locomotion.MannequinMeshId, logicalRoot, physicalRoot,
            locomotion.Presentation, locomotion.StandingIdleAnimationId,
            locomotion.CrouchingIdleAnimationId, locomotion.JumpStartAnimationId,
            locomotion.FallLoopAnimationId, locomotion.LandAnimationId,
            locomotion.LeanAdditiveBasePoseAnimationId, standingSamples, crouchingSamples,
            leanSamples, allAnimationIds, pose.Aim, turns, rotates, maskHeaders,
            logicalBoneIds, normalizedAnimationIds);
        if (bindingDigest == 0 || graphDigest == 0)
        {
            throw new ArgumentException("A P5 runtime snapshot digest must be nonzero.");
        }

        return new AlsP5CoreRuntimeBindingSnapshot(
            Version, bindingDigest, layout.Digest, graphDigest,
            animationSet.DefinitionDigest, curveKeys, curveBindings,
            curveBindingIdentities, animationCurveRanges, allowTransitionsPolicy,
            allowTransitionsBindingIndices, footCurveBindings,
            pose.FootCurves.GroundedIkWeight, pose.FootCurves.JumpStartIkWeight,
            pose.FootCurves.FallLoopIkWeight, pose.FootCurves.LandRecoveryIkWeight,
            timelineDefinitions, syncMarkers, syncGroup, syncMembers, syncOccurrences,
            dynamicTransition, actionDefinitions, actionSections, actionSegments,
            actionTimelineRanges, occurrenceEntries, locomotion.SkeletonId,
            locomotion.MannequinMeshId, logicalRoot, physicalRoot,
            locomotion.Presentation, locomotion.StandingIdleAnimationId,
            locomotion.CrouchingIdleAnimationId, locomotion.JumpStartAnimationId,
            locomotion.FallLoopAnimationId, locomotion.LandAnimationId,
            locomotion.LeanAdditiveBasePoseAnimationId, standingSamples, crouchingSamples,
            leanSamples, allAnimationIds, pose.Aim, turns, rotates, maskHeaders,
            logicalBoneIds, normalizedAnimationIds);
    }

    private static void ValidateDefinitionDigest(AlsAnimationSetDefinition animationSet)
    {
        var stored = animationSet.DefinitionDigest;
        if (stored is null || stored.Length != 64 || stored.Any(value => value is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')) ||
            !string.Equals(AlsAnimationSetPayload.ComputeDefinitionDigest(animationSet), stored,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The animation-set definition digest is stale or noncanonical.",
                nameof(animationSet));
        }
    }

    private static void ValidateAuthoritativeAnimationSet(AlsAnimationSetDefinition animationSet)
    {
        try
        {
            _ = AlsAnimationSetPayload.Deserialize(AlsAnimationSetPayload.Serialize(animationSet));
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException(
                "The animation set failed authoritative payload validation.",
                nameof(animationSet),
                exception);
        }
    }

    private static void ValidateExactLayout(
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5aAnimationRuntimeProfile p5a,
        AlsP5OccurrenceLayout supplied)
    {
        var expected = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        if (supplied.Version != expected.Version || supplied.Digest != expected.Digest ||
            !supplied.Entries.SequenceEqual(expected.Entries) ||
            !supplied.SyncMappings.SequenceEqual(expected.SyncMappings))
        {
            throw new ArgumentException("The occurrence layout differs from the fresh canonical allocation.",
                nameof(supplied));
        }
    }

    private static void ValidateAnimationTable(AlsAnimationDefinition[] animations)
    {
        if (animations.Length == 0)
        {
            throw new ArgumentException("The animation table is empty.", nameof(animations));
        }
        for (var animationId = 0; animationId < animations.Length; animationId++)
        {
            var animation = animations[animationId] ?? throw new ArgumentException(
                "The animation table contains null.", nameof(animations));
            if (animation.Id != animationId || !Positive(animation.PlayLength))
            {
                throw new ArgumentException("Animation IDs and durations must be canonical.", nameof(animations));
            }
            var curveIds = new HashSet<int>();
            foreach (var curve in animation.Curves)
            {
                if (curve.CurveId < 0 || !curveIds.Add(curve.CurveId) || curve.Keys.Length == 0)
                {
                    throw new ArgumentException("Animation curve identity is invalid.", nameof(animations));
                }
                var previousTime = float.NegativeInfinity;
                foreach (var key in curve.Keys)
                {
                    if (!Finite(key.TimeSeconds) || !Finite(key.Value) || !Finite(key.ArriveTangent) ||
                        !Finite(key.LeaveTangent) || key.TimeSeconds < 0f ||
                        key.TimeSeconds > animation.PlayLength || key.TimeSeconds <= previousTime ||
                        !Enum.IsDefined(key.Interpolation))
                    {
                        throw new ArgumentException("Animation curve keys are invalid.", nameof(animations));
                    }
                    previousTime = key.TimeSeconds;
                }
            }
            foreach (var value in animation.Timeline)
            {
                ValidateTimelineSource(value, animation.Id, animation.PlayLength);
            }
            var markerIds = new HashSet<int>();
            foreach (var marker in animation.SyncMarkers)
            {
                if (marker.MarkerId < 0 || !markerIds.Add(marker.MarkerId) ||
                    string.IsNullOrEmpty(marker.Name) || !Finite(marker.TimeSeconds) ||
                    marker.TimeSeconds < 0f || marker.TimeSeconds > animation.PlayLength ||
                    marker.SourceIndex < 0 || marker.TrackIndex < 0)
                {
                    throw new ArgumentException("Animation Sync markers are invalid.", nameof(animations));
                }
            }
        }
    }

    private static (CoreOccurrenceEntry[] Core, AlsP5OccurrenceLayoutEntry[] Import)
        CompileOccurrenceEntries(AlsP5OccurrenceLayout layout)
    {
        var entries = layout.Entries;
        AlsP5OccurrenceLayoutCompiler.Validate(layout.Version, entries);
        var result = new CoreOccurrenceEntry[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            var value = entries[index];
            var kind = value.SourceKind switch
            {
                AlsP5OccurrenceSourceKind.Base => CoreOccurrenceKind.Base,
                AlsP5OccurrenceSourceKind.Turn => CoreOccurrenceKind.Turn,
                AlsP5OccurrenceSourceKind.Rotate => CoreOccurrenceKind.Rotate,
                AlsP5OccurrenceSourceKind.Transition => CoreOccurrenceKind.Transition,
                AlsP5OccurrenceSourceKind.ActionMontage => CoreOccurrenceKind.ActionMontage,
                AlsP5OccurrenceSourceKind.ActionSequence => CoreOccurrenceKind.ActionSequence,
                _ => throw new ArgumentException("The occurrence source kind is unknown.", nameof(layout)),
            };
            result[index] = new CoreOccurrenceEntry(kind, value.SourceBindingIndex,
                value.GraphSlotIndex, value.OccurrenceHandleId, value.AuthorityGroupId);
        }
        AlsP5OccurrenceLayoutContract.Validate(layout.Version, layout.Digest, result);
        return (result, entries);
    }

    private static int[] BaseAnimationIds(AlsLocomotionAnimationProfile locomotion) =>
    [
        locomotion.StandingIdleAnimationId,
        .. locomotion.StandingSamples.Select(value => value.AnimationId),
        locomotion.CrouchingIdleAnimationId,
        .. locomotion.CrouchingSamples.Select(value => value.AnimationId),
        locomotion.JumpStartAnimationId,
        locomotion.FallLoopAnimationId,
        locomotion.LandAnimationId,
    ];

    private static int[] CompileEventSemantics(AlsCompiledEventSemantic[] values)
    {
        var kinds = Enum.GetValues<AlsCompiledTimelineEventKind>();
        if (values.Length != kinds.Length)
        {
            throw new ArgumentException("The event semantic table is incomplete.", nameof(values));
        }
        var result = new int[kinds.Length];
        var semanticIds = new HashSet<int>();
        for (var index = 0; index < values.Length; index++)
        {
            if (!Enum.IsDefined(values[index].Kind) || values[index].Kind != kinds[index] ||
                values[index].SemanticId != index || !semanticIds.Add(values[index].SemanticId))
            {
                throw new ArgumentException("The event semantic table is invalid.", nameof(values));
            }
            result[index] = values[index].SemanticId;
        }
        return result;
    }

    private static void ValidateSkeletonAndGraphInputs(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsAnimationDefinition[] animations,
        int[] baseAnimationIds,
        AlsTurnProfile[] turns,
        AlsRotateProfile[] rotates,
        out AlsSkeletonDefinition skeleton,
        out int logicalRoot,
        out int physicalRoot,
        out AlsP5GraphSample[] standingSamples,
        out AlsP5GraphSample[] crouchingSamples,
        out AlsP5GraphSample[] leanSamples,
        out int[] allAnimationIds,
        out AlsP5GraphMaskHeader[] maskHeaders,
        out int[] logicalBoneIds,
        out int[] normalizedAnimationIds)
    {
        if (pose.SkeletonId != locomotion.SkeletonId ||
            (uint)locomotion.SkeletonId >= (uint)animationSet.Skeletons.Length ||
            (uint)locomotion.MannequinMeshId >= (uint)animationSet.SkeletalMeshes.Length)
        {
            throw new ArgumentException("P3/P4 skeleton or mannequin identity is invalid.");
        }
        skeleton = animationSet.Skeletons[locomotion.SkeletonId];
        var mesh = animationSet.SkeletalMeshes[locomotion.MannequinMeshId];
        if (mesh.Id != locomotion.MannequinMeshId || mesh.SkeletonId != locomotion.SkeletonId)
        {
            throw new ArgumentException("The mannequin mesh is not bound to the P3/P4 skeleton.");
        }
        ValidateSkeletonMaps(skeleton);
        logicalRoot = skeleton.RequiredBones.Root;
        if ((uint)logicalRoot >= (uint)skeleton.LogicalToPhysical.Length)
        {
            throw new ArgumentException("The required logical root is invalid.");
        }
        physicalRoot = skeleton.LogicalToPhysical[logicalRoot];
        if ((uint)physicalRoot >= (uint)skeleton.PhysicalBones.Length)
        {
            throw new ArgumentException("The required root has no physical mapping.");
        }
        if (!Finite(locomotion.Presentation.TranslationMeters.X) ||
            !Finite(locomotion.Presentation.TranslationMeters.Y) ||
            !Finite(locomotion.Presentation.TranslationMeters.Z) ||
            !Finite(locomotion.Presentation.YawRadians))
        {
            throw new ArgumentException("The graph presentation is non-finite.");
        }

        if (baseAnimationIds.Length != 22 || locomotion.StandingSamples.Length != 13 ||
            locomotion.CrouchingSamples.Length != 4 || turns.Length != 8 || rotates.Length != 4)
        {
            throw new ArgumentException("The frozen P3/P4 graph topology is incomplete.");
        }
        ValidateP3P4ReferenceBounds(animationSet, locomotion, pose, baseAnimationIds, turns, rotates);
        ValidateP3P4Provenance(animationSet, locomotion, pose, skeleton, baseAnimationIds);
        standingSamples = MapSamples(locomotion.StandingSamples, animations, locomotion.SkeletonId);
        crouchingSamples = MapSamples(locomotion.CrouchingSamples, animations, locomotion.SkeletonId);
        leanSamples = MapSamples(locomotion.LeanAdditiveSamples, animations, locomotion.SkeletonId);
        foreach (var animationId in baseAnimationIds)
        {
            ValidateAnimationId(animations, animationId, locomotion.SkeletonId);
        }
        ValidateAnimationId(animations, locomotion.LeanAdditiveBasePoseAnimationId, locomotion.SkeletonId);
        foreach (var turn in turns)
        {
            ValidateAnimationAndCurve(animations, turn.AnimationId, turn.CurveId, locomotion.SkeletonId);
            if (!Enum.IsDefined(turn.Stance) || turn.Direction is not (-1 or 1) ||
                turn.NominalDegrees <= 0 || !Positive(turn.BasePlayRate) ||
                !Positive(turn.BlendSeconds) || turn.ScaleAngle > 1)
            {
                throw new ArgumentException("A Turn profile row is invalid.");
            }
        }
        foreach (var rotate in rotates)
        {
            ValidateAnimationAndCurve(animations, rotate.AnimationId, rotate.CurveId, locomotion.SkeletonId);
            if (!Enum.IsDefined(rotate.Stance) || rotate.Direction is not (-1 or 1))
            {
                throw new ArgumentException("A Rotate profile row is invalid.");
            }
        }

        var expectedAll = baseAnimationIds
            .Concat(locomotion.LeanAdditiveSamples.Select(value => value.AnimationId))
            .Append(locomotion.LeanAdditiveBasePoseAnimationId)
            .Distinct().Order().ToArray();
        allAnimationIds = locomotion.AllAnimationIds.ToArray();
        if (!allAnimationIds.SequenceEqual(expectedAll))
        {
            throw new ArgumentException("P3 AllAnimationIds is not sorted, unique and complete.");
        }

        var aim = pose.Aim;
        if ((uint)aim.AimOffsetId >= (uint)animationSet.AimOffsets.Length)
        {
            throw new ArgumentException("The AimOffset ID is invalid.");
        }
        normalizedAnimationIds =
        [
            aim.AdditiveBasePoseAnimationId,
            aim.DownAnimationId,
            aim.ForwardAnimationId,
            aim.UpAnimationId,
        ];
        if (normalizedAnimationIds.Distinct().Count() != normalizedAnimationIds.Length)
        {
            throw new ArgumentException("Normalized Aim animation IDs must be distinct.");
        }
        foreach (var animationId in normalizedAnimationIds)
        {
            ValidateAnimationId(animations, animationId, locomotion.SkeletonId);
        }

        CompileMasks(pose.Masks, skeleton, out maskHeaders, out logicalBoneIds);
    }

    private static void ValidateP3P4ReferenceBounds(
        AlsAnimationSetDefinition set,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        int[] baseAnimationIds,
        AlsTurnProfile[] turns,
        AlsRotateProfile[] rotates)
    {
        static void RequireAnimation(int animationId, int animationCount)
        {
            if ((uint)animationId >= (uint)animationCount)
            {
                throw new ArgumentException("A P3/P4 animation ID is out of bounds.");
            }
        }

        foreach (var animationId in baseAnimationIds)
        {
            RequireAnimation(animationId, set.Animations.Length);
        }
        foreach (var sample in locomotion.LeanAdditiveSamples)
        {
            if (sample is null)
            {
                throw new ArgumentException("A P3 Lean sample is null.");
            }
            RequireAnimation(sample.AnimationId, set.Animations.Length);
        }
        RequireAnimation(locomotion.LeanAdditiveBasePoseAnimationId, set.Animations.Length);

        if ((uint)pose.Aim.AimOffsetId >= (uint)set.AimOffsets.Length)
        {
            throw new ArgumentException("The P4 AimOffset ID is out of bounds.");
        }
        RequireAnimation(pose.Aim.DownAnimationId, set.Animations.Length);
        RequireAnimation(pose.Aim.ForwardAnimationId, set.Animations.Length);
        RequireAnimation(pose.Aim.UpAnimationId, set.Animations.Length);
        RequireAnimation(pose.Aim.AdditiveBasePoseAnimationId, set.Animations.Length);
        foreach (var turn in turns)
        {
            RequireAnimation(turn.AnimationId, set.Animations.Length);
        }
        foreach (var rotate in rotates)
        {
            RequireAnimation(rotate.AnimationId, set.Animations.Length);
        }
    }

    private static void ValidateP3P4Provenance(
        AlsAnimationSetDefinition set,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsSkeletonDefinition skeleton,
        int[] baseAnimationIds)
    {
        if (pose.SchemaVersion != 1 || !pose.IsRuntimeComplete ||
            !string.Equals(set.SkeletalMeshes[locomotion.MannequinMeshId].StableId,
                "86d98d8177feb473c8a5f406c5b42f8c2a2f7b07", StringComparison.Ordinal))
        {
            throw new ArgumentException("The P3/P4 runtime identity header is noncanonical.");
        }
        if (baseAnimationIds.Distinct().Count() != baseAnimationIds.Length ||
            baseAnimationIds.Length != CanonicalBaseAnimationStableIds.Length)
        {
            throw new ArgumentException("The P3 base animation roles are incomplete or duplicated.");
        }
        for (var index = 0; index < baseAnimationIds.Length; index++)
        {
            if (!string.Equals(set.Animations[baseAnimationIds[index]].StableId,
                    CanonicalBaseAnimationStableIds[index], StringComparison.Ordinal))
            {
                throw new ArgumentException("A P3 base animation role is not canonical.");
            }
        }
        ValidateCanonicalCoordinates(locomotion.StandingSamples, CanonicalStandingCoordinates);
        ValidateCanonicalCoordinates(locomotion.CrouchingSamples, CanonicalCrouchingCoordinates);
        ValidateLeanProvenance(set, locomotion);
        ValidateAimProvenance(set, pose.Aim, locomotion.SkeletonId);
        ValidateTurnRotateProvenance(set, pose, locomotion.SkeletonId);
        ValidateMaskProvenance(pose.Masks, skeleton);
        ValidateFootProvenance(set, locomotion, pose, skeleton);
    }

    private static void ValidateCanonicalCoordinates(
        AlsLocomotionAnimationSample[] samples,
        (float X, float Y)[] expected)
    {
        if (samples.Length != expected.Length) throw new ArgumentException("A P3 sample bank is incomplete.");
        for (var index = 0; index < samples.Length; index++)
        {
            if (samples[index].X != expected[index].X || samples[index].Y != expected[index].Y ||
                samples[index].RateScale != 1f)
            {
                throw new ArgumentException("A P3 sample bank differs from the authored mapping.");
            }
        }
    }

    private static void ValidateLeanProvenance(
        AlsAnimationSetDefinition set,
        AlsLocomotionAnimationProfile locomotion)
    {
        var blend = set.BlendSpaces.SingleOrDefault(value => string.Equals(value.StableId,
            "6cdc10621a632cfe5aae96e76a82b37da26891e3", StringComparison.Ordinal));
        var samples = locomotion.LeanAdditiveSamples;
        if (blend is null || blend.Samples.Length != samples.Length || samples.Length == 0)
        {
            throw new ArgumentException("The P3 Lean additive source is missing or incomplete.");
        }
        var baseId = -1;
        var baseType = -1;
        var baseFrame = -1;
        for (var index = 0; index < samples.Length; index++)
        {
            var authored = blend.Samples[index];
            var mapped = samples[index];
            if (authored.SampleValue.Length < 2 || mapped.AnimationId != authored.AnimationId ||
                mapped.X != authored.SampleValue[0] || mapped.Y != authored.SampleValue[1] ||
                mapped.RateScale != authored.RateScale)
            {
                throw new ArgumentException("A P3 Lean sample differs from the current BlendSpace.");
            }
            var animation = set.Animations[mapped.AnimationId];
            if (animation.AdditiveType != 1 || animation.SkeletonId != locomotion.SkeletonId ||
                (uint)animation.AdditiveBasePoseAnimationId >= (uint)set.Animations.Length)
            {
                throw new ArgumentException("A P3 Lean sample has invalid additive provenance.");
            }
            if (index == 0)
            {
                baseId = animation.AdditiveBasePoseAnimationId;
                baseType = animation.AdditiveBasePoseType;
                baseFrame = animation.AdditiveBasePoseFrame;
            }
            else if (animation.AdditiveBasePoseAnimationId != baseId ||
                     animation.AdditiveBasePoseType != baseType ||
                     animation.AdditiveBasePoseFrame != baseFrame)
            {
                throw new ArgumentException("P3 Lean samples do not share one additive base.");
            }
        }
        if (baseId != locomotion.LeanAdditiveBasePoseAnimationId ||
            set.Animations[baseId].AdditiveType != 0 ||
            set.Animations[baseId].SkeletonId != locomotion.SkeletonId)
        {
            throw new ArgumentException("The P3 Lean additive base is invalid.");
        }
    }

    private static void ValidateAimProvenance(
        AlsAnimationSetDefinition set,
        AlsAimProfile aim,
        int skeletonId)
    {
        var offset = set.AimOffsets[aim.AimOffsetId];
        if (!string.Equals(offset.StableId, "b4bf2befd979de45f53f300dc0e60c702fc3a686",
                StringComparison.Ordinal) || offset.Parameters.Length != 3 ||
            offset.Parameters[0] != new AlsBlendParameterDefinition("Pitch", -90f, 90f, 4) ||
            offset.Parameters[1] != new AlsBlendParameterDefinition("None", 0f, 100f, 4) ||
            offset.Parameters[2] != new AlsBlendParameterDefinition("None", 0f, 100f, 4) ||
            offset.Samples.Length != 3)
        {
            throw new ArgumentException("The P4 AimOffset contract is noncanonical.");
        }
        var expectedIds = new[] { aim.DownAnimationId, aim.ForwardAnimationId, aim.UpAnimationId };
        var expectedPitch = new[] { -90f, 0f, 90f };
        var baseId = aim.AdditiveBasePoseAnimationId;
        var additiveBaseType = set.Animations[expectedIds[0]].AdditiveBasePoseType;
        var additiveBaseFrame = set.Animations[expectedIds[0]].AdditiveBasePoseFrame;
        for (var index = 0; index < expectedIds.Length; index++)
        {
            var matching = offset.Samples.Where(value => value.SampleValue is { Length: 3 } &&
                value.SampleValue[0] == expectedPitch[index] && value.SampleValue[1] == 0f &&
                value.SampleValue[2] == 0f).ToArray();
            if (matching.Length != 1)
                throw new ArgumentException("A P4 Aim sample coordinate is missing or duplicate.");
            var sample = matching[0];
            var animation = set.Animations[expectedIds[index]];
            if (sample.AnimationId != expectedIds[index] || sample.RateScale != 1f ||
                animation.SkeletonId != skeletonId || animation.AdditiveType != 2 ||
                animation.AdditiveBasePoseAnimationId != baseId ||
                animation.AdditiveBasePoseType != additiveBaseType ||
                animation.AdditiveBasePoseFrame != additiveBaseFrame)
            {
                throw new ArgumentException("A P4 Aim sample or additive binding is noncanonical.");
            }
        }
        if (set.Animations[baseId].AdditiveType != 0 || set.Animations[baseId].SkeletonId != skeletonId)
        {
            throw new ArgumentException("The P4 Aim additive base is invalid.");
        }
    }

    private static void ValidateTurnRotateProvenance(
        AlsAnimationSetDefinition set,
        AlsPoseAnimationProfile pose,
        int skeletonId)
    {
        var expectedTurns = new (AlsPoseStance Stance, sbyte Direction, short Degrees)[]
        {
            (AlsPoseStance.Standing, -1, 90), (AlsPoseStance.Standing, 1, 90),
            (AlsPoseStance.Standing, -1, 180), (AlsPoseStance.Standing, 1, 180),
            (AlsPoseStance.Crouching, -1, 90), (AlsPoseStance.Crouching, 1, 90),
            (AlsPoseStance.Crouching, -1, 180), (AlsPoseStance.Crouching, 1, 180),
        };
        var turnAnimations = new HashSet<int>();
        for (var index = 0; index < pose.Turns.Length; index++)
        {
            var value = pose.Turns[index];
            var animation = set.Animations[value.AnimationId];
            if ((value.Stance, value.Direction, value.NominalDegrees) != expectedTurns[index] ||
                value.BasePlayRate != 1.2f || value.BlendSeconds != .2f || value.ScaleAngle != 1 ||
                !turnAnimations.Add(value.AnimationId) || animation.SkeletonId != skeletonId ||
                !string.Equals(animation.StableId, CanonicalTurnStableIds[index], StringComparison.Ordinal) ||
                !HasCanonicalRotationCurve(animation, value.CurveId))
            {
                throw new ArgumentException("A P4 Turn role is noncanonical.");
            }
        }
        var expectedRotates = new (AlsPoseStance Stance, sbyte Direction)[]
        {
            (AlsPoseStance.Standing, -1), (AlsPoseStance.Standing, 1),
            (AlsPoseStance.Crouching, -1), (AlsPoseStance.Crouching, 1),
        };
        var rotateAnimations = new HashSet<int>();
        for (var index = 0; index < pose.Rotates.Length; index++)
        {
            var value = pose.Rotates[index];
            var animation = set.Animations[value.AnimationId];
            if ((value.Stance, value.Direction) != expectedRotates[index] ||
                !rotateAnimations.Add(value.AnimationId) || animation.SkeletonId != skeletonId ||
                !string.Equals(animation.StableId, CanonicalRotateStableIds[index], StringComparison.Ordinal) ||
                !HasCanonicalRotationCurve(animation, value.CurveId))
            {
                throw new ArgumentException("A P4 Rotate role is noncanonical.");
            }
        }
    }

    private static bool HasCanonicalRotationCurve(AlsAnimationDefinition animation, int curveId) =>
        animation.Curves.Count(value => value.CurveId == curveId &&
            value.CanonicalKind == AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond &&
            value.Provenance == AlsCurveProvenance.DerivedRootTrack) == 1;

    private static void ValidateMaskProvenance(
        AlsLayerMaskProfile masks,
        AlsSkeletonDefinition skeleton)
    {
        var rootNames = new[]
        {
            "spine_01", "neck_01", "clavicle_l", "clavicle_r", "Hand_L", "hand_r",
            "Pelvis", "Thigh_L", "Thigh_R", "Foot_L", "Foot_R",
        };
        string[][] boundaryNames =
        [
            ["neck_01", "clavicle_l", "clavicle_r"], [], ["Hand_L"], ["hand_r"], [], [],
            ["spine_01", "Thigh_L", "Thigh_R"], ["Foot_L"], ["Foot_R"], [], [],
        ];
        var claimed = new HashSet<int>();
        var entries = masks.Entries;
        if (entries.Length != rootNames.Length) throw new ArgumentException("P4 masks are incomplete.");
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var roots = skeleton.LogicalBones.Where(value =>
                string.Equals(value.Name, rootNames[index], StringComparison.Ordinal)).ToArray();
            if (roots.Length != 1 || entry.Kind != (AlsPoseMaskKind)index ||
                entry.RootBoneId != roots[0].LogicalId || entry.BoneIds.Length == 0)
            {
                throw new ArgumentException("A P4 mask root is noncanonical.");
            }
            var boundaryIds = boundaryNames[index].Select(name => skeleton.LogicalBones.Single(value =>
                string.Equals(value.Name, name, StringComparison.Ordinal)).LogicalId).ToArray();
            var expectedMembers = skeleton.LogicalBones.Where(bone =>
                    IsDescendantOrSelf(skeleton.LogicalBones, bone.LogicalId, entry.RootBoneId) &&
                    !boundaryIds.Any(boundary =>
                        IsDescendantOrSelf(skeleton.LogicalBones, bone.LogicalId, boundary)))
                .Select(value => value.LogicalId).Order().ToArray();
            if (!entry.BoneIds.SequenceEqual(expectedMembers))
            {
                throw new ArgumentException("A P4 mask differs from its canonical boundary expansion.");
            }
            var previous = -1;
            foreach (var boneId in entry.BoneIds)
            {
                if (boneId <= previous || !claimed.Add(boneId) ||
                    !IsDescendantOrSelf(skeleton.LogicalBones, boneId, entry.RootBoneId))
                {
                    throw new ArgumentException("P4 mask ownership is not sorted, unique and descendant-closed.");
                }
                previous = boneId;
            }
        }
    }

    private static void ValidateFootProvenance(
        AlsAnimationSetDefinition set,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsSkeletonDefinition skeleton)
    {
        var masks = pose.Masks.Entries;
        var feet = pose.Feet;
        if (feet.LeftLegRootBoneId != masks[(int)AlsPoseMaskKind.LeftLeg].RootBoneId ||
            feet.RightLegRootBoneId != masks[(int)AlsPoseMaskKind.RightLeg].RootBoneId ||
            feet.LeftFootRootBoneId != masks[(int)AlsPoseMaskKind.LeftFoot].RootBoneId ||
            feet.RightFootRootBoneId != masks[(int)AlsPoseMaskKind.RightFoot].RootBoneId ||
            feet.TraceUpMeters != .5f || feet.TraceDownMeters != .75f ||
            feet.FootHeightMeters != .13f || feet.MaxPelvisCorrectionMeters != .4f ||
            feet.PositionHalfLifeSeconds != .08f || feet.RotationHalfLifeSeconds != .1f ||
            feet.LockReleaseHalfLifeSeconds != .12f || feet.PelvisUpHalfLifeSeconds != .08f ||
            feet.PelvisDownHalfLifeSeconds != .1f || feet.MaximumLegReachMeters != 1.2f ||
            feet.CapsuleHalfHeightSource != AlsCapsuleHalfHeightSource.CharacterController ||
            feet.MaximumThighAngleRadians != MathF.PI / 2f ||
            feet.MaximumFootAngleRadians != 40f * MathF.PI / 180f ||
            feet.PlatformTeleportDistanceMeters != 1f ||
            feet.PlatformTeleportAngleRadians != 45f * MathF.PI / 180f ||
            feet.LockWeightEpsilon != .0001f)
        {
            throw new ArgumentException("The P4 foot-placement settings are noncanonical.");
        }

        var pelvis = PhysicalBoneId(skeleton, skeleton.RequiredBones.Pelvis);
        var left = CompileLeg(skeleton, pelvis, feet.LeftLegRootBoneId, feet.LeftFootRootBoneId);
        var right = CompileLeg(skeleton, pelvis, feet.RightLegRootBoneId, feet.RightFootRootBoneId);
        if (pose.FootRig != new AlsCompiledFootRig(pelvis, left, right))
        {
            throw new ArgumentException("The P4 compiled foot rig differs from the current skeleton.");
        }

        var reachable = new List<int>();
        var unique = new HashSet<int>();
        Add(locomotion.StandingIdleAnimationId);
        Add(locomotion.CrouchingIdleAnimationId);
        foreach (var value in locomotion.StandingSamples) Add(value.AnimationId);
        foreach (var value in locomotion.CrouchingSamples) Add(value.AnimationId);
        Add(locomotion.JumpStartAnimationId);
        Add(locomotion.FallLoopAnimationId);
        Add(locomotion.LandAnimationId);
        foreach (var value in pose.Turns) Add(value.AnimationId);
        foreach (var value in pose.Rotates) Add(value.AnimationId);

        var bindings = pose.FootCurves.Bindings;
        if (bindings.Length != reachable.Count || pose.FootCurves.GroundedIkWeight != 1f ||
            pose.FootCurves.JumpStartIkWeight != 0f || pose.FootCurves.FallLoopIkWeight != 0f ||
            pose.FootCurves.LandRecoveryIkWeight != 1f)
        {
            throw new ArgumentException("The P4 foot curve closure or unit IK defaults are noncanonical.");
        }
        for (var index = 0; index < bindings.Length; index++)
        {
            var value = bindings[index];
            var animation = set.Animations[reachable[index]];
            var leftCurve = ExactSourceCurveId(animation, "FootLock_L");
            var rightCurve = ExactSourceCurveId(animation, "FootLock_R");
            if (value.AnimationId != animation.Id || value.LeftLockCurveId != leftCurve ||
                value.RightLockCurveId != rightCurve ||
                BitConverter.SingleToUInt32Bits(value.LeftLockDefault) != 0 ||
                BitConverter.SingleToUInt32Bits(value.RightLockDefault) != 0)
            {
                throw new ArgumentException("A P4 foot curve binding differs from current animation provenance.");
            }
        }

        void Add(int animationId)
        {
            if (unique.Add(animationId)) reachable.Add(animationId);
        }
    }

    private static int ExactSourceCurveId(AlsAnimationDefinition animation, string sourceName)
    {
        var matches = animation.Curves.Where(value =>
            string.Equals(value.SourceName, sourceName, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            0 => -1,
            1 => matches[0].CurveId,
            _ => throw new ArgumentException("An animation contains duplicate canonical source curves."),
        };
    }

    private static int PhysicalBoneId(AlsSkeletonDefinition skeleton, int logicalId)
    {
        if ((uint)logicalId >= (uint)skeleton.LogicalToPhysical.Length ||
            (uint)skeleton.LogicalToPhysical[logicalId] >= (uint)skeleton.PhysicalBones.Length)
        {
            throw new ArgumentException("A P4 foot bone has no physical mapping.");
        }
        return skeleton.LogicalToPhysical[logicalId];
    }

    private static AlsCompiledLegChain CompileLeg(
        AlsSkeletonDefinition skeleton,
        int pelvis,
        int thighLogical,
        int footLogical)
    {
        var thigh = PhysicalBoneId(skeleton, thighLogical);
        var foot = PhysicalBoneId(skeleton, footLogical);
        var knee = skeleton.PhysicalBones[foot].ParentPhysicalId;
        if ((uint)knee >= (uint)skeleton.PhysicalBones.Length ||
            skeleton.PhysicalBones[knee].ParentPhysicalId != thigh ||
            skeleton.PhysicalBones[thigh].ParentPhysicalId != pelvis)
        {
            throw new ArgumentException("A P4 foot rig chain is not contiguous.");
        }
        return new AlsCompiledLegChain(thigh, knee, foot);
    }

    private static void ValidateSkeletonMaps(AlsSkeletonDefinition skeleton)
    {
        if (skeleton.LogicalBones.Length == 0 || skeleton.PhysicalBones.Length == 0 ||
            skeleton.LogicalToPhysical.Length != skeleton.LogicalBones.Length ||
            skeleton.PhysicalToLogical.Length != skeleton.PhysicalBones.Length)
        {
            throw new ArgumentException("The skeleton mapping table is incomplete.");
        }
        for (var index = 0; index < skeleton.LogicalBones.Length; index++)
        {
            var bone = skeleton.LogicalBones[index];
            if (bone.LogicalId != index || bone.ParentLogicalId >= index || bone.ParentLogicalId < -1)
            {
                throw new ArgumentException("The logical skeleton hierarchy is invalid.");
            }
            var physical = skeleton.LogicalToPhysical[index];
            if (physical >= 0 && ((uint)physical >= (uint)skeleton.PhysicalToLogical.Length ||
                skeleton.PhysicalToLogical[physical] != index))
            {
                throw new ArgumentException("Logical/physical skeleton mappings disagree.");
            }
        }
        for (var index = 0; index < skeleton.PhysicalBones.Length; index++)
        {
            var logical = skeleton.PhysicalToLogical[index];
            if ((uint)logical >= (uint)skeleton.LogicalToPhysical.Length ||
                skeleton.LogicalToPhysical[logical] != index)
            {
                throw new ArgumentException("Physical/logical skeleton mappings disagree.");
            }
        }
    }

    private static AlsP5GraphSample[] MapSamples(
        AlsLocomotionAnimationSample[] values,
        AlsAnimationDefinition[] animations,
        int skeletonId)
    {
        var result = new AlsP5GraphSample[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index] ?? throw new ArgumentException("A graph sample is null.");
            ValidateAnimationId(animations, value.AnimationId, skeletonId);
            if (!Finite(value.X) || !Finite(value.Y) || !Positive(value.RateScale))
            {
                throw new ArgumentException("A graph sample is invalid.");
            }
            result[index] = new AlsP5GraphSample(value.AnimationId, value.X, value.Y, value.RateScale);
        }
        return result;
    }

    private static void CompileMasks(
        AlsLayerMaskProfile masks,
        AlsSkeletonDefinition skeleton,
        out AlsP5GraphMaskHeader[] headers,
        out int[] logicalBoneIds)
    {
        var entries = masks.Entries;
        var kinds = Enum.GetValues<AlsPoseMaskKind>();
        if (entries.Length != kinds.Length)
        {
            throw new ArgumentException("The graph mask table is incomplete.");
        }
        headers = new AlsP5GraphMaskHeader[entries.Length];
        var flattened = new List<int>();
        var seenKinds = new HashSet<AlsPoseMaskKind>();
        for (var index = 0; index < entries.Length; index++)
        {
            var mask = entries[index] ?? throw new ArgumentException("A graph mask is null.");
            if (!Enum.IsDefined(mask.Kind) || mask.Kind != kinds[index] || !seenKinds.Add(mask.Kind) ||
                (uint)mask.RootBoneId >= (uint)skeleton.LogicalBones.Length || mask.BoneIds.Length == 0)
            {
                throw new ArgumentException("A graph mask header is invalid.");
            }
            var members = new HashSet<int>();
            var offset = flattened.Count;
            foreach (var boneId in mask.BoneIds)
            {
                if ((uint)boneId >= (uint)skeleton.LogicalBones.Length || !members.Add(boneId) ||
                    !IsDescendantOrSelf(skeleton.LogicalBones, boneId, mask.RootBoneId))
                {
                    throw new ArgumentException("A graph mask member is invalid.");
                }
                flattened.Add(boneId);
            }
            headers[index] = new AlsP5GraphMaskHeader(mask.Kind, mask.RootBoneId,
                offset, mask.BoneIds.Length);
        }
        logicalBoneIds = flattened.ToArray();
    }

    private static bool IsDescendantOrSelf(AlsBoneDefinition[] bones, int boneId, int rootId)
    {
        for (var current = boneId; current >= 0; current = bones[current].ParentLogicalId)
        {
            if (current == rootId) return true;
        }
        return false;
    }

    private static void CompileCurves(
        AlsAnimationDefinition[] animations,
        AlsCompiledCurveSemantic allowTransitions,
        out AlsCurveKey[] keys,
        out AlsCurveBinding[] bindings,
        out AlsP5CurveBindingIdentity[] identities,
        out AlsAnimationCurveRange[] ranges,
        out AlsP5CurveSemanticPolicy policy,
        out int[] allowTransitionIndices)
    {
        var keyList = new List<AlsCurveKey>();
        var bindingList = new List<AlsCurveBinding>();
        var identityList = new List<AlsP5CurveBindingIdentity>();
        ranges = new AlsAnimationCurveRange[animations.Length];
        for (var animationId = 0; animationId < animations.Length; animationId++)
        {
            var animation = animations[animationId];
            var bindingOffset = bindingList.Count;
            foreach (var curve in animation.Curves)
            {
                var keyOffset = keyList.Count;
                foreach (var key in curve.Keys)
                {
                    var interpolation = key.Interpolation switch
                    {
                        AlsCurveInterpolation.Constant => AlsCurveInterpolationMode.Constant,
                        AlsCurveInterpolation.Linear => AlsCurveInterpolationMode.Linear,
                        AlsCurveInterpolation.Cubic => AlsCurveInterpolationMode.Cubic,
                        _ => throw new ArgumentException("The curve interpolation is unknown."),
                    };
                    keyList.Add(new AlsCurveKey(key.TimeSeconds, key.Value,
                        key.ArriveTangent, key.LeaveTangent, interpolation));
                }
                bindingList.Add(new AlsCurveBinding(curve.CurveId, keyOffset, curve.Keys.Length,
                    animation.PlayLength, 1, animation.Loop ? (byte)1 : (byte)0));
                identityList.Add(new AlsP5CurveBindingIdentity(animationId, curve.CurveId));
            }
            ranges[animationId] = new AlsAnimationCurveRange(animationId, bindingOffset,
                bindingList.Count - bindingOffset);
        }
        keys = keyList.ToArray();
        bindings = bindingList.ToArray();
        identities = identityList.ToArray();

        if (allowTransitions.AnimationCurveIds.Length != animations.Length ||
            allowTransitions.MissingValue != 1f || allowTransitions.ClampMinimum != 0f ||
            allowTransitions.ClampMaximum != 1f ||
            allowTransitions.CombineMode != AlsP5CurveCombineMode.AdditiveToDefault)
        {
            throw new ArgumentException("The AllowTransitions semantic is invalid.");
        }
        var combine = allowTransitions.CombineMode switch
        {
            AlsP5CurveCombineMode.AdditiveToDefault =>
                GodotAls.Core.Animation.AlsP5CurveCombineMode.AdditiveToDefault,
            _ => throw new ArgumentException("The curve combine mode is unknown."),
        };
        policy = new AlsP5CurveSemanticPolicy(allowTransitions.MissingValue, combine,
            allowTransitions.ClampMinimum, allowTransitions.ClampMaximum);
        allowTransitionIndices = new int[animations.Length];
        for (var animationId = 0; animationId < animations.Length; animationId++)
        {
            var curveId = allowTransitions.AnimationCurveIds[animationId];
            var authoredCurveId = ExactSourceCurveId(animations[animationId], "Enable_Transition");
            if (curveId != authoredCurveId)
            {
                throw new ArgumentException("AllowTransitions differs from the current animation curve mapping.");
            }
            if (curveId == -1)
            {
                allowTransitionIndices[animationId] = -1;
                continue;
            }
            var range = ranges[animationId];
            var found = -1;
            for (var offset = 0; offset < range.BindingCount; offset++)
            {
                var index = range.BindingOffset + offset;
                if (identities[index].CurveId == curveId)
                {
                    if (found >= 0) throw new ArgumentException("AllowTransitions curve identity is duplicate.");
                    found = index;
                }
            }
            if (found < 0) throw new ArgumentException("AllowTransitions curve identity is missing.");
            allowTransitionIndices[animationId] = found;
        }
    }

    private static void CompileFootCurves(
        AlsFootCurveProfile source,
        AlsAnimationDefinition[] animations,
        int skeletonId,
        out AlsP4FootCurveRuntimeBinding[] bindings)
    {
        if (!Finite(source.GroundedIkWeight) || !Finite(source.JumpStartIkWeight) ||
            !Finite(source.FallLoopIkWeight) || !Finite(source.LandRecoveryIkWeight))
        {
            throw new ArgumentException("Foot IK constants must be finite.");
        }
        var values = source.Bindings;
        bindings = new AlsP4FootCurveRuntimeBinding[values.Length];
        var animationIds = new HashSet<int>();
        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            ValidateAnimationId(animations, value.AnimationId, skeletonId);
            if (!animationIds.Add(value.AnimationId) || !Finite(value.LeftLockDefault) ||
                !Finite(value.RightLockDefault))
            {
                throw new ArgumentException("A foot curve binding is invalid.");
            }
            ValidateOptionalCurve(animations[value.AnimationId], value.LeftLockCurveId);
            ValidateOptionalCurve(animations[value.AnimationId], value.RightLockCurveId);
            bindings[index] = new AlsP4FootCurveRuntimeBinding(value.AnimationId,
                value.LeftLockCurveId, value.RightLockCurveId,
                value.LeftLockDefault, value.RightLockDefault);
        }
    }

    private static void ValidateRuntimeProfile(
        AlsP5aAnimationRuntimeProfile p5a,
        AlsMontageDefinition[] montages,
        AlsAnimationDefinition[] animations,
        int skeletonId,
        CoreOccurrenceEntry[] occurrenceEntries,
        AlsP5OccurrenceLayoutEntry[] importEntries)
    {
        if (p5a.SchemaVersion != Version || p5a.Actions.Length == 0)
        {
            throw new ArgumentException("The P5A runtime profile header is invalid.");
        }
        var actionIds = new HashSet<int>();
        for (var index = 0; index < p5a.Actions.Length; index++)
        {
            var action = p5a.Actions[index];
            if (action.DefinitionId != index || !actionIds.Add(action.DefinitionId) ||
                (uint)action.MontageId >= (uint)montages.Length ||
                montages[action.MontageId].Id != action.MontageId ||
                action.MontageDurationSeconds != montages[action.MontageId].PlayLength ||
                !Positive(action.MontageDurationSeconds) ||
                action.SlotId < 0 || action.StartSectionId < 0 || action.Priority < 0 ||
                !Positive(action.PlayRate) || !Positive(action.BlendSeconds) ||
                !Enum.IsDefined(action.LoopPolicy) || action.Sections.Length == 0)
            {
                throw new ArgumentException("An Action definition is invalid.");
            }
            RequireOccurrence(occurrenceEntries, CoreOccurrenceKind.ActionMontage, index);
            var sectionIds = new HashSet<int>();
            foreach (var section in action.Sections)
            {
                if (section.ActionDefinitionId != index || section.SectionId < 0 ||
                    !sectionIds.Add(section.SectionId) || section.NextSectionId < -1 ||
                    !Finite(section.StartTime) || !Finite(section.EndTime) ||
                    section.StartTime < 0f || section.EndTime <= section.StartTime ||
                    section.EndTime > action.MontageDurationSeconds)
                {
                    throw new ArgumentException("An Action section is invalid.");
                }
            }
            if (!sectionIds.Contains(action.StartSectionId) ||
                action.Sections.Any(value => value.NextSectionId >= 0 && !sectionIds.Contains(value.NextSectionId)))
            {
                throw new ArgumentException("The Action section graph is invalid.");
            }
        }
        for (var index = 0; index < p5a.SegmentBindings.Length; index++)
        {
            var segment = p5a.SegmentBindings[index];
            if ((uint)segment.ActionDefinitionId >= (uint)p5a.Actions.Length ||
                segment.SlotId != p5a.Actions[segment.ActionDefinitionId].SlotId ||
                segment.SegmentId < 0 || (uint)segment.AnimationId >= (uint)animations.Length ||
                animations[segment.AnimationId].SkeletonId != skeletonId ||
                !Finite(segment.MontageStartTime) || !Finite(segment.MontageEndTime) ||
                !Finite(segment.AnimationStartTime) || !Finite(segment.AnimationEndTime) ||
                !Positive(segment.PlayRate) || segment.LoopCount <= 0 ||
                segment.MontageStartTime < 0f || segment.MontageEndTime <= segment.MontageStartTime ||
                segment.MontageEndTime > p5a.Actions[segment.ActionDefinitionId].MontageDurationSeconds ||
                segment.AnimationStartTime < 0f || segment.AnimationEndTime <= segment.AnimationStartTime ||
                segment.AnimationEndTime > animations[segment.AnimationId].PlayLength)
            {
                throw new ArgumentException("An Action segment is invalid.");
            }
            RequireOccurrence(occurrenceEntries, CoreOccurrenceKind.ActionSequence, index);
        }
        var previousAction = -1;
        var previousTime = float.NegativeInfinity;
        foreach (var timeline in p5a.TimelineEntries)
        {
            if ((uint)timeline.ActionDefinitionId >= (uint)p5a.Actions.Length ||
                !Enum.IsDefined(timeline.Kind) || !Enum.IsDefined(timeline.SourceKind) ||
                timeline.EventId < 0 || timeline.SourceIndex < 0 || timeline.TrackIndex < 0 ||
                timeline.BoundaryOrdinal < 0 || !Finite(timeline.TimeSeconds) ||
                !Finite(timeline.DurationSeconds) || !Finite(timeline.TriggerWeightThreshold) ||
                timeline.TimeSeconds < 0f || timeline.DurationSeconds < 0f ||
                timeline.TimeSeconds + timeline.DurationSeconds >
                    p5a.Actions[timeline.ActionDefinitionId].MontageDurationSeconds + 1e-6f ||
                timeline.TriggerWeightThreshold < 0f || timeline.TriggerWeightThreshold > 1f ||
                !Enum.IsDefined(timeline.TickMode))
            {
                throw new ArgumentException("An Action timeline row is invalid.");
            }
            if (timeline.ActionDefinitionId < previousAction ||
                timeline.ActionDefinitionId == previousAction && timeline.TimeSeconds < previousTime)
            {
                throw new ArgumentException("Action timeline rows are not in compiled order.");
            }
            previousAction = timeline.ActionDefinitionId;
            previousTime = timeline.TimeSeconds;
            if (timeline.SourceKind == AlsCompiledActionTimelineSourceKind.Montage)
            {
                if (timeline.SegmentBindingIndex != -1 ||
                    timeline.SourceAssetId != p5a.Actions[timeline.ActionDefinitionId].MontageId)
                {
                    throw new ArgumentException("An Action Montage timeline key is invalid.");
                }
            }
            else if ((uint)timeline.SegmentBindingIndex >= (uint)p5a.SegmentBindings.Length ||
                p5a.SegmentBindings[timeline.SegmentBindingIndex].ActionDefinitionId != timeline.ActionDefinitionId ||
                timeline.SourceAssetId != p5a.SegmentBindings[timeline.SegmentBindingIndex].AnimationId)
            {
                throw new ArgumentException("An Action Sequence timeline key is invalid.");
            }
            _ = MapPayload(timeline.Kind, timeline.Payload, new int[6]);
        }

        foreach (var entry in importEntries)
        {
            if (entry.SourceKind == AlsP5OccurrenceSourceKind.ActionMontage &&
                    entry.SourceBindingIndex >= p5a.Actions.Length ||
                entry.SourceKind == AlsP5OccurrenceSourceKind.ActionSequence &&
                    entry.SourceBindingIndex >= p5a.SegmentBindings.Length)
            {
                throw new ArgumentException("The layout references a missing Action binding.");
            }
        }
    }

    private static void ValidateOccurrenceClosure(
        CoreOccurrenceEntry[] entries,
        int baseCount,
        int turnCount,
        int rotateCount,
        int actionCount,
        int segmentCount)
    {
        var expectedCount = checked(baseCount + turnCount + rotateCount + 1 + actionCount + segmentCount);
        if (entries.Length != expectedCount)
        {
            throw new ArgumentException("The P5 occurrence layout closure is incomplete.");
        }
        ValidateBank(CoreOccurrenceKind.Base, baseCount, graphSlotIsBinding: true);
        ValidateBank(CoreOccurrenceKind.Turn, turnCount, graphSlotIsBinding: true);
        ValidateBank(CoreOccurrenceKind.Rotate, rotateCount, graphSlotIsBinding: true);
        ValidateBank(CoreOccurrenceKind.Transition, 1, graphSlotIsBinding: false);
        ValidateBank(CoreOccurrenceKind.ActionMontage, actionCount, graphSlotIsBinding: false);
        ValidateBank(CoreOccurrenceKind.ActionSequence, segmentCount, graphSlotIsBinding: false);

        void ValidateBank(CoreOccurrenceKind kind, int count, bool graphSlotIsBinding)
        {
            var bank = entries.Where(value => value.SourceKind == kind).ToArray();
            if (bank.Length != count)
            {
                throw new ArgumentException("A P5 occurrence bank is incomplete.");
            }
            for (var index = 0; index < bank.Length; index++)
            {
                if (bank[index].SourceBindingIndex != index ||
                    bank[index].GraphSlotIndex != (graphSlotIsBinding ? index : 0))
                {
                    throw new ArgumentException("A P5 occurrence bank key is invalid.");
                }
            }
        }
    }

    private static List<AlsTimelineEventDefinition> CompileGeneralTimeline(
        AlsAnimationDefinition[] animations,
        int[] baseAnimationIds,
        AlsTurnProfile[] turns,
        AlsRotateProfile[] rotates,
        AlsCompiledDynamicTransition transition,
        CoreOccurrenceEntry[] occurrences,
        int[] semanticIds)
    {
        var result = new List<AlsTimelineEventDefinition>();
        foreach (var occurrence in occurrences)
        {
            switch (occurrence.SourceKind)
            {
                case CoreOccurrenceKind.Base:
                    if ((uint)occurrence.SourceBindingIndex >= (uint)baseAnimationIds.Length)
                        throw new ArgumentException("A Base occurrence binding is invalid.");
                    AddAnimation(baseAnimationIds[occurrence.SourceBindingIndex]);
                    break;
                case CoreOccurrenceKind.Turn:
                    if ((uint)occurrence.SourceBindingIndex >= (uint)turns.Length)
                        throw new ArgumentException("A Turn occurrence binding is invalid.");
                    AddAnimation(turns[occurrence.SourceBindingIndex].AnimationId);
                    break;
                case CoreOccurrenceKind.Rotate:
                    if ((uint)occurrence.SourceBindingIndex >= (uint)rotates.Length)
                        throw new ArgumentException("A Rotate occurrence binding is invalid.");
                    AddAnimation(rotates[occurrence.SourceBindingIndex].AnimationId);
                    break;
                case CoreOccurrenceKind.Transition:
                    if (occurrence.SourceBindingIndex != 0)
                        throw new ArgumentException("The Transition occurrence binding is invalid.");
                    foreach (var animationId in transition.Slots.Select(value => value.AnimationId).Distinct())
                        AddAnimation(animationId);
                    break;
            }

            void AddAnimation(int animationId)
            {
                foreach (var value in animations[animationId].Timeline)
                {
                    result.Add(new AlsTimelineEventDefinition(value.EventId, animationId, -1,
                        occurrence.OccurrenceHandleId, AlsTimelineSourceKind.Animation,
                        value.SourceIndex, value.TrackIndex, 0, value.TimeSeconds,
                        value.DurationSeconds, value.TriggerWeightThreshold,
                        MapKind(value.Kind), MapTick(value.TickMode),
                        MapPayload(value.Kind, value.Payload, semanticIds)));
                }
            }
        }
        return result;
    }

    private static void CompileSync(
        AlsP5aAnimationRuntimeProfile p5a,
        AlsP5OccurrenceLayout layout,
        AlsAnimationDefinition[] animations,
        int skeletonId,
        int[] baseAnimationIds,
        CoreOccurrenceEntry[] occurrences,
        out AlsSyncMarkerDefinition[] markers,
        out AlsSyncGroupBinding group,
        out AlsSyncMemberBinding[] members,
        out AlsP5SyncOccurrenceBinding[] mappedOccurrences)
    {
        if (p5a.SyncGroups.Length != 1 || p5a.SyncGroups[0].GroupId != 0)
        {
            throw new ArgumentException("Exactly one canonical P5A Sync group is required.");
        }
        var sourceMembers = p5a.SyncGroups[0].Members;
        var sourceMappings = layout.SyncMappings;
        if (sourceMembers.Length == 0 || sourceMappings.Length != sourceMembers.Length)
        {
            throw new ArgumentException("The P5A Sync occurrence map is incomplete.");
        }
        members = new AlsSyncMemberBinding[sourceMembers.Length];
        mappedOccurrences = new AlsP5SyncOccurrenceBinding[sourceMembers.Length];
        markers = new AlsSyncMarkerDefinition[checked(sourceMembers.Length * 2)];
        var animationIds = new HashSet<int>();
        for (var index = 0; index < sourceMembers.Length; index++)
        {
            var member = sourceMembers[index];
            if (member.GroupMemberIndex != index || !animationIds.Add(member.AnimationId) ||
                (uint)member.AnimationId >= (uint)animations.Length ||
                animations[member.AnimationId].SkeletonId != skeletonId ||
                !Positive(member.DurationSeconds) ||
                member.DurationSeconds != animations[member.AnimationId].PlayLength ||
                member.LoopPolicy != AlsP5LoopPolicy.Loop || !member.CanLead)
            {
                throw new ArgumentException("A Sync member is invalid.");
            }
            var left = FindMarker(animations[member.AnimationId], member.LeftMarkerId, "Left");
            var right = FindMarker(animations[member.AnimationId], member.RightMarkerId, "Right");
            markers[index * 2] = new AlsSyncMarkerDefinition(left.MarkerId, 0, member.AnimationId,
                left.SourceIndex, left.TrackIndex, left.TimeSeconds);
            markers[index * 2 + 1] = new AlsSyncMarkerDefinition(right.MarkerId, 1, member.AnimationId,
                right.SourceIndex, right.TrackIndex, right.TimeSeconds);
            members[index] = new AlsSyncMemberBinding(0, member.AnimationId, member.DurationSeconds,
                member.LoopPolicy == AlsP5LoopPolicy.Loop ? (byte)1 : (byte)0,
                member.CanLead ? (byte)1 : (byte)0);

            var mapping = sourceMappings[index];
            if (mapping.SyncGroupId != 0 || mapping.GroupMemberIndex != index ||
                mapping.AnimationId != member.AnimationId ||
                (uint)mapping.OccurrenceHandleId >= (uint)occurrences.Length)
            {
                throw new ArgumentException("A Sync occurrence mapping is invalid.");
            }
            var occurrence = occurrences[mapping.OccurrenceHandleId];
            if (occurrence.SourceKind != CoreOccurrenceKind.Base ||
                occurrence.OccurrenceHandleId != mapping.OccurrenceHandleId ||
                (uint)occurrence.SourceBindingIndex >= (uint)baseAnimationIds.Length ||
                baseAnimationIds[occurrence.SourceBindingIndex] != member.AnimationId)
            {
                throw new ArgumentException("A Sync occurrence does not resolve to Base.");
            }
            mappedOccurrences[index] = new AlsP5SyncOccurrenceBinding(0, index,
                member.AnimationId, mapping.OccurrenceHandleId);
        }
        group = new AlsSyncGroupBinding(0, 0, members.Length, 0, 1);
    }

    private static AlsAnimationSyncMarkerDefinition FindMarker(
        AlsAnimationDefinition animation, int markerId, string name)
    {
        var matches = animation.SyncMarkers.Where(value =>
            value.MarkerId == markerId && string.Equals(value.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException("A Sync marker is missing or duplicate.");
    }

    private static AlsDynamicTransitionBinding CompileTransition(
        AlsCompiledDynamicTransition source,
        AlsAnimationDefinition[] animations,
        int skeletonId,
        CoreOccurrenceEntry[] occurrences)
    {
        var slots = source.Slots;
        if (slots.Length != 4 || !Positive(source.DistanceMeters) ||
            !Positive(source.BlendSeconds) || !Positive(source.PlayRate) || source.CooldownFrames < 0)
        {
            throw new ArgumentException("The Dynamic Transition binding is invalid.");
        }
        var expected = new[]
        {
            (AlsP5TransitionStance.Standing, AlsP5TransitionFoot.Left),
            (AlsP5TransitionStance.Standing, AlsP5TransitionFoot.Right),
            (AlsP5TransitionStance.Crouching, AlsP5TransitionFoot.Left),
            (AlsP5TransitionStance.Crouching, AlsP5TransitionFoot.Right),
        };
        var clips = new AlsDynamicTransitionClipBinding[4];
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (slot.SlotIndex != index || !Enum.IsDefined(slot.Stance) || !Enum.IsDefined(slot.Foot) ||
                (slot.Stance, slot.Foot) != expected[index] ||
                (uint)slot.AnimationId >= (uint)animations.Length ||
                (uint)slot.AdditiveBaseAnimationId >= (uint)animations.Length ||
                animations[slot.AnimationId].SkeletonId != skeletonId ||
                animations[slot.AdditiveBaseAnimationId].SkeletonId != skeletonId ||
                animations[slot.AnimationId].AdditiveType == 0 ||
                animations[slot.AdditiveBaseAnimationId].AdditiveType != 0 ||
                animations[slot.AnimationId].AdditiveBasePoseAnimationId != slot.AdditiveBaseAnimationId)
            {
                throw new ArgumentException("A Dynamic Transition slot is invalid.");
            }
            clips[index] = new AlsDynamicTransitionClipBinding(slot.AnimationId,
                slot.AdditiveBaseAnimationId, animations[slot.AnimationId].PlayLength);
        }
        var occurrence = RequireOccurrence(occurrences, CoreOccurrenceKind.Transition, 0);
        return new AlsDynamicTransitionBinding(occurrence.OccurrenceHandleId,
            occurrence.AuthorityGroupId, clips[0], clips[1], clips[2], clips[3],
            source.DistanceMeters, source.BlendSeconds, source.PlayRate, source.CooldownFrames);
    }

    private static void ValidateActionProvenance(
        AlsAnimationSetDefinition set,
        AlsP5aAnimationRuntimeProfile p5a)
    {
        var expectedSegments = new List<AlsCompiledActionSegmentBinding>();
        var expectedTimeline = new List<AlsCompiledActionTimelineEntry>();
        long expandedTimelineEntries = 0;
        for (var actionIndex = 0; actionIndex < p5a.Actions.Length; actionIndex++)
        {
            var action = p5a.Actions[actionIndex];
            if ((uint)action.MontageId >= (uint)set.Montages.Length)
                throw new ArgumentException("An Action Montage is missing from the current set.");
            var montage = set.Montages[action.MontageId];
            if (montage.Id != action.MontageId || montage.PlayLength != action.MontageDurationSeconds ||
                montage.Slots.Length != 1 || montage.Slots[0].SlotId != action.SlotId)
            {
                throw new ArgumentException("An Action definition differs from its current Montage.");
            }

            var expectedSections = new AlsCompiledActionSectionBinding[montage.Sections.Length];
            for (var sectionIndex = 0; sectionIndex < montage.Sections.Length; sectionIndex++)
            {
                var section = montage.Sections[sectionIndex];
                var end = sectionIndex + 1 < montage.Sections.Length
                    ? montage.Sections[sectionIndex + 1].StartTime
                    : montage.PlayLength;
                if (section.SectionId != sectionIndex || !Finite(section.StartTime) || !Finite(end) ||
                    section.StartTime < 0f || end <= section.StartTime || end > montage.PlayLength + 1e-6f ||
                    section.NextSectionId < -1 || section.NextSectionId >= montage.Sections.Length)
                {
                    throw new ArgumentException("Current Action Montage sections are invalid.");
                }
                expectedSections[sectionIndex] = new AlsCompiledActionSectionBinding(actionIndex,
                    section.SectionId, section.NextSectionId, section.StartTime, end);
            }
            if (!action.Sections.SequenceEqual(expectedSections))
                throw new ArgumentException("Action sections differ from the current Montage.");
            ValidateActionSectionGraph(expectedSections, action.StartSectionId, action.LoopPolicy);

            var firstSegmentIndex = expectedSegments.Count;
            var slot = montage.Slots[0];
            if (slot.Segments.Length == 0) throw new ArgumentException("Current Action slot has no segments.");
            for (var segmentIndex = 0; segmentIndex < slot.Segments.Length; segmentIndex++)
            {
                var segment = slot.Segments[segmentIndex];
                if (segment.SegmentId != segmentIndex ||
                    (uint)segment.AnimationId >= (uint)set.Animations.Length)
                {
                    throw new ArgumentException("A current Action segment is unresolved.");
                }
                var animation = set.Animations[segment.AnimationId];
                var montageEnd = segmentIndex + 1 < slot.Segments.Length
                    ? slot.Segments[segmentIndex + 1].StartPosition
                    : montage.PlayLength;
                if (animation.AdditiveType != 0 || segment.LoopCount <= 0 || segment.LoopCount == int.MaxValue ||
                    !Positive(segment.PlayRate) || !Finite(segment.StartPosition) ||
                    !Finite(segment.AnimationStartTime) || !Finite(segment.AnimationEndTime) ||
                    segment.StartPosition < 0f || segment.AnimationStartTime < 0f ||
                    segment.AnimationEndTime <= segment.AnimationStartTime ||
                    segment.AnimationEndTime > animation.PlayLength + 1e-6f)
                {
                    throw new ArgumentException("A current Action segment mapping equation is invalid.");
                }
                long estimatedEntries;
                try
                {
                    estimatedEntries = checked((long)segment.LoopCount * animation.Timeline.Length);
                }
                catch (OverflowException exception)
                {
                    throw new ArgumentException("The Action timeline expansion budget overflowed.", exception);
                }
                AddActionTimelineBudget(ref expandedTimelineEntries, estimatedEntries);
                var mappedDuration = (double)segment.LoopCount *
                    ((double)segment.AnimationEndTime - segment.AnimationStartTime) / segment.PlayRate;
                if (!double.IsFinite(mappedDuration) || !Finite(montageEnd) ||
                    montageEnd <= segment.StartPosition ||
                    montageEnd > montage.PlayLength + 1e-6f ||
                    Math.Abs(((double)montageEnd - segment.StartPosition) - mappedDuration) > 1e-8)
                {
                    throw new ArgumentException("A current Action segment mapping equation is invalid.");
                }
                expectedSegments.Add(new AlsCompiledActionSegmentBinding(actionIndex, slot.SlotId,
                    segment.SegmentId, segment.AnimationId, segment.StartPosition, montageEnd,
                    segment.AnimationStartTime, segment.AnimationEndTime, segment.PlayRate,
                    segment.LoopCount));
            }
            var currentSegments = expectedSegments.Skip(firstSegmentIndex).ToArray();
            foreach (var section in expectedSections)
            {
                var cursor = section.StartTime;
                foreach (var segment in currentSegments.OrderBy(value => value.MontageStartTime))
                {
                    if (segment.MontageEndTime <= cursor + 1e-6f) continue;
                    if (segment.MontageStartTime > cursor + 1e-6f) break;
                    cursor = Math.Max(cursor, segment.MontageEndTime);
                    if (cursor >= section.EndTime - 1e-6f) break;
                }
                if (cursor < section.EndTime - 1e-6f)
                    throw new ArgumentException("An Action section is not covered by current segments.");
            }

            AddActionTimelineBudget(ref expandedTimelineEntries, montage.Timeline.Length);
            foreach (var value in montage.Timeline)
            {
                ValidateTimelineSource(value, montage.Id, montage.PlayLength);
                expectedTimeline.Add(ToActionTimelineEntry(actionIndex, value,
                    AlsCompiledActionTimelineSourceKind.Montage, montage.Id, -1,
                    value.TimeSeconds, value.DurationSeconds, 0));
            }
            for (var segmentOffset = 0; segmentOffset < slot.Segments.Length; segmentOffset++)
            {
                var bindingIndex = firstSegmentIndex + segmentOffset;
                AddExpectedSequenceTimeline(bindingIndex, expectedSegments[bindingIndex],
                    set.Animations[expectedSegments[bindingIndex].AnimationId], expectedTimeline);
            }
        }

        if (!p5a.SegmentBindings.SequenceEqual(expectedSegments))
            throw new ArgumentException("Action segments differ from the current Montage slot.");
        var sorted = expectedTimeline
            .OrderBy(value => value.ActionDefinitionId)
            .ThenBy(value => value.TimeSeconds)
            .ThenBy(value => value.SourceKind)
            .ThenBy(value => value.SegmentBindingIndex)
            .ThenBy(value => value.EventId)
            .ThenBy(value => value.SourceIndex)
            .ThenBy(value => value.BoundaryOrdinal)
            .ThenBy(value => value.TrackIndex)
            .ToArray();
        if (!p5a.TimelineEntries.SequenceEqual(sorted))
            throw new ArgumentException("Action timeline rows differ from current Montage/Sequence provenance.");
    }

    private static void ValidateActionSectionGraph(
        AlsCompiledActionSectionBinding[] sections,
        int startSectionId,
        AlsP5LoopPolicy loopPolicy)
    {
        var seen = new HashSet<int>();
        var current = startSectionId;
        for (var step = 0; step <= sections.Length; step++)
        {
            if (current < 0)
            {
                if (loopPolicy == AlsP5LoopPolicy.Loop || seen.Count != sections.Length)
                    throw new ArgumentException("The Action section graph terminates or is incomplete.");
                return;
            }
            if ((uint)current >= (uint)sections.Length || !seen.Add(current))
            {
                if (current != startSectionId || loopPolicy != AlsP5LoopPolicy.Loop ||
                    seen.Count != sections.Length)
                    throw new ArgumentException("The Action section graph cycle is noncanonical.");
                return;
            }
            current = sections[current].NextSectionId;
        }
        throw new ArgumentException("The Action section graph is invalid.");
    }

    private static void AddExpectedSequenceTimeline(
        int bindingIndex,
        AlsCompiledActionSegmentBinding segment,
        AlsAnimationDefinition animation,
        List<AlsCompiledActionTimelineEntry> output)
    {
        if (animation.Timeline.Length == 0)
        {
            return;
        }
        var sourceRange = (double)segment.AnimationEndTime - segment.AnimationStartTime;
        var loopDuration = sourceRange / segment.PlayRate;
        for (var iteration = 0; iteration < segment.LoopCount; iteration++)
        {
            var ordinal = checked(iteration + 1);
            foreach (var value in animation.Timeline)
            {
                if (value.DurationSeconds <= 0f)
                {
                    if (value.TimeSeconds < segment.AnimationStartTime ||
                        value.TimeSeconds > segment.AnimationEndTime) continue;
                    var time = MapActionTime(segment, loopDuration, iteration, value.TimeSeconds);
                    if (iteration == segment.LoopCount - 1 && value.TimeSeconds == segment.AnimationEndTime)
                        time = segment.MontageEndTime;
                    output.Add(ToActionTimelineEntry(segment.ActionDefinitionId, value,
                        AlsCompiledActionTimelineSourceKind.Sequence, animation.Id, bindingIndex,
                        time, 0f, ordinal));
                    continue;
                }
                var begin = Math.Max((double)value.TimeSeconds, segment.AnimationStartTime);
                var end = Math.Min((double)value.TimeSeconds + value.DurationSeconds,
                    segment.AnimationEndTime);
                if (end <= begin) continue;
                var mappedBegin = MapActionTime(segment, loopDuration, iteration, begin);
                var mappedEnd = MapActionTime(segment, loopDuration, iteration, end);
                if (iteration == segment.LoopCount - 1 && end == segment.AnimationEndTime)
                    mappedEnd = segment.MontageEndTime;
                output.Add(ToActionTimelineEntry(segment.ActionDefinitionId, value,
                    AlsCompiledActionTimelineSourceKind.Sequence, animation.Id, bindingIndex,
                    mappedBegin, mappedEnd - mappedBegin, ordinal));
            }
        }
    }

    private static void AddActionTimelineBudget(ref long current, long additional)
    {
        long total;
        try
        {
            total = checked(current + additional);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("The Action timeline expansion budget overflowed.", exception);
        }
        if (total > MaximumExpandedTimelineEntries)
        {
            throw new ArgumentException(
                $"The Action timeline expansion budget exceeds {MaximumExpandedTimelineEntries} entries.");
        }
        current = total;
    }

    private static float MapActionTime(
        AlsCompiledActionSegmentBinding segment,
        double loopDuration,
        int iteration,
        double sourceTime) => (float)(segment.MontageStartTime + iteration * loopDuration +
            (sourceTime - segment.AnimationStartTime) / segment.PlayRate);

    private static AlsCompiledActionTimelineEntry ToActionTimelineEntry(
        int actionId,
        AlsCompiledTimelineEventDefinition value,
        AlsCompiledActionTimelineSourceKind sourceKind,
        int sourceAssetId,
        int segmentBindingIndex,
        float time,
        float duration,
        int ordinal) => new(actionId, value.EventId, value.Kind, sourceKind, sourceAssetId,
            segmentBindingIndex, value.SourceIndex, value.TrackIndex, time, duration,
            value.TriggerWeightThreshold, value.TickMode, value.Payload, ordinal);

    private static void CompileActions(
        AlsP5aAnimationRuntimeProfile p5a,
        CoreOccurrenceEntry[] occurrences,
        int[] semanticIds,
        List<AlsTimelineEventDefinition> generalTimeline,
        out AlsTimelineEventDefinition[] timeline,
        out AlsActionDefinition[] definitions,
        out AlsActionSectionBinding[] sections,
        out AlsActionSegmentBinding[] segments,
        out AlsActionTimelineRange[] ranges)
    {
        definitions = new AlsActionDefinition[p5a.Actions.Length];
        var sectionList = new List<AlsActionSectionBinding>();
        for (var index = 0; index < p5a.Actions.Length; index++)
        {
            var source = p5a.Actions[index];
            var montage = RequireOccurrence(occurrences, CoreOccurrenceKind.ActionMontage, index);
            var sequenceAuthorities = p5a.SegmentBindings.Select((value, bindingIndex) => (value, bindingIndex))
                .Where(value => value.value.ActionDefinitionId == index)
                .Select(value => RequireOccurrence(occurrences, CoreOccurrenceKind.ActionSequence,
                    value.bindingIndex).AuthorityGroupId).Distinct().ToArray();
            if (sequenceAuthorities.Length != 1)
            {
                throw new ArgumentException("Each Action requires exactly one Sequence authority.");
            }
            definitions[index] = new AlsActionDefinition(montage.OccurrenceHandleId,
                montage.AuthorityGroupId, sequenceAuthorities[0], source.DefinitionId,
                source.MontageId, source.MontageDurationSeconds, source.SlotId,
                source.StartSectionId, source.Priority, source.PlayRate, source.BlendSeconds,
                source.Interruptible ? (byte)1 : (byte)0,
                source.LoopPolicy == AlsP5LoopPolicy.Loop ? (byte)1 : (byte)0);
            foreach (var value in source.Sections)
            {
                sectionList.Add(new AlsActionSectionBinding(value.ActionDefinitionId,
                    value.SectionId, value.NextSectionId, value.StartTime, value.EndTime));
            }
        }
        sections = sectionList.ToArray();
        segments = new AlsActionSegmentBinding[p5a.SegmentBindings.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            var source = p5a.SegmentBindings[index];
            var occurrence = RequireOccurrence(occurrences, CoreOccurrenceKind.ActionSequence, index);
            segments[index] = new AlsActionSegmentBinding(occurrence.OccurrenceHandleId,
                source.ActionDefinitionId, source.SlotId, source.SegmentId, source.AnimationId,
                source.MontageStartTime, source.MontageEndTime, source.AnimationStartTime,
                source.AnimationEndTime, source.PlayRate, source.LoopCount);
        }

        var timelineList = new List<AlsTimelineEventDefinition>(
            generalTimeline.Count + p5a.TimelineEntries.Length);
        timelineList.AddRange(generalTimeline);
        ranges = new AlsActionTimelineRange[p5a.Actions.Length];
        for (var actionId = 0; actionId < p5a.Actions.Length; actionId++)
        {
            var offset = timelineList.Count;
            foreach (var value in p5a.TimelineEntries.Where(value => value.ActionDefinitionId == actionId))
            {
                var occurrence = value.SourceKind == AlsCompiledActionTimelineSourceKind.Montage
                    ? RequireOccurrence(occurrences, CoreOccurrenceKind.ActionMontage, actionId)
                    : RequireOccurrence(occurrences, CoreOccurrenceKind.ActionSequence,
                        value.SegmentBindingIndex);
                timelineList.Add(new AlsTimelineEventDefinition(value.EventId,
                    value.SourceAssetId, actionId, occurrence.OccurrenceHandleId,
                    value.SourceKind == AlsCompiledActionTimelineSourceKind.Montage
                        ? AlsTimelineSourceKind.Montage
                        : AlsTimelineSourceKind.MontageSegmentAnimation,
                    value.SourceIndex, value.TrackIndex, value.BoundaryOrdinal,
                    value.TimeSeconds, value.DurationSeconds, value.TriggerWeightThreshold,
                    MapKind(value.Kind), MapTick(value.TickMode),
                    MapPayload(value.Kind, value.Payload, semanticIds)));
            }
            ranges[actionId] = new AlsActionTimelineRange(actionId, offset,
                timelineList.Count - offset);
        }
        timeline = timelineList.ToArray();
    }

    private static CoreOccurrenceEntry RequireOccurrence(
        CoreOccurrenceEntry[] values, CoreOccurrenceKind kind, int sourceBindingIndex)
    {
        CoreOccurrenceEntry result = default;
        var count = 0;
        foreach (var value in values)
        {
            if (value.SourceKind == kind && value.SourceBindingIndex == sourceBindingIndex)
            {
                result = value;
                count++;
            }
        }
        return count == 1 ? result : throw new ArgumentException("A required occurrence is missing or duplicate.");
    }

    private static AlsTimelineEventKind MapKind(AlsCompiledTimelineEventKind value) => value switch
    {
        AlsCompiledTimelineEventKind.Generic => AlsTimelineEventKind.Generic,
        AlsCompiledTimelineEventKind.Footstep => AlsTimelineEventKind.Footstep,
        AlsCompiledTimelineEventKind.SetAction => AlsTimelineEventKind.SetAction,
        AlsCompiledTimelineEventKind.SetGroundedEntry => AlsTimelineEventKind.SetGroundedEntry,
        AlsCompiledTimelineEventKind.EarlyBlendOut => AlsTimelineEventKind.EarlyBlendOut,
        AlsCompiledTimelineEventKind.RootMotionScale => AlsTimelineEventKind.RootMotionScale,
        _ => throw new ArgumentException("The timeline event kind is unknown."),
    };

    private static AlsTimelineTickMode MapTick(AlsCompiledTimelineTickMode value) => value switch
    {
        AlsCompiledTimelineTickMode.Queued => AlsTimelineTickMode.Queued,
        AlsCompiledTimelineTickMode.BranchingPoint => AlsTimelineTickMode.BranchingPoint,
        _ => throw new ArgumentException("The timeline tick mode is unknown."),
    };

    private static AlsCompactEventPayload MapPayload(
        AlsCompiledTimelineEventKind kind,
        AlsCompiledTimelinePayloadDefinition value,
        int[] semanticIds)
    {
        if (!Enum.IsDefined(value.Foot) || !Enum.IsDefined(value.Action) ||
            !Enum.IsDefined(value.GroundedEntryMode) || !Enum.IsDefined(value.LocomotionMode) ||
            !Enum.IsDefined(value.RotationMode) || !Enum.IsDefined(value.Stance) ||
            !Finite(value.BlendOutSeconds) || !Finite(value.TranslationScale))
        {
            throw new ArgumentException("A timeline payload enum or scalar is invalid.");
        }
        ValidateCanonicalPayload(kind, value);
        var semanticId = semanticIds.Length == 0 ? 0 : semanticIds[(int)kind];
        return kind switch
        {
            AlsCompiledTimelineEventKind.Generic =>
                new AlsCompactEventPayload(semanticId, 0, 0, 0, 0f, 0, AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.Footstep =>
                new AlsCompactEventPayload(semanticId, (int)MapFoot(value.Foot), 0, 0, 0f, 0,
                    AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.SetAction =>
                new AlsCompactEventPayload(semanticId, (int)MapAction(value.Action), 0, 0, 0f, 0,
                    AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.SetGroundedEntry =>
                new AlsCompactEventPayload(semanticId, (int)MapGrounded(value.GroundedEntryMode),
                    0, 0, 0f, 0, AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.EarlyBlendOut =>
                new AlsCompactEventPayload(semanticId, (int)MapLocomotion(value.LocomotionMode),
                    (int)MapRotation(value.RotationMode), (int)MapStance(value.Stance),
                    value.BlendOutSeconds,
                    (ushort)((value.CheckInput ? 1 : 0) |
                        (value.CheckLocomotionMode ? 2 : 0) |
                        (value.CheckRotationMode ? 4 : 0) |
                        (value.CheckStance ? 8 : 0)), AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.RootMotionScale =>
                new AlsCompactEventPayload(semanticId, 0, 0, 0, value.TranslationScale, 0,
                    AlsActionResultCode.None),
            _ => throw new ArgumentException("The timeline event kind is unknown."),
        };
    }

    private static void ValidateCanonicalPayload(
        AlsCompiledTimelineEventKind kind,
        AlsCompiledTimelinePayloadDefinition value)
    {
        var footUnused = kind != AlsCompiledTimelineEventKind.Footstep;
        var actionUnused = kind != AlsCompiledTimelineEventKind.SetAction;
        var groundedUnused = kind != AlsCompiledTimelineEventKind.SetGroundedEntry;
        var earlyUnused = kind != AlsCompiledTimelineEventKind.EarlyBlendOut;
        var translationUnused = kind != AlsCompiledTimelineEventKind.RootMotionScale;
        if (footUnused && value.Foot != AlsCompiledTimelineFoot.Unspecified ||
            actionUnused && value.Action != AlsCompiledTimelineAction.None ||
            groundedUnused && value.GroundedEntryMode != AlsCompiledTimelineGroundedEntryMode.None ||
            earlyUnused && (BitConverter.SingleToUInt32Bits(value.BlendOutSeconds) != 0 ||
                value.CheckInput || value.CheckLocomotionMode || value.CheckRotationMode || value.CheckStance ||
                value.LocomotionMode != AlsCompiledTimelineLocomotionMode.Grounded ||
                value.RotationMode != AlsCompiledTimelineRotationMode.VelocityDirection ||
                value.Stance != AlsCompiledTimelineStance.Standing) ||
            !earlyUnused && value.BlendOutSeconds < 0f ||
            translationUnused && BitConverter.SingleToUInt32Bits(value.TranslationScale) != 0 ||
            !translationUnused && value.TranslationScale < 0f)
        {
            throw new ArgumentException("A timeline payload contains noncanonical unused fields.");
        }
    }

    private static AlsTimelineFoot MapFoot(AlsCompiledTimelineFoot value) => value switch
    { AlsCompiledTimelineFoot.Unspecified => AlsTimelineFoot.Unspecified,
      AlsCompiledTimelineFoot.Left => AlsTimelineFoot.Left,
      AlsCompiledTimelineFoot.Right => AlsTimelineFoot.Right,
      _ => throw new ArgumentException("The foot payload is unknown."), };
    private static AlsTimelineAction MapAction(AlsCompiledTimelineAction value) => value switch
    { AlsCompiledTimelineAction.None => AlsTimelineAction.None,
      AlsCompiledTimelineAction.Rolling => AlsTimelineAction.Rolling,
      AlsCompiledTimelineAction.Mantling => AlsTimelineAction.Mantling,
      AlsCompiledTimelineAction.Ragdolling => AlsTimelineAction.Ragdolling,
      AlsCompiledTimelineAction.GettingUp => AlsTimelineAction.GettingUp,
      _ => throw new ArgumentException("The Action payload is unknown."), };
    private static AlsTimelineGroundedEntryMode MapGrounded(AlsCompiledTimelineGroundedEntryMode value) => value switch
    { AlsCompiledTimelineGroundedEntryMode.None => AlsTimelineGroundedEntryMode.None,
      AlsCompiledTimelineGroundedEntryMode.FromRoll => AlsTimelineGroundedEntryMode.FromRoll,
      _ => throw new ArgumentException("The grounded-entry payload is unknown."), };
    private static AlsTimelineLocomotionMode MapLocomotion(AlsCompiledTimelineLocomotionMode value) => value switch
    { AlsCompiledTimelineLocomotionMode.Grounded => AlsTimelineLocomotionMode.Grounded,
      AlsCompiledTimelineLocomotionMode.InAir => AlsTimelineLocomotionMode.InAir,
      AlsCompiledTimelineLocomotionMode.Mantling => AlsTimelineLocomotionMode.Mantling,
      AlsCompiledTimelineLocomotionMode.Ragdoll => AlsTimelineLocomotionMode.Ragdoll,
      AlsCompiledTimelineLocomotionMode.Recovering => AlsTimelineLocomotionMode.Recovering,
      _ => throw new ArgumentException("The locomotion payload is unknown."), };
    private static AlsTimelineRotationMode MapRotation(AlsCompiledTimelineRotationMode value) => value switch
    { AlsCompiledTimelineRotationMode.VelocityDirection => AlsTimelineRotationMode.VelocityDirection,
      AlsCompiledTimelineRotationMode.LookingDirection => AlsTimelineRotationMode.LookingDirection,
      AlsCompiledTimelineRotationMode.Aiming => AlsTimelineRotationMode.Aiming,
      _ => throw new ArgumentException("The rotation payload is unknown."), };
    private static AlsTimelineStance MapStance(AlsCompiledTimelineStance value) => value switch
    { AlsCompiledTimelineStance.Standing => AlsTimelineStance.Standing,
      AlsCompiledTimelineStance.Crouching => AlsTimelineStance.Crouching,
      _ => throw new ArgumentException("The stance payload is unknown."), };

    private static void ValidateTimelineSource(
        AlsCompiledTimelineEventDefinition value, int sourceAssetId, float duration)
    {
        if (value.EventId < 0 || value.SourceAssetId != sourceAssetId ||
            !Enum.IsDefined(value.Kind) || !Enum.IsDefined(value.TickMode) ||
            value.SourceIndex < 0 || value.TrackIndex < 0 || !Finite(value.TimeSeconds) ||
            !Finite(value.DurationSeconds) || !Finite(value.TriggerWeightThreshold) ||
            value.TimeSeconds < 0f || value.DurationSeconds < 0f ||
            value.TimeSeconds + value.DurationSeconds > duration + 1e-6f ||
            value.TriggerWeightThreshold < 0f || value.TriggerWeightThreshold > 1f)
        {
            throw new ArgumentException("An authored timeline row is invalid.");
        }
        _ = MapPayload(value.Kind, value.Payload, new int[6]);
    }

    private static void ValidateAnimationId(
        AlsAnimationDefinition[] animations, int animationId, int skeletonId)
    {
        if ((uint)animationId >= (uint)animations.Length ||
            animations[animationId].Id != animationId || animations[animationId].SkeletonId != skeletonId ||
            !Positive(animations[animationId].PlayLength))
        {
            throw new ArgumentException("A referenced animation is invalid or cross-skeleton.");
        }
    }

    private static void ValidateAnimationAndCurve(
        AlsAnimationDefinition[] animations, int animationId, int curveId, int skeletonId)
    {
        ValidateAnimationId(animations, animationId, skeletonId);
        if (curveId < 0 || animations[animationId].Curves.Count(value => value.CurveId == curveId) != 1)
        {
            throw new ArgumentException("A referenced animation curve is missing or duplicate.");
        }
    }

    private static void ValidateOptionalCurve(AlsAnimationDefinition animation, int curveId)
    {
        if (curveId < -1 || curveId >= 0 && animation.Curves.Count(value => value.CurveId == curveId) != 1)
        {
            throw new ArgumentException("A referenced optional curve is invalid.");
        }
    }

    private static bool Finite(float value) => float.IsFinite(value);
    private static bool Positive(float value) => float.IsFinite(value) && value > 0f;

    internal static ulong ComputeBindingDigest(
        ulong layoutDigest,
        AlsCurveKey[] curveKeys,
        AlsCurveBinding[] curveBindings,
        AlsP5CurveBindingIdentity[] identities,
        AlsAnimationCurveRange[] ranges,
        AlsP5CurveSemanticPolicy policy,
        int[] allowIndices,
        AlsP4FootCurveRuntimeBinding[] footBindings,
        float groundedIk,
        float jumpIk,
        float fallIk,
        float landIk,
        AlsTimelineEventDefinition[] timeline,
        AlsSyncMarkerDefinition[] markers,
        AlsSyncGroupBinding group,
        AlsSyncMemberBinding[] members,
        AlsP5SyncOccurrenceBinding[] syncOccurrences,
        AlsDynamicTransitionBinding transition,
        AlsActionDefinition[] actions,
        AlsActionSectionBinding[] sections,
        AlsActionSegmentBinding[] segments,
        AlsActionTimelineRange[] actionRanges)
    {
        var writer = new FnvWriter();
        writer.Add(Version); writer.Add(layoutDigest);
        writer.Add(curveKeys, static (ref FnvWriter w, in AlsCurveKey x) =>
        { w.Add(x.TimeSeconds); w.Add(x.Value); w.Add(x.ArriveTangent); w.Add(x.LeaveTangent); w.Add((byte)x.Interpolation); });
        writer.Add(curveBindings, static (ref FnvWriter w, in AlsCurveBinding x) =>
        { w.Add(x.CurveId); w.Add(x.KeyOffset); w.Add(x.KeyCount); w.Add(x.DurationSeconds); w.Add(x.Required); w.Add(x.Loop); });
        writer.Add(identities, static (ref FnvWriter w, in AlsP5CurveBindingIdentity x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); });
        writer.Add(ranges, static (ref FnvWriter w, in AlsAnimationCurveRange x) =>
        { w.Add(x.AnimationId); w.Add(x.BindingOffset); w.Add(x.BindingCount); });
        writer.Add(policy.MissingValue); writer.Add((byte)policy.CombineMode);
        writer.Add(policy.ClampMinimum); writer.Add(policy.ClampMaximum);
        writer.Add(allowIndices, static (ref FnvWriter w, in int x) => w.Add(x));
        writer.Add(footBindings, static (ref FnvWriter w, in AlsP4FootCurveRuntimeBinding x) =>
        { w.Add(x.AnimationId); w.Add(x.LeftLockCurveId); w.Add(x.RightLockCurveId); w.Add(x.LeftLockDefault); w.Add(x.RightLockDefault); });
        writer.Add(groundedIk); writer.Add(jumpIk); writer.Add(fallIk); writer.Add(landIk);
        writer.Add<AlsTimelineEventDefinition>(timeline, WriteTimeline);
        writer.Add(markers, static (ref FnvWriter w, in AlsSyncMarkerDefinition x) =>
        { w.Add(x.MarkerId); w.Add(x.MarkerNameId); w.Add(x.AnimationId); w.Add(x.SourceIndex); w.Add(x.TrackIndex); w.Add(x.TimeSeconds); });
        WriteSyncGroup(ref writer, group);
        writer.Add(members, static (ref FnvWriter w, in AlsSyncMemberBinding x) =>
        { w.Add(x.GroupId); w.Add(x.AnimationId); w.Add(x.DurationSeconds); w.Add(x.Loop); w.Add(x.CanLead); });
        writer.Add(syncOccurrences, static (ref FnvWriter w, in AlsP5SyncOccurrenceBinding x) =>
        { w.Add(x.GroupId); w.Add(x.GroupMemberIndex); w.Add(x.AnimationId); w.Add(x.OccurrenceHandleId); });
        WriteTransition(ref writer, transition);
        writer.Add<AlsActionDefinition>(actions, WriteAction);
        writer.Add(sections, static (ref FnvWriter w, in AlsActionSectionBinding x) =>
        { w.Add(x.ActionDefinitionId); w.Add(x.SectionId); w.Add(x.NextSectionId); w.Add(x.StartTime); w.Add(x.EndTime); });
        writer.Add(segments, static (ref FnvWriter w, in AlsActionSegmentBinding x) =>
        { w.Add(x.OccurrenceHandleId); w.Add(x.ActionDefinitionId); w.Add(x.SlotId); w.Add(x.SegmentId); w.Add(x.AnimationId); w.Add(x.MontageStartTime); w.Add(x.MontageEndTime); w.Add(x.AnimationStartTime); w.Add(x.AnimationEndTime); w.Add(x.PlayRate); w.Add(x.LoopCount); });
        writer.Add(actionRanges, static (ref FnvWriter w, in AlsActionTimelineRange x) =>
        { w.Add(x.ActionDefinitionId); w.Add(x.DefinitionOffset); w.Add(x.DefinitionCount); });
        return writer.Value;
    }

    internal static ulong ComputeGraphDigest(
        int skeletonId, int meshId, int logicalRoot, int physicalRoot,
        AlsPresentationDefinition presentation, int standingIdle, int crouchingIdle,
        int jump, int fall, int land, int leanBase, AlsP5GraphSample[] standing,
        AlsP5GraphSample[] crouching, AlsP5GraphSample[] lean, int[] allAnimations,
        AlsAimProfile aim, AlsTurnProfile[] turns, AlsRotateProfile[] rotates,
        AlsP5GraphMaskHeader[] headers, int[] logicalBones, int[] normalized)
    {
        var writer = new FnvWriter();
        writer.Add(Version); writer.Add(skeletonId); writer.Add(meshId);
        writer.Add(logicalRoot); writer.Add(physicalRoot);
        writer.Add(presentation.TranslationMeters.X); writer.Add(presentation.TranslationMeters.Y);
        writer.Add(presentation.TranslationMeters.Z); writer.Add(presentation.YawRadians);
        writer.Add(standingIdle); writer.Add(crouchingIdle); writer.Add(jump); writer.Add(fall);
        writer.Add(land); writer.Add(leanBase);
        writer.Add<AlsP5GraphSample>(standing, WriteGraphSample);
        writer.Add<AlsP5GraphSample>(crouching, WriteGraphSample);
        writer.Add<AlsP5GraphSample>(lean, WriteGraphSample);
        writer.Add(allAnimations, static (ref FnvWriter w, in int x) => w.Add(x));
        writer.Add(aim.AimOffsetId); writer.Add(aim.DownAnimationId); writer.Add(aim.ForwardAnimationId);
        writer.Add(aim.UpAnimationId); writer.Add(aim.AdditiveBasePoseAnimationId);
        writer.Add(turns, static (ref FnvWriter w, in AlsTurnProfile x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); w.Add((byte)x.Stance); w.Add(x.Direction); w.Add(x.NominalDegrees); w.Add(x.BasePlayRate); w.Add(x.BlendSeconds); w.Add(x.ScaleAngle); });
        writer.Add(rotates, static (ref FnvWriter w, in AlsRotateProfile x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); w.Add((byte)x.Stance); w.Add(x.Direction); });
        writer.Add(headers, static (ref FnvWriter w, in AlsP5GraphMaskHeader x) =>
        { w.Add((byte)x.Kind); w.Add(x.LogicalRootBoneId); w.Add(x.BoneOffset); w.Add(x.BoneCount); });
        writer.Add(logicalBones, static (ref FnvWriter w, in int x) => w.Add(x));
        writer.Add(normalized, static (ref FnvWriter w, in int x) => w.Add(x));
        return writer.Value;
    }

    private static void WriteTimeline(ref FnvWriter w, in AlsTimelineEventDefinition x)
    {
        w.Add(x.EventId); w.Add(x.SourceAnimationId); w.Add(x.SourceActionId);
        w.Add(x.RequiredOccurrenceHandleId); w.Add((byte)x.SourceKind); w.Add(x.SourceIndex);
        w.Add(x.TrackIndex); w.Add(x.BoundaryOrdinal); w.Add(x.TimeSeconds); w.Add(x.DurationSeconds);
        w.Add(x.TriggerWeightThreshold); w.Add((byte)x.Kind); w.Add((byte)x.TickMode);
        w.Add(x.Payload.SemanticId); w.Add(x.Payload.EnumValue0); w.Add(x.Payload.EnumValue1);
        w.Add(x.Payload.EnumValue2); w.Add(x.Payload.ScalarValue0); w.Add(x.Payload.Flags);
        w.Add((ushort)x.Payload.TerminationReason);
    }
    private static void WriteSyncGroup(ref FnvWriter w, in AlsSyncGroupBinding x)
    { w.Add(x.GroupId); w.Add(x.MemberOffset); w.Add(x.MemberCount); w.Add(x.LeftMarkerNameId); w.Add(x.RightMarkerNameId); }
    private static void WriteTransition(ref FnvWriter w, in AlsDynamicTransitionBinding x)
    {
        w.Add(x.OccurrenceHandleId); w.Add(x.AuthorityGroupId);
        WriteTransitionClip(ref w, x.StandingLeft); WriteTransitionClip(ref w, x.StandingRight);
        WriteTransitionClip(ref w, x.CrouchingLeft); WriteTransitionClip(ref w, x.CrouchingRight);
        w.Add(x.DistanceMeters); w.Add(x.BlendSeconds); w.Add(x.PlayRate); w.Add(x.CooldownFrames);
    }
    private static void WriteTransitionClip(ref FnvWriter w, in AlsDynamicTransitionClipBinding x)
    { w.Add(x.AnimationId); w.Add(x.AdditiveBaseAnimationId); w.Add(x.DurationSeconds); }
    private static void WriteAction(ref FnvWriter w, in AlsActionDefinition x)
    {
        w.Add(x.OccurrenceHandleId); w.Add(x.MontageAuthorityGroupId); w.Add(x.SequenceAuthorityGroupId);
        w.Add(x.DefinitionId); w.Add(x.MontageId); w.Add(x.MontageDurationSeconds); w.Add(x.SlotId);
        w.Add(x.StartSectionId); w.Add(x.Priority); w.Add(x.PlayRate); w.Add(x.BlendSeconds);
        w.Add(x.Interruptible); w.Add(x.Loop);
    }
    private static void WriteGraphSample(ref FnvWriter w, in AlsP5GraphSample x)
    { w.Add(x.AnimationId); w.Add(x.X); w.Add(x.Y); w.Add(x.RateScale); }

    private delegate void ElementWriter<T>(ref FnvWriter writer, in T value);
    private struct FnvWriter
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value;
        public FnvWriter() => _value = Offset;
        public ulong Value => _value;
        private void AddByte(byte value)
        { _value ^= value; _value *= Prime; }
        public void Add(byte value) => AddByte(value);
        public void Add(bool value) => AddByte(value ? (byte)1 : (byte)0);
        public void Add(sbyte value) => AddByte(unchecked((byte)value));
        public void Add(short value) => Add(unchecked((ushort)value));
        public void Add(ushort value) { AddByte((byte)value); AddByte((byte)(value >> 8)); }
        public void Add(int value) => Add(unchecked((uint)value));
        public void Add(uint value)
        { AddByte((byte)value); AddByte((byte)(value >> 8)); AddByte((byte)(value >> 16)); AddByte((byte)(value >> 24)); }
        public void Add(ulong value) { Add(unchecked((uint)value)); Add(unchecked((uint)(value >> 32))); }
        public void Add(long value) => Add(unchecked((ulong)value));
        public void Add(float value) => Add(BitConverter.SingleToUInt32Bits(value));
        public void Add(double value) => Add(BitConverter.DoubleToUInt64Bits(value));
        public void Add<T>(ReadOnlySpan<T> values, ElementWriter<T> write)
        { Add(values.Length); foreach (ref readonly var value in values) write(ref this, in value); }
    }
}
