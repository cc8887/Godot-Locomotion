#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsLocomotionTraceCommandlet.generated.h"

UCLASS()
class ALSLOCOMOTIONTRACE_API UAlsLocomotionTraceCommandlet : public UCommandlet
{
    GENERATED_BODY()

public:
    UAlsLocomotionTraceCommandlet();

    virtual int32 Main(const FString& Parameters) override;
};
