#pragma once

#include "AlsExportTypes.h"
#include "Dom/JsonObject.h"

class FAlsAnimationMetadataReader
{
public:
    static bool Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata,
        TArray<FAlsExportedFloatCurve>& OutCurves, FString& OutError);
    static bool RunCurveKeySelfTest(FString& OutError);
};
