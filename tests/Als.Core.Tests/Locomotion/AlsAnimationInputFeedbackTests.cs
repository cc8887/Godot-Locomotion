using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsAnimationInputFeedbackTests
{
    [Fact]
    public void MapsReorderedFinalCurvesAndPreservesAbsentVersusAuthoredZero()
    {
        var id = new AlsFrameIdentity(4, 11, 2);
        var feedback = AlsAnimationInputFeedback.FromCompletedFrame(id,
            ["HipOrientation_Bias", "Unused", "Feet_Position", "Feet_Crossing"],
            [new(-1), new(42), new(0), new(7, false)]);
        feedback.Validate(id);
        Assert.Equal(new AlsInertialCurve(0), feedback.FeetPosition);
        Assert.Equal(new AlsInertialCurve(7, false), feedback.FeetCrossing);
        var rules = new AlsGroundedRuleInput(true, false, true, AlsStance.Crouching, false, true, 9, 9)
            { HipBias = 9, FeetCrossing = 9, MovementState = AlsMovementStateInput.InAir, Jumped = true };
        Assert.Equal(rules with { BasePoseClf = 0, FeetPosition = 0, FeetCrossing = 0, HipBias = -1 }, feedback.ApplyTo(rules));
        var missing = AlsAnimationInputFeedback.FromCompletedFrame(id, ["feet_position"], [new(1)]);
        Assert.False(missing.FeetPosition.Present);
        Assert.Equal(0, missing.ApplyTo(rules).FeetPosition);
    }

    [Theory]
    [InlineData("Feet_Position")] [InlineData("Feet_Crossing")] [InlineData("HipOrientation_Bias")]
    public void RejectsAmbiguousAndNonFiniteFinalCurves(string name)
    {
        var id = new AlsFrameIdentity(1, 11, 2);
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(id, [name, name], [new(0), new(1)]));
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(id, [name], [new(float.NaN)]));
        Assert.Throws<ArgumentException>(() => AlsAnimationInputFeedback.FromCompletedFrame(id, [name], [new(float.PositiveInfinity)]));
    }

    [Fact]
    public void RequiresAnAcceptedFrameBeforeCurvesCanBecomeHistory()
    {
        var id = new AlsFrameIdentity(3, 11, 2);
        var feedback = AlsAnimationInputFeedback.FromCompletedFrame(id, ["Feet_Crossing"], [new(0)]);
        Assert.Throws<ArgumentException>(() => feedback.Validate(new(2, 11, 2)));
        Assert.Throws<ArgumentException>(() => feedback.Validate(new(3, 12, 2)));
        Assert.Throws<ArgumentException>(() => feedback.Validate(new(3, 11, 3)));
        Assert.Throws<ArgumentException>(() => (feedback with { HasFrame = false }).Validate(default));
        Assert.Throws<ArgumentException>(() => (default(AlsAnimationInputFeedback) with { FeetPosition = new(0) }).Validate(default));
        Assert.Throws<ArgumentException>(() => (default(AlsAnimationInputFeedback) with { HipOrientationBias = new(0) }).Validate(default));
        feedback.Validate(id);
        default(AlsAnimationInputFeedback).Validate(default);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FinalCrossingGateWaitsForUncrossingRatherThanAnElapsedDelay(int hz)
    {
        var state = new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward,
            PreviousDirection = AlsCycleDirection.LeftBackward, VelocityBlend = new(0, 0, 1, 0) };
        var rules = new AlsGroundedRuleInput(true, false, false, AlsStance.Standing, true, false, 0, 0);
        // Nonzero final crossing can come from a later layer even when a local
        // cycle is uncrossed. Its exact value, including a small blend, gates exit.
        for (var frame = 1; frame <= hz * 2; frame++)
        {
            var feedback = AlsAnimationInputFeedback.FromCompletedFrame(new(frame, 11, 2),
                ["Feet_Crossing", "HipOrientation_Bias"], [new(.001f), new(0)]);
            var inputs = feedback.ApplyTo(rules);
            state = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / hz, inputs.FeetCrossing, inputs.HipBias);
            Assert.Equal(AlsCycleDirection.LeftBackward, state.Direction);
            Assert.True(state.WaitingForFeet);
        }
        var uncommitted = AlsAnimationInputFeedback.FromCompletedFrame(new(hz * 2 + 1, 11, 2),
            ["Feet_Crossing", "HipOrientation_Bias"], [new(0), new(0)]).ApplyTo(rules);
        var candidate = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / hz, uncommitted.FeetCrossing, uncommitted.HipBias);
        Assert.Equal(AlsCycleDirection.LeftForward, candidate.Direction);
        Assert.True(candidate.HipTransition);
        Assert.Equal(.75f, candidate.TransitionDuration);
        Assert.Equal(AlsCycleDirection.LeftBackward, state.Direction);
    }
}
