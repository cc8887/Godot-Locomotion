#include "AlsExportPlanner.h"

#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonWriter.h"

bool FAlsExportPlanner::Write(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    int32& OutExportableCount, int32& OutConfigCount, FString& OutError)
{
    IFileManager& FileManager = IFileManager::Get();
    if (!FileManager.MakeDirectory(*OutputDirectory, true))
    {
        OutError = FString::Printf(TEXT("Unable to create output directory: %s"), *OutputDirectory);
        return false;
    }

    OutExportableCount = 0;
    OutConfigCount = 0;
    FString Json;
    const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Json);
    Writer->WriteObjectStart();
    Writer->WriteValue(TEXT("schemaVersion"), 1);
    Writer->WriteArrayStart(TEXT("assets"));
    for (const FAlsExportAsset& Asset : Assets)
    {
        if (AlsAssetKindIsExportable(Asset.Kind))
        {
            ++OutExportableCount;
        }
        else
        {
            ++OutConfigCount;
        }

        Writer->WriteObjectStart();
        Writer->WriteValue(TEXT("id"), Asset.Id);
        Writer->WriteValue(TEXT("objectPath"), Asset.AssetData.GetObjectPathString());
        Writer->WriteValue(TEXT("packagePath"), Asset.AssetData.PackageName.ToString());
        Writer->WriteValue(TEXT("classPath"), Asset.AssetData.AssetClassPath.ToString());
        Writer->WriteValue(TEXT("kind"), AlsAssetKindToString(Asset.Kind));
        if (Asset.OutputPath.IsEmpty())
        {
            Writer->WriteNull(TEXT("outputPath"));
        }
        else
        {
            Writer->WriteValue(TEXT("outputPath"), Asset.OutputPath);
        }
        Writer->WriteArrayStart(TEXT("dependencies"));
        for (const FString& Dependency : Asset.Dependencies)
        {
            Writer->WriteValue(Dependency);
        }
        Writer->WriteArrayEnd();
        Writer->WriteArrayStart(TEXT("externalDependencies"));
        for (const FString& Dependency : Asset.ExternalDependencies)
        {
            Writer->WriteValue(Dependency);
        }
        Writer->WriteArrayEnd();
        Writer->WriteObjectEnd();
    }
    Writer->WriteArrayEnd();
    Writer->WriteObjectStart(TEXT("summary"));
    Writer->WriteValue(TEXT("assetCount"), Assets.Num());
    Writer->WriteValue(TEXT("exportableCount"), OutExportableCount);
    Writer->WriteObjectEnd();
    Writer->WriteObjectEnd();
    Writer->Close();
    Json.ReplaceInline(TEXT("\r\n"), TEXT("\n"));
    Json.AppendChar(TEXT('\n'));

    const FString FinalPath = FPaths::Combine(OutputDirectory, TEXT("export_plan.json"));
    const FString TemporaryPath = FinalPath + TEXT(".tmp");
    if (!FFileHelper::SaveStringToFile(Json, *TemporaryPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        OutError = FString::Printf(TEXT("Unable to write temporary export plan: %s"), *TemporaryPath);
        return false;
    }
    if (!FileManager.Move(*FinalPath, *TemporaryPath, true, true, false, true))
    {
        OutError = FString::Printf(TEXT("Unable to publish export plan: %s"), *FinalPath);
        return false;
    }
    return true;
}
