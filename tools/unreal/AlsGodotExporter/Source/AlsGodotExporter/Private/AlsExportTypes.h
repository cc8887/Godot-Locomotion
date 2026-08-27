#pragma once

#include "AssetRegistry/AssetData.h"

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

const TCHAR* AlsAssetKindToString(EAlsAssetKind Kind);
bool AlsAssetKindIsExportable(EAlsAssetKind Kind);
