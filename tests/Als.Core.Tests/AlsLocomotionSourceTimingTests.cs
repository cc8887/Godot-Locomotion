using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsLocomotionSourceTimingTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(.93f, .93f)]
    [InlineData(1f, 0f)]
    public void LoopingSummaryCanonicalizesOnlyTheInclusiveEndpoint(float source, float expected) =>
        Assert.Equal(expected, AlsLocomotionSourceTiming.NormalizeLoopingPhase(source));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.01f)]
    [InlineData(1.01f)]
    public void LoopingSummaryDoesNotHideInvalidSourceRatios(float source) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionSourceTiming.NormalizeLoopingPhase(source));

    [Theory]
    [InlineData(0f)]
    [InlineData(1.75f)]
    [InlineData(3.75f)]
    [InlineData(6.5f)]
    public void SourceGraphDefersOnlyTimingWithoutAdvancingTheLegacyClock(float speed)
    {
        var input = P3TestInput.Moving(speed);
        var legacyState = new AlsRuntimeState { AnimationPhase = .37f };
        var sourceState = legacyState;
        var legacy = new AlsFrameResult();
        var source = legacy;
        AlsLocomotionModel.Evaluate(input, ref legacyState, ref legacy, P3TestSettings.Reference);
        AlsLocomotionModel.Evaluate(input, ref sourceState, ref source, P3TestSettings.Reference,
            AlsLocomotionTimingPolicy.StandingSourceGraph);
        Assert.Equal(.37f, sourceState.AnimationPhase);
        Assert.True(AlsLocomotionModel.HasPendingSourceTiming(source));
        Assert.True(float.IsNaN(source.Stride) && float.IsNaN(source.PlayRate) && float.IsNaN(source.AnimationPhase));
        source.Stride = legacy.Stride; source.PlayRate = legacy.PlayRate; source.AnimationPhase = legacy.AnimationPhase;
        sourceState.AnimationPhase = legacyState.AnimationPhase;
        Assert.Equal(legacyState, sourceState);
        Assert.Equal(Digest(legacy), Digest(source));
    }

    [Theory]
    [InlineData(AlsStance.Crouching, false)]
    [InlineData(AlsStance.Standing, true)]
    public void UnownedBranchesKeepTheirExistingTiming(AlsStance stance, bool air)
    {
        var input = P3TestInput.Moving(2, stance: stance);
        if (air) input = input with { Floor = input.Floor with { IsGrounded = 0 } };
        var a = new AlsRuntimeState { AnimationPhase = .37f }; var b = a;
        var x = new AlsFrameResult(); var y = x;
        AlsLocomotionModel.Evaluate(input, ref a, ref x, P3TestSettings.Reference);
        AlsLocomotionModel.Evaluate(input, ref b, ref y, P3TestSettings.Reference, AlsLocomotionTimingPolicy.StandingSourceGraph);
        Assert.False(AlsLocomotionModel.HasPendingSourceTiming(y));
        Assert.Equal(a, b); Assert.Equal(Digest(x), Digest(y));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(1.2f, 3f, .93f)]
    public void CompletionUsesSourceValuesAndCanOnlyHappenOnce(float stride, float rate, float phase)
    {
        var input = P3TestInput.Moving(2);
        var state = new AlsRuntimeState(); var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference, AlsLocomotionTimingPolicy.StandingSourceGraph);
        var before = state;
        AlsLocomotionModel.CompleteStandingSourceTiming(input.Identity, new(stride, rate, phase), ref state, ref result);
        Assert.False(AlsLocomotionModel.HasPendingSourceTiming(result));
        Assert.Equal(stride, result.Stride); Assert.Equal(rate, result.PlayRate);
        Assert.Equal(phase, result.AnimationPhase); Assert.Equal(phase, state.AnimationPhase);
        before.AnimationPhase = phase; Assert.Equal(before, state);
        Assert.Throws<InvalidOperationException>(() => AlsLocomotionModel.CompleteStandingSourceTiming(
            input.Identity, new(stride, rate, phase), ref state, ref result));
    }

    [Theory]
    [InlineData(float.NaN, 1f, .2f)]
    [InlineData(-1f, 1f, .2f)]
    [InlineData(1f, float.PositiveInfinity, .2f)]
    [InlineData(1f, -1f, .2f)]
    [InlineData(1f, 1f, float.NaN)]
    [InlineData(1f, 1f, -1f)]
    [InlineData(1f, 1f, 1f)]
    public void InvalidCompletionDoesNotPartiallyModifyTheCandidate(float stride, float rate, float phase)
    {
        var input = P3TestInput.Moving(2);
        var state = new AlsRuntimeState(); var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference, AlsLocomotionTimingPolicy.StandingSourceGraph);
        var before = state; var digest = Digest(result);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionModel.CompleteStandingSourceTiming(
            input.Identity, new(stride, rate, phase), ref state, ref result));
        Assert.Equal(before, state); Assert.Equal(digest, Digest(result));
    }

    [Fact]
    public void WrongFrameCannotCompletePendingTiming()
    {
        var input = P3TestInput.Moving(2);
        var state = new AlsRuntimeState(); var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference, AlsLocomotionTimingPolicy.StandingSourceGraph);
        var before = state; var digest = Digest(result);
        Assert.Throws<InvalidOperationException>(() => AlsLocomotionModel.CompleteStandingSourceTiming(
            new AlsFrameIdentity(input.Identity.FrameId + 1, input.Identity.CharacterId, input.Identity.SlotGeneration),
            new(1, 1, .2f), ref state, ref result));
        Assert.Equal(before, state); Assert.Equal(digest, Digest(result));
    }

    [Fact]
    public void InvalidPolicyDoesNotModifyTheCandidate()
    {
        var input = P3TestInput.Grounded(velocity: new Vector3(0, 0, -2));
        var state = new AlsRuntimeState(); var result = new AlsFrameResult();
        var before = state; var digest = Digest(result);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionModel.Evaluate(input, ref state, ref result,
            P3TestSettings.Reference, (AlsLocomotionTimingPolicy)255));
        Assert.Equal(before, state); Assert.Equal(digest, Digest(result));
    }

    [Theory]
    [InlineData(AlsStance.Standing, false)]
    [InlineData(AlsStance.Crouching, false)]
    [InlineData(AlsStance.Standing, true)]
    [InlineData(AlsStance.Crouching, true)]
    public void CompleteMovementOwnsTimingForEveryStanceAndAirState(AlsStance stance, bool airborne)
    {
        var input = P3TestInput.Moving(2, stance: stance);
        if (airborne) input = input with { Floor = input.Floor with { IsGrounded = 0 } };
        var state = new AlsRuntimeState { AnimationPhase = .37f }; var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, P3TestSettings.Reference, AlsLocomotionTimingPolicy.CompleteMovementGraph);
        Assert.True(AlsLocomotionModel.HasPendingSourceTiming(result));
        Assert.Equal(.37f, state.AnimationPhase);
        var prior = state; var digest = Digest(result);
        Assert.Throws<InvalidOperationException>(() => AlsLocomotionModel.CompleteMovementSourceTiming(
            new AlsFrameIdentity(input.Identity.FrameId + 1, input.Identity.CharacterId, input.Identity.SlotGeneration),
            new(1, 1, .2f), ref state, ref result));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionModel.CompleteMovementSourceTiming(
            input.Identity, new(1, 1, float.NaN), ref state, ref result));
        Assert.Equal(prior, state); Assert.Equal(digest, Digest(result));
        AlsLocomotionModel.CompleteMovementSourceTiming(input.Identity, new(.7f, 1.2f, .43f), ref state, ref result);
        Assert.Equal(.43f, state.AnimationPhase); Assert.Equal(.43f, result.AnimationPhase);
        Assert.Equal(.7f, result.Stride); Assert.Equal(1.2f, result.PlayRate);
        Assert.Throws<InvalidOperationException>(() => AlsLocomotionModel.CompleteMovementSourceTiming(
            input.Identity, new(.7f, 1.2f, .43f), ref state, ref result));
    }

    private static ulong Digest(in AlsFrameResult result)
    {
        var digest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref digest, result);
        return digest;
    }
}
