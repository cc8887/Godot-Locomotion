namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootOffsetInputState(AlsDoubleVector Location, AlsAimingRotation Rotation);
public readonly record struct AlsFootOffsetUpdate(AlsFootOffsetInputState State, AlsDoubleVector LocationTarget);
public readonly record struct AlsFootTraceSegment(AlsDoubleVector Start, AlsDoubleVector End);
public readonly record struct AlsFootOffsetObservation(float EnableCurve, AlsDoubleVector FootWorldLocation,
    AlsDoubleVector RootWorldLocation, bool Walkable, AlsDoubleVector ImpactPoint, AlsDoubleVector ImpactNormal);
public readonly record struct AlsFootIkPropertyState(AlsFootLockInputState LeftLock, AlsFootLockInputState RightLock,
    AlsFootOffsetInputState LeftOffset, AlsFootOffsetInputState RightOffset, AlsPelvisIkInputState Pelvis);

public static class AlsFootIkMath
{
    // Native FVector centimeters, VInterpTo float delta/speed and snap threshold.
    public static AlsDoubleVector Interpolate(AlsDoubleVector current, AlsDoubleVector target, float delta, float speed)
    {
        if (!current.IsFinite || !target.IsFinite || !float.IsFinite(delta) || delta < 0 || !float.IsFinite(speed))
            throw new ArgumentException("Invalid native foot vector interpolation.");
        var distance = target - current;
        var result = speed <= 0 || distance.LengthSquared < 1e-4f ? target :
            current + distance * System.Math.Clamp(delta * speed, 0f, 1f);
        if (!result.IsFinite) throw new ArgumentException("Nonfinite foot interpolation result.");
        return result;
    }
}

// SetFootOffsets. LocationTarget is a caller-owned in/out function argument;
// UpdateFootIK starts it at zero each invocation, not at last frame's target.
public sealed class AlsFootOffsetInputModel
{
    public double FootHeight { get; }
    public double TraceAbove { get; }
    public double TraceBelow { get; }
    public float DownSpeed { get; }
    public float UpSpeed { get; }
    public float RotationSpeed { get; }
    public AlsFootOffsetInputModel(double height, double above, double below, float down, float up, float rotation)
    {
        if (!double.IsFinite(height) || height < 0 || !double.IsFinite(above) || above < 0 || !double.IsFinite(below) || below < 0 ||
            !float.IsFinite(down) || down <= 0 || !float.IsFinite(up) || up <= 0 || !float.IsFinite(rotation) || rotation <= 0)
            throw new ArgumentException("Invalid original foot-offset settings.");
        FootHeight = height; TraceAbove = above; TraceBelow = below;
        DownSpeed = down; UpSpeed = up; RotationSpeed = rotation;
    }
    public AlsFootTraceSegment Trace(AlsDoubleVector foot, AlsDoubleVector root)
    {
        if (!foot.IsFinite || !root.IsFinite) throw new ArgumentException("Invalid foot trace origins.");
        return new(new(foot.X, foot.Y, root.Z + TraceAbove), new(foot.X, foot.Y, root.Z - TraceBelow));
    }
    public AlsFootOffsetUpdate Evaluate(in AlsFootOffsetInputState previous, AlsDoubleVector locationTarget,
        in AlsFootOffsetObservation input, double delta)
    {
        if (!previous.Location.IsFinite || !previous.Rotation.Finite || !locationTarget.IsFinite ||
            !float.IsFinite(input.EnableCurve) || !input.FootWorldLocation.IsFinite || !input.RootWorldLocation.IsFinite ||
            !input.ImpactPoint.IsFinite || !input.ImpactNormal.IsFinite || !double.IsFinite(delta) || delta < 0 || !float.IsFinite((float)delta))
            throw new ArgumentException("Invalid foot-offset input.");
        if (input.EnableCurve <= 0) return new(default, locationTarget);
        var rotationTarget = default(AlsAimingRotation);
        if (input.Walkable)
        {
            var floor = new AlsDoubleVector(input.FootWorldLocation.X, input.FootWorldLocation.Y, input.RootWorldLocation.Z);
            locationTarget = input.ImpactPoint + input.ImpactNormal * FootHeight - (floor + new AlsDoubleVector(0, 0, FootHeight));
            const double degrees = 180 / System.Math.PI;
            // DegAtan2 is double; MakeRotator takes float channels.
            rotationTarget = new((float)(-System.Math.Atan2(input.ImpactNormal.X, input.ImpactNormal.Z) * degrees), 0,
                (float)(System.Math.Atan2(input.ImpactNormal.Y, input.ImpactNormal.Z) * degrees));
        }
        var speed = previous.Location.Z > locationTarget.Z ? DownSpeed : UpSpeed;
        return new(new(AlsFootIkMath.Interpolate(previous.Location, locationTarget, (float)delta, speed),
            AlsAimingInputModel.InterpolateRotation(previous.Rotation, rotationTarget, (float)delta, RotationSpeed)), locationTarget);
    }
}

// This asset's ResetIKOffsets is asymmetric. UE ProcessEvent confirms it resets
// left offset location, right LOCK location and left offset rotation twice.
// Preserve that source behavior explicitly; do not silently invent a symmetric reset.
public sealed class AlsFootIkResetModel
{
    public float Speed { get; }
    public AlsFootIkResetModel(float speed)
    {
        if (!float.IsFinite(speed) || speed <= 0) throw new ArgumentException("Invalid IK reset speed.");
        Speed = speed;
    }
    public AlsFootIkPropertyState Evaluate(in AlsFootIkPropertyState previous, double delta)
    {
        if (!double.IsFinite(delta) || delta < 0 || !float.IsFinite((float)delta)) throw new ArgumentException("Invalid IK reset delta.");
        var rotation = AlsAimingInputModel.InterpolateRotation(previous.LeftOffset.Rotation, default, (float)delta, Speed);
        rotation = AlsAimingInputModel.InterpolateRotation(rotation, default, (float)delta, Speed);
        return previous with
        {
            LeftOffset = new(AlsFootIkMath.Interpolate(previous.LeftOffset.Location, default, (float)delta, Speed), rotation),
            RightLock = previous.RightLock with { Location = AlsFootIkMath.Interpolate(previous.RightLock.Location, default, (float)delta, Speed) }
        };
    }
}
