using System.Numerics;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMovementActionRulesTests
{
    [Theory]
    [InlineData(-6.999f, AlsMovementActionTrigger.None)]
    [InlineData(-7f, AlsMovementActionTrigger.LandingRoll)]
    [InlineData(-9.999f, AlsMovementActionTrigger.LandingRoll)]
    [InlineData(-10f, AlsMovementActionTrigger.LandingRagdoll)]
    [InlineData(-30f, AlsMovementActionTrigger.LandingRagdoll)]
    [InlineData(10f, AlsMovementActionTrigger.None)]
    public void LandingUsesNativeInclusiveThresholdsAndRagdollPriority(float vertical, AlsMovementActionTrigger expected)
    {
        var next = AlsMovementActionRules.Evaluate(false, true, new(2,vertical,0), -45, false);
        Assert.Equal(expected, next.Trigger);
        Assert.Equal(expected == AlsMovementActionTrigger.LandingRagdoll, next.RequiresRagdoll);
        if (expected == AlsMovementActionTrigger.LandingRoll) Assert.Equal(90, next.TargetYawDegrees);
    }

    [Fact]
    public void OnlyModeEdgesTriggerAndOnlyRollingDepartureRequestsRagdoll()
    {
        Assert.Equal(default, AlsMovementActionRules.Evaluate(true, true, new(0,-15,0), 0, true));
        Assert.Equal(default, AlsMovementActionRules.Evaluate(false, false, new(0,-15,0), 0, true));
        Assert.Equal(default, AlsMovementActionRules.Evaluate(true, false, default, 0, false));
        var next = AlsMovementActionRules.Evaluate(true, false, new(2,0,0), 45, true);
        Assert.Equal(AlsMovementActionTrigger.RollingInAir, next.Trigger); Assert.True(next.RequiresRagdoll);
    }

    [Fact]
    public void LandingYawUsesCachedVelocityIncludingTheOneCentimeterThreshold()
    {
        Assert.Equal(70, AlsMovementActionRules.Evaluate(false, true, new(.009f,-8,0), 70, false).TargetYawDegrees);
        Assert.Equal(90, AlsMovementActionRules.Evaluate(false, true, new(.01f,-8,0), 70, false).TargetYawDegrees);
        var input = AlsFrameInput.CreateDefault(new(10,1,1), .01f) with
        { InputDirection = -Vector3.UnitX, ActionParameters = new(1.3f, true, 90) };
        var outcomes = new GodotAls.Core.Events.AlsActionOutcomeBuffer(); outcomes.TryAdd(new(10,0,22,AlsActionResultCode.Accepted));
        Assert.Equal(90, AlsRollingGameplay.ApplyOutcomes(default, input, outcomes).TargetYawDegrees);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void NonfiniteVelocityFailsClosed(float value) =>
        Assert.Throws<ArgumentException>(() => AlsMovementActionRules.Evaluate(false,true,new(0,value,0),0,false));
}
