using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsWorldManifoldTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private readonly AlsIslandBody[] _bodies = [new(Identity, new(1, AlsDoubleVector.One)), new(Identity, default)];
    private readonly AlsProjectionVelocity[] _velocities = new AlsProjectionVelocity[2];
    private readonly AlsPrecisePose[] _poses = [Identity with { Position = new(0, 0, -.1) }, Identity];
    private (AlsWorldContacts World, Source Geometry, AlsContactRegistry Registry) Create(bool quadratic = false)
    {
        var registry = new AlsContactRegistry(2, 2);
        registry.Register(new(0, Identity, 1, 1, quadratic)); registry.Register(new(1, Identity, 1, 1));
        var source = new Source(); return (new(registry, source, new(0, 0, 0), new(1f / 60, 0, 2000)), source, registry);
    }
    private void Gather(AlsWorldContacts world) => world.Gather(_poses, _velocities, _bodies, 1d / 60);
    private void Commit(AlsWorldContacts world) { world.StageCommit(); world.Commit(); }
    [Fact]
    public void RestoresPolygonalPairButCullsSeparationAndQueriesAgainAfterIt()
    {
        var (world, source, _) = Create(); Gather(world); Commit(world);
        Gather(world); Commit(world); Assert.Equal(1, source.Calls); Assert.Equal(1, world.LastRestoredPairs);
        _poses[0] = Identity with { Position = new(0, 0, .1) };
        Gather(world); Commit(world); Assert.Equal(0, world.LastActivePairs); Assert.Equal(0, world.LastRestoredPairs);
        Gather(world); Commit(world); Assert.Equal(2, source.Calls);
    }
    [Fact]
    public void QuadraticPairNeverRestoresEvenIfProviderOptsIn()
    {
        var (world, source, _) = Create(true);
        for (var i = 0; i < 3; i++) { Gather(world); Commit(world); }
        Assert.Equal(3, source.Calls); Assert.Equal(0, world.LastRestoredPairs);
    }
    [Fact]
    public void AbortedReplacementDoesNotChangeOriginalGeometryOrEpoch()
    {
        var (world, source, registry) = Create(); Gather(world); Commit(world);
        _poses[0] = _poses[0] with { Position = new(1, 0, -.1) }; Gather(world); world.StageCommit(); world.Abort();
        Assert.False(registry.IsLocked); Assert.Equal(1, world.CompletedSteps);
        _poses[0] = _poses[0] with { Position = new(.1, 0, -.1) }; Gather(world); Commit(world);
        Assert.Equal(2, source.Calls); Assert.Equal(1, world.LastRestoredPairs);
        registry.RebindBody(0); Gather(world); Commit(world); Assert.Equal(3, source.Calls);
    }
    [Fact]
    public void FilteredOutEpochAndShapeRevisionPreventStaleRestoration()
    {
        var (world, source, registry) = Create(); Gather(world); Commit(world);
        registry.DisableBodyPair(0, 1, true); Gather(world); Commit(world);
        registry.DisableBodyPair(0, 1, false); Gather(world); Commit(world); Assert.Equal(2, source.Calls);
        registry.Replace(new(0, registry.Key(0).Revision), registry.At(0)); Gather(world); Commit(world); Assert.Equal(3, source.Calls);
        world.Reset(); Gather(world); Commit(world); Assert.Equal(4, source.Calls);
    }
    private sealed class Source : IAlsContactGeometrySource
    {
        public int Calls;
        public float Cull;
        public float? Phi;
        public bool TryGetManifoldSettings(int a, int b, out AlsContactManifoldSettings settings) { settings = new(2, Cull); return true; }
        public int Query(int a, in AlsPrecisePose p, int b, in AlsPrecisePose q, Span<AlsDetectedContact> destination)
        { Calls++; if (p.Position.Z > 0 && !Phi.HasValue) return 0; destination[0] = new(Vector3.Zero, new((float)p.Position.X, 0, 0), Vector3.UnitZ) { NativePhi = Phi }; return 1; }
    }

    [Theory]
    [InlineData(3f, 1)]
    [InlineData(3.000001f, 0)]
    public void InitialActivationUsesOriginalPhiAndIncludesExactCullBoundary(float phi, int active)
    {
        var (world, source, registry) = Create(); source.Cull = 3; source.Phi = phi;
        // Rounded local points deliberately imply penetration, not native phi.
        Gather(world); world.StageCommit(); world.Abort();
        Assert.Equal(0, world.CompletedSteps); Assert.False(registry.IsLocked);
        Gather(world); Commit(world); Assert.Equal(active, world.LastActivePairs);
        Assert.Equal(2, source.Calls);
        Gather(world); Commit(world);
        Assert.Equal(active == 0 ? 3 : 2, source.Calls);
    }
}
