using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationTrackPathContractTests
{
    [Fact]
    public void AcceptsTheExactSkeletonBonePath()
    {
        Assert.Equal("pelvis", AlsAnimationTrackPathContract.GetBoneName("Skeleton3D:pelvis"));
    }

    [Theory]
    [InlineData("Other/Skeleton3D:pelvis")]
    [InlineData("Skeleton3D:pelvis:position")]
    [InlineData("Skeleton3D")]
    [InlineData(":pelvis")]
    public void RejectsPathsOutsideTheFixedImporterContract(string path)
    {
        Assert.Throws<AlsCompilationException>(() => AlsAnimationTrackPathContract.GetBoneName(path));
    }
}
