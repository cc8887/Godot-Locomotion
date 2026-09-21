using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandStepObserverTests
{
    private static AlsJointIsland Create() => new(
        [new(AlsPrecisePose.Identity, new(1, AlsDoubleVector.One))], [],
        [new(AlsPrecisePose.Identity, new(new Vector3(1, 2, 3), Vector3.Zero))], 3, 1);

    [Fact]
    public void ObserverSeesActualIterationContractWithoutChangingResult()
    {
        var island = Create(); var control = Create(); var observer = new Observer();
        island.SetStepObserver(observer); island.StepForceFree(.01); control.StepForceFree(.01);
        Assert.Equal(control.BodyAt(0), island.BodyAt(0));
        Assert.Equal((3, 1), observer.Iterations); Assert.True(observer.Completed);
        Assert.Equal(new[] { "position_contacts/0", "position_joints/0", "position_contacts/1", "position_joints/1",
            "position_contacts/2", "position_joints/2", "implicit/0", "velocity_contacts/0", "velocity_joints/0",
            "projection_input/0", "projection/0", "corrected/0" }, observer.Stages);
        Assert.Equal(AlsPrecisePose.Identity, observer.Initial);
        Assert.Equal(control.BodyAt(0).Actor, observer.Predicted);
    }

    [Fact]
    public void DisabledObserverDoesNotReceiveCallbacksOrAllocatePerStep()
    {
        var island = Create(); var observer = new Observer { Enabled = false }; island.SetStepObserver(observer);
        for (var i = 0; i < 100; i++) island.StepForceFree(.01);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) island.StepForceFree(.01);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Empty(observer.Stages); Assert.False(observer.Completed); Assert.Equal(default, observer.Iterations);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("position_joints/1")]
    [InlineData("complete")]
    public void ObserverFailureAbortsPublicationAndAllowsIdenticalRetry(string failure)
    {
        var island = Create(); var control = Create(); var contacts = new Contacts();
        island.SetStepObserver(new Observer { Failure = failure });
        Assert.Throws<InvalidOperationException>(() => island.StepForceFree(.01, contacts));
        Assert.Equal(control.BodyAt(0), island.BodyAt(0)); Assert.Equal(1, contacts.Aborts); Assert.Equal(0, contacts.Commits);
        island.SetStepObserver(null); island.StepForceFree(.01, contacts); control.StepForceFree(.01);
        Assert.Equal(control.BodyAt(0), island.BodyAt(0)); Assert.Equal(1, contacts.Commits);
    }

    [Fact]
    public void ObserverCannotReenterOrReplaceItselfDuringSolve()
    {
        var island = Create(); var observer = new Observer { OnBegin = () =>
        {
            Assert.Throws<InvalidOperationException>(() => island.SetStepObserver(null));
            Assert.Throws<InvalidOperationException>(() => island.StepForceFree(.01));
            Assert.Throws<InvalidOperationException>(() => island.Reset([]));
        } };
        island.SetStepObserver(observer); island.StepForceFree(.01); Assert.True(observer.Completed);
    }

    private sealed class Contacts : IAlsIslandContacts
    {
        public int Aborts, Commits;
        public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities, ReadOnlySpan<AlsIslandBody> bodies, double dt) { }
        public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt) { }
        public void Abort() => Aborts++;
        public void Commit() => Commits++;
    }
    private sealed class Observer : IAlsIslandStepObserver
    {
        public bool Enabled { get; set; } = true;
        public bool Completed;
        public string? Failure;
        public Action? OnBegin;
        public (int, int) Iterations;
        public AlsPrecisePose Initial, Predicted;
        public List<string> Stages = [];
        public void Begin(double dt, int positionIterations, int velocityIterations, ReadOnlySpan<AlsIslandBody> bodies,
            ReadOnlySpan<AlsIslandJoint> joints, ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> jointOrder)
        {
            if (Failure == "begin") throw new InvalidOperationException();
            Iterations = (positionIterations, velocityIterations); Initial = initial[0]; OnBegin?.Invoke();
        }
        public void Capture(string stage, int iteration, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionDelta> delta, ReadOnlySpan<AlsProjectionVelocity> velocity)
        {
            var name = $"{stage}/{iteration}"; if (Failure == name) throw new InvalidOperationException();
            Stages.Add(name); Predicted = predicted[0];
        }
        public void Complete() { if (Failure == "complete") throw new InvalidOperationException(); Completed = true; }
    }
}
