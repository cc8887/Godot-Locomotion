#include "AlsSourcePoseKeysLibrary.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/Skeleton.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace
{
float SourceComponent(const FVector3f& Key, const int32 Index)
{
    return Index == 0 ? Key.X : Index == 1 ? Key.Y : Key.Z;
}

float SourceComponent(const FQuat4f& Key, const int32 Index)
{
    return Index == 0 ? Key.X : Index == 1 ? Key.Y : Index == 2 ? Key.Z : Key.W;
}

template <typename KeyType>
bool ReadChannel(const TArray<KeyType>& Keys, const int32 KeyCount, const int32 ComponentCount,
                 TArray<TSharedPtr<FJsonValue>>& OutKeys, const bool AllowEmpty = false)
{
    if (AllowEmpty && Keys.IsEmpty()) return true;
    if (Keys.Num() != 1 && Keys.Num() != KeyCount) return false;
    for (const KeyType& Key : Keys)
    {
        TArray<TSharedPtr<FJsonValue>> Components;
        for (int32 Index = 0; Index < ComponentCount; ++Index)
        {
            // float32 -> double is exact. Keep source quaternion components untouched.
            const double Value = static_cast<double>(SourceComponent(Key, Index));
            if (!FMath::IsFinite(Value)) return false;
            Components.Add(MakeShared<FJsonValueNumber>(Value));
        }
        OutKeys.Add(MakeShared<FJsonValueArray>(Components));
    }
    return true;
}
}

FString UAlsSourcePoseKeysLibrary::ReadSourcePoseKeys(UAnimSequence* Animation)
{
    if (!Animation || !Animation->GetSkeleton()) return {};
    const TScriptInterface<IAnimationDataModel> Model = Animation->GetDataModelInterface();
    if (!Model) return {};
    const int32 KeyCount = Model->GetNumberOfKeys();
    if (KeyCount <= 0) return {};

    const TSharedRef<FJsonObject> Asset = MakeShared<FJsonObject>();
    Asset->SetStringField(TEXT("source"), Animation->GetPathName());
    Asset->SetStringField(TEXT("skeletonSource"), Animation->GetSkeleton()->GetPathName());
    Asset->SetNumberField(TEXT("frameRateNumerator"), Model->GetFrameRate().Numerator);
    Asset->SetNumberField(TEXT("frameRateDenominator"), Model->GetFrameRate().Denominator);
    Asset->SetNumberField(TEXT("sampledKeyCount"), KeyCount);
    Asset->SetNumberField(TEXT("playLength"), Model->GetPlayLength());
    TArray<TSharedPtr<FJsonValue>> Tracks;

    // This source-data API remains implemented in UAnimDataModel. New transform
    // getters materialize FTransform values and cannot preserve original channel counts.
    PRAGMA_DISABLE_DEPRECATION_WARNINGS
    const TArray<FBoneAnimationTrack>& SourceTracks = Model->GetBoneAnimationTracks();
    PRAGMA_ENABLE_DEPRECATION_WARNINGS
    for (const FBoneAnimationTrack& Track : SourceTracks)
    {
        const FRawAnimSequenceTrack& Raw = Track.InternalTrackData;
        TArray<TSharedPtr<FJsonValue>> Positions, Rotations, Scales;
        if (!ReadChannel(Raw.PosKeys, KeyCount, 3, Positions) ||
            !ReadChannel(Raw.RotKeys, KeyCount, 4, Rotations) ||
            !ReadChannel(Raw.ScaleKeys, KeyCount, 3, Scales, true)) return {};
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("bone"), Track.Name.ToString());
        Row->SetArrayField(TEXT("positions"), Positions);
        Row->SetArrayField(TEXT("rotations"), Rotations);
        Row->SetArrayField(TEXT("scales"), Scales);
        Tracks.Add(MakeShared<FJsonValueObject>(Row));
    }
    Asset->SetArrayField(TEXT("tracks"), Tracks);
    FString Json;
    if (!FJsonSerializer::Serialize(Asset, TJsonWriterFactory<>::Create(&Json))) return {};
    return Json;
}
