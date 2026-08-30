#pragma once

#include "AssetRegistry/AssetData.h"
#include "Dom/JsonObject.h"

enum class EAlsAssetKind : uint8
{
    Skeleton,
    SkeletalMesh,
    StaticMesh,
    AnimationSequence,
    AnimMontage,
    BlendSpace,
    AimOffset,
    PhysicsAsset,
    Material,
    MaterialInstance,
    Texture,
    Curve,
    DataTable,
    Blueprint,
    OtherConfig,
};

struct FAlsExportAsset
{
    FAssetData AssetData;
    EAlsAssetKind Kind = EAlsAssetKind::OtherConfig;
    FString Id;
    FString OutputPath;
    TArray<FString> Dependencies;
    TArray<FString> ExternalDependencies;
};

struct FAlsExportFile
{
    FString RelativePath;
    FString Sha256;
    int64 Size = 0;
};

struct FAlsExportedFloatCurveKey
{
    double TimeSeconds = 0.0;
    double Value = 0.0;
    FString Interpolation = TEXT("Constant");
    double ArriveTangent = 0.0;
    double LeaveTangent = 0.0;
};

struct FAlsExportedFloatCurve
{
    int32 StableCurveId = INDEX_NONE;
    FString CanonicalKind = TEXT("None");
    FString SourceName;
    FString SourceProvenance = TEXT("source_curve");
    FString PreInfinity = TEXT("Constant");
    FString PostInfinity = TEXT("Constant");
    TArray<FAlsExportedFloatCurveKey> Keys;
};

struct FAlsExportedTimelineEntry
{
    FString StableEventId;
    FString Kind;
    FString SourceClassPath;
    FString DisplayName;
    double TimeSeconds{0.0};
    double DurationSeconds{0.0};
    double TriggerWeightThreshold{0.0};
    FString TickMode;
    int32 SourceIndex{INDEX_NONE};
    int32 TrackIndex{INDEX_NONE};
    TSharedPtr<FJsonObject> Payload;
};

struct FAlsExportedSyncMarker
{
    FString StableMarkerId;
    FString Name;
    double TimeSeconds{0.0};
    int32 SourceIndex{INDEX_NONE};
    int32 TrackIndex{INDEX_NONE};
};

const TCHAR* AlsAssetKindToString(EAlsAssetKind Kind);
bool AlsAssetKindIsExportable(EAlsAssetKind Kind);
