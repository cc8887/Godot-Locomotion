#include "AlsTraceCharacter.h"

#include "AlsAnimationInstance.h"
#include "AlsCharacterMovementComponent.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Components/SkeletalMeshComponent.h"
#include "Modules/ModuleManager.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Settings/AlsCharacterSettings.h"
#include "Settings/AlsMovementSettings.h"
#include "UObject/ConstructorHelpers.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(AlsTraceCharacter)

void UAlsTraceSkeletalMeshComponent::TickComponent(
    const float DeltaTime,
    const ELevelTick TickType,
    FActorComponentTickFunction* ThisTickFunction)
{
    ++TracePublicTickCount;
    Super::TickComponent(DeltaTime, TickType, ThisTickFunction);
}

void UAlsTraceSkeletalMeshComponent::RefreshBoneTransforms(FActorComponentTickFunction* TickFunction)
{
    ++TraceEvaluationCount;
    Super::RefreshBoneTransforms(TickFunction);
}

void UAlsTraceSkeletalMeshComponent::FinalizeBoneTransform()
{
    ++TracePostUpdateCount;
    Super::FinalizeBoneTransform();
}

void UAlsTraceSkeletalMeshComponent::ResetTracePipelineCounts()
{
    TracePublicTickCount = 0;
    TraceEvaluationCount = 0;
    TracePostUpdateCount = 0;
}

AAlsTraceCharacter::AAlsTraceCharacter(const FObjectInitializer& ObjectInitializer)
    : Super(ObjectInitializer.SetDefaultSubobjectClass<UAlsTraceSkeletalMeshComponent>(ACharacter::MeshComponentName))
{
    // Settings load Roll transitively during CDO construction, before commandlet Main can run.
    FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("ALSCamera"));
    static ConstructorHelpers::FObjectFinder<UAlsCharacterSettings> CharacterSettingsAsset{
	TEXT("/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default")};
    static ConstructorHelpers::FObjectFinder<UAlsMovementSettings> MovementSettingsAsset{
	TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal")};
    static ConstructorHelpers::FObjectFinder<UAlsAnimationInstanceSettings> AnimationSettingsAsset{
	TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default")};
    static ConstructorHelpers::FObjectFinder<USkeletalMesh> SkeletalMeshAsset{
	TEXT("/ALS/ALS/Character/SKM_Als.SKM_Als")};
    static ConstructorHelpers::FClassFinder<UAnimInstance> AnimationBlueprint{
	TEXT("/ALS/ALS/Character/AB_Als")};

    Settings = CharacterSettingsAsset.Object;
    MovementSettings = MovementSettingsAsset.Object;
    TraceAnimationSettings = AnimationSettingsAsset.Object;

    GetMesh()->SetSkeletalMeshAsset(SkeletalMeshAsset.Object);
    GetMesh()->SetAnimInstanceClass(AnimationBlueprint.Class);
    GetMesh()->VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption::AlwaysTickPoseAndRefreshBones;
}

void AAlsTraceCharacter::BeginPlay()
{
    // The native trace subclass assigns its animation blueprint in its constructor. Re-cache the final instance created
    // during actor startup so the unmodified ALS tick path observes the same instance as the mesh.
    AnimationInstance = Cast<UAlsAnimationInstance>(GetMesh()->GetAnimInstance());
    Super::BeginPlay();
}

const UAlsCharacterMovementComponent* AAlsTraceCharacter::GetTraceMovement() const
{
    return AlsCharacterMovement;
}

const UAlsAnimationInstance* AAlsTraceCharacter::GetTraceAnimationInstance() const
{
    return AnimationInstance.Get();
}

UAlsAnimationInstance* AAlsTraceCharacter::GetTraceAnimationInstanceMutable()
{
    return AnimationInstance.Get();
}

UAlsTraceSkeletalMeshComponent* AAlsTraceCharacter::GetTraceMesh() const
{
    return CastChecked<UAlsTraceSkeletalMeshComponent>(GetMesh());
}

const UAlsMovementSettings* AAlsTraceCharacter::GetTraceMovementSettings() const
{
    return MovementSettings;
}

const UAlsAnimationInstanceSettings* AAlsTraceCharacter::GetTraceAnimationSettings() const
{
    return TraceAnimationSettings;
}

void AAlsTraceCharacter::SetTraceViewRotation(const FRotator& Rotation)
{
    const FRotator NormalizedRotation{Rotation.GetNormalized()};
    ReplicatedViewRotation = NormalizedRotation;
    ViewState.NetworkSmoothing.InitialRotation = NormalizedRotation;
    ViewState.NetworkSmoothing.TargetRotation = NormalizedRotation;
    ViewState.NetworkSmoothing.FinalRotation = NormalizedRotation;
    ViewState.Rotation = NormalizedRotation;
    ViewState.PreviousYawAngle = UE_REAL_TO_FLOAT(NormalizedRotation.Yaw);
    ViewState.YawSpeed = 0.0f;
}

void AAlsTraceCharacter::BindTraceMontageStartedObserver(
    UAnimMontage* Montage, TFunction<void(UAnimMontage*)> Observer)
{
    UnbindTraceMontageStartedObserver();
    TraceObservedMontage = Montage;
    TraceMontageStartedObserver = MoveTemp(Observer);
    GetTraceAnimationInstanceMutable()->OnMontageStarted.AddDynamic(
        this, &AAlsTraceCharacter::ObserveTraceMontageStarted);
}

void AAlsTraceCharacter::UnbindTraceMontageStartedObserver()
{
    if (UAlsAnimationInstance* Animation{GetTraceAnimationInstanceMutable()})
    {
        Animation->OnMontageStarted.RemoveDynamic(this, &AAlsTraceCharacter::ObserveTraceMontageStarted);
    }
    TraceObservedMontage.Reset();
    TraceMontageStartedObserver = nullptr;
}

void AAlsTraceCharacter::ObserveTraceMontageStarted(UAnimMontage* Montage)
{
    if (Montage == TraceObservedMontage.Get() && TraceMontageStartedObserver)
    {
        TraceMontageStartedObserver(Montage);
    }
}

void AAlsTraceCharacter::ApplyTraceDesiredState(const FGameplayTag NewRotationMode, const bool bNewAiming,
                                                const FGameplayTag NewStance, const FGameplayTag NewOverlayMode)
{
    bDesiredAiming = bNewAiming;
    DesiredRotationMode = NewRotationMode;
    DesiredStance = NewStance;
    OverlayMode = NewOverlayMode;
    ApplyDesiredStance();
    SetStance(NewStance);
    RefreshRotationMode();
}
