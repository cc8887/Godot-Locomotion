#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsLengthSyncCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsLengthSyncCommandlet final : public UCommandlet
{
    GENERATED_BODY()
public:
    UAlsLengthSyncCommandlet();
    virtual int32 Main(const FString& Params) override;
};
