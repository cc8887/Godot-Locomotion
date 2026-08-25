#pragma once

#include "AlsExportTypes.h"

class FAlsManifestWriter
{
public:
    static bool WritePlanned(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets, FString& OutError);
    static bool WriteComplete(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
        const TArray<FAlsExportFile>& Files, FString& OutError);
};
