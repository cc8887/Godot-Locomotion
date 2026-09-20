using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Pure input functions. The global animation update owns their committed history and gate.
public sealed class AlsGroundedInputFunctions
{
    private readonly double _normalTolerance;
    private readonly AlsMovementInputCurve _diagonal;

    public AlsGroundedInputFunctions(float velocityNormalToleranceCmSquared, AlsMovementInputCurve diagonal)
    {
        if (!float.IsFinite(velocityNormalToleranceCmSquared) || velocityNormalToleranceCmSquared <= 0)
            throw new ArgumentOutOfRangeException(nameof(velocityNormalToleranceCmSquared));
        _normalTolerance = velocityNormalToleranceCmSquared;
        _diagonal = diagonal ?? throw new ArgumentNullException(nameof(diagonal));
    }

    // Public vectors are Godot metres/s; source SafeNormal tolerance is squared cm/s.
    // Channels are F, B, L, R. The full local XYZ sum includes the vertical component.
    public Vector4 VelocityBlend(Vector3 worldVelocity, Quaternion actorRotation)
    {
        Finite(worldVelocity); var inverse = InverseRotation(actorRotation);
        var square = (double)worldVelocity.X * worldVelocity.X + (double)worldVelocity.Y * worldVelocity.Y + (double)worldVelocity.Z * worldVelocity.Z;
        if (square * 10000 < _normalTolerance) return Vector4.Zero;
        var scale = 1 / System.Math.Sqrt(square);
        var normalized = new Vector3((float)(worldVelocity.X * scale), (float)(worldVelocity.Y * scale), (float)(worldVelocity.Z * scale));
        var local = Vector3.Transform(normalized, inverse);
        var sum = (double)MathF.Abs(local.X) + MathF.Abs(local.Y) + MathF.Abs(local.Z);
        if (sum == 0) return Vector4.Zero; // UE Divide_VectorFloat zero denominator.
        var forward = -local.Z / sum; var right = local.X / sum;
        return new((float)System.Math.Clamp(forward, 0, 1), (float)-System.Math.Clamp(forward, -1, 0),
            (float)-System.Math.Clamp(right, -1, 0), (float)System.Math.Clamp(right, 0, 1));
    }

    public static Vector4 InterpolateVelocity(Vector4 current, Vector4 target, float delta, float speed)
    {
        // Same per-channel source FInterpTo as Lean; deliberately no normalization or fallback.
        var fb = AlsMovementInputFunctions.InterpolateLean(new(current.X, current.Y), new(target.X, target.Y), delta, speed);
        var lr = AlsMovementInputFunctions.InterpolateLean(new(current.Z, current.W), new(target.Z, target.W), delta, speed);
        return new(fb.X, fb.Y, lr.X, lr.Y);
    }

    public Vector3 RelativeAcceleration(Vector3 worldAcceleration, Vector3 worldVelocity, Quaternion actorRotation,
        float maxAcceleration, float maxBrakingDeceleration)
    {
        Finite(worldAcceleration); Finite(worldVelocity); var inverse = InverseRotation(actorRotation);
        if (!float.IsFinite(maxAcceleration) || maxAcceleration < 0 || !float.IsFinite(maxBrakingDeceleration) || maxBrakingDeceleration < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAcceleration));
        var dot = (double)worldAcceleration.X * worldVelocity.X + (double)worldAcceleration.Y * worldVelocity.Y + (double)worldAcceleration.Z * worldVelocity.Z;
        var maximum = dot > 0 ? maxAcceleration : maxBrakingDeceleration;
        // FVector::GetClampedToMaxSize returns zero below 1e-4 source cm/s².
        if ((double)maximum * 100 < 1e-4f) return Vector3.Zero;
        var square = (double)worldAcceleration.X * worldAcceleration.X + (double)worldAcceleration.Y * worldAcceleration.Y + (double)worldAcceleration.Z * worldAcceleration.Z;
        var divisor = System.Math.Max(maximum, System.Math.Sqrt(square));
        var relative = new Vector3((float)(worldAcceleration.X / divisor), (float)(worldAcceleration.Y / divisor), (float)(worldAcceleration.Z / divisor));
        return Vector3.Transform(relative, inverse);
    }

    public float DiagonalScale(Vector4 committedOrCandidateVelocityBlend)
    {
        if (!float.IsFinite(committedOrCandidateVelocityBlend.X) || !float.IsFinite(committedOrCandidateVelocityBlend.Y))
            throw new ArgumentOutOfRangeException(nameof(committedOrCandidateVelocityBlend));
        return _diagonal.Sample((float)System.Math.Abs((double)committedOrCandidateVelocityBlend.X + committedOrCandidateVelocityBlend.Y));
    }

    public static float WalkRunBlend(AlsGait gait) => gait switch
    {
        AlsGait.Walking => 0, AlsGait.Running or AlsGait.Sprinting => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(gait)),
    };

    private static Quaternion InverseRotation(Quaternion rotation)
    {
        if (!float.IsFinite(rotation.LengthSquared()) || rotation.LengthSquared() < .5f)
            throw new ArgumentException("Invalid actor rotation.");
        return Quaternion.Conjugate(Quaternion.Normalize(rotation));
    }
    private static void Finite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
