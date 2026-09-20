#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsPoseBlendCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsPoseBlendCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsPoseBlendCommandlet();
    virtual int32 Main(const FString& Params) override;
};
