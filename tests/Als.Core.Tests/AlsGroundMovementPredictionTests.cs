using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsGroundMovementPredictionTests
{
    [Fact]
    public void ConstantBrakingKeepsWorldDirectionAndIgnoresVerticalVelocity()
    {
        var input=new AlsStopMovementSnapshot(new(3,4,999),false,100,0,1,2);
        var location=AlsGroundMovementPrediction.StopLocation(input);
        Assert.Equal(3.75,location.X,6); Assert.Equal(5,location.Y,6); Assert.Equal(0,location.Z);
        Assert.Equal(6.25,AlsGroundMovementPrediction.StopDistance(input),6);
    }
    [Fact]
    public void SeparateFrictionSelectsItsOwnComponentValue()
    {
        var input=new AlsStopMovementSnapshot(new(100,0,0),true,2,8,1,0);
        Assert.Equal(25,AlsGroundMovementPrediction.StopDistance(input));
        Assert.Equal(6.25,AlsGroundMovementPrediction.StopDistance(input with { UseSeparateBrakingFriction=false }));
    }
    [Fact]
    public void NegativeAndZeroBrakingCannotCreateAStoppingDistance()
    {
        var input=new AlsStopMovementSnapshot(new(100,100,0),false,-2,-3,-1,-4);
        Assert.Equal(AlsDoubleVector.Zero,AlsGroundMovementPrediction.StopLocation(input));
        Assert.Throws<ArgumentException>(()=>AlsGroundMovementPrediction.StopLocation(input with { GroundFriction=float.NaN }));
    }
}
