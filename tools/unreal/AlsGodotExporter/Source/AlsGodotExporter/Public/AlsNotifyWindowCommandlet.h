#pragma once

#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimSequence.h"
#include "Commandlets/Commandlet.h"
#include "AlsNotifyWindowCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyQueueProbeNotify final : public UAnimNotify
{
    GENERATED_BODY()
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyWindowProbeState final : public UAnimNotifyState
{
    GENERATED_BODY()
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyWindowProbeSequence final : public UAnimSequence
{
    GENERATED_BODY()
public:
    float ProbeLength = 1.f;
    virtual float GetPlayLength() const override { return ProbeLength; }
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyQueueProbeSequence final : public UAnimSequence
{
    GENERATED_BODY()
public:
    TArray<FAnimNotifyEventReference> QueuedReferences;
    virtual float GetPlayLength() const override { return 1.f; }
    virtual void TickAssetPlayer(FAnimTickRecord&, FAnimNotifyQueue& Queue, FAnimAssetTickContext&) const override
    {
        Queue.AnimNotifies = QueuedReferences;
    }
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsNotifyWindowCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsNotifyWindowCommandlet();
    virtual int32 Main(const FString& Params) override;
};
