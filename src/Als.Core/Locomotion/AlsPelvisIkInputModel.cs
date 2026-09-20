namespace GodotAls.Core.Locomotion;

public readonly record struct AlsPelvisIkInputState(double Alpha, AlsDoubleVector Offset);

// SetPelvisIKOffset uses Blueprint doubles and UE world vectors in centimeters.
// Conversion to Godot axes/meters belongs at the eventual bone-control boundary.
// This is the original input function, not the legacy constrained foot-placement model.
public sealed class AlsPelvisIkInputModel
{
    public string LeftCurve { get; }
    public string RightCurve { get; }
    public float UpSpeed { get; }
    public float DownSpeed { get; }
    public AlsPelvisIkInputState InitialState { get; }

    public AlsPelvisIkInputModel(string leftCurve, string rightCurve, float upSpeed, float downSpeed,
        AlsPelvisIkInputState initialState)
    {
        if (string.IsNullOrWhiteSpace(leftCurve) || string.IsNullOrWhiteSpace(rightCurve) || leftCurve == rightCurve ||
            !float.IsFinite(upSpeed) || upSpeed <= 0 || !float.IsFinite(downSpeed) || downSpeed <= 0 ||
            !double.IsFinite(initialState.Alpha) || !initialState.Offset.IsFinite)
            throw new ArgumentException("Invalid original pelvis input settings.");
        LeftCurve = leftCurve; RightCurve = rightCurve; UpSpeed = upSpeed; DownSpeed = downSpeed; InitialState = initialState;
    }

    public AlsPelvisIkInputState Evaluate(in AlsPelvisIkInputState previous, in AlsDoubleVector leftTarget,
        in AlsDoubleVector rightTarget, float leftCurve, float rightCurve, double delta)
    {
        if (!double.IsFinite(previous.Alpha) || !previous.Offset.IsFinite || !leftTarget.IsFinite || !rightTarget.IsFinite ||
            !float.IsFinite(leftCurve) || !float.IsFinite(rightCurve) || !double.IsFinite(delta) ||
            !float.IsFinite((float)delta) || delta < 0)
            throw new ArgumentException("Invalid pelvis function input.");
        // GetCurveValue returns float, promoted before Blueprint Add/Divide.
        // No per-foot or pelvis clamp is present in this property function.
        var alpha = ((double)leftCurve + rightCurve) / 2;
        if (alpha <= 0) return new(alpha, default);
        // Less, not LessEqual: ties select the right full vector (including X/Y).
        var target = leftTarget.Z < rightTarget.Z ? leftTarget : rightTarget;
        var speed = target.Z > previous.Offset.Z ? UpSpeed : DownSpeed;
        return new(alpha, AlsFootIkMath.Interpolate(previous.Offset, target, (float)delta, speed));
    }
}
