using Godot;
using GodotAls.Assets;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using CoreOccurrenceKind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;

namespace GodotAls.Animation;

public partial class P5aRuntimeBindingSmoke : Node
{
    private const string P3ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const string P4ProfilePath = "res://assets/config/p4_pose_profile.json";
    private const string P5aProfilePath = "res://assets/config/p5a_animation_runtime.json";
    private const double AnimationLengthTolerance = 1.0 / 30.0;

    public override void _Ready()
    {
        try
        {
            RunSmoke();
            GD.Print(
                "P5A_RUNTIME_BINDING_OK event_semantics=6 curve_semantics=1 groups=1 " +
                "members=17 transitions=4 actions=1 segments=1 occurrence_handles_valid=1 " +
                "authority_global=1 action_domains=2 layout_digest=1 graph_digest=1 root_filter=1");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void RunSmoke()
    {
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var animationSet = resource.LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P3ProfilePath)), animationSet);
        var pose = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)), animationSet, locomotion);
        var p5a = AlsP5aAnimationRuntimeProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P5aProfilePath)), animationSet);
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        var snapshot = AlsP5CoreRuntimeBindingCompiler.Compile(
            animationSet, locomotion, pose, p5a, layout);

        using var library = AlsAnimationLibraryBuilder.BuildP5a(animationSet, snapshot);
        AddChild(library.Root);
        using var binding = AlsP5aAnimationRuntimeBinding.Compile(snapshot, library);

        VerifyReadOnlyCollections(binding, library);
        VerifySnapshotViews(snapshot, binding, p5a, pose);
        VerifyLibrary(animationSet, locomotion, pose, snapshot, library);
        VerifyPhysicalDescriptors(locomotion, pose, binding);
        VerifyStalePairings(animationSet, locomotion, pose, p5a, layout, snapshot, library);
        VerifyInvalidDurationsRejected(animationSet, snapshot);
        VerifyMutatedSkeletonRejected(animationSet, snapshot);
        VerifyCoherentNonIdentityRootFixture();
    }

    private static void VerifyReadOnlyCollections(
        AlsP5aAnimationRuntimeBinding binding,
        AlsAnimationLibraryBuildResult library)
    {
        RequireReadOnly(binding.PhysicalSlots, "physical slots");
        RequireReadOnly(library.ActionFilterPaths, "Action filter paths");

        static void RequireReadOnly<T>(IReadOnlyList<T> values, string label)
        {
            Require(values.Count > 0, $"The {label} fixture is empty.");
            if (values is not IList<T> writable) return;
            try
            {
                writable[0] = values[0];
            }
            catch (NotSupportedException)
            {
                return;
            }
            throw new InvalidOperationException($"Published {label} permit mutation.");
        }
    }

    private static void VerifySnapshotViews(
        AlsP5CoreRuntimeBindingSnapshot snapshot,
        AlsP5aAnimationRuntimeBinding binding,
        AlsP5aAnimationRuntimeProfile p5a,
        AlsPoseAnimationProfile pose)
    {
        var beforeCore = snapshot.CreateCoreView();
        var beforeLayout = snapshot.CreateOccurrenceLayoutView();
        var beforeGraph = snapshot.CreateGraphBuildView();
        var core = binding.CreateCoreView();
        var occurrence = binding.CreateOccurrenceLayoutView();
        var graph = binding.CreateGraphBuildView();

        Require(core.Digest == beforeCore.Digest && core.LayoutDigest == beforeCore.LayoutDigest,
            "Adapter changed the pure Core view provenance.");
        Require(occurrence.Digest == beforeLayout.Digest &&
            occurrence.Entries.SequenceEqual(beforeLayout.Entries),
            "Adapter changed the pure occurrence layout.");
        Require(graph.Digest == beforeGraph.Digest &&
            graph.AllAnimationIds.SequenceEqual(beforeGraph.AllAnimationIds) &&
            graph.LogicalBoneIds.SequenceEqual(beforeGraph.LogicalBoneIds),
            "Adapter changed the pure graph view.");
        Require(snapshot.AnimationSetDefinitionDigest == binding.Stamp.AnimationSetDefinitionDigest &&
            snapshot.LayoutDigest == binding.Stamp.LayoutDigest &&
            snapshot.Digest == binding.Stamp.BindingDigest &&
            snapshot.GraphDigest == binding.Stamp.GraphDigest,
            "Adapter lost a P5A provenance stamp.");

        var configuredSemanticIds = p5a.EventSemantics
            .Select(value => value.SemanticId).ToArray();
        Require(configuredSemanticIds.SequenceEqual(Enumerable.Range(0, 6)) &&
            core.TimelineDefinitions.ToArray().All(value =>
                configuredSemanticIds.Contains(value.Payload.SemanticId)),
            "The six configured event semantic IDs or their compiled references changed.");
        Require(core.AllowTransitionsPolicy.MissingValue == p5a.AllowTransitions.MissingValue &&
            core.AllowTransitionsPolicy.ClampMinimum == p5a.AllowTransitions.ClampMinimum &&
            core.AllowTransitionsPolicy.ClampMaximum == p5a.AllowTransitions.ClampMaximum &&
            core.AllowTransitionsBindingIndices.Length ==
                p5a.AllowTransitions.AnimationCurveIds.Length,
            "The single Enable_Transition semantic policy or its compiled bindings changed.");
        Require(p5a.SyncGroups.Length == 1 && core.SyncGroup.MemberCount == 17 &&
            core.SyncMembers.Length == 17 &&
            core.SyncMembers.ToArray().All(value => value.DurationSeconds > 0f),
            "The single 17-member duration-bearing Sync group is incomplete.");
        Require(core.GroundedIkWeight == pose.FootCurves.GroundedIkWeight &&
            core.JumpStartIkWeight == pose.FootCurves.JumpStartIkWeight &&
            core.FallLoopIkWeight == pose.FootCurves.FallLoopIkWeight &&
            core.LandRecoveryIkWeight == pose.FootCurves.LandRecoveryIkWeight,
            "The four P4 IK constants changed.");
        var expectedFeet = pose.FootCurves.Bindings;
        Require(core.FootCurveBindings.Length == expectedFeet.Length,
            "P4 foot-curve binding count changed.");
        for (var index = 0; index < expectedFeet.Length; index++)
        {
            var expected = expectedFeet[index];
            var actual = core.FootCurveBindings[index];
            Require(actual.AnimationId == expected.AnimationId &&
                actual.LeftLockCurveId == expected.LeftLockCurveId &&
                actual.RightLockCurveId == expected.RightLockCurveId &&
                actual.LeftLockDefault == expected.LeftLockDefault &&
                actual.RightLockDefault == expected.RightLockDefault,
                $"P4 foot-curve binding changed at {index}.");
        }

        var actualTransitions = new[]
        {
            core.DynamicTransition.StandingLeft,
            core.DynamicTransition.StandingRight,
            core.DynamicTransition.CrouchingLeft,
            core.DynamicTransition.CrouchingRight,
        };
        var expectedTransitions = p5a.DynamicTransition.Slots;
        Require(expectedTransitions.Length == actualTransitions.Length &&
            core.DynamicTransition.OccurrenceHandleId >= 0,
            "The four Dynamic Transition slots are incomplete.");
        for (var index = 0; index < expectedTransitions.Length; index++)
        {
            Require(actualTransitions[index].AnimationId == expectedTransitions[index].AnimationId &&
                actualTransitions[index].AdditiveBaseAnimationId ==
                    expectedTransitions[index].AdditiveBaseAnimationId,
                $"Transition clip or additive base differs at slot {index}.");
        }
        Require(core.ActionDefinitions.Length == 1 && core.ActionSections.Length == 1 &&
            core.ActionSegments.Length == 1 && core.ActionTimelineRanges.Length == 1,
            "The locked Roll Action, slot, section or segment binding changed.");
        Require(core.ActionDefinitions[0].MontageAuthorityGroupId !=
            core.ActionDefinitions[0].SequenceAuthorityGroupId,
            "Montage and Sequence must occupy distinct authority domains.");
        Require(core.DynamicTransition == beforeCore.DynamicTransition &&
            core.ActionDefinitions.SequenceEqual(beforeCore.ActionDefinitions) &&
            core.ActionSections.SequenceEqual(beforeCore.ActionSections) &&
            core.ActionSegments.SequenceEqual(beforeCore.ActionSegments) &&
            core.ActionTimelineRanges.SequenceEqual(beforeCore.ActionTimelineRanges),
            "Transition/Action snapshot fields or handles changed through the adapter.");
        var authoredAction = p5a.Actions[0];
        var montageOccurrence = occurrence.Entries.ToArray().Single(value =>
            value.SourceKind == CoreOccurrenceKind.ActionMontage && value.SourceBindingIndex == 0);
        var segmentOccurrence = occurrence.Entries.ToArray().Single(value =>
            value.SourceKind == CoreOccurrenceKind.ActionSequence && value.SourceBindingIndex == 0);
        Require(core.ActionDefinitions[0] == new AlsActionDefinition(
            montageOccurrence.OccurrenceHandleId, montageOccurrence.AuthorityGroupId,
            segmentOccurrence.AuthorityGroupId, authoredAction.DefinitionId,
            authoredAction.MontageId, authoredAction.MontageDurationSeconds,
            authoredAction.SlotId, authoredAction.StartSectionId, authoredAction.Priority,
            authoredAction.PlayRate, authoredAction.BlendSeconds,
            authoredAction.Interruptible ? (byte)1 : (byte)0,
            authoredAction.LoopPolicy == AlsP5LoopPolicy.Loop ? (byte)1 : (byte)0,
            authoredAction.Lifecycle), "The declared Roll definition or handle changed.");
        var authoredSection = authoredAction.Sections[0];
        Require(core.ActionSections[0] == new AlsActionSectionBinding(
            authoredSection.ActionDefinitionId, authoredSection.SectionId,
            authoredSection.NextSectionId, authoredSection.StartTime, authoredSection.EndTime),
            "The declared Roll section changed.");
        var authoredSegment = p5a.SegmentBindings[0];
        Require(core.ActionSegments[0] == new AlsActionSegmentBinding(
            segmentOccurrence.OccurrenceHandleId, authoredSegment.ActionDefinitionId,
            authoredSegment.SlotId, authoredSegment.SegmentId, authoredSegment.AnimationId,
            authoredSegment.MontageStartTime, authoredSegment.MontageEndTime,
            authoredSegment.AnimationStartTime, authoredSegment.AnimationEndTime,
            authoredSegment.PlayRate, authoredSegment.LoopCount),
            "The declared Roll segment or occurrence handle changed.");
        var transitionOccurrence = occurrence.Entries.ToArray().Single(value =>
            value.SourceKind == CoreOccurrenceKind.Transition && value.SourceBindingIndex == 0);
        Require(core.DynamicTransition.OccurrenceHandleId == transitionOccurrence.OccurrenceHandleId &&
            core.DynamicTransition.AuthorityGroupId == transitionOccurrence.AuthorityGroupId &&
            core.DynamicTransition.DistanceMeters == p5a.DynamicTransition.DistanceMeters &&
            core.DynamicTransition.BlendSeconds == p5a.DynamicTransition.BlendSeconds &&
            core.DynamicTransition.PlayRate == p5a.DynamicTransition.PlayRate &&
            core.DynamicTransition.CooldownFrames == p5a.DynamicTransition.CooldownFrames,
            "Transition timing, policy or occurrence handle changed.");

        var entries = occurrence.Entries;
        Require(entries.Length > 0 && entries.ToArray().Select(value => value.OccurrenceHandleId)
            .SequenceEqual(Enumerable.Range(0, entries.Length)),
            "Occurrence handles are not nonnegative, unique ordinals.");
        var sharedAuthority = entries[0].AuthorityGroupId;
        Require(entries.ToArray().Where(value => value.SourceKind is
                CoreOccurrenceKind.Base or CoreOccurrenceKind.Turn or
                CoreOccurrenceKind.Rotate)
            .All(value => value.AuthorityGroupId == sharedAuthority),
            "Base, Turn and Rotate do not share one global authority domain.");

        foreach (ref readonly var definition in core.TimelineDefinitions)
        {
            Require(definition.RequiredOccurrenceHandleId >= 0,
                "A production Timeline definition has no occurrence handle.");
            var requiredHandle = definition.RequiredOccurrenceHandleId;
            Require((uint)requiredHandle < (uint)entries.Length,
                "A Timeline definition refers to an unknown occurrence handle.");
            var matching = entries[requiredHandle];
            if (definition.SourceKind == AlsTimelineSourceKind.MontageSegmentAnimation)
            {
                Require(definition.BoundaryOrdinal > 0 &&
                    matching.SourceKind == CoreOccurrenceKind.ActionSequence,
                    "A flattened Sequence definition lost its handle or nonzero ordinal.");
            }
            else
            {
                Require(definition.BoundaryOrdinal == 0,
                    "An ordinary definition received a nonzero boundary ordinal.");
            }
        }

        var maskOffset = 0;
        var sourceMasks = pose.Masks.Entries;
        Require(graph.MaskHeaders.Length == sourceMasks.Length,
            "P4 logical mask header count changed.");
        for (var index = 0; index < sourceMasks.Length; index++)
        {
            var source = sourceMasks[index];
            var header = graph.MaskHeaders[index];
            Require(header.Kind == source.Kind && header.LogicalRootBoneId == source.RootBoneId &&
                header.BoneOffset == maskOffset && header.BoneCount == source.BoneIds.Length &&
                graph.LogicalBoneIds.Slice(maskOffset, source.BoneIds.Length)
                    .SequenceEqual(source.BoneIds),
                $"P4 logical mask provenance changed at {index}.");
            maskOffset += source.BoneIds.Length;
        }
    }

    private static void VerifyLibrary(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5CoreRuntimeBindingSnapshot snapshot,
        AlsAnimationLibraryBuildResult library)
    {
        var core = snapshot.CreateCoreView();
        var expected = new HashSet<int>(locomotion.AllAnimationIds);
        foreach (var value in pose.Turns) expected.Add(value.AnimationId);
        foreach (var value in pose.Rotates) expected.Add(value.AnimationId);
        expected.Add(pose.Aim.DownAnimationId);
        expected.Add(pose.Aim.ForwardAnimationId);
        expected.Add(pose.Aim.UpAnimationId);
        expected.Add(pose.Aim.AdditiveBasePoseAnimationId);
        expected.Add(core.DynamicTransition.StandingLeft.AnimationId);
        expected.Add(core.DynamicTransition.StandingRight.AnimationId);
        expected.Add(core.DynamicTransition.CrouchingLeft.AnimationId);
        expected.Add(core.DynamicTransition.CrouchingRight.AnimationId);
        expected.Add(core.DynamicTransition.StandingLeft.AdditiveBaseAnimationId);
        foreach (ref readonly var member in core.SyncMembers) expected.Add(member.AnimationId);
        foreach (ref readonly var segment in core.ActionSegments) expected.Add(segment.AnimationId);

        Require(library.Resources.Count == expected.Count &&
            library.Resources.Keys.Order().SequenceEqual(expected.Order()),
            "The P5A animation resource closure is incomplete or duplicated.");
        var normalized = snapshot.CreateGraphBuildView().NormalizedAnimationIds.ToArray().ToHashSet();
        foreach (var animationId in expected)
        {
            Require(library.Resources.TryGetValue(animationId, out var descriptor) &&
                descriptor.AnimationId == animationId &&
                descriptor.ClipName.ToString() == $"clip_{animationId}" &&
                descriptor.PlayLengthSeconds == animationSet.Animations[animationId].PlayLength &&
                descriptor.NormalizedTrack == (normalized.Contains(animationId) ? (byte)1 : (byte)0) &&
                library.Library.HasAnimation(descriptor.ClipName),
                $"Animation resource descriptor changed for {animationId}.");
            using var loaded = library.Library.GetAnimation(descriptor.ClipName)
                ?? throw new InvalidOperationException($"Loaded library clip is missing: {animationId}");
            Require(Math.Abs(loaded.Length - descriptor.PlayLengthSeconds) <=
                AnimationLengthTolerance + 1e-6,
                $"Loaded clip duration changed for {animationId}.");
        }

        var graph = snapshot.CreateGraphBuildView();
        var skeleton = animationSet.Skeletons[graph.SkeletonId];
        Require(graph.RootMotionExtractionLogicalBoneId == skeleton.RequiredBones.Root &&
            graph.RootMotionExtractionPhysicalBoneId ==
                skeleton.LogicalToPhysical[graph.RootMotionExtractionLogicalBoneId] &&
            library.PhysicalRootBoneId == graph.RootMotionExtractionPhysicalBoneId,
            "Required root logical-to-physical mapping changed.");
        Require(skeleton.LogicalBones.Length != skeleton.PhysicalBones.Length &&
            skeleton.LogicalToPhysical.Where((value, index) => value != index).Any(),
            "The real fixture no longer exercises a non-identity logical-to-physical table.");

        using var skeletonPath = library.Root.GetPathTo(library.Skeleton);
        var expectedFilterPaths = Enumerable.Range(0, skeleton.PhysicalBones.Length)
            .Where(value => value != library.PhysicalRootBoneId &&
                HasAncestor(skeleton.PhysicalBones, value, library.PhysicalRootBoneId))
            .Select(value => $"{skeletonPath}:{library.Skeleton.GetBoneName(value)}")
            .ToArray();
        var actualFilterPaths = library.ActionFilterPaths.Select(value => value.ToString()).ToArray();
        Require(actualFilterPaths.SequenceEqual(expectedFilterPaths) &&
            library.ActionFilterPaths.All(value =>
                !string.Equals(value.ToString(), library.PhysicalRootPath.ToString(),
                    StringComparison.Ordinal)),
            "Action filter must contain every physical descendant and exclude exactly the root.");
    }

    private static void VerifyPhysicalDescriptors(
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5aAnimationRuntimeBinding binding)
    {
        var expectedBase = new[] { locomotion.StandingIdleAnimationId }
            .Concat(locomotion.StandingSamples.Select(value => value.AnimationId))
            .Append(locomotion.CrouchingIdleAnimationId)
            .Concat(locomotion.CrouchingSamples.Select(value => value.AnimationId))
            .Append(locomotion.JumpStartAnimationId)
            .Append(locomotion.FallLoopAnimationId)
            .Append(locomotion.LandAnimationId)
            .ToArray();
        var slots = binding.PhysicalSlots;
        Require(slots.Count == expectedBase.Length + 2 * pose.Turns.Length + 2 * pose.Rotates.Length,
            "The fixed Base/Turn/Rotate physical descriptors are incomplete.");
        for (var index = 0; index < expectedBase.Length; index++)
        {
            var slot = slots[index];
            Require(slot.SourceKind == CoreOccurrenceKind.Base &&
                slot.GraphSlotIndex == index && slot.AnimationId == expectedBase[index] &&
                slot.OccurrenceHandleId >= 0 && slot.AuthorityGroupId >= 0 &&
                !slot.GraphNodeName.IsEmpty,
                $"Base physical slot binding changed at {index}.");
        }

        foreach (var (kind, animations) in new[]
        {
            (CoreOccurrenceKind.Turn, pose.Turns.Select(value => value.AnimationId).ToArray()),
            (CoreOccurrenceKind.Rotate, pose.Rotates.Select(value => value.AnimationId).ToArray()),
        })
        {
            for (var index = 0; index < animations.Length; index++)
            {
                var copies = slots.Where(value => value.SourceKind == kind &&
                    value.SourceBindingIndex == index).ToArray();
                Require(copies.Length == 2 && copies[0].GraphSlotIndex == index &&
                    copies[1].GraphSlotIndex == animations.Length + index &&
                    copies[0].OccurrenceHandleId != copies[1].OccurrenceHandleId &&
                    copies[0].GraphNodeName != copies[1].GraphNodeName &&
                    copies.All(value => value.AnimationId == animations[index] &&
                        value.AuthorityGroupId == 0 && value.SyncGroupId == -1),
                    $"The {kind} physical copies alias at binding {index}.");
            }
        }

        var syncOccurrences = binding.CreateCoreView().SyncOccurrences;
        foreach (ref readonly var sync in syncOccurrences)
        {
            var syncValue = sync;
            Require(slots.Any(value => value.OccurrenceHandleId == syncValue.OccurrenceHandleId &&
                value.SyncGroupId == syncValue.GroupId &&
                value.SyncMemberIndex == syncValue.GroupMemberIndex &&
                value.AnimationId == syncValue.AnimationId),
                "Configured Sync membership was not attached to its physical Base slot.");
        }
    }

    private static void VerifyStalePairings(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5aAnimationRuntimeProfile p5a,
        AlsP5OccurrenceLayout layout,
        AlsP5CoreRuntimeBindingSnapshot snapshot,
        AlsAnimationLibraryBuildResult library)
    {
        var changedP5a = p5a with
        {
            DynamicTransition = p5a.DynamicTransition with
            {
                DistanceMeters = p5a.DynamicTransition.DistanceMeters + 0.001f,
            },
        };
        var changedBinding = AlsP5CoreRuntimeBindingCompiler.Compile(
            animationSet, locomotion, pose, changedP5a, layout);
        ExpectInvalid(() => AlsP5aAnimationRuntimeBinding.Compile(changedBinding, library),
            "binding digest");

        var changedLocomotion = locomotion with
        {
            Presentation = locomotion.Presentation with
            {
                YawRadians = locomotion.Presentation.YawRadians + 0.001f,
            },
        };
        var changedLayout = AlsP5OccurrenceLayoutCompiler.Compile(changedLocomotion, pose, p5a);
        var changedGraph = AlsP5CoreRuntimeBindingCompiler.Compile(
            animationSet, changedLocomotion, pose, p5a, changedLayout);
        ExpectInvalid(() => AlsP5aAnimationRuntimeBinding.Compile(changedGraph, library),
            "graph digest");

        ExpectInvalid(() => AlsP5aAnimationRuntimeBinding.ValidateStamp(
            library.Stamp with { LayoutDigest = library.Stamp.LayoutDigest + 1 }, snapshot),
            "layout digest");

        var changedAnimations = animationSet.Animations.ToArray();
        changedAnimations[0] = changedAnimations[0] with { Loop = !changedAnimations[0].Loop };
        var staleSet = animationSet with { Animations = changedAnimations };
        ExpectInvalid(() => AlsAnimationLibraryBuilder.BuildP5a(staleSet, snapshot).Dispose(),
            "stale animation-set definition digest");
        var changedSetDraft = animationSet with
        {
            Animations = changedAnimations,
            DefinitionDigest = string.Empty,
        };
        var changedSet = changedSetDraft with
        {
            DefinitionDigest = AlsAnimationSetPayload.ComputeDefinitionDigest(changedSetDraft),
        };
        ExpectInvalid(() => AlsAnimationLibraryBuilder.BuildP5a(changedSet, snapshot).Dispose(),
            "animation-set definition digest");
    }

    private void VerifyMutatedSkeletonRejected(
        AlsAnimationSetDefinition animationSet,
        AlsP5CoreRuntimeBindingSnapshot snapshot)
    {
        using var hierarchyLibrary = AlsAnimationLibraryBuilder.BuildP5a(animationSet, snapshot);
        AddChild(hierarchyLibrary.Root);
        var descendant = Enumerable.Range(0, hierarchyLibrary.Skeleton.GetBoneCount())
            .First(value => hierarchyLibrary.Skeleton.GetBoneParent(value) ==
                hierarchyLibrary.PhysicalRootBoneId);
        hierarchyLibrary.Skeleton.SetBoneParent(descendant, -1);
        ExpectInvalid(() => AlsP5aAnimationRuntimeBinding.Compile(snapshot, hierarchyLibrary),
            "skeleton hierarchy");
    }

    private static void VerifyInvalidDurationsRejected(
        AlsAnimationSetDefinition animationSet,
        AlsP5CoreRuntimeBindingSnapshot snapshot)
    {
        var animationId = snapshot.CreateGraphBuildView().StandingIdleAnimationId;
        foreach (var length in new[]
            { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var animations = animationSet.Animations.ToArray();
            animations[animationId] = animations[animationId] with { PlayLength = length };
            var draft = animationSet with { Animations = animations };
            var digest = AlsAnimationSetPayload.ComputeDefinitionDigest(draft);
            var invalidSet = draft with { DefinitionDigest = digest };
            // Forge only the header to exercise BuildP5a's own publication gate,
            // independently of the upstream compiler's duration validation.
            var clone = (AlsP5CoreRuntimeBindingSnapshot)typeof(object)
                .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.Invoke(snapshot, null)!;
            typeof(AlsP5CoreRuntimeBindingSnapshot)
                .GetField("<AnimationSetDefinitionDigest>k__BackingField",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(clone, digest);
            try
            {
                using var unexpected = AlsAnimationLibraryBuilder.BuildP5a(invalidSet, clone);
            }
            catch (InvalidOperationException exception) when (
                exception.Message == "A P5A clip has an invalid duration.")
            {
                continue;
            }
            throw new InvalidOperationException("BuildP5a published an invalid clip duration.");
        }
    }

    private static void VerifyCoherentNonIdentityRootFixture()
    {
        var root = new Node { Name = "SyntheticRoot" };
        var skeleton = new Skeleton3D { Name = "SyntheticSkeleton" };
        root.AddChild(skeleton);
        var names = new[] { "wrapper", "dummy", "root", "child" };
        var parents = new[] { -1, 0, 0, 2 };
        for (var index = 0; index < names.Length; index++)
        {
            using var name = new StringName(names[index]);
            skeleton.AddBone(name);
            skeleton.SetBoneParent(index, parents[index]);
            skeleton.SetBoneRest(index, Transform3D.Identity);
        }

        var logicalToPhysical = new[] { 0, 2, 3, 1 };
        var physicalToLogical = new[] { 0, 3, 1, 2 };
        var physical = names.Select((name, index) => new AlsBoneDefinition(
            physicalToLogical[index], index, name,
            physicalToLogical[index] == 0 ? -1 :
                physicalToLogical[parents[index]],
            parents[index],
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.Identity,
            System.Numerics.Vector3.One)).ToArray();
        var logical = new[]
        {
            physical[0] with { LogicalId = 0, ParentLogicalId = -1 },
            physical[2] with { LogicalId = 1, ParentLogicalId = 0 },
            physical[3] with { LogicalId = 2, ParentLogicalId = 1 },
            physical[1] with { LogicalId = 3, ParentLogicalId = 0 },
        };
        var draft = new AlsSkeletonDefinition(
            "synthetic", "/Synthetic", string.Empty, string.Empty,
            logical, physical, [], [], logicalToPhysical, physicalToLogical,
            new AlsRequiredBoneIds(1, 1, 2, 2));
        var definition = draft with
        {
            TargetPhysicalRestPoseHash =
                AlsImportedResourceAuditor.ComputeTargetRestPoseHash(skeleton, draft),
        };

        var paths = AlsAnimationLibraryBuilder.BuildP5aActionFilterPaths(
            root, skeleton, definition, 1, 2, "synthetic");
        try
        {
            Require(paths.Select(value => value.ToString())
                .SequenceEqual(new[] { "SyntheticSkeleton:child" }),
                "A coherent non-identity root mapping did not produce physical descendants only.");
            var wrongMapping = definition with
            {
                LogicalToPhysical = new[] { 0, 1, 3, 2 },
                PhysicalToLogical = new[] { 0, 1, 3, 2 },
            };
            ExpectInvalid(() =>
            {
                var invalidPaths = AlsAnimationLibraryBuilder.BuildP5aActionFilterPaths(
                    root, skeleton, wrongMapping, 1, 1, "wrong root identity");
                foreach (var path in invalidPaths) path.Dispose();
            }, "root identity");
        }
        finally
        {
            foreach (var path in paths) path.Dispose();
            root.Free();
        }
    }

    private static bool HasAncestor(
        IReadOnlyList<AlsBoneDefinition> bones,
        int boneId,
        int ancestor)
    {
        for (var current = bones[boneId].ParentPhysicalId; current >= 0;
            current = bones[current].ParentPhysicalId)
        {
            if (current == ancestor) return true;
        }
        return false;
    }

    private static void ExpectInvalid(Action action, string label)
    {
        try
        {
            action();
            throw new InvalidOperationException($"A stale {label} pairing was accepted.");
        }
        catch (InvalidOperationException exception) when (
            !exception.Message.StartsWith("A stale ", StringComparison.Ordinal))
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
