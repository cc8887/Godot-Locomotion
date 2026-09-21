using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactShockTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static readonly AlsContactPointInput Input = new(new(1, 2, 0), new(-2, 1, 0), Vector3.UnitZ,
        Vector3.UnitX, Vector3.UnitY, new(-.2f, .01f, .02f), 0);
    private static AlsCachedContactPoint Point(float m0 = 1, float m1 = 1) => new(Input, new(.6f, .4f, .4f),
        Identity.Rotation, new(m0, AlsDoubleVector.One), Identity.Rotation, new(m1, AlsDoubleVector.One));
    [Fact]
    public void NormalMassSwitchKeepsTangentMassAndAccumulatedPushoutAndImpulse()
    {
        var point = Point(); AlsProjectionDelta a = default, b = default;
        point.SolvePositionNormal(ref a, ref b); point.SolvePositionFriction(ref a, ref b);
        var va = new AlsProjectionVelocity(new(1, 0, -2), Vector3.Zero); AlsProjectionVelocity vb = default;
        point.SolveVelocity(ref va, ref vb, 1f / 60, true);
        var mass = point.ContactMass; var push = point.PushOut; var impulse = point.Impulse;
        point.SetShockPropagation(1, 2, .77f);
        Assert.True(point.ContactMass.X > mass.X); Assert.Equal(mass.Y, point.ContactMass.Y); Assert.Equal(mass.Z, point.ContactMass.Z);
        Assert.Equal(push, point.PushOut); Assert.Equal(impulse, point.Impulse);
        point.SetShockPropagation(1, 2, 1); Assert.Equal(mass, point.ContactMass);
    }
    [Theory]
    [InlineData(1, 1, 2, 2)]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 0, 1, 0)]
    public void StaticEndpointsAndEqualLevelsDoNotScale(float m0, float m1, int level0, int level1)
    {
        var point = Point(m0, m1); var mass = point.ContactMass;
        point.SetShockPropagation(level0, level1, .77f); Assert.Equal(mass, point.ContactMass);
    }
    [Fact]
    public void NewGatherResetsScaledMassAndLambdas()
    {
        var manifold = new AlsCachedContactManifold(1); AlsContactPointInput[] inputs = [Input];
        var mass = new AlsJointInverseMass(1, AlsDoubleVector.One);
        manifold.Gather(inputs, new(.6f, .4f, .4f), Identity.Rotation, mass, Identity.Rotation, mass);
        var baseline = manifold.PointAt(0).ContactMass;
        manifold.SetShockPropagation(2, 1, .77f); AlsProjectionDelta a = default, b = default;
        manifold.SolvePosition(ref a, ref b, true); Assert.NotEqual(Vector3.Zero, manifold.PointAt(0).PushOut);
        manifold.Gather(inputs, new(.6f, .4f, .4f), Identity.Rotation, mass, Identity.Rotation, mass);
        Assert.Equal(baseline, manifold.PointAt(0).ContactMass); Assert.Equal(Vector3.Zero, manifold.PointAt(0).PushOut);
        Assert.Throws<ArgumentOutOfRangeException>(() => manifold.SetShockPropagation(1, 2, float.NaN));
    }
    [Fact]
    public void WorldUsesCurrentGraphLevelsAndNativeIterationBoundaryWithoutChangingBodies()
    {
        var registry = new AlsContactRegistry(3, 3);
        for (var i = 0; i < 3; i++) registry.Register(new(i, Identity, 1, 1));
        AlsIslandBody[] definitions = [new(Identity, default), new(Identity, new(1, AlsDoubleVector.One)), new(Identity, new(1, AlsDoubleVector.One))];
        AlsPrecisePose[] poses = [Identity, Identity with { Position = new(0, 0, .95) }, Identity with { Position = new(0, 0, 2.85) }];
        var states = poses.Select(p => new AlsIslandBodyState(p, default)).ToArray();
        var island = new AlsJointIsland(definitions, [], states);
        var world = new AlsWorldContacts(registry, new StackGeometry(), new(.6f, .4f, .4f), new(1f / 60, 0, 2000), island: island);
        world.Gather(poses, new AlsProjectionVelocity[3], definitions, 1d / 60); world.PrepareConstraintOrder(island, []);
        Assert.Equal(1, world.PreparedBodyLevelAt(1)); Assert.Equal(2, world.PreparedBodyLevelAt(2)); Assert.Equal(2, world.PreparedPairCount);
        var expected = new AlsCachedContactManifold[2]; var pairs = new AlsPreparedContactPair[2];
        for (var i = 0; i < 2; i++)
        {
            pairs[i] = world.PreparedPairAt(i); var pair = pairs[i]; expected[i] = new(1);
            expected[i].Gather([world.PreparedPointAt(i, 0)], pair.Material, Identity.Rotation, definitions[pair.Body0].InverseMass,
                Identity.Rotation, definitions[pair.Body1].InverseMass);
        }
        var actual = new AlsProjectionDelta[3]; var control = new AlsProjectionDelta[3];
        for (var it = 0; it < 8; it++)
        {
            world.SolvePosition(actual, it, 8);
            for (var i = 0; i < 2; i++)
            {
                var pair = pairs[i]; expected[i].SetShockPropagation(pair.Body0, pair.Body1, it >= 5 ? .77f : 1);
                expected[i].SolvePosition(ref control[pair.Body0], ref control[pair.Body1], it >= 4);
            }
            Assert.Equal(control, actual);
        }
        for (var i = 0; i < 3; i++) Assert.Equal(states[i], island.BodyAt(i));
        world.Abort(); Assert.False(registry.IsLocked);
    }
    private sealed class StackGeometry : IAlsContactGeometrySource
    {
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> points)
        {
            if (b != a + 1) return 0;
            points[0] = new(a == 0 ? Vector3.Zero : Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ); return 1;
        }
    }
}
