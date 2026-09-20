namespace GodotAls.Core.Locomotion;

// An optional final-IK contact anchor. This is separate from ALS's pre-IK
// socket lock: ground/pelvis offsets must not be reapplied to a planted endpoint.
public readonly record struct AlsFootContactTarget(float Weight, AlsDoubleVector Location);
public readonly record struct AlsFootContactAnchor(bool Active, ulong BaseIdentity, ulong TeleportSequence,
    AlsDoubleVector LocalLocation, AlsDoubleVector SourceAnchor, float Amount)
{
    // A static collider has a contact identity even though ALS has no movement base.
    public ulong SurfaceIdentity { get; init; }
}

public static class AlsFootContactAnchoring
{
    public static AlsFootContactTarget Target(in AlsFootContactAnchor old, in AlsBasedFootLockState locked,
        ulong teleportSequence, bool matchingGround, AlsPrecisePose basis, AlsPrecisePose component,
        ulong surfaceIdentity = 0)
    {
        surfaceIdentity = Surface(locked, surfaceIdentity);
        if (!Eligible(locked, matchingGround, surfaceIdentity) || !old.Active || old.BaseIdentity != locked.BaseIdentity ||
            old.SurfaceIdentity != surfaceIdentity ||
            old.TeleportSequence != teleportSequence || old.SourceAnchor != SourcePosition(locked) ||
            locked.Amount >= 1 - AlsPoseBlender.WeightThreshold && locked.Amount > old.Amount)
            return default;
        if (locked.BaseIdentity == 0) basis = AlsPrecisePose.Identity;
        basis.Validate(); component.Validate();
        var world = old.LocalLocation.Rotate(basis.Rotation) + basis.Position;
        var local = AlsPrecisePose.Relative(new(world, AlsQuaternion.Identity, AlsDoubleVector.One), component).Position;
        return new(locked.Amount, local);
    }

    public static AlsFootContactAnchor Complete(in AlsFootContactAnchor old, in AlsBasedFootLockState locked,
        ulong teleportSequence, bool matchingGround, AlsPrecisePose basis, AlsPrecisePose component,
        AlsDoubleVector finalLocation, bool reused, ulong surfaceIdentity = 0)
    {
        surfaceIdentity = Surface(locked, surfaceIdentity);
        if (!Eligible(locked, matchingGround, surfaceIdentity)) return default;
        if (reused) return old with { Amount = locked.Amount };
        // Rising partial curves cannot establish a new planted contact.
        if (locked.Amount < 1 - AlsPoseBlender.WeightThreshold) return default;
        if (locked.BaseIdentity == 0) basis = AlsPrecisePose.Identity;
        basis.Validate(); component.Validate();
        if (!finalLocation.IsFinite) throw new ArgumentException("Nonfinite final contact position.");
        var world = AlsPrecisePose.Compose(new(finalLocation, AlsQuaternion.Identity, AlsDoubleVector.One), component).Position;
        return new(true, locked.BaseIdentity, teleportSequence,
            (world - basis.Position).Rotate(basis.Rotation.Conjugate()), SourcePosition(locked), locked.Amount)
            { SurfaceIdentity = surfaceIdentity };
    }

    private static ulong Surface(in AlsBasedFootLockState locked, ulong surfaceIdentity) =>
        surfaceIdentity != 0 ? surfaceIdentity : locked.BaseIdentity;
    private static AlsDoubleVector SourcePosition(in AlsBasedFootLockState locked) =>
        locked.BaseIdentity == 0 ? locked.WorldLock.Position : locked.BaseLock.Position;
    private static bool Eligible(in AlsBasedFootLockState locked, bool matchingGround, ulong surfaceIdentity) =>
        matchingGround && locked.HasSample && surfaceIdentity != 0 &&
        (locked.BaseIdentity == 0 || locked.BaseIdentity == surfaceIdentity) && locked.Amount > AlsPoseBlender.WeightThreshold &&
        !locked.ThighConstrained;

    // Minimum downward pelvis correction needed to reach a pinned endpoint.
    // Horizontal overreach cannot be solved by pelvis height and remains clamped.
    public static double PelvisLowering(AlsDoubleVector thigh, AlsFootContactTarget target, double maximumReach)
    {
        if (target.Weight <= 0) return 0;
        if (!thigh.IsFinite || !target.Location.IsFinite || !double.IsFinite(maximumReach) || maximumReach <= 0 ||
            !float.IsFinite(target.Weight) || target.Weight > 1)
            throw new ArgumentException("Invalid contact reach input.");
        var delta = target.Location - thigh;
        var verticalSquared = maximumReach * maximumReach - delta.X * delta.X - delta.Y * delta.Y;
        if (verticalSquared <= 0) return 0;
        return System.Math.Min(0, delta.Z + System.Math.Sqrt(verticalSquared)) * target.Weight;
    }
}
