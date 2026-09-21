using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsParticleOverlapSettingsTests
{
    [Theory]
    [InlineData(-1, 1e10f, 1e10f)]
    [InlineData(-2, 24, 24)]
    [InlineData(0, 1e10f, 0)]
    [InlineData(12, 1e10f, 12)]
    public void ExternalOverrideResolvesBeforeConstraintSetup(float external, float solver, float expected)
    {
        var resolved = AlsInitialOverlapSettings.ResolveParticle(external, solver);
        Assert.Equal(expected, resolved);
        Assert.Equal(expected, AlsInitialOverlapSettings.Resolve(resolved, 0));
    }

    [Fact]
    public void InvalidSolverValuesAreRejectedEvenForAnExplicitOverride()
    {
        Assert.Throws<ArgumentException>(() => AlsInitialOverlapSettings.ResolveParticle(0, -1));
        Assert.Throws<ArgumentException>(() => AlsInitialOverlapSettings.ResolveParticle(float.NaN, 1));
        Assert.Throws<ArgumentException>(() => AlsInitialOverlapSettings.ResolveParticle(0, float.PositiveInfinity));
    }
}
