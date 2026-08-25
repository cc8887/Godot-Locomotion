using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsMetadataReferenceTests
{
    [Fact]
    public void EmptyOptionalBasePoseReferenceCompilesToMinusOne()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());

        var clip = Assert.Single(AlsAnimationSetCompiler.Compile(manifest).Animations);

        Assert.Equal(-1, clip.AdditiveBasePoseAnimationId);
    }
}
