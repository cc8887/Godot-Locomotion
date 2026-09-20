namespace GodotAls.Core.Locomotion;

// Float arithmetic boundaries from UAlsMath and UE FMath, kept separate from
// older generic helpers whose exact exponential has different native behavior.
internal static class AlsRefactoredRigMath
{
    // The native optimized build evaluates the cubic in Horner form. Keeping
    // expanded powers changes the float damper alpha by up to two ULPs, which
    // the actual Rig Unit trajectory detects at 30/60 Hz.
    public static float InvExp(float x) =>
        1f / (1f + x * (1.00746054f + x * (.45053901f + x * .25724632f)));

    public static float DamperAlpha(float delta, float halfLife) =>
        1f - InvExp(.69314718056f / (halfLife + 1e-8f) * delta);

    public static (float Sin, float Cos) SinCos(float value)
    {
        var quotient = (.31830988618f * .5f) * value;
        quotient = (float)(long)(value >= 0 ? quotient + .5f : quotient - .5f);
        var y = value - 6.28318530718f * quotient;
        float sign;
        if (y > 1.57079632679f) { y = 3.14159265359f - y; sign = -1; }
        else if (y < -1.57079632679f) { y = -3.14159265359f - y; sign = -1; }
        else sign = 1;
        var y2 = y * y;
        var sine = (((((-2.3889859e-8f * y2 + 2.7525562e-6f) * y2 - .00019840874f) * y2 + .0083333310f) * y2 - .16666667f) * y2 + 1f) * y;
        var cosine = ((((-2.6051615e-7f * y2 + 2.4760495e-5f) * y2 - .0013888378f) * y2 + .041666638f) * y2 - .5f) * y2 + 1f;
        return (sine, sign * cosine);
    }
}
