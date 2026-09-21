using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsCollisionMarginsTests
{
    [Fact]
    public void SleepingPolytopesKeepTheirDynamicMargin()
    {
        var sleeping=new AlsCollisionMarginInput(.6f,false,AlsCollisionMotionState.Sleeping);
        var dynamic=new AlsCollisionMarginInput(.2f,false,AlsCollisionMotionState.Dynamic);
        Assert.Equal(new AlsCollisionMarginPair(.2f,.2f),AlsCollisionMargins.Resolve(sleeping,dynamic,0));
        Assert.Equal(new AlsCollisionMarginPair(0,.2f),AlsCollisionMargins.Resolve(sleeping with {Motion=AlsCollisionMotionState.Kinematic},dynamic,0));
    }
    [Fact]
    public void QuadraticRadiusSurvivesStaticStateAndDisablesOpposingPolygonMargin()
    {
        var sphere=new AlsCollisionMarginInput(3,true,AlsCollisionMotionState.Static);
        var box=new AlsCollisionMarginInput(.2f,false,AlsCollisionMotionState.Dynamic);
        Assert.Equal(new AlsCollisionMarginPair(3,0),AlsCollisionMargins.Resolve(sphere,box,.05f));
        Assert.Equal(new AlsCollisionMarginPair(0,3),AlsCollisionMargins.Resolve(box,sphere,.05f));
    }
    [Fact]
    public void BothZeroMarginsPreserveNativeSecondSideMinimum()
    {
        var zero=new AlsCollisionMarginInput(0,false,AlsCollisionMotionState.Dynamic);
        Assert.Equal(new AlsCollisionMarginPair(0,.05f),AlsCollisionMargins.Resolve(zero,zero,.05f));
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-1)]
    public void InvalidNativeMarginIsRejected(float margin)
    {
        var valid=new AlsCollisionMarginInput(0,false,AlsCollisionMotionState.Dynamic);
        Assert.Throws<ArgumentException>(()=>AlsCollisionMargins.Resolve(valid with {ShapeMargin=margin},valid,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsCollisionMargins.Resolve(valid,valid,margin));
    }
}
