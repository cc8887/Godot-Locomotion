using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsActionLifecycleTests
{
    [Theory]
    [InlineData(AlsActionLifecycleMode.MontageHoldAtEnd, AlsActionBlendOption.Linear)]
    [InlineData(AlsActionLifecycleMode.MontageHoldAtEnd, AlsActionBlendOption.Cubic)]
    [InlineData(AlsActionLifecycleMode.MontageHoldAtEnd, AlsActionBlendOption.HermiteCubic)]
    [InlineData(AlsActionLifecycleMode.MontageAutoBlendOut, AlsActionBlendOption.Linear)]
    [InlineData(AlsActionLifecycleMode.MontageAutoBlendOut, AlsActionBlendOption.Cubic)]
    [InlineData(AlsActionLifecycleMode.MontageAutoBlendOut, AlsActionBlendOption.HermiteCubic)]
    public void SaturatedBlendInRemainsValidWithItsPositiveNativeCountdown(
        AlsActionLifecycleMode mode, AlsActionBlendOption option)
    {
        var fixture = new Fixture(1f, new(mode, .075f, option, .1f, option, -1f));
        for (var step = 0; step < 3; step++) fixture.Advance(.025f);
        var saturated = fixture.State.Lifecycle;
        Assert.Equal(1f, saturated.Alpha);
        Assert.Equal(1f, saturated.CurrentWeight);
        Assert.Equal(3.725290298461914e-9f, saturated.RemainingSeconds);
        fixture.Advance(.025f);
        Assert.Equal((byte)1, fixture.State.Playing);
        Assert.Equal(saturated, fixture.State.Lifecycle);
        Assert.Equal(0, fixture.Outcomes.Count);
    }

    [Fact]
    public void HermiteNaturalStopUsesTheMeasuredFloatEndpointBeforeTheClipEnd()
    {
        var fixture = new Fixture(1.5f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .1f, AlsActionBlendOption.HermiteCubic, .3f, AlsActionBlendOption.HermiteCubic, -1f));
        for (var frame = 1; frame <= 90; frame++)
        {
            if (frame == 90)
            {
                var prior = fixture.State.Lifecycle;
                var alpha = prior.Alpha + ((1f - prior.Alpha) / prior.RemainingSeconds) * (1f / 60f);
                var remaining = prior.RemainingSeconds - (1f / 60f);
                var shaped = alpha * alpha * (3f - 2f * alpha);
                Assert.Equal(.9999985098838806f, alpha);
                Assert.Equal(4.284083843231201e-7f, remaining);
                Assert.True(remaining > 0f);
                Assert.Equal(0f, prior.BeginWeight + (prior.DesiredWeight - prior.BeginWeight) * shaped);
            }
            fixture.Advance(1f / 60f);
            if (frame == 73)
            {
                Assert.Equal(1.2166662216186523f, fixture.State.PlaybackTime);
                Assert.Equal(.28333377838134766f, fixture.State.Lifecycle.RemainingSeconds);
                Assert.Equal(0f, fixture.State.Lifecycle.Alpha);
                Assert.Equal((byte)1, fixture.State.Lifecycle.BlendingOut);
            }
            if (frame < 90) Assert.Equal((byte)1, fixture.State.Playing);
        }
        Assert.Equal((byte)0, fixture.State.Playing);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(1.4999992847442627f, fixture.Result.ContributingPlayback.CurrentTime);
        Assert.Equal((double)(1f / 60f), fixture.Result.CompletionOffsetSeconds);
        Assert.Equal(1, fixture.Outcomes.Count);
    }

    [Theory]
    [InlineData(AlsActionBlendOption.Linear)]
    [InlineData(AlsActionBlendOption.Cubic)]
    [InlineData(AlsActionBlendOption.HermiteCubic)]
    public void CustomZeroTriggerKeepsEndpointActiveUntilTheLaterBlendCompletes(AlsActionBlendOption option)
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, option, .5f, option, 0f));
        fixture.Advance(1f);
        Assert.Equal((byte)1, fixture.State.Playing);
        Assert.Equal((byte)1, fixture.State.Lifecycle.TraversalFinished);
        Assert.Equal(.5f, fixture.State.Lifecycle.RemainingSeconds);
        Assert.Equal(0, fixture.Outcomes.Count);
        fixture.Advance(.25f);
        Assert.Equal((byte)1, fixture.State.Playing);
        Assert.Equal(.5f, fixture.State.Lifecycle.CurrentWeight);
        Assert.Equal(0, fixture.Count);
        fixture.Advance(.25f);
        Assert.Equal((byte)0, fixture.State.Playing);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(-1, fixture.Result.ClosingSliceIndex);
        Assert.Equal(.25d, fixture.Result.CompletionOffsetSeconds);
        Assert.Equal(0, fixture.Count);
        Assert.Equal(0f, fixture.Result.ContributingPlayback.FinalSegmentDeltaSeconds);
        fixture.Advance(.25f);
        Assert.Equal(0, fixture.Outcomes.Count);
        Assert.Equal(0, fixture.Count);
    }

    [Theory]
    [InlineData(.25f, 1f, 3, .75f)]
    [InlineData(.125f, 1f, 6, .75f)]
    [InlineData(.25f, 2f, 3, 1f)]
    public void TriggerAndPlaybackRateDetermineCompletionWithoutAFrameConstant(
        float delta, float rate, int updates, float position)
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, .75f), rate);
        for (var index = 0; index < updates; index++) fixture.Advance(delta);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(position, fixture.Result.ContributingPlayback.CurrentTime);
    }

    [Fact]
    public void DisabledAutoBlendHoldsWithoutTraversalAndCancelClearsEveryLifecycleField()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageHoldAtEnd,
            .1f, AlsActionBlendOption.HermiteCubic, .3f, AlsActionBlendOption.HermiteCubic, -1f));
        fixture.Advance(2f);
        fixture.Advance(2f);
        Assert.Equal((byte)1, fixture.State.Playing);
        Assert.Equal(1f, fixture.State.PlaybackTime);
        Assert.Equal(0, fixture.Count);
        Assert.Equal(0, fixture.Outcomes.Count);
        fixture.Apply(new(1, AlsActionCommand.Cancel, 0, -1, 0, 1));
        Assert.Equal((byte)0, fixture.State.Playing);
        Assert.Equal(default, fixture.State.Lifecycle);
        Assert.Equal(AlsActionResultCode.InterruptedByExplicitCancel, fixture.Outcomes[0].ResultCode);
    }

    [Fact]
    public void ZeroBlendCompletesAtTheRealTraversalBoundaryExactlyOnce()
    {
        var fixture = new Fixture(.5f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, AlsActionBlendOption.Linear, 0f, AlsActionBlendOption.Linear, -1f));
        fixture.Advance(1f);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(.5f, fixture.Result.ContributingPlayback.CurrentTime);
        Assert.Equal(.5f, fixture.Result.ContributingPlayback.FinalSegmentDeltaSeconds);
        Assert.Equal((byte)1, fixture.Slices[0].ClosesActionAfterSlice);
        fixture.Advance(1f);
        Assert.Equal(0, fixture.Outcomes.Count);
    }

    [Fact]
    public void CompletedWeightStillTraversesTheFullFrameAcrossASegmentBoundary()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, AlsActionBlendOption.Linear, .25f, AlsActionBlendOption.Linear, 1f));
        fixture.Segments =
        [
            new(1, 0, 0, 0, 1, 0f, .5f, 0f, .5f, 1f, 1),
            new(2, 0, 0, 1, 2, .5f, 1f, 0f, .5f, 1f, 1),
        ];
        fixture.Advance(.25f);
        fixture.Advance(.5f);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(.75f, fixture.Result.ContributingPlayback.CurrentTime);
        Assert.Equal(2, fixture.Count);
        Assert.Equal(1, fixture.Slices[1].SegmentBindingIndex);
    }

    [Fact]
    public void NotifySegmentSlicesDoNotStartTheNaturalBlendBeforeTheMontageAdvanceEnds()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, -1f));
        fixture.Segments =
        [
            new(1, 0, 0, 0, 1, 0f, .5f, 0f, .5f, 1f, 1),
            new(2, 0, 0, 1, 2, .5f, 1f, 0f, .5f, 1f, 1),
        ];
        fixture.Advance(.75f);
        Assert.Equal(.25f, fixture.State.Lifecycle.RemainingSeconds);
        Assert.Equal(0f, fixture.State.Lifecycle.Alpha);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("begin")]
    [InlineData("remaining")]
    [InlineData("alpha-time")]
    public void ImpossibleCommittedBlendStateIsRejectedWithoutTouchingAnyOutput(string mutation)
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, -1f));
        switch (mutation)
        {
            case "complete": fixture.State.Lifecycle = new() { Alpha = 1f, BeginWeight = 1f, BlendingOut = 1 }; break;
            case "begin": fixture.State.Lifecycle = new() { Alpha = 1f, BeginWeight = .5f, CurrentWeight = 1f, DesiredWeight = 1f }; break;
            case "remaining": fixture.State.Lifecycle.RemainingSeconds = 2f; break;
            case "alpha-time": fixture.State.Lifecycle.RemainingSeconds = 0f; break;
        }
        var before = fixture.State;
        var originalSlice = fixture.Slices[0];
        var originalCount = fixture.Count;
        var originalOutcome = fixture.Outcomes[0];
        Assert.False(AlsActionPlayer.TryAdvance(fixture.Definitions, fixture.Sections, fixture.Segments,
            .1d, fixture.State, fixture.Slices, ref fixture.Count, ref fixture.Outcomes,
            out var next, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(before, next);
        Assert.Equal(originalCount, fixture.Count);
        Assert.Equal(originalSlice, fixture.Slices[0]);
        Assert.Equal(originalOutcome, fixture.Outcomes[0]);
    }

    [Fact]
    public void ExactSectionHandoffDoesNotChargeANewTerminalSectionWithoutTraversal()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            0f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, 1f));
        fixture.Sections = [new(0, 0, 1, 0f, .5f), new(0, 1, -1, .5f, 1f)];
        fixture.Advance(.5f);
        Assert.Equal(1, fixture.State.SectionId);
        Assert.Equal((byte)0, fixture.State.Lifecycle.BlendingOut);
        fixture.Advance(.125f);
        Assert.Equal((byte)1, fixture.State.Lifecycle.BlendingOut);
        Assert.Equal(.5f, fixture.State.Lifecycle.RemainingSeconds);
        Assert.Equal(0f, fixture.State.Lifecycle.Alpha);
    }

    [Fact]
    public void LinkedSectionsOnlyStartNaturalBlendInTheTerminalSection()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.HermiteCubic, .5f, AlsActionBlendOption.HermiteCubic, -1f));
        fixture.Sections = [new(0, 0, 1, 0f, .5f), new(0, 1, -1, .5f, 1f)];
        fixture.Advance(.375f);
        Assert.Equal((byte)0, fixture.State.Lifecycle.BlendingOut);
        fixture.Advance(.375f);
        Assert.Equal(1, fixture.State.SectionId);
        Assert.Equal(2L, fixture.State.PlaybackEpoch);
        Assert.Equal(.75f, fixture.State.PlaybackTime);
        Assert.Equal(.25f, fixture.State.Lifecycle.RemainingSeconds);
        Assert.Equal(0f, fixture.State.Lifecycle.Alpha);
        Assert.Equal(2, fixture.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SectionAndSequenceLoopsDoNotResetLifecycleOrCreatePrematureCompletion(bool sectionLoop)
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.HermiteCubic, .5f, AlsActionBlendOption.HermiteCubic, -1f));
        if (sectionLoop)
        {
            fixture.Definitions[0] = fixture.Definitions[0] with { Loop = 1 };
            fixture.Sections[0] = fixture.Sections[0] with { NextSectionId = 0 };
            fixture.Advance(1.25f);
            Assert.Equal(2L, fixture.State.PlaybackEpoch);
            Assert.Equal(.25f, fixture.State.PlaybackTime);
            Assert.Equal((byte)0, fixture.State.Lifecycle.BlendingOut);
            Assert.Equal(1f, fixture.State.Lifecycle.CurrentWeight);
        }
        else
        {
            fixture.Segments[0] = fixture.Segments[0] with { AnimationEndTime = .5f, LoopCount = 2 };
            fixture.Advance(.75f);
            Assert.Equal(1L, fixture.State.PlaybackEpoch);
            Assert.Equal(.75f, fixture.State.PlaybackTime);
            Assert.Equal(.25f, fixture.State.Lifecycle.RemainingSeconds);
        }
        Assert.Equal((byte)1, fixture.State.Playing);
        Assert.Equal(0, fixture.Outcomes.Count);
        Assert.Equal(2, fixture.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementAndRuntimeFailureClearTheOldBlendAtomically(bool runtimeFailure)
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, .75f));
        fixture.Advance(.25f);
        Assert.Equal((byte)1, fixture.State.Lifecycle.BlendingOut);
        fixture.Count = 0;
        fixture.Outcomes.Clear();
        var request = runtimeFailure ? AlsActionRequest.None : new AlsActionRequest(2, AlsActionCommand.Start, 0, 0, 1, 1);
        Assert.True(AlsActionPlayer.TryApplyRequest(fixture.Definitions, fixture.Sections, fixture.Segments,
            1, runtimeFailure ? (byte)1 : (byte)0, request, fixture.State, fixture.Slices,
            ref fixture.Count, ref fixture.Outcomes, out var next, out _, out var failure), failure.ToString());
        Assert.Equal(runtimeFailure ? AlsActionResultCode.InterruptedByRuntimeFailure : AlsActionResultCode.InterruptedByReplacement,
            fixture.Outcomes[0].ResultCode);
        Assert.Equal(runtimeFailure ? (byte)0 : (byte)1, next.Playing);
        Assert.Equal((byte)0, next.Lifecycle.BlendingOut);
        Assert.Equal((byte)0, next.Lifecycle.TraversalFinished);
        Assert.Equal(0f, next.Lifecycle.Alpha);
        Assert.Equal(0f, next.Lifecycle.CurrentWeight);
        Assert.Equal(runtimeFailure ? 0f : .5f, next.Lifecycle.RemainingSeconds);
    }

    [Fact]
    public void TraversalCapacityFailureRollsBackTheAlreadyAdvancedBlend()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, -1f));
        fixture.Count = AlsActionPlayer.TraversalCapacity;
        var before = fixture.State;
        var originalSlice = fixture.Slices[0];
        Assert.False(AlsActionPlayer.TryAdvance(fixture.Definitions, fixture.Sections, fixture.Segments,
            .25d, fixture.State, fixture.Slices, ref fixture.Count, ref fixture.Outcomes,
            out var next, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
        Assert.Equal(before, next);
        Assert.Equal(AlsActionPlayer.TraversalCapacity, fixture.Count);
        Assert.Equal(originalSlice, fixture.Slices[0]);
        Assert.Equal(1, fixture.Outcomes.Count);
    }

    [Fact]
    public void BlendOutStartsFromTheCurrentPartialBlendInWeight()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageAutoBlendOut,
            1f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, .75f));
        fixture.Advance(.25f);
        Assert.Equal(.25f, fixture.State.Lifecycle.BeginWeight);
        Assert.Equal(.25f, fixture.State.Lifecycle.CurrentWeight);
        Assert.Equal(0f, fixture.State.Lifecycle.Alpha);
        Assert.Equal(.5f, fixture.State.Lifecycle.RemainingSeconds);
        fixture.Advance(.25f);
        Assert.Equal(.125f, fixture.State.Lifecycle.CurrentWeight);
        Assert.Equal(.5f, fixture.State.Lifecycle.Alpha);
        fixture.Advance(.25f);
        Assert.Equal(AlsActionResultCode.Completed, fixture.Result.ClosingReason);
        Assert.Equal(.75f, fixture.Result.ContributingPlayback.CurrentTime);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("in-option")]
    [InlineData("out-option")]
    [InlineData("in-nan")]
    [InlineData("out-negative")]
    [InlineData("trigger-infinity")]
    [InlineData("legacy-payload")]
    public void InvalidLifecycleSettingsAreRejectedByThePublicActionBoundary(string mutation)
    {
        var fixture = new Fixture(1f, default);
        var settings = new AlsActionLifecycleSettings(AlsActionLifecycleMode.MontageAutoBlendOut,
            .5f, AlsActionBlendOption.Linear, .5f, AlsActionBlendOption.Linear, -1f);
        settings = mutation switch
        {
            "mode" => settings with { Mode = (AlsActionLifecycleMode)255 },
            "in-option" => settings with { BlendInOption = (AlsActionBlendOption)3 },
            "out-option" => settings with { BlendOutOption = (AlsActionBlendOption)3 },
            "in-nan" => settings with { BlendInSeconds = float.NaN },
            "out-negative" => settings with { BlendOutSeconds = -.5f },
            "trigger-infinity" => settings with { BlendOutTriggerSeconds = float.PositiveInfinity },
            _ => settings with { Mode = AlsActionLifecycleMode.LegacySectionEnd },
        };
        fixture.Definitions[0] = fixture.Definitions[0] with { Lifecycle = settings };
        Assert.False(AlsActionPlayer.TryAdvance(fixture.Definitions, fixture.Sections, fixture.Segments,
            .1d, fixture.State, fixture.Slices, ref fixture.Count, ref fixture.Outcomes,
            out var next, out _, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        Assert.Equal(fixture.State, next);
    }

    [Fact]
    public void HeldEndpointAndActiveBlendUpdatesAllocateNoManagedStorage()
    {
        var fixture = new Fixture(1f, new(AlsActionLifecycleMode.MontageHoldAtEnd,
            2f, AlsActionBlendOption.HermiteCubic, .3f, AlsActionBlendOption.HermiteCubic, -1f));
        for (var index = 0; index < 100; index++) fixture.Advance(.01f);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++) fixture.Advance(.01f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Fixture
    {
        public readonly AlsActionDefinition[] Definitions;
        public AlsActionSectionBinding[] Sections;
        public AlsActionSegmentBinding[] Segments;
        public readonly AlsActionTraversalSlice[] Slices = new AlsActionTraversalSlice[16];
        public AlsActionPlayerState State = AlsActionPlayerState.CreateDefault();
        public AlsActionAdvanceResult Result;
        public AlsActionOutcomeBuffer Outcomes;
        public int Count;

        public Fixture(float duration, AlsActionLifecycleSettings lifecycle, float rate = 1f)
        {
            Definitions = [new(0, 0, 1, 0, 0, duration, 0, 0, 1, rate, .2f, 1, 0, lifecycle)];
            Sections = [new(0, 0, -1, 0f, duration)];
            Segments = [new(1, 0, 0, 0, 1, 0f, duration, 0f, duration, 1f, 1)];
            Apply(new(1, AlsActionCommand.Start, 0, 0, 1, 1));
        }

        public void Apply(AlsActionRequest request)
        {
            Count = 0;
            Outcomes.Clear();
            if (!AlsActionPlayer.TryApplyRequest(Definitions, Sections, Segments, 1, 0, request,
                    State, Slices, ref Count, ref Outcomes, out State, out _, out var failure))
                throw new InvalidOperationException(failure.ToString());
        }

        public void Advance(float delta)
        {
            Count = 0;
            Outcomes.Clear();
            if (!AlsActionPlayer.TryAdvance(Definitions, Sections, Segments, delta,
                    State, Slices, ref Count, ref Outcomes, out State, out Result, out var failure))
                throw new InvalidOperationException(failure.ToString());
        }
    }
}
