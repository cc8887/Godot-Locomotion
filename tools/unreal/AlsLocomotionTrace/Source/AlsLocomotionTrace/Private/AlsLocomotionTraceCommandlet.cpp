#include "AlsLocomotionTraceCommandlet.h"

#include "AlsTraceCharacter.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacterMovementComponent.h"
#include "Components/BoxComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/EngineBaseTypes.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
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
#include "State/AlsInAirState.h"
#include "State/AlsLeanState.h"
#include "State/AlsLocomotionAnimationState.h"
#include "State/AlsStandingState.h"
#include "UObject/UnrealType.h"
#include "Utility/AlsGameplayTags.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(AlsLocomotionTraceCommandlet)

IMPLEMENT_MODULE(FDefaultModuleImpl, AlsLocomotionTrace)

namespace AlsLocomotionTrace
{
constexpr double FixedDeltaSeconds{1.0 / 60.0};
constexpr int32 SchemaVersion{1};
constexpr const TCHAR* LockedReferenceCommit{TEXT("b754d6f0f2bb03741d301f8fb88077ebfe561e17")};
constexpr const TCHAR* LockedPatchHash{TEXT("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f")};

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
        Command.MovementAxes.Y = Frame < 180 ? 0.75 : 0.0;
        Command.Stance = Frame < 60 || Frame >= 150 ? AlsStanceTags::Standing : AlsStanceTags::Crouching;
        if (Frame >= 90 && Frame < 150)
        {
            Command.Stance = AlsStanceTags::Standing;
            Command.bStandBlocked = true;
        }
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
    Character.GetTraceMovement()->TickComponent(FixedDeltaSeconds, LEVELTICK_All, nullptr);
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
    Values->SetNumberField(TEXT("jumpSpeed"), Movement->JumpZVelocity / 100.0);
    Values->SetNumberField(TEXT("landingRecoveryDuration"), 0.2);
    Values->SetNumberField(TEXT("leanHalfLife"), AnimationSettings->General.LeanInterpolationHalfLife);
    Values->SetNumberField(TEXT("maxAcceleration"), Movement->GetMaxAcceleration() / 100.0);
    Values->SetNumberField(TEXT("maxBrakingDeceleration"), Movement->GetMaxBrakingDeceleration() / 100.0);
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
                                      const float LandingRecoveryRemaining, float& AnimationPhase)
{
    UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstance()};
    const FAlsStandingState* Standing{GetAnimationState<FAlsStandingState>(Animation, TEXT("StandingState"))};
    const FAlsCrouchingState* Crouching{GetAnimationState<FAlsCrouchingState>(Animation, TEXT("CrouchingState"))};
    const FAlsLeanState* Lean{GetAnimationState<FAlsLeanState>(Animation, TEXT("LeanState"))};
    const FAlsLocomotionAnimationState* Locomotion{GetAnimationState<FAlsLocomotionAnimationState>(Animation, TEXT("LocomotionState"))};
    const FAlsInAirState* InAir{GetAnimationState<FAlsInAirState>(Animation, TEXT("InAirState"))};
    const bool bGrounded{Movement->IsMovingOnGround()};
    const FVector LocalVelocity{Character.GetActorQuat().UnrotateVector(Character.GetVelocity())};
    const float Stride{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->StrideBlendAmount : 0.0f)
        : (Standing != nullptr ? Standing->StrideBlendAmount : 0.0f)};
    const float PlayRate{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->PlayRate : 1.0f)
        : (Standing != nullptr ? Standing->PlayRate : 1.0f)};
    AnimationPhase = FMath::Fmod(AnimationPhase + static_cast<float>(FixedDeltaSeconds) * PlayRate, 1.0f);

    FString AnimationState{TEXT("Grounded")};
    if (!bGrounded) AnimationState = InAir != nullptr && InAir->bJumped ? TEXT("JumpStart") : TEXT("FallLoop");
    else if (!bWasGrounded || LandingRecoveryRemaining > 0.0f) AnimationState = TEXT("LandRecovery");

    const TSharedRef<FJsonObject> Actual{MakeShared<FJsonObject>()};
    Actual->SetObjectField(TEXT("acceleration"), Vector3Object(Movement->GetCurrentAcceleration()));
    Actual->SetNumberField(TEXT("animationPhase"), AnimationPhase);
    Actual->SetStringField(TEXT("animationState"), AnimationState);
    Actual->SetObjectField(TEXT("blendCoordinates"), Vector2Object(LocalVelocity.Y / 100.0, LocalVelocity.X / 100.0));
    Actual->SetStringField(TEXT("gait"), GaitName(Character.GetGait()));
    Actual->SetBoolField(TEXT("grounded"), bGrounded);
    Actual->SetBoolField(TEXT("jumpTransition"), !bGrounded && bWasGrounded);
    Actual->SetObjectField(TEXT("lean"), Vector2Object(Lean != nullptr ? Lean->RightAmount : 0.0, Lean != nullptr ? Lean->ForwardAmount : 0.0));
    Actual->SetStringField(TEXT("locomotionState"), bGrounded ? TEXT("Grounded") : TEXT("InAir"));
    Actual->SetNumberField(TEXT("playRate"), PlayRate);
    Actual->SetObjectField(TEXT("position"), Vector3Object(Character.GetActorLocation() - Origin));
    Actual->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    Actual->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    Actual->SetNumberField(TEXT("stride"), Stride);
    Actual->SetNumberField(TEXT("targetYaw"), FMath::DegreesToRadians(Locomotion != nullptr ? Locomotion->TargetYawAngleWorldSpace : Character.GetActorRotation().Yaw));
    Actual->SetObjectField(TEXT("velocity"), Vector3Object(Character.GetVelocity()));
    Actual->SetNumberField(TEXT("yaw"), FMath::DegreesToRadians(Character.GetActorRotation().Yaw));

    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("actual"), Actual);
    Object->SetObjectField(TEXT("command"), CreateCommandObject(Command));
    Object->SetNumberField(TEXT("index"), Frame);
    Object->SetNumberField(TEXT("tick"), Frame);
    Object->SetNumberField(TEXT("time"), Frame * FixedDeltaSeconds);
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
    float LandingRecoveryRemaining{0.0f};
    float AnimationPhase{0.0f};

    for (int32 Frame{0}; Frame < Definition.FrameCount; ++Frame)
    {
        const FTraceCommand Command{Definition.GetCommand(Frame)};
        Character->SetDesiredGait(Command.Gait);
        Character->SetDesiredStance(Command.Stance);
        Character->SetDesiredRotationMode(Command.RotationMode);
        Character->SetDesiredAiming(Command.RotationMode == AlsRotationModeTags::Aiming);
        Controller->SetControlRotation(FRotator{0.0,
            Command.RotationMode == AlsRotationModeTags::Aiming ? Command.AimYaw : Command.ViewYaw, 0.0});

        if (Command.bStandBlocked && !IsValid(ClearanceBlocker))
        {
            ClearanceBlocker = World->SpawnActor<AActor>();
            UBoxComponent* Collision{NewObject<UBoxComponent>(ClearanceBlocker, TEXT("ClearanceBlocker"))};
            ClearanceBlocker->SetRootComponent(Collision);
            Collision->SetBoxExtent(FVector{80.0, 80.0, 10.0});
            Collision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
            Collision->RegisterComponent();
            ClearanceBlocker->SetActorLocation(Character->GetActorLocation() + FVector{0.0, 0.0, 145.0});
        }
        else if (!Command.bStandBlocked && IsValid(ClearanceBlocker))
        {
            ClearanceBlocker->Destroy();
            ClearanceBlocker = nullptr;
        }

        const FVector MovementDirection{Command.MovementAxes.Y, Command.MovementAxes.X, 0.0};
        if (!MovementDirection.IsNearlyZero()) Character->AddMovementInput(MovementDirection.GetSafeNormal(), 1.0f, true);
        if (Command.bJumpPressed) Character->Jump();

        TickTraceCharacter(*Character);
        const bool bGrounded{Character->GetTraceMovement()->IsMovingOnGround()};
        if (!bWasGrounded && bGrounded) LandingRecoveryRemaining = 0.2f;
        else LandingRecoveryRemaining = FMath::Max(0.0f, LandingRecoveryRemaining - static_cast<float>(FixedDeltaSeconds));
        Frames.Add(MakeShared<FJsonValueObject>(SnapshotFrame(*Character, Command, Frame, Origin,
            bWasGrounded, LandingRecoveryRemaining, AnimationPhase)));
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
        UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_OK"));
        return 0;
    }

    FApp::SetUseFixedTimeStep(true);
    FApp::SetFixedDeltaTime(FixedDeltaSeconds);
    UWorld* World{CreateTraceWorld()};
    if (!IsValid(World)) return 7;
    bool bSuccess{true};
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
    DestroyTraceWorld(World);
    if (!bSuccess) return 8;
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_GENERATION_OK sequences=5 commit=%s"), *Commit);
    return 0;
}
