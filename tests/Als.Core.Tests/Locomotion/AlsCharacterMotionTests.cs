using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterMotionTests
{
    private static readonly AlsCharacterVelocitySettings Velocity=new(600,0,0,false,0,0,0,.02f);
    private static readonly AlsCharacterMotionSettings Settings=new(Velocity,Velocity with{MaxSpeed=300},
        new(Velocity,2000,1,0,0,-1000,4000),2000,100);
    private static AlsCharacterMotion Controller(AlsCharacterMotionSettings? settings=null)=>new(settings??Settings);

    [Theory]
    [InlineData(0,0)]
    [InlineData(.25,.25)]
    [InlineData(1,1)]
    [InlineData(2,1)]
    public void ConsumedInputRetainsAccelerationButCapsAnalog(double input,float analog)
    {
        var step=Controller().Advance(default,new(input,0,0),true,false,false,false,.02f);
        Assert.Equal(new AlsDoubleVector(input*2000,0,0),step.Acceleration);Assert.Equal(analog,step.Analog);
    }
    [Theory]
    [InlineData(false,39.99999999999999)]
    [InlineData(true,19.999999999999996)]
    public void GroundedUsesTheActualStanceSpeedAndRemovesVerticalVelocity(bool crouching,double speed)
    {
        var settings=Settings with{Standing=Velocity with{MaxSpeed=40},Crouching=Velocity with{MaxSpeed=20}};
        var step=Controller(settings).Advance(new(0,0,30),new(1,0,0),true,crouching,false,false,.05f);
        Assert.False(step.Falling);Assert.Equal(new AlsDoubleVector(speed,0,0),step.Velocity);
        Assert.Equal(step.Velocity*.05f,step.Displacement);
    }
    [Fact]
    public void JumpStartsGravityFromTheImpulseAndUsesMidpointDisplacement()
    {
        var step=Controller().Advance(default,default,true,false,true,false,.02f);
        Assert.True(step.Falling);Assert.Equal(100,step.StartVelocity.Z);
        Assert.Equal(80,step.Velocity.Z,5);Assert.Equal(1.8,step.Displacement.Z,6);
    }
    [Theory]
    [InlineData(0,100)]
    [InlineData(150,150)]
    public void JumpKeepsAnAlreadyFasterUpwardVelocity(double z,double expected)
    {
        var step=Controller().Advance(new(25,-10,z),default,true,false,true,false,.02f);
        Assert.Equal(new AlsDoubleVector(25,-10,expected),step.StartVelocity);
    }
    [Theory]
    [InlineData(false,false,true)]
    [InlineData(true,true,false)]
    [InlineData(false,true,true)]
    public void FallingOrCrouchingDoesNotApplyAnotherJump(bool grounded,bool crouching,bool falling)
    {
        var step=Controller().Advance(new(0,0,-10),default,grounded,crouching,true,false,.02f);
        Assert.Equal(-10,step.StartVelocity.Z);Assert.Equal(falling,step.Falling);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootMotionSuppressesLateralInputAndBrakingWhileAirStillFalls(bool grounded)
    {
        var braking=Velocity with{Friction=4,SeparateBrakingFriction=true,BrakingFriction=8,BrakingFrictionFactor=2,BrakingDeceleration=1000};
        var settings=Settings with{Standing=braking,Falling=Settings.Falling with{Velocity=braking}};
        var step=Controller(settings).Advance(new(125,-60,30),new(-1,0,0),grounded,false,false,true,.02f);
        Assert.Equal(-2000,step.Acceleration.X);Assert.Equal(1,step.Analog);
        Assert.Equal(125,step.Velocity.X);Assert.Equal(-60,step.Velocity.Y);
        Assert.Equal(grounded?0:10,step.Velocity.Z,5);
        if(!grounded)Assert.Equal(.4,step.Displacement.Z,6);
    }
    [Fact]
    public void NativeCollisionReceiptHasPriorityOverAllFallbacks()
    {
        var step=Controller().Advance(default,default,true,false,true,false,.02f);
        var native=new AlsDoubleVector(12,34,-56);
        Assert.Equal(native,AlsCharacterMotion.ResolveVelocity(step,new(1,2,3),native,8,true,true));
    }
    [Theory]
    [InlineData(0,false,false,true)]
    [InlineData(0,true,false,false)]
    [InlineData(1,false,false,false)]
    [InlineData(0,false,true,false)]
    public void CollisionRootOrLandingChoosesPhysicalVelocityOtherwiseKeepsPrecision(int collisions,bool grounded,bool root,bool retain)
    {
        var step=Controller().Advance(new(125,0,-40),default,false,false,false,false,.02f);
        var actual=new AlsDoubleVector(124.9999,0,0);
        Assert.Equal(retain?step.Velocity:actual,AlsCharacterMotion.ResolveVelocity(step,actual,null,collisions,grounded,root));
    }
    [Fact]
    public void GroundedNonnegativeIntegratedVelocityRetainsPrecisionWithoutCollisions()
    {
        var step=Controller().Advance(default,new(.3,0,0),true,false,false,false,.02f);
        Assert.Equal(step.Velocity,AlsCharacterMotion.ResolveVelocity(step,new(12.000001,0,0),null,0,true,false));
    }
    [Fact]
    public void RepeatedPhysicalInputDoesNotAccumulateAHiddenJumpOrVelocityClock()
    {
        var controller=Controller();var first=controller.Advance(default,new(.2,0,0),true,false,true,false,.02f);
        controller.Advance(new(100,50,-10),default,false,true,false,true,.05f);
        Assert.Equal(first,controller.Advance(default,new(.2,0,0),true,false,true,false,.02f));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(.051f)]
    [InlineData(float.NaN)]
    public void InvalidPhysicalDeltaIsRejected(float delta)=>
        Assert.Throws<ArgumentException>(()=>Controller().Advance(default,default,true,false,false,false,delta));
    [Fact]
    public void SharedAlsInputAmountRetainsZeroLimitAndUnclampedMagnitude()
    {
        Assert.Equal(0,AlsCharacterVelocity.InputAmount(new(30,40,0),0));
        Assert.Equal(.5,AlsCharacterVelocity.InputAmount(new(30,40,0),100));
        Assert.Equal(2,AlsCharacterVelocity.InputAmount(new(120,160,0),100));
    }
    [Fact]
    public void InvalidInputAndCollisionReceiptAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>Controller().Advance(default,new(double.NaN,0,0),true,false,false,false,.02f));
        Assert.Throws<ArgumentException>(()=>AlsCharacterMotion.ResolveVelocity(default,default,null,-1,true,false));
        Assert.Throws<ArgumentException>(()=>AlsCharacterVelocity.InputAmount(default,-1));
    }
}
