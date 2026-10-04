#pragma once
#include "CoreMinimal.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "LyraNotifyLiveProbe.generated.h"
UCLASS()
class ULyraNotifyLiveState final:public UAnimNotifyState
{
    GENERATED_BODY()
public:
    int32 Policy=-1;
    TFunction<void(int32,float,const FAnimNotifyEventReference&)> Record;
    virtual void NotifyBegin(USkeletalMeshComponent*,UAnimSequenceBase*,float,const FAnimNotifyEventReference&) override;
    virtual void NotifyTick(USkeletalMeshComponent*,UAnimSequenceBase*,float,const FAnimNotifyEventReference&) override;
    virtual void NotifyEnd(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference&) override;
};
UCLASS()
class ULyraNotifyLiveInstant final:public UAnimNotify
{
    GENERATED_BODY()
public:
    TFunction<void(const FAnimNotifyEventReference&)> Record;
    virtual void Notify(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference&) override;
};
UCLASS()
class ULyraNotifyLiveInstance final:public UAnimInstance
{
    GENERATED_BODY()
public:
    int32 SkippedPolicy=-1;
    TFunction<void()> Named;
    UFUNCTION() void AnimNotify_ProbeNamed(){if(Named)Named();}
    virtual bool ShouldTriggerAnimNotifyState(const UAnimNotifyState*) const override;
};
