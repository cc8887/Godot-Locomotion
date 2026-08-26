#pragma once

#include "AlsCharacter.h"
#include "GameFramework/Controller.h"
#include "AlsTraceCharacter.generated.h"

class UAlsAnimationInstance;
class UAlsAnimationInstanceSettings;
class UAlsCharacterMovementComponent;
class UAlsMovementSettings;

UCLASS(Transient)
class AAlsTraceController : public AController
{
    GENERATED_BODY()
};

UCLASS(Transient)
class AAlsTraceCharacter : public AAlsCharacter
{
    GENERATED_BODY()

public:
    AAlsTraceCharacter(const FObjectInitializer& ObjectInitializer = FObjectInitializer::Get());

    const UAlsCharacterMovementComponent* GetTraceMovement() const;
    const UAlsAnimationInstance* GetTraceAnimationInstance() const;
    const UAlsMovementSettings* GetTraceMovementSettings() const;
    const UAlsAnimationInstanceSettings* GetTraceAnimationSettings() const;

protected:
    virtual void BeginPlay() override;

private:
    UPROPERTY(Transient)
    TObjectPtr<UAlsAnimationInstanceSettings> TraceAnimationSettings;
};
