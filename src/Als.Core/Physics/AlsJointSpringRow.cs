namespace GodotAls.Core.Physics;

// Scalar cached Chaos soft-row algebra. Lambda is an angle/position impulse,
// not a velocity impulse; callers divide its rotation correction by dt when
// transporting it to a velocity-based backend. This alone is not a solver.
public readonly record struct AlsJointSpringRow(double StiffnessDt2, double DampingDt,
    double Denominator, double MaxLambda)
{
    public static AlsJointSpringRow Create(double dt, double inverseEffectiveInertia,
        double stiffness, double damping, bool accelerationMode, double maxTorque = 0)
    {
        if (!double.IsFinite(dt) || dt <= 0 || !double.IsFinite(inverseEffectiveInertia) || inverseEffectiveInertia <= 0 ||
            !double.IsFinite(stiffness) || stiffness < 0 || !double.IsFinite(damping) || damping < 0 ||
            !double.IsFinite(maxTorque) || maxTorque < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        var scale = accelerationMode ? 1/inverseEffectiveInertia : 1;
        var k = scale*stiffness*dt*dt; var c = scale*damping*dt;
        // Native zero (and UE_MAX_FLT) means unlimited, not zero motor torque.
        var max = maxTorque > 0 && maxTorque < float.MaxValue ? maxTorque*dt*dt*scale : 0;
        var denominator=1+(k+c)*inverseEffectiveInertia;
        if(!double.IsFinite(k)||!double.IsFinite(c)||!double.IsFinite(denominator)||!double.IsFinite(max))
            throw new ArgumentOutOfRangeException(nameof(dt),"Joint spring coefficients overflowed.");
        return new(k,c,denominator,max);
    }

    // relativeRotationDelta = (child - parent - target) integrated over dt.
    // DeltaLambda is positive when it rotates parent toward child and child
    // toward parent. The accumulated lambda must reset for each physics step.
    public double Solve(double error, double relativeRotationDelta, ref double lambda)
    {
        var delta = (StiffnessDt2*error+DampingDt*relativeRotationDelta-lambda)/Denominator;
        var next = lambda+delta;
        if (MaxLambda > 0) next = System.Math.Clamp(next,-MaxLambda,MaxLambda);
        delta = next-lambda; lambda = next; return delta;
    }
}
