using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsFootContactAnchorTests
{
    private static AlsPrecisePose Pose(AlsDoubleVector p, AlsQuaternion q) => new(p, q, AlsDoubleVector.One);
    private static AlsBasedFootLockState Locked => AlsBasedFootLockState.Empty with
        { HasSample = true, BaseIdentity = 7, Amount = 1, BaseLock = Pose(new(20, 8, 12), AlsQuaternion.Identity) };

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FinalEndpointMovesRigidlyWithTheBaseAndReleaseUsesActualCurve(int hz)
    {
        var identity = AlsPrecisePose.Identity;
        var anchor = AlsFootContactAnchoring.Complete(default, Locked, 1, true, identity, identity, new(20, 8, 14), false);
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var t = (double)frame / hz;
            var rotation = AlsQuaternion.FromAxisAngle(Vector3.Normalize(new(1, 2, 3)), (float)(t * .1));
            var basis = Pose(new(t * 4, -t, t * 2), rotation);
            var component = Pose(new(0, t * 3, 80), AlsQuaternion.FromAxisAngle(Vector3.UnitZ, (float)t));
            var target = AlsFootContactAnchoring.Target(anchor, Locked, 1, true, basis, component);
            var world = AlsPrecisePose.Compose(Pose(target.Location, AlsQuaternion.Identity), component).Position;
            var expected = new AlsDoubleVector(20, 8, 14).Rotate(rotation) + basis.Position;
            Assert.InRange((world - expected).LengthSquared, 0, 1e-20);
            Assert.Equal(1, target.Weight);
            Assert.Equal(target, AlsFootContactAnchoring.Target(anchor, Locked, 1, true, basis, component));
            var next = AlsFootContactAnchoring.Complete(anchor, Locked, 1, true, basis, component, target.Location, true);
            Assert.Equal(anchor, next);
        }
        var partial = AlsFootContactAnchoring.Target(anchor, Locked with { Amount = .65f }, 1, true, identity, identity);
        Assert.Equal(.65f, partial.Weight);
        Assert.Equal(new(20, 8, 14), partial.Location);
    }

    [Fact]
    public void NewSupportTeleportRelockAndConstraintsCannotReuseAnOldContact()
    {
        var identity = AlsPrecisePose.Identity;
        var anchor = AlsFootContactAnchoring.Complete(default, Locked, 1, true, identity, identity, new(20, 8, 14), false);
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, Locked, 1, false, identity, identity));
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, Locked, 2, true, identity, identity));
        foreach (var locked in new[] { Locked with { BaseIdentity = 8 }, Locked with { Amount = 0 },
            Locked with { ThighConstrained = true }, Locked with { BaseLock = Pose(new(21, 8, 12), AlsQuaternion.Identity) } })
            Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked, 1, true, identity, identity));
        var partial = anchor with { Amount = .65f };
        Assert.Equal(anchor, AlsFootContactAnchoring.Complete(anchor, Locked, 1, true, identity, identity,
            new(100, 200, 300), true)); // an unreachable/clamped output must not walk the saved endpoint
        Assert.Equal(default, AlsFootContactAnchoring.Target(partial, Locked, 1, true, identity, identity));
        Assert.False(AlsFootContactAnchoring.Complete(default, Locked with { Amount = .65f }, 1, true, identity, identity,
            new(20, 8, 14), false).Active);
    }

    [Fact]
    public void StaticContactStaysInWorldSpaceWithoutInventingAMovementBase()
    {
        var locked = Locked with { BaseIdentity = 0, BaseLock = AlsPrecisePose.Identity,
            WorldLock = Pose(new(20, 8, 12), AlsQuaternion.Identity) };
        var identity = AlsPrecisePose.Identity;
        var anchor = AlsFootContactAnchoring.Complete(default, locked, 1, true, default, identity, new(20, 8, 14), false, 41);
        Assert.True(anchor.Active); Assert.Equal(0UL, anchor.BaseIdentity); Assert.Equal(41UL, anchor.SurfaceIdentity);
        Assert.Equal(locked.WorldLock.Position, anchor.SourceAnchor);
        for (var frame = 0; frame < 120; frame++)
        {
            var component = Pose(new(frame * .1, -frame * .2, 80), AlsQuaternion.FromAxisAngle(Vector3.UnitZ, frame * .01f));
            var target = AlsFootContactAnchoring.Target(anchor, locked, 1, true, default, component, 41);
            var world = AlsPrecisePose.Compose(Pose(target.Location, AlsQuaternion.Identity), component).Position;
            Assert.InRange((world - new AlsDoubleVector(20, 8, 14)).LengthSquared, 0, 1e-20);
            Assert.Equal(anchor, AlsFootContactAnchoring.Complete(anchor, locked, 1, true, default, component, target.Location, true, 41));
        }
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked, 1, true, default, identity, 42));
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked, 1, true, default, identity));
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked, 2, true, default, identity, 41));
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked with {
            WorldLock = locked.WorldLock with { Position = new(21, 8, 12) } }, 1, true, default, identity, 41));
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor, locked with { ThighConstrained = true }, 1, true, default, identity, 41));
        Assert.Equal(.65f, AlsFootContactAnchoring.Target(anchor, locked with { Amount = .65f }, 1, true, default, identity, 41).Weight);
        Assert.Equal(default, AlsFootContactAnchoring.Target(anchor with { Amount = .65f }, locked, 1, true, default, identity, 41));
        Assert.False(AlsFootContactAnchoring.Complete(anchor, locked with { Amount = 0 }, 1, true, default, identity, default, false, 41).Active);
    }

    [Fact]
    public void SwitchingStaticAndRelativeSupportsRecapturesEvenForTheSameCollider()
    {
        var identity = AlsPrecisePose.Identity;
        var relative = Locked with { BaseIdentity = 41 };
        var world = relative with { BaseIdentity = 0, WorldLock = relative.BaseLock, BaseLock = identity };
        var staticAnchor = AlsFootContactAnchoring.Complete(default, world, 1, true, identity, identity, new(20, 8, 14), false, 41);
        Assert.Equal(default, AlsFootContactAnchoring.Target(staticAnchor, relative, 1, true, identity, identity, 41));
        var relativeAnchor = AlsFootContactAnchoring.Complete(default, relative, 1, true, identity, identity, new(20, 8, 14), false, 41);
        Assert.Equal(default, AlsFootContactAnchoring.Target(relativeAnchor, world, 1, true, identity, identity, 41));
        Assert.False(AlsFootContactAnchoring.Complete(default, relative, 1, true, identity, identity, default, false, 42).Active);
    }

    [Fact]
    public void PelvisLoweringSolvesVerticalReachWithoutClaimingToSolveHorizontalOverreach()
    {
        var thigh = new AlsDoubleVector(0, 0, 100);
        var target = new AlsFootContactTarget(1, new(30, 0, 10));
        var lower = AlsFootContactAnchoring.PelvisLowering(thigh, target, 80);
        Assert.True(lower < 0);
        var corrected = thigh + new AlsDoubleVector(0, 0, lower);
        Assert.InRange(System.Math.Sqrt((target.Location - corrected).LengthSquared), 79.999999, 80.000001);
        Assert.Equal(0, AlsFootContactAnchoring.PelvisLowering(thigh, new(1, new(90, 0, 10)), 80));
        Assert.Equal(0, AlsFootContactAnchoring.PelvisLowering(thigh, new(1, new(0, 0, 30)), 80));
    }
}
