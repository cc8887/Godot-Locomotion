using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLocomotionDetailMachineTests
{
    private static readonly AlsDetailMachineInput Running = new(AlsGait.Running, 2, false, 1, 1, 1, 1);
    private static readonly AlsDetailTransitionDefinition[] Edges =
    [
        new(AlsDetailState.Walking, AlsDetailState.RunConduit, .2f, AlsDetailTransitionLogic.Standard, AlsDetailCondition.RunningGaitAndGroundedFull, 1),
        new(AlsDetailState.RunConduit, AlsDetailState.RunStart, .1f, AlsDetailTransitionLogic.Inertialization, AlsDetailCondition.DetailNotFull, 1),
        new(AlsDetailState.RunConduit, AlsDetailState.WalkRun, .1f, AlsDetailTransitionLogic.Inertialization, AlsDetailCondition.DetailFull, 1),
        new(AlsDetailState.WalkRun, AlsDetailState.Running, .1f, AlsDetailTransitionLogic.Standard, AlsDetailCondition.RelevantAnimationFinished, 0),
        new(AlsDetailState.RunStart, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Standard, AlsDetailCondition.RelevantAnimationFinished, 0),
        new(AlsDetailState.Running, AlsDetailState.FirstPivot, .1f, AlsDetailTransitionLogic.Inertialization, AlsDetailCondition.Pivot, 0),
        new(AlsDetailState.FirstPivot, AlsDetailState.SecondPivot, .1f, AlsDetailTransitionLogic.Inertialization, AlsDetailCondition.PivotAfterElapsed, .1f),
        new(AlsDetailState.SecondPivot, AlsDetailState.FirstPivot, .1f, AlsDetailTransitionLogic.Inertialization, AlsDetailCondition.PivotAfterElapsed, .1f),
    ];

    [Theory]
    [InlineData(0)] [InlineData(1000)]
    public void ExplicitInitializeSurvivesSuppressionWithoutRepeatingWalkingInitialization(long serial)
    {
        var initial = AlsLocomotionDetailMachine.Initialize();
        Assert.True(initial.State.HasInitialized);
        Assert.False(initial.State.HasUpdated);
        Assert.Equal(0, initial.UpdateCount);
        Assert.Equal(0, initial.State.ElapsedSeconds);
        Assert.Equal(1, initial.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, initial.GetInitialization(0));
        var update = AlsLocomotionDetailMachine.Update(Edges, initial.State, Running with { DetailWeight = .5f }, .01f, serial);
        Assert.False(update.Reinitialized);
        Assert.Equal(1, update.InitializationCount);
        Assert.Equal(AlsDetailState.RunStart, update.GetInitialization(0));
        Assert.Equal(.01f, update.State.ElapsedSeconds);
        Assert.False(initial.State.HasUpdated);
        var gap = AlsLocomotionDetailMachine.Update(Edges, update.State, Running with { DetailWeight = .5f }, .01f, serial + 2);
        Assert.True(gap.Reinitialized);
        Assert.Equal(2, gap.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, gap.GetInitialization(0));
    }

    [Fact]
    public void DetailTransitionSurvivesFrameGapButReinitializesOnSkippedAnimationUpdate()
    {
        var counter=new AlsGraphTraversalCounter(-2,1);
        var first=AlsLocomotionDetailMachine.Update(Edges,default,Running with {DetailWeight=.5f},.01f,1,counter);
        counter=counter.Next(2);
        var next=AlsLocomotionDetailMachine.Update(Edges,first.State,Running with {RelevantTimeRemainingSeconds=0},.01f,1001,counter);
        Assert.False(next.Reinitialized); Assert.Equal(AlsDetailState.Running,next.State.CurrentState);
        Assert.Equal(1,next.State.Transitions.Count); Assert.Equal(counter,next.State.LastUpdateCounter);
        var gap=counter.Next(3).Next(4);
        var reset=AlsLocomotionDetailMachine.Update(Edges,next.State,Running with {DetailWeight=.5f},.01f,1002,gap);
        Assert.True(reset.Reinitialized); Assert.Equal(AlsDetailState.RunStart,reset.State.CurrentState);
        Assert.Equal(AlsDetailState.Walking,reset.GetInitialization(0));
        Assert.Equal(1001,next.State.LastUpdateSerial);
        Assert.Throws<ArgumentException>(()=>AlsLocomotionDetailMachine.Update(Edges,next.State,Running,.01f,1003));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void FirstUpdateUsesTerminalConduitEdgeAndDoesNotAdvanceSecondTransition(int hz)
    {
        var input = Running with { RelevantTimeRemainingSeconds = 0, DetailWeight = .5f, ContextWeight = .5f };
        var update = AlsLocomotionDetailMachine.Update(Edges, default, input, 1f / hz, 0);
        Assert.True(update.Reinitialized);
        Assert.Equal(AlsDetailState.RunStart, update.State.CurrentState);
        Assert.Equal(.1f, update.InertializationSeconds);
        Assert.Equal(0, update.State.Transitions.Count);
        Assert.Equal(1f / hz, update.State.ElapsedSeconds);
        Assert.Equal((byte)(1 | 1 << (int)AlsDetailState.RunStart), update.InitializeStates);
        Assert.Equal(update.InitializeStates, update.ClearCachedWeightStates);
        Assert.Equal(2, update.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, update.GetInitialization(0));
        Assert.Equal(AlsDetailState.RunStart, update.GetInitialization(1));
        Assert.Equal(1, update.UpdateCount);
        Assert.Equal(new(AlsDetailState.RunStart, .5f, true), update.GetUpdate(0));
    }

    [Fact]
    public void ElapsedGateUsesPreviousUpdateAndStrictBoundary()
    {
        var state = ReachPivot();
        Assert.Equal(.1f, state.ElapsedSeconds);
        var update = AlsLocomotionDetailMachine.Update(Edges, state, Running with { Pivot = true }, .001f, 3);
        Assert.Equal(AlsDetailState.FirstPivot, update.State.CurrentState);
        update = AlsLocomotionDetailMachine.Update(Edges, update.State, Running with { Pivot = true }, .01f, 4);
        Assert.Equal(AlsDetailState.SecondPivot, update.State.CurrentState);
        Assert.Equal(.01f, update.State.ElapsedSeconds);
        Assert.Equal(.1f, update.InertializationSeconds);
    }

    [Fact]
    public void InertialCompletionStillUpdatesOlderStandardStatesBeforePruning()
    {
        var first = AlsLocomotionDetailMachine.Update(Edges, default, Running with { DetailWeight = .5f }, .01f, 0);
        var second = AlsLocomotionDetailMachine.Update(Edges, first.State, Running with { RelevantTimeRemainingSeconds = 0 }, .01f, 1);
        Assert.Equal(1, second.State.Transitions.Count);
        var third = AlsLocomotionDetailMachine.Update(Edges, second.State, Running with { Pivot = true, DetailWeight = .7f, ContextWeight = .7f }, .01f, 2);
        Assert.Equal(0, third.State.Transitions.Count);
        Assert.Equal(3, third.UpdateCount);
        Assert.Equal(AlsDetailState.RunStart, third.GetUpdate(0).State);
        Assert.Equal(AlsDetailState.Running, third.GetUpdate(1).State);
        Assert.False(third.GetUpdate(0).InertializationSync);
        Assert.InRange(third.GetUpdate(0).Weight + third.GetUpdate(1).Weight, .69999f, .70001f);
        Assert.Equal(new(AlsDetailState.FirstPivot, .7f, true), third.GetUpdate(2));
    }

    [Fact]
    public void ReentryOfWeightedStateClearsWeightsButDoesNotResetPlayers()
    {
        AlsDetailTransitionDefinition[] edges =
        [
            new(AlsDetailState.Walking, AlsDetailState.Running, .2f, AlsDetailTransitionLogic.Standard, AlsDetailCondition.RunningGaitAndGroundedFull, 1),
            new(AlsDetailState.Running, AlsDetailState.Walking, .1f, AlsDetailTransitionLogic.Standard, AlsDetailCondition.WalkingAndGaitCurveBelow, 1.2f),
        ];
        var first = AlsLocomotionDetailMachine.Update(edges, default, Running, .02f, 0);
        var second = AlsLocomotionDetailMachine.Update(edges, first.State, Running with { Gait = AlsGait.Walking, WeightGait = 1 }, .001f, 1);
        Assert.Equal(AlsDetailState.Walking, second.State.CurrentState);
        Assert.Equal(0, second.InitializeStates);
        Assert.Equal(0, second.InitializationCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => second.GetInitialization(0));
        Assert.Equal(1, second.ClearCachedWeightStates);
        Assert.Equal(2, second.State.Transitions.Count);
        Assert.Equal(2, second.UpdateCount);
        Assert.Equal(.001f, second.State.ElapsedSeconds);
    }

    [Fact]
    public void RelevanceUsesTraversalGapNotWeightZeroAndSameSerialIsAllowed()
    {
        var state = ReachPivot();
        var zeroWeight = AlsLocomotionDetailMachine.Update(Edges, state, Running with { DetailWeight = 0, ContextWeight = 0 }, .01f, 3);
        Assert.False(zeroWeight.Reinitialized);
        Assert.Equal(0, zeroWeight.GetUpdate(0).Weight);
        var same = AlsLocomotionDetailMachine.Update(Edges, zeroWeight.State, Running, 0, 3);
        Assert.False(same.Reinitialized);
        Assert.Equal(zeroWeight.State.ElapsedSeconds, same.State.ElapsedSeconds);
        var gap = AlsLocomotionDetailMachine.Update(Edges, same.State, Running, .01f, 5);
        Assert.True(gap.Reinitialized);
        Assert.Equal(AlsDetailState.WalkRun, gap.State.CurrentState);
        Assert.Equal(.01f, gap.State.ElapsedSeconds);
        Assert.Equal(2, gap.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, gap.GetInitialization(0));
        Assert.Equal(AlsDetailState.WalkRun, gap.GetInitialization(1));
    }

    [Fact]
    public void RuleReadBufferWeightIsIndependentOfCurrentUpdateContextWeight()
    {
        var update = AlsLocomotionDetailMachine.Update(Edges, default,
            Running with { DetailWeight = 0, ContextWeight = .8f }, .01f, 0);
        Assert.Equal(AlsDetailState.RunStart, update.State.CurrentState);
        Assert.Equal(.8f, update.GetUpdate(0).Weight);
    }

    [Fact]
    public void RelevantPlayerUsesStrictMaximumCachedWeightAndFirstBakedTie()
    {
        Assert.Equal(float.MaxValue, AlsLocomotionDetailMachine.RelevantTimeRemaining([], out var none));
        Assert.Equal(-1, none);
        Assert.Equal(float.MaxValue, AlsLocomotionDetailMachine.RelevantTimeRemaining([new(1, 1, 0)], out none));
        Assert.Equal(-1, none);
        Assert.Equal(.75f, AlsLocomotionDetailMachine.RelevantTimeRemaining([new(1, .25f, .5f), new(1, 1, .5f)], out var first));
        Assert.Equal(0, first);
        Assert.Equal(0, AlsLocomotionDetailMachine.RelevantTimeRemaining([new(1, .25f, .5f), new(1, 1, .50001f)], out var second));
        Assert.Equal(1, second);
    }

    [Fact]
    public void CandidateCopiesRetryDeterministicallyWithoutAllocations()
    {
        var state = ReachPivot();
        var snapshot = state;
        var candidate = AlsLocomotionDetailMachine.Update(Edges, state, Running with { Pivot = true }, .02f, 3);
        var retry = AlsLocomotionDetailMachine.Update(Edges, state, Running with { Pivot = true }, .02f, 3);
        Assert.Equal(candidate.State.ElapsedSeconds, retry.State.ElapsedSeconds);
        Assert.Equal(candidate.State.CurrentState, retry.State.CurrentState);
        Assert.Equal(candidate.State.Transitions.Count, retry.State.Transitions.Count);
        for (var i = 0; i < candidate.UpdateCount; i++) Assert.Equal(candidate.GetUpdate(i), retry.GetUpdate(i));
        Assert.Equal(candidate.InitializationCount, retry.InitializationCount);
        for (var i = 0; i < candidate.InitializationCount; i++) Assert.Equal(candidate.GetInitialization(i), retry.GetInitialization(i));
        Assert.Equal(snapshot.ElapsedSeconds, state.ElapsedSeconds);
        for (var i = 0; i < 64; i++) AlsLocomotionDetailMachine.Update(Edges, state, Running, .01f, 3);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) AlsLocomotionDetailMachine.Update(Edges, state, Running, .01f, 3);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void InitializationJournalRejectsReadsOutsideActualEntries(int index)
    {
        var update = AlsLocomotionDetailMachine.Update(Edges, default, Running, .01f, 0);
        Assert.Equal(2, update.InitializationCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => update.GetInitialization(index));
    }

    [Fact]
    public void WalkingInitializationIsEmittedOnlyOnFirstUpdateOrRelevanceGap()
    {
        var walking = Running with { Gait = AlsGait.Walking, WeightGait = 1 };
        var first = AlsLocomotionDetailMachine.Update(Edges, default, walking, .01f, 0);
        Assert.Equal(1, first.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, first.GetInitialization(0));
        var next = AlsLocomotionDetailMachine.Update(Edges, first.State, walking, .01f, 1);
        Assert.Equal(0, next.InitializationCount);
        var gap = AlsLocomotionDetailMachine.Update(Edges, next.State, walking, .01f, 3);
        Assert.Equal(1, gap.InitializationCount);
        Assert.Equal(AlsDetailState.Walking, gap.GetInitialization(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidDeltaAndObservationAreRejected(float value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsLocomotionDetailMachine.Update(Edges, default, Running, value, 0));
        Assert.Throws<ArgumentException>(() => AlsLocomotionDetailMachine.RelevantTimeRemaining([new(1, value, .5f)], out _));
    }

    private static AlsDetailMachineState ReachPivot()
    {
        var first = AlsLocomotionDetailMachine.Update(Edges, default, Running, .01f, 0);
        var second = AlsLocomotionDetailMachine.Update(Edges, first.State, Running with { RelevantTimeRemainingSeconds = 0 }, .2f, 1);
        return AlsLocomotionDetailMachine.Update(Edges, second.State, Running with { Pivot = true }, .1f, 2).State;
    }
}
