using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCachedContactTests
{
    private static readonly AlsQuaternion Q = AlsQuaternion.Identity;
    private static readonly AlsJointInverseMass Mass = new(1, AlsDoubleVector.One);
    private static readonly AlsContactPointInput Point = new(Vector3.Zero, Vector3.Zero, Vector3.UnitZ,
        Vector3.UnitX, Vector3.UnitY, new(-.2f, 0, 0), 0);

    [Fact]
    public void UnilateralContactCanReleaseAccumulationWithoutPullingBodiesTogether()
    {
        var row = new AlsCachedContactPoint(Point, new(0, 0, 0), Q, Mass, Q, default);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
        row.SolvePositionNormal(ref a, ref b); Assert.Equal(.2f, row.PushOut.X); Assert.Equal(.2f, a.Position.Z);
        a = a with { Position = new(0, 0, 1) }; // another constraint separates this body
        row.SolvePositionNormal(ref a, ref b);
        Assert.Equal(0, row.PushOut.X); Assert.Equal(.8f, a.Position.Z);
        row.SolvePositionNormal(ref a, ref b); Assert.Equal(.8f, a.Position.Z); Assert.Equal(default, b);
    }

    [Fact]
    public void VelocitySolveRemovesTheSeparatingSpeedIntroducedByPushout()
    {
        var row = new AlsCachedContactPoint(Point, new(0, 0, 0), Q, Mass, Q, default);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
        row.SolvePositionNormal(ref a, ref b);
        var va = AlsCachedJoint.AddImplicitVelocity(new(new(0, 0, -1), Vector3.Zero), a, 1d / 60, true);
        var vb = default(AlsProjectionVelocity); Assert.InRange(va.Linear.Z, 10.99f, 11.01f);
        row.SolveVelocity(ref va, ref vb, 1f / 60, false);
        Assert.InRange(MathF.Abs(va.Linear.Z), 0, 1e-6f); Assert.True(row.Impulse.X < 0);
    }

    [Fact]
    public void SlidingClampsToDynamicConeAndVelocityFrictionIncludesPositionImpulse()
    {
        var row = new AlsCachedContactPoint(Point with { Error = new(-.2f, 2, 0) }, new(.6f, .4f, .4f), Q, Mass, Q, default);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
        row.SolvePositionNormal(ref a, ref b); row.SolvePositionFriction(ref a, ref b);
        Assert.InRange(MathF.Abs(row.PushOut.Y + .08f), 0, 1e-6f); Assert.InRange(row.StaticFrictionRatio, .07999f, .08001f);
        var va = AlsCachedJoint.AddImplicitVelocity(new(new(10, 0, -1), Vector3.Zero), a, 1d / 60, true);
        var vb = default(AlsProjectionVelocity); row.SolveVelocity(ref va, ref vb, 1f / 60, true);
        Assert.InRange(va.Linear.X, 9.5999f, 9.6001f); Assert.InRange(MathF.Abs(va.Linear.Z), 0, 1e-6f);
    }

    [Fact]
    public void DisabledPositionStillTransfersKinematicContactVelocityToDynamicBody()
    {
        var row = new AlsCachedContactPoint(Point with { DisablePosition = true }, new(0, 0, 0), Q, Mass, Q, default);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta); row.SolvePositionNormal(ref a, ref b);
        Assert.Equal(default, a); Assert.Equal(default, b);
        var va = default(AlsProjectionVelocity); var vb = new AlsProjectionVelocity(new(0, 0, 5), Vector3.Zero);
        row.SolveVelocity(ref va, ref vb, 1f / 60, false);
        Assert.Equal(new Vector3(0, 0, 5), va.Linear); Assert.Equal(new Vector3(0, 0, 5), vb.Linear);
    }

    [Fact]
    public void FailedGatherRetainsPriorManifoldAndSuccessfulGatherClearsAllAccumulators()
    {
        var manifold = new AlsCachedContactManifold(2); AlsContactMaterial material = new(.6f, .4f, .4f);
        manifold.Gather([Point], material, Q, Mass, Q, default);
        var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta); manifold.SolvePosition(ref a, ref b, true);
        Assert.Throws<ArgumentException>(() => manifold.Gather([Point, Point with { Normal = Vector3.Zero }], material, Q, Mass, Q, default));
        Assert.Equal(1, manifold.Count); Assert.True(manifold.PointAt(0).PushOut.X > 0);
        manifold.Gather([Point], material, Q, Mass, Q, default);
        Assert.Equal(Vector3.Zero, manifold.PointAt(0).PushOut); Assert.Equal(Vector3.Zero, manifold.PointAt(0).Impulse);
        manifold.Gather([], material, Q, Mass, Q, default); Assert.Equal(0, manifold.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => manifold.PointAt(0));
    }

    [Fact]
    public void ContactGatherAndIterationsDoNotAllocate()
    {
        var manifold = new AlsCachedContactManifold(4); var points = new[] { Point, Point with { Arm0 = Vector3.UnitX } };
        void Run()
        {
            manifold.Gather(points, new(.6f, .4f, .4f), Q, Mass, Q, default);
            var a = default(AlsProjectionDelta); var b = default(AlsProjectionDelta);
            for (var it = 0; it < 8; it++) manifold.SolvePosition(ref a, ref b, it >= 4);
            var va = AlsCachedJoint.AddImplicitVelocity(default, a, 1d / 60, true); var vb = default(AlsProjectionVelocity);
            for (var it = 0; it < 2; it++) manifold.SolveVelocity(ref va, ref vb, 1f / 60, it == 1);
        }
        for (var i = 0; i < 256; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 2048; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
