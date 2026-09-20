using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsGroundedRates(float Stride, float StandingPlayRate, float CrouchingPlayRate);

public sealed class AlsGroundedRateFunctions
{
    private readonly Vector3 _speeds;
    private readonly AlsMovementInputCurve _walk, _run, _crouch;
    private readonly AlsMovementInputFunctions _functions;
    public AlsGroundedRateFunctions(Vector3 animatedSpeeds, AlsMovementInputCurve walk, AlsMovementInputCurve run,
        AlsMovementInputCurve crouch, AlsMovementInputFunctions functions)
    {
        if (!float.IsFinite(animatedSpeeds.LengthSquared()) || animatedSpeeds.X <= 0 || animatedSpeeds.Y <= 0 || animatedSpeeds.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(animatedSpeeds));
        _speeds = animatedSpeeds; _walk = walk ?? throw new ArgumentNullException(nameof(walk));
        _run = run ?? throw new ArgumentNullException(nameof(run)); _crouch = crouch ?? throw new ArgumentNullException(nameof(crouch));
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
    }

    // Curves come from the previous completed pose; speed and component scale are current inputs.
    public AlsGroundedRates Evaluate(float speedMetersPerSecond, float weightGait, float basePoseCrouch, float meshVerticalScale)
    {
        Finite(speedMetersPerSecond); Finite(weightGait); Finite(basePoseCrouch); Finite(meshVerticalScale);
        var run = System.Math.Clamp((double)weightGait - 1, 0, 1);
        var sprint = System.Math.Clamp((double)weightGait - 2, 0, 1);
        var curveSpeed = speedMetersPerSecond * 100;
        var stride = (float)Lerp(Lerp(_walk.Sample(curveSpeed), _run.Sample(curveSpeed), run), _crouch.Sample(curveSpeed), basePoseCrouch);
        var rate = Lerp(Lerp((double)speedMetersPerSecond / _speeds.X, (double)speedMetersPerSecond / _speeds.Y, run),
            (double)speedMetersPerSecond / _speeds.Z, sprint);
        var standing = (float)System.Math.Clamp(Divide(Divide(rate, stride), meshVerticalScale), 0, 3);
        return new(stride, standing, _functions.CrouchingPlayRate(speedMetersPerSecond, stride, meshVerticalScale));
    }
    private static double Lerp(double a, double b, double alpha) => a + alpha * (b - a);
    private static double Divide(double value, double divisor) => divisor == 0 ? 0 : value / divisor;
    private static void Finite(float value)
    { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
}
