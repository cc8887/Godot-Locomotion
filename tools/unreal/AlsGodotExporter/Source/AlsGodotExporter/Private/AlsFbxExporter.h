#pragma once

#include "AlsExportTypes.h"

class FAlsFbxExporter
{
public:
    static bool Export(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
        int32& OutFileCount, TArray<FString>& OutNormalizedKeys, FString& OutError);
};
