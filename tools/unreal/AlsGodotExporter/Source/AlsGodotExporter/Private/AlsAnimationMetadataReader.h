#pragma once

#include "AlsExportTypes.h"
#include "Dom/JsonObject.h"

class UAnimSequenceBase;

class FAlsAnimationMetadataReader
{
public:
    static bool Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata,
        TArray<FAlsExportedFloatCurve>& OutCurves, FString& OutError);
    static bool ReadTimeline(const UAnimSequenceBase& Sequence, const FString& AssetStableId,
        TSharedRef<FJsonObject>& OutMetadata, FString& OutError);
    static bool RunCurveKeySelfTest(int32& OutCaseCount, FString& OutError);
    static bool RunTimelineSelfTest(int32& OutCaseCount, FString& OutError);
};
