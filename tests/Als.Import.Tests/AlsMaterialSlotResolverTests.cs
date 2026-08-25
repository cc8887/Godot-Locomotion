using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMaterialSlotResolverTests
{
    [Fact]
    public void ResolvesNamesOnlyWithinTheMeshMaterialSet()
    {
        var names = new[] { "Shared", "Allowed", "Shared" };

        Assert.Equal(1, AlsMaterialSlotResolver.Resolve(names, [1], "Allowed"));
        Assert.Equal(-1, AlsMaterialSlotResolver.Resolve(names, [1], "Shared"));
    }

    [Fact]
    public void DoesNotGuessFromSurfaceOrderWhenTheImportedNameIsMissing()
    {
        Assert.Equal(-1, AlsMaterialSlotResolver.Resolve(["First"], [0], string.Empty));
    }

    [Fact]
    public void RejectsAnAmbiguousNameWithinTheMeshMaterialSet()
    {
        Assert.Equal(-1, AlsMaterialSlotResolver.Resolve(["Shared", "Shared"], [0, 1], "Shared"));
    }
}
