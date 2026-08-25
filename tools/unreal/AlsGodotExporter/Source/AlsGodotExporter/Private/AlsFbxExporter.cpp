#include "AlsFbxExporter.h"

#include "AlsFbxNormalizer.h"
#include "AssetExportTask.h"
#include "Exporters/AnimSequenceExporterFBX.h"
#include "Exporters/Exporter.h"
#include "Exporters/FbxExportOption.h"
#include "Exporters/SkeletalMeshExporterFBX.h"
#include "Exporters/StaticMeshExporterFBX.h"
#include "Editor.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "RendererInterface.h"
#include "SceneInterface.h"

namespace
{
bool EnsureEditorRenderScene(FString& OutError)
{
    if (!FApp::CanEverRender())
    {
        OutError = TEXT("Skeletal mesh FBX export requires -AllowCommandletRendering.");
        return false;
    }

    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!World)
    {
        OutError = TEXT("Skeletal mesh FBX export requires an editor world.");
        return false;
    }
    if (!World->Scene || !World->Scene->GetSkeletalMeshUpdater())
    {
        IRendererModule& Renderer = FModuleManager::LoadModuleChecked<IRendererModule>(TEXT("Renderer"));
        if (World->Scene)
        {
            World->Scene->Release();
            Renderer.RemoveScene(World->Scene);
            World->Scene = nullptr;
        }
        Renderer.AllocateScene(World, false, false, World->GetFeatureLevel());
        World->UpdateWorldComponents(false, false);
        FlushRenderingCommands();
    }
    if (!World->Scene || !World->Scene->GetSkeletalMeshUpdater())
    {
        OutError = TEXT("Unable to allocate the editor render scene required for skeletal mesh FBX export.");
        return false;
    }
    return true;
}
}

bool FAlsFbxExporter::Export(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    int32& OutFileCount, TArray<FString>& OutNormalizedKeys, FString& OutError)
{
    TGuardValue<bool> ClientModeGuard(GIsClient, true);
    OutFileCount = 0;
    OutNormalizedKeys.Reset();
    for (const FAlsExportAsset& Asset : Assets)
    {
        if (Asset.Kind != EAlsAssetKind::SkeletalMesh && Asset.Kind != EAlsAssetKind::StaticMesh &&
            Asset.Kind != EAlsAssetKind::AnimationSequence)
        {
            continue;
        }

        UObject* Object = Asset.AssetData.GetAsset();
        if (!Object)
        {
            OutError = FString::Printf(TEXT("Unable to load FBX export asset: %s"), *Asset.AssetData.GetObjectPathString());
            return false;
        }

        UExporter* Exporter = nullptr;
        if (Asset.Kind == EAlsAssetKind::SkeletalMesh)
        {
            if (!EnsureEditorRenderScene(OutError))
            {
                return false;
            }
            Exporter = NewObject<USkeletalMeshExporterFBX>();
        }
        else if (Asset.Kind == EAlsAssetKind::StaticMesh)
        {
            Exporter = NewObject<UStaticMeshExporterFBX>();
        }
        else
        {
            Exporter = NewObject<UAnimSequenceExporterFBX>();
        }

        UFbxExportOption* Options = NewObject<UFbxExportOption>();
        Options->FbxExportCompatibility = EFbxExportCompatibility::FBX_2020;
        Options->bASCII = true;
        Options->bForceFrontXAxis = true;
        Options->VertexColor = true;
        Options->LevelOfDetail = false;
        Options->Collision = false;
        Options->bExportSourceMesh = false;
        Options->bExportMorphTargets = true;
        Options->bExportPreviewMesh = false;
        Options->MapSkeletalMotionToRoot = false;

        const FString Filename = FPaths::Combine(OutputDirectory, Asset.OutputPath);
        if (!IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename), true))
        {
            OutError = FString::Printf(TEXT("Unable to create FBX output directory: %s"), *FPaths::GetPath(Filename));
            return false;
        }
        UAssetExportTask* Task = NewObject<UAssetExportTask>();
        Task->Object = Object;
        Task->Exporter = Exporter;
        Task->Filename = Filename;
        Task->bSelected = false;
        Task->bReplaceIdentical = true;
        Task->bPrompt = false;
        Task->bAutomated = true;
        Task->bUseFileArchive = true;
        Task->bWriteEmptyFiles = false;
        Task->Options = Options;
        if (!UExporter::RunAssetExportTask(Task))
        {
            OutError = FString::Printf(TEXT("FBX export failed for %s: %s"),
                *Asset.AssetData.GetObjectPathString(), *FString::Join(Task->Errors, TEXT("; ")));
            return false;
        }

        TArray<FString> ModifiedKeys;
        if (!FAlsFbxNormalizer::Normalize(Filename, ModifiedKeys, OutError))
        {
            return false;
        }
        for (const FString& Key : ModifiedKeys)
        {
            OutNormalizedKeys.AddUnique(Key);
        }
        ++OutFileCount;
    }
    OutNormalizedKeys.Sort();
    return true;
}
