#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsInertializationCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsInertializationCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsInertializationCommandlet();
    virtual int32 Main(const FString& Params) override;
};
