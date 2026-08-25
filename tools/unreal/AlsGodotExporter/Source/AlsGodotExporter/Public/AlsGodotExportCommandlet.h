#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsGodotExportCommandlet.generated.h"

UCLASS()
class ALSGODOTEXPORTER_API UAlsGodotExportCommandlet final : public UCommandlet
{
    GENERATED_BODY()

public:
    UAlsGodotExportCommandlet();

    virtual int32 Main(const FString& Params) override;
};
