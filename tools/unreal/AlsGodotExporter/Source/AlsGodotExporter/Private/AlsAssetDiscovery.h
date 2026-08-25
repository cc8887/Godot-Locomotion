#pragma once

#include "AlsExportTypes.h"

class FAlsAssetDiscovery
{
public:
    static bool Discover(TArray<FAlsExportAsset>& OutAssets, FString& OutError);

private:
    static EAlsAssetKind Classify(const FAssetData& AssetData);
    static bool IsExcluded(const FString& ObjectPath);
};
