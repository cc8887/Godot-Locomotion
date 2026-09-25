using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsGroundedStateMachineTests
{
    [Fact]
    public void RefactoredMovementDetailsCannotUseLegacyOrDirectionRuleDomains()
    {
        var states = Enumerable.Range(0, 6).Select(i => new AlsGroundedStateDefinition(false, AlsGroundedCondition.Never,
            i == 0 ? 0 : 1, i == 0 ? 1 : 0, -1, -1, -1)).ToArray();
        var edge = new AlsGroundedEdge(0, 1, AlsGroundedCondition.Never, 0, .1f, AlsTransitionBlend.HermiteCubic, true, -1, -1, -1)
            { RefactoredMovementDetailsRule = AlsRefactoredMovementDetailsRule.Pivot };
        var definition = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.RefactoredMovementDetails, 0, 1, false, states, [edge]);
        var times = new AlsGroundedAutomaticTime[6];
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(definition, default, default, times, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.UpdateRefactoredDirection(definition, default, default, 1, 0, 0));
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.UpdateRefactoredMovementDetails(Model, default, default, new AlsGroundedAutomaticTime[2], 1, 0, 0));
        foreach (var invalid in new[] { edge with { RefactoredMovementDetailsRule = null },
            edge with { RefactoredDirectionRule = new(AlsRefactoredDirectionRuleKind.Forward) },
            edge with { Condition = AlsGroundedCondition.Always },
            edge with { RefactoredMovementDetailsRule = AlsRefactoredMovementDetailsRule.AutomaticRemainingTime } })
            Assert.Throws<ArgumentException>(() => new AlsGroundedMachineDefinition(AlsGroundedMachineKind.RefactoredMovementDetails, 0, 1, false, states, [invalid]));
        Assert.Throws<ArgumentException>(() => new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 1, false, states, [edge]));
        Assert.Throws<ArgumentException>(() => new AlsGroundedMachineDefinition(AlsGroundedMachineKind.RefactoredMovementDetails, 0, 3, false, states, [edge]));
        Assert.Throws<ArgumentException>(() => new AlsGroundedMachineDefinition(AlsGroundedMachineKind.RefactoredMovementDetails, 0, 1, true, states, [edge]));
        var update = AlsGroundedStateMachine.UpdateRefactoredMovementDetails(definition, default, new("", 0, 0, 0, true), times, .5f, 0, 0);
        Assert.Equal(1, update.State.CurrentState); Assert.Equal(.1f, update.InertializationSeconds);
        Assert.Equal(new(1, .5f, true), update.GetUpdate(0));
    }

    private static readonly AlsGroundedRuleInput Idle = new(false, false, false, AlsStance.Standing, true, false, 0, 0);
    private static readonly AlsGroundedMachineDefinition Model = new(AlsGroundedMachineKind.Main, 0, 3, true,
        [new(false, AlsGroundedCondition.Always, 0, 1, 10, 11, 12), new(false, AlsGroundedCondition.Always, 1, 1, 20, 21, 22)],
        [new(0, 1, AlsGroundedCondition.ShouldMove, 0, .3f, AlsTransitionBlend.HermiteCubic, false, 30, 31, 32),
         new(1, 0, AlsGroundedCondition.NotShouldMove, 0, .2f, AlsTransitionBlend.HermiteCubic, false, 40, 41, 42)]);

    [Fact]
    public void NativeTraversalControlsRelevanceIndependentlyOfFrameSerial()
    {
        var times=new AlsGroundedAutomaticTime[2];
        var counter=new AlsGraphTraversalCounter(short.MaxValue,1);
        var first=AlsGroundedStateMachine.Update(Model,default,Idle,times,1,.01f,1,updateCounter:counter);
        counter=counter.Next(2);
        var next=AlsGroundedStateMachine.Update(Model,first.State,Idle with {ShouldMove=true},times,1,.01f,1001,updateCounter:counter);
        Assert.False(next.Reinitialized); Assert.Equal(1,next.State.Transitions.Count);
        Assert.Equal(counter,next.State.LastUpdateCounter);
        Assert.Equal(1001,next.State.LastUpdateSerial);
        var repeat=AlsGroundedStateMachine.Update(Model,next.State,Idle with {ShouldMove=true},times,1,.01f,1002,updateCounter:new(counter.Counter,99));
        Assert.False(repeat.Reinitialized);
        var gap=counter.Next(100).Next(101);
        var reentry=AlsGroundedStateMachine.Update(Model,repeat.State,Idle,times,1,.01f,1003,updateCounter:gap);
        var retry=AlsGroundedStateMachine.Update(Model,repeat.State,Idle,times,1,.01f,1003,updateCounter:gap);
        Assert.True(reentry.Reinitialized); Assert.Equal(0,reentry.State.CurrentState);
        Assert.Equal(.01f,reentry.State.ElapsedSeconds); Assert.Equal(reentry.GetEvent(0),retry.GetEvent(0));
        Assert.Equal(counter.Counter,repeat.State.LastUpdateCounter!.Value.Counter);
        Assert.Throws<ArgumentException>(()=>AlsGroundedStateMachine.Update(Model,next.State,Idle,times,1,.01f,1004));
        Assert.Throws<ArgumentException>(()=>AlsGroundedStateMachine.Update(Model,default,Idle,times,1,.01f,1,updateCounter:default(AlsGraphTraversalCounter)));
        Assert.True(new AlsGraphTraversalCounter(-2,1).WasSynchronizedCounter(new(0,2)));
        Assert.True(default(AlsGraphTraversalCounter).WasSynchronizedCounter(new(0,2)));
    }

    [Fact]
    public void ExplicitInitializationDoesNotUpdatePlayersOrAdvanceTime()
    {
        var initialized = AlsGroundedStateMachine.Initialize(Model);
        Assert.True(initialized.State.HasInitialized);
        Assert.False(initialized.State.HasUpdated);
        Assert.Equal(0, initialized.State.ElapsedSeconds);
        Assert.Equal(0, initialized.UpdateCount);
        Assert.Equal(1, initialized.InitializationCount);
        Assert.Equal(0, initialized.GetInitialization(0));
        Assert.Equal(1, initialized.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 0, 10), initialized.GetEvent(0));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReenteringAnActiveStateHonorsForcedResetWithoutChangingBlendHistory(bool forceReset)
    {
        var states = Model.States.ToArray();
        states[0] = states[0] with { AlwaysResetOnEntry = forceReset };
        var definition = new AlsGroundedMachineDefinition(Model.Kind, 0, 3, true, states, Model.Edges.ToArray());
        var times = new AlsGroundedAutomaticTime[2];
        var initial = AlsGroundedStateMachine.Update(definition, default, Idle, times, 1, .01f, 0);
        var leaving = AlsGroundedStateMachine.Update(definition, initial.State,
            Idle with { ShouldMove = true }, times, 1, .05f, 1);
        Assert.InRange(AlsTransitionStack.Weight(leaving.State.Transitions, 0), .01f, .99f);
        var reentry = AlsGroundedStateMachine.Update(definition, leaving.State, Idle, times, 1, .01f, 2);
        Assert.Equal(forceReset ? 1 : 0, reentry.InitializationCount);
        if (forceReset) Assert.Equal(0, reentry.GetInitialization(0));
        Assert.Equal(1, reentry.ClearCachedWeightStates);
        Assert.Equal(2, reentry.State.Transitions.Count);
        Assert.Equal(1, leaving.State.Transitions.Count);
        var retry = AlsGroundedStateMachine.Update(definition, leaving.State, Idle, times, 1, .01f, 2);
        Assert.Equal(reentry.InitializationCount, retry.InitializationCount);
        Assert.Equal(reentry.State.Transitions.Latest, retry.State.Transitions.Latest);
    }

    [Theory]
    [InlineData(0)] [InlineData(1000)]
    public void FirstUpdateAfterExplicitInitializationDoesNotRepeatEntryOrLoseFirstBlendPolicy(long serial)
    {
        var initialized = AlsGroundedStateMachine.Initialize(Model);
        var update = AlsGroundedStateMachine.Update(Model, initialized.State, Idle with { ShouldMove = true },
            new AlsGroundedAutomaticTime[2], 1, .01f, serial);
        Assert.False(update.Reinitialized);
        Assert.True(update.State.HasUpdated);
        Assert.Equal(1, update.State.CurrentState);
        Assert.Equal(0, update.State.Transitions.Count);
        Assert.Equal(.01f, update.State.ElapsedSeconds);
        Assert.Equal(1, update.InitializationCount);
        Assert.Equal(1, update.GetInitialization(0));
        Assert.Equal(2, update.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.StateExited, 0, 11), update.GetEvent(0));
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 1, 20), update.GetEvent(1));
        Assert.False(initialized.State.HasUpdated);
    }

    [Fact]
    public void InitializedMachineRejectsAnotherKindBeforeItsFirstUpdate()
    {
        var initialized = AlsGroundedStateMachine.Initialize(Model);
        var other = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Stop, 0, 3, true,
            Model.States.ToArray(), Model.Edges.ToArray());
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(other, initialized.State, Idle,
            new AlsGroundedAutomaticTime[2], 1, .01f, 100));
    }

    [Fact]
    public void ExplicitInitializationDoesNotAllowStaleAutomaticObservationsOnFirstUpdate()
    {
        var model = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 1, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1), new(false, AlsGroundedCondition.Always, 1, 0, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.Automatic, -1, .3f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        var initial = AlsGroundedStateMachine.Initialize(model);
        AlsGroundedAutomaticTime[] times = [new(true, 1, 1, false, false, 0, 0), default];
        var update = AlsGroundedStateMachine.Update(model, initial.State, Idle, times, 1, .01f, 100);
        Assert.Equal(0, update.TransitionCount);
        Assert.Equal(0, update.InitializationCount);
        var next = AlsGroundedStateMachine.Update(model, update.State, Idle, times, 1, .01f, 101);
        Assert.Equal(1, next.State.CurrentState);
        var gap = AlsGroundedStateMachine.Update(model, next.State, Idle, times, 1, .01f, 103);
        Assert.True(gap.Reinitialized);
        Assert.Equal(0, gap.State.CurrentState);
    }

    [Fact]
    public void FirstUpdateSkipsBlendButRetainsStateEventsAndDoesNotEmitTransitionStart()
    {
        var update = AlsGroundedStateMachine.Update(Model, default, Idle with { ShouldMove = true }, new AlsGroundedAutomaticTime[2], 1, .01f, 0);
        Assert.Equal(1, update.State.CurrentState);
        Assert.Equal(0, update.State.Transitions.Count);
        Assert.Equal(3, update.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 0, 10), update.GetEvent(0));
        Assert.Equal(new(AlsGroundedEventKind.StateExited, 0, 11), update.GetEvent(1));
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 1, 20), update.GetEvent(2));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void InterruptedCandidateRetriesAreIndependentAndEventOrderIsNative(int hz)
    {
        var times = new AlsGroundedAutomaticTime[2];
        var initial = AlsGroundedStateMachine.Update(Model, default, Idle, times, 1, 1f / hz, 0).State;
        var first = AlsGroundedStateMachine.Update(Model, initial, Idle with { ShouldMove = true }, times, 1, .1f, 1);
        var second = AlsGroundedStateMachine.Update(Model, first.State, Idle, times, .7f, 1f / hz, 2);
        var retry = AlsGroundedStateMachine.Update(Model, first.State, Idle, times, .7f, 1f / hz, 2);
        Assert.Equal(0, second.State.CurrentState);
        Assert.Equal(2, second.State.Transitions.Count);
        Assert.Equal(1, second.State.GetActiveEdge(1));
        Assert.Equal(1, first.State.Transitions.Count);
        Assert.Equal(4, second.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.TransitionInterrupted, 0, 32), second.GetEvent(0));
        Assert.Equal(new(AlsGroundedEventKind.StateExited, 1, 21), second.GetEvent(1));
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 0, 10), second.GetEvent(2));
        Assert.Equal(new(AlsGroundedEventKind.TransitionStarted, 1, 40), second.GetEvent(3));
        for (var i = 0; i < second.EventCount; i++) Assert.Equal(second.GetEvent(i), retry.GetEvent(i));
        for (var i = 0; i < second.State.Transitions.Count; i++) Assert.Equal(second.State.Transitions.GetTransition(i), retry.State.Transitions.GetTransition(i));
        var complete = AlsGroundedStateMachine.Update(Model, second.State, Idle, times, 1, 1, 3);
        Assert.Equal(0, complete.State.Transitions.Count);
        Assert.Equal(2, complete.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.TransitionEnded, 1, 41), complete.GetEvent(0));
        Assert.Equal(new(AlsGroundedEventKind.StateFullyBlended, 0, 12), complete.GetEvent(1));
    }

    [Fact]
    public void RelevanceGapReinitializesAndDoesNotPublishOldTransitionEvents()
    {
        var times = new AlsGroundedAutomaticTime[2];
        var state = AlsGroundedStateMachine.Update(Model, default, Idle with { ShouldMove = true }, times, 1, .01f, 0).State;
        var update = AlsGroundedStateMachine.Update(Model, state, Idle, times, 1, .01f, 10);
        Assert.True(update.Reinitialized);
        Assert.Equal(0, update.State.CurrentState);
        Assert.Equal(1, update.EventCount);
        Assert.Equal(new(AlsGroundedEventKind.StateEntered, 0, 10), update.GetEvent(0));
        Assert.Equal(.01f, update.State.ElapsedSeconds);
    }

    [Theory]
    [InlineData(false, 0f, 0f, .1f)]
    [InlineData(true, .95f, .1f, 0f)]
    public void AutomaticRulesUseRawTimeLoopDeltaAndShortenCrossfade(bool loop, float previousTime, float tickDelta, float expectedDuration)
    {
        var definition = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1), new(false, AlsGroundedCondition.Always, 1, 0, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.Automatic, -1, .3f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        var times = new AlsGroundedAutomaticTime[2];
        var initial = AlsGroundedStateMachine.Update(definition, default, Idle, times, 1, .01f, 0);
        times[0] = new(true, 1, loop ? .05f : .9f, loop, loop, previousTime, tickDelta);
        var update = AlsGroundedStateMachine.Update(definition, initial.State, Idle, times, 1, .01f, 1);
        Assert.Equal(1, update.State.CurrentState);
        Assert.Equal(expectedDuration, update.State.Transitions.Latest.Duration, 5);
    }

    [Fact]
    public void AutomaticExitCannotConsumeCachedWeightClearedBySameFrameEntry()
    {
        var definition = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1), new(false, AlsGroundedCondition.Always, 1, 1, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.ShouldMove, 0, .3f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(1, 0, AlsGroundedCondition.Automatic, -1, .2f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        var times = new[] { default(AlsGroundedAutomaticTime), new(true, 1, 1, false, false, 0, 0) };
        var update = AlsGroundedStateMachine.Update(definition, default, Idle with { ShouldMove = true }, times, 1, .01f, 0);
        Assert.Equal(1, update.TransitionCount);
        Assert.Equal(1, update.State.CurrentState);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-.1f)]
    public void InvalidDeltaRejectedWithoutChangingPrevious(float delta)
    {
        var state = default(AlsGroundedMachineState);
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(Model, state, Idle, new AlsGroundedAutomaticTime[2], 1, delta, 0));
        Assert.False(state.HasUpdated);
    }

    [Fact]
    public void DuplicateSerialAndInvalidObservationsAreRejected()
    {
        var times = new AlsGroundedAutomaticTime[2];
        var state = AlsGroundedStateMachine.Update(Model, default, Idle, times, 1, .01f, 1).State;
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(Model, state, Idle, times, 1, .01f, 1));
        times[0] = new(true, 1, 2, false, false, 0, 0);
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(Model, state, Idle, times, 1, .01f, 2));
    }

    [Fact]
    public void InertialCompletionKeepsOlderStateUpdatesBeforeCleanup()
    {
        var definition = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Main, 0, 1, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 1, 1, -1, -1, -1),
             new(false, AlsGroundedCondition.Always, 2, 0, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.ShouldMove, 0, .3f, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
             new(1, 2, AlsGroundedCondition.RotateRight, 0, .1f, AlsTransitionBlend.HermiteCubic, true, -1, -1, -1)]);
        var times = new AlsGroundedAutomaticTime[3];
        var initial = AlsGroundedStateMachine.Update(definition, default, Idle, times, 1, .01f, 0);
        var blend = AlsGroundedStateMachine.Update(definition, initial.State, Idle with { ShouldMove = true }, times, 1, .1f, 1);
        var inertial = AlsGroundedStateMachine.Update(definition, blend.State, Idle with { RotateRight = true }, times, .7f, .01f, 2);
        Assert.Equal(0, inertial.State.Transitions.Count);
        Assert.Equal(.1f, inertial.InertializationSeconds);
        Assert.Equal(3, inertial.UpdateCount);
        Assert.Equal(0, inertial.GetUpdate(0).State);
        Assert.Equal(1, inertial.GetUpdate(1).State);
        Assert.Equal(new(2, .7f, true), inertial.GetUpdate(2));
    }

    [Fact]
    public void RecordedMachineWeightAndKindBelongToCandidate()
    {
        var times = new AlsGroundedAutomaticTime[2];
        var original = AlsGroundedStateMachine.Update(Model, default, Idle, times, .25f, .01f, 0);
        var next = AlsGroundedStateMachine.Update(Model, original.State, Idle, times, .75f, .01f, 1);
        Assert.Equal(.25f, original.State.RecordedWeight);
        Assert.Equal(.75f, next.State.RecordedWeight);
        var other = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Stop, 0, 1, true,
            Model.States.ToArray(), Model.Edges.ToArray());
        Assert.Throws<ArgumentException>(() => AlsGroundedStateMachine.Update(other, original.State, Idle, times, 1, .01f, 1));
    }

    [Fact]
    public void UpdatesAndCandidateRetriesAllocateNoManagedMemory()
    {
        var state = default(AlsGroundedMachineState);
        var times = new AlsGroundedAutomaticTime[2];
        for (var i = 0; i < 1000; i++) state = AlsGroundedStateMachine.Update(Model, state, Idle with { ShouldMove = i % 20 < 10 }, times, 1, 1f / 60, i).State;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1000; i < 3000; i++)
        {
            var input = Idle with { ShouldMove = i % 20 < 10 };
            _ = AlsGroundedStateMachine.Update(Model, state, input, times, 1, 1f / 60, i);
            state = AlsGroundedStateMachine.Update(Model, state, input, times, 1, 1f / 60, i).State;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
}
