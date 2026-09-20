#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsBlendSpaceTraceCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsBlendSpaceTraceCommandlet final : public UCommandlet
{
    GENERATED_BODY()

public:
    UAlsBlendSpaceTraceCommandlet();
    virtual int32 Main(const FString& Params) override;
};
