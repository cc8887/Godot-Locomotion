using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsStandingCycleTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void CrossoverBlocksHipChangeUntilFeetUncross(int hz)
    {
        var state = new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward,
            PreviousDirection = AlsCycleDirection.LeftBackward, VelocityBlend = new Vector4(0, 0, 1, 0) };
        for (var frame = 0; frame < hz; frame++)
        {
            state = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / hz, 1, 0);
            Assert.Equal(AlsCycleDirection.LeftBackward, state.Direction);
            Assert.True(state.WaitingForFeet);
        }
        state = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / hz, 0, 0);
        Assert.Equal(AlsCycleDirection.LeftForward, state.Direction);
        Assert.True(state.HipTransition);
        Assert.Equal(0.75f, state.TransitionDuration);
        for (var frame = 0; frame < hz / 2; frame++)
            state = AlsStandingCycle.AdvanceDirection(state, Vector2.UnitX, 1f / hz, 0, 0);
        Assert.True(state.IsTransitioning);
    }

    [Fact]
    public void LateralReversalKeepsHipFamilyBeforeGatedTransition()
    {
        var state = new AlsStandingCycleState { Direction = AlsCycleDirection.RightForward,
            PreviousDirection = AlsCycleDirection.RightForward, VelocityBlend = new Vector4(0, 0, 0, 1) };
        state = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / 60, 1, 0);
        Assert.Equal(AlsCycleDirection.LeftBackward, state.Direction);
        Assert.False(state.HipTransition);
        Assert.Equal(0, AlsStandingCycle.DirectionWeight(state.Direction, 2, new Vector4(0, 0, 1, 0)));
        Assert.Equal(1, AlsStandingCycle.DirectionWeight(state.Direction, 3, new Vector4(0, 0, 1, 0)));
    }

    [Theory]
    [InlineData(1, AlsCycleDirection.LeftBackward)]
    [InlineData(-1, AlsCycleDirection.LeftForward)]
    [InlineData(0, AlsCycleDirection.LeftForward)]
    public void OverlayHipBiasSelectsTheAllowedVariant(float bias, AlsCycleDirection expected)
    {
        var state = new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward };
        state = AlsStandingCycle.AdvanceDirection(state, -Vector2.UnitX, 1f / 60, 0, bias);
        Assert.Equal(expected, state.Direction);
    }

    [Fact]
    public void WeightFactorProfileMatchesNormalizedNativeWeights()
    {
        Assert.Equal(.00001f * (1 / (.00001f + .5f)), AlsStandingCycle.ProfileAlpha(0, 2));
        Assert.Equal(0.8f, AlsStandingCycle.ProfileAlpha(0.5f, 2), 5);
        Assert.Equal(2 * (1 / (2 + .00001f)), AlsStandingCycle.ProfileAlpha(1, 2));
    }

    // Native transition nodes 0..13 and 18..21, in that order. X=right, Y=forward.
    [Theory]
    [InlineData(AlsCycleDirection.Forward, 0, -1, AlsCycleDirection.Backward, 0.5f)]
    [InlineData(AlsCycleDirection.Backward, 0, 1, AlsCycleDirection.Forward, 0.5f)]
    [InlineData(AlsCycleDirection.Forward, 1, 0, AlsCycleDirection.RightForward, 0.7f)]
    [InlineData(AlsCycleDirection.Forward, -1, 0, AlsCycleDirection.LeftForward, 0.7f)]
    [InlineData(AlsCycleDirection.Backward, 1, 0, AlsCycleDirection.RightBackward, 0.7f)]
    [InlineData(AlsCycleDirection.Backward, -1, 0, AlsCycleDirection.LeftBackward, 0.7f)]
    [InlineData(AlsCycleDirection.RightForward, 0, 1, AlsCycleDirection.Forward, 0.7f)]
    [InlineData(AlsCycleDirection.LeftForward, 0, 1, AlsCycleDirection.Forward, 0.7f)]
    [InlineData(AlsCycleDirection.LeftBackward, 0, -1, AlsCycleDirection.Backward, 0.7f)]
    [InlineData(AlsCycleDirection.RightBackward, 0, -1, AlsCycleDirection.Backward, 0.7f)]
    [InlineData(AlsCycleDirection.LeftForward, 1, 0, AlsCycleDirection.RightBackward, 0.5f)]
    [InlineData(AlsCycleDirection.RightForward, -1, 0, AlsCycleDirection.LeftBackward, 0.5f)]
    [InlineData(AlsCycleDirection.RightBackward, -1, 0, AlsCycleDirection.LeftForward, 0.5f)]
    [InlineData(AlsCycleDirection.LeftBackward, 1, 0, AlsCycleDirection.RightForward, 0.5f)]
    [InlineData(AlsCycleDirection.LeftBackward, 0, 1, AlsCycleDirection.LeftForward, 0.7f)]
    [InlineData(AlsCycleDirection.LeftForward, 0, -1, AlsCycleDirection.LeftBackward, 0.7f)]
    [InlineData(AlsCycleDirection.RightBackward, 0, 1, AlsCycleDirection.RightForward, 0.7f)]
    [InlineData(AlsCycleDirection.RightForward, 0, -1, AlsCycleDirection.RightBackward, 0.7f)]
    public void OrdinaryEdgesMatchNativeTopologyAndDuration(AlsCycleDirection from, float x, float y,
        AlsCycleDirection to, float seconds)
    {
        var state = AlsStandingCycle.AdvanceDirection(new AlsStandingCycleState { Direction = from },
            new Vector2(x, y), 1f / 60, 1, 0);
        Assert.Equal(to, state.Direction);
        Assert.Equal(from, state.PreviousDirection);
        Assert.Equal(seconds, state.TransitionDuration);
        Assert.False(state.HipTransition);
        Assert.False(state.WaitingForFeet);
    }

    [Theory]
    [InlineData(AlsCycleDirection.LeftForward, 1, AlsCycleDirection.LeftBackward)]
    [InlineData(AlsCycleDirection.LeftBackward, -1, AlsCycleDirection.LeftForward)]
    [InlineData(AlsCycleDirection.RightForward, -1, AlsCycleDirection.RightBackward)]
    [InlineData(AlsCycleDirection.RightBackward, 1, AlsCycleDirection.RightForward)]
    public void BiasedHipEdgesUseNativeSign(AlsCycleDirection from, float bias, AlsCycleDirection to)
    {
        var velocity = from is AlsCycleDirection.LeftForward or AlsCycleDirection.LeftBackward ? -Vector2.UnitX : Vector2.UnitX;
        var initial = new AlsStandingCycleState { Direction = from };
        var blocked = AlsStandingCycle.AdvanceDirection(initial, velocity, 1f / 60, 0.000001f, bias);
        Assert.Equal(from, blocked.Direction);
        Assert.True(blocked.WaitingForFeet);
        var allowed = AlsStandingCycle.AdvanceDirection(initial, velocity, 1f / 60, 0, bias);
        Assert.Equal(to, allowed.Direction);
        Assert.True(allowed.HipTransition);
        Assert.Equal(0.75f, allowed.TransitionDuration);
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(0.5f)]
    public void HipThresholdBoundariesDoNotSatisfyStrictNativeComparisons(float bias)
    {
        foreach (var direction in new[] { AlsCycleDirection.LeftForward, AlsCycleDirection.LeftBackward,
                     AlsCycleDirection.RightForward, AlsCycleDirection.RightBackward })
        {
            var velocity = direction is AlsCycleDirection.LeftForward or AlsCycleDirection.LeftBackward ? -Vector2.UnitX : Vector2.UnitX;
            var state = AlsStandingCycle.AdvanceDirection(new AlsStandingCycleState { Direction = direction }, velocity, 1f / 60, 0, bias);
            Assert.Equal(direction, state.Direction);
            Assert.False(state.IsTransitioning);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAndCustomTransitionsBothUseNativeLegProfile(bool hipTransition)
    {
        var state = new AlsStandingCycleState { HipTransition = hipTransition, BlendAlpha = 0.5f };
        Assert.Equal(0.5f, AlsStandingCycle.TransitionAlpha(state, false));
        Assert.Equal(0.8f, AlsStandingCycle.TransitionAlpha(state, true), 5);
    }

    [Fact]
    public void PrepareDoesNotMutateCommittedState()
    {
        var committed = new AlsStandingCycleState { Direction = AlsCycleDirection.RightForward };
        var first = AlsStandingCycle.AdvanceDirection(committed, -Vector2.UnitX, 1f / 60, 0, 0);
        var replay = AlsStandingCycle.AdvanceDirection(committed, -Vector2.UnitX, 1f / 60, 0, 0);
        Assert.Equal(first, replay);
        Assert.Equal(AlsCycleDirection.RightForward, committed.Direction);
    }

    [Fact]
    public void OrdinaryDirectionCanInterruptAnActiveTransition()
    {
        var previous = new AlsStandingCycleState { Direction = AlsCycleDirection.Forward,
            PreviousDirection = AlsCycleDirection.LeftForward, TransitionDuration = .7f, TransitionElapsed = .1f };
        var next = AlsStandingCycle.AdvanceDirection(previous, -Vector2.UnitY, 1f / 60, 1, 0, .2f);
        Assert.Equal(AlsCycleDirection.Backward, next.Direction);
        Assert.Equal(.5f, next.TransitionDuration);
    }

    [Fact]
    public void NeutralHipRequiresFullStateWeightButBiasedHipDoesNot()
    {
        var neutral = AlsStandingCycle.AdvanceDirection(new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward },
            -Vector2.UnitX, 1f / 60, 0, 0, .9f);
        Assert.Equal(AlsCycleDirection.LeftBackward, neutral.Direction);
        var biased = AlsStandingCycle.AdvanceDirection(new AlsStandingCycleState { Direction = AlsCycleDirection.LeftBackward },
            -Vector2.UnitX, 1f / 60, 0, -1, .9f);
        Assert.Equal(AlsCycleDirection.LeftForward, biased.Direction);
        Assert.True(biased.HipTransition);
    }
}
