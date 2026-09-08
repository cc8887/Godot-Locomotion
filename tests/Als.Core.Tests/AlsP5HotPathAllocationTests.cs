using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsP5HotPathAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletePrepareFinalizePathAllocatesZeroBytesAfterOneHundredWarmups(bool lifecycle)
    {
        var curveKeys = new[]
        {
            new AlsCurveKey(0f, 0.2f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(1f, 0.2f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(0f, 0.25f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(1f, 0.25f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(0f, 0.75f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(1f, 0.75f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(0f, 0.1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(1f, 0.1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(0f, 0.15f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new AlsCurveKey(1f, 0.15f, 0f, 0f, AlsCurveInterpolationMode.Linear),
        };
        var curveBindings = new[]
        {
            new AlsCurveBinding(100, 0, 2, 1f, 1, 1),
            new AlsCurveBinding(101, 2, 2, 1f, 1, 1),
            new AlsCurveBinding(102, 4, 2, 1f, 1, 1),
            new AlsCurveBinding(100, 6, 2, 1f, 1, 1),
            new AlsCurveBinding(100, 8, 2, 1f, 1, 1),
        };
        var curveIdentities = new[]
        {
            new AlsP5CurveBindingIdentity(0, 100),
            new AlsP5CurveBindingIdentity(0, 101),
            new AlsP5CurveBindingIdentity(0, 102),
            new AlsP5CurveBindingIdentity(1, 100),
            new AlsP5CurveBindingIdentity(20, 100),
        };
        var curveRanges = new AlsAnimationCurveRange[23];
        for (var index = 0; index < curveRanges.Length; index++)
        {
            curveRanges[index] = new AlsAnimationCurveRange(index, index == 0 ? 0 : index <= 20 ? 4 : 5, 0);
        }
        curveRanges[0] = new AlsAnimationCurveRange(0, 0, 3);
        curveRanges[1] = new AlsAnimationCurveRange(1, 3, 1);
        curveRanges[20] = new AlsAnimationCurveRange(20, 4, 1);
        var allowIndices = new int[23];
        Array.Fill(allowIndices, -1);
        allowIndices[0] = 0;
        allowIndices[1] = 3;
        allowIndices[20] = 4;
        var footBindings = new[] { new AlsP4FootCurveRuntimeBinding(0, 101, 102, 0f, 0f) };
        var timelineDefinitions = new[]
        {
            new AlsTimelineEventDefinition(
                1, 0, -1, 0, AlsTimelineSourceKind.Animation, 0, 0, 0,
                0.05f, 0f, 0f, AlsTimelineEventKind.Generic,
                AlsTimelineTickMode.Queued, default),
        };
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(1, 10, 0, 0, 0, 0f),
            new AlsSyncMarkerDefinition(2, 11, 0, 1, 0, 0.5f),
        };
        var syncMembers = new[] { new AlsSyncMemberBinding(0, 0, 1f, 1, 1) };
        var syncOccurrences = new[] { new AlsP5SyncOccurrenceBinding(0, 0, 0, 0) };
        var transition = new AlsDynamicTransitionBinding(
            1, 1,
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(2, 2, 1f),
            new AlsDynamicTransitionClipBinding(3, 2, 1f),
            new AlsDynamicTransitionClipBinding(4, 2, 1f),
            0.5f, 0.2f, 1f, 2);
        var actionDefinitions = new[]
        {
            new GodotAls.Core.Actions.AlsActionDefinition(
                2, 2, 3, 0, 10, 1f, 0, 0, 1, 1f, 0.2f, 1, 0),
        };
        if (lifecycle) actionDefinitions[0] = actionDefinitions[0] with
        {
            Lifecycle = new(AlsActionLifecycleMode.MontageAutoBlendOut, .2f,
                AlsActionBlendOption.HermiteCubic, .5f, AlsActionBlendOption.HermiteCubic, -1f),
        };
        var actionSections = new[]
        {
            new GodotAls.Core.Actions.AlsActionSectionBinding(0, 0, -1, 0f, 1f),
        };
        var actionSegments = new[]
        {
            new GodotAls.Core.Actions.AlsActionSegmentBinding(
                3, 0, 0, 0, 20, 0f, 0.5f, 0f, 0.5f, 1f, 1),
            new GodotAls.Core.Actions.AlsActionSegmentBinding(
                4, 0, 0, 1, 21, 0.5f, 1f, 0f, 0.5f, 1f, 1),
        };
        var bindings = new AlsP5RuntimeBindings(
            AlsP5RuntimeBindings.CurrentVersion, 1, 1,
            curveKeys,
            curveBindings,
            curveIdentities,
            curveRanges,
            new AlsP5CurveSemanticPolicy(0f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f),
            allowIndices,
            footBindings,
            1f, 0f, 0f, 1f,
            timelineDefinitions,
            markers,
            new AlsSyncGroupBinding(0, 0, 1, 10, 11),
            syncMembers,
            syncOccurrences,
            transition,
            actionDefinitions,
            actionSections,
            actionSegments,
            new[] { new AlsActionTimelineRange(0, 1, 0) });
        var basePlayback = new[]
        {
            new AlsBasePlaybackDescriptor(0, 0, 0, 1, 0d, (double)0.2f, 0d, (double)0.2f, 1f, 1f, 1, 1, 0),
        };
        var input = new AlsP5FrameInput(
            new AlsFrameIdentity(1, 0, 1), 0d, (double)0.2f, 0.2f, 1,
            AlsActionRequest.None, 0, 0,
            AlsTimelineLocomotionMode.Grounded,
            AlsTimelineRotationMode.LookingDirection,
            AlsTimelineStance.Standing,
            new AlsP4CurveFrameInput(
                basePlayback,
                ReadOnlySpan<AlsBasePlaybackDescriptor>.Empty,
                ReadOnlySpan<AlsBasePlaybackDescriptor>.Empty,
                AlsAnimationState.Grounded,
                0f,
                0f));
        var currentCursors = Enumerable.Range(0, 5)
            .Select(_ => AlsTimelineCursor.CreateDefault()).ToArray();
        var candidateCursors = Enumerable.Range(0, 5)
            .Select(_ => AlsTimelineCursor.CreateDefault()).ToArray();
        var currentAuthorities = Enumerable.Range(0, 4)
            .Select(AlsTimelineAuthorityState.CreateDefault).ToArray();
        var candidateAuthorities = Enumerable.Range(0, 4)
            .Select(AlsTimelineAuthorityState.CreateDefault).ToArray();
        var currentOwnership = CreateOwnership();
        var candidateOwnership = CreateOwnership();
        var control = new[] { new AlsP5RuntimeScratchControl(1) };
        var occurrences = new AlsTimelineOccurrence[16];
        var slices = new GodotAls.Core.Actions.AlsActionTraversalSlice[16];
        var playbacks = new AlsTimelinePlayback[35];
        var syncInput = new AlsSyncPlayback[1];
        var syncOutput = new AlsSyncMappedPlayback[1];
        var curveSamples = new AlsCurveBlendSample[5];
        var state = AlsRuntimeState.CreateDefault();
        state.ActionPlayer = new AlsActionPlayerState
        {
            ActionDefinitionId = 0,
            SectionId = 0,
            SegmentBindingIndex = 0,
            RequestId = 1,
            LastProcessedRequestId = 1,
            LastProcessedCommandRequestId = 1,
            LastProcessedCommand = AlsActionCommand.Start,
            PlaybackEpoch = 1,
            PlaybackTime = 0.4f,
            Priority = 1,
            Playing = 1,
            Interruptible = 1,
        };
        state.ActionBlendLane = AlsLaneBlendState.CreateDefault();
        if (lifecycle) state.ActionPlayer.Lifecycle = new()
        {
            Alpha = .5f, RemainingSeconds = .1f, CurrentWeight = .5f, DesiredWeight = 1f,
        };
        state.ActionBlendLane.LaneWeight = 1f;
        state.ActionBlendLane.IncomingMix = 1f;
        state.ActionBlendLane.BlendSeconds = 0.2f;
        state.ActionBlendLane.VisualActive = 1;
        state.DynamicTransition.QueuedAnimationId = 1;
        state.DynamicTransition.QueuedFoot = AlsTransitionFoot.Left;
        state.DynamicTransition.Queued = 1;
        var p4Result = AlsFrameResult.CreateDefault(input.Identity);
        p4Result.PlayRate = 1f;
        currentCursors[2] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 2,
            AnimationId = 10,
            ActionId = 0,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = (double)0.4f,
        };
        currentCursors[3] = new AlsTimelineCursor
        {
            OccurrenceHandleId = 3,
            AnimationId = 20,
            ActionId = 0,
            PlaybackEpoch = 1,
            ConsumedUnwrappedTimeSeconds = (double)0.4f,
        };
        currentAuthorities[2] = new AlsTimelineAuthorityState
        {
            GroupId = 2,
            OccurrenceHandleId = 2,
            AnimationId = 10,
            ActionId = 0,
            PlaybackEpoch = 1,
            Active = 1,
        };
        currentAuthorities[3] = new AlsTimelineAuthorityState
        {
            GroupId = 3,
            OccurrenceHandleId = 3,
            AnimationId = 20,
            ActionId = 0,
            PlaybackEpoch = 1,
            Active = 1,
        };
        var probe = new AlsDynamicTransitionInput(
            AlsStance.Standing, 0f, Vector3.Zero, Vector3.Zero, 0,
            Vector3.Zero, Vector3.Zero, 0);

        long before = 0;
        var traversedSegmentBoundary = false;
        for (var index = 0; index < 10_100; index++)
        {
            if (index == 100)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
            }

            var scratch = new AlsP5RuntimeScratch(
                1, 1, control, candidateCursors, candidateAuthorities, candidateOwnership,
                occurrences, slices, playbacks, syncInput, syncOutput, curveSamples);
            if (!AlsP5Runtime.TryPrepare(
                    in bindings, in input, in state,
                    currentCursors, currentAuthorities, currentOwnership, 1,
                    ref scratch, out var prepared, out var prepareFailure))
            {
                throw new InvalidOperationException($"Prepare failed: {prepareFailure}");
            }
            traversedSegmentBoundary |=
                slices[0].SegmentBindingIndex == 0 &&
                slices[1].SegmentBindingIndex == 1 &&
                slices[0].ClosesSegmentAfterSlice == 1 &&
                slices[1].ActivatesSegmentAtSliceStart == 1;
            var finalizeScratch = scratch;
            if (!AlsP5Runtime.TryFinalize(
                    prepared, ref finalizeScratch, in p4Result, in state, in probe,
                    out _, out _, out _, out var finalizeFailure))
            {
                throw new InvalidOperationException($"Finalize failed: {finalizeFailure}");
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(traversedSegmentBoundary);
    }

    private static AlsNotifyStateOwnership[] CreateOwnership()
    {
        var values = new AlsNotifyStateOwnership[16];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = AlsNotifyStateOwnership.CreateDefault();
        }
        return values;
    }
}
