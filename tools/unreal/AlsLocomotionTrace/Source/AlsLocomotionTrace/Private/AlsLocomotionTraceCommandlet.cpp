#include "AlsLocomotionTraceCommandlet.h"

#include "AlsTraceCharacter.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacterMovementComponent.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimNode_RelevantAssetPlayerBase.h"
#include "Animation/AnimationAsset.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Components/BoxComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/EngineBaseTypes.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Engine/CollisionProfile.h"
#include "HAL/FileManager.h"
#include "JsonObjectConverter.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Misc/Parse.h"
#include "Modules/ModuleManager.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Settings/AlsCharacterSettings.h"
#include "Settings/AlsMovementSettings.h"
#include "State/AlsCrouchingState.h"
#include "State/AlsGroundedState.h"
#include "State/AlsLeanState.h"
#include "State/AlsLocomotionAnimationState.h"
#include "State/AlsMovementBaseState.h"
#include "State/AlsStandingState.h"
#include "State/AlsFeetState.h"
#include "State/AlsLayeringState.h"
#include "State/AlsRotateInPlaceState.h"
#include "State/AlsSpineState.h"
#include "State/AlsTurnInPlaceState.h"
#include "State/AlsViewAnimationState.h"
#include "UObject/UnrealType.h"
#include "Utility/AlsGameplayTags.h"
#include "Utility/AlsConstants.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(AlsLocomotionTraceCommandlet)

IMPLEMENT_MODULE(FDefaultModuleImpl, AlsLocomotionTrace)

namespace AlsLocomotionTrace
{
constexpr double FixedDeltaSeconds{1.0 / 60.0};
constexpr float StationaryYawSpeedThreshold{0.1f};
constexpr float StationaryYawAccelerationThreshold{0.1f};
constexpr int32 SchemaVersion{1};
constexpr const TCHAR* LockedReferenceCommit{TEXT("b754d6f0f2bb03741d301f8fb88077ebfe561e17")};
constexpr const TCHAR* LockedPatchHash{TEXT("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f")};
constexpr const TCHAR* P4AimOverlayClassPath{
    TEXT("/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Rifle.AB_Als_Rifle_C")};

struct FTraceCommand
{
    FVector2D MovementAxes{ForceInit};
    float ViewYaw{0.0f};
    float AimYaw{0.0f};
    FGameplayTag Gait{AlsGaitTags::Running};
    FGameplayTag Stance{AlsStanceTags::Standing};
    FGameplayTag RotationMode{AlsRotationModeTags::ViewDirection};
    bool bJumpPressed{false};
    bool bStandBlocked{false};
};

struct FSequenceDefinition
{
    FString Name;
    int32 FrameCount{0};
    TFunction<FTraceCommand(int32)> GetCommand;
};

struct FPortDirectionalSpeeds
{
    float Forward{0.0f};
    float Sideways{0.0f};
    float Backward{0.0f};
};

struct FPortStanceSpeeds
{
    FPortDirectionalSpeeds Walking;
    FPortDirectionalSpeeds Running;
    FPortDirectionalSpeeds Sprinting;
};

struct FPortSettings
{
    FPortStanceSpeeds Standing;
    FPortStanceSpeeds Crouching;
    float AnimatedWalkSpeed{0.0f};
    float AnimatedRunSpeed{0.0f};
    float AnimatedSprintSpeed{0.0f};
    float AnimatedCrouchSpeed{0.0f};
    float MovingSpeedThreshold{0.0f};
    float VelocityAngleStart{0.0f};
    float VelocityAngleEnd{0.0f};
    float LeanHalfLife{0.0f};
};

struct FPortState
{
    bool bInitialized{false};
    bool bGrounded{true};
    bool bJumpStartActive{false};
    float LandingRecoveryTime{0.0f};
    FVector2D SmoothedLean{ForceInit};
    float AnimationPhase{0.0f};
    float SmoothedTargetYaw{0.0f};
    float TargetYaw{0.0f};
};

struct FNativeObservationState
{
    float AnimationPhase{0.0f};
    int32 LandingRecoveryFramesRemaining{0};
    bool bJumpStartActive{false};
};

struct FPortResult
{
    FString LocomotionState;
    FGameplayTag Gait;
    FString AnimationState;
    FVector2D BlendCoordinates{ForceInit};
    float Stride{0.0f};
    float PlayRate{0.0f};
    FVector2D Lean{ForceInit};
    float AnimationPhase{0.0f};
    float TargetYaw{0.0f};
};

FString GetRequiredValue(const FString& Parameters, const TCHAR* Key)
{
    FString Value;
    if (!FParse::Value(*Parameters, Key, Value) || Value.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("Missing required argument %s"), Key);
        return {};
    }
    return Value.TrimQuotes();
}

bool ValidateReference(const FString& ReferenceRoot, const FString& LockedCommit, const FString& PatchHashes)
{
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_BEGIN root=%s"), *ReferenceRoot);
    if (!IFileManager::Get().DirectoryExists(*ReferenceRoot))
    {
        UE_LOG(LogTemp, Error, TEXT("Reference root does not exist: %s"), *ReferenceRoot);
        return false;
    }

    FString ActualCommit;
    const FString HeadPath{ReferenceRoot / TEXT(".git/HEAD")};
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_HEAD_BEGIN path=%s"), *HeadPath);
    if (!FFileHelper::LoadFileToString(ActualCommit, *HeadPath))
    {
        UE_LOG(LogTemp, Error, TEXT("Could not read detached git HEAD: %s"), *HeadPath);
        return false;
    }
    ActualCommit.TrimStartAndEndInline();
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_HEAD_OK commit=%s"), *ActualCommit);
    if (ActualCommit.StartsWith(TEXT("ref:")) || ActualCommit != LockedCommit)
    {
        UE_LOG(LogTemp, Error, TEXT("Locked commit mismatch. Expected %s, actual %s"), *LockedCommit, *ActualCommit);
        return false;
    }

    FString DescriptorText;
    const FString DescriptorPath{ReferenceRoot / TEXT("ALS.uplugin")};
    TSharedPtr<FJsonObject> Descriptor;
    if (!FFileHelper::LoadFileToString(DescriptorText, *DescriptorPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(DescriptorText), Descriptor) ||
        !Descriptor.IsValid() || Descriptor->GetStringField(TEXT("EngineVersion")) != TEXT("5.9.0"))
    {
        UE_LOG(LogTemp, Error, TEXT("ALS descriptor is not the locked UE 5.9 compatibility result: %s"), *DescriptorPath);
        return false;
    }

    if (PatchHashes != LockedPatchHash)
    {
        UE_LOG(LogTemp, Error, TEXT("Locked patch hash mismatch. Expected %s, got %s"), LockedPatchHash, *PatchHashes);
        return false;
    }
    return true;
}

bool IsLowercaseCommitSha(const FString& Commit)
{
    if (Commit.Len() != 40) return false;
    for (const TCHAR Character : Commit)
    {
        if (!FChar::IsHexDigit(Character) || (Character >= TEXT('A') && Character <= TEXT('F'))) return false;
    }
    return true;
}

FString GaitName(const FGameplayTag& Tag)
{
    if (Tag == AlsGaitTags::Walking) return TEXT("Walking");
    if (Tag == AlsGaitTags::Sprinting) return TEXT("Sprinting");
    return TEXT("Running");
}

FString StanceName(const FGameplayTag& Tag)
{
    return Tag == AlsStanceTags::Crouching ? TEXT("Crouching") : TEXT("Standing");
}

FString RotationModeName(const FGameplayTag& Tag)
{
    if (Tag == AlsRotationModeTags::VelocityDirection) return TEXT("VelocityDirection");
    if (Tag == AlsRotationModeTags::Aiming) return TEXT("Aiming");
    return TEXT("LookingDirection");
}

template <typename T>
const T* GetAnimationState(const UAlsAnimationInstance* AnimationInstance, const TCHAR* PropertyName)
{
    if (!IsValid(AnimationInstance)) return nullptr;
    const FStructProperty* Property{FindFProperty<FStructProperty>(AnimationInstance->GetClass(), PropertyName)};
    if (Property == nullptr || Property->Struct != T::StaticStruct()) return nullptr;
    return Property->ContainerPtrToValuePtr<T>(AnimationInstance);
}

TSharedRef<FJsonObject> Vector2Object(const double X, const double Y)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), X);
    Object->SetNumberField(TEXT("y"), Y);
    return Object;
}

TArray<TSharedPtr<FJsonValue>> PatchHashArray()
{
    return {MakeShared<FJsonValueString>(LockedPatchHash)};
}

TSharedRef<FJsonObject> Vector3Object(const FVector& Value)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X / 100.0);
    Object->SetNumberField(TEXT("y"), Value.Y / 100.0);
    Object->SetNumberField(TEXT("z"), Value.Z / 100.0);
    return Object;
}

float DamperExactAlpha(const float HalfLife)
{
    return HalfLife <= 0.0f ? 1.0f : 1.0f - FMath::Exp2(-static_cast<float>(FixedDeltaSeconds) / HalfLife);
}

float NormalizeRadians(const float Angle)
{
    return FMath::DegreesToRadians(FMath::UnwindDegrees(FMath::RadiansToDegrees(Angle)));
}

float InterpolateAngleConstant(const float Current, const float Target, const float Speed)
{
    const float Delta{NormalizeRadians(Target - Current)};
    const float MaxDelta{Speed * static_cast<float>(FixedDeltaSeconds)};
    if (Speed <= 0.0f || FMath::Abs(Delta) <= MaxDelta)
    {
        return NormalizeRadians(Target);
    }
    return NormalizeRadians(Current + FMath::Sign(Delta) * MaxDelta);
}

float SampleDirectionalSpeed(const FPortDirectionalSpeeds& Speeds, const float LocalYaw,
                             const FPortSettings& Settings)
{
    const float Amount{1.0f - FMath::Clamp(
        (FMath::Abs(NormalizeRadians(LocalYaw)) - Settings.VelocityAngleStart) /
        (Settings.VelocityAngleEnd - Settings.VelocityAngleStart), 0.0f, 1.0f)};
    return FMath::Lerp(Speeds.Backward, Speeds.Forward, Amount);
}

FPortDirectionalSpeeds MakeDirectional(const float Forward, const float Backward,
                                       const float AngleStart, const float AngleEnd)
{
    const float Amount{1.0f - FMath::Clamp((PI * 0.5f - AngleStart) / (AngleEnd - AngleStart), 0.0f, 1.0f)};
    return {Forward, FMath::Lerp(Backward, Forward, Amount), Backward};
}

FPortSettings CreatePortSettings(const AAlsTraceCharacter& Character)
{
    const UAlsMovementSettings* MovementSettings{Character.GetTraceMovementSettings()};
    const UAlsAnimationInstanceSettings* AnimationSettings{Character.GetTraceAnimationSettings()};
    const UAlsCharacterSettings* CharacterSettings{Character.GetSettings()};
    const FAlsMovementStanceSettings* RotationSettings{MovementSettings->RotationModes.Find(AlsRotationModeTags::ViewDirection)};
    const FAlsMovementGaitSettings* Standing{RotationSettings->Stances.Find(AlsStanceTags::Standing)};
    const FAlsMovementGaitSettings* Crouching{RotationSettings->Stances.Find(AlsStanceTags::Crouching)};
    FPortSettings Settings;
    Settings.VelocityAngleStart = FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Min);
    Settings.VelocityAngleEnd = FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Max);
    Settings.Standing.Walking = MakeDirectional(Standing->WalkForwardSpeed / 100.0f, Standing->WalkBackwardSpeed / 100.0f,
                                                Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Standing.Running = MakeDirectional(Standing->RunForwardSpeed / 100.0f, Standing->RunBackwardSpeed / 100.0f,
                                                Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Standing.Sprinting = MakeDirectional(Standing->SprintSpeed / 100.0f, Standing->SprintSpeed / 100.0f,
                                                  Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Walking = MakeDirectional(Crouching->WalkForwardSpeed / 100.0f, Crouching->WalkForwardSpeed / 100.0f,
                                                 Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Running = MakeDirectional(Crouching->RunForwardSpeed / 100.0f, Crouching->RunForwardSpeed / 100.0f,
                                                 Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Sprinting = Settings.Crouching.Running;
    Settings.AnimatedWalkSpeed = AnimationSettings->Standing.AnimatedWalkSpeed / 100.0f;
    Settings.AnimatedRunSpeed = AnimationSettings->Standing.AnimatedRunSpeed / 100.0f;
    Settings.AnimatedSprintSpeed = AnimationSettings->Standing.AnimatedSprintSpeed / 100.0f;
    Settings.AnimatedCrouchSpeed = AnimationSettings->Crouching.AnimatedCrouchSpeed / 100.0f;
    Settings.MovingSpeedThreshold = CharacterSettings->MovingSpeedThreshold / 100.0f;
    Settings.LeanHalfLife = AnimationSettings->General.LeanInterpolationHalfLife;
    return Settings;
}

FGameplayTag ResolveMaximumGait(const FTraceCommand& Command, const FGameplayTag& ActualStance)
{
    if (Command.Gait != AlsGaitTags::Sprinting) return Command.Gait;
    if (ActualStance == AlsStanceTags::Crouching || Command.RotationMode == AlsRotationModeTags::Aiming)
    {
        return AlsGaitTags::Running;
    }
    const FVector2D Direction{Command.MovementAxes.GetSafeNormal()};
    if (Direction.IsNearlyZero() ||
        (Command.RotationMode == AlsRotationModeTags::ViewDirection && Direction.Y <= FMath::Cos(FMath::DegreesToRadians(50.0f))))
    {
        return AlsGaitTags::Running;
    }
    return AlsGaitTags::Sprinting;
}

FPortResult EvaluatePort(const AAlsTraceCharacter& Character, const FTraceCommand& Command,
                          const bool bJumpTransition, const FVector& ActualAcceleration,
                          const FPortSettings& Settings, FPortState& State)
{
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const bool bGrounded{Movement->IsMovingOnGround()};
    const bool bLanded{!State.bGrounded && bGrounded};
    const bool bJumped{State.bGrounded && !bGrounded && bJumpTransition && Command.bJumpPressed};
    const FVector LocalVelocity{Character.GetActorQuat().UnrotateVector(Character.GetVelocity()) / 100.0f};
    const FVector LocalAcceleration{Character.GetActorQuat().UnrotateVector(ActualAcceleration) / 100.0f};
    const FVector2D Velocity{LocalVelocity.Y, LocalVelocity.X};
    const FVector2D Acceleration{LocalAcceleration.Y, LocalAcceleration.X};
    const float Speed{static_cast<float>(FVector2D{Character.GetVelocity().X, Character.GetVelocity().Y}.Size() / 100.0)};
    const float RawLocalYaw{Speed > 0.0f
        ? static_cast<float>(FMath::Atan2(-Velocity.X, Velocity.Y))
        : 0.0f};
    // System.MathF canonicalizes the exact -pi boundary to +pi in AlsMath.
    const float LocalYaw{RawLocalYaw == -PI ? PI : RawLocalYaw};
    const FGameplayTag ActualStance{Character.GetStance()};
    const FPortStanceSpeeds& StanceSpeeds{ActualStance == AlsStanceTags::Crouching ? Settings.Crouching : Settings.Standing};
    const float WalkSpeed{SampleDirectionalSpeed(StanceSpeeds.Walking, LocalYaw, Settings)};
    const float RunSpeed{SampleDirectionalSpeed(StanceSpeeds.Running, LocalYaw, Settings)};
    const FGameplayTag MaximumGait{ResolveMaximumGait(Command, ActualStance)};
    const FGameplayTag ActualGait{Speed < WalkSpeed + 0.1f
        ? AlsGaitTags::Walking
        : (Speed < RunSpeed + 0.1f || MaximumGait != AlsGaitTags::Sprinting
            ? AlsGaitTags::Running : AlsGaitTags::Sprinting)};

    FString AnimationState{TEXT("Grounded")};
    if (!bGrounded)
    {
        State.LandingRecoveryTime = 0.0f;
        if (bJumped)
        {
            State.bJumpStartActive = true;
            AnimationState = TEXT("JumpStart");
        }
        else if (State.bJumpStartActive && Character.GetVelocity().Z > 0.0)
        {
            AnimationState = TEXT("JumpStart");
        }
        else
        {
            State.bJumpStartActive = false;
            AnimationState = TEXT("FallLoop");
        }
    }
    else if (bLanded)
    {
        State.bJumpStartActive = false;
        State.LandingRecoveryTime = FMath::Max(0.0f, 0.2f - static_cast<float>(FixedDeltaSeconds));
        AnimationState = TEXT("LandRecovery");
    }
    else if (State.LandingRecoveryTime > 0.0f)
    {
        State.LandingRecoveryTime = State.LandingRecoveryTime <= static_cast<float>(FixedDeltaSeconds) + 1.0e-6f
            ? 0.0f : State.LandingRecoveryTime - static_cast<float>(FixedDeltaSeconds);
        AnimationState = TEXT("LandRecovery");
    }
    else
    {
        State.bJumpStartActive = false;
    }

    const float ActorYaw{FMath::DegreesToRadians(
        static_cast<float>(Character.GetActorRotation().Yaw))};
    float TargetYaw{ActorYaw};
    const float HorizontalAcceleration{static_cast<float>(
        FVector2D{ActualAcceleration.X, ActualAcceleration.Y}.Size() / 100.0)};
    const bool bStationaryYawCandidate{
        bGrounded &&
        Speed <= StationaryYawSpeedThreshold &&
        HorizontalAcceleration <= StationaryYawAccelerationThreshold &&
        (Command.RotationMode == AlsRotationModeTags::ViewDirection ||
         Command.RotationMode == AlsRotationModeTags::Aiming)};
    if (bStationaryYawCandidate ||
        (Speed <= Settings.MovingSpeedThreshold &&
         Command.RotationMode == AlsRotationModeTags::VelocityDirection))
    {
        State.SmoothedTargetYaw = ActorYaw;
    }
    else
    {
        const float RawVelocityYaw{Speed > 1.0e-6f
            ? static_cast<float>(FMath::Atan2(Character.GetVelocity().Y, Character.GetVelocity().X))
            : ActorYaw};
        // Port output is converted from UE yaw to Godot yaw by negation. Preserve Core's +pi
        // canonical boundary by emitting -pi before that conversion.
        const float VelocityYaw{RawVelocityYaw == PI ? -PI : RawVelocityYaw};
        float SelectedTargetYaw;
        if (Command.RotationMode == AlsRotationModeTags::VelocityDirection ||
            (Command.RotationMode == AlsRotationModeTags::ViewDirection && ActualGait == AlsGaitTags::Sprinting))
        {
            SelectedTargetYaw = VelocityYaw;
        }
        else if (Command.RotationMode == AlsRotationModeTags::ViewDirection)
        {
            // P3A has no RotationYawOffsetCurve input. Its documented zero-curve fallback
            // follows view yaw; nativeActual remains an independent ALS observation.
            SelectedTargetYaw = NormalizeRadians(FMath::DegreesToRadians(Command.ViewYaw));
        }
        else
        {
            SelectedTargetYaw = FMath::DegreesToRadians(Command.AimYaw);
        }

        const float PreviousSmoothedTargetYaw{State.bInitialized ? State.SmoothedTargetYaw : ActorYaw};
        if (Command.RotationMode == AlsRotationModeTags::Aiming)
        {
            State.SmoothedTargetYaw = SelectedTargetYaw;
        }
        else
        {
            const float TargetYawSpeed{Command.RotationMode == AlsRotationModeTags::VelocityDirection
                ? FMath::DegreesToRadians(800.0f)
                : FMath::DegreesToRadians(500.0f)};
            State.SmoothedTargetYaw = InterpolateAngleConstant(
                PreviousSmoothedTargetYaw, SelectedTargetYaw, TargetYawSpeed);
        }
        const float Delta{NormalizeRadians(State.SmoothedTargetYaw - ActorYaw)};
        TargetYaw = NormalizeRadians(ActorYaw + Delta * DamperExactAlpha(0.1f));
    }

    const FPortDirectionalSpeeds& GaitSpeeds{ActualGait == AlsGaitTags::Walking ? StanceSpeeds.Walking :
        (ActualGait == AlsGaitTags::Running ? StanceSpeeds.Running : StanceSpeeds.Sprinting)};
    const float ReferenceSpeed{SampleDirectionalSpeed(GaitSpeeds, LocalYaw, Settings)};
    const float Stride{ReferenceSpeed > 1.0e-6f ? FMath::Clamp(Speed / ReferenceSpeed, 0.0f, 1.0f) : 0.0f};
    const float AnimatedSpeed{ActualStance == AlsStanceTags::Crouching ? Settings.AnimatedCrouchSpeed :
        (ActualGait == AlsGaitTags::Walking ? Settings.AnimatedWalkSpeed :
            (ActualGait == AlsGaitTags::Running ? Settings.AnimatedRunSpeed : Settings.AnimatedSprintSpeed))};
    const float Denominator{AnimatedSpeed * FMath::Max(Stride, 0.0001f)};
    const float RawPlayRate{Denominator > 1.0e-6f ? Speed / Denominator : 0.0f};
    const float PlayRate{FMath::Clamp(RawPlayRate, 0.0001f,
        ActualStance == AlsStanceTags::Crouching ? 2.0f : 3.0f)};

    FVector2D LeanTarget{ForceInit};
    const float Limit{FVector2D::DotProduct(Velocity, Acceleration) >= 0.0f
        ? Movement->GetMaxAcceleration() / 100.0f : Movement->GetMaxBrakingDeceleration() / 100.0f};
    if (Limit > 1.0e-6f) LeanTarget = Acceleration.GetClampedToMaxSize(Limit) / Limit;
    State.SmoothedLean = FMath::Lerp(State.SmoothedLean, LeanTarget, DamperExactAlpha(Settings.LeanHalfLife));

    if (bGrounded && Speed > Settings.MovingSpeedThreshold)
    {
        State.AnimationPhase = FMath::Fmod(State.AnimationPhase + static_cast<float>(FixedDeltaSeconds) * PlayRate, 1.0f);
    }
    State.bInitialized = true;
    State.bGrounded = bGrounded;
    State.TargetYaw = TargetYaw;
    return {bGrounded ? TEXT("Grounded") : TEXT("InAir"), ActualGait, AnimationState, Velocity,
            Stride, PlayRate, State.SmoothedLean, State.AnimationPhase, TargetYaw};
}

bool SaveJson(const FString& Path, const TSharedRef<FJsonObject>& Object)
{
    FString Json;
    const TSharedRef<TJsonWriter<TCHAR, TPrettyJsonPrintPolicy<TCHAR>>> Writer{
        TJsonWriterFactory<TCHAR, TPrettyJsonPrintPolicy<TCHAR>>::Create(&Json)};
    if (!FJsonSerializer::Serialize(Object, Writer)) return false;
    Json.ReplaceInline(TEXT("\r\n"), TEXT("\n"));
    Json.AppendChar(TEXT('\n'));
    return FFileHelper::SaveStringToFile(Json, *Path, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

TArray<FSequenceDefinition> CreateSequenceDefinitions()
{
    TArray<FSequenceDefinition> Definitions;
    Definitions.Add({TEXT("idle_gaits"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        if (Frame >= 30 && Frame < 90) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Walking; }
        else if (Frame >= 90 && Frame < 150) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Running; }
        else if (Frame >= 150 && Frame < 210) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Sprinting; }
        return Command;
    }});
    Definitions.Add({TEXT("directions"), 240, [](const int32 Frame)
    {
        static const FVector2D Directions[]{{0, 1}, {0, -1}, {-1, 0}, {1, 0}, {-1, 1}, {1, 1}, {-1, -1}, {1, -1}};
        FTraceCommand Command;
        Command.MovementAxes = Directions[FMath::Clamp(Frame / 30, 0, 7)].GetSafeNormal();
        return Command;
    }});
    Definitions.Add({TEXT("crouch_clearance"), 210, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes.Y = Frame < 90 || Frame >= 160 ? 0.75 : 0.0;
        Command.Stance = Frame < 30 || Frame >= 120 ? AlsStanceTags::Standing : AlsStanceTags::Crouching;
        Command.bStandBlocked = Frame >= 90 && Frame < 160;
        return Command;
    }});
    Definitions.Add({TEXT("rotation_modes"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes = FVector2D{0.65, 0.75}.GetSafeNormal();
        Command.ViewYaw = Frame * 0.75f;
        Command.AimYaw = Command.ViewYaw + 20.0f;
        if (Frame < 80) Command.RotationMode = AlsRotationModeTags::VelocityDirection;
        else if (Frame < 160) Command.RotationMode = AlsRotationModeTags::ViewDirection;
        else Command.RotationMode = AlsRotationModeTags::Aiming;
        return Command;
    }});
    Definitions.Add({TEXT("jump_land"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes.Y = Frame < 180 ? 0.65 : 0.0;
        Command.bJumpPressed = Frame == 30;
        return Command;
    }});
    return Definitions;
}

UWorld* CreateTraceWorld()
{
    const UWorld::InitializationValues InitializationValues{UWorld::InitializationValues()
        .AllowAudioPlayback(false)
        .RequiresHitProxies(false)
        .CreatePhysicsScene(true)
        .CreateNavigation(false)
        .CreateAISystem(false)
        .ShouldSimulatePhysics(true)
        .SetTransactional(false)};
    UWorld* World{UWorld::CreateWorld(EWorldType::Game, false, FName{TEXT("AlsLocomotionTraceWorld")}, nullptr,
        false, ERHIFeatureLevel::Num, &InitializationValues)};
    if (!IsValid(World)) return nullptr;
    FWorldContext& Context{GEngine->CreateNewWorldContext(EWorldType::Game)};
    Context.SetCurrentWorld(World);

    AActor* Floor{World->SpawnActor<AActor>()};
    UBoxComponent* FloorCollision{NewObject<UBoxComponent>(Floor, TEXT("TraceFloor"))};
    Floor->SetRootComponent(FloorCollision);
    FloorCollision->SetBoxExtent(FVector{5000.0, 5000.0, 10.0});
    FloorCollision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
    FloorCollision->RegisterComponent();
    Floor->SetActorLocation(FVector{0.0, 0.0, -10.0});
    World->InitializeActorsForPlay(FURL{});
    World->BeginPlay();
    return World;
}

void DestroyTraceWorld(UWorld* World)
{
    if (!IsValid(World)) return;
    GEngine->DestroyWorldContext(World);
    World->DestroyWorld(false);
}

void TickTraceCharacter(AAlsTraceCharacter& Character)
{
    Character.Tick(FixedDeltaSeconds);
    CastChecked<UAlsCharacterMovementComponent>(Character.GetCharacterMovement())->TickComponent(
        FixedDeltaSeconds, LEVELTICK_All, nullptr);
    Character.GetMesh()->TickAnimation(FixedDeltaSeconds, false);
    Character.GetMesh()->RefreshBoneTransforms();
}

bool ValidateRequiredAssets()
{
    constexpr const TCHAR* CharacterSettingsPath{TEXT("/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default")};
    constexpr const TCHAR* MovementSettingsPath{TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal")};
    constexpr const TCHAR* AnimationSettingsPath{TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default")};
    constexpr const TCHAR* SkeletalMeshPath{TEXT("/ALS/ALS/Character/SKM_Als.SKM_Als")};
    constexpr const TCHAR* AnimationClassPath{TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C")};
    const TPair<const TCHAR*, bool> Assets[]
    {
		{CharacterSettingsPath, LoadObject<UAlsCharacterSettings>(nullptr, CharacterSettingsPath) != nullptr},
		{MovementSettingsPath, LoadObject<UAlsMovementSettings>(nullptr, MovementSettingsPath) != nullptr},
		{AnimationSettingsPath, LoadObject<UAlsAnimationInstanceSettings>(nullptr, AnimationSettingsPath) != nullptr},
		{SkeletalMeshPath, LoadObject<USkeletalMesh>(nullptr, SkeletalMeshPath) != nullptr},
		{AnimationClassPath, StaticLoadClass(UAlsAnimationInstance::StaticClass(), nullptr, AnimationClassPath) != nullptr},
    };
    for (const auto& Asset : Assets)
    {
        UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_ASSET_BEGIN path=%s"), Asset.Key);
        if (!Asset.Value)
        {
            UE_LOG(LogTemp, Error, TEXT("Required ALS asset has the wrong type or could not be loaded: %s"), Asset.Key);
            return false;
        }
        UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_ASSET_OK path=%s"), Asset.Key);
    }
    return true;
}

bool ValidateSequenceDefinitions()
{
    const TArray<FSequenceDefinition> Definitions{CreateSequenceDefinitions()};
    constexpr const TCHAR* ExpectedNames[]{TEXT("idle_gaits"), TEXT("directions"), TEXT("crouch_clearance"),
                                           TEXT("rotation_modes"), TEXT("jump_land")};
    constexpr int32 ExpectedFrameCounts[]{240, 240, 210, 240, 240};
    if (Definitions.Num() != UE_ARRAY_COUNT(ExpectedNames)) return false;
    for (int32 Index{0}; Index < Definitions.Num(); ++Index)
    {
        if (Definitions[Index].Name != ExpectedNames[Index] || Definitions[Index].FrameCount != ExpectedFrameCounts[Index])
        {
            return false;
        }
    }
    return true;
}

TSharedRef<FJsonObject> CreateSettingsDocument(const FString& Commit, const AAlsTraceCharacter& Character)
{
    const UAlsCharacterSettings* CharacterSettings{Character.GetSettings()};
    const UAlsMovementSettings* MovementSettings{Character.GetTraceMovementSettings()};
    const UAlsAnimationInstanceSettings* AnimationSettings{Character.GetTraceAnimationSettings()};
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const FAlsMovementStanceSettings* RotationSettings{MovementSettings->RotationModes.Find(AlsRotationModeTags::ViewDirection)};
    const FAlsMovementGaitSettings* Standing{RotationSettings->Stances.Find(AlsStanceTags::Standing)};
    const FAlsMovementGaitSettings* Crouching{RotationSettings->Stances.Find(AlsStanceTags::Crouching)};

    const TSharedRef<FJsonObject> Values{MakeShared<FJsonObject>()};
    Values->SetNumberField(TEXT("accelerationSmoothingHalfLife"), 0.1);
    Values->SetNumberField(TEXT("animatedCrouchSpeed"), AnimationSettings->Crouching.AnimatedCrouchSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedRunSpeed"), AnimationSettings->Standing.AnimatedRunSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedSprintSpeed"), AnimationSettings->Standing.AnimatedSprintSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedWalkSpeed"), AnimationSettings->Standing.AnimatedWalkSpeed / 100.0);
    Values->SetNumberField(TEXT("crouchedHalfHeight"), Movement->GetCrouchedHalfHeight() / 100.0);
    Values->SetNumberField(TEXT("crouchRunForwardSpeed"), Crouching->RunForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("crouchWalkForwardSpeed"), Crouching->WalkForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("gravity"), FMath::Abs(Movement->GetGravityZ()) / 100.0);
    Values->SetNumberField(TEXT("initialMaxAcceleration"), Movement->GetMaxAcceleration() / 100.0);
    Values->SetNumberField(TEXT("initialMaxBrakingDeceleration"), Movement->GetMaxBrakingDeceleration() / 100.0);
    Values->SetNumberField(TEXT("jumpSpeed"), Movement->JumpZVelocity / 100.0);
    Values->SetNumberField(TEXT("landingRecoveryDuration"), 0.2);
    Values->SetNumberField(TEXT("leanHalfLife"), AnimationSettings->General.LeanInterpolationHalfLife);
    Values->SetNumberField(TEXT("movingSpeedThreshold"), CharacterSettings->MovingSpeedThreshold / 100.0);
    Values->SetNumberField(TEXT("playRateMaximum"), 3.0);
    Values->SetNumberField(TEXT("playRateMinimum"), 0.0);
    Values->SetNumberField(TEXT("rotationInterpolationHalfLife"), 0.1);
    Values->SetNumberField(TEXT("runBackwardSpeed"), Standing->RunBackwardSpeed / 100.0);
    Values->SetNumberField(TEXT("runForwardSpeed"), Standing->RunForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("sprintSpeed"), Standing->SprintSpeed / 100.0);
    Values->SetNumberField(TEXT("standingHalfHeight"), Character.GetCapsuleComponent()->GetUnscaledCapsuleHalfHeight() / 100.0);
    Values->SetNumberField(TEXT("targetYawInterpolationSpeed"), 12.0);
    Values->SetNumberField(TEXT("velocityAngleInterpolationEnd"), FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Max));
    Values->SetNumberField(TEXT("velocityAngleInterpolationStart"), FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Min));
    Values->SetNumberField(TEXT("velocitySmoothingHalfLife"), 0.1);
    Values->SetNumberField(TEXT("walkBackwardSpeed"), Standing->WalkBackwardSpeed / 100.0);
    Values->SetNumberField(TEXT("walkForwardSpeed"), Standing->WalkForwardSpeed / 100.0);

    const TSharedRef<FJsonObject> Sources{MakeShared<FJsonObject>()};
	Sources->SetStringField(TEXT("animation"), TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default"));
	Sources->SetStringField(TEXT("character"), TEXT("/ALS/ALS/Data/Character/CS_Als_Default"));
	Sources->SetStringField(TEXT("movement"), TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal"));
    Sources->SetStringField(TEXT("portDefaults"), TEXT("accelerationSmoothingHalfLife,landingRecoveryDuration,playRateMaximum,playRateMinimum,rotationInterpolationHalfLife,targetYawInterpolationSpeed,velocitySmoothingHalfLife"));

    const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
    Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
    Document->SetStringField(TEXT("kind"), TEXT("settings"));
    Document->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    Document->SetStringField(TEXT("referenceCommit"), Commit);
    Document->SetNumberField(TEXT("schemaVersion"), SchemaVersion);
    Document->SetObjectField(TEXT("sources"), Sources);
    Document->SetObjectField(TEXT("values"), Values);
    return Document;
}

TSharedRef<FJsonObject> CreateCommandObject(const FTraceCommand& Command)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("aimYaw"), FMath::DegreesToRadians(Command.AimYaw));
    Object->SetBoolField(TEXT("jumpPressed"), Command.bJumpPressed);
    Object->SetObjectField(TEXT("movementAxes"), Vector2Object(Command.MovementAxes.X, Command.MovementAxes.Y));
    Object->SetStringField(TEXT("requestedGait"), GaitName(Command.Gait));
    Object->SetStringField(TEXT("requestedRotationMode"), RotationModeName(Command.RotationMode));
    Object->SetStringField(TEXT("requestedStance"), StanceName(Command.Stance));
    Object->SetBoolField(TEXT("standBlocked"), Command.bStandBlocked);
    Object->SetNumberField(TEXT("viewYaw"), FMath::DegreesToRadians(Command.ViewYaw));
    return Object;
}

TSharedRef<FJsonObject> SnapshotFrame(AAlsTraceCharacter& Character, const FTraceCommand& Command,
                                       const int32 Frame, const FVector& Origin, const bool bWasGrounded,
                                       FNativeObservationState& NativeState,
                                       const FPortSettings& PortSettings, FPortState& PortState,
                                       FVector& PreviousActualVelocity)
{
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstance()};
    const FAlsStandingState* Standing{GetAnimationState<FAlsStandingState>(Animation, TEXT("StandingState"))};
    const FAlsCrouchingState* Crouching{GetAnimationState<FAlsCrouchingState>(Animation, TEXT("CrouchingState"))};
    const FAlsLeanState* Lean{GetAnimationState<FAlsLeanState>(Animation, TEXT("LeanState"))};
    const FAlsLocomotionAnimationState* Locomotion{GetAnimationState<FAlsLocomotionAnimationState>(Animation, TEXT("LocomotionState"))};
    const bool bGrounded{Movement->IsMovingOnGround()};
    const FVector ActualVelocity{Character.GetVelocity()};
    const FVector ActualAcceleration{(ActualVelocity - PreviousActualVelocity) / FixedDeltaSeconds};
    const FVector LocalVelocity{Character.GetActorQuat().UnrotateVector(Character.GetVelocity())};
    const float Stride{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->StrideBlendAmount : 0.0f)
        : (Standing != nullptr ? Standing->StrideBlendAmount : 0.0f)};
    const float PlayRate{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->PlayRate : 1.0f)
        : (Standing != nullptr ? Standing->PlayRate : 1.0f)};
    NativeState.AnimationPhase = FMath::Fmod(
        NativeState.AnimationPhase + static_cast<float>(FixedDeltaSeconds) * PlayRate, 1.0f);
    const bool bJumpTransition{!bGrounded && bWasGrounded};
    const bool bLandingTransition{bGrounded && !bWasGrounded};
    if (!bGrounded)
    {
        NativeState.LandingRecoveryFramesRemaining = 0;
    }
    else if (bLandingTransition)
    {
        NativeState.LandingRecoveryFramesRemaining = 13;
    }
    const bool bAcceptedJump{bJumpTransition && Command.bJumpPressed};
    if (bAcceptedJump)
    {
        NativeState.bJumpStartActive = true;
    }
    else if (bGrounded || ActualVelocity.Z <= 0.0)
    {
        NativeState.bJumpStartActive = false;
    }
    const FString ObservedAnimationState{!bGrounded
        ? (NativeState.bJumpStartActive ? TEXT("JumpStart") : TEXT("FallLoop"))
        : (NativeState.LandingRecoveryFramesRemaining > 0 ? TEXT("LandRecovery") : TEXT("Grounded"))};
    const FPortResult Port{EvaluatePort(
        Character, Command, bJumpTransition, ActualAcceleration, PortSettings, PortState)};

    const TSharedRef<FJsonObject> PhysicalActual{MakeShared<FJsonObject>()};
    PhysicalActual->SetObjectField(TEXT("acceleration"), Vector3Object(ActualAcceleration));
    PhysicalActual->SetBoolField(TEXT("grounded"), bGrounded);
    PhysicalActual->SetBoolField(TEXT("jumpTransition"), bJumpTransition);
    PhysicalActual->SetNumberField(TEXT("maxAcceleration"), Movement->GetMaxAcceleration() / 100.0);
    PhysicalActual->SetNumberField(TEXT("maxBrakingDeceleration"), Movement->GetMaxBrakingDeceleration() / 100.0);
    PhysicalActual->SetObjectField(TEXT("position"), Vector3Object(Character.GetActorLocation() - Origin));
    PhysicalActual->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    PhysicalActual->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    PhysicalActual->SetObjectField(TEXT("velocity"), Vector3Object(Character.GetVelocity()));
    PhysicalActual->SetNumberField(TEXT("yaw"), FMath::DegreesToRadians(Character.GetActorRotation().Yaw));

    const TSharedRef<FJsonObject> NativeActual{MakeShared<FJsonObject>()};
    NativeActual->SetObjectField(TEXT("blendCoordinates"), Vector2Object(LocalVelocity.Y / 100.0, LocalVelocity.X / 100.0));
    NativeActual->SetStringField(TEXT("gait"), GaitName(Character.GetGait()));
    NativeActual->SetObjectField(TEXT("lean"), Vector2Object(Lean != nullptr ? Lean->RightAmount : 0.0, Lean != nullptr ? Lean->ForwardAmount : 0.0));
    NativeActual->SetStringField(TEXT("locomotionState"), bGrounded ? TEXT("Grounded") : TEXT("InAir"));
    NativeActual->SetStringField(TEXT("observedAnimationState"), ObservedAnimationState);
    NativeActual->SetNumberField(TEXT("playRate"), PlayRate);
    NativeActual->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    NativeActual->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    NativeActual->SetNumberField(TEXT("stride"), Stride);
    NativeActual->SetNumberField(TEXT("synthesizedAnimationPhase"), NativeState.AnimationPhase);
    NativeActual->SetNumberField(TEXT("targetYaw"), FMath::DegreesToRadians(Locomotion != nullptr ? Locomotion->TargetYawAngleWorldSpace : Character.GetActorRotation().Yaw));
    if (NativeState.LandingRecoveryFramesRemaining > 0)
    {
        --NativeState.LandingRecoveryFramesRemaining;
    }

    const TSharedRef<FJsonObject> PortExpected{MakeShared<FJsonObject>()};
    PortExpected->SetNumberField(TEXT("animationPhase"), Port.AnimationPhase);
    PortExpected->SetStringField(TEXT("animationState"), Port.AnimationState);
    PortExpected->SetObjectField(TEXT("blendCoordinates"), Vector2Object(Port.BlendCoordinates.X, Port.BlendCoordinates.Y));
    PortExpected->SetStringField(TEXT("gait"), GaitName(Port.Gait));
    PortExpected->SetObjectField(TEXT("lean"), Vector2Object(Port.Lean.X, Port.Lean.Y));
    PortExpected->SetStringField(TEXT("locomotionState"), Port.LocomotionState);
    PortExpected->SetNumberField(TEXT("playRate"), Port.PlayRate);
    PortExpected->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    PortExpected->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    PortExpected->SetNumberField(TEXT("stride"), Port.Stride);
    PortExpected->SetNumberField(TEXT("targetYaw"), Port.TargetYaw);

    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("command"), CreateCommandObject(Command));
    Object->SetNumberField(TEXT("index"), Frame);
    Object->SetObjectField(TEXT("nativeActual"), NativeActual);
    Object->SetObjectField(TEXT("physicalActual"), PhysicalActual);
    Object->SetObjectField(TEXT("portExpected"), PortExpected);
    Object->SetNumberField(TEXT("tick"), Frame);
    Object->SetNumberField(TEXT("time"), Frame * FixedDeltaSeconds);
    PreviousActualVelocity = ActualVelocity;
    return Object;
}

bool GenerateSequence(UWorld* World, const FSequenceDefinition& Definition, const FString& Commit,
                      const FString& OutputDirectory)
{
    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AAlsTraceCharacter* Character{World->SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
        FVector{0.0, 0.0, 92.0}, FRotator::ZeroRotator, SpawnParameters)};
    AAlsTraceController* Controller{World->SpawnActor<AAlsTraceController>()};
    if (IsValid(Character) && !Character->HasActorBegunPlay()) Character->DispatchBeginPlay();
    if (IsValid(Controller) && IsValid(Character)) Controller->Possess(Character);
    if (!IsValid(Character) || !IsValid(Controller) || !IsValid(Character->GetTraceAnimationInstance()))
    {
        USkeletalMeshComponent* Mesh{IsValid(Character) ? Character->GetMesh() : nullptr};
        const UAnimInstance* MeshAnimationInstance{IsValid(Mesh) ? Mesh->GetAnimInstance() : nullptr};
        UE_LOG(LogTemp, Error,
            TEXT("Could not spawn trace character with UAlsAnimationInstance for %s: character=%d mesh=%d registered=%d animClass=%s animInstance=%s cachedAlsInstance=%d"),
            *Definition.Name, IsValid(Character) ? 1 : 0, IsValid(Mesh) ? 1 : 0,
            IsValid(Mesh) && Mesh->IsRegistered() ? 1 : 0,
            IsValid(Mesh) && IsValid(Mesh->GetAnimClass()) ? *Mesh->GetAnimClass()->GetPathName() : TEXT("<null>"),
            IsValid(MeshAnimationInstance) ? *MeshAnimationInstance->GetClass()->GetPathName() : TEXT("<null>"),
            IsValid(Character) && IsValid(Character->GetTraceAnimationInstance()) ? 1 : 0);
        return false;
    }

    for (int32 Warmup{0}; Warmup < 15; ++Warmup) TickTraceCharacter(*Character);
    const FVector Origin{Character->GetActorLocation()};
    TArray<TSharedPtr<FJsonValue>> Frames;
    Frames.Reserve(Definition.FrameCount);
    AActor* ClearanceBlocker{nullptr};
    bool bWasGrounded{Character->GetTraceMovement()->IsMovingOnGround()};
    FNativeObservationState NativeState;
    const FPortSettings PortSettings{CreatePortSettings(*Character)};
    FPortState PortState;
    FVector PreviousActualVelocity{Character->GetVelocity()};

    for (int32 Frame{0}; Frame < Definition.FrameCount; ++Frame)
    {
        FTraceCommand Command{Definition.GetCommand(Frame)};
        Command.MovementAxes = Command.MovementAxes.GetClampedToMaxSize(1.0f);
        if (Command.bStandBlocked && !IsValid(ClearanceBlocker))
        {
            ClearanceBlocker = World->SpawnActor<AActor>();
            UBoxComponent* Collision{NewObject<UBoxComponent>(ClearanceBlocker, TEXT("ClearanceBlocker"))};
            ClearanceBlocker->SetRootComponent(Collision);
            Collision->SetBoxExtent(FVector{80.0, 80.0, 10.0});
            Collision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
            Collision->RegisterComponent();
            const UCapsuleComponent* Capsule{Character->GetCapsuleComponent()};
            const double CrouchedCapsuleTop{Capsule->GetComponentLocation().Z + Capsule->GetScaledCapsuleHalfHeight()};
            ClearanceBlocker->SetActorLocation(FVector{Character->GetActorLocation().X, Character->GetActorLocation().Y,
                CrouchedCapsuleTop + 15.0});
        }
        else if (!Command.bStandBlocked && IsValid(ClearanceBlocker))
        {
            ClearanceBlocker->Destroy();
            ClearanceBlocker = nullptr;
        }

        Character->SetDesiredGait(Command.Gait);
        Character->SetDesiredStance(Command.Stance);
        Character->SetDesiredRotationMode(Command.RotationMode);
        Character->SetDesiredAiming(Command.RotationMode == AlsRotationModeTags::Aiming);
        Controller->SetControlRotation(FRotator{0.0,
            Command.RotationMode == AlsRotationModeTags::Aiming ? Command.AimYaw : Command.ViewYaw, 0.0});

        const FVector MovementDirection{Command.MovementAxes.Y, Command.MovementAxes.X, 0.0};
        if (!MovementDirection.IsNearlyZero())
        {
            Character->AddMovementInput(MovementDirection.GetSafeNormal(), MovementDirection.Size(), true);
        }
        if (Command.bJumpPressed) Character->Jump();

        TickTraceCharacter(*Character);
        const bool bGrounded{Character->GetTraceMovement()->IsMovingOnGround()};
        Frames.Add(MakeShared<FJsonValueObject>(SnapshotFrame(*Character, Command, Frame, Origin,
            bWasGrounded, NativeState, PortSettings, PortState, PreviousActualVelocity)));
        bWasGrounded = bGrounded;
    }

    Controller->UnPossess();
    Controller->Destroy();
    Character->Destroy();
    const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
    Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
    Document->SetArrayField(TEXT("frames"), Frames);
    Document->SetStringField(TEXT("kind"), TEXT("trace"));
    Document->SetStringField(TEXT("name"), Definition.Name);
    Document->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    Document->SetStringField(TEXT("referenceCommit"), Commit);
    Document->SetNumberField(TEXT("schemaVersion"), SchemaVersion);
    return SaveJson(OutputDirectory / FString::Printf(TEXT("trace_%s.json"), *Definition.Name), Document);
}

struct FP4CaseDefinition
{
    FString CaseId;
    FString Category;
    FString Stance;
    FString Direction;
    FString Phase;
    FString SourceAnimationObjectPath{TEXT("/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle")};
    TArray<FString> SourceCurveNames{TEXT("FootLock_L"), TEXT("FootLock_R")};
    float ViewYaw{0.0f};
    float ViewPitch{0.0f};
    float AimYaw{0.0f};
    float AimPitch{0.0f};
    FString RotationMode{TEXT("LookingDirection")};
    FVector FloorNormal{0.0, 1.0, 0.0};
    int32 PlatformId{-1};
    int64 PlatformColliderId{-1};
    FVector PlatformPosition{ForceInit};
    FQuat PlatformRotation{FQuat::Identity};
    int32 PreviousPlatformId{-1};
    int64 PreviousPlatformColliderId{-1};
    FVector PreviousPlatformPosition{ForceInit};
    FQuat PreviousPlatformRotation{FQuat::Identity};
    uint8 bInitialLocked{0};
    FVector LeftOrigin{-0.2, 0.9, 0.0};
    FVector RightOrigin{0.2, 0.9, 0.0};
    FVector LeftHitPosition{-0.2, 0.0, 0.0};
    FVector RightHitPosition{0.2, 0.0, 0.0};
    uint8 bValidHits{0};
    float LeftIkWeight{0.0f};
    float RightIkWeight{0.0f};
    float LeftLockCurve{0.0f};
    float RightLockCurve{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
};

struct FP4FootLockSnapshot
{
    FVector LeftWorld{ForceInit};
    FVector RightWorld{ForceInit};
    FVector LeftBase{ForceInit};
    FVector RightBase{ForceInit};
    FQuat LeftWorldRotation{FQuat::Identity};
    FQuat RightWorldRotation{FQuat::Identity};
    FQuat LeftBaseRotation{FQuat::Identity};
    FQuat RightBaseRotation{FQuat::Identity};
    float LeftAmount{0.0f};
    float RightAmount{0.0f};
};

struct FP4PlatformSnapshot
{
    UPrimitiveComponent* Component{nullptr};
    int32 PlatformId{-1};
    int64 ColliderId{-1};
    FTransform Transform{FTransform::Identity};
};

struct FP4FootHitCapture
{
    uint8 bValid{0};
    uint8 bWalkable{0};
    FVector Position{ForceInit};
    FVector Normal{0.0, 1.0, 0.0};
    int32 PlatformId{-1};
    int64 ColliderId{-1};
    FVector PlatformPosition{ForceInit};
    FQuat PlatformRotation{FQuat::Identity};
    FVector PointVelocity{ForceInit};
};

struct FP4StimulusCapture
{
    FVector CharacterPosition{ForceInit};
    float CharacterYaw{0.0f};
    float ViewYaw{0.0f};
    float ViewPitch{0.0f};
    float AimYaw{0.0f};
    float AimPitch{0.0f};
    FString RotationMode;
    FString Stance;
    uint8 bGrounded{0};
    float Speed{0.0f};
    float Acceleration{0.0f};
    FVector FloorNormal{0.0, 1.0, 0.0};
    FP4PlatformSnapshot CurrentPlatform;
    FP4PlatformSnapshot PreviousPlatform;
    FVector PlatformAngularVelocity{ForceInit};
    FP4FootLockSnapshot InitialLocks;
    FVector LeftFootOrigin{ForceInit};
    FVector RightFootOrigin{ForceInit};
    FP4FootHitCapture LeftFootHit;
    FP4FootHitCapture RightFootHit;
    float LeftIkWeight{0.0f};
    float RightIkWeight{0.0f};
    float LeftLockCurve{0.0f};
    float RightLockCurve{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
    float PreviousYawPhase{0.0f};
    float YawPhasePlayRate{0.0f};
    int32 TransitionFrameDelta{0};
    uint8 bLeftPlatformRemoved{0};
    int32 LeftRemovedPlatformId{-1};
    int64 LeftRemovedColliderId{-1};
    uint8 bRightPlatformRemoved{0};
    int32 RightRemovedPlatformId{-1};
    int64 RightRemovedColliderId{-1};
};

struct FP4RuntimePlayerObservation
{
    FString SourceAnimationObjectPath;
    FString InstanceClassPath;
    FString NodePropertyName;
    int32 NodePropertyOrdinal{-1};
    float BlendWeight{0.0f};
    float CurrentTimeSeconds{0.0f};
    float PreviousTimeSeconds{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
    float PreviousPhase{0.0f};
    float CurrentPhase{0.0f};
    float PhasePlayRate{0.0f};
};

struct FP4NativeRuntimeCapture
{
    FP4PlatformSnapshot CurrentPlatform;
    FP4PlatformSnapshot PreviousPlatform;
    FVector FloorNormal{0.0, 1.0, 0.0};
    FP4FootHitCapture LeftFootHit;
    FP4FootHitCapture RightFootHit;
};

struct FP4CaseRuntimeOwner
{
    explicit FP4CaseRuntimeOwner(UWorld& InWorld) : World(InWorld) {}

    ~FP4CaseRuntimeOwner()
    {
        if (IsValid(Controller)) Controller->UnPossess();
        Destroy(Controller);
        Destroy(Character);
        for (AActor* Actor : SceneActors) Destroy(Actor);
    }

    void Destroy(AActor* Actor) const
    {
        if (IsValid(Actor) && !Actor->IsActorBeingDestroyed()) World.DestroyActor(Actor);
    }

    UWorld& World;
    TArray<AActor*> SceneActors;
    AAlsTraceCharacter* Character{nullptr};
    AAlsTraceController* Controller{nullptr};
};

bool GetP4SelectionIds(const FString& RuntimeSourceAnimationObjectPath, const FString& Category,
                       int32& TurnAnimationId, int32& TurnCurveId, int32& TurnNominalDegrees,
                       int32& RotateAnimationId, int32& RotateCurveId);

struct FP4TickEvidence
{
    int32 AnimationUpdateCount{0};
    int32 AnimationEvaluationCount{0};
    int32 AnimationPostUpdateCount{0};
    int32 PublicAnimationTickCount{0};
};

FP4TickEvidence LastP4TickEvidence;

TSharedRef<FJsonObject> PortVector3Object(const FVector& Value)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X);
    Object->SetNumberField(TEXT("y"), Value.Y);
    Object->SetNumberField(TEXT("z"), Value.Z);
    return Object;
}

TSharedRef<FJsonObject> QuaternionObject(const FQuat& Source)
{
    FQuat Value{Source.GetNormalized()};
    if (Value.W < 0.0) Value = Value * -1.0;
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X);
    Object->SetNumberField(TEXT("y"), Value.Y);
    Object->SetNumberField(TEXT("z"), Value.Z);
    Object->SetNumberField(TEXT("w"), Value.W);
    return Object;
}

TSharedRef<FJsonObject> TransformObject(const FVector& Position, const FQuat& Rotation)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("position"), PortVector3Object(Position));
    Object->SetObjectField(TEXT("rotation"), QuaternionObject(Rotation));
    return Object;
}

TSharedRef<FJsonObject> P4ReferenceObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("repository"), TEXT("https://github.com/Sixze/ALS-Refactored.git"));
    Object->SetStringField(TEXT("commit"), LockedReferenceCommit);
    Object->SetStringField(TEXT("targetEngine"), TEXT("5.9.0"));
    Object->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    return Object;
}

TSharedRef<FJsonObject> P4CoordinateSystemObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("source"), TEXT("UE5_XForward_YRight_ZUp"));
    Object->SetStringField(TEXT("target"), TEXT("Godot_XRight_YUp_ZBack"));
    Object->SetStringField(TEXT("distanceUnit"), TEXT("meter"));
    Object->SetStringField(TEXT("angleUnit"), TEXT("radian"));
    Object->SetStringField(TEXT("quaternionSign"), TEXT("canonical_nonnegative_w"));
    return Object;
}

TSharedRef<FJsonObject> P4SourcesObject(const UClass& AimOverlayClass)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("characterSettings"), TEXT("/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default"));
    Object->SetStringField(TEXT("movementSettings"), TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal"));
    Object->SetStringField(TEXT("animationSettings"), TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default"));
    Object->SetStringField(TEXT("animationBlueprint"), TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));
    Object->SetStringField(TEXT("aimOverlayAnimationBlueprint"), AimOverlayClass.GetPathName());
    Object->SetStringField(TEXT("skeletalMesh"), TEXT("/ALS/ALS/Character/SKM_Als.SKM_Als"));
    Object->SetStringField(TEXT("controlRig"), TEXT("/ALS/ALS/Character/CR_Als.CR_Als_C"));
    return Object;
}

TSharedRef<FJsonObject> P4TolerancesObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("positionMeters"), 0.001);
    Object->SetNumberField(TEXT("rotationRadians"), 0.0017453292519943296);
    return Object;
}

TSharedRef<FJsonObject> P4FootHitObject(const FP4CaseDefinition& Definition, const bool bLeft)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("valid"), Definition.bValidHits);
    Object->SetNumberField(TEXT("walkable"), Definition.bValidHits);
    Object->SetObjectField(TEXT("position"), PortVector3Object(bLeft ? Definition.LeftHitPosition : Definition.RightHitPosition));
    Object->SetObjectField(TEXT("normal"), PortVector3Object(Definition.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Definition.PlatformId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Definition.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Definition.PlatformRotation));
    Object->SetNumberField(TEXT("colliderId"), Definition.bValidHits ? Definition.PlatformColliderId : -1);
    Object->SetObjectField(TEXT("pointVelocity"), PortVector3Object(FVector::ZeroVector));
    return Object;
}

TSharedRef<FJsonObject> P4StimulusObject(const FP4CaseDefinition& Definition)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("characterPosition"), PortVector3Object(FVector::ZeroVector));
    Object->SetNumberField(TEXT("characterYaw"), 0.0);
    Object->SetNumberField(TEXT("viewYaw"), Definition.ViewYaw);
    Object->SetNumberField(TEXT("viewPitch"), Definition.ViewPitch);
    Object->SetNumberField(TEXT("aimYaw"), Definition.AimYaw);
    Object->SetNumberField(TEXT("aimPitch"), Definition.AimPitch);
    Object->SetStringField(TEXT("rotationMode"), Definition.RotationMode);
    Object->SetStringField(TEXT("stance"), Definition.Stance);
    Object->SetNumberField(TEXT("grounded"), 1);
    Object->SetNumberField(TEXT("speed"), 0.0);
    Object->SetNumberField(TEXT("acceleration"), 0.0);
    Object->SetObjectField(TEXT("floorNormal"), PortVector3Object(Definition.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Definition.PlatformId);
    Object->SetNumberField(TEXT("platformColliderId"), Definition.PlatformColliderId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Definition.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Definition.PlatformRotation));
    Object->SetObjectField(TEXT("platformAngularVelocity"), PortVector3Object(FVector::ZeroVector));
    Object->SetNumberField(TEXT("previousPlatformId"), Definition.PreviousPlatformId);
    Object->SetNumberField(TEXT("previousPlatformColliderId"), Definition.PreviousPlatformColliderId);
    Object->SetObjectField(TEXT("previousPlatformPosition"), PortVector3Object(Definition.PreviousPlatformPosition));
    Object->SetObjectField(TEXT("previousPlatformRotation"), QuaternionObject(Definition.PreviousPlatformRotation));
    const auto CreateInitialLock{[&Definition](const bool bLeft)
    {
        const TSharedRef<FJsonObject> Lock{MakeShared<FJsonObject>()};
        Lock->SetObjectField(TEXT("localPosition"), PortVector3Object(bLeft ? Definition.LeftOrigin : Definition.RightOrigin));
        Lock->SetObjectField(TEXT("localRotation"), QuaternionObject(FQuat::Identity));
        Lock->SetNumberField(TEXT("amount"), Definition.bInitialLocked ? 1.0 : 0.0);
        Lock->SetNumberField(TEXT("locked"), Definition.bInitialLocked);
        return Lock;
    }};
    Object->SetObjectField(TEXT("initialLeftLock"), CreateInitialLock(true));
    Object->SetObjectField(TEXT("initialRightLock"), CreateInitialLock(false));
    Object->SetObjectField(TEXT("leftFootOrigin"), PortVector3Object(Definition.LeftOrigin));
    Object->SetObjectField(TEXT("rightFootOrigin"), PortVector3Object(Definition.RightOrigin));
    Object->SetObjectField(TEXT("leftFootHit"), P4FootHitObject(Definition, true));
    Object->SetObjectField(TEXT("rightFootHit"), P4FootHitObject(Definition, false));
    Object->SetNumberField(TEXT("leftIkWeight"), Definition.LeftIkWeight);
    Object->SetNumberField(TEXT("rightIkWeight"), Definition.RightIkWeight);
    Object->SetNumberField(TEXT("leftLockCurve"), Definition.LeftLockCurve);
    Object->SetNumberField(TEXT("rightLockCurve"), Definition.RightLockCurve);
    Object->SetNumberField(TEXT("previousYawCurve"), Definition.PreviousYawCurve);
    Object->SetNumberField(TEXT("currentYawCurve"), Definition.CurrentYawCurve);
    return Object;
}

FVector ToPortPosition(const FVector& UePosition)
{
    return FVector{UePosition.Y / 100.0, UePosition.Z / 100.0, -UePosition.X / 100.0};
}

FVector ToPortDirection(const FVector& UeDirection)
{
    return FVector{UeDirection.Y, UeDirection.Z, -UeDirection.X};
}

FQuat ToPortRotation(const FQuat& UeRotation)
{
    const FVector XAxis{ToPortDirection(UeRotation.RotateVector(FVector::RightVector))};
    const FVector YAxis{ToPortDirection(UeRotation.RotateVector(FVector::UpVector))};
    const FVector ZAxis{ToPortDirection(UeRotation.RotateVector(-FVector::ForwardVector))};
    FMatrix Matrix{FMatrix::Identity};
    Matrix.SetAxes(&XAxis, &YAxis, &ZAxis);
    FQuat Result{Matrix};
    Result.Normalize();
    if (Result.W < 0.0) Result = Result * -1.0;
    return Result;
}

int64 P4ColliderId(const UPrimitiveComponent* Component, const UPrimitiveComponent* Primary,
                   const UPrimitiveComponent* Secondary)
{
    if (Component == nullptr) return -1;
    if (Component == Primary) return 10;
    if (Component == Secondary) return 20;
    return 100;
}

FP4PlatformSnapshot CaptureP4Platform(UPrimitiveComponent* Component, const UPrimitiveComponent* Primary,
                                      const UPrimitiveComponent* Secondary)
{
    FP4PlatformSnapshot Snapshot;
    Snapshot.Component = Component;
    Snapshot.PlatformId = Component == Primary ? 1 : Component == Secondary ? 2 : -1;
    Snapshot.ColliderId = P4ColliderId(Component, Primary, Secondary);
    if (Component != nullptr) Snapshot.Transform = Component->GetComponentTransform();
    return Snapshot;
}

FP4FootLockSnapshot CaptureP4FootLocks(const FAlsFeetState& Feet)
{
    FP4FootLockSnapshot Snapshot;
    Snapshot.LeftWorld = Feet.Left.LockLocationWorldSpace;
    Snapshot.RightWorld = Feet.Right.LockLocationWorldSpace;
    Snapshot.LeftBase = FVector{Feet.Left.LockLocationMovementBaseSpace};
    Snapshot.RightBase = FVector{Feet.Right.LockLocationMovementBaseSpace};
    Snapshot.LeftWorldRotation = Feet.Left.LockRotationWorldSpace;
    Snapshot.RightWorldRotation = Feet.Right.LockRotationWorldSpace;
    Snapshot.LeftBaseRotation = FQuat{Feet.Left.LockRotationMovementBaseSpace};
    Snapshot.RightBaseRotation = FQuat{Feet.Right.LockRotationMovementBaseSpace};
    Snapshot.LeftAmount = Feet.Left.LockAmount;
    Snapshot.RightAmount = Feet.Right.LockAmount;
    return Snapshot;
}

FP4FootHitCapture CaptureP4FootHit(UWorld& World, AAlsTraceCharacter& Character, const FVector& Origin,
                                  const FP4PlatformSnapshot& CurrentPlatform,
                                  const FP4PlatformSnapshot& PreviousPlatform,
                                  const UPrimitiveComponent* Primary, const UPrimitiveComponent* Secondary)
{
    FP4FootHitCapture Capture;
    FHitResult Hit;
    FCollisionQueryParams QueryParams{SCENE_QUERY_STAT(P4GoldenFootHit), false, &Character};
    if (!World.LineTraceSingleByChannel(Hit, Origin + FVector::UpVector * 50.0,
        Origin - FVector::UpVector * 150.0, ECC_Visibility, QueryParams))
    {
        return Capture;
    }

    UPrimitiveComponent* Component{Hit.GetComponent()};
    Capture.bValid = 1;
    Capture.bWalkable = Character.GetCharacterMovement()->IsWalkable(Hit) ? 1 : 0;
    Capture.Position = ToPortPosition(Hit.ImpactPoint);
    Capture.Normal = ToPortDirection(Hit.ImpactNormal).GetSafeNormal();
    Capture.ColliderId = P4ColliderId(Component, Primary, Secondary);
    if (Component == CurrentPlatform.Component)
    {
        Capture.PlatformId = CurrentPlatform.PlatformId;
        Capture.PlatformPosition = ToPortPosition(CurrentPlatform.Transform.GetLocation());
        Capture.PlatformRotation = ToPortRotation(CurrentPlatform.Transform.GetRotation());
        if (PreviousPlatform.Component == CurrentPlatform.Component)
        {
            const FVector LinearVelocity{
                (CurrentPlatform.Transform.GetLocation() - PreviousPlatform.Transform.GetLocation()) / FixedDeltaSeconds};
            const FQuat RotationDelta{CurrentPlatform.Transform.GetRotation() * PreviousPlatform.Transform.GetRotation().Inverse()};
            const FVector AngularVelocity{RotationDelta.GetNormalized().ToRotationVector() / FixedDeltaSeconds};
            Capture.PointVelocity = ToPortDirection(LinearVelocity + FVector::CrossProduct(
                AngularVelocity, Hit.ImpactPoint - CurrentPlatform.Transform.GetLocation())) / 100.0;
        }
    }
    return Capture;
}

bool CaptureP4Stimulus(UWorld& World, AAlsTraceCharacter& Character, const FP4CaseDefinition& Definition,
                       const FP4PlatformSnapshot& PreviousPlatform, const FP4FootLockSnapshot& InitialLocks,
                       UPrimitiveComponent* Primary, UPrimitiveComponent* Secondary,
                       const FP4RuntimePlayerObservation& RuntimePlayer, const int32 TransitionFrameDelta,
                       FP4StimulusCapture& Capture)
{
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
    if (Feet == nullptr) return false;
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    if (Mesh->GetBoneIndex(TEXT("ik_foot_l")) < 0 || Mesh->GetBoneIndex(TEXT("ik_foot_r")) < 0)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 IK foot bone is unavailable case=%s"), *Definition.CaseId);
        return false;
    }

    const float CapsuleHalfHeight{Character.GetCapsuleComponent()->GetScaledCapsuleHalfHeight()};
    Capture.CharacterPosition = ToPortPosition(
        Character.GetActorLocation() - FVector::UpVector * CapsuleHalfHeight);
    Capture.CharacterYaw = -FMath::DegreesToRadians(Character.GetActorRotation().Yaw);
    const FRotator ViewRotation{Character.GetViewState().Rotation};
    Capture.ViewYaw = -FMath::DegreesToRadians(ViewRotation.Yaw);
    Capture.ViewPitch = FMath::DegreesToRadians(ViewRotation.Pitch);
    Capture.AimYaw = Capture.ViewYaw;
    Capture.AimPitch = Capture.ViewPitch;
    Capture.RotationMode = Definition.RotationMode;
    Capture.Stance = Definition.Stance;
    const UCharacterMovementComponent* Movement{Character.GetCharacterMovement()};
    Capture.bGrounded = Movement->IsMovingOnGround() ? 1 : 0;
    Capture.Speed = Movement->Velocity.Size2D() / 100.0f;
    Capture.Acceleration = Movement->GetCurrentAcceleration().Size2D() / 100.0f;
    Capture.FloorNormal = Movement->CurrentFloor.bBlockingHit
        ? ToPortDirection(Movement->CurrentFloor.HitResult.ImpactNormal).GetSafeNormal()
        : FVector{0.0, 1.0, 0.0};
    Capture.PreviousPlatform = PreviousPlatform;
    Capture.CurrentPlatform = CaptureP4Platform(Character.GetMovementBase(), Primary, Secondary);
    if (Capture.CurrentPlatform.Component == Capture.PreviousPlatform.Component &&
        Capture.CurrentPlatform.Component != nullptr)
    {
        const FQuat RotationDelta{Capture.CurrentPlatform.Transform.GetRotation() *
            Capture.PreviousPlatform.Transform.GetRotation().Inverse()};
        Capture.PlatformAngularVelocity = ToPortDirection(
            RotationDelta.GetNormalized().ToRotationVector() / FixedDeltaSeconds);
    }
    Capture.InitialLocks = InitialLocks;
    const FVector LeftOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_l"), EBoneSpaces::WorldSpace)};
    const FVector RightOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_r"), EBoneSpaces::WorldSpace)};
    Capture.LeftFootOrigin = ToPortPosition(LeftOrigin);
    Capture.RightFootOrigin = ToPortPosition(RightOrigin);
    Capture.LeftFootHit = CaptureP4FootHit(World, Character, LeftOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.RightFootHit = CaptureP4FootHit(World, Character, RightOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.LeftIkWeight = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootLeftIkCurveName()), 0.0f, 1.0f);
    Capture.RightIkWeight = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootRightIkCurveName()), 0.0f, 1.0f);
    Capture.LeftLockCurve = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootLeftLockCurveName()), 0.0f, 1.0f);
    Capture.RightLockCurve = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootRightLockCurveName()), 0.0f, 1.0f);
    Capture.PreviousYawCurve = RuntimePlayer.PreviousYawCurve;
    Capture.CurrentYawCurve = RuntimePlayer.CurrentYawCurve;
    Capture.PreviousYawPhase = RuntimePlayer.PreviousPhase;
    Capture.YawPhasePlayRate = RuntimePlayer.PhasePlayRate;
    Capture.TransitionFrameDelta = TransitionFrameDelta;
    const bool bPlatformRemoved{Definition.CaseId == TEXT("platform_release") &&
        Capture.PreviousPlatform.PlatformId >= 0 && Capture.PreviousPlatform.ColliderId >= 0 &&
        Capture.CurrentPlatform.PlatformId < 0};
    Capture.bLeftPlatformRemoved = bPlatformRemoved ? 1 : 0;
    Capture.LeftRemovedPlatformId = bPlatformRemoved ? Capture.PreviousPlatform.PlatformId : -1;
    Capture.LeftRemovedColliderId = bPlatformRemoved ? Capture.PreviousPlatform.ColliderId : -1;
    Capture.bRightPlatformRemoved = bPlatformRemoved ? 1 : 0;
    Capture.RightRemovedPlatformId = bPlatformRemoved ? Capture.PreviousPlatform.PlatformId : -1;
    Capture.RightRemovedColliderId = bPlatformRemoved ? Capture.PreviousPlatform.ColliderId : -1;
    return true;
}

bool CaptureP4NativeRuntime(UWorld& World, AAlsTraceCharacter& Character,
                            const FP4PlatformSnapshot& PreviousPlatform,
                            UPrimitiveComponent* Primary, UPrimitiveComponent* Secondary,
                            FP4NativeRuntimeCapture& Capture)
{
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    if (Mesh == nullptr || Mesh->GetBoneIndex(TEXT("ik_foot_l")) < 0 ||
        Mesh->GetBoneIndex(TEXT("ik_foot_r")) < 0)
    {
        return false;
    }
    const UCharacterMovementComponent* Movement{Character.GetCharacterMovement()};
    Capture.PreviousPlatform = PreviousPlatform;
    Capture.CurrentPlatform = CaptureP4Platform(Character.GetMovementBase(), Primary, Secondary);
    Capture.FloorNormal = Movement->CurrentFloor.bBlockingHit
        ? ToPortDirection(Movement->CurrentFloor.HitResult.ImpactNormal).GetSafeNormal()
        : FVector{0.0, 1.0, 0.0};
    const FVector LeftOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_l"), EBoneSpaces::WorldSpace)};
    const FVector RightOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_r"), EBoneSpaces::WorldSpace)};
    Capture.LeftFootHit = CaptureP4FootHit(World, Character, LeftOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.RightFootHit = CaptureP4FootHit(World, Character, RightOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    return true;
}

TSharedRef<FJsonObject> P4FootHitObject(const FP4FootHitCapture& Capture)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("valid"), Capture.bValid);
    Object->SetNumberField(TEXT("walkable"), Capture.bWalkable);
    Object->SetObjectField(TEXT("position"), PortVector3Object(Capture.Position));
    Object->SetObjectField(TEXT("normal"), PortVector3Object(Capture.Normal));
    Object->SetNumberField(TEXT("platformId"), Capture.PlatformId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Capture.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Capture.PlatformRotation));
    Object->SetNumberField(TEXT("colliderId"), Capture.ColliderId);
    Object->SetObjectField(TEXT("pointVelocity"), PortVector3Object(Capture.PointVelocity));
    return Object;
}

TSharedRef<FJsonObject> P4StimulusObject(const FP4StimulusCapture& Capture)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("characterPosition"), PortVector3Object(Capture.CharacterPosition));
    Object->SetNumberField(TEXT("characterYaw"), Capture.CharacterYaw);
    Object->SetNumberField(TEXT("viewYaw"), Capture.ViewYaw);
    Object->SetNumberField(TEXT("viewPitch"), Capture.ViewPitch);
    Object->SetNumberField(TEXT("aimYaw"), Capture.AimYaw);
    Object->SetNumberField(TEXT("aimPitch"), Capture.AimPitch);
    Object->SetStringField(TEXT("rotationMode"), Capture.RotationMode);
    Object->SetStringField(TEXT("stance"), Capture.Stance);
    Object->SetNumberField(TEXT("grounded"), Capture.bGrounded);
    Object->SetNumberField(TEXT("speed"), Capture.Speed);
    Object->SetNumberField(TEXT("acceleration"), Capture.Acceleration);
    Object->SetObjectField(TEXT("floorNormal"), PortVector3Object(Capture.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Capture.CurrentPlatform.PlatformId);
    Object->SetNumberField(TEXT("platformColliderId"), Capture.CurrentPlatform.ColliderId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(
        ToPortPosition(Capture.CurrentPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(
        ToPortRotation(Capture.CurrentPlatform.Transform.GetRotation())));
    Object->SetObjectField(TEXT("platformAngularVelocity"), PortVector3Object(Capture.PlatformAngularVelocity));
    Object->SetNumberField(TEXT("previousPlatformId"), Capture.PreviousPlatform.PlatformId);
    Object->SetNumberField(TEXT("previousPlatformColliderId"), Capture.PreviousPlatform.ColliderId);
    Object->SetObjectField(TEXT("previousPlatformPosition"), PortVector3Object(
        ToPortPosition(Capture.PreviousPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("previousPlatformRotation"), QuaternionObject(
        ToPortRotation(Capture.PreviousPlatform.Transform.GetRotation())));
    const auto InitialLock{[](const FVector& Position, const FQuat& Rotation, const float Amount)
    {
        const TSharedRef<FJsonObject> Lock{MakeShared<FJsonObject>()};
        Lock->SetObjectField(TEXT("localPosition"), PortVector3Object(Amount > 0.0f ? ToPortPosition(Position) : FVector::ZeroVector));
        Lock->SetObjectField(TEXT("localRotation"), QuaternionObject(Amount > 0.0f ? ToPortRotation(Rotation) : FQuat::Identity));
        Lock->SetNumberField(TEXT("amount"), Amount);
        Lock->SetNumberField(TEXT("locked"), Amount > 0.0f ? 1 : 0);
        return Lock;
    }};
    const bool bPlatformLock{Capture.PreviousPlatform.PlatformId >= 0};
    Object->SetObjectField(TEXT("initialLeftLock"), InitialLock(
        bPlatformLock ? Capture.InitialLocks.LeftBase : Capture.InitialLocks.LeftWorld,
        bPlatformLock ? Capture.InitialLocks.LeftBaseRotation : Capture.InitialLocks.LeftWorldRotation,
        Capture.InitialLocks.LeftAmount));
    Object->SetObjectField(TEXT("initialRightLock"), InitialLock(
        bPlatformLock ? Capture.InitialLocks.RightBase : Capture.InitialLocks.RightWorld,
        bPlatformLock ? Capture.InitialLocks.RightBaseRotation : Capture.InitialLocks.RightWorldRotation,
        Capture.InitialLocks.RightAmount));
    Object->SetObjectField(TEXT("leftFootOrigin"), PortVector3Object(Capture.LeftFootOrigin));
    Object->SetObjectField(TEXT("rightFootOrigin"), PortVector3Object(Capture.RightFootOrigin));
    Object->SetObjectField(TEXT("leftFootHit"), P4FootHitObject(Capture.LeftFootHit));
    Object->SetObjectField(TEXT("rightFootHit"), P4FootHitObject(Capture.RightFootHit));
    Object->SetNumberField(TEXT("leftIkWeight"), Capture.LeftIkWeight);
    Object->SetNumberField(TEXT("rightIkWeight"), Capture.RightIkWeight);
    Object->SetNumberField(TEXT("leftLockCurve"), Capture.LeftLockCurve);
    Object->SetNumberField(TEXT("rightLockCurve"), Capture.RightLockCurve);
    Object->SetNumberField(TEXT("previousYawCurve"), Capture.PreviousYawCurve);
    Object->SetNumberField(TEXT("currentYawCurve"), Capture.CurrentYawCurve);
    Object->SetNumberField(TEXT("previousYawPhase"), Capture.PreviousYawPhase);
    Object->SetNumberField(TEXT("yawPhasePlayRate"), Capture.YawPhasePlayRate);
    Object->SetNumberField(TEXT("transitionFrameDelta"), Capture.TransitionFrameDelta);
    const TSharedRef<FJsonObject> RemovalSignals{MakeShared<FJsonObject>()};
    RemovalSignals->SetNumberField(TEXT("leftRemoved"), Capture.bLeftPlatformRemoved);
    RemovalSignals->SetNumberField(TEXT("leftPlatformId"), Capture.LeftRemovedPlatformId);
    RemovalSignals->SetNumberField(TEXT("leftColliderId"), Capture.LeftRemovedColliderId);
    RemovalSignals->SetNumberField(TEXT("rightRemoved"), Capture.bRightPlatformRemoved);
    RemovalSignals->SetNumberField(TEXT("rightPlatformId"), Capture.RightRemovedPlatformId);
    RemovalSignals->SetNumberField(TEXT("rightColliderId"), Capture.RightRemovedColliderId);
    Object->SetObjectField(TEXT("platformRemovalSignals"), RemovalSignals);
    return Object;
}

TSharedPtr<FJsonObject> P4BoneTransformObject(USkeletalMeshComponent& Mesh, const FName BoneName)
{
    const int32 BoneIndex{Mesh.GetBoneIndex(BoneName)};
    if (BoneIndex < 0)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required bone is unavailable bone=%s"), *BoneName.ToString());
        return nullptr;
    }
    const TArray<FTransform> LocalTransforms{Mesh.GetBoneSpaceTransforms()};
    if (!LocalTransforms.IsValidIndex(BoneIndex))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required bone transform is unavailable bone=%s index=%d count=%d"),
            *BoneName.ToString(), BoneIndex, LocalTransforms.Num());
        return nullptr;
    }
    const FTransform& Transform{LocalTransforms[BoneIndex]};
    return TransformObject(ToPortPosition(Transform.GetLocation()), ToPortRotation(Transform.GetRotation()));
}

TSharedPtr<FJsonObject> P4BoneComponentTransformObject(USkeletalMeshComponent& Mesh, const FName BoneName)
{
    const int32 BoneIndex{Mesh.GetBoneIndex(BoneName)};
    const TArray<FTransform>& ComponentTransforms{Mesh.GetComponentSpaceTransforms()};
    if (BoneIndex < 0 || !ComponentTransforms.IsValidIndex(BoneIndex))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required component-space bone transform is unavailable bone=%s index=%d count=%d"),
            *BoneName.ToString(), BoneIndex, ComponentTransforms.Num());
        return nullptr;
    }
    const FTransform& Transform{ComponentTransforms[BoneIndex]};
    return TransformObject(ToPortPosition(Transform.GetLocation()), ToPortRotation(Transform.GetRotation()));
}

TSharedRef<FJsonObject> P4CaseSourceObject(const FP4CaseDefinition& Definition,
                                           const FString& SourceAnimationObjectPath)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("animationObjectPath"), SourceAnimationObjectPath);
    Object->SetStringField(TEXT("curveSourceObjectPath"), SourceAnimationObjectPath);
    TArray<TSharedPtr<FJsonValue>> CurveNames;
    for (const FString& CurveName : Definition.SourceCurveNames)
    {
        CurveNames.Add(MakeShared<FJsonValueString>(CurveName));
    }
    Object->SetArrayField(TEXT("curveNames"), CurveNames);
    return Object;
}

TSharedPtr<FJsonObject> P4NativeActualObject(AAlsTraceCharacter& Character, const FP4CaseDefinition& Definition,
                                             const float PreviousLeftFootLockAmount,
                                             const float PreviousRightFootLockAmount,
                                             const FP4FootLockSnapshot& PreviousFootLocks,
                                             const float TurnPhase, const float RotatePhase,
                                             const float TurnAccumulatedYaw, const float RotateAccumulatedYaw,
                                             const FString& LeftFootReleaseReason,
                                             const FString& RightFootReleaseReason,
                                             const bool bSourceSelectionVerified,
                                             const FP4RuntimePlayerObservation& RuntimePlayer,
                                             const int32 TransitionFrameDelta,
                                             const FP4NativeRuntimeCapture& RuntimeCapture)
{
    const UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstance()};
    const FAlsViewAnimationState* View{GetAnimationState<FAlsViewAnimationState>(Animation, TEXT("ViewState"))};
    const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(Animation, TEXT("SpineState"))};
    const FAlsLayeringState* Layering{GetAnimationState<FAlsLayeringState>(Animation, TEXT("LayeringState"))};
    const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
    const FAlsTurnInPlaceState* Turn{GetAnimationState<FAlsTurnInPlaceState>(Animation, TEXT("TurnInPlaceState"))};
    const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(Animation, TEXT("RotateInPlaceState"))};
    const FAlsMovementBaseState* MovementBase{GetAnimationState<FAlsMovementBaseState>(Animation, TEXT("MovementBase"))};
    if (View == nullptr || Spine == nullptr || Layering == nullptr || Feet == nullptr || Turn == nullptr ||
        Rotate == nullptr || MovementBase == nullptr)
    {
        UE_LOG(LogTemp, Error,
            TEXT("P4 native ALS state unavailable case=%s view=%d spine=%d layering=%d feet=%d turn=%d rotate=%d base=%d"),
            *Definition.CaseId, View != nullptr, Spine != nullptr, Layering != nullptr, Feet != nullptr,
            Turn != nullptr, Rotate != nullptr, MovementBase != nullptr);
        return nullptr;
    }

    const bool bTurnCase{Definition.Category == TEXT("Turn")};
    const bool bTurnActive{bTurnCase && Animation->GetCurrentActiveMontage() != nullptr};
    const int32 TurnDirection{bTurnActive ? (View->YawAngle < 0.0f ? 1 : -1) : 0};
    int32 TurnAnimationId;
    int32 TurnCurveId;
    int32 TurnNominalDegrees;
    int32 RotateAnimationId;
    int32 RotateCurveId;
    const bool bSelectionMapped{GetP4SelectionIds(RuntimePlayer.SourceAnimationObjectPath, Definition.Category,
        TurnAnimationId, TurnCurveId, TurnNominalDegrees, RotateAnimationId, RotateCurveId)};
    const bool bRotateActive{Definition.Category == TEXT("Rotate") &&
        (Rotate->bRotatingLeft || Rotate->bRotatingRight)};
    if ((bTurnActive || bRotateActive) && !bSelectionMapped)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 observed runtime source has no locked ID mapping category=%s path=%s"),
            *Definition.Category, *RuntimePlayer.SourceAnimationObjectPath);
        return nullptr;
    }
    const int32 RotateDirection{bRotateActive ? (Rotate->bRotatingLeft ? 1 : -1) : 0};
    if (!bTurnActive)
    {
        TurnAnimationId = -1;
        TurnCurveId = -1;
        TurnNominalDegrees = 0;
    }
    if (!bRotateActive)
    {
        RotateAnimationId = -1;
        RotateCurveId = -1;
    }
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    // UE positive yaw maps to negative Godot yaw after the (+Y,+Z,-X) basis conversion.
    Object->SetNumberField(TEXT("viewYaw"), -FMath::DegreesToRadians(View->YawAngle));
    Object->SetNumberField(TEXT("viewPitch"), FMath::DegreesToRadians(View->PitchAngle));
    Object->SetNumberField(TEXT("spineYaw"), -FMath::DegreesToRadians(Spine->FinalYawAngle));
    Object->SetNumberField(TEXT("headBlendAmount"), Layering->HeadBlendAmount);
    Object->SetNumberField(TEXT("turnUpdated"), Turn->bUpdatedThisFrame ? 1 : 0);
    Object->SetNumberField(TEXT("turnAnimationId"), TurnAnimationId);
    Object->SetNumberField(TEXT("turnCurveId"), TurnCurveId);
    Object->SetNumberField(TEXT("turnNominalDegrees"), TurnNominalDegrees);
    Object->SetNumberField(TEXT("turnActive"), bTurnActive ? 1 : 0);
    Object->SetNumberField(TEXT("turnDirection"), TurnDirection);
    Object->SetNumberField(TEXT("rotateAnimationId"), RotateAnimationId);
    Object->SetNumberField(TEXT("rotateCurveId"), RotateCurveId);
    Object->SetNumberField(TEXT("rotateDirection"), RotateDirection);
    Object->SetNumberField(TEXT("rotateActive"), bRotateActive ? 1 : 0);
    Object->SetNumberField(TEXT("rotateLeft"), Rotate->bRotatingLeft ? 1 : 0);
    Object->SetNumberField(TEXT("rotateRight"), Rotate->bRotatingRight ? 1 : 0);
    Object->SetNumberField(TEXT("playRate"), bTurnCase ? Turn->PlayRate : Rotate->PlayRate);
    Object->SetNumberField(TEXT("turnPhase"), TurnPhase);
    Object->SetNumberField(TEXT("rotatePhase"), RotatePhase);
    Object->SetNumberField(TEXT("turnAccumulatedYaw"), TurnAccumulatedYaw);
    Object->SetNumberField(TEXT("rotateAccumulatedYaw"), RotateAccumulatedYaw);
    Object->SetStringField(TEXT("runtimeSourceAnimationObjectPath"), RuntimePlayer.SourceAnimationObjectPath);
    Object->SetStringField(TEXT("runtimeInstanceClassPath"), RuntimePlayer.InstanceClassPath);
    Object->SetStringField(TEXT("runtimeNodePropertyName"), RuntimePlayer.NodePropertyName);
    Object->SetNumberField(TEXT("runtimeNodePropertyOrdinal"), RuntimePlayer.NodePropertyOrdinal);
    Object->SetNumberField(TEXT("runtimeSourceWeight"), RuntimePlayer.BlendWeight);
    Object->SetNumberField(TEXT("runtimeSourceTimeSeconds"), RuntimePlayer.CurrentTimeSeconds);
    Object->SetNumberField(TEXT("runtimePreviousSourceTimeSeconds"), RuntimePlayer.PreviousTimeSeconds);
    Object->SetNumberField(TEXT("runtimePreviousYawCurve"), RuntimePlayer.PreviousYawCurve);
    Object->SetNumberField(TEXT("runtimeCurrentYawCurve"), RuntimePlayer.CurrentYawCurve);
    Object->SetNumberField(TEXT("sourceSelectionVerified"), bSourceSelectionVerified ? 1 : 0);
    Object->SetNumberField(TEXT("rotationYawSpeed"),
        Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName()));
    Object->SetNumberField(TEXT("feetValid"), Feet->bValid ? 1 : 0);
    Object->SetNumberField(TEXT("movementBaseChanged"), MovementBase->bBaseChanged ? 1 : 0);
    Object->SetNumberField(TEXT("hasRelativeBaseLocation"), MovementBase->bHasRelativeLocation ? 1 : 0);
    Object->SetNumberField(TEXT("hasRelativeBaseRotation"), MovementBase->bHasRelativeRotation ? 1 : 0);
    Object->SetNumberField(TEXT("movementBaseDeltaYaw"), -FMath::DegreesToRadians(MovementBase->DeltaRotation.Yaw));
    Object->SetNumberField(TEXT("previousLeftFootLockAmount"), PreviousLeftFootLockAmount);
    Object->SetNumberField(TEXT("previousRightFootLockAmount"), PreviousRightFootLockAmount);
    Object->SetNumberField(TEXT("leftFootLockAmount"), Feet->Left.LockAmount);
    Object->SetNumberField(TEXT("rightFootLockAmount"), Feet->Right.LockAmount);
    const int32 LockedPlatformId{LeftFootReleaseReason != TEXT("None")
        ? RuntimeCapture.PreviousPlatform.PlatformId
        : RuntimeCapture.LeftFootHit.PlatformId};
    Object->SetNumberField(TEXT("leftFootPlatformId"), Feet->Left.LockAmount > UE_KINDA_SMALL_NUMBER
        ? LockedPlatformId : -1);
    Object->SetNumberField(TEXT("rightFootPlatformId"), Feet->Right.LockAmount > UE_KINDA_SMALL_NUMBER
        ? LockedPlatformId : -1);
    Object->SetStringField(TEXT("leftFootReleaseReason"), LeftFootReleaseReason);
    Object->SetStringField(TEXT("rightFootReleaseReason"), RightFootReleaseReason);
    Object->SetNumberField(TEXT("transitionFrameDelta"), TransitionFrameDelta);
    Object->SetNumberField(TEXT("animationUpdateCount"), LastP4TickEvidence.AnimationUpdateCount);
    Object->SetNumberField(TEXT("animationEvaluationCount"), LastP4TickEvidence.AnimationEvaluationCount);
    Object->SetNumberField(TEXT("animationPostUpdateCount"), LastP4TickEvidence.AnimationPostUpdateCount);
    Object->SetNumberField(TEXT("publicAnimationTickCount"), LastP4TickEvidence.PublicAnimationTickCount);
    Object->SetStringField(TEXT("footHitProvenance"), TEXT("derived_transition"));
    Object->SetStringField(TEXT("releaseReasonProvenance"), TEXT("derived_transition"));
    Object->SetNumberField(TEXT("movementBaseId"), RuntimeCapture.CurrentPlatform.PlatformId);
    Object->SetNumberField(TEXT("movementBaseColliderId"), RuntimeCapture.CurrentPlatform.ColliderId);
    Object->SetObjectField(TEXT("movementBasePosition"), PortVector3Object(
        ToPortPosition(RuntimeCapture.CurrentPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("movementBaseRotation"), QuaternionObject(
        ToPortRotation(RuntimeCapture.CurrentPlatform.Transform.GetRotation())));
    Object->SetNumberField(TEXT("previousMovementBaseId"), RuntimeCapture.PreviousPlatform.PlatformId);
    Object->SetNumberField(TEXT("previousMovementBaseColliderId"), RuntimeCapture.PreviousPlatform.ColliderId);
    Object->SetObjectField(TEXT("previousMovementBasePosition"), PortVector3Object(
        ToPortPosition(RuntimeCapture.PreviousPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("previousMovementBaseRotation"), QuaternionObject(
        ToPortRotation(RuntimeCapture.PreviousPlatform.Transform.GetRotation())));
    Object->SetObjectField(TEXT("actualFloorNormal"), PortVector3Object(RuntimeCapture.FloorNormal));
    Object->SetObjectField(TEXT("actualLeftFootHit"), P4FootHitObject(RuntimeCapture.LeftFootHit));
    Object->SetObjectField(TEXT("actualRightFootHit"), P4FootHitObject(RuntimeCapture.RightFootHit));
    Object->SetObjectField(TEXT("previousLeftLockWorldPosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.LeftWorld)));
    Object->SetObjectField(TEXT("previousRightLockWorldPosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.RightWorld)));
    Object->SetObjectField(TEXT("previousLeftLockBasePosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.LeftBase)));
    Object->SetObjectField(TEXT("previousRightLockBasePosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.RightBase)));
    Object->SetObjectField(TEXT("leftLockWorldPosition"), PortVector3Object(ToPortPosition(Feet->Left.LockLocationWorldSpace)));
    Object->SetObjectField(TEXT("rightLockWorldPosition"), PortVector3Object(ToPortPosition(Feet->Right.LockLocationWorldSpace)));
    Object->SetObjectField(TEXT("leftLockBasePosition"), PortVector3Object(ToPortPosition(FVector{Feet->Left.LockLocationMovementBaseSpace})));
    Object->SetObjectField(TEXT("rightLockBasePosition"), PortVector3Object(ToPortPosition(FVector{Feet->Right.LockLocationMovementBaseSpace})));
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    const TSharedPtr<FJsonObject> Pelvis{P4BoneTransformObject(*Mesh, TEXT("pelvis"))};
    const TSharedPtr<FJsonObject> LeftFoot{P4BoneTransformObject(*Mesh, TEXT("foot_l"))};
    const TSharedPtr<FJsonObject> RightFoot{P4BoneTransformObject(*Mesh, TEXT("foot_r"))};
    const TSharedPtr<FJsonObject> PelvisComponent{P4BoneComponentTransformObject(*Mesh, TEXT("pelvis"))};
    const TSharedPtr<FJsonObject> LeftFootComponent{P4BoneComponentTransformObject(*Mesh, TEXT("foot_l"))};
    const TSharedPtr<FJsonObject> RightFootComponent{P4BoneComponentTransformObject(*Mesh, TEXT("foot_r"))};
    const TSharedPtr<FJsonObject> Head{P4BoneTransformObject(*Mesh, TEXT("head"))};
    const TSharedPtr<FJsonObject> SpineBone{P4BoneTransformObject(*Mesh, TEXT("spine_03"))};
    const TSharedPtr<FJsonObject> UpperBody{P4BoneTransformObject(*Mesh, TEXT("spine_01"))};
    const TSharedPtr<FJsonObject> HeadComponent{P4BoneComponentTransformObject(*Mesh, TEXT("head"))};
    const TSharedPtr<FJsonObject> SpineComponent{P4BoneComponentTransformObject(*Mesh, TEXT("spine_03"))};
    const TSharedPtr<FJsonObject> UpperBodyComponent{P4BoneComponentTransformObject(*Mesh, TEXT("spine_01"))};
    if (!Pelvis.IsValid() || !LeftFoot.IsValid() || !RightFoot.IsValid() ||
        !PelvisComponent.IsValid() || !LeftFootComponent.IsValid() || !RightFootComponent.IsValid() ||
        !Head.IsValid() || !SpineBone.IsValid() || !UpperBody.IsValid() || !HeadComponent.IsValid() ||
        !SpineComponent.IsValid() || !UpperBodyComponent.IsValid()) return nullptr;
    Object->SetObjectField(TEXT("pelvis"), Pelvis.ToSharedRef());
    Object->SetObjectField(TEXT("leftFoot"), LeftFoot.ToSharedRef());
    Object->SetObjectField(TEXT("rightFoot"), RightFoot.ToSharedRef());
    Object->SetObjectField(TEXT("pelvisComponent"), PelvisComponent.ToSharedRef());
    Object->SetObjectField(TEXT("leftFootComponent"), LeftFootComponent.ToSharedRef());
    Object->SetObjectField(TEXT("rightFootComponent"), RightFootComponent.ToSharedRef());
    Object->SetObjectField(TEXT("head"), Head.ToSharedRef());
    Object->SetObjectField(TEXT("spine"), SpineBone.ToSharedRef());
    Object->SetObjectField(TEXT("upperBody"), UpperBody.ToSharedRef());
    Object->SetObjectField(TEXT("headComponent"), HeadComponent.ToSharedRef());
    Object->SetObjectField(TEXT("spineComponent"), SpineComponent.ToSharedRef());
    Object->SetObjectField(TEXT("upperBodyComponent"), UpperBodyComponent.ToSharedRef());
    return Object;
}

TSharedRef<FJsonObject> EmptyP4FootOutput()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("position"), PortVector3Object(FVector::ZeroVector));
    Object->SetObjectField(TEXT("rotation"), QuaternionObject(FQuat::Identity));
    Object->SetNumberField(TEXT("lockAmount"), 0.0);
    Object->SetNumberField(TEXT("platformId"), -1);
    return Object;
}

TSharedRef<FJsonObject> EmptyP4PortExpected()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("aimRelativeYaw"), 0.0);
    Object->SetNumberField(TEXT("aimRelativePitch"), 0.0);
    Object->SetNumberField(TEXT("headWeight"), 0.0);
    Object->SetNumberField(TEXT("spineWeight"), 0.0);
    Object->SetNumberField(TEXT("upperBodyWeight"), 0.0);
    Object->SetNumberField(TEXT("spineResidualYaw"), 0.0);
    Object->SetNumberField(TEXT("turnAnimationId"), -1);
    Object->SetNumberField(TEXT("turnCurveId"), -1);
    Object->SetNumberField(TEXT("turnPhase"), 0.0);
    Object->SetNumberField(TEXT("turnPlayRate"), 0.0);
    Object->SetNumberField(TEXT("turnNominalDegrees"), 0);
    Object->SetNumberField(TEXT("turnDirection"), 0);
    Object->SetNumberField(TEXT("turnActive"), 0);
    Object->SetNumberField(TEXT("turnYawDelta"), 0.0);
    Object->SetNumberField(TEXT("rotateAnimationId"), -1);
    Object->SetNumberField(TEXT("rotateCurveId"), -1);
    Object->SetNumberField(TEXT("rotatePhase"), 0.0);
    Object->SetNumberField(TEXT("rotatePlayRate"), 0.0);
    Object->SetNumberField(TEXT("rotateDirection"), 0);
    Object->SetNumberField(TEXT("rotateActive"), 0);
    Object->SetNumberField(TEXT("rotateYawDelta"), 0.0);
    Object->SetObjectField(TEXT("pelvisOffset"), PortVector3Object(FVector::ZeroVector));
    Object->SetObjectField(TEXT("leftFoot"), EmptyP4FootOutput());
    Object->SetObjectField(TEXT("rightFoot"), EmptyP4FootOutput());
    Object->SetStringField(TEXT("leftReleaseReason"), TEXT("None"));
    Object->SetStringField(TEXT("rightReleaseReason"), TEXT("None"));
    return Object;
}

TArray<FP4CaseDefinition> CreateP4CaseDefinitions()
{
    TArray<FP4CaseDefinition> Cases;
    const auto AddAim{[&Cases](const TCHAR* Id, const TCHAR* Direction, const float Yaw, const float Pitch)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Aim"); Value.Stance = TEXT("Standing");
        Value.Direction = Direction; Value.Phase = TEXT("steady"); Value.RotationMode = TEXT("Aiming");
        Value.ViewYaw = Yaw; Value.AimYaw = Yaw; Value.ViewPitch = Pitch; Value.AimPitch = Pitch;
        Value.SourceAnimationObjectPath = TEXT("/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look");
        Value.SourceCurveNames = {TEXT("Layering_Head")};
        Cases.Add(Value);
    }};
    AddAim(TEXT("aim_center"), TEXT("Center"), 0.0f, 0.0f);
    AddAim(TEXT("aim_up"), TEXT("Up"), 0.0f, PI / 3.0f);
    AddAim(TEXT("aim_down"), TEXT("Down"), 0.0f, -PI / 3.0f);
    AddAim(TEXT("aim_left"), TEXT("Left"), -PI / 3.0f, 0.0f);
    AddAim(TEXT("aim_right"), TEXT("Right"), PI / 3.0f, 0.0f);

    const auto AddTurn{[&Cases](const TCHAR* Stance, const TCHAR* Direction, const int32 Angle)
    {
        FP4CaseDefinition Value;
        Value.CaseId = FString::Printf(TEXT("turn_%s_%s_%d"), *FString{Stance}.ToLower(), *FString{Direction}.ToLower(), Angle);
        Value.Category = TEXT("Turn"); Value.Stance = Stance; Value.Direction = Direction; Value.Phase = TEXT("playing");
        const float Sign{FString{Direction} == TEXT("Left") ? -1.0f : 1.0f};
        Value.ViewYaw = Sign * FMath::DegreesToRadians(Angle == 180 ? 170.0f : 90.0f);
        Value.AimYaw = Value.ViewYaw; Value.PreviousYawCurve = Sign; Value.CurrentYawCurve = Sign;
        const FString StancePrefix{FString{Stance} == TEXT("Crouching") ? TEXT("Crouch_") : TEXT("")};
        const FString AssetName{FString::Printf(TEXT("A_Als_%sTurn_%d_%s"), *StancePrefix, Angle, Direction)};
        Value.SourceAnimationObjectPath = FString::Printf(TEXT("/ALS/ALS/Animations/TurnInPlace/%s.%s"), *AssetName, *AssetName);
        Value.SourceCurveNames = {TEXT("RotationYawSpeed")};
        Cases.Add(Value);
    }};
    for (const TCHAR* Stance : {TEXT("Standing"), TEXT("Crouching")})
    {
        AddTurn(Stance, TEXT("Left"), 90); AddTurn(Stance, TEXT("Right"), 90);
        AddTurn(Stance, TEXT("Left"), 180); AddTurn(Stance, TEXT("Right"), 180);
    }

    const auto AddRotate{[&Cases](const TCHAR* Stance, const TCHAR* Direction)
    {
        FP4CaseDefinition Value;
        Value.CaseId = FString::Printf(TEXT("rotate_%s_%s"), *FString{Stance}.ToLower(), *FString{Direction}.ToLower());
        Value.Category = TEXT("Rotate"); Value.Stance = Stance; Value.Direction = Direction; Value.Phase = TEXT("playing");
        Value.RotationMode = TEXT("Aiming");
        const float Sign{FString{Direction} == TEXT("Left") ? -1.0f : 1.0f};
        Value.ViewYaw = Sign * FMath::DegreesToRadians(60.0f); Value.AimYaw = Value.ViewYaw;
        Value.PreviousYawCurve = Sign; Value.CurrentYawCurve = Sign;
        const FString StancePrefix{FString{Stance} == TEXT("Crouching") ? TEXT("Crouch_") : TEXT("")};
        const FString AssetName{FString::Printf(TEXT("A_Als_%sRotate_90_%s"), *StancePrefix, Direction)};
        Value.SourceAnimationObjectPath = FString::Printf(TEXT("/ALS/ALS/Animations/RotateInPlace/%s.%s"), *AssetName, *AssetName);
        Value.SourceCurveNames = {TEXT("RotationYawSpeed")};
        Cases.Add(Value);
    }};
    for (const TCHAR* Stance : {TEXT("Standing"), TEXT("Crouching")})
    {
        AddRotate(Stance, TEXT("Left")); AddRotate(Stance, TEXT("Right"));
    }

    const auto AddFeet{[&Cases](const TCHAR* Id, const FVector& Normal, const double LeftHeight, const double RightHeight)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Feet"); Value.Stance = TEXT("Standing");
        Value.Direction = TEXT("None"); Value.Phase = TEXT("steady"); Value.bValidHits = 1;
        Value.PlatformColliderId = 100;
        Value.FloorNormal = Normal.GetSafeNormal(); Value.LeftHitPosition.Y = LeftHeight; Value.RightHitPosition.Y = RightHeight;
        Value.LeftIkWeight = 1.0f; Value.RightIkWeight = 1.0f;
        Cases.Add(Value);
    }};
    AddFeet(TEXT("feet_flat"), FVector{0.0, 1.0, 0.0}, 0.0, 0.0);
    AddFeet(TEXT("feet_slope"), FVector{0.0, 0.9396926208, 0.3420201433}, 0.0, 0.0);
    AddFeet(TEXT("feet_stairs"), FVector{0.0, 1.0, 0.0}, 0.2, 0.0);

    const auto AddPlatform{[&Cases](const TCHAR* Id, const TCHAR* Phase)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Platform"); Value.Stance = TEXT("Standing");
        Value.Direction = TEXT("None"); Value.Phase = Phase; Value.bValidHits = 1;
        Value.PlatformId = 1; Value.PlatformColliderId = 10;
        Value.PreviousPlatformId = 1; Value.PreviousPlatformColliderId = 10; Value.bInitialLocked = 1;
        Value.LeftOrigin = FVector{-0.2, 0.13, 0.0}; Value.RightOrigin = FVector{0.2, 0.13, 0.0};
        Value.LeftIkWeight = 1.0f; Value.RightIkWeight = 1.0f;
        Value.LeftLockCurve = 1.0f; Value.RightLockCurve = 1.0f;
        Cases.Add(Value);
    }};
    AddPlatform(TEXT("platform_translate"), TEXT("hold"));
    Cases.Last().PlatformPosition = FVector{0.1, 0.0, 0.0};
    Cases.Last().LeftHitPosition.X += 0.1; Cases.Last().RightHitPosition.X += 0.1;
    AddPlatform(TEXT("platform_rotate"), TEXT("hold"));
    Cases.Last().PlatformRotation = FQuat{FVector{0.0, 1.0, 0.0}, FMath::DegreesToRadians(10.0f)};
    AddPlatform(TEXT("platform_base_change"), TEXT("release"));
    Cases.Last().PlatformId = 2; Cases.Last().PlatformColliderId = 20;
    AddPlatform(TEXT("platform_teleport"), TEXT("release"));
    Cases.Last().PlatformPosition = FVector{2.0, 0.0, 0.0};
    AddPlatform(TEXT("platform_release"), TEXT("release"));
    Cases.Last().LeftLockCurve = 0.0f; Cases.Last().RightLockCurve = 0.0f;
    return Cases;
}

const UAlsTurnInPlaceSettings* SelectP4TurnSettings(const UAlsAnimationInstanceSettings& Settings,
                                                    const FP4CaseDefinition& Definition)
{
    const bool bLeft{Definition.Direction == TEXT("Left")};
    const bool bTurn180{FMath::Abs(Definition.ViewYaw) > FMath::DegreesToRadians(130.0f)};
    if (Definition.Stance == TEXT("Crouching"))
    {
        return bTurn180
            ? (bLeft ? Settings.TurnInPlace.CrouchingTurn180Left : Settings.TurnInPlace.CrouchingTurn180Right)
            : (bLeft ? Settings.TurnInPlace.CrouchingTurn90Left : Settings.TurnInPlace.CrouchingTurn90Right);
    }
    return bTurn180
        ? (bLeft ? Settings.TurnInPlace.StandingTurn180Left : Settings.TurnInPlace.StandingTurn180Right)
        : (bLeft ? Settings.TurnInPlace.StandingTurn90Left : Settings.TurnInPlace.StandingTurn90Right);
}

bool GetP4SelectionIds(const FString& RuntimeSourceAnimationObjectPath, const FString& Category,
                       int32& TurnAnimationId, int32& TurnCurveId, int32& TurnNominalDegrees,
                       int32& RotateAnimationId, int32& RotateCurveId)
{
    TurnAnimationId = -1;
    TurnCurveId = -1;
    TurnNominalDegrees = 0;
    RotateAnimationId = -1;
    RotateCurveId = -1;
    struct FLockedSelection
    {
        const TCHAR* Path;
        const TCHAR* Category;
        int32 AnimationId;
        int32 NominalDegrees;
    };
    static constexpr FLockedSelection Selections[]{
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Left.A_Als_Turn_90_Left"), TEXT("Turn"), 101, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Right.A_Als_Turn_90_Right"), TEXT("Turn"), 100, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_180_Left.A_Als_Turn_180_Left"), TEXT("Turn"), 103, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_180_Right.A_Als_Turn_180_Right"), TEXT("Turn"), 102, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_90_Left.A_Als_Crouch_Turn_90_Left"), TEXT("Turn"), 105, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_90_Right.A_Als_Crouch_Turn_90_Right"), TEXT("Turn"), 104, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_180_Left.A_Als_Crouch_Turn_180_Left"), TEXT("Turn"), 107, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_180_Right.A_Als_Crouch_Turn_180_Right"), TEXT("Turn"), 106, 180},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Left.A_Als_Rotate_90_Left"), TEXT("Rotate"), 109, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Right.A_Als_Rotate_90_Right"), TEXT("Rotate"), 108, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Crouch_Rotate_90_Left.A_Als_Crouch_Rotate_90_Left"), TEXT("Rotate"), 111, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Crouch_Rotate_90_Right.A_Als_Crouch_Rotate_90_Right"), TEXT("Rotate"), 110, 90},
    };
    for (const FLockedSelection& Selection : Selections)
    {
        if (Category == Selection.Category && RuntimeSourceAnimationObjectPath == Selection.Path)
        {
            if (Category == TEXT("Turn"))
            {
                TurnAnimationId = Selection.AnimationId;
                TurnCurveId = Selection.AnimationId + 100;
                TurnNominalDegrees = Selection.NominalDegrees;
            }
            else
            {
                RotateAnimationId = Selection.AnimationId;
                RotateCurveId = Selection.AnimationId + 100;
            }
            return true;
        }
    }
    return Category != TEXT("Turn") && Category != TEXT("Rotate");
}

int32 SelectUniqueP4RuntimeCandidateIndex(const int32 CandidateCount)
{
    return CandidateCount == 1 ? 0 : INDEX_NONE;
}

bool ObserveP4RuntimeAssetPlayer(USkeletalMeshComponent& Mesh, UAnimationAsset& ExpectedAsset,
                                 const FName CurveName, FP4RuntimePlayerObservation& Observation)
{
    struct FCandidate
    {
        const FAnimNode_AssetPlayerRelevancyBase* Player{nullptr};
        const FStructProperty* Property{nullptr};
        UAnimInstance* Instance{nullptr};
        int32 Ordinal{-1};
    };
    TArray<FCandidate, TInlineAllocator<2>> Candidates;
    TArray<UAnimInstance*, TInlineAllocator<8>> Instances;
    if (UAnimInstance* Main{Mesh.GetAnimInstance()}; IsValid(Main)) Instances.Add(Main);
    const USkeletalMeshComponent& ConstMesh{Mesh};
    for (UAnimInstance* Linked : ConstMesh.GetLinkedAnimInstances())
    {
        if (IsValid(Linked)) Instances.AddUnique(Linked);
    }
    for (UAnimInstance* Instance : Instances)
    {
        const IAnimClassInterface* Interface{IAnimClassInterface::GetFromClass(Instance->GetClass())};
        if (Interface == nullptr) continue;
        int32 Ordinal{0};
        for (const FStructProperty* NodeProperty : Interface->GetAnimNodeProperties())
        {
            if (NodeProperty->Struct->IsChildOf(FAnimNode_AssetPlayerRelevancyBase::StaticStruct()))
            {
                const FAnimNode_AssetPlayerRelevancyBase* Player{
                    NodeProperty->ContainerPtrToValuePtr<FAnimNode_AssetPlayerRelevancyBase>(Instance)};
                if (Player != nullptr && Player->GetAnimAsset() == &ExpectedAsset &&
                    Player->GetCachedBlendWeight() > UE_KINDA_SMALL_NUMBER)
                {
                    Candidates.Add({Player, NodeProperty, Instance, Ordinal});
                }
            }
            ++Ordinal;
        }
    }
    const int32 SelectedCandidateIndex{SelectUniqueP4RuntimeCandidateIndex(Candidates.Num())};
    if (SelectedCandidateIndex == INDEX_NONE)
    {
        UE_LOG(LogTemp, Error,
            TEXT("P4 runtime asset player selection is not unique instance=%s asset=%s candidates=%d"),
            TEXT("<all-active-instances>"), *ExpectedAsset.GetPathName(), Candidates.Num());
        return false;
    }

    const FCandidate& Candidate{Candidates[SelectedCandidateIndex]};
    const FAnimNode_AssetPlayerRelevancyBase* BestPlayer{Candidate.Player};

    const FDeltaTimeRecord* DeltaRecord{BestPlayer->GetDeltaTimeRecord()};
    if (DeltaRecord == nullptr || !DeltaRecord->IsPreviousValid() || ExpectedAsset.GetPlayLength() <= UE_SMALL_NUMBER)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 runtime asset player time unavailable asset=%s"), *ExpectedAsset.GetPathName());
        return false;
    }
    Observation.SourceAnimationObjectPath = ExpectedAsset.GetPathName();
    Observation.InstanceClassPath = Candidate.Instance->GetClass()->GetPathName();
    Observation.NodePropertyName = Candidate.Property->GetName();
    Observation.NodePropertyOrdinal = Candidate.Ordinal;
    Observation.BlendWeight = BestPlayer->GetCachedBlendWeight();
    Observation.CurrentTimeSeconds = BestPlayer->GetAccumulatedTime();
    Observation.PreviousTimeSeconds = DeltaRecord->GetPrevious();
    const float Duration{ExpectedAsset.GetPlayLength()};
    Observation.CurrentPhase = Observation.CurrentTimeSeconds / Duration;
    Observation.PreviousPhase = Observation.PreviousTimeSeconds / Duration;
    float PhaseDelta{Observation.CurrentPhase - Observation.PreviousPhase};
    if (PhaseDelta < 0.0f && BestPlayer->IsLooping()) PhaseDelta += 1.0f;
    Observation.PhasePlayRate = PhaseDelta / static_cast<float>(FixedDeltaSeconds);
    if (!CurveName.IsNone())
    {
        const UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(&ExpectedAsset)};
        if (!IsValid(Sequence)) return false;
        Observation.PreviousYawCurve = Sequence->EvaluateCurveData(
            CurveName, FAnimExtractContext{Observation.PreviousTimeSeconds});
        Observation.CurrentYawCurve = Sequence->EvaluateCurveData(
            CurveName, FAnimExtractContext{Observation.CurrentTimeSeconds});
    }
    return true;
}

bool RunP4RuntimeSelectionSelfTest()
{
    // Two observations of the same asset at different phases are ambiguous unless node identity narrows them first.
    return SelectUniqueP4RuntimeCandidateIndex(1) == 0 &&
        SelectUniqueP4RuntimeCandidateIndex(0) == INDEX_NONE &&
        SelectUniqueP4RuntimeCandidateIndex(2) == INDEX_NONE;
}

FP4TickEvidence TickP4World(UWorld& World, AAlsTraceCharacter& Character, const FRotator* ForcedView = nullptr)
{
    if (ForcedView != nullptr)
    {
        Character.SetTraceViewRotation(*ForcedView);
    }
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const int16 PreviousUpdateCounter{Animation->GetUpdateCounter().Get()};
    UAlsTraceSkeletalMeshComponent* Mesh{Character.GetTraceMesh()};
    Mesh->ResetTracePipelineCounts();
    CommandletHelpers::TickEngine(&World, FixedDeltaSeconds);
    World.Tick(LEVELTICK_All, FixedDeltaSeconds);
    const int16 CurrentUpdateCounter{Animation->GetUpdateCounter().Get()};
    const int32 UpdateCount{static_cast<int32>(CurrentUpdateCounter) - PreviousUpdateCounter};
    LastP4TickEvidence = FP4TickEvidence{
        UpdateCount,
        Mesh->GetTraceEvaluationCount(),
        Mesh->GetTracePostUpdateCount(),
        Mesh->GetTracePublicTickCount(),
    };
    return LastP4TickEvidence;
}

UBoxComponent* SpawnP4Surface(UWorld& World, TArray<AActor*>& Actors, const TCHAR* Name,
                              const FVector& Location, const FVector& Extent,
                              const FRotator& Rotation = FRotator::ZeroRotator,
                              const bool bVisibilityQueryOnly = false)
{
    AActor* Actor{World.SpawnActor<AActor>()};
    if (!IsValid(Actor)) return nullptr;
    UBoxComponent* Collision{NewObject<UBoxComponent>(Actor, Name)};
    Actor->SetRootComponent(Collision);
    Collision->SetBoxExtent(Extent);
    if (bVisibilityQueryOnly)
    {
        Collision->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
        Collision->SetCollisionResponseToAllChannels(ECR_Ignore);
        Collision->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
    }
    else
    {
        Collision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
    }
    Collision->SetMobility(EComponentMobility::Movable);
    Collision->RegisterComponent();
    Actor->SetActorLocationAndRotation(Location, Rotation);
    Actors.Add(Actor);
    return Collision;
}

TPair<int32, int32> CaptureP4WorldCleanupBaseline(UWorld& World)
{
    int32 ActorCount{0};
    int32 CollisionCount{0};
    for (TActorIterator<AActor> Iterator{&World}; Iterator; ++Iterator)
    {
        AActor* Actor{*Iterator};
        if (!IsValid(Actor) || Actor->IsActorBeingDestroyed()) continue;
        ++ActorCount;
        TArray<UPrimitiveComponent*> Components;
        Actor->GetComponents(Components);
        for (const UPrimitiveComponent* Component : Components)
        {
            if (IsValid(Component) && Component->GetCollisionEnabled() != ECollisionEnabled::NoCollision)
            {
                ++CollisionCount;
            }
        }
    }
    return {ActorCount, CollisionCount};
}

bool RunP4CleanupSelfTest(UWorld& World)
{
    const TPair<int32, int32> Baseline{CaptureP4WorldCleanupBaseline(World)};
    for (int32 FailureStage{0}; FailureStage < 3; ++FailureStage)
    {
        {
            FP4CaseRuntimeOwner Owner{World};
            if (SpawnP4Surface(World, Owner.SceneActors, TEXT("P4CleanupSelfTestSurface"),
                FVector{0.0, 0.0, -500.0}, FVector{10.0, 10.0, 10.0}) == nullptr)
            {
                return false;
            }
            if (FailureStage > 0)
            {
                FActorSpawnParameters Parameters;
                Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
                Owner.Character = World.SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
                    FVector{0.0, 0.0, -400.0}, FRotator::ZeroRotator, Parameters);
                Owner.Controller = World.SpawnActor<AAlsTraceController>();
                if (!IsValid(Owner.Character) || !IsValid(Owner.Controller)) return false;
                if (!Owner.Character->HasActorBegunPlay()) Owner.Character->DispatchBeginPlay();
                Owner.Controller->Possess(Owner.Character);
                if (FailureStage > 1)
                {
                    Owner.Character->GetMesh()->SetAnimInstanceClass(nullptr);
                    if (Owner.Character->GetMesh()->GetAnimInstance() != nullptr) return false;
                }
            }
        }
        if (CaptureP4WorldCleanupBaseline(World) != Baseline)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 cleanup self-test leaked world state stage=%d"), FailureStage);
            return false;
        }
    }
    UE_LOG(LogTemp, Display, TEXT("P4_CLEANUP_SELF_TEST_OK stages=3"));
    return true;
}

bool TickUntilP4FootLock(UWorld& World, AAlsTraceCharacter& Character, const int32 MaximumFrames)
{
    for (int32 Frame{0}; Frame < MaximumFrames; ++Frame)
    {
        TickP4World(World, Character);
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(
            Character.GetTraceAnimationInstance(), TEXT("FeetState"))};
        if (Feet != nullptr && Feet->bValid &&
            FMath::Max(Feet->Left.LockAmount, Feet->Right.LockAmount) >= 0.99f)
        {
            return true;
        }
    }
    return false;
}

TSharedPtr<FJsonObject> GenerateP4Case(UWorld* World, const FP4CaseDefinition& Definition,
                                      UClass& AimOverlayClass)
{
    if (LoadObject<UAnimationAsset>(nullptr, *Definition.SourceAnimationObjectPath) == nullptr)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 source animation is unavailable case=%s path=%s"),
            *Definition.CaseId, *Definition.SourceAnimationObjectPath);
        return nullptr;
    }

    FP4CaseRuntimeOwner CaseOwner{*World};
    TArray<AActor*>& SceneActors{CaseOwner.SceneActors};
    UBoxComponent* PrimaryMovementBase{nullptr};
    UBoxComponent* SecondaryMovementBase{nullptr};
    FVector CharacterLocation{0.0, 0.0, 92.0};
    if (Definition.Category == TEXT("Feet") && Definition.CaseId == TEXT("feet_slope"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4Slope"),
            FVector{0.0, 0.0, 5.0}, FVector{100.0, 100.0, 5.0}, FRotator{0.0, 0.0, 20.0});
        CharacterLocation.Z = 112.0;
    }
    else if (Definition.Category == TEXT("Feet") && Definition.CaseId == TEXT("feet_stairs"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4StairLeft"),
            FVector{0.0, -22.0, 10.0}, FVector{60.0, 18.0, 10.0});
        SecondaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4StairRight"),
            FVector{0.0, 22.0, 2.5}, FVector{60.0, 18.0, 2.5});
        CharacterLocation.Z = 112.0;
    }
    else if (Definition.Category == TEXT("Platform"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4PlatformPrimary"),
            FVector{0.0, 0.0, 20.0}, FVector{250.0, 250.0, 20.0});
        SecondaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4PlatformSecondary"),
            FVector{0.0, 0.0, -40.0}, FVector{250.0, 250.0, 20.0});
        CharacterLocation.Z = 132.0;
    }

    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AAlsTraceCharacter* Character{World->SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
        CharacterLocation, FRotator::ZeroRotator, SpawnParameters)};
    AAlsTraceController* Controller{World->SpawnActor<AAlsTraceController>()};
    CaseOwner.Character = Character;
    CaseOwner.Controller = Controller;
    if (IsValid(Character) && !Character->HasActorBegunPlay()) Character->DispatchBeginPlay();
    if (IsValid(Controller) && IsValid(Character)) Controller->Possess(Character);
    if (!IsValid(Character) || !IsValid(Controller) || !IsValid(Character->GetTraceAnimationInstance()))
    {
        return nullptr;
    }
    if (Definition.Category == TEXT("Platform") && PrimaryMovementBase != nullptr)
    {
        Character->SetBase(PrimaryMovementBase);
    }
    const FGameplayTag CaseStance{Definition.Stance == TEXT("Crouching")
        ? AlsStanceTags::Crouching
        : AlsStanceTags::Standing};
    Character->ApplyTraceDesiredState(AlsRotationModeTags::ViewDirection, false,
        CaseStance, AlsOverlayModeTags::Default);
    Controller->SetControlRotation(FRotator::ZeroRotator);
    Character->SetTraceViewRotation(FRotator::ZeroRotator);
    for (int32 Warmup{0}; Warmup < 20; ++Warmup) TickP4World(*World, *Character);

    if (Definition.Category == TEXT("Feet"))
    {
        const auto TraceFoot{[&](const FVector& PortOrigin, UPrimitiveComponent* ExpectedComponent)
        {
            const FVector Center{-PortOrigin.Z * 100.0, PortOrigin.X * 100.0, PortOrigin.Y * 100.0};
            FHitResult Hit;
            FCollisionQueryParams QueryParams{SCENE_QUERY_STAT(P4FootSurface), false, Character};
            const bool bHit{World->LineTraceSingleByChannel(Hit, Center + FVector{0.0, 0.0, 50.0},
                Center - FVector{0.0, 0.0, 150.0}, ECC_Visibility, QueryParams)};
            return bHit && (ExpectedComponent == nullptr || Hit.GetComponent() == ExpectedComponent);
        }};
        const bool bLeftHit{TraceFoot(Definition.LeftOrigin, PrimaryMovementBase)};
        const bool bRightHit{TraceFoot(Definition.RightOrigin,
            Definition.CaseId == TEXT("feet_stairs") ? SecondaryMovementBase : PrimaryMovementBase)};
        if (!bLeftHit || !bRightHit)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 foot query surface mismatch case=%s left=%d right=%d"),
                *Definition.CaseId, bLeftHit, bRightHit);
            return nullptr;
        }
    }

    const bool bAiming{Definition.RotationMode == TEXT("Aiming")};
    if (bAiming)
    {
        Character->GetMesh()->LinkAnimClassLayers(&AimOverlayClass);
    }
    Character->ApplyTraceDesiredState(bAiming ? AlsRotationModeTags::Aiming : AlsRotationModeTags::ViewDirection,
        bAiming, CaseStance, bAiming ? AlsOverlayModeTags::Rifle : AlsOverlayModeTags::Default);
    const FRotator CaseView{FMath::RadiansToDegrees(Definition.ViewPitch),
        FMath::RadiansToDegrees(bAiming ? Definition.AimYaw : Definition.ViewYaw), 0.0};

    float PreviousLeftFootLockAmount{0.0f};
    float PreviousRightFootLockAmount{0.0f};
    FP4FootLockSnapshot PreviousFootLocks;
    FP4PlatformSnapshot PreviousPlatformSnapshot;
    UPrimitiveComponent* const TracePlatformPrimary{
        Definition.Category == TEXT("Platform") ? PrimaryMovementBase : nullptr};
    UPrimitiveComponent* const TracePlatformSecondary{
        Definition.Category == TEXT("Platform") ? SecondaryMovementBase : nullptr};
    float TurnPhase{0.0f};
    float RotatePhase{0.0f};
    float RotateAccumulatedYaw{0.0f};
    FP4RuntimePlayerObservation RuntimePlayer;
    int32 TransitionFrameDelta{0};
    bool bSourceSelectionVerified{false};
    FString LeftFootReleaseReason{TEXT("None")};
    FString RightFootReleaseReason{TEXT("None")};
    const float InitialActorYaw{static_cast<float>(Character->GetActorRotation().Yaw)};
    FString SourceAnimationObjectPath{Definition.SourceAnimationObjectPath};
    const UAlsTurnInPlaceSettings* SelectedTurnSettings{nullptr};
    bool bSampleReady{true};
    if (Definition.Category == TEXT("Turn"))
    {
        SelectedTurnSettings = SelectP4TurnSettings(*Character->GetTraceAnimationSettings(), Definition);
        if (!IsValid(SelectedTurnSettings) || !IsValid(SelectedTurnSettings->Sequence))
        {
            UE_LOG(LogTemp, Error, TEXT("P4 turn settings sequence is unavailable case=%s"), *Definition.CaseId);
            bSampleReady = false;
        }
        else
        {
            SourceAnimationObjectPath = SelectedTurnSettings->Sequence->GetPathName();
            if (SourceAnimationObjectPath != Definition.SourceAnimationObjectPath)
            {
                UE_LOG(LogTemp, Error, TEXT("P4 turn settings source mismatch case=%s expected=%s actual=%s"),
                    *Definition.CaseId, *Definition.SourceAnimationObjectPath, *SourceAnimationObjectPath);
                bSampleReady = false;
            }
        }
    }
    if (bAiming)
    {
        bSampleReady = false;
        const FRotator NeutralView{FRotator::ZeroRotator};
        Controller->SetControlRotation(NeutralView);
        Character->SetTraceViewRotation(NeutralView);
        for (int32 Frame{0}; Frame < 60; ++Frame)
        {
            TickP4World(*World, *Character, &NeutralView);
            const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(
                Character->GetTraceAnimationInstance(), TEXT("SpineState"))};
            const float PoseAiming{Character->GetTraceAnimationInstance()->GetCurveValue(
                UAlsConstants::PoseAimingCurveName())};
            if (Character->GetRotationMode() == AlsRotationModeTags::Aiming && Spine != nullptr &&
                Spine->bSpineRotationAllowed && Spine->SpineAmount >= 0.75f && PoseAiming >= 0.75f)
            {
                bSampleReady = true;
                break;
            }
        }
    }

    Controller->SetControlRotation(CaseView);
    Character->SetTraceViewRotation(CaseView);
    if (Definition.Category == TEXT("Turn"))
    {
        const bool bTurnSettingsReady{bSampleReady};
        bSampleReady = false;
        for (int32 Frame{0}; bTurnSettingsReady && Frame < 120; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstanceMutable()};
            UAnimMontage* Montage{Animation->GetCurrentActiveMontage()};
            if (IsValid(Montage) && Montage->SlotAnimTracks.Num() > 0 &&
                Montage->SlotAnimTracks[0].AnimTrack.AnimSegments.Num() > 0)
            {
                const UAnimSequenceBase* MontageSequence{
                    Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0].GetAnimReference()};
                if (MontageSequence != SelectedTurnSettings->Sequence)
                {
                    UE_LOG(LogTemp, Error,
                        TEXT("P4 active turn montage source mismatch case=%s expected=%s actual=%s"),
                        *Definition.CaseId, *SourceAnimationObjectPath,
                        IsValid(MontageSequence) ? *MontageSequence->GetPathName() : TEXT("<null>"));
                    break;
                }
                TurnPhase = Montage->GetPlayLength() > UE_SMALL_NUMBER
                    ? Animation->Montage_GetPosition(Montage) / Montage->GetPlayLength()
                    : 0.0f;
                const float RotationYawSpeed{
                    Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName())};
                if (TurnPhase >= 0.05f && TurnPhase <= 0.8f && FMath::Abs(RotationYawSpeed) > 1.0e-4f)
                {
                    const FAlsTurnInPlaceState* TurnState{GetAnimationState<FAlsTurnInPlaceState>(
                        Animation, TEXT("TurnInPlaceState"))};
                    FAnimMontageInstance* MontageInstance{Animation->GetActiveInstanceForMontage(Montage)};
                    const FAnimSegment& Segment{Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0]};
                    float SequenceTime{0.0f};
                    float PreviousTime{0.0f};
                    UAnimSequenceBase* CurrentSequence{MontageInstance != nullptr
                        ? Segment.GetAnimationData(MontageInstance->GetPosition(), SequenceTime) : nullptr};
                    UAnimSequenceBase* PreviousSequence{MontageInstance != nullptr
                        ? Segment.GetAnimationData(MontageInstance->GetPreviousPosition(), PreviousTime) : nullptr};
                    const float Duration{SelectedTurnSettings->Sequence->GetPlayLength()};
                    if (CurrentSequence != SelectedTurnSettings->Sequence || PreviousSequence != CurrentSequence ||
                        Duration <= UE_SMALL_NUMBER)
                    {
                        UE_LOG(LogTemp, Error, TEXT("P4 turn runtime source time unavailable case=%s"), *Definition.CaseId);
                        break;
                    }
                    RuntimePlayer.SourceAnimationObjectPath = CurrentSequence->GetPathName();
                    RuntimePlayer.InstanceClassPath = Animation->GetClass()->GetPathName();
                    RuntimePlayer.NodePropertyName = TEXT("ActiveTurnMontageSlot0Segment0");
                    RuntimePlayer.NodePropertyOrdinal = 0;
                    RuntimePlayer.BlendWeight = MontageInstance->GetWeight();
                    RuntimePlayer.CurrentTimeSeconds = SequenceTime;
                    RuntimePlayer.PreviousTimeSeconds = PreviousTime;
                    RuntimePlayer.CurrentPhase = SequenceTime / Duration;
                    RuntimePlayer.PreviousPhase = PreviousTime / Duration;
                    RuntimePlayer.PhasePlayRate = (RuntimePlayer.CurrentPhase - RuntimePlayer.PreviousPhase) /
                        static_cast<float>(FixedDeltaSeconds);
                    RuntimePlayer.PreviousYawCurve = CurrentSequence->EvaluateCurveData(
                        UAlsConstants::RotationYawSpeedCurveName(), FAnimExtractContext{PreviousTime});
                    RuntimePlayer.CurrentYawCurve = CurrentSequence->EvaluateCurveData(
                        UAlsConstants::RotationYawSpeedCurveName(), FAnimExtractContext{SequenceTime});
                    TurnPhase = RuntimePlayer.CurrentPhase;
                    bSourceSelectionVerified = RuntimePlayer.SourceAnimationObjectPath == SourceAnimationObjectPath &&
                        FMath::Abs(RuntimePlayer.CurrentYawCurve) > 1.0e-4f && TurnState != nullptr;
                    bSampleReady = bSourceSelectionVerified;
                    break;
                }
            }
        }
    }
    else if (Definition.Category == TEXT("Aim"))
    {
        const bool bAimingReady{bSampleReady};
        bSampleReady = false;
        for (int32 Frame{0}; bAimingReady && Frame < 10; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(
                Character->GetTraceAnimationInstance(), TEXT("SpineState"))};
            if (Spine != nullptr && Spine->bSpineRotationAllowed && Spine->SpineAmount >= 0.75f)
            {
                bSampleReady = (Definition.Direction != TEXT("Left") && Definition.Direction != TEXT("Right")) ||
                    FMath::Abs(Spine->FinalYawAngle) >= 1.0f;
                if (bSampleReady)
                {
                    UAnimationAsset* AimAsset{LoadObject<UAnimationAsset>(nullptr, *Definition.SourceAnimationObjectPath)};
                    bSourceSelectionVerified = IsValid(AimAsset) &&
                        ObserveP4RuntimeAssetPlayer(*Character->GetMesh(), *AimAsset, NAME_None, RuntimePlayer) &&
                        Character->GetRotationMode() == AlsRotationModeTags::Aiming &&
                        Character->GetMesh()->GetLinkedAnimLayerInstanceByClass(&AimOverlayClass) != nullptr;
                    bSampleReady = bSourceSelectionVerified;
                    break;
                }
            }
        }
    }
    else if (Definition.Category == TEXT("Rotate"))
    {
        const bool bAimingReady{bSampleReady};
        bSampleReady = false;
        UAnimSequenceBase* RotateSequence{LoadObject<UAnimSequenceBase>(nullptr, *Definition.SourceAnimationObjectPath)};
        for (int32 Frame{0}; bAimingReady && IsValid(RotateSequence) && Frame < 60; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(
                Character->GetTraceAnimationInstance(), TEXT("RotateInPlaceState"))};
            const bool bExpectedDirection{Rotate != nullptr &&
                (Definition.Direction == TEXT("Left") ? Rotate->bRotatingLeft : Rotate->bRotatingRight)};
            const float RotationYawSpeed{Character->GetTraceAnimationInstance()->GetCurveValue(
                UAlsConstants::RotationYawSpeedCurveName())};
            // ALS applies this curve as degrees per second to actor yaw. Convert the public runtime
            // sample to Godot radians while preserving every sampled frame in the trace window.
            RotateAccumulatedYaw -= FMath::DegreesToRadians(
                RotationYawSpeed * static_cast<float>(FixedDeltaSeconds));
            if (bExpectedDirection && FMath::Abs(RotationYawSpeed) > 1.0e-4f)
            {
                bSourceSelectionVerified = ObserveP4RuntimeAssetPlayer(*Character->GetMesh(), *RotateSequence,
                    UAlsConstants::RotationYawSpeedCurveName(), RuntimePlayer) &&
                    RuntimePlayer.SourceAnimationObjectPath == Definition.SourceAnimationObjectPath &&
                    FMath::Abs(RuntimePlayer.CurrentYawCurve) > 1.0e-4f;
                RotatePhase = RuntimePlayer.CurrentPhase;
                bSampleReady = bSourceSelectionVerified;
                if (bSampleReady) break;
            }
        }
    }
    else
    {
        bSampleReady = TickUntilP4FootLock(*World, *Character, 180);
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(
            Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
        if (Feet != nullptr)
        {
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousLeftFootLockAmount = PreviousFootLocks.LeftAmount;
            PreviousRightFootLockAmount = PreviousFootLocks.RightAmount;
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(),
                TracePlatformPrimary, TracePlatformSecondary);
        }
    }

    if (bSampleReady && Definition.Category == TEXT("Platform") && PrimaryMovementBase != nullptr)
    {
        TransitionFrameDelta = 1;
        AActor* PrimaryActor{PrimaryMovementBase->GetOwner()};
        if (Definition.CaseId == TEXT("platform_translate"))
        {
            for (int32 Frame{0}; Frame < 9; ++Frame)
            {
                PrimaryActor->AddActorWorldOffset(FVector{0.0, 1.0, 0.0});
                TickP4World(*World, *Character);
            }
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldOffset(FVector{0.0, 1.0, 0.0});
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_rotate"))
        {
            for (int32 Frame{0}; Frame < 9; ++Frame)
            {
                PrimaryActor->AddActorWorldRotation(FRotator{0.0, -1.0, 0.0});
                TickP4World(*World, *Character);
            }
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldRotation(FRotator{0.0, -1.0, 0.0});
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_base_change") && SecondaryMovementBase != nullptr)
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            SecondaryMovementBase->GetOwner()->SetActorTransform(PrimaryActor->GetActorTransform());
            PrimaryMovementBase->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            Character->SetBase(SecondaryMovementBase);
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_teleport"))
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldOffset(FVector{0.0, 200.0, 0.0}, false, nullptr, ETeleportType::TeleportPhysics);
            Character->GetTraceAnimationInstanceMutable()->MarkTeleported();
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_release"))
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            if (Feet == nullptr || Feet->Left.LockAmount <= 1.0e-4f || Feet->Right.LockAmount <= 1.0e-4f)
            {
                bSampleReady = false;
            }
            else
            {
                PreviousFootLocks = CaptureP4FootLocks(*Feet);
                PreviousLeftFootLockAmount = PreviousFootLocks.LeftAmount;
                PreviousRightFootLockAmount = PreviousFootLocks.RightAmount;
                PreviousPlatformSnapshot = CaptureP4Platform(
                    Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
                Character->SetBase(static_cast<UPrimitiveComponent*>(nullptr));
                PrimaryActor->Destroy();
                TickP4World(*World, *Character);
                const FAlsFeetState* ReleasedFeet{GetAnimationState<FAlsFeetState>(
                    Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
                bSampleReady = ReleasedFeet != nullptr && PreviousPlatformSnapshot.PlatformId >= 0 &&
                    PreviousPlatformSnapshot.ColliderId >= 0 &&
                    CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary).PlatformId < 0;
                LeftFootReleaseReason = TEXT("PlatformRemoved");
                RightFootReleaseReason = TEXT("PlatformRemoved");
            }
        }
    }
    else if (bSampleReady && Definition.Category == TEXT("Feet"))
    {
        TickP4World(*World, *Character);
    }

    if (!bSampleReady)
    {
        const UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstance()};
        const FAlsViewAnimationState* View{GetAnimationState<FAlsViewAnimationState>(Animation, TEXT("ViewState"))};
        const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(Animation, TEXT("SpineState"))};
        const FAlsTurnInPlaceState* Turn{GetAnimationState<FAlsTurnInPlaceState>(Animation, TEXT("TurnInPlaceState"))};
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
        const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(
            Animation, TEXT("RotateInPlaceState"))};
        UE_LOG(LogTemp, Error,
            TEXT("P4 native sample did not reach required ALS state case=%s viewYaw=%.3f viewPitch=%.3f characterViewYaw=%.3f actorYaw=%.3f aiming=%d poseAiming=%.3f spineAllowed=%d spineAmount=%.3f spineYaw=%.3f rotateL=%d rotateR=%d rotationYawSpeed=%.6f turnDelay=%.3f turnRate=%.3f montage=%d feetValid=%d lockL=%.3f lockR=%.3f curveIkL=%.3f curveIkR=%.3f curveLockL=%.3f curveLockR=%.3f"),
            *Definition.CaseId, View != nullptr ? View->YawAngle : -999.0f,
            View != nullptr ? View->PitchAngle : -999.0f, Character->GetViewState().Rotation.Yaw,
            Character->GetActorRotation().Yaw, Character->GetRotationMode() == AlsRotationModeTags::Aiming,
            Animation->GetCurveValue(UAlsConstants::PoseAimingCurveName()),
            Spine != nullptr && Spine->bSpineRotationAllowed, Spine != nullptr ? Spine->SpineAmount : -1.0f,
            Spine != nullptr ? Spine->FinalYawAngle : -999.0f,
            Rotate != nullptr && Rotate->bRotatingLeft, Rotate != nullptr && Rotate->bRotatingRight,
            Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName()),
            Turn != nullptr ? Turn->ActivationDelay : -1.0f,
            Turn != nullptr ? Turn->PlayRate : -1.0f, Animation->GetCurrentActiveMontage() != nullptr,
            Feet != nullptr && Feet->bValid, Feet != nullptr ? Feet->Left.LockAmount : -1.0f,
            Feet != nullptr ? Feet->Right.LockAmount : -1.0f,
            Animation->GetCurveValue(UAlsConstants::FootLeftIkCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootRightIkCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootLeftLockCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootRightLockCurveName()));
        return nullptr;
    }

    FP4StimulusCapture Stimulus;
    if (!CaptureP4Stimulus(*World, *Character, Definition, PreviousPlatformSnapshot, PreviousFootLocks,
        TracePlatformPrimary, TracePlatformSecondary, RuntimePlayer, TransitionFrameDelta, Stimulus))
    {
        return nullptr;
    }
    if (Definition.CaseId == TEXT("platform_base_change"))
    {
        if (Stimulus.PreviousPlatform.PlatformId < 0 || Stimulus.CurrentPlatform.PlatformId < 0 ||
            Stimulus.PreviousPlatform.PlatformId == Stimulus.CurrentPlatform.PlatformId ||
            Stimulus.PreviousPlatform.ColliderId == Stimulus.CurrentPlatform.ColliderId)
        {
            UE_LOG(LogTemp, Error,
                TEXT("P4 platform base change was not observed previous=%d/%lld current=%d/%lld"),
                Stimulus.PreviousPlatform.PlatformId, Stimulus.PreviousPlatform.ColliderId,
                Stimulus.CurrentPlatform.PlatformId, Stimulus.CurrentPlatform.ColliderId);
            return nullptr;
        }
        LeftFootReleaseReason = TEXT("BaseChanged");
        RightFootReleaseReason = TEXT("BaseChanged");
    }
    else if (Definition.CaseId == TEXT("platform_teleport"))
    {
        const float TranslationMeters{static_cast<float>(FVector::Distance(
            Stimulus.CurrentPlatform.Transform.GetLocation(), Stimulus.PreviousPlatform.Transform.GetLocation()) / 100.0f)};
        if (TranslationMeters <= 1.0f)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 platform teleport transition was not observed distance=%.3f"), TranslationMeters);
            return nullptr;
        }
        LeftFootReleaseReason = TEXT("Teleported");
        RightFootReleaseReason = TEXT("Teleported");
    }
    FP4NativeRuntimeCapture NativeRuntime;
    if (!CaptureP4NativeRuntime(*World, *Character, PreviousPlatformSnapshot,
        TracePlatformPrimary, TracePlatformSecondary, NativeRuntime))
    {
        return nullptr;
    }
    const float AccumulatedYaw{static_cast<float>(-FMath::DegreesToRadians(FMath::FindDeltaAngleDegrees(
        InitialActorYaw, Character->GetActorRotation().Yaw)))};
    const TSharedPtr<FJsonObject> NativeActual{
        P4NativeActualObject(*Character, Definition, PreviousLeftFootLockAmount, PreviousRightFootLockAmount,
            PreviousFootLocks, TurnPhase, RotatePhase,
            Definition.Category == TEXT("Turn") ? AccumulatedYaw : 0.0f,
            Definition.Category == TEXT("Rotate") ? RotateAccumulatedYaw : 0.0f,
            LeftFootReleaseReason, RightFootReleaseReason, bSourceSelectionVerified,
            RuntimePlayer, TransitionFrameDelta, NativeRuntime)};
    if (!NativeActual.IsValid())
    {
        return nullptr;
    }

    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("caseId"), Definition.CaseId);
    Object->SetStringField(TEXT("category"), Definition.Category);
    Object->SetStringField(TEXT("stance"), Definition.Stance);
    Object->SetStringField(TEXT("direction"), Definition.Direction);
    Object->SetStringField(TEXT("phase"), Definition.Phase);
    Object->SetStringField(TEXT("provenance"), TEXT("port_oracle_v1"));
    Object->SetObjectField(TEXT("source"), P4CaseSourceObject(Definition, SourceAnimationObjectPath));
    Object->SetObjectField(TEXT("stimulus"), P4StimulusObject(Stimulus));
    Object->SetObjectField(TEXT("nativeActual"), NativeActual.ToSharedRef());
    Object->SetObjectField(TEXT("portExpected"), EmptyP4PortExpected());
    return Object;
}

bool GenerateP4Documents(UWorld* World, const FString& OutputDirectory)
{
    if (!RunP4RuntimeSelectionSelfTest() || !RunP4CleanupSelfTest(*World))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 commandlet self-test failed"));
        return false;
    }
    UClass* AimOverlayClass{LoadObject<UClass>(nullptr, P4AimOverlayClassPath)};
    if (!IsValid(AimOverlayClass) || AimOverlayClass->GetPathName() != P4AimOverlayClassPath)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required overlay animation class is unavailable or mismatched expected=%s actual=%s"),
            P4AimOverlayClassPath, IsValid(AimOverlayClass) ? *AimOverlayClass->GetPathName() : TEXT("<null>"));
        return false;
    }
    const TArray<FP4CaseDefinition> Definitions{CreateP4CaseDefinitions()};
    const TCHAR* Names[]{TEXT("aim"), TEXT("turn"), TEXT("rotate"), TEXT("feet"), TEXT("platform")};
    bool bSuccess{Definitions.Num() == 25};
    for (const TCHAR* Name : Names)
    {
        TArray<TSharedPtr<FJsonValue>> Cases;
        const FString Category{FString{Name}.Left(1).ToUpper() + FString{Name}.Mid(1)};
        for (const FP4CaseDefinition& Definition : Definitions)
        {
            if (Definition.Category == Category)
            {
                const TSharedPtr<FJsonObject> Case{GenerateP4Case(World, Definition, *AimOverlayClass)};
                if (!Case.IsValid())
                {
                    bSuccess = false;
                }
                else
                {
                    Cases.Add(MakeShared<FJsonValueObject>(Case));
                }
            }
        }

        const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
        Document->SetNumberField(TEXT("schemaVersion"), 1);
        Document->SetStringField(TEXT("kind"), TEXT("p4_pose_trace"));
        Document->SetStringField(TEXT("name"), Name);
        Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
        Document->SetObjectField(TEXT("reference"), P4ReferenceObject());
        Document->SetObjectField(TEXT("coordinateSystem"), P4CoordinateSystemObject());
        Document->SetObjectField(TEXT("sources"), P4SourcesObject(*AimOverlayClass));
        Document->SetObjectField(TEXT("tolerances"), P4TolerancesObject());
        Document->SetStringField(TEXT("nativeProvenance"), TEXT("als_runtime"));
        Document->SetStringField(TEXT("portProvenance"), TEXT("port_oracle_v1"));
        Document->SetArrayField(TEXT("cases"), Cases);
        bSuccess &= SaveJson(OutputDirectory / FString::Printf(TEXT("trace_p4_%s.json"), Name), Document);
    }
    return bSuccess;
}
}

UAlsLocomotionTraceCommandlet::UAlsLocomotionTraceCommandlet()
{
    IsClient = false;
    IsEditor = true;
    IsServer = false;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UAlsLocomotionTraceCommandlet::Main(const FString& Parameters)
{
    using namespace AlsLocomotionTrace;
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_MAIN_BEGIN"));
    const FString OutputDirectory{GetRequiredValue(Parameters, TEXT("Output="))};
    const FString ReferenceRoot{GetRequiredValue(Parameters, TEXT("ReferenceRoot="))};
    const FString Commit{GetRequiredValue(Parameters, TEXT("ReferenceCommit="))};
    const FString PatchHashes{GetRequiredValue(Parameters, TEXT("PatchHashes="))};
    const bool bReadyCheck{FParse::Param(*Parameters, TEXT("ReadyCheck"))};
    FString TraceKind;
    FParse::Value(*Parameters, TEXT("TraceKind="), TraceKind);
    const bool bP4{TraceKind == TEXT("P4")};
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_ARGUMENTS_OK ready=%d"), bReadyCheck ? 1 : 0);
    if (OutputDirectory.IsEmpty() || ReferenceRoot.IsEmpty() || Commit.IsEmpty() || PatchHashes.IsEmpty()) return 2;
    if (!IsLowercaseCommitSha(Commit) || Commit != LockedReferenceCommit ||
        !ValidateReference(ReferenceRoot, Commit, PatchHashes)) return 3;
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_OK"));
    if (!ValidateSequenceDefinitions() || !ValidateRequiredAssets()) return 4;
    if (!IFileManager::Get().MakeDirectory(*OutputDirectory, true)) return 5;

    const FString ProbePath{OutputDirectory / TEXT(".write_probe")};
    if (!FFileHelper::SaveStringToFile(TEXT("ready"), *ProbePath) || !IFileManager::Get().Delete(*ProbePath)) return 6;
    if (bReadyCheck)
    {
        if (bP4) { UE_LOG(LogTemp, Display, TEXT("P4_TRACE_READY_OK")); }
        else { UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_OK")); }
        return 0;
    }

    FApp::SetUseFixedTimeStep(true);
    FApp::SetFixedDeltaTime(FixedDeltaSeconds);
    UWorld* World{CreateTraceWorld()};
    if (!IsValid(World)) return 7;
    bool bSuccess{true};
    if (bP4)
    {
        bSuccess = GenerateP4Documents(World, OutputDirectory);
    }
    else
    {
        const TArray<FSequenceDefinition> Definitions{CreateSequenceDefinitions()};
        for (const FSequenceDefinition& Definition : Definitions)
        {
            bSuccess &= GenerateSequence(World, Definition, Commit, OutputDirectory);
        }

        if (bSuccess)
        {
            AAlsTraceCharacter* SettingsCharacter{World->SpawnActor<AAlsTraceCharacter>()};
            bSuccess = IsValid(SettingsCharacter) && SaveJson(OutputDirectory / TEXT("p3_locomotion_settings.json"),
                CreateSettingsDocument(Commit, *SettingsCharacter));
        }
    }
    DestroyTraceWorld(World);
    if (!bSuccess) return 8;
    if (bP4) { UE_LOG(LogTemp, Display, TEXT("P4_TRACE_GENERATION_OK cases=25 commit=%s"), *Commit); }
    else { UE_LOG(LogTemp, Display, TEXT("P3_TRACE_GENERATION_OK sequences=5 commit=%s"), *Commit); }
    return 0;
}
