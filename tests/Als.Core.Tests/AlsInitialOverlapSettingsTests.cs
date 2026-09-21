using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsInitialOverlapSettingsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(60, -1)]
    [InlineData(120, -2)]
    public void OwnerUsesPairSettingsCopiesCallerStorageAndRollsBack(float speed, float error)
    {
        var identity = AlsPrecisePose.Identity; var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, identity, 1, 1)); registry.Register(new(1, identity, 1, 1));
        var speeds = new[] { -1f, speed };
        var world = new AlsWorldContacts(registry, new Source(), new(0, 0, 0), new(1f / 60, 0, 1000, 1000), bodyOverlapVelocities: speeds);
        speeds[1] = 10000; // The world owns an immutable copy of the binding.
        AlsPrecisePose[] poses = [identity with { Position = new(0, 0, -2) }, identity];
        AlsIslandBody[] bodies = [new(identity, new(1, AlsDoubleVector.One)), new(identity, default)];
        var velocities = new AlsProjectionVelocity[2];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            world.Gather(poses, velocities, bodies, 1d / 60);
            Assert.InRange(MathF.Abs(world.PreparedPointAt(0, 0).Error.X - error), 0, 1e-6f);
            world.StageCommit(); world.Abort();
            Assert.Equal(0, world.CompletedSteps); Assert.False(registry.IsLocked);
        }
        Assert.Throws<ArgumentException>(() => new AlsWorldContacts(registry, new Source(), new(0, 0, 0),
            new(1f / 60, 0, 1000), bodyOverlapVelocities: [0]));
        Assert.Throws<ArgumentException>(() => AlsInitialOverlapSettings.Resolve(float.NaN, 0));
    }
    private sealed class Source : IAlsContactGeometrySource
    {
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> points)
        { points[0] = new(Vector3.Zero, Vector3.Zero, Vector3.UnitZ); return 1; }
    }
}
