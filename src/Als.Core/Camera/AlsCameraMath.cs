using GodotAls.Core.Locomotion;
using M = System.Math;

namespace GodotAls.Core.Camera;

// ALSCamera's calculation boundary uses UE world coordinates (centimeters),
// double FRotator degrees and float curve/time inputs. No renderer/input state.
public static class AlsCameraMath
{
    public static float DamperAlpha(float delta, float halfLife)
    {
        Time(delta, halfLife);
        return AlsRefactoredRigMath.DamperAlpha(delta, halfLife);
    }

    public static AlsAimingRotation Rotation(AlsAimingRotation current, AlsAimingRotation target,
        float delta, float halfLife, bool allowLag)
    {
        Time(delta, halfLife); Validate(current); Validate(target);
        if (!allowLag) return target;
        var difference = new AlsAimingRotation(Normalize(target.Pitch - current.Pitch),
            Normalize(target.Yaw - current.Yaw), Normalize(target.Roll - current.Roll));
        if (M.Abs(difference.Pitch) <= .0001 && M.Abs(difference.Yaw) <= .0001 && M.Abs(difference.Roll) <= .0001)
            return target;
        var alpha = DamperAlpha(delta, halfLife);
        // The Win64 vector implementation uses >= 175, unlike the scalar >.
        double Blend(double value, double change) => Normalize(value + (change >= 175 ? change - 360 : change) * alpha);
        return new(Blend(current.Pitch, difference.Pitch), Blend(current.Yaw, difference.Yaw), Blend(current.Roll, difference.Roll));
    }

    public static AlsDoubleVector PivotLag(AlsDoubleVector current, AlsDoubleVector target,
        double cameraYaw, float delta, float halfLifeX, float halfLifeY, float halfLifeZ, bool allowLag)
    {
        Validate(current); Validate(target); Finite(cameraYaw);
        Time(delta, halfLifeX); Time(delta, halfLifeY); Time(delta, halfLifeZ);
        if (!allowLag) return target;
        var halfYaw = cameraYaw * (M.PI / 360);
        var yaw = new AlsQuaternion(0, 0, M.Sin(halfYaw), M.Cos(halfYaw));
        var a = current.Rotate(yaw.Conjugate()); var b = target.Rotate(yaw.Conjugate());
        return new AlsDoubleVector(a.X + (b.X - a.X) * DamperAlpha(delta, halfLifeX),
            a.Y + (b.Y - a.Y) * DamperAlpha(delta, halfLifeY),
            a.Z + (b.Z - a.Z) * DamperAlpha(delta, halfLifeZ)).Rotate(yaw);
    }

    public static bool AllowLag(bool requested, AlsDoubleVector previousTarget, AlsDoubleVector target, float teleportDistance)
    {
        Validate(previousTarget); Validate(target); Nonnegative(teleportDistance);
        return requested && (teleportDistance <= 0 || (previousTarget - target).LengthSquared <= teleportDistance * teleportDistance);
    }

    // Scene sweeps/initial-penetration adjustment supply the actual trace result.
    // Shrinking is immediate; only extension uses the native half-life damper.
    public static (AlsDoubleVector Location, float Ratio) TraceDistance(AlsDoubleVector start, AlsDoubleVector end,
        AlsDoubleVector result, float previousRatio, float delta, float halfLife, bool allowLag, bool smoothing)
    {
        Validate(start); Validate(end); Validate(result); Time(delta, halfLife); Nonnegative(previousRatio);
        var vector = end - start; var distance = M.Sqrt(vector.LengthSquared);
        if (!allowLag || !smoothing || distance <= .0001) return (result, 1);
        var target = (float)(M.Sqrt((result - start).LengthSquared) / distance);
        var ratio = target <= previousRatio ? target : previousRatio + (target - previousRatio) * DamperAlpha(delta, halfLife);
        return (start + vector * ratio, ratio);
    }

    private static double Normalize(double value) => AlsCharacterRotationMath.Normalize(value);
    private static void Validate(AlsDoubleVector value) { if (!value.IsFinite) throw new ArgumentException("Nonfinite camera vector."); }
    private static void Validate(AlsAimingRotation value) { Finite(value.Pitch); Finite(value.Yaw); Finite(value.Roll); }
    private static void Finite(double value) { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void Nonnegative(float value) { Finite(value); if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void Time(float delta, float halfLife) { Nonnegative(delta); Nonnegative(halfLife); }
}
