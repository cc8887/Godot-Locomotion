using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCameraBlendListTests
{
    [Fact]
    public void FourChildrenPreserveInterruptedContributionsAndCanBeCopied()
    {
        var blend = new AlsCameraBlendList(4); float[] times = [1, 1, 1, 0];
        blend.Advance(0, .1f, times, true, x => x);
        var start = blend.Advance(1, .25f, times, true, x => x);
        Assert.Equal(1, start.ResetChild); Assert.Equal(new[] { .75f, .25f, 0, 0 }, blend.Weights.ToArray());
        blend.Advance(2, .25f, times, true, x => x);
        Assert.Equal(new[] { .5625f, .1875f, .25f, 0 }, blend.Weights.ToArray());
        var copy = blend.Copy();
        var work = blend.Advance(3, .01f, times, true, x => x);
        Assert.Equal(2, work.ZeroWeightPrevious); Assert.Equal(3, work.ResetChild);
        Assert.Equal(new[] { 0f, 0, 0, 1 }, blend.Weights.ToArray());
        Assert.Equal(new[] { .5625f, .1875f, .25f, 0 }, copy.Weights.ToArray());
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void TwoChildCasesMatchExistingNativeAlignedBinaryOwner(int hz)
    {
        var blend = new AlsCameraBlendList(2); var old = default(AlsBinaryBlendState);
        var settings = new AlsBinaryBlendSettings(.7f, .3f, AlsTransitionBlend.Cubic);
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var child = frame / 7 % 2;
            blend.Advance(child, 1f / hz, new[] { .7f, .3f }, false, p => AlsTransitionStack.Alpha(p, AlsTransitionBlend.Cubic));
            old = AlsBinaryBlendList.Advance(old, child, 1f / hz, settings).State;
            Assert.Equal(old.FirstWeight, blend.Weights[0]); Assert.Equal(old.SecondWeight, blend.Weights[1]);
        }
    }
}
