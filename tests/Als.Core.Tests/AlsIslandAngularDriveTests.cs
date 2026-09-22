using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandAngularDriveTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsAngularAxisSettings Free = new(AlsAngularMotion.Free, 0, false, 0, 0, 0, 0);
    private static readonly AlsIslandJoint Joint = new(0, 1, Identity, Identity,
        new(Free, Free, Free, AlsQuaternion.Identity, ConditionMass: false), default);
    private static readonly AlsIslandBody[] Bodies = [new(Identity, default), new(Identity, new(1, AlsDoubleVector.One))];
    private static AlsJointIsland Create(bool sleep = false) => new(Bodies, [Joint],
        [new(Identity, default), new(Identity, default)], sleepSettings: sleep ? [default, new(.01f, .01f, 2)] : []);
    private static AlsIslandAngularDrive Drive(float angle, double stiffness = 250, double damping = 0) =>
        new(0, AlsQuaternion.FromAxisAngle(Vector3.UnitY, angle), new(stiffness, stiffness, stiffness), new(damping, damping, damping));

    [Fact]
    public void ChangingTargetsAndStrengthUseFreshRowsAndRetainSettingsWhenOmitted()
    {
        var actual = Create(); var changed = false;
        for (var frame = 0; frame < 36; frame++)
        {
            var drive = Drive(frame < 12 ? .4f : -.3f, frame < 24 ? 250 : 0, frame >= 24 ? 8 : 0);
            var axis = Free with { DriveStiffness = drive.Stiffness.X, DriveDamping = drive.Damping.X };
            var joint = Joint with { Angular = Joint.Angular with { X = axis, Y = axis, Z = axis, DriveTarget = drive.Target } };
            // Reconstructing an independent solver from the prior committed
            // body state checks that no drive row/lambda leaks across steps.
            var expected = new AlsJointIsland(Bodies, [joint], [actual.BodyAt(0), actual.BodyAt(1)]);
            expected.StepForceFree(1d / 60);
            actual.Step(1d / 60, default, angularDrives: [drive]);
            Assert.Equal(expected.BodyAt(1), actual.BodyAt(1));
            Assert.Equal(joint, actual.JointDefinitionAt(0));
            changed |= actual.BodyAt(1).Velocity.Angular != Vector3.Zero;
        }
        Assert.True(changed);
        var retained = actual.JointDefinitionAt(0);
        var oracle = new AlsJointIsland(Bodies, [retained], [actual.BodyAt(0), actual.BodyAt(1)]);
        oracle.StepForceFree(1d / 60); actual.StepForceFree(1d / 60);
        Assert.Equal(oracle.BodyAt(1), actual.BodyAt(1)); Assert.Equal(retained, actual.JointDefinitionAt(0));
    }

    [Fact]
    public void FailedStepPublishesNeitherDriveNorBodiesAndRetryUsesSameCandidate()
    {
        var actual = Create(); var expected = Create(); var contact = new Failure(); var observer = new Observe();
        actual.SetStepObserver(observer); var input = Drive(.6f);
        Assert.Throws<InvalidOperationException>(() => actual.Step(.02, default, contacts: contact, angularDrives: [input]));
        Assert.Equal(input.Target, observer.Target); Assert.Equal(Joint, actual.JointDefinitionAt(0));
        Assert.Equal(expected.BodyAt(1), actual.BodyAt(1)); Assert.Equal(1, contact.Aborts);
        contact.Fail = false;
        actual.Step(.02, default, contacts: contact, angularDrives: [input]);
        expected.Step(.02, default, angularDrives: [input]);
        Assert.Equal(expected.BodyAt(1), actual.BodyAt(1)); Assert.Equal(input.Target, actual.JointDefinitionAt(0).Angular.DriveTarget);
    }

    [Fact]
    public void InvalidUpdatesCannotPartiallyPublishOrEnableConnectivityOnlyJoint()
    {
        var island = Create(); var good = Drive(.4f);
        foreach (var inputs in new[] { new[] { good, good }, new[] { good, good with { Joint = -1 } },
            new[] { good with { Joint = 1 } }, new[] { good with { Target = default } },
            new[] { good with { Stiffness = new(1, -1, 1) } }, new[] { good with { Damping = new(0, 0, double.NaN) } } })
        {
            Assert.Throws<ArgumentException>(() => island.Step(.02, default, angularDrives: inputs));
            Assert.Equal(Joint, island.JointDefinitionAt(0)); Assert.Equal(new(Identity, default), island.BodyAt(1));
        }
        var free = new AlsJointIsland(Bodies, [Joint with { Angular = default, ConnectivityOnly = true }],
            [new(Identity, default), new(Identity, default)]);
        Assert.Throws<ArgumentException>(() => free.Step(.02, default, angularDrives: [good]));
    }

    [Fact]
    public void SettingsPersistWhileSleepingWithoutWakingAndExplicitWakeAppliesThem()
    {
        var island = Create(sleep: true);
        for (var i = 0; i < 5; i++) island.StepForceFree(.02);
        Assert.True(island.IsSleeping); var state = island.BodyAt(1); var input = Drive(.6f);
        island.Step(.02, default, angularDrives: [input]);
        Assert.True(island.IsSleeping); Assert.Equal(state, island.BodyAt(1));
        Assert.Equal(input.Target, island.JointDefinitionAt(0).Angular.DriveTarget);
        island.RequestWake(); island.StepForceFree(.02);
        Assert.False(island.IsSleeping); Assert.NotEqual(Vector3.Zero, island.BodyAt(1).Velocity.Angular);
    }

    [Fact]
    public void DriveUpdatesBorrowInputsAndDoNotAllocateDuringSteps()
    {
        var island = Create(); var inputs = new[] { Drive(.4f) };
        for (var i = 0; i < 256; i++) island.Step(.01, default, angularDrives: inputs);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++)
        {
            inputs[0] = Drive(i % 2 == 0 ? .2f : -.2f);
            island.Step(.01, default, angularDrives: inputs);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var saved = island.JointDefinitionAt(0); inputs[0] = Drive(1);
        Assert.Equal(saved, island.JointDefinitionAt(0));
    }

    private sealed class Failure : IAlsIslandContacts
    {
        public bool Fail = true; public int Aborts;
        public void Gather(ReadOnlySpan<AlsPrecisePose> p, ReadOnlySpan<AlsProjectionVelocity> v, ReadOnlySpan<AlsIslandBody> b, double dt) { }
        public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int count) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int count, double dt) { }
        public void StageCommit() { if (Fail) throw new InvalidOperationException(); }
        public void Abort() => Aborts++;
    }
    private sealed class Observe : IAlsIslandStepObserver
    {
        public bool Enabled => true; public AlsQuaternion Target;
        public void Begin(double dt, int pi, int vi, ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
            ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> order) => Target = joints[0].Angular.DriveTarget;
        public void Capture(string stage, int iteration, ReadOnlySpan<AlsPrecisePose> predicted,
            ReadOnlySpan<AlsProjectionDelta> delta, ReadOnlySpan<AlsProjectionVelocity> velocity) { }
        public void Complete() { }
    }
}
