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

    FString MakeOutputPath(const EAlsAssetKind Kind, const FString& Id)
    {
        switch (Kind)
        {
        case EAlsAssetKind::SkeletalMesh:
            return FString::Printf(TEXT("meshes/skeletal/%s.fbx"), *Id);
        case EAlsAssetKind::StaticMesh:
            return FString::Printf(TEXT("meshes/static/%s.fbx"), *Id);
        case EAlsAssetKind::AnimationSequence:
            return FString::Printf(TEXT("animations/%s.fbx"), *Id);
        case EAlsAssetKind::Texture:
            return FString::Printf(TEXT("textures/%s.png"), *Id);
        default:
            return FString();
        }
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

bool FAlsAssetDiscovery::Discover(TArray<FAlsExportAsset>& OutAssets, FString& OutError)
{
    IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();
    AssetRegistry.WaitForCompletion();

    FARFilter Filter;
    Filter.PackagePaths = {
        FName(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Props")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Data")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimModifiers")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Blueprints/CameraSystem")),
        FName(TEXT("/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic")),
    };
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
            if (!DependencyPath.StartsWith(TEXT("/Game/AdvancedLocomotionV4/")) || IsExcluded(DependencyPath + TEXT(".Asset")))
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
        Asset.OutputPath = MakeOutputPath(Asset.Kind, Asset.Id);

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
