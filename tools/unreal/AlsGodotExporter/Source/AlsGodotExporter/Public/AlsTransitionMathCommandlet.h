#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsTransitionMathCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsTransitionMathCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsTransitionMathCommandlet();
    virtual int32 Main(const FString& Params) override;
};
