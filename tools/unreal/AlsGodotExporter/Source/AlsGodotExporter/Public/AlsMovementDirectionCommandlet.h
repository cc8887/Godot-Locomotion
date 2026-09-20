#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsMovementDirectionCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsMovementDirectionCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsMovementDirectionCommandlet();
    virtual int32 Main(const FString& Params) override;
};
