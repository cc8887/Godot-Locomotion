#pragma once

#include "AlsExportTypes.h"

class FAlsOutputAuditor
{
public:
    static bool Audit(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
        const TArray<FString>& NormalizedFbxKeys, TArray<FAlsExportFile>& OutFiles, FString& OutError);
};
