namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootLockInputState(double Alpha, AlsDoubleVector Location, AlsAimingRotation Rotation);
public readonly record struct AlsFootLockObservation(float EnableCurve, float LockCurve,
    AlsDoubleVector FootComponentLocation, AlsAimingRotation FootComponentRotation,
    AlsDoubleVector WorldVelocity, float WorldDelta, AlsAimingRotation ComponentRotation,
    AlsAimingRotation ActorRotation, AlsAimingRotation LastMovementRotation, bool MovingOnGround);

// SetFootLocking + SetFootLockOffsets. All vectors use UE axes/centimeters and
// all rotators use UE degrees. Physics/component observations are captured by
// the caller; this model performs no queries and owns no committed history.
public sealed class AlsFootLockInputModel
{
    public double CaptureThreshold { get; }
    public AlsFootLockInputModel(double captureThreshold)
    {
        if (!double.IsFinite(captureThreshold) || captureThreshold <= 0 || captureThreshold > 1)
            throw new ArgumentException("Invalid original foot-lock threshold.");
        CaptureThreshold = captureThreshold;
    }

    public AlsFootLockInputState Evaluate(in AlsFootLockInputState previous, in AlsFootLockObservation input)
    {
        Validate(previous);
        if (!float.IsFinite(input.EnableCurve) || !float.IsFinite(input.LockCurve) ||
            !input.FootComponentLocation.IsFinite || !input.FootComponentRotation.Finite || !input.WorldVelocity.IsFinite ||
            !float.IsFinite(input.WorldDelta) || input.WorldDelta < 0 || !input.ComponentRotation.Finite ||
            !input.ActorRotation.Finite || !input.LastMovementRotation.Finite)
            throw new ArgumentException("Invalid foot-lock observations.");
        // Disabled IK does not write the in/out references or compensate them.
        if (input.EnableCurve <= 0) return previous;
        var next = previous;
        if (input.LockCurve >= CaptureThreshold || input.LockCurve < previous.Alpha)
            next = next with { Alpha = input.LockCurve };
        // Capture on every full-lock update, not only on the rising edge.
        if (next.Alpha >= CaptureThreshold)
            next = next with { Location = input.FootComponentLocation, Rotation = input.FootComponentRotation };
        if (next.Alpha > 0)
        {
            // RotationDifference is function-local and stays zero off the ground.
            var rotation = input.MovingOnGround ? AlsAimingRotation.Delta(input.ActorRotation, input.LastMovementRotation) : default;
            var translation = Unrotate(input.WorldVelocity * input.WorldDelta, input.ComponentRotation);
            // BreakRotator/RotateAngleAxis pass yaw through float. LocalRotation
            // compensation uses the full double rotator difference separately.
            var angle = (double)(float)rotation.Yaw * (System.Math.PI / 180);
            var s = System.Math.Sin(angle); var c = System.Math.Cos(angle);
            var location = next.Location - translation;
            location = new(c * location.X + s * location.Y, -s * location.X + c * location.Y, location.Z);
            next = next with { Location = location, Rotation = AlsAimingRotation.Delta(next.Rotation, rotation) };
        }
        Validate(next); return next;
    }

    private static AlsDoubleVector Unrotate(AlsDoubleVector vector, AlsAimingRotation rotation)
    {
        // UE5.9 Win64 FRotationMatrix multiplies even its double rotator by
        // GlobalVectorConstants::DEG_TO_RAD (a float constant, then promoted).
        // RotateAngleAxis above uses the separate scalar double conversion.
        const double radians = System.MathF.PI / 180f;
        var sp = System.Math.Sin(rotation.Pitch * radians); var cp = System.Math.Cos(rotation.Pitch * radians);
        var sy = System.Math.Sin(rotation.Yaw * radians); var cy = System.Math.Cos(rotation.Yaw * radians);
        var sr = System.Math.Sin(rotation.Roll * radians); var cr = System.Math.Cos(rotation.Roll * radians);
        // FRotationMatrix transpose, preserving the native pitch/roll signs.
        return new(vector.X * (cp * cy) + vector.Y * (cp * sy) + vector.Z * sp,
            vector.X * (sr * sp * cy - cr * sy) + vector.Y * (sr * sp * sy + cr * cy) - vector.Z * (sr * cp),
            -vector.X * (cr * sp * cy + sr * sy) + vector.Y * (cy * sr - cr * sp * sy) + vector.Z * (cr * cp));
    }
    private static void Validate(AlsFootLockInputState value)
    {
        if (!double.IsFinite(value.Alpha) || !value.Location.IsFinite || !value.Rotation.Finite)
            throw new ArgumentException("Invalid foot-lock state.");
    }
}
