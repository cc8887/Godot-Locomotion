using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsStableAssetIdTests
{
    [Fact]
    public void StableIdUsesNormalizedObjectPath()
    {
        Assert.Equal(
            "521a92ef21af7e41f2a1f1df2d3ab94e813aa194",
            AlsStableAssetId.Create("/Game/Test/Asset.Asset"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Content/Test/Asset.Asset")]
    [InlineData("/Game/Test\\Asset.Asset")]
    [InlineData("/Engine/Test/Asset.Asset")]
    public void StableIdRejectsNonCanonicalObjectPath(string objectPath)
    {
        Assert.ThrowsAny<ArgumentException>(() => AlsStableAssetId.Create(objectPath));
    }
}
