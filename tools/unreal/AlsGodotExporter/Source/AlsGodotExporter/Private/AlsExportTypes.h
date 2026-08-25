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

const TCHAR* AlsAssetKindToString(EAlsAssetKind Kind);
bool AlsAssetKindIsExportable(EAlsAssetKind Kind);
