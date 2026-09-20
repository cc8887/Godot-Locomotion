using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCharacterRotationFeedbackTests
{
    private static AlsFrameIdentity Current => new(10, 4, 2);
    private static AlsCharacterRotationFeedback Previous => new(new(9, 4, 2), 15, -.4f, true, true, AlsTimelineAction.None);

    [Fact]
    public void EntirelyDefaultFeedbackIsTheOnlyUninitializedPayload()
    {
        default(AlsCharacterRotationFeedback).ValidateForFrame(Current);
        default(AlsCharacterRotationFeedback).ValidateForFrame(new AlsFrameIdentity(0, 4, 2));
        var invalid = new[] {
            default(AlsCharacterRotationFeedback) with { YawOffset = 1 },
            default(AlsCharacterRotationFeedback) with { RotationAmount = 1 },
            default(AlsCharacterRotationFeedback) with { YawOffsetPresent = true },
            default(AlsCharacterRotationFeedback) with { RotationAmountPresent = true },
            default(AlsCharacterRotationFeedback) with { Action = AlsTimelineAction.Rolling },
        };
        foreach (var feedback in invalid) Assert.Throws<ArgumentException>(() => feedback.ValidateForFrame(Current));
    }

    [Theory]
    [InlineData(AlsTimelineAction.None)]
    [InlineData(AlsTimelineAction.Rolling)]
    [InlineData(AlsTimelineAction.Mantling)]
    [InlineData(AlsTimelineAction.Ragdolling)]
    [InlineData(AlsTimelineAction.GettingUp)]
    public void AcceptsFinitePastFeedbackWithEverySupportedAction(AlsTimelineAction action)
    {
        var feedback = Previous with { Action = action };
        feedback.ValidateForFrame(Current);
        // Missing curves may retain finite values; callers consume absence as zero.
        (feedback with { YawOffsetPresent = false, RotationAmountPresent = false }).ValidateForFrame(Current);
        Assert.Equal(15, feedback.YawOffset); Assert.Equal(-.4f, feedback.RotationAmount);
    }

    public static IEnumerable<object[]> NonfiniteCurves()
    {
        foreach (var yaw in new[] { false, true })
        foreach (var present in new[] { false, true })
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            yield return new object[] { yaw, present, value };
    }

    [Theory]
    [MemberData(nameof(NonfiniteCurves))]
    public void RejectsNonfiniteValuesEvenWhenTheirCurveIsAbsent(bool yaw, bool present, float value)
    {
        var feedback = yaw ? Previous with { YawOffset = value, YawOffsetPresent = present }
            : Previous with { RotationAmount = value, RotationAmountPresent = present };
        Assert.Throws<ArgumentException>(() => feedback.ValidateForFrame(Current));
    }

    [Theory]
    [InlineData(5)] [InlineData(255)]
    public void RejectsUnsupportedActionBeforeItsBranchIsConsumed(byte value)
    {
        var feedback = Previous with { Action = (AlsTimelineAction)value };
        Assert.Throws<ArgumentException>(() => feedback.ValidateForFrame(Current));
    }

    [Theory]
    [InlineData(9, 5, 2)]
    [InlineData(9, 4, 1)]
    [InlineData(10, 4, 2)]
    [InlineData(11, 4, 2)]
    public void RejectsDifferentOwnersAndNonpastFrames(long frame, uint character, uint generation)
    {
        var feedback = Previous with { Identity = new AlsFrameIdentity(frame, character, generation) };
        Assert.Throws<ArgumentException>(() => feedback.ValidateForFrame(Current));
    }

    [Fact]
    public void RequiresAnInitializedConsumerIdentity()
    {
        Assert.Throws<ArgumentException>(() => Previous.ValidateForFrame(default));
        Assert.Throws<ArgumentException>(() => default(AlsCharacterRotationFeedback).ValidateForFrame(default));
    }
}
