#include "AlsTextureExporter.h"

#include "AssetExportTask.h"
#include "Exporters/Exporter.h"
#include "Exporters/TextureExporterPNG.h"
#include "HAL/FileManager.h"
#include "Misc/Paths.h"

bool FAlsTextureExporter::Export(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    int32& OutFileCount, FString& OutError)
{
    OutFileCount = 0;
    for (const FAlsExportAsset& Asset : Assets)
    {
        if (Asset.Kind != EAlsAssetKind::Texture)
        {
            continue;
        }
        UObject* Object = Asset.AssetData.GetAsset();
        if (!Object)
        {
            OutError = FString::Printf(TEXT("Unable to load texture export asset: %s"), *Asset.AssetData.GetObjectPathString());
            return false;
        }

        const FString Filename = FPaths::Combine(OutputDirectory, Asset.OutputPath);
        if (!IFileManager::Get().MakeDirectory(*FPaths::GetPath(Filename), true))
        {
            OutError = FString::Printf(TEXT("Unable to create texture output directory: %s"), *FPaths::GetPath(Filename));
            return false;
        }
        UAssetExportTask* Task = NewObject<UAssetExportTask>();
        Task->Object = Object;
        Task->Exporter = NewObject<UTextureExporterPNG>();
        Task->Filename = Filename;
        Task->bSelected = false;
        Task->bReplaceIdentical = true;
        Task->bPrompt = false;
        Task->bAutomated = true;
        Task->bUseFileArchive = true;
        Task->bWriteEmptyFiles = false;
        if (!UExporter::RunAssetExportTask(Task))
        {
            OutError = FString::Printf(TEXT("PNG export failed for %s: %s"),
                *Asset.AssetData.GetObjectPathString(), *FString::Join(Task->Errors, TEXT("; ")));
            return false;
        }
        ++OutFileCount;
    }
    return true;
}
