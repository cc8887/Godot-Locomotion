#pragma once

#include "AlsCharacter.h"
#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/Controller.h"
#include "AlsTraceCharacter.generated.h"

class UAlsAnimationInstance;
class UAlsAnimationInstanceSettings;
class UAlsCharacterMovementComponent;
class UAlsMovementSettings;
class UAnimMontage;

UCLASS(Transient)
class AAlsTraceController : public AController
{
    GENERATED_BODY()
};

UCLASS(Transient)
class UAlsTraceSkeletalMeshComponent : public USkeletalMeshComponent
{
    GENERATED_BODY()

public:
    virtual void TickComponent(float DeltaTime, ELevelTick TickType,
                               FActorComponentTickFunction* ThisTickFunction) override;
    virtual void RefreshBoneTransforms(FActorComponentTickFunction* TickFunction = nullptr) override;
    virtual void FinalizeBoneTransform() override;

    void ResetTracePipelineCounts();
    int32 GetTracePublicTickCount() const { return TracePublicTickCount; }
    int32 GetTraceEvaluationCount() const { return TraceEvaluationCount; }
    int32 GetTracePostUpdateCount() const { return TracePostUpdateCount; }

private:
    int32 TracePublicTickCount{0};
    int32 TraceEvaluationCount{0};
    int32 TracePostUpdateCount{0};
};

UCLASS(Transient)
class AAlsTraceCharacter : public AAlsCharacter
{
    GENERATED_BODY()

public:
    AAlsTraceCharacter(const FObjectInitializer& ObjectInitializer = FObjectInitializer::Get());

    const UAlsCharacterMovementComponent* GetTraceMovement() const;
    const UAlsAnimationInstance* GetTraceAnimationInstance() const;
    UAlsAnimationInstance* GetTraceAnimationInstanceMutable();
    UAlsTraceSkeletalMeshComponent* GetTraceMesh() const;
    const UAlsMovementSettings* GetTraceMovementSettings() const;
    const UAlsAnimationInstanceSettings* GetTraceAnimationSettings() const;
    void SetTraceViewRotation(const FRotator& Rotation);
    void ApplyTraceDesiredState(FGameplayTag NewRotationMode, bool bNewAiming,
                                FGameplayTag NewStance, FGameplayTag NewOverlayMode);
    void BindTraceMontageStartedObserver(UAnimMontage* Montage, TFunction<void(UAnimMontage*)> Observer);
    void UnbindTraceMontageStartedObserver();

protected:
    virtual void BeginPlay() override;

private:
    UFUNCTION()
    void ObserveTraceMontageStarted(UAnimMontage* Montage);

    TWeakObjectPtr<UAnimMontage> TraceObservedMontage;
    TFunction<void(UAnimMontage*)> TraceMontageStartedObserver;

    UPROPERTY(Transient)
    TObjectPtr<UAlsAnimationInstanceSettings> TraceAnimationSettings;
};
