#pragma once

#include "AlsExportTypes.h"

class FAlsExportPlanner
{
public:
    static bool Write(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
        int32& OutExportableCount, int32& OutConfigCount, FString& OutError);
};
