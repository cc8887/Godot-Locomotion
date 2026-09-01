using System.Numerics;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;

namespace GodotAls.Core.Tests;

[Collection("AllocationTests")]
public sealed class AlsP5HotPathAllocationTests
{
    [Fact]
    public void CompletePrepareFinalizePathAllocatesZeroBytesAfterOneHundredWarmups()
    {
        var curveRanges = new[] { new AlsAnimationCurveRange(0, 0, 0) };
        var allowIndices = new[] { -1 };
        var footBindings = new[] { new AlsP4FootCurveRuntimeBinding(0, -1, -1, 0f, 0f) };
        var syncMembers = new[] { new AlsSyncMemberBinding(0, 0, 1f, 1, 1) };
        var transition = new AlsDynamicTransitionBinding(
            1, 1,
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            new AlsDynamicTransitionClipBinding(1, 2, 1f),
            0.5f, 0.2f, 1f, 2);
        var bindings = new AlsP5RuntimeBindings(
            1, 1, 1,
            ReadOnlySpan<AlsCurveKey>.Empty,
            ReadOnlySpan<AlsCurveBinding>.Empty,
            ReadOnlySpan<AlsP5CurveBindingIdentity>.Empty,
            curveRanges,
            new AlsP5CurveSemanticPolicy(0f, AlsP5CurveCombineMode.AdditiveToDefault, 0f, 1f),
            allowIndices,
            footBindings,
            1f, 0f, 0f, 1f,
            ReadOnlySpan<AlsTimelineEventDefinition>.Empty,
            ReadOnlySpan<AlsSyncMarkerDefinition>.Empty,
            new AlsSyncGroupBinding(0, 0, 1, 10, 11),
            syncMembers,
            ReadOnlySpan<AlsP5SyncOccurrenceBinding>.Empty,
            transition,
            ReadOnlySpan<GodotAls.Core.Actions.AlsActionDefinition>.Empty,
            ReadOnlySpan<GodotAls.Core.Actions.AlsActionSectionBinding>.Empty,
            ReadOnlySpan<GodotAls.Core.Actions.AlsActionSegmentBinding>.Empty,
            ReadOnlySpan<AlsActionTimelineRange>.Empty);
        var basePlayback = new[]
        {
            new AlsBasePlaybackDescriptor(0, 0, 0, 1, 0d, (double)0.1f, 0d, (double)0.1f, 1f, 1f, 1, 1, 0),
        };
        var input = new AlsP5FrameInput(
            new AlsFrameIdentity(1, 0, 1), 0d, (double)0.1f, 0.1f, 1,
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
        var currentCursors = new[]
        {
            AlsTimelineCursor.CreateDefault(),
            AlsTimelineCursor.CreateDefault(),
        };
        var candidateCursors = new[]
        {
            AlsTimelineCursor.CreateDefault(),
            AlsTimelineCursor.CreateDefault(),
        };
        var currentAuthorities = new[]
        {
            AlsTimelineAuthorityState.CreateDefault(0),
            AlsTimelineAuthorityState.CreateDefault(1),
        };
        var candidateAuthorities = new[]
        {
            AlsTimelineAuthorityState.CreateDefault(0),
            AlsTimelineAuthorityState.CreateDefault(1),
        };
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
        var p4Result = AlsFrameResult.CreateDefault(input.Identity);
        var probe = new AlsDynamicTransitionInput(
            AlsStance.Standing, 0f, Vector3.Zero, Vector3.Zero, 0,
            Vector3.Zero, Vector3.Zero, 0);

        long before = 0;
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
