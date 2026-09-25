using System.Numerics;
using M = System.Math;

namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredMovementDirection { Forward, Backward, Left, Right }

// Original UE axes and centimetres, before renderer conversion. Curve values
// are the previously committed animation curves, not this frame's future pose.
public readonly record struct AlsRefactoredMovementInput(
    AlsDoubleVector Velocity, AlsDoubleVector Acceleration, AlsQuaternion Rotation,
    float Speed, float Scale, float VelocityYaw, double ViewYaw, float MaxAcceleration, float MaxBraking,
    string Gait, bool VelocityDirectionMode, bool PendingUpdate, float Delta,
    float RunningAmount, float SprintingAmount, float HipsLock, float SprintBlock);

public readonly record struct AlsRefactoredMovementState
{
    public bool VelocityInitialized { get; init; }
    public Vector4 VelocityBlend { get; init; } // F/B/L/R
    public Vector2 Lean { get; init; } // right/forward
    public AlsRefactoredMovementDirection Direction { get; init; }
    public float HipsLock { get; init; }
    public Vector4 YawOffsets { get; init; } // F/B/L/R
    public float StandingStride { get; init; }
    public float WalkRun { get; init; }
    public float StandingRate { get; init; }
    public float SprintBlock { get; init; }
    public float SprintTime { get; init; }
    public float SprintAcceleration { get; init; }
    public float CrouchingStride { get; init; }
    public float CrouchingRate { get; init; }
    public static AlsRefactoredMovementState Initial => new() { StandingRate = 1, CrouchingRate = 1 };
}

/// <summary>Original Parent refresh arithmetic. The graph owner controls which
/// function runs, its order and relevance; these functions never infer an update.</summary>
public static class AlsRefactoredMovementModel
{
    public static void Validate(in AlsRefactoredMovementInput i)
    {
        if (!i.Velocity.IsFinite || !i.Acceleration.IsFinite || !double.IsFinite(i.Rotation.LengthSquared) ||
            M.Abs(i.Rotation.LengthSquared - 1) > 1e-6 || !float.IsFinite(i.Speed) || i.Speed < 0 ||
            !float.IsFinite(i.Scale) || i.Scale <= 0 || !float.IsFinite(i.VelocityYaw) || !double.IsFinite(i.ViewYaw) ||
            !float.IsFinite(i.MaxAcceleration) || i.MaxAcceleration < 0 || !float.IsFinite(i.MaxBraking) || i.MaxBraking < 0 ||
            i.Gait is null || !float.IsFinite(i.Delta) || i.Delta < 0 || !float.IsFinite(i.RunningAmount) ||
            !float.IsFinite(i.SprintingAmount) || !float.IsFinite(i.HipsLock) || !float.IsFinite(i.SprintBlock))
            throw new ArgumentException("Invalid native movement input.");
    }

    public static Vector2 AccelerationAmount(in AlsRefactoredMovementInput i)
    {
        var maximum = AlsDoubleVector.Dot(i.Acceleration, i.Velocity) >= 0 ? i.MaxAcceleration : i.MaxBraking;
        if (maximum <= .0001f) return default;
        var acceleration = i.Acceleration.Rotate(i.Rotation.Conjugate()).ToSingle();
        // FVector operator/ multiplies by the reciprocal.
        acceleration *= 1f / maximum;
        var squared = acceleration.X * acceleration.X + acceleration.Y * acceleration.Y + acceleration.Z * acceleration.Z;
        if (squared > 1) acceleration *= 1f / MathF.Sqrt(squared);
        return new(acceleration.X, acceleration.Y);
    }

    public static AlsRefactoredMovementState RefreshGrounded(in AlsRefactoredMovementState previous,
        in AlsRefactoredMovementInput i, float velocityHalfLife, float leanHalfLife)
    {
        var direction = i.Velocity.Rotate(i.Rotation.Conjugate()).ToSingle(); var target = Vector3.Zero;
        var squared = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
        if (squared > 1e-8f)
        {
            direction *= 1f / MathF.Sqrt(squared);
            target = direction * (1f / (MathF.Abs(direction.X) + MathF.Abs(direction.Y) + MathF.Abs(direction.Z)));
        }
        var blend = new Vector4(M.Clamp(target.X, 0, 1), MathF.Abs(M.Clamp(target.X, -1, 0)),
            MathF.Abs(M.Clamp(target.Y, -1, 0)), M.Clamp(target.Y, 0, 1));
        if (previous.VelocityInitialized && velocityHalfLife > 0)
        {
            var alpha = AlsRefactoredRigMath.DamperAlpha(i.Delta, velocityHalfLife);
            blend = new(Lerp(previous.VelocityBlend.X, blend.X, alpha), Lerp(previous.VelocityBlend.Y, blend.Y, alpha),
                Lerp(previous.VelocityBlend.Z, blend.Z, alpha), Lerp(previous.VelocityBlend.W, blend.W, alpha));
        }
        var acceleration = AccelerationAmount(i); var lean = new Vector2(acceleration.Y, acceleration.X);
        if (!i.PendingUpdate && leanHalfLife > 0)
        {
            var alpha = AlsRefactoredRigMath.DamperAlpha(i.Delta, leanHalfLife);
            lean = new(Lerp(previous.Lean.X, lean.X, alpha), Lerp(previous.Lean.Y, lean.Y, alpha));
        }
        return previous with { VelocityInitialized = true, VelocityBlend = blend, Lean = lean };
    }

    public static float VelocityYaw(in AlsRefactoredMovementInput i)
    {
        var angle = (float)(i.VelocityYaw - i.ViewYaw);
        if (!float.IsFinite(angle)) throw new ArgumentException("Invalid movement yaw difference.");
        // Preserve UnwindDegrees' signed boundary and subtraction order.
        while (angle > 180)
        {
            var next = angle - 360;
            if (next == angle) throw new ArgumentException("Movement yaw cannot be unwound at float precision.");
            angle = next;
        }
        while (angle < -180)
        {
            var next = angle + 360;
            if (next == angle) throw new ArgumentException("Movement yaw cannot be unwound at float precision.");
            angle = next;
        }
        return angle;
    }

    public static AlsRefactoredMovementState RefreshGroundedMovement(in AlsRefactoredMovementState previous,
        in AlsRefactoredMovementInput i, Vector4 yawOffsets)
    {
        var angle = VelocityYaw(i);
        var direction = i.VelocityDirectionMode || i.Gait == "Als.Gait.Sprinting" || angle >= -75 && angle <= 75
            ? AlsRefactoredMovementDirection.Forward : angle >= 65 && angle <= 115
            ? AlsRefactoredMovementDirection.Right : angle <= -65 && angle >= -115
            ? AlsRefactoredMovementDirection.Left : AlsRefactoredMovementDirection.Backward;
        return previous with { Direction = direction, HipsLock = M.Clamp(i.HipsLock, -1, 1), YawOffsets = yawOffsets };
    }

    public static AlsRefactoredMovementState RefreshStanding(in AlsRefactoredMovementState previous,
        in AlsRefactoredMovementInput i, float walkStride, float runStride, float walkSpeed, float runSpeed, float sprintSpeed)
    {
        var speed = i.Speed / i.Scale; var stride = Lerp(walkStride, runStride, i.RunningAmount);
        var walkRun = Lerp(speed / walkSpeed, speed / runSpeed, i.RunningAmount);
        var rate = M.Clamp(Lerp(walkRun, speed / sprintSpeed, i.SprintingAmount) / stride, .0001f, 3);
        var sprinting = i.Gait == "Als.Gait.Sprinting";
        var sprintTime = !sprinting ? 0 : i.PendingUpdate ? .5f : previous.SprintTime + i.Delta;
        return previous with { StandingStride = stride, StandingRate = rate, WalkRun = i.Gait == "Als.Gait.Walking" ? 0 : 1,
            SprintBlock = M.Clamp(i.SprintBlock, 0, 1), SprintTime = sprintTime,
            SprintAcceleration = !sprinting || sprintTime >= .5f ? 0 : AccelerationAmount(i).X };
    }

    public static AlsRefactoredMovementState RefreshCrouching(in AlsRefactoredMovementState previous,
        in AlsRefactoredMovementInput i, float stride, float crouchSpeed) => previous with
    { CrouchingStride = stride, CrouchingRate = M.Clamp((i.Speed / i.Scale) / (crouchSpeed * stride), .0001f, 2) };

    private static float Lerp(float a, float b, float alpha) => a + alpha * (b - a);
}
