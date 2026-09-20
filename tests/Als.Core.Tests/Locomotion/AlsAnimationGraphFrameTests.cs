using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsAnimationGraphFrameTests
{
    [Fact]
    public void TraversalCountsRemainIndependentOfSkippedFrameIdentities()
    {
        var first=default(AlsAnimationGraphFrame).Next(new(32766,11,2),900);
        var next=first.Next(new(65536,11,2),930);
        Assert.Equal(0,first.Update.Counter); Assert.Equal(1,next.Update.Counter);
        Assert.Equal(1,next.Evaluation.Counter); Assert.Equal((ulong)930,next.Evaluation.GlobalFrame);
        Assert.Equal(first.Initialization,next.Initialization); Assert.Equal(first.Bones,next.Bones);
        next.Validate(new(65536,11,2));
    }
    [Theory]
    [InlineData(32767,-32768)]
    [InlineData(-2,0)]
    public void SignedTraversalWrapSkipsTheNativeInvalidSentinel(short previous,short expected)
    {
        var counter=new AlsGraphTraversalCounter(previous,20);
        var state=new AlsAnimationGraphFrame(new(2,11,2),new(0,1),new(0,1),counter,counter);
        var next=state.Next(new(3,11,2),21);
        Assert.Equal(expected,next.Update.Counter); Assert.Equal(expected,next.Evaluation.Counter);
    }
    [Fact]
    public void MultipleCharacterUpdatesMayShareOneGlobalFrame()
    {
        var state=default(AlsAnimationGraphFrame).Next(new(1,11,2),99);
        var next=state.Next(new(2,11,2),99);
        Assert.False(next.Evaluation.MatchesAll(state.Evaluation));
        Assert.True(next.Bones.MatchesAll(state.Bones));
    }
    [Fact]
    public void RejectsReusedIdentityForeignGenerationAndRegressingGlobalFrame()
    {
        var state=default(AlsAnimationGraphFrame).Next(new(10,11,2),99);
        Assert.Throws<ArgumentException>(()=>state.Next(new(10,11,2),100));
        Assert.Throws<ArgumentException>(()=>state.Next(new(11,11,3),100));
        Assert.Throws<ArgumentException>(()=>state.Next(new(11,12,2),100));
        Assert.Throws<ArgumentException>(()=>state.Next(new(11,11,2),98));
        Assert.Throws<ArgumentException>(()=>default(AlsAnimationGraphFrame).Validate(new(11,11,2)));
    }
}
