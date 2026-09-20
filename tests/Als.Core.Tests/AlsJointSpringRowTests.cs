using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointSpringRowTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ImplicitAccelerationSpringMatchesClosedFormAndIsInertiaIndependent(int hz)
    {
        var dt=1.0/hz; const double k=5000000,c=5000,error=.4,relativeSpeed=3;
        foreach(var inverse in new[]{.02,1,350})
        {
            var row=AlsJointSpringRow.Create(dt,inverse,k,c,true); double lambda=0;
            var delta=row.Solve(error,relativeSpeed*dt,ref lambda);
            var expected=(k*dt*dt*error+c*dt*dt*relativeSpeed)/(1+k*dt*dt+c*dt);
            Assert.InRange(System.Math.Abs(delta*inverse-expected),0,1e-12);
            // A second iteration after applying this row's correction must not
            // apply a second full impulse to the same constraint.
            var repeat=row.Solve(error-inverse*delta,relativeSpeed*dt-inverse*delta,ref lambda);
            Assert.InRange(System.Math.Abs(repeat),0,1e-12);
        }
    }
    [Fact]
    public void ForceModeAndAccelerationModeHaveDifferentMassAndTorqueSemantics()
    {
        var a=AlsJointSpringRow.Create(.02,4,100,2,true,3);
        var f=AlsJointSpringRow.Create(.02,4,100,2,false,3);
        Assert.Equal(.0003,a.MaxLambda,12); Assert.Equal(.0012,f.MaxLambda,12);
        double la=0,lf=0;
        Assert.Equal(a.MaxLambda,a.Solve(100,0,ref la));
        Assert.Equal(f.MaxLambda,f.Solve(100,0,ref lf));
        a.Solve(-100,0,ref la); Assert.Equal(-a.MaxLambda,la);
        Assert.Equal(0,AlsJointSpringRow.Create(.02,4,100,2,true).MaxLambda);
    }
    [Fact]
    public void PureDampingReducesRelativeEnergyAndInvalidStepIsRejected()
    {
        var row=AlsJointSpringRow.Create(1.0/60,3,0,20,true); double lambda=0;
        var correction=row.Solve(0,2.0/60,ref lambda)*3;
        Assert.InRange(2-correction*60,0,2);
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointSpringRow.Create(0,1,1,1,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointSpringRow.Create(.02,0,1,1,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointSpringRow.Create(.02,1,double.NaN,1,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>AlsJointSpringRow.Create(double.MaxValue,1,1,1,true));
    }
}
