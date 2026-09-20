#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsStopGraphCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsStopGraphCommandlet final : public UCommandlet
{
    GENERATED_BODY()

public:
    UAlsStopGraphCommandlet();
    virtual int32 Main(const FString& Params) override;
};
