using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsStandingCycleLifecycleTests
{
    private static AlsFrameIdentity Id(long frame) => new(frame, 0, 1);
    private static AlsGraphTraversalCounter Counter => new(0, 0);

    [Fact]
    public void UpdateCounterPreservesCycleAcrossFrameGapAndResetsAfterHiddenTraversal()
    {
        var tick=new AlsGraphTraversalCounter(short.MaxValue,1);
        var first=AlsStandingCycleLifecycle.Prepare(default,Id(1),true,false,Counter,tick);
        tick=tick.Next(2);
        var next=AlsStandingCycleLifecycle.Prepare(first.State,Id(1001),true,false,Counter,tick);
        Assert.False(next.Initialized); Assert.False(next.FirstUpdate); Assert.Equal(1,next.State.Epoch);
        var hidden=AlsStandingCycleLifecycle.Prepare(next.State,Id(1002),false,false,Counter);
        Assert.Equal(next.State,hidden.State);
        tick=tick.Next(3).Next(4);
        var resumed=AlsStandingCycleLifecycle.Prepare(hidden.State,Id(1003),true,false,Counter,tick);
        Assert.True(resumed.Initialized); Assert.True(resumed.FirstUpdate); Assert.False(resumed.InitializeSprint);
        Assert.Equal(2,resumed.State.Epoch); Assert.Equal(tick,resumed.State.LastUpdateCounter);
        Assert.Equal(resumed,AlsStandingCycleLifecycle.Prepare(hidden.State,Id(1003),true,false,Counter,tick));
    }

    [Fact]
    public void InactiveCycleDoesNotPretendItsDirectionHasUpdated()
    {
        var idle = AlsStandingCycleLifecycle.Prepare(default, Id(1), false, false, Counter);
        Assert.Equal(default, idle.State); Assert.False(idle.Initialized);
        var first = AlsStandingCycleLifecycle.Prepare(idle.State, Id(100), true, false, Counter);
        Assert.True(first.Initialized); Assert.True(first.FirstUpdate); Assert.True(first.InitializeSprint);
        Assert.Equal(1, first.State.Epoch); Assert.Equal(100, first.State.LastUpdateFrame);
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public void ExplicitInitializationAndDelayedFirstUpdateInitializeOnce(long frame)
    {
        var initial = AlsStandingCycleLifecycle.Prepare(default, Id(1), false, true, Counter);
        Assert.True(initial.State.HasInitialized); Assert.False(initial.State.HasUpdated);
        Assert.True(initial.InitializeSprint); Assert.False(initial.FirstUpdate);
        var update = AlsStandingCycleLifecycle.Prepare(initial.State, Id(frame), true, false, Counter);
        Assert.False(update.Initialized); Assert.True(update.FirstUpdate); Assert.False(update.InitializeSprint);
        Assert.Equal(1, update.State.Epoch);
    }

    [Fact]
    public void RelevanceGapResetsMachineAndSourcesButPreservesMatchingSprintSave()
    {
        var first = AlsStandingCycleLifecycle.Prepare(default, Id(1), true, false, Counter);
        var inactive = AlsStandingCycleLifecycle.Prepare(first.State, Id(2), false, false, Counter);
        Assert.Equal(first.State, inactive.State);
        var reentry = AlsStandingCycleLifecycle.Prepare(inactive.State, Id(3), true, false, Counter);
        Assert.True(reentry.Initialized); Assert.True(reentry.FirstUpdate); Assert.False(reentry.InitializeSprint);
        Assert.Equal(2, reentry.State.Epoch);
        Assert.Equal(reentry, AlsStandingCycleLifecycle.Prepare(inactive.State, Id(3), true, false, Counter));
    }

    [Theory]
    [InlineData(0, 1, false)] [InlineData(1, 0, true)]
    public void SprintInitializationUsesCounterNotGlobalFrame(short counter, ulong frame, bool reset)
    {
        var first = AlsStandingCycleLifecycle.Prepare(default, Id(1), true, false, Counter);
        var again = AlsStandingCycleLifecycle.Prepare(first.State, Id(2), false, true, new(counter, frame));
        Assert.Equal(reset, again.InitializeSprint); Assert.Equal(2, again.State.Epoch);
        Assert.False(again.State.HasUpdated);
    }

    [Fact]
    public void RejectsForeignOwnerAndEpochOverflowWithoutConsumingPrevious()
    {
        var initial = AlsStandingCycleLifecycle.Prepare(default, Id(10), false, true, Counter).State;
        Assert.Throws<ArgumentException>(() => AlsStandingCycleLifecycle.Prepare(initial, new(11, 1, 1), true, false, Counter));
        Assert.Throws<ArgumentException>(() => AlsStandingCycleLifecycle.Prepare(initial, Id(9), true, false, Counter));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsStandingCycleLifecycle.Prepare(initial with { Epoch = long.MaxValue }, Id(11), true, true, Counter));
        Assert.Equal(1, initial.Epoch);
    }
}
