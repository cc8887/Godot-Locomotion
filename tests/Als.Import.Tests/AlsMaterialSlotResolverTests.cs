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

    [Theory]
    [InlineData("M_AnimMan_Default_0")]
    [InlineData("M_AnimMan_Default_14")]
    public void ResolvesGodotNumberedDuplicateNamesToTheUniqueExportedMaterial(string importedName)
    {
        Assert.Equal(
            0,
            AlsMaterialSlotResolver.Resolve(["M_AnimMan_Default", "M_AnimMan_Eyes"], [0, 1], importedName));
    }

    [Fact]
    public void PrefersAnExactExportedNameBeforeInterpretingANumericSuffix()
    {
        Assert.Equal(1, AlsMaterialSlotResolver.Resolve(["Default", "Default_1"], [0, 1], "Default_1"));
    }

    [Fact]
    public void RejectsAnAmbiguousGodotDuplicateName()
    {
        Assert.Equal(-1, AlsMaterialSlotResolver.Resolve(["Shared", "Shared"], [0, 1], "Shared_2"));
    }
}
