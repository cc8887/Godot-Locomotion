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

const TCHAR* AlsAssetKindToString(EAlsAssetKind Kind);
bool AlsAssetKindIsExportable(EAlsAssetKind Kind);
