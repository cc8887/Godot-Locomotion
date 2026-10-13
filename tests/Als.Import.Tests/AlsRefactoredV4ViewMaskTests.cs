using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredV4ViewMaskTests
{
    private static readonly string[] Names = ["ViewBlock", "Mask_AimOffset", "Enable_SpineRotation", "Unrelated"];

    [Theory]
    [InlineData(0f, .3f, .7f, .3f, .7f)]
    [InlineData(.5f, .3f, .7f, .65f, .35f)]
    [InlineData(1f, .3f, .7f, 1f, 0f)]
    [InlineData(1.1f, .3f, .7f, 1f, 0f)]
    public void AuthoredViewBlockRestrictsBothV4ViewBranches(float block, float mask, float spine, float expectedMask, float expectedSpine)
    {
        AlsInertialCurve[] curves = [new(block), new(mask), new(spine), new(4.5f)];
        AlsRefactoredV4ViewMask.Apply(Names, curves);
        Assert.Equal(expectedMask, curves[1].Value, 6);
        Assert.Equal(expectedSpine, curves[2].Value, 6);
        Assert.Equal(new(block), curves[0]);
        Assert.Equal(new(4.5f), curves[3]);
    }

    [Fact]
    public void MissingBlockIsAnExactNoOpAndDoesNotCreateSpineActivity()
    {
        AlsInertialCurve[] curves = [new(1, false), default, default, new(4.5f)];
        var original = curves.ToArray();
        AlsRefactoredV4ViewMask.Apply(Names, curves);
        Assert.Equal(original, curves);
        curves[0] = new(1);
        AlsRefactoredV4ViewMask.Apply(Names, curves);
        Assert.Equal(new(1), curves[1]);
        Assert.False(curves[2].Present);
    }
}
