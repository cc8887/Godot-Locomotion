#include "AlsAssetDiscovery.h"

#include "AlsStableAssetId.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Modules/ModuleManager.h"

namespace
{
    bool CanBringDependencyIntoClosure(const EAlsAssetKind Kind)
    {
        return Kind == EAlsAssetKind::Material || Kind == EAlsAssetKind::MaterialInstance || Kind == EAlsAssetKind::Texture;
    }

    bool ShouldInspectDependencies(const EAlsAssetKind Kind)
    {
        return Kind == EAlsAssetKind::SkeletalMesh || Kind == EAlsAssetKind::StaticMesh ||
            Kind == EAlsAssetKind::Material || Kind == EAlsAssetKind::MaterialInstance;
    }

    bool IsValidContentRoot(const FString& ContentRoot)
    {
        if (!ContentRoot.StartsWith(TEXT("/Game/"), ESearchCase::CaseSensitive) ||
            ContentRoot.EndsWith(TEXT("/"), ESearchCase::CaseSensitive) || ContentRoot.Contains(TEXT("\\")))
        {
            return false;
        }

        TArray<FString> Segments;
        ContentRoot.Mid(1).ParseIntoArray(Segments, TEXT("/"), false);
        for (const FString& Segment : Segments)
        {
            if (Segment.IsEmpty() || Segment == TEXT(".") || Segment == TEXT(".."))
            {
                return false;
            }
        }
        return true;
    }

    bool IsWithinContentRoot(const FString& PackagePath, const FString& ContentRoot)
    {
        return PackagePath == ContentRoot || PackagePath.StartsWith(ContentRoot + TEXT("/"), ESearchCase::CaseSensitive);
    }

    bool MakeOutputPath(const EAlsAssetKind Kind, const FAssetData& AssetData,
        const FString& ContentRoot, FString& OutPath, FString& OutError)
    {
        FString KindDirectory;
        const TCHAR* Extension = TEXT("fbx");
        switch (Kind)
        {
        case EAlsAssetKind::SkeletalMesh: KindDirectory = TEXT("meshes/skeletal"); break;
        case EAlsAssetKind::StaticMesh: KindDirectory = TEXT("meshes/static"); break;
        case EAlsAssetKind::AnimationSequence: KindDirectory = TEXT("animations"); break;
        case EAlsAssetKind::Texture: KindDirectory = TEXT("textures"); Extension = TEXT("png"); break;
        default:
            OutPath.Reset();
            return true;
        }

        const FString AssetName = AssetData.AssetName.ToString();
        const FString PackagePath = AssetData.PackageName.ToString();
        if (AssetName.IsEmpty() || AssetName == TEXT(".") || AssetName == TEXT("..") ||
            AssetName.Contains(TEXT("/")) || AssetName.Contains(TEXT("\\")) ||
            !IsWithinContentRoot(PackagePath, ContentRoot))
        {
            OutError = FString::Printf(TEXT("Cannot preserve the original asset name in a safe export path: %s"),
                *AssetData.GetObjectPathString());
            return false;
        }

        FString RelativeDirectory;
        if (PackagePath != ContentRoot)
        {
            FString RelativePackagePath = PackagePath.Mid(ContentRoot.Len() + 1);
            const FString AssetSuffix = TEXT("/") + AssetName;
            if (RelativePackagePath == AssetName)
            {
                RelativeDirectory.Reset();
            }
            else if (RelativePackagePath.EndsWith(AssetSuffix, ESearchCase::CaseSensitive))
            {
                RelativePackagePath.LeftChopInline(AssetSuffix.Len(), EAllowShrinking::No);
                RelativeDirectory = MoveTemp(RelativePackagePath);
            }
            else
            {
                OutError = FString::Printf(TEXT("Asset package does not end with its original asset name: %s"),
                    *AssetData.GetObjectPathString());
                return false;
            }
        }

        OutPath = KindDirectory;
        if (!RelativeDirectory.IsEmpty())
        {
            OutPath += TEXT("/") + RelativeDirectory;
        }
        OutPath += TEXT("/") + AssetName + TEXT(".") + Extension;
        return true;
    }
}

const TCHAR* AlsAssetKindToString(const EAlsAssetKind Kind)
{
    switch (Kind)
    {
    case EAlsAssetKind::Skeleton: return TEXT("Skeleton");
    case EAlsAssetKind::SkeletalMesh: return TEXT("SkeletalMesh");
    case EAlsAssetKind::StaticMesh: return TEXT("StaticMesh");
    case EAlsAssetKind::AnimationSequence: return TEXT("AnimationSequence");
    case EAlsAssetKind::AnimMontage: return TEXT("AnimMontage");
    case EAlsAssetKind::BlendSpace: return TEXT("BlendSpace");
    case EAlsAssetKind::AimOffset: return TEXT("AimOffset");
    case EAlsAssetKind::PhysicsAsset: return TEXT("PhysicsAsset");
    case EAlsAssetKind::Material: return TEXT("Material");
    case EAlsAssetKind::MaterialInstance: return TEXT("MaterialInstance");
    case EAlsAssetKind::Texture: return TEXT("Texture");
    case EAlsAssetKind::Curve: return TEXT("Curve");
    case EAlsAssetKind::DataTable: return TEXT("DataTable");
    case EAlsAssetKind::Blueprint: return TEXT("Blueprint");
    default: return TEXT("OtherConfig");
    }
}

bool AlsAssetKindIsExportable(const EAlsAssetKind Kind)
{
    return Kind == EAlsAssetKind::SkeletalMesh || Kind == EAlsAssetKind::StaticMesh ||
        Kind == EAlsAssetKind::AnimationSequence || Kind == EAlsAssetKind::Texture;
}

bool FAlsAssetDiscovery::Discover(const FString& ContentRoot, TArray<FAlsExportAsset>& OutAssets, FString& OutError)
{
    OutError.Reset();
    if (!IsValidContentRoot(ContentRoot))
    {
        OutError = FString::Printf(TEXT("ContentRoot must be a canonical Unreal content path below /Game/: %s"),
            *ContentRoot);
        return false;
    }

    IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();
    AssetRegistry.WaitForCompletion();

    FARFilter Filter;
    Filter.PackagePaths.Add(FName(*ContentRoot));
    Filter.bRecursivePaths = true;
    Filter.bIncludeOnlyOnDiskAssets = true;

    TArray<FAssetData> InitialAssets;
    if (!AssetRegistry.GetAssets(Filter, InitialAssets, true))
    {
        OutError = TEXT("AssetRegistry failed to query ALS content roots.");
        return false;
    }

    TArray<FAssetData> SelectedAssets;
    TSet<FString> SelectedObjectPaths;
    for (const FAssetData& AssetData : InitialAssets)
    {
        const FString ObjectPath = AssetData.GetObjectPathString();
        if (!IsExcluded(ObjectPath) && !SelectedObjectPaths.Contains(ObjectPath))
        {
            SelectedObjectPaths.Add(ObjectPath);
            SelectedAssets.Add(AssetData);
        }
    }

    for (int32 Index = 0; Index < SelectedAssets.Num(); ++Index)
    {
        const EAlsAssetKind Kind = Classify(SelectedAssets[Index]);
        if (!ShouldInspectDependencies(Kind))
        {
            continue;
        }

        TArray<FName> DependencyPackages;
        AssetRegistry.GetDependencies(SelectedAssets[Index].PackageName, DependencyPackages,
            UE::AssetRegistry::EDependencyCategory::Package);
        for (const FName DependencyPackage : DependencyPackages)
        {
            const FString DependencyPath = DependencyPackage.ToString();
            if (!IsWithinContentRoot(DependencyPath, ContentRoot) || IsExcluded(DependencyPath + TEXT(".Asset")))
            {
                continue;
            }

            TArray<FAssetData> DependencyAssets;
            AssetRegistry.GetAssetsByPackageName(DependencyPackage, DependencyAssets, true);
            for (const FAssetData& DependencyAsset : DependencyAssets)
            {
                const FString ObjectPath = DependencyAsset.GetObjectPathString();
                if (CanBringDependencyIntoClosure(Classify(DependencyAsset)) && !SelectedObjectPaths.Contains(ObjectPath))
                {
                    SelectedObjectPaths.Add(ObjectPath);
                    SelectedAssets.Add(DependencyAsset);
                }
            }
        }
    }

    TMap<FName, TArray<FString>> IdsByPackage;
    TMap<FString, FString> ObjectPathById;
    OutAssets.Reset(SelectedAssets.Num());
    for (const FAssetData& AssetData : SelectedAssets)
    {
        FAlsExportAsset& Asset = OutAssets.AddDefaulted_GetRef();
        Asset.AssetData = AssetData;
        Asset.Kind = Classify(AssetData);
        const FString ObjectPath = AssetData.GetObjectPathString();
        Asset.Id = FAlsStableAssetId::Create(ObjectPath);
        if (!MakeOutputPath(Asset.Kind, AssetData, ContentRoot, Asset.OutputPath, OutError))
        {
            return false;
        }

        if (const FString* ExistingPath = ObjectPathById.Find(Asset.Id); ExistingPath && *ExistingPath != ObjectPath)
        {
            OutError = FString::Printf(TEXT("Stable ID collision: %s and %s"), **ExistingPath, *ObjectPath);
            return false;
        }
        ObjectPathById.Add(Asset.Id, ObjectPath);
        IdsByPackage.FindOrAdd(AssetData.PackageName).Add(Asset.Id);
    }

    for (FAlsExportAsset& Asset : OutAssets)
    {
        TArray<FName> DependencyPackages;
        AssetRegistry.GetDependencies(Asset.AssetData.PackageName, DependencyPackages,
            UE::AssetRegistry::EDependencyCategory::Package);
        TSet<FString> InternalIds;
        TSet<FString> ExternalPackages;
        for (const FName DependencyPackage : DependencyPackages)
        {
            if (const TArray<FString>* DependencyIds = IdsByPackage.Find(DependencyPackage))
            {
                for (const FString& DependencyId : *DependencyIds)
                {
                    if (DependencyId != Asset.Id)
                    {
                        InternalIds.Add(DependencyId);
                    }
                }
            }
            else if (DependencyPackage.ToString().StartsWith(TEXT("/Engine/")))
            {
                ExternalPackages.Add(DependencyPackage.ToString());
            }
        }
        Asset.Dependencies = InternalIds.Array();
        Asset.ExternalDependencies = ExternalPackages.Array();
        Asset.Dependencies.Sort();
        Asset.ExternalDependencies.Sort();
    }

    OutAssets.Sort([](const FAlsExportAsset& Left, const FAlsExportAsset& Right)
    {
        return Left.Id < Right.Id;
    });
    return true;
}

EAlsAssetKind FAlsAssetDiscovery::Classify(const FAssetData& AssetData)
{
    const FString ClassName = AssetData.AssetClassPath.GetAssetName().ToString();
    if (ClassName == TEXT("Skeleton")) return EAlsAssetKind::Skeleton;
    if (ClassName == TEXT("SkeletalMesh")) return EAlsAssetKind::SkeletalMesh;
    if (ClassName == TEXT("StaticMesh")) return EAlsAssetKind::StaticMesh;
    if (ClassName == TEXT("AnimSequence")) return EAlsAssetKind::AnimationSequence;
    if (ClassName == TEXT("AnimMontage")) return EAlsAssetKind::AnimMontage;
    if (ClassName.Contains(TEXT("AimOffset"))) return EAlsAssetKind::AimOffset;
    if (ClassName.Contains(TEXT("BlendSpace")))
    {
        return AssetData.GetObjectPathString().Contains(TEXT("/AimOffsets/")) ? EAlsAssetKind::AimOffset : EAlsAssetKind::BlendSpace;
    }
    if (ClassName == TEXT("PhysicsAsset")) return EAlsAssetKind::PhysicsAsset;
    if (ClassName == TEXT("Material")) return EAlsAssetKind::Material;
    if (ClassName.Contains(TEXT("MaterialInstance"))) return EAlsAssetKind::MaterialInstance;
    if (ClassName.StartsWith(TEXT("Texture"))) return EAlsAssetKind::Texture;
    if (ClassName.StartsWith(TEXT("Curve"))) return EAlsAssetKind::Curve;
    if (ClassName == TEXT("DataTable")) return EAlsAssetKind::DataTable;
    if (ClassName.Contains(TEXT("Blueprint")) || ClassName == TEXT("AnimBlueprint")) return EAlsAssetKind::Blueprint;
    return EAlsAssetKind::OtherConfig;
}

bool FAlsAssetDiscovery::IsExcluded(const FString& ObjectPath)
{
    static const TCHAR* ExcludedSegments[] = {
        TEXT("/Audio/"), TEXT("/Environment/"), TEXT("/Levels/"), TEXT("/UI/"),
        TEXT("/AI/"), TEXT("/GameModes/"), TEXT("/Developers/"),
    };
    for (const TCHAR* Segment : ExcludedSegments)
    {
        if (ObjectPath.Contains(Segment, ESearchCase::IgnoreCase))
        {
            return true;
        }
    }
    return false;
}
