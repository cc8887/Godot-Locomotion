using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointMassConditioningTests
{
    [Fact]
    public void ConditioningRemapsMiddleInertiaAndPreservesFixedParent()
    {
        var result = AlsJointMassConditioning.Apply(default,new(1,new(1,.5,.05)),.2,5);
        Assert.Equal(default,result.Parent);
        Assert.Equal(1,result.Child.Mass);
        Assert.Equal(.25,result.Child.Inertia.X,12);
        Assert.Equal(1/(4+16.0/19),result.Child.Inertia.Y,12);
        Assert.Equal(.05,result.Child.Inertia.Z,12);
    }

    [Fact]
    public void LightParentIsConditionedLocallyWithoutChangingChild()
    {
        var child = new AlsJointInverseMass(.1,new(.01,.01,.01));
        var result = AlsJointMassConditioning.Apply(new(10,new(1,1,1)),child,.2,5);
        Assert.Equal(.5,result.Parent.Mass,12);
        Assert.Equal(new(.05,.05,.05),result.Parent.Inertia);
        Assert.Equal(child,result.Child);
    }

    [Fact]
    public void DisabledConditioningAndUnitConversionPreserveResponse()
    {
        var parent = new AlsJointInverseMass(.3,new(.5,.02,.003));
        var child = new AlsJointInverseMass(.1,new(.002,.003,.001));
        var disabled = AlsJointMassConditioning.Apply(parent,child,0,0);
        Assert.Equal(parent,disabled.Parent); Assert.Equal(child,disabled.Child);
        var native = AlsJointMassConditioning.Apply(parent,child,.2,5);
        var meters = AlsJointMassConditioning.Apply(parent with {Inertia=parent.Inertia*10000},child with {Inertia=child.Inertia*10000},.2,5);
        Assert.True((meters.Parent.Inertia-native.Parent.Inertia*10000).NearlyZero(1e-10));
        Assert.True((meters.Child.Inertia-native.Child.Inertia*10000).NearlyZero(1e-10));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointMassConditioning.Apply(new(1,default),child,.2,5));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointMassConditioning.Apply(parent,child,.2,.5));
    }

    [Fact]
    public void ConditioningAllocatesNoManagedMemory()
    {
        var p=new AlsJointInverseMass(10,new(1,.5,.05));var c=new AlsJointInverseMass(.1,new(.01,.02,.001));
        for(var i=0;i<100;i++)AlsJointMassConditioning.Apply(p,c,.2,5);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++)AlsJointMassConditioning.Apply(p,c,.2,5);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
