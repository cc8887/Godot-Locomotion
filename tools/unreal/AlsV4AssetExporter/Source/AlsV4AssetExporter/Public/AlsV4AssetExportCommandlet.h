#pragma once

#include "Commandlets/Commandlet.h"
#include "AlsV4AssetExportCommandlet.generated.h"

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsV4AssetExportCommandlet final : public UCommandlet
{
    GENERATED_BODY()

public:
    UAlsV4AssetExportCommandlet();

    virtual int32 Main(const FString& Params) override;
};
