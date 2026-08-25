#pragma once

#include "AlsExportTypes.h"
#include "Dom/JsonObject.h"

class FAlsRigMetadataReader
{
public:
    static bool Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError);
};
