#pragma once

#include "AlsExportTypes.h"

class FAlsManifestWriter
{
public:
    static bool WritePlanned(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets, FString& OutError);
};
