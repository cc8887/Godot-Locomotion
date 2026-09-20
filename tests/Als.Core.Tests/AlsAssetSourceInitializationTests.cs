using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

public sealed class AlsAssetSourceInitializationTests
{
    [Theory]
    [InlineData(AlsAssetSyncKind.Sequence, 0, 2, -1, 1, 1, 2)]
    [InlineData(AlsAssetSyncKind.Sequence, 0, 2, 1, 1, -1, 2)]
    [InlineData(AlsAssetSyncKind.Sequence, -1, 2, -1, 1, 1, 0)]
    [InlineData(AlsAssetSyncKind.Sequence, 10, 2, 1, 1, 1, 2)]
    [InlineData(AlsAssetSyncKind.Sequence, .2f, 2, -1, 1, 1, .2f)]
    [InlineData(AlsAssetSyncKind.Sequence, 0, 2, -1, 0, 1, 0)]
    [InlineData(AlsAssetSyncKind.Sequence, 0, 2, 1, -1, 1, 2)]
    [InlineData(AlsAssetSyncKind.BlendSpace, 0, 2, -1, 1, -1, 1)]
    [InlineData(AlsAssetSyncKind.BlendSpace, 0, 2, 1, 1, -1, 0)]
    [InlineData(AlsAssetSyncKind.BlendSpace, .93f, 2, 1, 1, 1, .93f)]
    [InlineData(AlsAssetSyncKind.BlendSpace, 2, 2, -1, 1, 1, 1)]
    public void FollowsNativeStartClampingAndReverseInitialization(AlsAssetSyncKind kind, float start, float length,
        float rate, float basis, float assetRate, float expected) =>
        Assert.Equal(expected, AlsAssetSourceInitialization.Time(kind, start, length, rate, basis, assetRate));

    [Fact]
    public void EpochsCannotSilentlyWrapAndInvalidRatesFailBeforeInitialization()
    {
        Assert.Equal(1, AlsAssetSourceInitialization.NextEpoch(0));
        Assert.Equal(2, AlsAssetSourceInitialization.NextEpoch(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsAssetSourceInitialization.NextEpoch(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsAssetSourceInitialization.NextEpoch(long.MaxValue));
        Assert.Throws<ArgumentException>(() => AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, 0, 0, 1));
        Assert.Throws<ArgumentException>(() => AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, float.NaN, 2, 1));
        Assert.Throws<ArgumentException>(() => AlsAssetSourceInitialization.Time(AlsAssetSyncKind.Sequence, 0, 2, float.MaxValue, 1, float.MaxValue));
    }
}
