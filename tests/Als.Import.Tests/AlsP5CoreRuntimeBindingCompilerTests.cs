using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;
using GodotAls.Import.Compilation;
using CoreOccurrenceEntry = GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry;
using CoreOccurrenceKind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;
using ImportOccurrenceEntry = GodotAls.Import.Compilation.AlsP5OccurrenceLayoutEntry;
using ImportOccurrenceKind = GodotAls.Import.Compilation.AlsP5OccurrenceSourceKind;

namespace GodotAls.Import.Tests;

public sealed class AlsP5CoreRuntimeBindingCompilerTests
{
    private const ulong FrozenLayoutDigest = 0xD6FEF54173240D32UL;
    private const ulong FrozenBindingDigest = 0x2B4BE600D531C734UL;
    private const ulong FrozenGraphDigest = 0x44403C2869D8F615UL;

    [Fact]
    public void PublicSurfaceIsFrozenStackSafeAndGodotFree()
    {
        var compiler = typeof(AlsP5CoreRuntimeBindingCompiler);
        Assert.True(compiler.IsPublic && compiler.IsAbstract && compiler.IsSealed);
        var compile = Assert.Single(compiler.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal("Compile", compile.Name);
        Assert.Equal(typeof(AlsP5CoreRuntimeBindingSnapshot), compile.ReturnType);
        Assert.Equal(
            [typeof(AlsAnimationSetDefinition), typeof(AlsLocomotionAnimationProfile),
             typeof(AlsPoseAnimationProfile), typeof(AlsP5aAnimationRuntimeProfile),
             typeof(AlsP5OccurrenceLayout)],
            compile.GetParameters().Select(value => value.ParameterType));

        var snapshot = typeof(AlsP5CoreRuntimeBindingSnapshot);
        Assert.True(snapshot.IsPublic && snapshot.IsClass && snapshot.IsSealed);
        Assert.Empty(snapshot.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(
            ["AnimationSetDefinitionDigest", "Digest", "GraphDigest", "LayoutDigest", "Version"],
            snapshot.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(value => value.Name).Order());
        Assert.Equal(
            ["CreateCoreView", "CreateGraphBuildView", "CreateOccurrenceLayoutView"],
            snapshot.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(value => !value.IsSpecialName)
                .Select(value => value.Name).Order());
        Assert.All(snapshot.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(value => value.FieldType.IsArray), value =>
        {
            Assert.True(value.IsPrivate, value.Name);
            Assert.True(value.IsInitOnly, value.Name);
        });
        Assert.DoesNotContain(snapshot.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            value => value is FieldInfo || value is PropertyInfo property &&
                (property.PropertyType.IsArray || property.PropertyType != typeof(string) &&
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType)));

        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsP5GraphSample>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsP5GraphMaskHeader>());
        var graphView = typeof(AlsP5GraphBuildView);
        Assert.True(graphView.IsPublic && graphView.IsValueType && graphView.IsByRefLike);
        Assert.Equal(
            [
                "Version", "Digest", "SkeletonId", "MannequinMeshId",
                "RootMotionExtractionLogicalBoneId", "RootMotionExtractionPhysicalBoneId",
                "Presentation", "StandingIdleAnimationId", "CrouchingIdleAnimationId",
                "JumpStartAnimationId", "FallLoopAnimationId", "LandAnimationId",
                "LeanAdditiveBaseAnimationId", "StandingSamples", "CrouchingSamples",
                "LeanSamples", "AllAnimationIds", "Aim", "Turns", "Rotates",
                "MaskHeaders", "LogicalBoneIds", "NormalizedAnimationIds",
            ],
            graphView.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(value => value.MetadataToken).Select(value => value.Name));
        var constructor = Assert.Single(graphView.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(constructor.GetParameters(), value => Assert.False(value.ParameterType.IsByRef, value.Name));

        Assert.DoesNotContain(typeof(AlsP5CoreRuntimeBindingCompiler).Assembly.GetReferencedAssemblies(),
            value => value.Name is "GodotSharp" or "Godot");
    }

    [Fact]
    public void CanonicalFixtureMapsCompleteRuntimeOccurrenceAndGraphClosure()
    {
        var fixture = Fixture.Create();
        var snapshot = fixture.Compile();
        var runtime = snapshot.CreateCoreView();
        var occurrence = snapshot.CreateOccurrenceLayoutView();
        var graph = snapshot.CreateGraphBuildView();

        Assert.Equal(1, snapshot.Version);
        Assert.Equal(fixture.Set.DefinitionDigest, snapshot.AnimationSetDefinitionDigest);
        Assert.Equal(snapshot.Digest, runtime.Digest);
        Assert.Equal(snapshot.LayoutDigest, runtime.LayoutDigest);
        Assert.Equal(snapshot.LayoutDigest, occurrence.Digest);
        Assert.Equal(snapshot.GraphDigest, graph.Digest);
        Assert.Equal(1, runtime.Version);
        Assert.Equal(1, occurrence.Version);
        Assert.Equal(1, graph.Version);

        Assert.Equal(fixture.Layout.Entries.Length, occurrence.Entries.Length);
        for (var index = 0; index < occurrence.Entries.Length; index++)
        {
            var source = fixture.Layout.Entries[index];
            var mapped = occurrence.Entries[index];
            Assert.Equal((byte)source.SourceKind, (byte)mapped.SourceKind);
            Assert.Equal(source.SourceBindingIndex, mapped.SourceBindingIndex);
            Assert.Equal(source.GraphSlotIndex, mapped.GraphSlotIndex);
            Assert.Equal(source.OccurrenceHandleId, mapped.OccurrenceHandleId);
            Assert.Equal(source.AuthorityGroupId, mapped.AuthorityGroupId);
        }

        Assert.Equal(fixture.Set.Animations.Length, runtime.AnimationCurveRanges.Length);
        Assert.Equal(fixture.Set.Animations.Length, runtime.AllowTransitionsBindingIndices.Length);
        var bindingOffset = 0;
        var keyOffset = 0;
        for (var animationId = 0; animationId < fixture.Set.Animations.Length; animationId++)
        {
            var animation = fixture.Set.Animations[animationId];
            var range = runtime.AnimationCurveRanges[animationId];
            Assert.Equal(animationId, animation.Id);
            Assert.Equal(new AlsAnimationCurveRange(animationId, bindingOffset, animation.Curves.Length), range);
            for (var curveIndex = 0; curveIndex < animation.Curves.Length; curveIndex++)
            {
                var curve = animation.Curves[curveIndex];
                var identity = runtime.CurveBindingIdentities[bindingOffset];
                var binding = runtime.CurveBindings[bindingOffset];
                Assert.Equal(new AlsP5CurveBindingIdentity(animationId, curve.CurveId), identity);
                Assert.Equal(curve.CurveId, binding.CurveId);
                Assert.Equal(keyOffset, binding.KeyOffset);
                Assert.Equal(curve.Keys.Length, binding.KeyCount);
                Assert.Equal(animation.PlayLength, binding.DurationSeconds);
                Assert.Equal((byte)1, binding.Required);
                Assert.Equal(animation.Loop ? (byte)1 : (byte)0, binding.Loop);
                for (var curveKeyIndex = 0; curveKeyIndex < curve.Keys.Length; curveKeyIndex++)
                {
                    var sourceKey = curve.Keys[curveKeyIndex];
                    var key = runtime.CurveKeys[keyOffset + curveKeyIndex];
                    Assert.Equal(sourceKey.TimeSeconds, key.TimeSeconds);
                    Assert.Equal(sourceKey.Value, key.Value);
                    Assert.Equal(sourceKey.ArriveTangent, key.ArriveTangent);
                    Assert.Equal(sourceKey.LeaveTangent, key.LeaveTangent);
                    Assert.Equal((byte)sourceKey.Interpolation, (byte)key.Interpolation);
                }
                keyOffset += curve.Keys.Length;
                bindingOffset++;
            }
        }
        Assert.Equal(runtime.CurveBindings.Length, bindingOffset);
        Assert.Equal(runtime.CurveKeys.Length, keyOffset);
        Assert.Equal(fixture.P5a.AllowTransitions.MissingValue, runtime.AllowTransitionsPolicy.MissingValue);
        Assert.Equal((byte)fixture.P5a.AllowTransitions.CombineMode,
            (byte)runtime.AllowTransitionsPolicy.CombineMode);
        Assert.Equal(fixture.P5a.AllowTransitions.ClampMinimum, runtime.AllowTransitionsPolicy.ClampMinimum);
        Assert.Equal(fixture.P5a.AllowTransitions.ClampMaximum, runtime.AllowTransitionsPolicy.ClampMaximum);
        for (var animationId = 0; animationId < fixture.Set.Animations.Length; animationId++)
        {
            var curveId = fixture.P5a.AllowTransitions.AnimationCurveIds[animationId];
            var expected = curveId < 0 ? -1 : FindCurveBinding(runtime, animationId, curveId);
            Assert.Equal(expected, runtime.AllowTransitionsBindingIndices[animationId]);
        }

        Assert.Equal(fixture.Pose.FootCurves.Bindings.Length, runtime.FootCurveBindings.Length);
        for (var index = 0; index < runtime.FootCurveBindings.Length; index++)
        {
            var source = fixture.Pose.FootCurves.Bindings[index];
            Assert.Equal(new AlsP4FootCurveRuntimeBinding(source.AnimationId, source.LeftLockCurveId,
                source.RightLockCurveId, source.LeftLockDefault, source.RightLockDefault),
                runtime.FootCurveBindings[index]);
        }
        Assert.Equal(fixture.Pose.FootCurves.GroundedIkWeight, runtime.GroundedIkWeight);
        Assert.Equal(fixture.Pose.FootCurves.JumpStartIkWeight, runtime.JumpStartIkWeight);
        Assert.Equal(fixture.Pose.FootCurves.FallLoopIkWeight, runtime.FallLoopIkWeight);
        Assert.Equal(fixture.Pose.FootCurves.LandRecoveryIkWeight, runtime.LandRecoveryIkWeight);

        Assert.Equal(17, runtime.SyncMembers.Length);
        Assert.Equal(34, runtime.SyncMarkers.Length);
        Assert.Equal(17, runtime.SyncOccurrences.Length);
        Assert.Equal(new AlsSyncGroupBinding(0, 0, 17, 0, 1), runtime.SyncGroup);
        for (var index = 0; index < runtime.SyncMembers.Length; index++)
        {
            var member = fixture.P5a.SyncGroups[0].Members[index];
            Assert.Equal(new AlsSyncMemberBinding(0, member.AnimationId, member.DurationSeconds, 1, 1),
                runtime.SyncMembers[index]);
            Assert.Equal(0, runtime.SyncMarkers[index * 2].MarkerNameId);
            Assert.Equal(1, runtime.SyncMarkers[index * 2 + 1].MarkerNameId);
            var mapping = fixture.Layout.SyncMappings[index];
            Assert.Equal(new AlsP5SyncOccurrenceBinding(mapping.SyncGroupId, mapping.GroupMemberIndex,
                mapping.AnimationId, mapping.OccurrenceHandleId), runtime.SyncOccurrences[index]);
        }

        var transitionEntry = fixture.Layout.Entries.Single(value => value.SourceKind == ImportOccurrenceKind.Transition);
        Assert.Equal(transitionEntry.OccurrenceHandleId, runtime.DynamicTransition.OccurrenceHandleId);
        Assert.Equal(transitionEntry.AuthorityGroupId, runtime.DynamicTransition.AuthorityGroupId);
        Assert.Equal(fixture.P5a.DynamicTransition.DistanceMeters, runtime.DynamicTransition.DistanceMeters);
        Assert.Equal(fixture.P5a.DynamicTransition.BlendSeconds, runtime.DynamicTransition.BlendSeconds);
        Assert.Equal(fixture.P5a.DynamicTransition.PlayRate, runtime.DynamicTransition.PlayRate);
        Assert.Equal(fixture.P5a.DynamicTransition.CooldownFrames, runtime.DynamicTransition.CooldownFrames);
        AssertTransitionClip(fixture, fixture.P5a.DynamicTransition.Slots[0], runtime.DynamicTransition.StandingLeft);
        AssertTransitionClip(fixture, fixture.P5a.DynamicTransition.Slots[1], runtime.DynamicTransition.StandingRight);
        AssertTransitionClip(fixture, fixture.P5a.DynamicTransition.Slots[2], runtime.DynamicTransition.CrouchingLeft);
        AssertTransitionClip(fixture, fixture.P5a.DynamicTransition.Slots[3], runtime.DynamicTransition.CrouchingRight);

        Assert.Equal(fixture.P5a.Actions.Length, runtime.ActionDefinitions.Length);
        Assert.Equal(fixture.P5a.Actions.Sum(value => value.Sections.Length), runtime.ActionSections.Length);
        Assert.Equal(fixture.P5a.SegmentBindings.Length, runtime.ActionSegments.Length);
        Assert.Equal(fixture.P5a.Actions.Length, runtime.ActionTimelineRanges.Length);
        Assert.Equal(fixture.P5a.TimelineEntries.Length,
            runtime.ActionTimelineRanges.ToArray().Sum(value => value.DefinitionCount));
        var actionOrdinals = new List<int>();
        foreach (ref readonly var range in runtime.ActionTimelineRanges)
        {
            foreach (ref readonly var definition in
                runtime.TimelineDefinitions.Slice(range.DefinitionOffset, range.DefinitionCount))
            {
                actionOrdinals.Add(definition.BoundaryOrdinal);
            }
        }
        Assert.Equal(fixture.P5a.TimelineEntries.Select(value => value.BoundaryOrdinal), actionOrdinals);
        var action = fixture.P5a.Actions[0];
        var actionEntry = fixture.Layout.Entries.Single(value =>
            value.SourceKind == ImportOccurrenceKind.ActionMontage && value.SourceBindingIndex == action.DefinitionId);
        var sequenceEntry = fixture.Layout.Entries.Single(value =>
            value.SourceKind == ImportOccurrenceKind.ActionSequence && value.SourceBindingIndex == 0);
        var actionBinding = runtime.ActionDefinitions[0];
        Assert.Equal(actionEntry.OccurrenceHandleId, actionBinding.OccurrenceHandleId);
        Assert.Equal(actionEntry.AuthorityGroupId, actionBinding.MontageAuthorityGroupId);
        Assert.Equal(sequenceEntry.AuthorityGroupId, actionBinding.SequenceAuthorityGroupId);
        Assert.Equal(action.DefinitionId, actionBinding.DefinitionId);
        Assert.Equal(action.MontageId, actionBinding.MontageId);
        Assert.Equal(action.MontageDurationSeconds, actionBinding.MontageDurationSeconds);
        Assert.Equal(action.SlotId, actionBinding.SlotId);
        Assert.Equal(action.StartSectionId, actionBinding.StartSectionId);
        Assert.Equal(action.Priority, actionBinding.Priority);
        Assert.Equal(action.PlayRate, actionBinding.PlayRate);
        Assert.Equal(action.BlendSeconds, actionBinding.BlendSeconds);
        Assert.Equal(action.Interruptible ? (byte)1 : (byte)0, actionBinding.Interruptible);
        Assert.Equal(action.LoopPolicy == AlsP5LoopPolicy.Loop ? (byte)1 : (byte)0, actionBinding.Loop);
        for (var index = 0; index < runtime.ActionSegments.Length; index++)
        {
            var source = fixture.P5a.SegmentBindings[index];
            var mapped = runtime.ActionSegments[index];
            var handle = fixture.Layout.Entries.Single(value =>
                value.SourceKind == ImportOccurrenceKind.ActionSequence && value.SourceBindingIndex == index);
            Assert.Equal(handle.OccurrenceHandleId, mapped.OccurrenceHandleId);
            Assert.Equal(source.ActionDefinitionId, mapped.ActionDefinitionId);
            Assert.Equal(source.SlotId, mapped.SlotId);
            Assert.Equal(source.SegmentId, mapped.SegmentId);
            Assert.Equal(source.AnimationId, mapped.AnimationId);
            Assert.Equal(source.MontageStartTime, mapped.MontageStartTime);
            Assert.Equal(source.MontageEndTime, mapped.MontageEndTime);
            Assert.Equal(source.AnimationStartTime, mapped.AnimationStartTime);
            Assert.Equal(source.AnimationEndTime, mapped.AnimationEndTime);
            Assert.Equal(source.PlayRate, mapped.PlayRate);
            Assert.Equal(source.LoopCount, mapped.LoopCount);
        }

        var skeleton = fixture.Set.Skeletons[fixture.Locomotion.SkeletonId];
        Assert.Equal(fixture.Locomotion.SkeletonId, graph.SkeletonId);
        Assert.Equal(fixture.Locomotion.MannequinMeshId, graph.MannequinMeshId);
        Assert.Equal(skeleton.RequiredBones.Root, graph.RootMotionExtractionLogicalBoneId);
        Assert.Equal(skeleton.LogicalToPhysical[skeleton.RequiredBones.Root],
            graph.RootMotionExtractionPhysicalBoneId);
        Assert.Equal(fixture.Locomotion.Presentation, graph.Presentation);
        Assert.Equal(fixture.Locomotion.StandingIdleAnimationId, graph.StandingIdleAnimationId);
        Assert.Equal(fixture.Locomotion.CrouchingIdleAnimationId, graph.CrouchingIdleAnimationId);
        Assert.Equal(fixture.Locomotion.JumpStartAnimationId, graph.JumpStartAnimationId);
        Assert.Equal(fixture.Locomotion.FallLoopAnimationId, graph.FallLoopAnimationId);
        Assert.Equal(fixture.Locomotion.LandAnimationId, graph.LandAnimationId);
        Assert.Equal(fixture.Locomotion.LeanAdditiveBasePoseAnimationId, graph.LeanAdditiveBaseAnimationId);
        AssertSamples(fixture.Locomotion.StandingSamples, graph.StandingSamples);
        AssertSamples(fixture.Locomotion.CrouchingSamples, graph.CrouchingSamples);
        AssertSamples(fixture.Locomotion.LeanAdditiveSamples, graph.LeanSamples);
        Assert.True(graph.AllAnimationIds.SequenceEqual(fixture.Locomotion.AllAnimationIds));
        Assert.Equal(fixture.Pose.Aim, graph.Aim);
        Assert.True(graph.Turns.SequenceEqual(fixture.Pose.Turns));
        Assert.True(graph.Rotates.SequenceEqual(fixture.Pose.Rotates));
        Assert.Equal(fixture.Pose.Masks.Entries.Length, graph.MaskHeaders.Length);
        var boneOffset = 0;
        for (var index = 0; index < graph.MaskHeaders.Length; index++)
        {
            var mask = fixture.Pose.Masks.Entries[index];
            Assert.Equal(new AlsP5GraphMaskHeader(mask.Kind, mask.RootBoneId, boneOffset, mask.BoneIds.Length),
                graph.MaskHeaders[index]);
            Assert.True(graph.LogicalBoneIds.Slice(boneOffset, mask.BoneIds.Length).SequenceEqual(mask.BoneIds));
            boneOffset += mask.BoneIds.Length;
        }
        Assert.Equal(graph.LogicalBoneIds.Length, boneOffset);
        Assert.True(graph.NormalizedAnimationIds.SequenceEqual(new[]
        {
            fixture.Pose.Aim.AdditiveBasePoseAnimationId,
            fixture.Pose.Aim.DownAnimationId,
            fixture.Pose.Aim.ForwardAnimationId,
            fixture.Pose.Aim.UpAnimationId,
        }));
    }

    [Fact]
    public void DigestsMatchIndependentLittleEndianWritersAndFrozenConstants()
    {
        var fixture = Fixture.Create();
        var snapshot = fixture.Compile();
        var runtime = snapshot.CreateCoreView();
        var occurrence = snapshot.CreateOccurrenceLayoutView();
        var graph = snapshot.CreateGraphBuildView();

        Assert.Equal(FrozenLayoutDigest, snapshot.LayoutDigest);
        Assert.Equal(FrozenBindingDigest, snapshot.Digest);
        Assert.Equal(FrozenGraphDigest, snapshot.GraphDigest);
        Assert.Equal(ReferenceLayoutDigest(occurrence.Version, occurrence.Entries), snapshot.LayoutDigest);
        Assert.Equal(ReferenceBindingDigest(in runtime), snapshot.Digest);
        Assert.Equal(ReferenceGraphDigest(in graph), snapshot.GraphDigest);
    }

    [Fact]
    public void RuntimeTimelineActionAndSyncRowsMatchCurrentAuthoredSourcesIndependently()
    {
        var fixture = Fixture.Create();
        var runtime = fixture.Compile().CreateCoreView();
        var baseIds = AuthoredBaseAnimationIds(fixture.Locomotion);
        var general = new List<AlsTimelineEventDefinition>();
        foreach (var occurrence in fixture.Layout.Entries)
        {
            IEnumerable<int> animationIds = occurrence.SourceKind switch
            {
                ImportOccurrenceKind.Base => [baseIds[occurrence.SourceBindingIndex]],
                ImportOccurrenceKind.Turn => [fixture.Pose.Turns[occurrence.SourceBindingIndex].AnimationId],
                ImportOccurrenceKind.Rotate => [fixture.Pose.Rotates[occurrence.SourceBindingIndex].AnimationId],
                ImportOccurrenceKind.Transition => fixture.P5a.DynamicTransition.Slots
                    .Select(value => value.AnimationId).Distinct(),
                _ => [],
            };
            foreach (var animationId in animationIds)
            {
                foreach (var authored in fixture.Set.Animations[animationId].Timeline)
                {
                    general.Add(ExpectedTimeline(authored, animationId, -1,
                        occurrence.OccurrenceHandleId, AlsTimelineSourceKind.Animation,
                        authored.TimeSeconds, authored.DurationSeconds, 0));
                }
            }
        }
        var actionOffset = runtime.ActionTimelineRanges[0].DefinitionOffset;
        Assert.Equal(general, runtime.TimelineDefinitions[..actionOffset].ToArray());

        var expectedSyncAnimations = fixture.Locomotion.StandingSamples
            .Concat(fixture.Locomotion.CrouchingSamples)
            .Select(value => value.AnimationId)
            .OrderBy(value => fixture.Set.Animations[value].StableId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSyncAnimations.Length, runtime.SyncMembers.Length);
        var usedHandles = new HashSet<int>();
        for (var index = 0; index < expectedSyncAnimations.Length; index++)
        {
            var animationId = expectedSyncAnimations[index];
            var animation = fixture.Set.Animations[animationId];
            Assert.Equal(new AlsSyncMemberBinding(0, animationId, animation.PlayLength, 1, 1),
                runtime.SyncMembers[index]);
            var baseIndex = Array.IndexOf(baseIds, animationId);
            Assert.True(baseIndex >= 0);
            var occurrence = fixture.Layout.Entries.Single(value =>
                value.SourceKind == ImportOccurrenceKind.Base && value.SourceBindingIndex == baseIndex);
            Assert.True(usedHandles.Add(occurrence.OccurrenceHandleId));
            Assert.Equal(new AlsP5SyncOccurrenceBinding(0, index, animationId,
                occurrence.OccurrenceHandleId), runtime.SyncOccurrences[index]);
            var left = animation.SyncMarkers.Single(value => value.Name == "Left");
            var right = animation.SyncMarkers.Single(value => value.Name == "Right");
            Assert.Equal(new AlsSyncMarkerDefinition(left.MarkerId, 0, animationId,
                left.SourceIndex, left.TrackIndex, left.TimeSeconds), runtime.SyncMarkers[index * 2]);
            Assert.Equal(new AlsSyncMarkerDefinition(right.MarkerId, 1, animationId,
                right.SourceIndex, right.TrackIndex, right.TimeSeconds), runtime.SyncMarkers[index * 2 + 1]);
        }

        var montage = fixture.Set.Montages.Single(value =>
            value.StableId == "2d9341182885d90ad666fff32c025937438b1827");
        Assert.Equal(montage.Sections.Length, runtime.ActionSections.Length);
        for (var index = 0; index < montage.Sections.Length; index++)
        {
            var source = montage.Sections[index];
            var end = index + 1 < montage.Sections.Length
                ? montage.Sections[index + 1].StartTime : montage.PlayLength;
            Assert.Equal(new AlsActionSectionBinding(0, source.SectionId, source.NextSectionId,
                source.StartTime, end), runtime.ActionSections[index]);
        }
        var authoredSegments = montage.Slots.Single().Segments;
        Assert.Equal(authoredSegments.Length, runtime.ActionSegments.Length);
        for (var index = 0; index < authoredSegments.Length; index++)
        {
            var source = authoredSegments[index];
            var end = index + 1 < authoredSegments.Length
                ? authoredSegments[index + 1].StartPosition : montage.PlayLength;
            var occurrence = fixture.Layout.Entries.Single(value =>
                value.SourceKind == ImportOccurrenceKind.ActionSequence && value.SourceBindingIndex == index);
            Assert.Equal(new AlsActionSegmentBinding(occurrence.OccurrenceHandleId, 0,
                montage.Slots[0].SlotId, source.SegmentId, source.AnimationId,
                source.StartPosition, end, source.AnimationStartTime, source.AnimationEndTime,
                source.PlayRate, source.LoopCount), runtime.ActionSegments[index]);
        }
        var expectedActionTimeline = BuildAuthoredActionTimeline(fixture, montage, runtime.ActionSegments);
        Assert.Equal(expectedActionTimeline,
            runtime.TimelineDefinitions.Slice(actionOffset,
                runtime.ActionTimelineRanges[0].DefinitionCount).ToArray());
    }

    [Fact]
    public void SnapshotOwnsEveryCallerArrayAndDefensiveProfileClone()
    {
        var fixture = Fixture.Create();
        var snapshot = fixture.Compile();
        var beforeBinding = snapshot.Digest;
        var beforeGraph = snapshot.GraphDigest;
        var beforeLayout = snapshot.LayoutDigest;
        var beforeRuntimeView = snapshot.CreateCoreView();
        var beforeRuntime = CaptureRuntime(in beforeRuntimeView);
        var beforeOccurrence = snapshot.CreateOccurrenceLayoutView().Entries.ToArray();
        var beforeGraphIds = snapshot.CreateGraphBuildView().AllAnimationIds.ToArray();

        fixture.Set.Animations[0].Curves[0].Keys[0] = fixture.Set.Animations[0].Curves[0].Keys[0] with { Value = 9876f };
        fixture.Locomotion.StandingSamples[0] = fixture.Locomotion.StandingSamples[0] with { X = 9876f };
        fixture.Locomotion.AllAnimationIds[0] = 9876;
        var turns = fixture.Pose.Turns;
        turns[0] = turns[0] with { BasePlayRate = 9876f };
        var maskEntries = fixture.Pose.Masks.Entries;
        var maskBones = maskEntries[0].BoneIds;
        maskBones[0] = 9876;
        var actions = fixture.P5a.Actions;
        actions[0] = actions[0] with { Priority = 9876 };
        var actionSections = fixture.P5a.Actions[0].Sections;
        actionSections[0] = actionSections[0] with { StartTime = 9876f };
        var segments = fixture.P5a.SegmentBindings;
        segments[0] = segments[0] with { MontageStartTime = 9876f };
        var timeline = fixture.P5a.TimelineEntries;
        timeline[0] = timeline[0] with { BoundaryOrdinal = 9876 };
        var syncMembers = fixture.P5a.SyncGroups[0].Members;
        syncMembers[0] = syncMembers[0] with { DurationSeconds = 9876f };
        var transitionSlots = fixture.P5a.DynamicTransition.Slots;
        transitionSlots[0] = transitionSlots[0] with { AnimationId = 9876 };
        var layoutEntries = fixture.Layout.Entries;
        layoutEntries[0] = layoutEntries[0] with { GraphSlotIndex = 9876 };
        var syncMappings = fixture.Layout.SyncMappings;
        syncMappings[0] = syncMappings[0] with { OccurrenceHandleId = 9876 };

        Assert.Equal(beforeBinding, snapshot.Digest);
        Assert.Equal(beforeGraph, snapshot.GraphDigest);
        Assert.Equal(beforeLayout, snapshot.LayoutDigest);
        var afterRuntimeView = snapshot.CreateCoreView();
        Assert.Equal(beforeRuntime, CaptureRuntime(in afterRuntimeView));
        Assert.Equal(beforeOccurrence, snapshot.CreateOccurrenceLayoutView().Entries.ToArray());
        Assert.Equal(beforeGraphIds, snapshot.CreateGraphBuildView().AllAnimationIds.ToArray());
    }

    [Fact]
    public void RuntimeGraphAndLayoutMutationsChangeOnlyOwningDigests()
    {
        var fixture = Fixture.Create();
        var baseline = fixture.Compile();

        var changedP5a = fixture.P5a with
        {
            DynamicTransition = fixture.P5a.DynamicTransition with
            {
                DistanceMeters = fixture.P5a.DynamicTransition.DistanceMeters + 0.001f,
            },
        };
        var runtimeChanged = AlsP5CoreRuntimeBindingCompiler.Compile(
            fixture.Set, fixture.Locomotion, fixture.Pose, changedP5a, fixture.Layout);
        Assert.NotEqual(baseline.Digest, runtimeChanged.Digest);
        Assert.Equal(baseline.GraphDigest, runtimeChanged.GraphDigest);

        var changedLocomotion = fixture.Locomotion with
        {
            Presentation = fixture.Locomotion.Presentation with
            {
                YawRadians = fixture.Locomotion.Presentation.YawRadians + 0.001f,
            },
        };
        var graphChanged = AlsP5CoreRuntimeBindingCompiler.Compile(
            fixture.Set, changedLocomotion, fixture.Pose, fixture.P5a,
            AlsP5OccurrenceLayoutCompiler.Compile(changedLocomotion, fixture.Pose, fixture.P5a));
        Assert.Equal(baseline.Digest, graphChanged.Digest);
        Assert.NotEqual(baseline.GraphDigest, graphChanged.GraphDigest);

        var entries = fixture.Layout.Entries;
        entries[0] = entries[0] with { AuthorityGroupId = 1 };
        var changedLayout = fixture.Layout with
        {
            Entries = entries,
            Digest = ReferenceLayoutDigest(fixture.Layout.Version, entries),
        };
        Assert.Throws<ArgumentException>(() => AlsP5CoreRuntimeBindingCompiler.Compile(
            fixture.Set, fixture.Locomotion, fixture.Pose, fixture.P5a, changedLayout));
    }

    [Fact]
    public void RootExtractionUsesNonIdentityLogicalToPhysicalMapping()
    {
        var fixture = Fixture.Create();
        var skeletonId = fixture.Locomotion.SkeletonId;
        var skeletons = fixture.Set.Skeletons.ToArray();
        var skeleton = skeletons[skeletonId];
        var logicalRoot = skeleton.RequiredBones.Root;
        var oldPhysicalRoot = skeleton.LogicalToPhysical[logicalRoot];
        var rig = fixture.Pose.FootRig;
        var footRigBones = new HashSet<int>
        {
            rig.PelvisBoneId,
            rig.Left.ThighBoneId, rig.Left.KneeBoneId, rig.Left.FootBoneId,
            rig.Right.ThighBoneId, rig.Right.KneeBoneId, rig.Right.FootBoneId,
        };
        var newPhysicalRoot = Enumerable.Range(0, skeleton.PhysicalBones.Length)
            .First(value => value != oldPhysicalRoot && !footRigBones.Contains(value));
        var swappedLogical = skeleton.PhysicalToLogical[newPhysicalRoot];
        Assert.NotEqual(logicalRoot, swappedLogical);

        var logicalToPhysical = skeleton.LogicalToPhysical.ToArray();
        var physicalToLogical = skeleton.PhysicalToLogical.ToArray();
        logicalToPhysical[logicalRoot] = newPhysicalRoot;
        logicalToPhysical[swappedLogical] = oldPhysicalRoot;
        physicalToLogical[newPhysicalRoot] = logicalRoot;
        physicalToLogical[oldPhysicalRoot] = swappedLogical;
        skeletons[skeletonId] = skeleton with
        {
            LogicalToPhysical = logicalToPhysical,
            PhysicalToLogical = physicalToLogical,
        };
        var changedSet = RefreshDigest(fixture.Set with { Skeletons = skeletons });

        var snapshot = fixture.Compile(set: changedSet);
        var graph = snapshot.CreateGraphBuildView();
        Assert.Equal(logicalRoot, graph.RootMotionExtractionLogicalBoneId);
        Assert.Equal(newPhysicalRoot, graph.RootMotionExtractionPhysicalBoneId);
        Assert.NotEqual(graph.RootMotionExtractionLogicalBoneId,
            graph.RootMotionExtractionPhysicalBoneId);
    }

    [Fact]
    public void DefinitionAndLayoutHeadersAreRecomputedBeforePublication()
    {
        var fixture = Fixture.Create();
        Assert.Throws<ArgumentException>(() => fixture.Compile(fixture.Set with { DefinitionDigest = "A".PadLeft(64, '0') }));
        Assert.Throws<ArgumentException>(() => fixture.Compile(fixture.Set with { DefinitionDigest = fixture.Set.DefinitionDigest.ToUpperInvariant() }));
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with { Version = 2 }));
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with { Digest = fixture.Layout.Digest + 1 }));
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with { Digest = 0 }));

        var entries = fixture.Layout.Entries;
        entries[0] = entries[0] with { SourceKind = (ImportOccurrenceKind)255 };
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with
        {
            Entries = entries,
            Digest = ReferenceLayoutDigest(fixture.Layout.Version, entries),
        }));

        var changedAnimations = fixture.Set.Animations.ToArray();
        var changedKeys = changedAnimations[0].Curves[0].Keys;
        changedKeys[0] = changedKeys[0] with { Value = changedKeys[0].Value + 0.125f };
        var changedCurves = changedAnimations[0].Curves;
        changedCurves[0] = changedCurves[0] with { Keys = changedKeys };
        changedAnimations[0] = changedAnimations[0] with { Curves = changedCurves };
        Assert.Throws<ArgumentException>(() => fixture.Compile(
            fixture.Set with { Animations = changedAnimations }));
    }

    [Fact]
    public void RejectsDuplicateMissingAndInvalidRuntimeIdentityRangesAndMappings()
    {
        var fixture = Fixture.Create();
        var mutations = new List<Action>
        {
            () => fixture.Compile(p5a: fixture.P5a with { SyncGroups = [] }),
            () => fixture.Compile(p5a: fixture.P5a with { SyncGroups = [fixture.P5a.SyncGroups[0], fixture.P5a.SyncGroups[0]] }),
            () => fixture.Compile(p5a: fixture.P5a with { EventSemantics = fixture.P5a.EventSemantics[..^1] }),
            () => fixture.Compile(p5a: fixture.P5a with { Actions = [fixture.P5a.Actions[0] with { DefinitionId = 1 }] }),
            () => fixture.Compile(p5a: fixture.P5a with { SegmentBindings = [fixture.P5a.SegmentBindings[0] with { ActionDefinitionId = 99 }] }),
            () => fixture.Compile(p5a: fixture.P5a with { SegmentBindings = [fixture.P5a.SegmentBindings[0] with { MontageEndTime = -1f }] }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = [fixture.P5a.TimelineEntries[0] with { SegmentBindingIndex = 99 }] }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = [fixture.P5a.TimelineEntries[0] with { SourceKind = (AlsCompiledActionTimelineSourceKind)255 }] }),
            () => fixture.Compile(p5a: fixture.P5a with { DynamicTransition = fixture.P5a.DynamicTransition with { Slots = fixture.P5a.DynamicTransition.Slots[..^1] } }),
            () => fixture.Compile(p5a: fixture.P5a with { DynamicTransition = fixture.P5a.DynamicTransition with { Slots = Replace(fixture.P5a.DynamicTransition.Slots, 0, fixture.P5a.DynamicTransition.Slots[0] with { SlotIndex = 1 }) } }),
            () => fixture.Compile(p5a: fixture.P5a with { AllowTransitions = fixture.P5a.AllowTransitions with { AnimationCurveIds = fixture.P5a.AllowTransitions.AnimationCurveIds[..^1] } }),
            () => fixture.Compile(layout: fixture.Layout with { SyncMappings = fixture.Layout.SyncMappings[..^1] }),
            () => fixture.Compile(layout: fixture.Layout with { SyncMappings = Replace(fixture.Layout.SyncMappings, 0, fixture.Layout.SyncMappings[0] with { OccurrenceHandleId = 35 }) }),
            () => fixture.Compile(layout: RehashLayout(fixture.Layout, Replace(fixture.Layout.Entries, 35, fixture.Layout.Entries[35] with { OccurrenceHandleId = 999 }))),
            () => fixture.Compile(layout: RehashLayout(fixture.Layout, Replace(fixture.Layout.Entries, 35, fixture.Layout.Entries[35] with { GraphSlotIndex = 1 }))),
            () => fixture.Compile(layout: RemoveAndRehashLayoutEntry(fixture.Layout, 21)),
            () => fixture.Compile(p5a: fixture.P5a with { Actions = [fixture.P5a.Actions[0] with { MontageId = int.MaxValue }] }),
            () => fixture.Compile(pose: fixture.Pose with { FootCurves = fixture.Pose.FootCurves with
                { Bindings = Replace(fixture.Pose.FootCurves.Bindings, 0,
                    fixture.Pose.FootCurves.Bindings[0] with { AnimationId = int.MaxValue }) } }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = Replace(
                fixture.P5a.TimelineEntries, 0, fixture.P5a.TimelineEntries[0] with
                { Payload = fixture.P5a.TimelineEntries[0].Payload with
                    { Foot = (AlsCompiledTimelineFoot)255 } }) }),
        };

        foreach (var mutation in mutations)
        {
            Assert.ThrowsAny<ArgumentException>(mutation);
        }
    }

    [Fact]
    public void RejectsSkeletonMeshRootGraphClosureMasksAndNormalizedErrors()
    {
        var fixture = Fixture.Create();
        var skeleton = fixture.Set.Skeletons[fixture.Locomotion.SkeletonId];
        var badSkeletons = fixture.Set.Skeletons.ToArray();
        badSkeletons[fixture.Locomotion.SkeletonId] = skeleton with
        {
            RequiredBones = skeleton.RequiredBones with { Root = skeleton.LogicalBones.Length },
        };
        var badRootSet = RefreshDigest(fixture.Set with { Skeletons = badSkeletons });

        var badMeshes = fixture.Set.SkeletalMeshes.ToArray();
        badMeshes[fixture.Locomotion.MannequinMeshId] = badMeshes[fixture.Locomotion.MannequinMeshId] with
        {
            SkeletonId = fixture.Locomotion.SkeletonId + 1,
        };
        var badMeshSet = RefreshDigest(fixture.Set with { SkeletalMeshes = badMeshes });
        var masks = fixture.Pose.Masks.Entries;
        masks[0] = masks[0] with { BoneIds = [skeleton.RequiredBones.Root] };

        var mutations = new List<Action>
        {
            () => fixture.Compile(pose: fixture.Pose with { SkeletonId = fixture.Pose.SkeletonId + 1 }),
            () => fixture.Compile(set: badRootSet),
            () => fixture.Compile(set: badMeshSet),
            () => fixture.Compile(locomotion: fixture.Locomotion with { SkeletonId = fixture.Set.Skeletons.Length }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { AllAnimationIds = fixture.Locomotion.AllAnimationIds.Reverse().ToArray() }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { AllAnimationIds = fixture.Locomotion.AllAnimationIds[..^1] }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { AllAnimationIds = [.. fixture.Locomotion.AllAnimationIds, fixture.Locomotion.AllAnimationIds[^1]] }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { StandingSamples = Replace(fixture.Locomotion.StandingSamples, 0, fixture.Locomotion.StandingSamples[0] with { AnimationId = -1 }) }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { StandingSamples = Replace(fixture.Locomotion.StandingSamples, 0, fixture.Locomotion.StandingSamples[0] with { X = float.NaN }) }),
            () => fixture.Compile(pose: fixture.Pose with { Aim = fixture.Pose.Aim with { DownAnimationId = fixture.Pose.Aim.ForwardAnimationId } }),
            () => fixture.Compile(pose: fixture.Pose with { Turns = Replace(fixture.Pose.Turns, 0, fixture.Pose.Turns[0] with { CurveId = -1 }) }),
            () => fixture.Compile(pose: fixture.Pose with { Masks = new AlsLayerMaskProfile(masks) }),
            () => fixture.Compile(pose: fixture.Pose with { Masks = new AlsLayerMaskProfile([fixture.Pose.Masks.Entries[0], fixture.Pose.Masks.Entries[0]]) }),
        };
        foreach (var mutation in mutations)
        {
            Assert.ThrowsAny<ArgumentException>(mutation);
        }
    }

    [Fact]
    public void PayloadMappingUsesOnlyDeclaredFieldsAndPositiveZero()
    {
        var fixture = Fixture.Create();
        var snapshot = fixture.Compile();
        var runtime = snapshot.CreateCoreView();
        foreach (ref readonly var value in runtime.TimelineDefinitions)
        {
            Assert.Equal(AlsActionResultCode.None, value.Payload.TerminationReason);
            switch (value.Kind)
            {
                case AlsTimelineEventKind.Generic:
                    AssertPayloadUnused(value.Payload, enum0: true, enum1: true, enum2: true, scalar: true, flags: true);
                    break;
                case AlsTimelineEventKind.Footstep:
                case AlsTimelineEventKind.SetAction:
                case AlsTimelineEventKind.SetGroundedEntry:
                    AssertPayloadUnused(value.Payload, enum1: true, enum2: true, scalar: true, flags: true);
                    break;
                case AlsTimelineEventKind.EarlyBlendOut:
                    break;
                case AlsTimelineEventKind.RootMotionScale:
                    AssertPayloadUnused(value.Payload, enum0: true, enum1: true, enum2: true, flags: true);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException("Unknown mapped event kind.");
            }
        }
    }

    [Fact]
    public void RejectsUnusedPayloadForgeriesForEveryAuthoredEventKind()
    {
        var fixture = Fixture.Create();
        foreach (var kind in Enum.GetValues<AlsCompiledTimelineEventKind>())
        {
            var animationId = Array.FindIndex(fixture.Set.Animations,
                animation => animation.Timeline.Any(value => value.Kind == kind));
            if (animationId >= 0)
            {
                var animations = fixture.Set.Animations;
                var timeline = animations[animationId].Timeline;
                var eventIndex = Array.FindIndex(timeline, value => value.Kind == kind);
                timeline[eventIndex] = timeline[eventIndex] with
                {
                    Payload = ForgeUnusedPayload(kind, timeline[eventIndex].Payload),
                };
                animations[animationId] = animations[animationId] with { Timeline = timeline };
                Assert.Throws<ArgumentException>(() => fixture.Compile(
                    set: RefreshDigest(fixture.Set with { Animations = animations })));
                continue;
            }

            var montageId = Array.FindIndex(fixture.Set.Montages,
                montage => montage.Timeline.Any(value => value.Kind == kind));
            if (montageId < 0)
            {
                var animations = fixture.Set.Animations;
                var fallbackAnimationId = Array.FindIndex(animations, value => value.Timeline.Length > 0);
                Assert.True(fallbackAnimationId >= 0);
                var fallbackTimeline = animations[fallbackAnimationId].Timeline;
                fallbackTimeline[0] = fallbackTimeline[0] with
                {
                    Kind = kind,
                    Payload = ForgeUnusedPayload(kind, CanonicalPayload(kind)),
                };
                animations[fallbackAnimationId] = animations[fallbackAnimationId] with
                {
                    Timeline = fallbackTimeline,
                };
                Assert.Throws<ArgumentException>(() => fixture.Compile(
                    set: RefreshDigest(fixture.Set with { Animations = animations })));
                continue;
            }
            var montages = fixture.Set.Montages;
            var montageTimeline = montages[montageId].Timeline;
            var montageEventIndex = Array.FindIndex(montageTimeline, value => value.Kind == kind);
            montageTimeline[montageEventIndex] = montageTimeline[montageEventIndex] with
            {
                Payload = ForgeUnusedPayload(kind, montageTimeline[montageEventIndex].Payload),
            };
            montages[montageId] = montages[montageId] with { Timeline = montageTimeline };
            Assert.Throws<ArgumentException>(() => fixture.Compile(
                set: RefreshDigest(fixture.Set with { Montages = montages })));
        }

    }

    [Fact]
    public void AuthoritativeAnimationSetValidationRejectsGeneralTimelineAndCurveForgeries()
    {
        var fixture = Fixture.Create();
        var events = ReachableBaseEvents(fixture);
        Assert.True(events.Length >= 2);
        var forgeries = new List<AlsAnimationSetDefinition>();

        var duplicateId = MutateAnimationEvent(fixture.Set, events[1], value => value with
        {
            EventId = fixture.Set.Animations[events[0].AnimationId].Timeline[events[0].EventIndex].EventId,
        });
        forgeries.Add(duplicateId);

        var duplicateStableId = MutateAnimationEvent(fixture.Set, events[1], value => value with
        {
            StableEventId = fixture.Set.Animations[events[0].AnimationId].Timeline[events[0].EventIndex]
                .StableEventId,
        });
        forgeries.Add(duplicateStableId);

        var wrongOrdinal = MutateAnimationEvent(fixture.Set, events[0], value => value with
        {
            EventId = int.MaxValue,
        });
        forgeries.Add(wrongOrdinal);

        var wrongStableId = MutateAnimationEvent(fixture.Set, events[0], value => value with
        {
            StableEventId = new string('0', 40),
        });
        forgeries.Add(wrongStableId);

        var missingClass = MutateAnimationEvent(fixture.Set, events[0], value => value with
        {
            SourceClassPath = "",
        });
        forgeries.Add(missingClass);

        var missingDisplay = MutateAnimationEvent(fixture.Set, events[0], value => value with
        {
            DisplayName = "",
        });
        forgeries.Add(missingDisplay);

        var orderedAnimationId = AuthoredBaseAnimationIds(fixture.Locomotion).First(animationId =>
            fixture.Set.Animations[animationId].Timeline.Length >= 2);
        var animations = fixture.Set.Animations.ToArray();
        var unordered = animations[orderedAnimationId].Timeline;
        (unordered[0], unordered[1]) = (unordered[1], unordered[0]);
        animations[orderedAnimationId] = animations[orderedAnimationId] with { Timeline = unordered };
        forgeries.Add(RefreshDigest(fixture.Set with { Animations = animations }));

        animations = fixture.Set.Animations.ToArray();
        var curveAnimationId = Array.FindIndex(animations, animation => animation.Curves.Any(curve =>
            curve.SourceName is "Enable_Transition" or "FootLock_L" or "FootLock_R"));
        Assert.True(curveAnimationId >= 0);
        var curves = animations[curveAnimationId].Curves;
        var curveIndex = Array.FindIndex(curves, curve =>
            curve.SourceName is "Enable_Transition" or "FootLock_L" or "FootLock_R");
        curves[curveIndex] = curves[curveIndex] with
        {
            CanonicalKind = AlsCanonicalCurveKind.None,
            Provenance = AlsCurveProvenance.DerivedRootTrack,
        };
        animations[curveAnimationId] = animations[curveAnimationId] with { Curves = curves };
        forgeries.Add(RefreshDigest(fixture.Set with { Animations = animations }));
        Assert.All(forgeries, source => AssertWrappedSetFailure(fixture, source));
    }

    [Fact]
    public void DisabledEarlyBlendOutEnumsRemainLegal()
    {
        var fixture = Fixture.Create();
        var legalSet = MutateActionPayload(fixture, AlsCompiledTimelineEventKind.EarlyBlendOut,
            payload => payload with
            {
                CheckLocomotionMode = false,
                LocomotionMode = AlsCompiledTimelineLocomotionMode.InAir,
                CheckRotationMode = false,
                RotationMode = AlsCompiledTimelineRotationMode.Aiming,
                CheckStance = false,
                Stance = AlsCompiledTimelineStance.Crouching,
            });
        var legalP5 = CompileP5(legalSet);
        var legalLayout = AlsP5OccurrenceLayoutCompiler.Compile(fixture.Locomotion, fixture.Pose, legalP5);
        var legalRuntime = fixture.Compile(set: legalSet, p5a: legalP5, layout: legalLayout).CreateCoreView();
        var legalEvent = legalRuntime.TimelineDefinitions.ToArray().Single(value =>
            value.SourceActionId >= 0 && value.Kind == AlsTimelineEventKind.EarlyBlendOut &&
            value.Payload.EnumValue0 == (int)AlsCompiledTimelineLocomotionMode.InAir &&
            value.Payload.EnumValue1 == (int)AlsCompiledTimelineRotationMode.Aiming &&
            value.Payload.EnumValue2 == (int)AlsCompiledTimelineStance.Crouching);
        Assert.Equal((int)AlsCompiledTimelineLocomotionMode.InAir, legalEvent.Payload.EnumValue0);
        Assert.Equal((int)AlsCompiledTimelineRotationMode.Aiming, legalEvent.Payload.EnumValue1);
        Assert.Equal((int)AlsCompiledTimelineStance.Crouching, legalEvent.Payload.EnumValue2);
    }

    [Fact]
    public void ActiveNegativeTimelineScalarsReject()
    {
        var fixture = Fixture.Create();
        var negativeBlendSet = MutateGeneralPayload(fixture,
            AlsCompiledTimelineEventKind.EarlyBlendOut,
            payload => payload with { BlendOutSeconds = -0.001f });

        var negativeTranslationSet = MutateGeneralPayload(fixture,
            AlsCompiledTimelineEventKind.RootMotionScale,
            payload => payload with { TranslationScale = -0.001f });
        Assert.All(new[] { negativeBlendSet, negativeTranslationSet }, source =>
        {
            AssertWrappedSetFailure(fixture, source);
        });
    }

    [Fact]
    public void EmptyActionTimelineWithHugeLoopReturnsImmediately()
    {
        var fixture = Fixture.Create();
        var empty = BuildLoopActionSet(fixture, int.MaxValue - 1, useEmptyTimeline: true);
        var emptyP5 = CompileP5(empty);
        var stopwatch = Stopwatch.StartNew();
        _ = fixture.Compile(set: empty, p5a: emptyP5,
            layout: AlsP5OccurrenceLayoutCompiler.Compile(fixture.Locomotion, fixture.Pose, emptyP5));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public void NonemptyActionTimelineOverBudgetRejectsBeforeGrowth()
    {
        var fixture = Fixture.Create();
        var excess = BuildLoopActionSet(fixture, 1_000_001, useEmptyTimeline: false);
        var excessP5 = RebindActionP5(fixture, excess);
        var excessLayout = AlsP5OccurrenceLayoutCompiler.Compile(
            fixture.Locomotion, fixture.Pose, excessP5);
        var baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        _ = fixture.Compile();
        var baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.Throws<ArgumentException>(() => fixture.Compile(
            set: excess, p5a: excessP5, layout: excessLayout));
        Assert.Contains("budget", exception.Message, StringComparison.OrdinalIgnoreCase);
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), stopwatch.Elapsed.ToString());
        Assert.True(allocated < baselineAllocated + 2_000_000,
            $"Allocated {allocated} bytes before budget rejection; legal baseline was {baselineAllocated}.");
    }

    [Fact]
    public void ConstructibleP3P4IdsAreBoundsCheckedBeforeEveryIndex()
    {
        var fixture = Fixture.Create();
        var badProfiles = new Action[]
        {
            () => fixture.Compile(locomotion: fixture.Locomotion with { StandingIdleAnimationId = -1 }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { StandingIdleAnimationId = int.MaxValue }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { StandingSamples = Replace(
                fixture.Locomotion.StandingSamples, 0,
                fixture.Locomotion.StandingSamples[0] with { AnimationId = -1 }) }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { LeanAdditiveSamples = Replace(
                fixture.Locomotion.LeanAdditiveSamples, 0,
                fixture.Locomotion.LeanAdditiveSamples[0] with { AnimationId = int.MaxValue }) }),
            () => fixture.Compile(locomotion: fixture.Locomotion with { LeanAdditiveBasePoseAnimationId = -1 }),
            () => fixture.Compile(pose: fixture.Pose with
                { Aim = fixture.Pose.Aim with { AimOffsetId = -1 } }),
            () => fixture.Compile(pose: fixture.Pose with
                { Aim = fixture.Pose.Aim with { AimOffsetId = int.MaxValue } }),
            () => fixture.Compile(pose: fixture.Pose with
                { Aim = fixture.Pose.Aim with { DownAnimationId = -1 } }),
            () => fixture.Compile(pose: fixture.Pose with { Turns = Replace(fixture.Pose.Turns, 0,
                fixture.Pose.Turns[0] with { AnimationId = int.MaxValue }) }),
            () => fixture.Compile(pose: fixture.Pose with { Turns = Replace(fixture.Pose.Turns, 0,
                fixture.Pose.Turns[0] with { CurveId = int.MaxValue }) }),
            () => fixture.Compile(pose: fixture.Pose with { Rotates = Replace(fixture.Pose.Rotates, 0,
                fixture.Pose.Rotates[0] with { AnimationId = -1 }) }),
            () => fixture.Compile(pose: fixture.Pose with { Rotates = Replace(fixture.Pose.Rotates, 0,
                fixture.Pose.Rotates[0] with { CurveId = int.MaxValue }) }),
            () => fixture.Compile(pose: fixture.Pose with { FootCurves = fixture.Pose.FootCurves with
                { Bindings = Replace(fixture.Pose.FootCurves.Bindings, 0,
                    fixture.Pose.FootCurves.Bindings[0] with { AnimationId = -1 }) } }),
            () => fixture.Compile(pose: fixture.Pose with { FootCurves = fixture.Pose.FootCurves with
                { Bindings = Replace(fixture.Pose.FootCurves.Bindings, 0,
                    fixture.Pose.FootCurves.Bindings[0] with { LeftLockCurveId = int.MaxValue }) } }),
            () => fixture.Compile(pose: fixture.Pose with
                { Feet = fixture.Pose.Feet with { LeftLegRootBoneId = int.MaxValue } }),
        };

        Assert.All(badProfiles, action =>
        {
            var exception = Record.Exception(action);
            Assert.IsType<ArgumentException>(exception);
        });
    }

    [Fact]
    public void SignedZeroAndFirstMiddleLastRowsParticipateInOwningDigest()
    {
        var fixture = Fixture.Create();
        var baseline = fixture.Compile();

        Assert.Equal(0U, BitConverter.SingleToUInt32Bits(fixture.Locomotion.Presentation.TranslationMeters.X));
        var graphLocomotion = fixture.Locomotion with
        {
            Presentation = fixture.Locomotion.Presentation with
            {
                TranslationMeters = fixture.Locomotion.Presentation.TranslationMeters with { X = -0.0f },
            },
        };
        var graphChanged = fixture.Compile(locomotion: graphLocomotion);
        Assert.Equal(baseline.Digest, graphChanged.Digest);
        Assert.NotEqual(baseline.GraphDigest, graphChanged.GraphDigest);

        var animations = fixture.Set.Animations;
        var animationIndex = Array.FindIndex(animations, animation => animation.Curves.Any(
            curve => curve.Keys.Any(key => BitConverter.SingleToUInt32Bits(key.Value) == 0)));
        Assert.True(animationIndex >= 0);
        var curveIndex = Array.FindIndex(animations[animationIndex].Curves,
            curve => curve.Keys.Any(key => BitConverter.SingleToUInt32Bits(key.Value) == 0));
        var keyIndex = Array.FindIndex(animations[animationIndex].Curves[curveIndex].Keys,
            key => BitConverter.SingleToUInt32Bits(key.Value) == 0);
        var curves = animations[animationIndex].Curves;
        var keys = curves[curveIndex].Keys;
        keys[keyIndex] = keys[keyIndex] with { Value = -0.0f };
        curves[curveIndex] = curves[curveIndex] with { Keys = keys };
        animations[animationIndex] = animations[animationIndex] with { Curves = curves };
        var runtimeChanged = fixture.Compile(set: RefreshDigest(fixture.Set with { Animations = animations }));
        Assert.NotEqual(baseline.Digest, runtimeChanged.Digest);
        Assert.Equal(baseline.GraphDigest, runtimeChanged.GraphDigest);

        AssertWriterRowsDiffer([1, 2, 3, 4], [9, 2, 3, 4]);
        AssertWriterRowsDiffer([1, 2, 3, 4], [1, 2, 9, 4]);
        AssertWriterRowsDiffer([1, 2, 3, 4], [1, 2, 3, 9]);
        AssertWriterRowsDiffer([1, 2, 3, 4], [1, 2, 3]);
    }

    [Fact]
    public void ProductionBindingAndGraphDigestsCoverEverySpanAndScalarFamily()
    {
        var snapshot = Fixture.Create().Compile();
        var runtime = snapshot.CreateCoreView();
        var graph = snapshot.CreateGraphBuildView();
        var binding = BindingDigestInput.Capture(in runtime);
        var bindingDigest = ProductionBindingDigest(binding);
        Assert.Equal(runtime.Digest, bindingDigest);
        AssertProductionSpanMatrix(bindingDigest, binding.CurveKeys,
            value => value with { Value = value.Value + .125f },
            values => ProductionBindingDigest(binding with { CurveKeys = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.CurveBindings,
            value => value with { Required = (byte)(value.Required ^ 1) },
            values => ProductionBindingDigest(binding with { CurveBindings = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Identities,
            value => value with { AnimationId = value.AnimationId + 1 },
            values => ProductionBindingDigest(binding with { Identities = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Ranges,
            value => value with { BindingOffset = value.BindingOffset + 1 },
            values => ProductionBindingDigest(binding with { Ranges = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.AllowIndices, value => value + 1,
            values => ProductionBindingDigest(binding with { AllowIndices = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.FootBindings,
            value => value with { LeftLockDefault = value.LeftLockDefault + .125f },
            values => ProductionBindingDigest(binding with { FootBindings = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Timeline,
            value => value with { EventId = value.EventId + 1 },
            values => ProductionBindingDigest(binding with { Timeline = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Markers,
            value => value with { SourceIndex = value.SourceIndex + 1 },
            values => ProductionBindingDigest(binding with { Markers = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Members,
            value => value with { CanLead = (byte)(value.CanLead ^ 1) },
            values => ProductionBindingDigest(binding with { Members = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.SyncOccurrences,
            value => value with { OccurrenceHandleId = value.OccurrenceHandleId + 1 },
            values => ProductionBindingDigest(binding with { SyncOccurrences = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Actions,
            value => value with { Priority = value.Priority + 1 },
            values => ProductionBindingDigest(binding with { Actions = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Sections,
            value => value with { StartTime = value.StartTime + .125f },
            values => ProductionBindingDigest(binding with { Sections = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.Segments,
            value => value with { LoopCount = value.LoopCount + 1 },
            values => ProductionBindingDigest(binding with { Segments = values }));
        AssertProductionSpanMatrix(bindingDigest, binding.ActionRanges,
            value => value with { DefinitionCount = value.DefinitionCount + 1 },
            values => ProductionBindingDigest(binding with { ActionRanges = values }));

        Assert.All(new[]
        {
            ProductionBindingDigest(binding with { LayoutDigest = binding.LayoutDigest + 1 }),
            ProductionBindingDigest(binding with { Policy = binding.Policy with { MissingValue = -0.0f } }),
            ProductionBindingDigest(binding with { Policy = binding.Policy with
                { CombineMode = (GodotAls.Core.Animation.AlsP5CurveCombineMode)255 } }),
            ProductionBindingDigest(binding with { Policy = binding.Policy with { ClampMinimum = -0.0f } }),
            ProductionBindingDigest(binding with { Policy = binding.Policy with { ClampMaximum = -0.0f } }),
            ProductionBindingDigest(binding with { GroundedIk = -0.0f }),
            ProductionBindingDigest(binding with { JumpIk = -0.0f }),
            ProductionBindingDigest(binding with { FallIk = -0.0f }),
            ProductionBindingDigest(binding with { LandIk = -0.0f }),
            ProductionBindingDigest(binding with { Group = binding.Group with { GroupId = 1 } }),
            ProductionBindingDigest(binding with { Group = binding.Group with { MemberOffset = 1 } }),
            ProductionBindingDigest(binding with { Group = binding.Group with { MemberCount = binding.Group.MemberCount + 1 } }),
            ProductionBindingDigest(binding with { Group = binding.Group with { LeftMarkerNameId = 1 } }),
            ProductionBindingDigest(binding with { Group = binding.Group with { RightMarkerNameId = 2 } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { OccurrenceHandleId = binding.Transition.OccurrenceHandleId + 1 } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { AuthorityGroupId = binding.Transition.AuthorityGroupId + 1 } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { StandingLeft = binding.Transition.StandingLeft with { AnimationId = binding.Transition.StandingLeft.AnimationId + 1 } } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { StandingRight = binding.Transition.StandingRight with { AdditiveBaseAnimationId = binding.Transition.StandingRight.AdditiveBaseAnimationId + 1 } } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { CrouchingLeft = binding.Transition.CrouchingLeft with { DurationSeconds = -0.0f } } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { CrouchingRight = binding.Transition.CrouchingRight with { AnimationId = binding.Transition.CrouchingRight.AnimationId + 1 } } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { DistanceMeters = binding.Transition.DistanceMeters + .125f } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { BlendSeconds = binding.Transition.BlendSeconds + .125f } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { PlayRate = binding.Transition.PlayRate + .125f } }),
            ProductionBindingDigest(binding with { Transition = binding.Transition with { CooldownFrames = binding.Transition.CooldownFrames + 1 } }),
        }, value => Assert.NotEqual(bindingDigest, value));

        var graphInput = GraphDigestInput.Capture(in graph);
        var graphDigest = ProductionGraphDigest(graphInput);
        Assert.Equal(graph.Digest, graphDigest);
        AssertProductionSpanMatrix(graphDigest, graphInput.Standing,
            value => value with { X = value.X + .125f },
            values => ProductionGraphDigest(graphInput with { Standing = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Crouching,
            value => value with { Y = value.Y + .125f },
            values => ProductionGraphDigest(graphInput with { Crouching = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Lean,
            value => value with { RateScale = value.RateScale + .125f },
            values => ProductionGraphDigest(graphInput with { Lean = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.AllAnimations, value => value + 1,
            values => ProductionGraphDigest(graphInput with { AllAnimations = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Turns,
            value => value with { BasePlayRate = value.BasePlayRate + .125f },
            values => ProductionGraphDigest(graphInput with { Turns = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Rotates,
            value => value with { Direction = (sbyte)-value.Direction },
            values => ProductionGraphDigest(graphInput with { Rotates = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Headers,
            value => value with { BoneCount = value.BoneCount + 1 },
            values => ProductionGraphDigest(graphInput with { Headers = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.LogicalBones, value => value + 1,
            values => ProductionGraphDigest(graphInput with { LogicalBones = values }));
        AssertProductionSpanMatrix(graphDigest, graphInput.Normalized, value => value + 1,
            values => ProductionGraphDigest(graphInput with { Normalized = values }));

        Assert.All(new[]
        {
            ProductionGraphDigest(graphInput with { SkeletonId = graphInput.SkeletonId + 1 }),
            ProductionGraphDigest(graphInput with { MeshId = graphInput.MeshId + 1 }),
            ProductionGraphDigest(graphInput with { LogicalRoot = graphInput.LogicalRoot + 1 }),
            ProductionGraphDigest(graphInput with { PhysicalRoot = graphInput.PhysicalRoot + 1 }),
            ProductionGraphDigest(graphInput with { Presentation = graphInput.Presentation with
                { TranslationMeters = graphInput.Presentation.TranslationMeters with { X = -0.0f } } }),
            ProductionGraphDigest(graphInput with { Presentation = graphInput.Presentation with
                { TranslationMeters = graphInput.Presentation.TranslationMeters with { Y = -0.0f } } }),
            ProductionGraphDigest(graphInput with { Presentation = graphInput.Presentation with
                { TranslationMeters = graphInput.Presentation.TranslationMeters with { Z = -0.0f } } }),
            ProductionGraphDigest(graphInput with { Presentation = graphInput.Presentation with { YawRadians = -0.0f } }),
            ProductionGraphDigest(graphInput with { StandingIdle = graphInput.StandingIdle + 1 }),
            ProductionGraphDigest(graphInput with { CrouchingIdle = graphInput.CrouchingIdle + 1 }),
            ProductionGraphDigest(graphInput with { Jump = graphInput.Jump + 1 }),
            ProductionGraphDigest(graphInput with { Fall = graphInput.Fall + 1 }),
            ProductionGraphDigest(graphInput with { Land = graphInput.Land + 1 }),
            ProductionGraphDigest(graphInput with { LeanBase = graphInput.LeanBase + 1 }),
            ProductionGraphDigest(graphInput with { Aim = graphInput.Aim with { AimOffsetId = graphInput.Aim.AimOffsetId + 1 } }),
            ProductionGraphDigest(graphInput with { Aim = graphInput.Aim with { DownAnimationId = graphInput.Aim.DownAnimationId + 1 } }),
            ProductionGraphDigest(graphInput with { Aim = graphInput.Aim with { ForwardAnimationId = graphInput.Aim.ForwardAnimationId + 1 } }),
            ProductionGraphDigest(graphInput with { Aim = graphInput.Aim with { UpAnimationId = graphInput.Aim.UpAnimationId + 1 } }),
            ProductionGraphDigest(graphInput with { Aim = graphInput.Aim with { AdditiveBasePoseAnimationId = graphInput.Aim.AdditiveBasePoseAnimationId + 1 } }),
        }, value => Assert.NotEqual(graphDigest, value));
    }

    [Fact]
    public void RejectsLayoutsThatDifferFromTheFreshOccurrenceAllocator()
    {
        var fixture = Fixture.Create();
        var fresh = AlsP5OccurrenceLayoutCompiler.Compile(fixture.Locomotion, fixture.Pose, fixture.P5a);
        Assert.Equal(fixture.Layout.Version, fresh.Version);
        Assert.Equal(fixture.Layout.Digest, fresh.Digest);
        Assert.Equal(fixture.Layout.Entries, fresh.Entries);
        Assert.Equal(fixture.Layout.SyncMappings, fresh.SyncMappings);
        _ = fixture.Compile(layout: fresh);

        var entries = fixture.Layout.Entries;
        entries[0] = entries[0] with { AuthorityGroupId = entries[0].AuthorityGroupId + 1 };
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: RehashLayout(fixture.Layout, entries)));

        var mappings = fixture.Layout.SyncMappings;
        var wrongBase = fixture.Layout.Entries.First(value => value.SourceKind == ImportOccurrenceKind.Base &&
            value.OccurrenceHandleId != mappings[0].OccurrenceHandleId);
        mappings[0] = mappings[0] with { OccurrenceHandleId = wrongBase.OccurrenceHandleId };
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with { SyncMappings = mappings }));

        mappings = fixture.Layout.SyncMappings;
        mappings[1] = mappings[1] with { OccurrenceHandleId = mappings[0].OccurrenceHandleId };
        Assert.Throws<ArgumentException>(() => fixture.Compile(layout: fixture.Layout with { SyncMappings = mappings }));
    }

    [Fact]
    public void RejectsNoncanonicalSyncLeadershipAndLoopPolicy()
    {
        var fixture = Fixture.Create();
        var members = fixture.P5a.SyncGroups[0].Members;
        members[0] = members[0] with { LoopPolicy = AlsP5LoopPolicy.Once };
        var once = fixture.P5a with
        {
            SyncGroups = [fixture.P5a.SyncGroups[0] with { Members = members }],
        };
        Assert.Throws<ArgumentException>(() => fixture.Compile(p5a: once,
            layout: AlsP5OccurrenceLayoutCompiler.Compile(fixture.Locomotion, fixture.Pose, once)));

        members = fixture.P5a.SyncGroups[0].Members;
        members[^1] = members[^1] with { CanLead = false };
        var follower = fixture.P5a with
        {
            SyncGroups = [fixture.P5a.SyncGroups[0] with { Members = members }],
        };
        Assert.Throws<ArgumentException>(() => fixture.Compile(p5a: follower,
            layout: AlsP5OccurrenceLayoutCompiler.Compile(fixture.Locomotion, fixture.Pose, follower)));
    }

    [Fact]
    public void RejectsForgedAllowTransitionsPolicyAndPerAnimationMap()
    {
        var fixture = Fixture.Create();
        var actual = fixture.Set.Animations.Select(animation =>
        {
            var matches = animation.Curves.Where(curve => curve.SourceName == "Enable_Transition").ToArray();
            Assert.True(matches.Length <= 1);
            return matches.Length == 0 ? -1 : matches[0].CurveId;
        }).ToArray();
        Assert.Equal(actual, fixture.P5a.AllowTransitions.AnimationCurveIds);
        _ = fixture.Compile();

        var present = Array.FindIndex(actual, value => value >= 0);
        Assert.True(present >= 0);
        var missing = actual;
        missing[present] = -1;
        Assert.Throws<ArgumentException>(() => fixture.Compile(p5a: fixture.P5a with
        {
            AllowTransitions = fixture.P5a.AllowTransitions with { AnimationCurveIds = missing },
        }));

        var badPolicies = new[]
        {
            fixture.P5a.AllowTransitions with { MissingValue = 0f },
            fixture.P5a.AllowTransitions with { ClampMinimum = -1f },
            fixture.P5a.AllowTransitions with { ClampMaximum = 2f },
        };
        Assert.All(badPolicies, policy =>
            Assert.Throws<ArgumentException>(() => fixture.Compile(p5a: fixture.P5a with
            {
                AllowTransitions = policy,
            })));
    }

    [Fact]
    public void RejectsP3P4IdentityAdditiveMaskAndFootClosureForgeries()
    {
        var fixture = Fixture.Create();
        var turn = fixture.Pose.Turns[1];
        var turns = fixture.Pose.Turns;
        turns[0] = turns[0] with
        {
            Stance = turn.Stance,
            Direction = turn.Direction,
            NominalDegrees = turn.NominalDegrees,
        };
        var rotate = fixture.Pose.Rotates[1];
        var rotates = fixture.Pose.Rotates;
        rotates[0] = rotates[0] with { Stance = rotate.Stance, Direction = rotate.Direction };
        var largestMaskIndex = Array.FindIndex(fixture.Pose.Masks.Entries,
            value => value.BoneIds.Length == fixture.Pose.Masks.Entries.Max(candidate => candidate.BoneIds.Length));
        var masks = fixture.Pose.Masks.Entries;
        Assert.True(masks[largestMaskIndex].BoneIds.Length > 1);
        masks[largestMaskIndex] = masks[largestMaskIndex] with
        {
            BoneIds = masks[largestMaskIndex].BoneIds.Reverse().ToArray(),
        };
        var incompleteMasks = fixture.Pose.Masks.Entries;
        var incompleteIndex = Array.FindIndex(incompleteMasks, value => value.BoneIds.Length > 1);
        incompleteMasks[incompleteIndex] = incompleteMasks[incompleteIndex] with
        {
            BoneIds = incompleteMasks[incompleteIndex].BoneIds[..^1],
        };
        var footBindings = fixture.Pose.FootCurves.Bindings;
        footBindings[0] = footBindings[0] with { LeftLockDefault = 0.5f };

        var mutations = new Action[]
        {
            () => fixture.Compile(pose: fixture.Pose with { SchemaVersion = 0 }),
            () => fixture.Compile(pose: fixture.Pose with { IsRuntimeComplete = false }),
            () => fixture.Compile(locomotion: fixture.Locomotion with
                { StandingIdleAnimationId = fixture.Locomotion.CrouchingIdleAnimationId },
                layout: AlsP5OccurrenceLayoutCompiler.Compile(
                    fixture.Locomotion with { StandingIdleAnimationId = fixture.Locomotion.CrouchingIdleAnimationId },
                    fixture.Pose, fixture.P5a)),
            () => fixture.Compile(pose: fixture.Pose with { Turns = turns }),
            () => fixture.Compile(pose: fixture.Pose with { Rotates = rotates }),
            () => fixture.Compile(pose: fixture.Pose with
                { Aim = fixture.Pose.Aim with { DownAnimationId = fixture.Pose.Aim.UpAnimationId,
                    UpAnimationId = fixture.Pose.Aim.DownAnimationId } }),
            () => fixture.Compile(pose: fixture.Pose with { Masks = new AlsLayerMaskProfile(masks) }),
            () => fixture.Compile(pose: fixture.Pose with { Masks = new AlsLayerMaskProfile(incompleteMasks) }),
            () => fixture.Compile(pose: fixture.Pose with
                { FootCurves = fixture.Pose.FootCurves with { Bindings = fixture.Pose.FootCurves.Bindings[..^1] } }),
            () => fixture.Compile(pose: fixture.Pose with
                { FootCurves = fixture.Pose.FootCurves with { Bindings = footBindings } }),
            () => fixture.Compile(pose: fixture.Pose with
                { FootCurves = fixture.Pose.FootCurves with { GroundedIkWeight = 0.5f } }),
            () => fixture.Compile(pose: fixture.Pose with
                { FootRig = fixture.Pose.FootRig with { PelvisBoneId = fixture.Pose.FootRig.PelvisBoneId + 1 } }),
            () => fixture.Compile(pose: fixture.Pose with
                { Feet = fixture.Pose.Feet with { TraceUpMeters = fixture.Pose.Feet.TraceUpMeters + 0.01f } }),
            () => fixture.Compile(locomotion: fixture.Locomotion with
                { LeanAdditiveSamples = Replace(fixture.Locomotion.LeanAdditiveSamples, 0,
                    fixture.Locomotion.LeanAdditiveSamples[0] with
                    { X = fixture.Locomotion.LeanAdditiveSamples[0].X + 0.01f }) }),
        };
        Assert.All(mutations, mutation => Assert.Throws<ArgumentException>(mutation));

        var transitionAnimationId = fixture.P5a.DynamicTransition.Slots[0].AnimationId;
        var animations = fixture.Set.Animations;
        animations[transitionAnimationId] = animations[transitionAnimationId] with { AdditiveType = 0 };
        Assert.Throws<ArgumentException>(() => fixture.Compile(
            set: RefreshDigest(fixture.Set with { Animations = animations })));
    }

    [Fact]
    public void RejectsForgedActionRowsAndCompiledSortKey()
    {
        var fixture = Fixture.Create();
        var semantics = fixture.P5a.EventSemantics;
        semantics[0] = semantics[0] with { SemanticId = semantics[0].SemanticId + 100 };

        var timeline = fixture.P5a.TimelineEntries;
        timeline[0] = timeline[0] with { EventId = timeline[0].EventId + 100 };
        var forgedPayload = fixture.P5a.TimelineEntries;
        forgedPayload[0] = forgedPayload[0] with
        {
            Payload = ForgeUnusedPayload(forgedPayload[0].Kind, forgedPayload[0].Payload),
        };
        var forgedSource = fixture.P5a.TimelineEntries;
        forgedSource[0] = forgedSource[0] with { SourceIndex = forgedSource[0].SourceIndex + 100 };

        var actions = fixture.P5a.Actions;
        var sections = actions[0].Sections;
        sections[0] = sections[0] with { EndTime = sections[0].EndTime - 0.001f };
        actions[0] = actions[0] with { Sections = sections };

        var segments = fixture.P5a.SegmentBindings;
        segments[0] = segments[0] with { MontageEndTime = segments[0].MontageEndTime - 0.001f };

        var montageOrdinal = Array.FindIndex(fixture.P5a.TimelineEntries,
            value => value.SourceKind == AlsCompiledActionTimelineSourceKind.Montage);
        var sequenceOrdinal = Array.FindIndex(fixture.P5a.TimelineEntries,
            value => value.SourceKind == AlsCompiledActionTimelineSourceKind.Sequence);
        Assert.True(montageOrdinal >= 0 && sequenceOrdinal >= 0);
        var badMontageOrdinal = fixture.P5a.TimelineEntries;
        badMontageOrdinal[montageOrdinal] = badMontageOrdinal[montageOrdinal] with { BoundaryOrdinal = 1 };
        var badSequenceOrdinal = fixture.P5a.TimelineEntries;
        badSequenceOrdinal[sequenceOrdinal] = badSequenceOrdinal[sequenceOrdinal] with { BoundaryOrdinal = 0 };

        var unsorted = fixture.P5a.TimelineEntries;
        Assert.True(unsorted.Length >= 2);
        unsorted[0] = unsorted[0] with { EventId = 1000 };
        unsorted[1] = unsorted[0] with { EventId = 0 };

        var mutations = new Action[]
        {
            () => fixture.Compile(p5a: fixture.P5a with { EventSemantics = semantics }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = timeline }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = forgedPayload }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = forgedSource }),
            () => fixture.Compile(p5a: fixture.P5a with { Actions = actions }),
            () => fixture.Compile(p5a: fixture.P5a with { SegmentBindings = segments }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = badMontageOrdinal }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = badSequenceOrdinal }),
            () => fixture.Compile(p5a: fixture.P5a with { TimelineEntries = unsorted }),
        };
        Assert.All(mutations, mutation => Assert.Throws<ArgumentException>(mutation));
    }

    [Fact]
    public void RejectsOldP5ActionRowsAfterCurrentMontageOrSequenceIsRefreshed()
    {
        var fixture = Fixture.Create();
        var action = fixture.P5a.Actions[0];
        var montages = fixture.Set.Montages;
        var montageTimeline = montages[action.MontageId].Timeline;
        montageTimeline[0] = montageTimeline[0] with { SourceIndex = montageTimeline[0].SourceIndex + 100 };
        montages[action.MontageId] = montages[action.MontageId] with { Timeline = montageTimeline };
        Assert.Throws<ArgumentException>(() => fixture.Compile(
            set: RefreshDigest(fixture.Set with { Montages = montages })));

        var sequenceId = fixture.P5a.SegmentBindings.First(value =>
            fixture.Set.Animations[value.AnimationId].Timeline.Length > 0).AnimationId;
        var animations = fixture.Set.Animations;
        var sequenceTimeline = animations[sequenceId].Timeline;
        sequenceTimeline[0] = sequenceTimeline[0] with { TrackIndex = sequenceTimeline[0].TrackIndex + 100 };
        animations[sequenceId] = animations[sequenceId] with { Timeline = sequenceTimeline };
        Assert.Throws<ArgumentException>(() => fixture.Compile(
            set: RefreshDigest(fixture.Set with { Animations = animations })));

        montages = fixture.Set.Montages;
        var montage = montages[action.MontageId];
        var sections = montage.Sections;
        var slots = montage.Slots;
        var segments = slots[0].Segments;
        const float delta = 0.001f;
        sections[0] = sections[0] with { StartTime = sections[0].StartTime + delta };
        segments[0] = segments[0] with
        {
            StartPosition = segments[0].StartPosition + delta,
            AnimationStartTime = segments[0].AnimationStartTime + delta * segments[0].PlayRate,
        };
        slots[0] = slots[0] with { Segments = segments };
        montages[action.MontageId] = montage with { Sections = sections, Slots = slots };
        Assert.Throws<ArgumentException>(() => fixture.Compile(
            set: RefreshDigest(fixture.Set with { Montages = montages })));
    }

    [Fact]
    public void RepeatedCreateViewsAllocateZeroBytesAndKeepStableHeadersAndSpans()
    {
        var snapshot = Fixture.Create().Compile();
        for (var index = 0; index < 100; index++)
        {
            Consume(snapshot);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            Consume(snapshot);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Consume(AlsP5CoreRuntimeBindingSnapshot value)
    {
        var runtime = value.CreateCoreView();
        var occurrence = value.CreateOccurrenceLayoutView();
        var graph = value.CreateGraphBuildView();
        if (runtime.Digest != value.Digest || occurrence.Digest != value.LayoutDigest ||
            graph.Digest != value.GraphDigest || runtime.CurveBindings.IsEmpty ||
            occurrence.Entries.IsEmpty || graph.AllAnimationIds.IsEmpty)
        {
            throw new InvalidOperationException();
        }
    }

    private static int FindCurveBinding(AlsP5RuntimeBindings runtime, int animationId, int curveId)
    {
        for (var index = 0; index < runtime.CurveBindingIdentities.Length; index++)
        {
            if (runtime.CurveBindingIdentities[index] == new AlsP5CurveBindingIdentity(animationId, curveId))
            {
                return index;
            }
        }
        return -1;
    }

    private static void AssertTransitionClip(
        Fixture fixture, AlsCompiledTransitionSlot source, AlsDynamicTransitionClipBinding value) =>
        Assert.Equal(new AlsDynamicTransitionClipBinding(source.AnimationId, source.AdditiveBaseAnimationId,
            fixture.Set.Animations[source.AnimationId].PlayLength), value);

    private static int[] AuthoredBaseAnimationIds(AlsLocomotionAnimationProfile locomotion) =>
    [
        locomotion.StandingIdleAnimationId,
        .. locomotion.StandingSamples.Select(value => value.AnimationId),
        locomotion.CrouchingIdleAnimationId,
        .. locomotion.CrouchingSamples.Select(value => value.AnimationId),
        locomotion.JumpStartAnimationId,
        locomotion.FallLoopAnimationId,
        locomotion.LandAnimationId,
    ];

    private static AlsTimelineEventDefinition[] BuildAuthoredActionTimeline(
        Fixture fixture,
        AlsMontageDefinition montage,
        ReadOnlySpan<AlsActionSegmentBinding> bindings)
    {
        var result = new List<AlsTimelineEventDefinition>();
        var montageOccurrence = fixture.Layout.Entries.Single(value =>
            value.SourceKind == ImportOccurrenceKind.ActionMontage && value.SourceBindingIndex == 0);
        foreach (var value in montage.Timeline)
        {
            result.Add(ExpectedTimeline(value, montage.Id, 0, montageOccurrence.OccurrenceHandleId,
                AlsTimelineSourceKind.Montage, value.TimeSeconds, value.DurationSeconds, 0));
        }
        for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            var binding = bindings[bindingIndex];
            var animation = fixture.Set.Animations[binding.AnimationId];
            var sourceRange = (double)binding.AnimationEndTime - binding.AnimationStartTime;
            var loopDuration = sourceRange / binding.PlayRate;
            for (var iteration = 0; iteration < binding.LoopCount; iteration++)
            {
                var ordinal = iteration + 1;
                foreach (var value in animation.Timeline)
                {
                    if (value.DurationSeconds <= 0f)
                    {
                        if (value.TimeSeconds < binding.AnimationStartTime ||
                            value.TimeSeconds > binding.AnimationEndTime) continue;
                        var time = MapAuthoredTime(binding, loopDuration, iteration, value.TimeSeconds);
                        if (iteration == binding.LoopCount - 1 && value.TimeSeconds == binding.AnimationEndTime)
                            time = binding.MontageEndTime;
                        result.Add(ExpectedTimeline(value, animation.Id, 0,
                            binding.OccurrenceHandleId, AlsTimelineSourceKind.MontageSegmentAnimation,
                            time, 0f, ordinal));
                        continue;
                    }
                    var begin = Math.Max((double)value.TimeSeconds, binding.AnimationStartTime);
                    var end = Math.Min((double)value.TimeSeconds + value.DurationSeconds,
                        binding.AnimationEndTime);
                    if (end <= begin) continue;
                    var mappedBegin = MapAuthoredTime(binding, loopDuration, iteration, begin);
                    var mappedEnd = MapAuthoredTime(binding, loopDuration, iteration, end);
                    if (iteration == binding.LoopCount - 1 && end == binding.AnimationEndTime)
                        mappedEnd = binding.MontageEndTime;
                    result.Add(ExpectedTimeline(value, animation.Id, 0,
                        binding.OccurrenceHandleId, AlsTimelineSourceKind.MontageSegmentAnimation,
                        mappedBegin, mappedEnd - mappedBegin, ordinal));
                }
            }
        }
        return result.OrderBy(value => value.SourceActionId)
            .ThenBy(value => value.TimeSeconds)
            .ThenBy(value => value.SourceKind)
            .ThenBy(value => value.SourceKind == AlsTimelineSourceKind.Montage ? -1 :
                fixture.Layout.Entries.Single(entry =>
                    entry.SourceKind == ImportOccurrenceKind.ActionSequence &&
                    entry.OccurrenceHandleId == value.RequiredOccurrenceHandleId).SourceBindingIndex)
            .ThenBy(value => value.EventId)
            .ThenBy(value => value.SourceIndex)
            .ThenBy(value => value.BoundaryOrdinal)
            .ThenBy(value => value.TrackIndex)
            .ToArray();
    }

    private static float MapAuthoredTime(
        AlsActionSegmentBinding binding,
        double loopDuration,
        int iteration,
        double sourceTime) => (float)(binding.MontageStartTime + iteration * loopDuration +
            (sourceTime - binding.AnimationStartTime) / binding.PlayRate);

    private static AlsTimelineEventDefinition ExpectedTimeline(
        AlsCompiledTimelineEventDefinition source,
        int sourceAnimationId,
        int sourceActionId,
        int occurrenceHandleId,
        AlsTimelineSourceKind sourceKind,
        float time,
        float duration,
        int ordinal) => new(source.EventId, sourceAnimationId, sourceActionId,
            occurrenceHandleId, sourceKind, source.SourceIndex, source.TrackIndex, ordinal,
            time, duration, source.TriggerWeightThreshold, (AlsTimelineEventKind)(byte)source.Kind,
            (AlsTimelineTickMode)(byte)source.TickMode, ExpectedPayload(source.Kind, source.Payload));

    private static AlsCompactEventPayload ExpectedPayload(
        AlsCompiledTimelineEventKind kind,
        AlsCompiledTimelinePayloadDefinition value)
    {
        var semanticId = (int)kind;
        return kind switch
        {
            AlsCompiledTimelineEventKind.Generic =>
                new AlsCompactEventPayload(semanticId, 0, 0, 0, 0f, 0, AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.Footstep =>
                new AlsCompactEventPayload(semanticId, (int)value.Foot, 0, 0, 0f, 0, AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.SetAction =>
                new AlsCompactEventPayload(semanticId, (int)value.Action, 0, 0, 0f, 0, AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.SetGroundedEntry =>
                new AlsCompactEventPayload(semanticId, (int)value.GroundedEntryMode, 0, 0, 0f, 0,
                    AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.EarlyBlendOut =>
                new AlsCompactEventPayload(semanticId, (int)value.LocomotionMode,
                    (int)value.RotationMode, (int)value.Stance, value.BlendOutSeconds,
                    (ushort)((value.CheckInput ? 1 : 0) | (value.CheckLocomotionMode ? 2 : 0) |
                        (value.CheckRotationMode ? 4 : 0) | (value.CheckStance ? 8 : 0)),
                    AlsActionResultCode.None),
            AlsCompiledTimelineEventKind.RootMotionScale =>
                new AlsCompactEventPayload(semanticId, 0, 0, 0, value.TranslationScale, 0,
                    AlsActionResultCode.None),
            _ => throw new Xunit.Sdk.XunitException("Unknown authored payload kind."),
        };
    }

    private static void AssertSamples(
        AlsLocomotionAnimationSample[] source, ReadOnlySpan<AlsP5GraphSample> mapped)
    {
        Assert.Equal(source.Length, mapped.Length);
        for (var index = 0; index < mapped.Length; index++)
        {
            Assert.Equal(new AlsP5GraphSample(source[index].AnimationId, source[index].X,
                source[index].Y, source[index].RateScale), mapped[index]);
        }
    }

    private static void AssertPayloadUnused(
        AlsCompactEventPayload payload, bool enum0 = false, bool enum1 = false, bool enum2 = false,
        bool scalar = false, bool flags = false)
    {
        if (enum0) Assert.Equal(0, payload.EnumValue0);
        if (enum1) Assert.Equal(0, payload.EnumValue1);
        if (enum2) Assert.Equal(0, payload.EnumValue2);
        if (scalar) Assert.Equal(0U, BitConverter.SingleToUInt32Bits(payload.ScalarValue0));
        if (flags) Assert.Equal((ushort)0, payload.Flags);
    }

    private static string CaptureRuntime(in AlsP5RuntimeBindings value) =>
        $"{value.Digest:x16}:{value.CurveKeys.Length}:{value.CurveBindings.Length}:" +
        $"{value.TimelineDefinitions.Length}:{value.SyncMarkers.Length}:" +
        $"{value.ActionDefinitions.Length}:{value.ActionSegments.Length}:" +
        $"{value.CurveKeys[0].Value:R}:{value.TimelineDefinitions[0].BoundaryOrdinal}";

    private static T[] Replace<T>(T[] source, int index, T value)
    {
        var result = source.ToArray();
        result[index] = value;
        return result;
    }

    private static AlsCompiledTimelinePayloadDefinition ForgeUnusedPayload(
        AlsCompiledTimelineEventKind kind,
        AlsCompiledTimelinePayloadDefinition source) => kind == AlsCompiledTimelineEventKind.Footstep
        ? source with { Action = AlsCompiledTimelineAction.Rolling }
        : source with { Foot = AlsCompiledTimelineFoot.Left };

    private static AlsCompiledTimelinePayloadDefinition CanonicalPayload(
        AlsCompiledTimelineEventKind kind) => kind switch
        {
            AlsCompiledTimelineEventKind.Footstep => new AlsCompiledTimelinePayloadDefinition(
                AlsCompiledTimelineFoot.Left, AlsCompiledTimelineAction.None,
                AlsCompiledTimelineGroundedEntryMode.None, 0f, false, false,
                AlsCompiledTimelineLocomotionMode.Grounded, false,
                AlsCompiledTimelineRotationMode.VelocityDirection, false,
                AlsCompiledTimelineStance.Standing, 0f),
            AlsCompiledTimelineEventKind.SetAction => new AlsCompiledTimelinePayloadDefinition(
                AlsCompiledTimelineFoot.Unspecified, AlsCompiledTimelineAction.Rolling,
                AlsCompiledTimelineGroundedEntryMode.None, 0f, false, false,
                AlsCompiledTimelineLocomotionMode.Grounded, false,
                AlsCompiledTimelineRotationMode.VelocityDirection, false,
                AlsCompiledTimelineStance.Standing, 0f),
            AlsCompiledTimelineEventKind.SetGroundedEntry => new AlsCompiledTimelinePayloadDefinition(
                AlsCompiledTimelineFoot.Unspecified, AlsCompiledTimelineAction.None,
                AlsCompiledTimelineGroundedEntryMode.FromRoll, 0f, false, false,
                AlsCompiledTimelineLocomotionMode.Grounded, false,
                AlsCompiledTimelineRotationMode.VelocityDirection, false,
                AlsCompiledTimelineStance.Standing, 0f),
            AlsCompiledTimelineEventKind.EarlyBlendOut => new AlsCompiledTimelinePayloadDefinition(
                AlsCompiledTimelineFoot.Unspecified, AlsCompiledTimelineAction.None,
                AlsCompiledTimelineGroundedEntryMode.None, .1f, false, false,
                AlsCompiledTimelineLocomotionMode.Grounded, false,
                AlsCompiledTimelineRotationMode.VelocityDirection, false,
                AlsCompiledTimelineStance.Standing, 0f),
            AlsCompiledTimelineEventKind.RootMotionScale => new AlsCompiledTimelinePayloadDefinition(
                AlsCompiledTimelineFoot.Unspecified, AlsCompiledTimelineAction.None,
                AlsCompiledTimelineGroundedEntryMode.None, 0f, false, false,
                AlsCompiledTimelineLocomotionMode.Grounded, false,
                AlsCompiledTimelineRotationMode.VelocityDirection, false,
                AlsCompiledTimelineStance.Standing, 1f),
            _ => default,
        };

    private static AlsP5OccurrenceLayout RehashLayout(
        AlsP5OccurrenceLayout source, ImportOccurrenceEntry[] entries) => source with
    {
        Entries = entries,
        Digest = ReferenceLayoutDigest(source.Version, entries),
    };

    private static AlsP5OccurrenceLayout RemoveAndRehashLayoutEntry(
        AlsP5OccurrenceLayout source, int removeIndex)
    {
        var entries = source.Entries.Where((_, index) => index != removeIndex)
            .Select((value, index) => value with { OccurrenceHandleId = index }).ToArray();
        var mappings = source.SyncMappings.Select(value => value.OccurrenceHandleId > removeIndex
            ? value with { OccurrenceHandleId = value.OccurrenceHandleId - 1 }
            : value).ToArray();
        return source with
        {
            Entries = entries,
            SyncMappings = mappings,
            Digest = ReferenceLayoutDigest(source.Version, entries),
        };
    }

    private static void AssertWriterRowsDiffer(int[] left, int[] right)
    {
        var leftWriter = new ReferenceFnvWriter();
        leftWriter.Add<int>(left, static (ref ReferenceFnvWriter writer, in int value) => writer.Add(value));
        var rightWriter = new ReferenceFnvWriter();
        rightWriter.Add<int>(right, static (ref ReferenceFnvWriter writer, in int value) => writer.Add(value));
        Assert.NotEqual(leftWriter.Value, rightWriter.Value);
    }

    private static AlsAnimationSetDefinition RefreshDigest(AlsAnimationSetDefinition source) =>
        source with { DefinitionDigest = AlsAnimationSetPayload.ComputeDefinitionDigest(source) };

    private readonly record struct EventLocation(int AnimationId, int EventIndex);

    private static EventLocation[] ReachableBaseEvents(Fixture fixture) =>
        AuthoredBaseAnimationIds(fixture.Locomotion).Distinct()
            .SelectMany(animationId => fixture.Set.Animations[animationId].Timeline
                .Select((_, eventIndex) => new EventLocation(animationId, eventIndex)))
            .ToArray();

    private static AlsAnimationSetDefinition MutateAnimationEvent(
        AlsAnimationSetDefinition source,
        EventLocation location,
        Func<AlsCompiledTimelineEventDefinition, AlsCompiledTimelineEventDefinition> mutate)
    {
        var animations = source.Animations.ToArray();
        var timeline = animations[location.AnimationId].Timeline;
        timeline[location.EventIndex] = mutate(timeline[location.EventIndex]);
        animations[location.AnimationId] = animations[location.AnimationId] with { Timeline = timeline };
        return RefreshDigest(source with { Animations = animations });
    }

    private static void AssertWrappedSetFailure(Fixture fixture, AlsAnimationSetDefinition source)
    {
        Assert.Equal(AlsAnimationSetPayload.ComputeDefinitionDigest(source), source.DefinitionDigest);
        var exception = Assert.Throws<ArgumentException>(() => fixture.Compile(set: source));
        Assert.Equal("animationSet", exception.ParamName);
        Assert.True(exception.InnerException is InvalidDataException, exception.ToString());
    }

    private static AlsAnimationSetDefinition MutateActionPayload(
        Fixture fixture,
        AlsCompiledTimelineEventKind kind,
        Func<AlsCompiledTimelinePayloadDefinition, AlsCompiledTimelinePayloadDefinition> mutate)
    {
        var compiled = fixture.P5a.TimelineEntries.FirstOrDefault(value => value.Kind == kind);
        var replaceKind = compiled.Kind != kind;
        if (replaceKind)
        {
            compiled = fixture.P5a.TimelineEntries[0];
        }
        if (compiled.SourceKind == AlsCompiledActionTimelineSourceKind.Montage)
        {
            var montages = fixture.Set.Montages.ToArray();
            var timeline = montages[compiled.SourceAssetId].Timeline;
            var eventIndex = Array.FindIndex(timeline, value => value.EventId == compiled.EventId);
            Assert.True(eventIndex >= 0);
            timeline[eventIndex] = timeline[eventIndex] with
            {
                Kind = kind,
                Payload = mutate(replaceKind ? CanonicalPayload(kind) : timeline[eventIndex].Payload),
            };
            montages[compiled.SourceAssetId] = montages[compiled.SourceAssetId] with { Timeline = timeline };
            return RefreshDigest(fixture.Set with { Montages = montages });
        }

        Assert.Equal(AlsCompiledActionTimelineSourceKind.Sequence, compiled.SourceKind);
        var animations = fixture.Set.Animations.ToArray();
        var animationTimeline = animations[compiled.SourceAssetId].Timeline;
        var animationEventIndex = Array.FindIndex(animationTimeline, value => value.EventId == compiled.EventId);
        Assert.True(animationEventIndex >= 0);
        animationTimeline[animationEventIndex] = animationTimeline[animationEventIndex] with
        {
            Kind = kind,
            Payload = mutate(replaceKind ? CanonicalPayload(kind) : animationTimeline[animationEventIndex].Payload),
        };
        animations[compiled.SourceAssetId] = animations[compiled.SourceAssetId] with
        {
            Timeline = animationTimeline,
        };
        return RefreshDigest(fixture.Set with { Animations = animations });
    }

    private static AlsAnimationSetDefinition MutateGeneralPayload(
        Fixture fixture,
        AlsCompiledTimelineEventKind kind,
        Func<AlsCompiledTimelinePayloadDefinition, AlsCompiledTimelinePayloadDefinition> mutate)
    {
        var location = ReachableBaseEvents(fixture)[0];
        return MutateAnimationEvent(fixture.Set, location, value => value with
        {
            Kind = kind,
            Payload = mutate(CanonicalPayload(kind)),
        });
    }

    private static AlsP5aAnimationRuntimeProfile CompileP5(AlsAnimationSetDefinition source) =>
        AlsP5aAnimationRuntimeProfileCompiler.Compile(File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(), "assets", "config", "p5a_animation_runtime.json")), source);

    private static AlsAnimationSetDefinition BuildLoopActionSet(
        Fixture fixture, int loopCount, bool useEmptyTimeline)
    {
        var action = fixture.P5a.Actions[0];
        var montages = fixture.Set.Montages.ToArray();
        var montage = montages[action.MontageId];
        var slots = montage.Slots;
        var slotIndex = Array.FindIndex(slots, value => value.SlotId == action.SlotId);
        Assert.True(slotIndex >= 0);
        var segments = slots[slotIndex].Segments;
        Assert.NotEmpty(segments);
        var animationId = Array.FindIndex(fixture.Set.Animations, animation =>
            animation.SkeletonId == fixture.Locomotion.SkeletonId &&
            animation.PlayLength > 0f && animation.AdditiveType == 0 &&
            (useEmptyTimeline
                ? animation.Timeline.Length == 0
                : animation.Timeline.Length == 1 && animation.Timeline[0].TimeSeconds == 0f));
        if (animationId < 0 && !useEmptyTimeline)
        {
            animationId = Array.FindIndex(fixture.Set.Animations, animation =>
                animation.SkeletonId == fixture.Locomotion.SkeletonId &&
                animation.PlayLength > 0f && animation.AdditiveType == 0 &&
                animation.Timeline.Any(value => value.TimeSeconds == 0f));
        }
        Assert.True(animationId >= 0);
        var animation = fixture.Set.Animations[animationId];

        var animationRange = (float)(montage.PlayLength * segments[0].PlayRate / loopCount);
        var mappedDuration = loopCount * (double)animationRange / segments[0].PlayRate;
        var mappedLength = (float)mappedDuration;
        for (var attempt = 0;
             mappedLength < montage.PlayLength || Math.Abs(mappedLength - mappedDuration) > 1e-8;
             attempt++)
        {
            Assert.True(attempt < 10_000, "Unable to construct an exact legal loop mapping.");
            animationRange = MathF.BitIncrement(animationRange);
            mappedDuration = loopCount * (double)animationRange / segments[0].PlayRate;
            mappedLength = (float)mappedDuration;
        }
        Assert.InRange(animationRange, float.Epsilon, animation.PlayLength);
        segments[0] = segments[0] with
        {
            AnimationId = animationId,
            StartPosition = 0f,
            AnimationStartTime = 0f,
            AnimationEndTime = animationRange,
            LoopCount = loopCount,
        };
        slots[slotIndex] = slots[slotIndex] with { Segments = segments };
        montages[action.MontageId] = montage with { Slots = slots, PlayLength = mappedLength };
        return RefreshDigest(fixture.Set with { Montages = montages });
    }

    private static AlsP5aAnimationRuntimeProfile RebindActionP5(
        Fixture fixture, AlsAnimationSetDefinition source)
    {
        var actions = fixture.P5a.Actions;
        var actionIndex = 0;
        var action = actions[actionIndex];
        var montage = source.Montages[action.MontageId];
        var sections = montage.Sections.Select((value, index) => new AlsCompiledActionSectionBinding(
            action.DefinitionId,
            value.SectionId,
            value.NextSectionId,
            value.StartTime,
            index + 1 < montage.Sections.Length ? montage.Sections[index + 1].StartTime : montage.PlayLength))
            .ToArray();
        actions[actionIndex] = action with
        {
            MontageDurationSeconds = montage.PlayLength,
            Sections = sections,
        };

        var segmentBindings = fixture.P5a.SegmentBindings;
        for (var index = 0; index < segmentBindings.Length; index++)
        {
            var binding = segmentBindings[index];
            if (binding.ActionDefinitionId != action.DefinitionId)
            {
                continue;
            }
            var slot = montage.Slots.Single(value => value.SlotId == binding.SlotId);
            var segment = slot.Segments.Single(value => value.SegmentId == binding.SegmentId);
            var montageEnd = segment.StartPosition +
                (float)(segment.LoopCount * ((double)segment.AnimationEndTime - segment.AnimationStartTime) /
                    segment.PlayRate);
            segmentBindings[index] = binding with
            {
                AnimationId = segment.AnimationId,
                MontageStartTime = segment.StartPosition,
                MontageEndTime = montageEnd,
                AnimationStartTime = segment.AnimationStartTime,
                AnimationEndTime = segment.AnimationEndTime,
                PlayRate = segment.PlayRate,
                LoopCount = segment.LoopCount,
            };
        }
        var timeline = fixture.P5a.TimelineEntries.Where(value =>
            value.SegmentBindingIndex < 0 ||
            value.SegmentBindingIndex >= segmentBindings.Length ||
            segmentBindings[value.SegmentBindingIndex].ActionDefinitionId != action.DefinitionId)
            .ToArray();
        return fixture.P5a with
        {
            Actions = actions,
            SegmentBindings = segmentBindings,
            TimelineEntries = timeline,
        };
    }

    private static void AssertProductionSpanMatrix<T>(
        ulong baseline, T[] values, Func<T, T> mutate, Func<T[], ulong> digest)
    {
        Assert.NotEmpty(values);
        var matrix = values.Length >= 3 ? values : [values[0], values[0], values[0]];
        var matrixBaseline = digest(matrix);
        if (values.Length >= 3)
        {
            Assert.Equal(baseline, matrixBaseline);
        }
        foreach (var index in new[] { 0, matrix.Length / 2, matrix.Length - 1 })
        {
            Assert.NotEqual(matrixBaseline, digest(Replace(matrix, index, mutate(matrix[index]))));
        }
        Assert.NotEqual(matrixBaseline, digest([.. matrix, matrix[^1]]));
        Assert.NotEqual(matrixBaseline, digest(matrix[..^1]));
    }

    private sealed record BindingDigestInput(
        ulong LayoutDigest,
        AlsCurveKey[] CurveKeys,
        AlsCurveBinding[] CurveBindings,
        AlsP5CurveBindingIdentity[] Identities,
        AlsAnimationCurveRange[] Ranges,
        AlsP5CurveSemanticPolicy Policy,
        int[] AllowIndices,
        AlsP4FootCurveRuntimeBinding[] FootBindings,
        float GroundedIk,
        float JumpIk,
        float FallIk,
        float LandIk,
        AlsTimelineEventDefinition[] Timeline,
        AlsSyncMarkerDefinition[] Markers,
        AlsSyncGroupBinding Group,
        AlsSyncMemberBinding[] Members,
        AlsP5SyncOccurrenceBinding[] SyncOccurrences,
        AlsDynamicTransitionBinding Transition,
        AlsActionDefinition[] Actions,
        AlsActionSectionBinding[] Sections,
        AlsActionSegmentBinding[] Segments,
        AlsActionTimelineRange[] ActionRanges)
    {
        public static BindingDigestInput Capture(in AlsP5RuntimeBindings value) => new(
            value.LayoutDigest,
            value.CurveKeys.ToArray(),
            value.CurveBindings.ToArray(),
            value.CurveBindingIdentities.ToArray(),
            value.AnimationCurveRanges.ToArray(),
            value.AllowTransitionsPolicy,
            value.AllowTransitionsBindingIndices.ToArray(),
            value.FootCurveBindings.ToArray(),
            value.GroundedIkWeight,
            value.JumpStartIkWeight,
            value.FallLoopIkWeight,
            value.LandRecoveryIkWeight,
            value.TimelineDefinitions.ToArray(),
            value.SyncMarkers.ToArray(),
            value.SyncGroup,
            value.SyncMembers.ToArray(),
            value.SyncOccurrences.ToArray(),
            value.DynamicTransition,
            value.ActionDefinitions.ToArray(),
            value.ActionSections.ToArray(),
            value.ActionSegments.ToArray(),
            value.ActionTimelineRanges.ToArray());
    }

    private static ulong ProductionBindingDigest(BindingDigestInput value)
        => AlsP5CoreRuntimeBindingCompiler.ComputeBindingDigest(
            value.LayoutDigest, value.CurveKeys, value.CurveBindings, value.Identities, value.Ranges,
            value.Policy, value.AllowIndices, value.FootBindings, value.GroundedIk, value.JumpIk,
            value.FallIk, value.LandIk, value.Timeline, value.Markers, value.Group, value.Members,
            value.SyncOccurrences, value.Transition, value.Actions, value.Sections, value.Segments,
            value.ActionRanges);

    private sealed record GraphDigestInput(
        int SkeletonId,
        int MeshId,
        int LogicalRoot,
        int PhysicalRoot,
        AlsPresentationDefinition Presentation,
        int StandingIdle,
        int CrouchingIdle,
        int Jump,
        int Fall,
        int Land,
        int LeanBase,
        AlsP5GraphSample[] Standing,
        AlsP5GraphSample[] Crouching,
        AlsP5GraphSample[] Lean,
        int[] AllAnimations,
        AlsAimProfile Aim,
        AlsTurnProfile[] Turns,
        AlsRotateProfile[] Rotates,
        AlsP5GraphMaskHeader[] Headers,
        int[] LogicalBones,
        int[] Normalized)
    {
        public static GraphDigestInput Capture(in AlsP5GraphBuildView value) => new(
            value.SkeletonId,
            value.MannequinMeshId,
            value.RootMotionExtractionLogicalBoneId,
            value.RootMotionExtractionPhysicalBoneId,
            value.Presentation,
            value.StandingIdleAnimationId,
            value.CrouchingIdleAnimationId,
            value.JumpStartAnimationId,
            value.FallLoopAnimationId,
            value.LandAnimationId,
            value.LeanAdditiveBaseAnimationId,
            value.StandingSamples.ToArray(),
            value.CrouchingSamples.ToArray(),
            value.LeanSamples.ToArray(),
            value.AllAnimationIds.ToArray(),
            value.Aim,
            value.Turns.ToArray(),
            value.Rotates.ToArray(),
            value.MaskHeaders.ToArray(),
            value.LogicalBoneIds.ToArray(),
            value.NormalizedAnimationIds.ToArray());
    }

    private static ulong ProductionGraphDigest(GraphDigestInput value)
        => AlsP5CoreRuntimeBindingCompiler.ComputeGraphDigest(
            value.SkeletonId, value.MeshId, value.LogicalRoot, value.PhysicalRoot, value.Presentation,
            value.StandingIdle, value.CrouchingIdle, value.Jump, value.Fall, value.Land,
            value.LeanBase, value.Standing, value.Crouching, value.Lean, value.AllAnimations,
            value.Aim, value.Turns, value.Rotates, value.Headers, value.LogicalBones, value.Normalized);

    private static ulong ReferenceLayoutDigest(int version, ReadOnlySpan<ImportOccurrenceEntry> entries)
    {
        var writer = new ReferenceFnvWriter();
        writer.Add(version);
        writer.Add(entries.Length);
        foreach (ref readonly var entry in entries)
        {
            writer.Add((byte)entry.SourceKind);
            writer.Add(entry.SourceBindingIndex);
            writer.Add(entry.GraphSlotIndex);
            writer.Add(entry.OccurrenceHandleId);
            writer.Add(entry.AuthorityGroupId);
        }
        return writer.Value;
    }

    private static ulong ReferenceLayoutDigest(int version, ReadOnlySpan<CoreOccurrenceEntry> entries)
    {
        var writer = new ReferenceFnvWriter();
        writer.Add(version);
        writer.Add(entries.Length);
        foreach (ref readonly var entry in entries)
        {
            writer.Add((byte)entry.SourceKind);
            writer.Add(entry.SourceBindingIndex);
            writer.Add(entry.GraphSlotIndex);
            writer.Add(entry.OccurrenceHandleId);
            writer.Add(entry.AuthorityGroupId);
        }
        return writer.Value;
    }

    private static ulong ReferenceBindingDigest(in AlsP5RuntimeBindings value)
    {
        var writer = new ReferenceFnvWriter();
        writer.Add(value.Version);
        writer.Add(value.LayoutDigest);
        writer.Add(value.CurveKeys, static (ref ReferenceFnvWriter w, in AlsCurveKey x) =>
        {
            w.Add(x.TimeSeconds); w.Add(x.Value); w.Add(x.ArriveTangent); w.Add(x.LeaveTangent);
            w.Add((byte)x.Interpolation);
        });
        writer.Add(value.CurveBindings, static (ref ReferenceFnvWriter w, in AlsCurveBinding x) =>
        {
            w.Add(x.CurveId); w.Add(x.KeyOffset); w.Add(x.KeyCount); w.Add(x.DurationSeconds);
            w.Add(x.Required); w.Add(x.Loop);
        });
        writer.Add(value.CurveBindingIdentities, static (ref ReferenceFnvWriter w, in AlsP5CurveBindingIdentity x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); });
        writer.Add(value.AnimationCurveRanges, static (ref ReferenceFnvWriter w, in AlsAnimationCurveRange x) =>
        { w.Add(x.AnimationId); w.Add(x.BindingOffset); w.Add(x.BindingCount); });
        writer.Add(value.AllowTransitionsPolicy.MissingValue);
        writer.Add((byte)value.AllowTransitionsPolicy.CombineMode);
        writer.Add(value.AllowTransitionsPolicy.ClampMinimum);
        writer.Add(value.AllowTransitionsPolicy.ClampMaximum);
        writer.Add(value.AllowTransitionsBindingIndices, static (ref ReferenceFnvWriter w, in int x) => w.Add(x));
        writer.Add(value.FootCurveBindings, static (ref ReferenceFnvWriter w, in AlsP4FootCurveRuntimeBinding x) =>
        { w.Add(x.AnimationId); w.Add(x.LeftLockCurveId); w.Add(x.RightLockCurveId); w.Add(x.LeftLockDefault); w.Add(x.RightLockDefault); });
        writer.Add(value.GroundedIkWeight); writer.Add(value.JumpStartIkWeight);
        writer.Add(value.FallLoopIkWeight); writer.Add(value.LandRecoveryIkWeight);
        writer.Add(value.TimelineDefinitions, WriteTimeline);
        writer.Add(value.SyncMarkers, static (ref ReferenceFnvWriter w, in AlsSyncMarkerDefinition x) =>
        { w.Add(x.MarkerId); w.Add(x.MarkerNameId); w.Add(x.AnimationId); w.Add(x.SourceIndex); w.Add(x.TrackIndex); w.Add(x.TimeSeconds); });
        WriteSyncGroup(ref writer, value.SyncGroup);
        writer.Add(value.SyncMembers, static (ref ReferenceFnvWriter w, in AlsSyncMemberBinding x) =>
        { w.Add(x.GroupId); w.Add(x.AnimationId); w.Add(x.DurationSeconds); w.Add(x.Loop); w.Add(x.CanLead); });
        writer.Add(value.SyncOccurrences, static (ref ReferenceFnvWriter w, in AlsP5SyncOccurrenceBinding x) =>
        { w.Add(x.GroupId); w.Add(x.GroupMemberIndex); w.Add(x.AnimationId); w.Add(x.OccurrenceHandleId); });
        WriteTransition(ref writer, value.DynamicTransition);
        writer.Add(value.ActionDefinitions, WriteAction);
        writer.Add(value.ActionSections, static (ref ReferenceFnvWriter w, in AlsActionSectionBinding x) =>
        { w.Add(x.ActionDefinitionId); w.Add(x.SectionId); w.Add(x.NextSectionId); w.Add(x.StartTime); w.Add(x.EndTime); });
        writer.Add(value.ActionSegments, static (ref ReferenceFnvWriter w, in AlsActionSegmentBinding x) =>
        { w.Add(x.OccurrenceHandleId); w.Add(x.ActionDefinitionId); w.Add(x.SlotId); w.Add(x.SegmentId); w.Add(x.AnimationId); w.Add(x.MontageStartTime); w.Add(x.MontageEndTime); w.Add(x.AnimationStartTime); w.Add(x.AnimationEndTime); w.Add(x.PlayRate); w.Add(x.LoopCount); });
        writer.Add(value.ActionTimelineRanges, static (ref ReferenceFnvWriter w, in AlsActionTimelineRange x) =>
        { w.Add(x.ActionDefinitionId); w.Add(x.DefinitionOffset); w.Add(x.DefinitionCount); });
        return writer.Value;
    }

    private static ulong ReferenceGraphDigest(in AlsP5GraphBuildView value)
    {
        var writer = new ReferenceFnvWriter();
        writer.Add(value.Version);
        writer.Add(value.SkeletonId); writer.Add(value.MannequinMeshId);
        writer.Add(value.RootMotionExtractionLogicalBoneId);
        writer.Add(value.RootMotionExtractionPhysicalBoneId);
        writer.Add(value.Presentation.TranslationMeters.X);
        writer.Add(value.Presentation.TranslationMeters.Y);
        writer.Add(value.Presentation.TranslationMeters.Z);
        writer.Add(value.Presentation.YawRadians);
        writer.Add(value.StandingIdleAnimationId); writer.Add(value.CrouchingIdleAnimationId);
        writer.Add(value.JumpStartAnimationId); writer.Add(value.FallLoopAnimationId);
        writer.Add(value.LandAnimationId); writer.Add(value.LeanAdditiveBaseAnimationId);
        writer.Add(value.StandingSamples, WriteGraphSample);
        writer.Add(value.CrouchingSamples, WriteGraphSample);
        writer.Add(value.LeanSamples, WriteGraphSample);
        writer.Add(value.AllAnimationIds, static (ref ReferenceFnvWriter w, in int x) => w.Add(x));
        writer.Add(value.Aim.AimOffsetId); writer.Add(value.Aim.DownAnimationId);
        writer.Add(value.Aim.ForwardAnimationId); writer.Add(value.Aim.UpAnimationId);
        writer.Add(value.Aim.AdditiveBasePoseAnimationId);
        writer.Add(value.Turns, static (ref ReferenceFnvWriter w, in AlsTurnProfile x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); w.Add((byte)x.Stance); w.Add(x.Direction); w.Add(x.NominalDegrees); w.Add(x.BasePlayRate); w.Add(x.BlendSeconds); w.Add(x.ScaleAngle); });
        writer.Add(value.Rotates, static (ref ReferenceFnvWriter w, in AlsRotateProfile x) =>
        { w.Add(x.AnimationId); w.Add(x.CurveId); w.Add((byte)x.Stance); w.Add(x.Direction); });
        writer.Add(value.MaskHeaders, static (ref ReferenceFnvWriter w, in AlsP5GraphMaskHeader x) =>
        { w.Add((byte)x.Kind); w.Add(x.LogicalRootBoneId); w.Add(x.BoneOffset); w.Add(x.BoneCount); });
        writer.Add(value.LogicalBoneIds, static (ref ReferenceFnvWriter w, in int x) => w.Add(x));
        writer.Add(value.NormalizedAnimationIds, static (ref ReferenceFnvWriter w, in int x) => w.Add(x));
        return writer.Value;
    }

    private static void WriteTimeline(ref ReferenceFnvWriter w, in AlsTimelineEventDefinition x)
    {
        w.Add(x.EventId); w.Add(x.SourceAnimationId); w.Add(x.SourceActionId);
        w.Add(x.RequiredOccurrenceHandleId); w.Add((byte)x.SourceKind); w.Add(x.SourceIndex);
        w.Add(x.TrackIndex); w.Add(x.BoundaryOrdinal); w.Add(x.TimeSeconds); w.Add(x.DurationSeconds);
        w.Add(x.TriggerWeightThreshold); w.Add((byte)x.Kind); w.Add((byte)x.TickMode);
        w.Add(x.Payload.SemanticId); w.Add(x.Payload.EnumValue0); w.Add(x.Payload.EnumValue1);
        w.Add(x.Payload.EnumValue2); w.Add(x.Payload.ScalarValue0); w.Add(x.Payload.Flags);
        w.Add((ushort)x.Payload.TerminationReason);
    }

    private static void WriteSyncGroup(ref ReferenceFnvWriter w, in AlsSyncGroupBinding x)
    { w.Add(x.GroupId); w.Add(x.MemberOffset); w.Add(x.MemberCount); w.Add(x.LeftMarkerNameId); w.Add(x.RightMarkerNameId); }

    private static void WriteTransition(ref ReferenceFnvWriter w, in AlsDynamicTransitionBinding x)
    {
        w.Add(x.OccurrenceHandleId); w.Add(x.AuthorityGroupId);
        WriteTransitionClip(ref w, x.StandingLeft); WriteTransitionClip(ref w, x.StandingRight);
        WriteTransitionClip(ref w, x.CrouchingLeft); WriteTransitionClip(ref w, x.CrouchingRight);
        w.Add(x.DistanceMeters); w.Add(x.BlendSeconds); w.Add(x.PlayRate); w.Add(x.CooldownFrames);
    }

    private static void WriteTransitionClip(ref ReferenceFnvWriter w, in AlsDynamicTransitionClipBinding x)
    { w.Add(x.AnimationId); w.Add(x.AdditiveBaseAnimationId); w.Add(x.DurationSeconds); }

    private static void WriteAction(ref ReferenceFnvWriter w, in AlsActionDefinition x)
    {
        w.Add(x.OccurrenceHandleId); w.Add(x.MontageAuthorityGroupId); w.Add(x.SequenceAuthorityGroupId);
        w.Add(x.DefinitionId); w.Add(x.MontageId); w.Add(x.MontageDurationSeconds); w.Add(x.SlotId);
        w.Add(x.StartSectionId); w.Add(x.Priority); w.Add(x.PlayRate); w.Add(x.BlendSeconds);
        w.Add(x.Interruptible); w.Add(x.Loop);
    }

    private static void WriteGraphSample(ref ReferenceFnvWriter w, in AlsP5GraphSample x)
    { w.Add(x.AnimationId); w.Add(x.X); w.Add(x.Y); w.Add(x.RateScale); }

    private delegate void ReferenceElementWriter<T>(ref ReferenceFnvWriter writer, in T value);

    private struct ReferenceFnvWriter
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong _value;
        public ReferenceFnvWriter() => _value = Offset;
        public ulong Value => _value;

        private void AddByte(byte value)
        {
            _value ^= value;
            _value *= Prime;
        }

        public void Add(byte value) => AddByte(value);
        public void Add(sbyte value) => AddByte(unchecked((byte)value));
        public void Add(short value) => Add(unchecked((ushort)value));
        public void Add(ushort value)
        { AddByte((byte)value); AddByte((byte)(value >> 8)); }
        public void Add(int value) => Add(unchecked((uint)value));
        public void Add(uint value)
        { AddByte((byte)value); AddByte((byte)(value >> 8)); AddByte((byte)(value >> 16)); AddByte((byte)(value >> 24)); }
        public void Add(ulong value)
        { Add(unchecked((uint)value)); Add(unchecked((uint)(value >> 32))); }
        public void Add(float value) => Add(BitConverter.SingleToUInt32Bits(value));
        public void Add<T>(ReadOnlySpan<T> values, ReferenceElementWriter<T> write)
        {
            Add(values.Length);
            foreach (ref readonly var value in values) write(ref this, in value);
        }
    }

    private sealed record Fixture(
        AlsAnimationSetDefinition Set,
        AlsLocomotionAnimationProfile Locomotion,
        AlsPoseAnimationProfile Pose,
        AlsP5aAnimationRuntimeProfile P5a,
        AlsP5OccurrenceLayout Layout)
    {
        public static Fixture Create()
        {
            var root = RepositoryRoot.Find();
            var set = P3RepositoryFixtures.LoadAnimationSet();
            var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
            var pose = AlsPoseProfileCompiler.Compile(File.ReadAllText(Path.Combine(
                root, "assets", "config", "p4_pose_profile.json")), set, locomotion);
            var p5a = AlsP5aAnimationRuntimeProfileCompiler.Compile(File.ReadAllText(Path.Combine(
                root, "assets", "config", "p5a_animation_runtime.json")), set);
            var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
            return new Fixture(set, locomotion, pose, p5a, layout);
        }

        public AlsP5CoreRuntimeBindingSnapshot Compile(
            AlsAnimationSetDefinition? set = null,
            AlsLocomotionAnimationProfile? locomotion = null,
            AlsPoseAnimationProfile? pose = null,
            AlsP5aAnimationRuntimeProfile? p5a = null,
            AlsP5OccurrenceLayout? layout = null) => AlsP5CoreRuntimeBindingCompiler.Compile(
                set ?? Set, locomotion ?? Locomotion, pose ?? Pose, p5a ?? P5a, layout ?? Layout);
    }
}
