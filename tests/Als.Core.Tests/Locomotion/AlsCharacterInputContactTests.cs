using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterInputContactTests
{
    private sealed class Contacts(params Vector3[] normals):IAlsCharacterContactNormals
    {
        public readonly List<int> Reads=[];
        public int Count=>normals.Length;
        public Vector3 Normal(int i){Reads.Add(i);return normals[i];}
    }
    [Theory]
    [InlineData(0,0,1,0,0)]
    [InlineData(.2f,.3f,1,.2f,.3f)]
    [InlineData(3,4,1,.6f,.8f)]
    [InlineData(3,4,.5f,.3f,.4f)]
    [InlineData(3,4,0,0,0)]
    public void WorldInputLimitsBeforeApplyingTheRequestedScale(float x,float z,float scale,float expectedX,float expectedZ)
    {
        var v=AlsCharacterInput.Consume(new(x,0,z),Vector3.UnitY,1.2f,true,scale);
        Assert.Equal(expectedX,v.X);Assert.Equal(0,v.Y);Assert.Equal(expectedZ,v.Z);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void LocalInputUsesRightHandedRadiansAtTheFloatBoundary(int sign)
    {
        var v=AlsCharacterInput.Consume(Vector3.UnitX,Vector3.UnitY,sign*MathF.PI/2,false,1);
        Assert.Equal(0,v.X,6);Assert.Equal(0,v.Y);Assert.Equal(-sign,v.Z,6);
    }
    [Fact]
    public void WorldInputDoesNotApplyActorYaw()
    {
        var input=new Vector3(.2f,0,.3f);
        Assert.Equal(input,AlsCharacterInput.Consume(input,Vector3.UnitY,2.5f,true,1));
    }
    [Fact]
    public void ZeroDirectionAndIdentityScaleRetainSignedComponents()
    {
        var input=new Vector3(-0f,0,-0f);var output=AlsCharacterInput.Consume(input,Vector3.UnitY,0,true,1);
        Assert.Equal(BitConverter.SingleToInt32Bits(input.X),BitConverter.SingleToInt32Bits(output.X));
        Assert.Equal(BitConverter.SingleToInt32Bits(input.Z),BitConverter.SingleToInt32Bits(output.Z));
    }
    [Fact]
    public void LimitingRetainsAShortVectorAndUsesTheRequestedMaximum()
    {
        var v=new Vector3(.2f,0,.3f);Assert.Equal(v,AlsCharacterSweepMath.LimitLength(v));
        Assert.Equal(new Vector3(1.2f,0,1.6f),AlsCharacterSweepMath.LimitLength(new(3,0,4),2));
    }
    [Fact]
    public void RotationSupportsOtherUpAxesWithoutASeparateInputClock()
    {
        var v=AlsCharacterInput.Consume(Vector3.UnitX,Vector3.UnitZ,MathF.PI/2,false,1);
        Assert.Equal(0,v.X,6);Assert.Equal(1,v.Y,6);Assert.Equal(0,v.Z);
    }
    [Theory]
    [InlineData(float.NaN,1)]
    [InlineData(0,-1)]
    [InlineData(0,float.PositiveInfinity)]
    public void InvalidInputParametersAreRejected(float yaw,float scale)=>
        Assert.Throws<ArgumentException>(()=>AlsCharacterInput.Consume(default,Vector3.UnitY,yaw,false,scale));
    [Fact]
    public void InputRejectsNonfiniteDirectionsAndUnnormalizedUp()
    {
        Assert.Throws<ArgumentException>(()=>AlsCharacterInput.Consume(new(float.NaN,0,0),Vector3.UnitY,0,true,1));
        Assert.Throws<ArgumentException>(()=>AlsCharacterInput.Consume(default,Vector3.Zero,0,true,1));
    }
    [Theory]
    [InlineData(-3,0)]
    [InlineData(3,3)]
    [InlineData(0,0)]
    public void ContactOnlyRemovesVelocityPointingIntoThePlane(float x,float expected)
    {
        var c=new Contacts(Vector3.UnitX);var v=AlsCharacterContactVelocity.Resolve(new(x,2,1),false,c);
        Assert.Equal(new Vector3(expected,2,1),v);Assert.Equal(new[]{0},c.Reads);
    }
    [Fact]
    public void ActualContactOrderIsPreservedWhenPlanesAreNotOrthogonal()
    {
        var n=AlsCharacterSweepMath.Normalize(new(-1,1,0));
        var a=new Contacts(Vector3.UnitX,n);var b=new Contacts(n,Vector3.UnitX);
        var va=AlsCharacterContactVelocity.Resolve(new(-2,-3,1),false,a);
        var vb=AlsCharacterContactVelocity.Resolve(new(-2,-3,1),false,b);
        Assert.Equal(-1.5f,va.X,5);Assert.Equal(-1.5f,va.Y,5);
        Assert.Equal(0,vb.X);Assert.Equal(-2.5f,vb.Y,5);Assert.Equal(1,va.Z);Assert.Equal(1,vb.Z);
        Assert.Equal(new[]{0,1},a.Reads);Assert.Equal(new[]{0,1},b.Reads);
    }
    [Theory]
    [InlineData(true,-2,0)]
    [InlineData(false,-2,-2)]
    [InlineData(true,2,2)]
    public void GroundedClampFollowsContactsAndRetainsUpwardMotion(bool grounded,float y,float expected)
    {
        var v=AlsCharacterContactVelocity.Resolve(new(2,y,3),grounded,new Contacts());
        Assert.Equal(new Vector3(2,expected,3),v);
    }
    [Fact]
    public void GroundClampRunsAfterTheFinalObliqueContact()
    {
        var n=AlsCharacterSweepMath.Normalize(new(1,-1,0));
        var v=AlsCharacterContactVelocity.Resolve(new(-2,0,3),true,new Contacts(n));
        Assert.Equal(-1,v.X,6);Assert.Equal(0,v.Y);Assert.Equal(3,v.Z);
    }
    [Fact]
    public void ContactResolverRejectsMissingPhysicalProvider()=>
        Assert.Throws<ArgumentNullException>(()=>AlsCharacterContactVelocity.Resolve(default,false,null!));
}
