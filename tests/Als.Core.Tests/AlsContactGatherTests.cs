using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactGatherTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsContactGatherBody Body = new(Identity, default, 1, default);
    private static readonly AlsContactGatherSettings Settings = new(1f / 60, 0, 2000);
    private static readonly AlsContactGeometry Contact = new(new(0, 0, -.2f), Vector3.Zero, Vector3.UnitZ,
        Vector3.Zero, Vector3.Zero, false, false);

    [Fact]
    public void NativeNearNormalSlidingCancellationRemainsValidForGatheredRows()
    {
        var contact = Contact with { Normal1 = new(0, 0, .9999999f) };
        var body = Body with { Velocity = new(new(.02f, 0, -500), Vector3.Zero) };
        var point = AlsContactGather.Gather(contact, body, Body, Settings).Point;
        Assert.True(MathF.Abs(Vector3.Dot(point.Normal, point.TangentU)) > 1e-5f);
        var sliding = body.Velocity.Linear - Vector3.Dot(body.Velocity.Linear, contact.Normal1) * contact.Normal1;
        Assert.InRange(Vector3.Distance(Vector3.Normalize(sliding), point.TangentU), 0, 1e-7f);
        var manifold = new AlsCachedContactManifold(1);
        manifold.GatherGeometry([contact], new(.7f, .7f, .7f), body, AlsQuaternion.Identity, AlsDoubleVector.One,
            Body, AlsQuaternion.Identity, AlsDoubleVector.One, Settings);
        var a = new AlsProjectionDelta(); var b = new AlsProjectionDelta();
        manifold.SolvePosition(ref a, ref b, true);
        Assert.True(new AlsDoubleVector(manifold.PointAt(0).PushOut).IsFinite);
        var replay = new AlsCachedContactManifold(2);
        var mass = new AlsJointInverseMass(1, AlsDoubleVector.One);
        replay.GatherRows([point], new(.7f, .7f, .7f), AlsQuaternion.Identity, mass, AlsQuaternion.Identity, mass, true);
        Assert.Equal(point, replay.PointAt(0).Input);
        var replayA = new AlsProjectionDelta(); var replayB = new AlsProjectionDelta();
        replay.SolvePosition(ref replayA, ref replayB, true);
        Assert.Equal(a, replayA); Assert.Equal(b, replayB);
        Assert.Throws<ArgumentException>(() => replay.Gather([point], new(.7f, .7f, .7f), AlsQuaternion.Identity, mass, AlsQuaternion.Identity, mass));
        Assert.Throws<ArgumentException>(() => replay.GatherRows([point, point with { TargetVelocity = float.NaN }],
            new(.7f, .7f, .7f), AlsQuaternion.Identity, mass, AlsQuaternion.Identity, mass, true));
        Assert.Equal(1, replay.Count); Assert.Equal(point, replay.PointAt(0).Input);
        // Direct row callers retain the explicit orthonormal-basis contract.
        Assert.Throws<ArgumentException>(() => new AlsCachedContactPoint(point, new(.7f, .7f, .7f),
            AlsQuaternion.Identity, new(1, AlsDoubleVector.One), AlsQuaternion.Identity, new(1, AlsDoubleVector.One)));
    }

    [Fact]
    public void ContactArmsUseInverseMassWeightedCommonPointAndDoubleWorldOrigin()
    {
        var a = Body with { InverseMass = .5f }; var b = Body with { InverseMass = .25f };
        var origin = new AlsDoubleVector(100000000, -200000000, 300000000);
        var actual = AlsContactGather.Gather(Contact, a, b, Settings).Point;
        Assert.InRange(MathF.Abs(actual.Arm0.Z - (-.2f * 2 / 3)), 0, 1e-7f);
        var moved = AlsContactGather.Gather(Contact,
            a with { CenterOfMass = origin, ShapeWorld = Identity with { Position = origin } },
            b with { CenterOfMass = origin, ShapeWorld = Identity with { Position = origin } }, Settings).Point;
        Assert.InRange(Vector3.Distance(actual.Arm0, moved.Arm0), 0, 1e-7f);
        var fixedSide = AlsContactGather.Gather(Contact, a, b with { InverseMass = 0 }, Settings).Point;
        Assert.Equal(Contact.Point0, fixedSide.Arm0); // one common point, not independent point-to-COM arms
    }

    [Fact]
    public void TangentFallbackUsesSquaredToleranceAndAnchoredContactSkipsSlidingDirection()
    {
        var yContact = Contact with { Normal1 = Vector3.UnitY };
        Assert.Equal(Vector3.UnitZ, AlsContactGather.Gather(yContact, Body, Body, Settings).Point.TangentU);
        var slow = Body with { Velocity = new(new(0, .005f, 0), Vector3.Zero) };
        Assert.Equal(Vector3.UnitX, AlsContactGather.Gather(Contact, slow, Body, Settings).Point.TangentU);
        var fast = slow with { Velocity = new(new(0, 2, 0), Vector3.Zero) };
        Assert.Equal(Vector3.UnitY, AlsContactGather.Gather(Contact, fast, Body, Settings).Point.TangentU);
        var anchored = Contact with { HasAnchor = true, Anchor0 = Vector3.UnitX };
        var result = AlsContactGather.Gather(anchored, fast, Body, Settings).Point;
        Assert.Equal(Vector3.UnitX, result.TangentU); Assert.Equal(1, result.Error.Y);
        Assert.Equal(Vector3.UnitY, AlsContactGather.Gather(anchored with { InitialContact = true }, fast, Body, Settings).Point.TangentU);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RestitutionThresholdIsScaledByDtAndUsesStrictComparison(int hz)
    {
        var s = Settings with { Dt = 1f / hz, Restitution = .5f };
        var threshold = s.RestitutionThreshold * s.Dt;
        var b = Body with { Velocity = new(new(0, 0, -threshold), Vector3.Zero) };
        Assert.Equal(0, AlsContactGather.Gather(Contact, b, Body, s).Point.TargetVelocity);
        b = b with { Velocity = new(new(0, 0, -threshold - 1), Vector3.Zero) };
        Assert.Equal((threshold + 1) * .5f, AlsContactGather.Gather(Contact, b, Body, s).Point.TargetVelocity);
    }

    [Fact]
    public void InitialOverlapTracksNewAndPersistentPointsWithoutHidingAdditionalPenetration()
    {
        var s = Settings with { Dt = .1f, MaxDepenetrationVelocity = .5f };
        var first = AlsContactGather.Gather(Contact with { InitialContact = true }, Body, Body, s);
        Assert.InRange(MathF.Abs(first.InitialPhi + .15f), 0, 1e-7f);
        Assert.InRange(MathF.Abs(first.Point.Error.X + .05f), 0, 1e-7f);
        var next = AlsContactGather.Gather(Contact with { Point0 = new(0, 0, -.3f), InitialPhi = first.InitialPhi }, Body, Body, s);
        Assert.InRange(MathF.Abs(next.InitialPhi + .1f), 0, 1e-7f);
        Assert.InRange(MathF.Abs(next.Point.Error.X + .2f), 0, 1e-7f);
        var existing = AlsContactGather.Gather(Contact with { InitialContact = true }, Body, Body,
            s with { InitialManifold = false, MinInitialPhi = -.08f });
        Assert.InRange(MathF.Abs(existing.InitialPhi + .03f), 0, 1e-7f);
        var shared = AlsContactGather.Gather(Contact with { InitialPhi = -.15f }, Body, Body,
            s with { PerContactInitialPhi = false, MinInitialPhi = -.08f });
        Assert.Equal(existing.InitialPhi, shared.InitialPhi);
    }

    [Fact]
    public void PushOutLimitPrecedesTargetDepthAndFlagsSurviveGather()
    {
        var c = Contact with { TargetPhi = .03f, DisablePosition = true, DisableVelocity = true, DisableFriction = true };
        var result = AlsContactGather.Gather(c, Body, Body, Settings with { Dt = .1f, MaxPushOutVelocity = 1 }).Point;
        Assert.Equal(-.1f - .03f, result.Error.X);
        Assert.True(result.DisablePosition && result.DisableVelocity && result.DisableFriction);
        Assert.Throws<ArgumentException>(() => AlsContactGather.Gather(c, Body with { InverseMass = 0 }, Body with { InverseMass = 0 }, Settings));
        Assert.Throws<ArgumentException>(() => AlsContactGather.Gather(c with { Normal1 = Vector3.Zero }, Body, Body, Settings));
        Assert.Throws<ArgumentException>(() => AlsContactGather.Gather(c, Body with { ShapeWorld = Identity with { Scale = new(2, 1, 1) } }, Body, Settings));
    }

    [Fact]
    public void GeometryManifoldRetainsStateOnFailureAndAllocatesNothingDuringRepeatedGather()
    {
        var manifold = new AlsCachedContactManifold(2); var geometry = new[] { Contact with { InitialContact = true } };
        var s = Settings with { MaxDepenetrationVelocity = .5f }; var material = new AlsContactMaterial(.6f, .4f, .4f);
        void Gather() => manifold.GatherGeometry(geometry, material, Body, AlsQuaternion.Identity, AlsDoubleVector.One,
            Body, AlsQuaternion.Identity, AlsDoubleVector.One, s);
        Gather(); var phi = manifold.InitialPhiAt(0);
        var bad = new[] { Contact, Contact with { Normal1 = Vector3.Zero } };
        Assert.Throws<ArgumentException>(() => manifold.GatherGeometry(bad, material, Body, AlsQuaternion.Identity, AlsDoubleVector.One,
            Body, AlsQuaternion.Identity, AlsDoubleVector.One, s));
        Assert.Equal(phi, manifold.InitialPhiAt(0)); Assert.Equal(1, manifold.Count);
        for (var i = 0; i < 256; i++) Gather();
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 2048; i++) Gather();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
