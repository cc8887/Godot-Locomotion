using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPivotNotifyTests
{
    [Fact]
    public void HiddenGlobalFrameClearsMachineWeightOnlyAtCommit()
    {
        var owner = AlsRefactoredCharacterActionTests.Data.Value.Profile.CreateRuntime(4, 1);
        var input = AlsRefactoredStandingHostTests.Input(0, 60) with { MovingSmooth = true };
        var counter = new AlsGraphTraversalCounter(0, 0);
        var id = new AlsFrameIdentity(0, 4, 1);
        var context = new AlsPoseUpdateContext(id, .35f, 1f / 60).WithUpdateCounter(counter);
        owner.BeginGlobal(context, input, true); owner.Standing.Prepare(context, input, new(0, 0), true);
        Assert.Equal(5, owner.Standing.MovementDetailsState); // Initial previous machine weight is zero.
        owner.PostUpdateActions(); owner.Commit(id);
        Assert.Equal(.35f, owner.Standing.CommittedMachineWeight);
        id = new(1, 4, 1); counter = counter.Next(1);
        context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
        owner.BeginGlobal(context, input); owner.PostUpdateActions(); owner.Discard();
        Assert.Equal(.35f, owner.Standing.CommittedMachineWeight);
        owner.BeginGlobal(context, input); owner.PostUpdateActions(); owner.Commit(id);
        Assert.Equal(0, owner.Standing.CommittedMachineWeight);
        id = new(2, 4, 1); counter = counter.Next(2);
        context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
        owner.BeginGlobal(context, input); owner.Standing.Prepare(context, input, new(0, 0));
        Assert.Equal(5, owner.Standing.MovementDetailsState);
        owner.PostUpdateActions(); owner.Commit(id);
        Assert.Equal(1, owner.Standing.CommittedMachineWeight);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ActualDirectionEventsReachNextUpdateAndRollbackTogether(int hz)
    {
        var p = AlsRefactoredLocomotionHostTests.Data.Value;
        var owner = p.CreateRuntime(4, 1);
        var clean = p.CreateRuntime(4, 1);
        var counter = new AlsGraphTraversalCounter(0, 0);
        var notifies = 0; var first = 0; var second = 0; var low = 0; var rejected = 0;
        for (var f = 0; f < hz * 8; f++)
        {
            var phase = f / hz; var t = f / (double)hz;
            var back = t is >= 1 and < 1.1 or >= 2 and < 3 or >= 4 and < 5 or >= 6 and < 7;
            var speed = phase switch { 4 => 200f, 5 => 199f, 6 => 201f, _ => 150f };
            var input = AlsRefactoredStandingHostTests.Input(0, hz);
            input = input with { Movement = input.Movement with { Velocity = new(back ? -speed : speed, 0, 0),
                Speed = speed, VelocityYaw = back ? 180 : 0, PendingUpdate = f == 0 },
                Rest = input.Rest with { Moving = true, PendingUpdate = f == 0 }, MovingSmooth = true };
            var frame = new AlsFrameIdentity(f, 4, 1);
            var context = new AlsPoseUpdateContext(frame, 1, 1f / hz).WithUpdateCounter(counter);
            void Prepare(AlsRefactoredLocomotionHost h)
            {
                h.BeginGlobal(context, new(input, AlsRefactoredLocomotionMode.Grounded, false, true, false, 0, 1, 0), new(0, 0), f == 0);
                h.Prepare(context, f == 0);
                if (f % 13 == 0) h.Evaluate(AlsPrecisePose.Identity);
            }
            Prepare(owner);
            var standing = owner.Actions.Standing;
            var state = standing.MovementDetailsState;
            var prior = standing.PivotActive;
            var events = standing.DirectionUpdate?.EventCount ?? 0;
            owner.PostUpdate();
            Assert.Equal(state, standing.MovementDetailsState); // No reentrant graph update.
            Assert.Equal(events, standing.PivotDispatchCount);
            Assert.Equal(events > 0 ? speed < p.Actions.Standing.MovementSettings.PivotThreshold : prior, standing.PivotActive);
            var pivot = standing.PivotActive;
            owner.Discard(); Prepare(owner); Prepare(clean);
            Assert.Equal(prior, standing.PivotActive);
            owner.PostUpdate(); clean.PostUpdate();
            Assert.Equal(events, standing.PivotDispatchCount);
            Assert.Equal(pivot, standing.PivotActive);
            Assert.Equal(clean.Actions.Standing.PivotActive, pivot);
            Assert.Equal(clean.Actions.Standing.MovementDetailsState, state);
            if (events > 0) { notifies += events; if (pivot) low++; else rejected++; }
            if (state == 3) first++; if (state == 4) second++;
            owner.Commit(frame); clean.Commit(frame); counter = counter.Next((ulong)f + 1);
        }
        Assert.True(notifies >= 5 && low > 0 && rejected >= 2);
        Assert.True(first > 0 && second > 0, $"First={first}, Second={second}");
    }
}
