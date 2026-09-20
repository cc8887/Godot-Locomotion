#pragma once

#include "Animation/AnimInstance.h"
#include "Commandlets/Commandlet.h"
#include "AlsPoseCacheCommandlet.generated.h"

UCLASS(Transient)
class ALSGODOTEXPORTER_API UAlsPoseCacheProbeAnimInstance final : public UAnimInstance
{
    GENERATED_BODY()
public:
    FAnimInstanceProxy& ProbeProxy();
protected:
    virtual FAnimInstanceProxy* CreateAnimInstanceProxy() override;
    virtual void DestroyAnimInstanceProxy(FAnimInstanceProxy* Proxy) override;
};

UCLASS()
class ALSGODOTEXPORTER_API UAlsPoseCacheCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsPoseCacheCommandlet();
    virtual int32 Main(const FString& Params) override;
};
