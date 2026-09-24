using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsRollingGameplayTests
{
    [Fact]
    public void GetUpAcceptanceDoesNotClaimRollingAndOldRollInterruptionStillClearsIt()
    {
        var frame = AlsFrameInput.CreateDefault(new(3, 1, 1), .01f);
        var outcomes = new AlsActionOutcomeBuffer();
        outcomes.TryAdd(new(2, 1, 20, AlsActionResultCode.Accepted));
        Assert.Equal(default, AlsRollingGameplay.ApplyOutcomes(default, frame, outcomes, 0));
        outcomes.TryAdd(new(1, 0, 10, AlsActionResultCode.InterruptedByReplacement));
        Assert.Equal(default, AlsRollingGameplay.ApplyOutcomes(new(1, 10, 90), frame, outcomes, 0));
    }
    [Theory]
    [InlineData(false, AlsTimelineAction.None, false, false)]
    [InlineData(true, AlsTimelineAction.None, true, true)]
    [InlineData(true, AlsTimelineAction.Rolling, true, false)]
    [InlineData(true, AlsTimelineAction.Rolling, false, true)]
    [InlineData(true, AlsTimelineAction.GettingUp, false, false)]
    public void NativeGroundedGate(bool grounded, AlsTimelineAction action, bool playing, bool expected) =>
        Assert.Equal(expected, AlsRollingGameplay.CanStart(new(grounded, action), playing));

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RotationConvergesWithoutFollowingLaterInput(int hz)
    {
        var yaw = 0f;
        for (var i = 0; i < hz; i++) yaw = AlsRollingGameplay.Rotate(yaw, 90, 1f / hz);
        Assert.InRange(yaw, 89.90f, 89.93f);
        Assert.Equal(90, AlsRollingGameplay.Rotate(0, 90, 1f / hz, 0));
        Assert.True(AlsRollingGameplay.Rotate(0, 180, 1f / hz) < 0);
        Assert.True(AlsRollingGameplay.Rotate(0, 176, 1f / hz) < 0);
        Assert.True(AlsRollingGameplay.Rotate(179, -179, 1f / hz) > 179);
    }

    [Fact]
    public void AcceptanceCapturesInputYawAndRejectionKeepsThePreviousTarget()
    {
        var frame = AlsFrameInput.CreateDefault(new(1, 1, 1), 1f/60) with { InputDirection = Vector3.UnitX };
        var outcomes = new AlsActionOutcomeBuffer(); outcomes.TryAdd(new(1, 0, 10, AlsActionResultCode.Accepted));
        var state = AlsRollingGameplay.ApplyOutcomes(default, frame, outcomes);
        Assert.Equal(new AlsRollingState(1, 10, 90), state);
        outcomes = default; outcomes.TryAdd(new(2, 0, 0, AlsActionResultCode.RejectedBusy));
        Assert.Equal(state, AlsRollingGameplay.ApplyOutcomes(state, frame with { InputDirection = -Vector3.UnitX }, outcomes));
        Assert.Equal(-90, AlsRollingGameplay.TargetYaw(frame with { InputDirection = default, CharacterYaw = MathF.PI / 2 }));
    }

    [Fact]
    public void OldInterruptionDoesNotClearNewPhysicalOwner()
    {
        var state = new AlsRollingState(2, 20, 90);
        var outcomes = new AlsActionOutcomeBuffer(); outcomes.TryAdd(new(1, 0, 10, AlsActionResultCode.InterruptedByReplacement));
        var frame = AlsFrameInput.CreateDefault(new(3,1,1), .01f);
        Assert.Equal(state, AlsRollingGameplay.ApplyOutcomes(state, frame, outcomes));
        outcomes.TryAdd(new(2, 0, 20, AlsActionResultCode.InterruptedByExplicitCancel));
        Assert.Equal(default, AlsRollingGameplay.ApplyOutcomes(state, frame, outcomes));
    }

    [Fact]
    public void OnlyTheCurrentPhysicalRollEndClearsGameplayOwnership()
    {
        var state = new AlsRollingState(2, 20, 90);
        var e = new AlsAnimationEvent(17, 19, 23, 29, 10, 3, 101, 211, 7, .004f, .75f,
            AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.End, new(13, (int)AlsTimelineAction.Rolling, 0, 0, 0, 0, 0));
        var events = new AlsEventBuffer(); events.TryAdd(e);
        Assert.Equal(state, AlsRollingGameplay.ApplyNotifies(state, events));
        events.TryAdd(e with { PlaybackEpoch = 20 });
        Assert.Equal(default, AlsRollingGameplay.ApplyNotifies(state, events));
    }
}
