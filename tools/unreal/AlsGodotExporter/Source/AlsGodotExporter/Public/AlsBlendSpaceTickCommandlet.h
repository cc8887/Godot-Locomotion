#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsBlendSpaceTickCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsBlendSpaceTickCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsBlendSpaceTickCommandlet();
    virtual int32 Main(const FString& Params) override;
};
