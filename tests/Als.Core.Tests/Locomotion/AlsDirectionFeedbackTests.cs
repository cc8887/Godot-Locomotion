using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsDirectionFeedbackTests
{
    private static AlsDirectionFeedbackDefinition Definition(float seconds = .1f) => new(2, seconds,
        Enumerable.Range(0, 6).Select(i => new AlsDirectionFeedbackEvent(16 + i, i, false)).ToArray(),
        [new(AlsCycleDirection.Forward, AlsCycleDirection.Backward, false, 22)]);

    [Theory]
    [InlineData(1.9999f, true)]
    [InlineData(2f, false)]
    [InlineData(2.0001f, false)]
    public void PivotUsesStrictUnfilteredSpeedLimit(float speed, bool expected)
    {
        AlsDirectionFeedbackEvents events = default;
        Definition().Transition(AlsCycleDirection.Forward, AlsCycleDirection.Backward, false, false, ref events);
        var state = AlsDirectionFeedback.Complete(default, events, speed, .02f, Definition());
        Assert.Equal(expected, state.Pivot);
        Assert.True(state.DelayPending);
        Assert.Equal(.08f, state.DelayRemaining, 6);
        Assert.Equal(1, state.TrackedHips);
    }

    [Fact]
    public void RepeatedNotifySetsPivotButDoesNotRestartExistingDelay()
    {
        AlsDirectionFeedbackEvents events = default;
        events.Add(new(22, 0, true));
        var definition = Definition();
        var state = AlsDirectionFeedback.Complete(default, events, 1, .04f, definition);
        Assert.True(state.Pivot);
        state = AlsDirectionFeedback.Complete(state, events, 3, .04f, definition);
        Assert.False(state.Pivot);
        Assert.Equal(.02f, state.DelayRemaining, 6);
        state = AlsDirectionFeedback.Complete(state, events, 1, .04f, definition);
        Assert.False(state.Pivot);
        Assert.False(state.DelayPending);
        Assert.Equal(0, state.DelayRemaining);
    }

    [Fact]
    public void SkippedFirstBlendStillEntersStatesButDoesNotStartPivot()
    {
        AlsDirectionFeedbackEvents events = default;
        var definition = Definition();
        definition.Enter(AlsCycleDirection.Forward, ref events);
        definition.Transition(AlsCycleDirection.Forward, AlsCycleDirection.Backward, false, true, ref events);
        Assert.Equal(2, events.Count);
        Assert.Equal(16, events[0].NotifyIndex);
        Assert.Equal(17, events[1].NotifyIndex);
        var state = AlsDirectionFeedback.Complete(default, events, 1, .02f, definition);
        Assert.Equal(1, state.TrackedHips);
        Assert.False(state.Pivot);
        Assert.False(state.DelayPending);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void LatentPhaseUsesFloatSubtractionAndDoesNotNeedAnActiveGraph(int hz)
    {
        var definition = Definition();
        AlsDirectionFeedbackEvents events = default;
        events.Add(new(22, 0, true));
        var delta = 1f / hz;
        var expected = .1f;
        var state = default(AlsDirectionFeedbackState);
        for (var frame = 0; frame < hz; frame++)
        {
            var before = state;
            var next = AlsDirectionFeedback.Complete(before, events, 1, delta, definition);
            Assert.Equal(next, AlsDirectionFeedback.Complete(before, events, 1, delta, definition));
            state = next;
            expected = MathF.Max(0, expected - delta);
            Assert.Equal(expected, state.DelayRemaining);
            Assert.Equal(expected > 0, state.Pivot);
            events = default;
        }
    }

    [Fact]
    public void EventsAreOrderedAndNotDeduplicatedByNotifyIndex()
    {
        AlsDirectionFeedbackEvents events = default;
        events.Add(new(16, 0, false)); events.Add(new(17, 1, false)); events.Add(new(16, 0, false));
        Assert.Equal(3, events.Count);
        Assert.Equal(0, AlsDirectionFeedback.Complete(default, events, 0, 0, Definition()).TrackedHips);
    }

    [Fact]
    public void ZeroDurationCompletesInTheSameLatentPhase()
    {
        AlsDirectionFeedbackEvents events = default;
        events.Add(new(22, 0, true));
        Assert.Equal(default, AlsDirectionFeedback.Complete(default, events, 1, 0, Definition(0)));
    }

    [Fact]
    public void RejectedTransitionDoesNotPublishHalfAnEventPair()
    {
        AlsDirectionFeedbackEvents events = default;
        for (var i = 0; i < 7; i++) events.Add(new(16, 0, false));
        Assert.Throws<InvalidOperationException>(() => Definition().Transition(AlsCycleDirection.Forward,
            AlsCycleDirection.Backward, false, false, ref events));
        Assert.Equal(7, events.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Definition().Enter((AlsCycleDirection)6, ref events));
        Assert.Equal(7, events.Count);
    }

    [Fact]
    public void InvalidInputDoesNotModifyTheCommittedState()
    {
        var previous = new AlsDirectionFeedbackState(true, true, .05f, 2);
        Assert.Throws<ArgumentException>(() => AlsDirectionFeedback.Complete(previous, default, float.NaN, .02f, Definition()));
        Assert.Equal(new(true, true, .05f, 2), previous);
        AlsDirectionFeedbackEvents events = default;
        Assert.Throws<InvalidOperationException>(() => Definition().Transition(AlsCycleDirection.Forward,
            AlsCycleDirection.LeftForward, false, false, ref events));
        Assert.Equal(0, events.Count);
    }
}
