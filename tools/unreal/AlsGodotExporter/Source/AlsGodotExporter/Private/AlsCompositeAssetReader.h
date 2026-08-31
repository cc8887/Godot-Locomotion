#pragma once

#include "AlsExportTypes.h"
#include "Dom/JsonObject.h"

class FAlsCompositeAssetReader
{
public:
    static bool Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError);
    static bool RunSelfTest(int32& OutCaseCount, FString& OutError);
};
