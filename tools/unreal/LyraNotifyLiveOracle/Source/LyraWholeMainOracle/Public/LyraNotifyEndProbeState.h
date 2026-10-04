#pragma once
#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "LyraNotifyEndProbeState.generated.h"
UCLASS()
class ULyraNotifyEndProbeState final:public UAnimNotifyState
{
    GENERATED_BODY()
public:
    int32 Tag=-1;
    TFunction<void(int32)> Record;
    virtual void NotifyEnd(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference&) override;
};
UCLASS()
class ULyraNotifyEndProbeInstance final:public UAnimInstance
{
    GENERATED_BODY()
public:
    int32 SkippedTag=-1;
    virtual bool ShouldTriggerAnimNotifyState(const UAnimNotifyState* State) const override;
};
