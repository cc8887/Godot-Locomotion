namespace GodotAls.Import.Compilation;

public readonly record struct AlsPresentationDefinition(
    System.Numerics.Vector3 TranslationMeters,
    float YawRadians);

public sealed record AlsLocomotionAnimationProfile(
    int SkeletonId,
    int MannequinMeshId,
    AlsPresentationDefinition Presentation,
    int StandingIdleAnimationId,
    int CrouchingIdleAnimationId,
    AlsLocomotionAnimationSample[] StandingSamples,
    AlsLocomotionAnimationSample[] CrouchingSamples,
    int JumpStartAnimationId,
    int FallLoopAnimationId,
    int LandAnimationId,
    AlsLocomotionAnimationSample[] LeanAdditiveSamples,
    int LeanAdditiveBasePoseAnimationId,
    int[] AllAnimationIds);

public sealed record AlsLocomotionAnimationSample(
    int AnimationId,
    float X,
    float Y,
    float RateScale);
