#pragma once
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "AlsNotifyLifecycleProbe.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyLifecycleProbeNotify final : public UAnimNotify
{
    GENERATED_BODY()
public:
    virtual void Notify(USkeletalMeshComponent*, UAnimSequenceBase*, const FAnimNotifyEventReference&) override;
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyLifecycleProbeState final : public UAnimNotifyState
{
    GENERATED_BODY()
public:
    virtual void NotifyBegin(USkeletalMeshComponent*, UAnimSequenceBase*, float, const FAnimNotifyEventReference&) override;
    virtual void NotifyTick(USkeletalMeshComponent*, UAnimSequenceBase*, float, const FAnimNotifyEventReference&) override;
    virtual void NotifyEnd(USkeletalMeshComponent*, UAnimSequenceBase*, const FAnimNotifyEventReference&) override;
};

int32 RunAlsNotifyLifecycleProbe(const FString& Params);
