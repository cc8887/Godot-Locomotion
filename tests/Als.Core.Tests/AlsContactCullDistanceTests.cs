using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsContactCullDistanceTests
{
    [Fact]
    public void ParticleSizeAndVelocityExpansionUseIndependentNativeFloatBoundaries()
    {
        Assert.Equal(1, AlsContactCullDistance.Scale(27, 0, .01f, 1));
        var scale = AlsContactCullDistance.Scale(250, 120, .01f, 1);
        Assert.Equal((float)(250d * .01f), scale);
        // Equal endpoint velocities still expand: this is maximum absolute V,
        // not relative speed. The diagonal uses its largest component, not norm.
        Assert.Equal(3 * scale + 4, AlsContactCullDistance.Calculate(3, scale, .1,
            new(30, -40, 40), new(30, -40, 40), 1, 100));
        Assert.Equal(3 * scale + 2, AlsContactCullDistance.Calculate(3, scale, .1,
            new(30, -40, 40), default, 1, 2));
        Assert.Equal(3 * scale, AlsContactCullDistance.Calculate(3, scale, .1,
            new(30, -40, 40), default, 1, 0));
        Assert.Equal(3 * scale, AlsContactCullDistance.Calculate(3, scale, .1,
            new(30, -40, 40), default, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsContactCullDistance.Scale(double.NaN, 0, .01f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsContactCullDistance.Calculate(3, 1, .1,
            new(float.NaN, 0, 0), default, 1, 2));
    }

    [Fact]
    public void WorldProviderReceivesCommittedVelocityAndFailedAttemptCannotAdvanceIt()
    {
        var identity = AlsPrecisePose.Identity;
        var island = new AlsJointIsland([new(identity, new(1, AlsDoubleVector.One)), new(identity, default)], [],
            [new(identity, new(new(10, 0, 0), Vector3.Zero)), new(identity, default)]);
        var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, identity, 1, 1)); registry.Register(new(1, identity, 1, 1));
        var source = new Probe(); var contacts = new AlsWorldContacts(registry, source, new(0, 0, 0), new(.1f, 0, 0));
        island.Step(.1, new(0, 0, -100), contacts: contacts);
        Assert.Equal(new Vector3(10, 0, 0), source.Previous);
        Assert.Equal(new Vector3(10, 0, -10), source.Predicted);
        var committed = island.BodyAt(0);
        source.Fail = true;
        Assert.Throws<InvalidOperationException>(() => island.Step(.1, new(0, 0, -100), contacts: contacts));
        Assert.Equal(committed, island.BodyAt(0)); Assert.False(registry.IsLocked);
        Assert.Equal(1, contacts.CompletedSteps);
        source.Fail = false;
        island.Step(.1, new(0, 0, -100), contacts: contacts);
        Assert.Equal(committed.Velocity.Linear, source.Previous);
        Assert.Equal(new Vector3(10, 0, -20), source.Predicted);
        Assert.Equal(3, source.Prepared);
        Assert.Equal(2, contacts.CompletedSteps);
    }

    private sealed class Probe : IAlsContactGeometrySource
    {
        public Vector3 Previous, Predicted;
        public int Prepared; public bool Fail;
        public void PrepareStep(ReadOnlySpan<AlsIslandBodyState> previous, ReadOnlySpan<AlsProjectionVelocity> velocities,
            ReadOnlySpan<AlsIslandBody> bodies, double dt)
        {
            Assert.Equal(2, previous.Length); Assert.Equal(2, bodies.Length);
            Prepared++; Previous = previous[0].Velocity.Linear; Predicted = velocities[0].Linear;
            if (Fail) throw new InvalidOperationException("Injected context failure");
        }
        public int Query(int a, in AlsPrecisePose p0, int b, in AlsPrecisePose p1, Span<AlsDetectedContact> destination)
        { Assert.True(Prepared > 0); return 0; }
    }
}
