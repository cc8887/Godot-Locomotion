using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementDetailsRuntimeTests
{
    private static readonly Lazy<AlsRefactoredMovementDetailsResources> Profile = new(() =>
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        return new(MantlingHostFixture.Read("refactored_stance_machines"), catalog);
    });
    private static AlsRefactoredMovementDetailsObservation[] Observations(float weight = 1, bool end = false) =>
        Profile.Value.TimingPlayers.ToArray().Select(p => new AlsRefactoredMovementDetailsObservation(p.PropertyIndex, weight, end ? p.Length : p.Length * .5f)).ToArray();
    private static AlsRefactoredMovementDetailsInput Running(float standing = 1, bool pivot = false) => new("Als.Gait.Running", 1, 1, standing, pivot);

    [Fact]
    public void FirstUpdateTransitionsAndEveryOriginalExitKeepTheirPriorityAndRequests()
    {
        var runtime = new AlsRefactoredMovementDetailsRuntime(Profile.Value); var observations = Observations(end: true);
        var edges = new HashSet<int>(); long frame = 0;
        void Step(AlsRefactoredMovementDetailsInput input, int edge, int expected, bool end = true)
        {
            observations = Observations(end: end); var previous = runtime.CommittedState.CurrentState;
            runtime.Prepare(frame, input, observations, 1f / 60, .7f);
            var update = runtime.Candidate;
            Assert.Equal(expected, update.State.CurrentState); Assert.Equal(1, update.TransitionCount);
            Assert.Equal(previous, Profile.Value.Edges[edge].From); Assert.Equal(expected, Profile.Value.Edges[edge].To);
            Assert.Equal(Profile.Value.Edges[edge].Inertialization ? Profile.Value.Edges[edge].Seconds : -1, update.InertializationSeconds);
            if (Profile.Value.Edges[edge].Inertialization)
            {
                Assert.Equal(0, update.State.Transitions.Count);
                var updates = Enumerable.Range(0, update.UpdateCount).Select(update.GetUpdate).ToArray();
                var current = Assert.Single(updates, u => u.State == expected);
                Assert.True(current.InertializationSync); Assert.Equal(.7f, current.Weight);
                // Native zero-duration inertial entries complete with query
                // alpha 0. Older standard states still update before cleanup.
                var older = updates.Where(u => u.State != expected).ToArray();
                Assert.All(older, u => { Assert.InRange(u.Weight, 0, .7f); Assert.False(u.InertializationSync); });
                if (older.Length > 0) Assert.Equal(.7f, older.Sum(u => u.Weight), 6);
                Assert.Equal(new AlsOverlayInertialRequest(117, Profile.Value.Edges[edge].Seconds, true, AlsTransitionBlend.HermiteCubic), runtime.InertializationRequest);
            }
            else { Assert.True(update.State.Transitions.Count > 0); Assert.Null(runtime.InertializationRequest); }
            edges.Add(edge); runtime.Commit(frame++);
        }
        Step(Running(0), 0, 5); // No first-update skip and no second automatic exit this frame.
        Step(Running(), 10, 2);
        Step(Running(pivot: true), 4, 3);
        Step(Running(pivot: true), 6, 4); // Pivot wins over completed animation in First Pivot.
        Step(Running(pivot: true), 8, 2); // Completed animation wins in Second Pivot.
        Step(Running(pivot: true), 4, 3);
        Step(Running(), 7, 2);
        Step(Running(pivot: true), 4, 3);
        Step(Running(pivot: true), 6, 4);
        Step(Running(pivot: true), 9, 3, end: false);
        Step(Running(), 7, 2);
        Step(new("Als.Gait.Walking", 1, 0, 1, false), 5, 0);
        Step(Running(), 1, 1);
        Step(Running(), 3, 2);
        Step(new("", 1, 0, 1, false), 5, 0);
        Step(Running() with { GroundedAmount = .9f }, 2, 2);
        Assert.Equal(Enumerable.Range(0, 11), edges.Order());
    }

    [Fact]
    public void AutomaticExitUsesRawPriorPlayerTimeAndFirstStrictWeightWinner()
    {
        var runtime = new AlsRefactoredMovementDetailsRuntime(Profile.Value);
        runtime.Prepare(0, Running(0), Observations(), 0); runtime.Commit(0);
        var observations = Observations(0, true);
        runtime.Prepare(1, Running(), observations, 100);
        Assert.Equal(5, runtime.Candidate.State.CurrentState); Assert.Equal(-1, runtime.SelectedPlayerProperties[5]); runtime.Commit(1);
        var first = observations.Length - 4; var second = first + 1; var p = Profile.Value.TimingPlayers[first];
        observations[first] = new(p.PropertyIndex, .25f, MathF.BitDecrement(p.Length));
        observations[second] = observations[second] with { CachedWeight = .25f };
        runtime.Prepare(2, Running(), observations, 1);
        Assert.Equal(p.PropertyIndex, runtime.SelectedPlayerProperties[5]); Assert.Equal(5, runtime.Candidate.State.CurrentState); runtime.Cancel();
        observations[second] = observations[second] with { CachedWeight = MathF.BitIncrement(.25f) };
        runtime.Prepare(2, Running(), observations, 0);
        Assert.Equal(observations[second].PropertyIndex, runtime.SelectedPlayerProperties[5]); Assert.Equal(2, runtime.Candidate.State.CurrentState); runtime.Cancel();
        observations[first] = observations[first] with { CachedWeight = 0 };
        observations[second] = observations[second] with { CachedWeight = float.Epsilon };
        runtime.Prepare(2, Running(), observations, 0); Assert.Equal(2, runtime.Candidate.State.CurrentState); runtime.Commit(2);
        // Initializations clear destination weights. Reentry cannot immediately
        // use a completed old observation to make a second transition.
        runtime.Prepare(3, Running(), observations, 0, reinitialize: true);
        Assert.Equal(1, runtime.Candidate.State.CurrentState); Assert.Equal(-1, runtime.SelectedPlayerProperties[1]);
        Assert.Equal(new[] { 0,1 }, Enumerable.Range(0, runtime.Candidate.InitializationCount).Select(runtime.Candidate.GetInitialization)); runtime.Cancel();
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ContinuousCandidatesCancelRetryAndSkipReentryWithoutPublishingRequests(int hz)
    {
        var runtime = new AlsRefactoredMovementDetailsRuntime(Profile.Value); var clean = new AlsRefactoredMovementDetailsRuntime(Profile.Value);
        var counter = new AlsGraphTraversalCounter(0, 0); var requests = 0; var transitions = 0;
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var phase = frame % 9;
            var input = phase == 0 ? new AlsRefactoredMovementDetailsInput("", 1, 0, 1, false) :
                Running(phase == 1 ? .5f : 1, phase is 3 or 4 or 5 or 7);
            var observations = Observations(end: phase is 2 or 6 or 8); var delta = frame % 13 == 0 ? 0 : 1f / hz;
            var reset = frame == hz;
            if (frame == hz * 2) counter = counter.Next((ulong)frame).Next((ulong)frame);
            runtime.Prepare(frame, input, observations, delta, reinitialize: reset, updateCounter: counter);
            var candidate = runtime.Candidate; var selected = runtime.SelectedPlayerProperties.ToArray(); var committed = runtime.CommittedState;
            runtime.Cancel(); Assert.Equal(committed, runtime.CommittedState);
            Assert.Throws<InvalidOperationException>(() => runtime.Candidate);
            Assert.Throws<InvalidOperationException>(() => runtime.InertializationRequest);
            runtime.Prepare(frame, input, observations, delta, reinitialize: reset, updateCounter: counter);
            clean.Prepare(frame, input, observations, delta, reinitialize: reset, updateCounter: counter);
            Assert.Equal(candidate, runtime.Candidate); Assert.Equal(candidate, clean.Candidate); Assert.Equal(selected, runtime.SelectedPlayerProperties.ToArray());
            Assert.Equal(reset || frame == 0 || frame == hz * 2, candidate.Reinitialized);
            Assert.InRange(candidate.TransitionCount, 0, 1); requests += candidate.InertializationSeconds >= 0 ? 1 : 0; transitions += candidate.TransitionCount;
            runtime.Commit(frame); clean.Commit(frame); counter = counter.Next((ulong)frame + 1);
        }
        Assert.True(requests > hz); Assert.True(transitions > hz);
    }

    [Fact]
    public void InvalidSnapshotsAndForeignFramesDoNotChangeCommittedState()
    {
        var runtime = new AlsRefactoredMovementDetailsRuntime(Profile.Value); var observations = Observations();
        runtime.Prepare(0, Running(), observations, 0); runtime.Commit(0); var committed = runtime.CommittedState;
        foreach (var kind in new[] { "identity", "weight", "time", "previous", "inventory", "input" })
        {
            var bad = observations.ToArray(); var input = Running();
            switch (kind)
            {
                case "identity": bad[0] = bad[0] with { PropertyIndex = bad[1].PropertyIndex }; break;
                case "weight": bad[0] = bad[0] with { CachedWeight = float.NaN }; break;
                case "time": bad[0] = bad[0] with { Time = float.MaxValue }; break;
                case "previous": bad[0] = bad[0] with { PreviousValid = true, PreviousTime = -1 }; break;
                case "inventory": bad = bad[..^1]; break;
                case "input": input = input with { StandingMachineWeight = float.NaN }; break;
            }
            Assert.Throws<ArgumentException>(() => runtime.Prepare(1, input, bad, 0)); Assert.Equal(committed, runtime.CommittedState);
        }
        runtime.Prepare(1, Running(), observations, 0);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(2, Running(), observations, 0));
        Assert.Throws<ArgumentException>(() => runtime.Commit(2)); runtime.Cancel();
        runtime.Prepare(1, Running(), observations, 0); runtime.Commit(1);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1, Running(), observations, 0));
    }
}
