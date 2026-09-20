using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsSprintInputTests
{
    private static readonly AlsOverlayAlphaPolicy Policy=new(1,0,true,0,1,true,20,.5f,true,0,.25f,0,1);
    [Fact]
    public void AccelerationMapsBeforeAsymmetricInterpolation()
    {
        var first=AlsSprintInput.Prepare(default,true,true,.125f,1f/60,Policy);
        Assert.Equal(.5f,first.State.Alpha);Assert.True(first.ResetFirst&&first.ResetImpulse);
        var rising=AlsSprintInput.Prepare(first.State,false,true,.25f,1f/60,Policy);
        Assert.Equal(2f/3,rising.State.Alpha,6);Assert.False(rising.ResetFirst||rising.ResetImpulse);
        var falling=AlsSprintInput.Prepare(rising.State,false,true,0,1f/60,Policy);
        Assert.Equal(rising.State.Alpha*(1-.5f/60),falling.State.Alpha,6);
        Assert.Equal(1,falling.State.FirstWeight+falling.State.ImpulseWeight,6);
    }
    [Fact]
    public void ChildActivationAndParentInitializationHaveSeparateResets()
    {
        var impulse=AlsSprintInput.Prepare(default,true,true,1,1f/60,Policy);
        Assert.False(impulse.ResetFirst);Assert.True(impulse.ResetImpulse);
        Assert.Equal(1,impulse.State.ImpulseWeight);Assert.Equal(0,impulse.State.FirstWeight);
        var both=AlsSprintInput.Prepare(impulse.State,false,true,0,1f/60,Policy);
        Assert.True(both.ResetFirst);Assert.False(both.ResetImpulse);
        var reinitialized=AlsSprintInput.Prepare(both.State,true,true,0,1f/60,Policy);
        Assert.True(reinitialized.ResetFirst);Assert.False(reinitialized.ResetImpulse);
        Assert.Equal(1,reinitialized.State.FirstWeight);Assert.Equal(0,reinitialized.State.ImpulseWeight);
    }
    [Fact]
    public void UnvisitedFramesHoldHistoryAndDiscardedUpdatesCanBeRetried()
    {
        var committed=AlsSprintInput.Prepare(default,true,true,.125f,1f/60,Policy).State;
        var hidden=AlsSprintInput.Prepare(committed,false,false,1,10,Policy);
        Assert.Equal(committed,hidden.State);Assert.False(hidden.Visited||hidden.ResetFirst||hidden.ResetImpulse);
        var candidate=AlsSprintInput.Prepare(committed,false,true,1,1f/30,Policy);
        Assert.Equal(candidate,AlsSprintInput.Prepare(committed,false,true,1,1f/30,Policy));
        var initializedHidden=AlsSprintInput.Prepare(committed,true,false,1,10,Policy);
        Assert.Equal(default,initializedHidden.State);Assert.False(initializedHidden.Visited);
    }
}
