using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsIslandContactCouplingTests
{
    private static AlsJointIsland Create()
    {
        var pose = AlsPrecisePose.Identity; var dynamic = new AlsIslandBody(pose, new(1, AlsDoubleVector.One));
        var free = new AlsAngularAxisSettings(AlsAngularMotion.Free, 0, false, 0, 0, 0, 0);
        return new([dynamic, dynamic, new(pose, default)],
            [new(0, 1, pose with { Position = new(0, 0, 1) }, pose, new(free, free, free, AlsQuaternion.Identity, ConditionMass: false), default)],
            [new(pose with { Position = new(0, 0, -.2) }, default), new(pose with { Position = new(0, 0, .8) }, default), new(pose, default)]);
    }

    [Fact]
    public void ContactAndJointExchangeCorrectionsDuringEveryIteration()
    {
        var island = Create(); var contacts = new ContactPass(); island.StepForceFree(1d / 60, contacts);
        // Each contact pass pushes the lower body to the surface. The joint
        // shares that correction equally with the upper body: the remaining
        // penetration halves at each iteration, rather than staying at 0.1.
        Assert.InRange(System.Math.Abs(island.BodyAt(0).Actor.Position.Z - (-.2 / 256)), 0, 2e-8);
        Assert.InRange(System.Math.Abs(island.BodyAt(1).Actor.Position.Z - (1 - .2 / 256)), 0, 2e-8);
        Assert.Equal(AlsPrecisePose.Identity, island.BodyAt(2).Actor);
        Assert.Equal(8, contacts.PositionCalls); Assert.Equal(2, contacts.VelocityCalls);
        Assert.True(contacts.SawPriorJointCorrection); Assert.True(contacts.SawImplicitVelocity);
    }

    [Fact]
    public void ContactFailureDoesNotPublishAndTheNextGatherCanRecover()
    {
        var island = Create(); var control = Create(); var contacts = new ContactPass { FailOnSecondIteration = true };
        Assert.Throws<InvalidOperationException>(() => island.StepForceFree(1d / 60, contacts));
        for (var i = 0; i < 3; i++) Assert.Equal(control.BodyAt(i), island.BodyAt(i));
        contacts.FailOnSecondIteration = false; island.StepForceFree(1d / 60, contacts); control.StepForceFree(1d / 60, new ContactPass());
        for (var i = 0; i < 3; i++) Assert.Equal(control.BodyAt(i), island.BodyAt(i));
    }

    [Fact]
    public void ContactGatherCannotReenterStepOrResetTheIsland()
    {
        var island = Create(); var contacts = new ContactPass { OnGather = () =>
        {
            Assert.Throws<InvalidOperationException>(() => island.StepForceFree(1d / 60));
            Assert.Throws<InvalidOperationException>(() => island.Reset([]));
        } };
        island.StepForceFree(1d / 60, contacts);
        Assert.Equal(8, contacts.PositionCalls);
    }

    // Analytic one-axis fixture for scheduling only; not a gameplay floor or a
    // replacement for narrow-phase/manifold generation in the Godot world.
    private sealed class ContactPass : IAlsIslandContacts
    {
        private readonly AlsCachedContactManifold _manifold = new(1);
        public bool FailOnSecondIteration, SawPriorJointCorrection, SawImplicitVelocity;
        public int PositionCalls, VelocityCalls;
        public Action? OnGather;
        public void Gather(ReadOnlySpan<AlsPrecisePose> predicted, ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies, double dt)
        {
            OnGather?.Invoke(); PositionCalls = VelocityCalls = 0;
            _manifold.GatherGeometry([new(Vector3.Zero, Vector3.Zero, Vector3.UnitZ, Vector3.Zero, Vector3.Zero, false, false)],
                new(0, 0, 0),
                new(predicted[0], predicted[0].Position, (float)bodies[0].InverseMass.Mass, velocities[0]),
                predicted[0].Rotation, bodies[0].InverseMass.Inertia,
                new(predicted[2], predicted[2].Position, (float)bodies[2].InverseMass.Mass, velocities[2]),
                predicted[2].Rotation, bodies[2].InverseMass.Inertia, new((float)dt, 0, 2000));
        }
        public void SolvePosition(Span<AlsProjectionDelta> bodies, int iteration, int iterationCount)
        {
            if (iteration == 1)
            {
                SawPriorJointCorrection = bodies[1].Position.Z > 0;
                if (FailOnSecondIteration) throw new InvalidOperationException("Injected contact failure.");
            }
            _manifold.SolvePosition(ref bodies[0], ref bodies[2], false); PositionCalls++;
        }
        public void SolveVelocity(Span<AlsProjectionVelocity> bodies, int iteration, int iterationCount, double dt)
        {
            if (iteration == 0) SawImplicitVelocity = bodies[0].Linear.Z > 0 && bodies[1].Linear.Z > 0;
            _manifold.SolveVelocity(ref bodies[0], ref bodies[2], (float)dt, false); VelocityCalls++;
        }
    }
}
