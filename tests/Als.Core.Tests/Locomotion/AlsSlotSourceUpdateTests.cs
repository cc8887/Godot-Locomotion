using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsSlotSourceUpdateTests
{
    [Theory]
    [InlineData(0, 1, 0, false, true, true, true)]
    [InlineData(1, .5f, .5f, false, true, true, false)]
    [InlineData(.5f, .8f, .2f, false, true, true, true)]
    [InlineData(1, 1, 1, false, true, true, false)]
    [InlineData(1, .5f, .5f, false, false, true, true)]
    [InlineData(1, 0, 1, false, true, false, false)]
    [InlineData(1, 0, 1, true, true, true, false)]
    [InlineData(0, 0, 0, true, true, true, true)]
    public void UsesNativeSourceRelevanceAndInactiveRules(float previous, float source, float slot, bool always, bool mark, bool updated, bool active)
    {
        var context = new AlsPoseUpdateContext(new(1, 2, 3), .7f, .01f, .6f).WithState(90, 1).WithInertialization(91, true);
        var result = AlsSlotSourceUpdate.Resolve(previous, new(source, slot, 2), context, always, mark);
        Assert.Equal(updated, result.Updated);
        if (!updated) return;
        Assert.Equal(active, result.Context.IsActive);
        Assert.Equal(.7f * MathF.Max(.00002f, source), result.Context.Weight);
        Assert.Equal(context.RootMotionWeight, result.Context.RootMotionWeight);
        Assert.Equal(context.Identity, result.Context.Identity); Assert.Equal(context.Delta, result.Context.Delta);
        Assert.Equal(context.GetState(0), result.Context.GetState(0)); Assert.Equal(91, result.Context.SkippedUpdateHandler);
    }

    [Fact]
    public void RelevantTinySourceStillTicksAtTwiceThresholdAndKeepsInactiveAncestors()
    {
        var context = new AlsPoseUpdateContext(new(1, 2, 3), .5f, .01f).AsInactive();
        var result = AlsSlotSourceUpdate.Resolve(0, new(.000015f, 0, 0), context, false);
        Assert.True(result.Updated); Assert.Equal(.00001f, result.Context.Weight); Assert.False(result.Context.IsActive);
        var child = result.Context.WithState(12, 2).WithWeight(.3f).WithInertialization(19, false);
        Assert.False(child.IsActive);
        Assert.False(AlsSlotSourceUpdate.Resolve(0, new(AlsPoseBlender.WeightThreshold, 0, 0), context, false).Updated);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)] [InlineData(0, -1, 0, 0)] [InlineData(0, 2, 0, 0)]
    [InlineData(0, 1, -1, 0)] [InlineData(0, 1, 2, 0)] [InlineData(0, 1, 0, -1)]
    [InlineData(float.NaN, 1, 0, 0)] [InlineData(0, float.NaN, 0, 0)]
    [InlineData(0, 1, float.PositiveInfinity, 0)] [InlineData(0, 1, 0, float.NaN)]
    public void RejectsNonFiniteOrInvalidOwnerWeights(float previous, float source, float slot, float total)
    {
        Assert.Throws<ArgumentException>(() => AlsSlotSourceUpdate.Resolve(previous, new(source, slot, total),
            new(new(1, 2, 3), 1, .01f), false));
    }
}
