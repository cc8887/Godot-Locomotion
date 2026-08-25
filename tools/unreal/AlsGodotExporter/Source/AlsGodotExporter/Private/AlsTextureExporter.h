#pragma once

#include "AlsExportTypes.h"

class FAlsTextureExporter
{
public:
    static bool Export(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
        int32& OutFileCount, FString& OutError);
};
