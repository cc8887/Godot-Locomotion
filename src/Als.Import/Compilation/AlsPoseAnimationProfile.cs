namespace GodotAls.Import.Compilation;

public sealed record AlsPoseAnimationProfile(
    int SchemaVersion,
    int SkeletonId,
    AlsAimProfile Aim,
    AlsTurnProfile[] Turns,
    AlsRotateProfile[] Rotates,
    AlsLayerMaskProfile Masks,
    AlsFootPlacementSettings Feet)
{
    private AlsTurnProfile[] _turns = Turns.ToArray();
    private AlsRotateProfile[] _rotates = Rotates.ToArray();

    public AlsTurnProfile[] Turns
    {
        get => _turns.ToArray();
        init => _turns = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsRotateProfile[] Rotates
    {
        get => _rotates.ToArray();
        init => _rotates = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsFootCurveProfile FootCurves { get; init; } = AlsFootCurveProfile.Empty;
}

public enum AlsPoseStance : byte
{
    Standing,
    Crouching,
}

public readonly record struct AlsTurnProfile(
    int AnimationId,
    int CurveId,
    AlsPoseStance Stance,
    sbyte Direction,
    short NominalDegrees,
    float BasePlayRate,
    float BlendSeconds,
    byte ScaleAngle);

public readonly record struct AlsRotateProfile(
    int AnimationId,
    int CurveId,
    AlsPoseStance Stance,
    sbyte Direction);

public readonly record struct AlsAimProfile(
    int AimOffsetId,
    int DownAnimationId,
    int ForwardAnimationId,
    int UpAnimationId,
    int AdditiveBasePoseAnimationId);

public enum AlsPoseMaskKind : byte
{
    UpperBody,
    Head,
    LeftArm,
    RightArm,
    LeftHand,
    RightHand,
    Pelvis,
    LeftLeg,
    RightLeg,
    LeftFoot,
    RightFoot,
}

public sealed record AlsBoneMaskProfile(
    AlsPoseMaskKind Kind,
    int RootBoneId,
    int[] BoneIds)
{
    private int[] _boneIds = BoneIds.ToArray();

    public int[] BoneIds
    {
        get => _boneIds.ToArray();
        init => _boneIds = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record AlsLayerMaskProfile(AlsBoneMaskProfile[] Entries)
{
    private AlsBoneMaskProfile[] _entries = Entries.ToArray();

    public AlsBoneMaskProfile[] Entries
    {
        get => _entries.ToArray();
        init => _entries = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public readonly record struct AlsFootPlacementSettings(
    int LeftLegRootBoneId,
    int RightLegRootBoneId,
    int LeftFootRootBoneId,
    int RightFootRootBoneId,
    float TraceUpMeters,
    float TraceDownMeters,
    float FootHeightMeters,
    float MaxPelvisCorrectionMeters,
    float PositionHalfLifeSeconds,
    float RotationHalfLifeSeconds,
    float LockReleaseHalfLifeSeconds)
{
    public float PelvisUpHalfLifeSeconds { get; init; }
    public float PelvisDownHalfLifeSeconds { get; init; }
    public float MaximumLegReachMeters { get; init; }
    public AlsCapsuleHalfHeightSource CapsuleHalfHeightSource { get; init; }
    public float MaximumThighAngleRadians { get; init; }
    public float MaximumFootAngleRadians { get; init; }
    public float PlatformTeleportDistanceMeters { get; init; }
    public float PlatformTeleportAngleRadians { get; init; }
    public float LockWeightEpsilon { get; init; }
}

public enum AlsCapsuleHalfHeightSource : byte
{
    CharacterController,
}

public readonly record struct AlsFootCurveBinding(
    int AnimationId,
    int LeftLockCurveId,
    int RightLockCurveId,
    float LeftLockDefault,
    float RightLockDefault);

public sealed record AlsFootCurveProfile(
    float GroundedIkWeight,
    float JumpStartIkWeight,
    float FallLoopIkWeight,
    float LandRecoveryIkWeight,
    AlsFootCurveBinding[] Bindings)
{
    private AlsFootCurveBinding[] _bindings = Bindings.ToArray();

    public static AlsFootCurveProfile Empty { get; } = new(0f, 0f, 0f, 0f, []);

    public AlsFootCurveBinding[] Bindings
    {
        get => _bindings.ToArray();
        init => _bindings = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}
