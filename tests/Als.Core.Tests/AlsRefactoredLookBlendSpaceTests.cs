using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredLookBlendSpaceTests
{
    [Fact]
    public void ThresholdPruningNormalizesRemainingSamplesAndRejectsNonFiniteInput()
    {
        var samples=new AlsAimGridVertex[2];
        foreach(var pitch in new[]{-90f,MathF.BitIncrement(-90),-.00001f,0,.00001f,MathF.BitDecrement(90),90})
        {
            var count=AlsRefactoredLookBlendSpace.Evaluate(pitch,samples);
            Assert.Equal(1,count);Assert.Equal(1,samples[0].Weight);
        }
        foreach(var pitch in new[]{-89.99f,-.01f,.01f,89.99f})
        {
            Assert.Equal(2,AlsRefactoredLookBlendSpace.Evaluate(pitch,samples));
            Assert.True(samples[0].Weight>=samples[1].Weight);
            Assert.InRange(samples[1].Weight,AlsPoseBlender.WeightThreshold,1);
            Assert.Equal(1,samples[0].Weight+samples[1].Weight,6);
        }
        Assert.Throws<ArgumentException>(()=>AlsRefactoredLookBlendSpace.Evaluate(float.NaN,samples));
        Assert.Throws<ArgumentException>(()=>AlsRefactoredLookBlendSpace.Evaluate(0,new AlsAimGridVertex[1]));
    }
}
