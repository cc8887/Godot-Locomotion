#include "AlsManifestWriter.h"

#include "AlsAnimationMetadataReader.h"
#include "AlsCompositeAssetReader.h"
#include "AlsMaterialMetadataReader.h"
#include "AlsRigMetadataReader.h"
#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
    bool ReadMetadata(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& Metadata, FString& OutError)
    {
        Metadata->SetBoolField(TEXT("overlay"), Asset.AssetData.GetObjectPathString().Contains(TEXT("/Overlay/")));
        Metadata->SetBoolField(TEXT("prop"), Asset.AssetData.GetObjectPathString().Contains(TEXT("/Props/")));
        switch (Asset.Kind)
        {
        case EAlsAssetKind::Skeleton:
        case EAlsAssetKind::SkeletalMesh:
        case EAlsAssetKind::StaticMesh:
        case EAlsAssetKind::PhysicsAsset:
            return FAlsRigMetadataReader::Read(Asset, Metadata, OutError);
        case EAlsAssetKind::AnimationSequence:
            return FAlsAnimationMetadataReader::Read(Asset, Metadata, OutError);
        case EAlsAssetKind::AnimMontage:
        case EAlsAssetKind::BlendSpace:
        case EAlsAssetKind::AimOffset:
            return FAlsCompositeAssetReader::Read(Asset, Metadata, OutError);
        case EAlsAssetKind::Material:
        case EAlsAssetKind::MaterialInstance:
        case EAlsAssetKind::Texture:
            return FAlsMaterialMetadataReader::Read(Asset, Metadata, OutError);
        default:
            Metadata->SetNumberField(TEXT("assetRegistryTagCount"), Asset.AssetData.TagsAndValues.Num());
            Metadata->SetStringField(TEXT("assetClass"), Asset.AssetData.AssetClassPath.ToString());
            return true;
        }
    }

    FString SerializeMetadata(const TSharedRef<FJsonObject>& Metadata)
    {
        FString Json;
        const TSharedRef<TJsonWriter<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>> Writer =
            TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json);
        FJsonSerializer::Serialize(Metadata, Writer);
        return Json;
    }

    void WriteAsset(const TSharedRef<TJsonWriter<>>& Writer, const FAlsExportAsset& Asset,
        const TMap<FString, TSharedRef<FJsonObject>>& MetadataById)
    {
        Writer->WriteObjectStart();
        Writer->WriteValue(TEXT("id"), Asset.Id);
        Writer->WriteValue(TEXT("objectPath"), Asset.AssetData.GetObjectPathString());
        Writer->WriteValue(TEXT("packagePath"), Asset.AssetData.PackageName.ToString());
        Writer->WriteValue(TEXT("assetName"), Asset.AssetData.AssetName.ToString());
        Writer->WriteValue(TEXT("classPath"), Asset.AssetData.AssetClassPath.ToString());
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
        Writer->WriteRawJSONValue(TEXT("metadata"), SerializeMetadata(MetadataById.FindChecked(Asset.Id)));
        Writer->WriteObjectEnd();
    }

    void WriteAssetArray(const TSharedRef<TJsonWriter<>>& Writer, const TCHAR* FieldName,
        const TArray<FAlsExportAsset>& Assets, const TMap<FString, TSharedRef<FJsonObject>>& MetadataById,
        const TFunctionRef<bool(EAlsAssetKind)> Predicate)
    {
        Writer->WriteArrayStart(FieldName);
        for (const FAlsExportAsset& Asset : Assets)
        {
            if (Predicate(Asset.Kind))
            {
                WriteAsset(Writer, Asset, MetadataById);
            }
        }
        Writer->WriteArrayEnd();
    }
}

namespace
{
bool WriteManifest(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    const TArray<FAlsExportFile>& Files, const TCHAR* Status, const bool bPublishFormal, FString& OutError)
{
    TMap<FString, TSharedRef<FJsonObject>> MetadataById;
    for (const FAlsExportAsset& Asset : Assets)
    {
        TSharedRef<FJsonObject> Metadata = MakeShared<FJsonObject>();
        if (!ReadMetadata(Asset, Metadata, OutError))
        {
            return false;
        }
        MetadataById.Add(Asset.Id, Metadata);
    }

    FString Json;
    const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Json);
    Writer->WriteObjectStart();
    Writer->WriteValue(TEXT("schemaVersion"), 1);
    Writer->WriteValue(TEXT("exporterVersion"), TEXT("1.0.0"));
    Writer->WriteValue(TEXT("sourceEngineVersion"), FEngineVersion::Current().ToString());
    Writer->WriteValue(TEXT("sourceProjectId"), FApp::GetProjectName());
    Writer->WriteValue(TEXT("sourceContentRoot"), TEXT("/Game/AdvancedLocomotionV4"));
    Writer->WriteObjectStart(TEXT("coordinateSystem"));
    Writer->WriteValue(TEXT("sourceHandedness"), TEXT("left"));
    Writer->WriteValue(TEXT("sourceUpAxis"), TEXT("Z"));
    Writer->WriteValue(TEXT("targetHandedness"), TEXT("right"));
    Writer->WriteValue(TEXT("targetUpAxis"), TEXT("Y"));
    Writer->WriteObjectEnd();
    Writer->WriteValue(TEXT("unitScale"), 0.01);

    WriteAssetArray(Writer, TEXT("skeletons"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::Skeleton; });
    WriteAssetArray(Writer, TEXT("skeletalMeshes"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::SkeletalMesh; });
    WriteAssetArray(Writer, TEXT("staticMeshes"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::StaticMesh; });
    WriteAssetArray(Writer, TEXT("animations"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::AnimationSequence; });
    WriteAssetArray(Writer, TEXT("montages"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::AnimMontage; });
    WriteAssetArray(Writer, TEXT("blendSpaces"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::BlendSpace; });
    WriteAssetArray(Writer, TEXT("aimOffsets"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::AimOffset; });
    WriteAssetArray(Writer, TEXT("materials"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::Material || Kind == EAlsAssetKind::MaterialInstance; });
    WriteAssetArray(Writer, TEXT("textures"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::Texture; });
    WriteAssetArray(Writer, TEXT("physicsAssets"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::PhysicsAsset; });
    WriteAssetArray(Writer, TEXT("curves"), Assets, MetadataById, [](EAlsAssetKind Kind) { return Kind == EAlsAssetKind::Curve; });
    WriteAssetArray(Writer, TEXT("configAssets"), Assets, MetadataById, [](EAlsAssetKind Kind)
    {
        return Kind == EAlsAssetKind::DataTable || Kind == EAlsAssetKind::Blueprint || Kind == EAlsAssetKind::OtherConfig;
    });
    Writer->WriteArrayStart(TEXT("files"));
    for (const FAlsExportFile& File : Files)
    {
        Writer->WriteObjectStart();
        Writer->WriteValue(TEXT("relativePath"), File.RelativePath);
        Writer->WriteValue(TEXT("sha256"), File.Sha256);
        Writer->WriteValue(TEXT("size"), File.Size);
        Writer->WriteObjectEnd();
    }
    Writer->WriteArrayEnd();
    Writer->WriteObjectStart(TEXT("auditSummary"));
    Writer->WriteValue(TEXT("status"), Status);
    Writer->WriteValue(TEXT("assetCount"), Assets.Num());
    Writer->WriteValue(TEXT("fileCount"), Files.Num());
    Writer->WriteValue(TEXT("errorCount"), 0);
    Writer->WriteValue(TEXT("warningCount"), 0);
    Writer->WriteObjectEnd();
    Writer->WriteObjectEnd();
    Writer->Close();
    Json.ReplaceInline(TEXT("\r\n"), TEXT("\n"));
    Json.AppendChar(TEXT('\n'));

    const FString PartialDirectory = FPaths::Combine(OutputDirectory, TEXT("partial"));
    if (!IFileManager::Get().MakeDirectory(*PartialDirectory, true))
    {
        OutError = FString::Printf(TEXT("Unable to create partial manifest directory: %s"), *PartialDirectory);
        return false;
    }
    const FString FinalPath = FPaths::Combine(PartialDirectory, TEXT("als_manifest.partial.json"));
    const FString TemporaryPath = FinalPath + TEXT(".tmp");
    if (!FFileHelper::SaveStringToFile(Json, *TemporaryPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
    {
        OutError = FString::Printf(TEXT("Unable to write partial manifest: %s"), *TemporaryPath);
        return false;
    }
    if (!IFileManager::Get().Move(*FinalPath, *TemporaryPath, true, true, false, true))
    {
        OutError = FString::Printf(TEXT("Unable to publish partial manifest: %s"), *FinalPath);
        return false;
    }
    if (bPublishFormal)
    {
        const FString FormalPath = FPaths::Combine(OutputDirectory, TEXT("als_manifest.json"));
        const FString FormalTemporaryPath = FormalPath + TEXT(".tmp");
        if (!FFileHelper::SaveStringToFile(Json, *FormalTemporaryPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM) ||
            !IFileManager::Get().Move(*FormalPath, *FormalTemporaryPath, true, true, false, true))
        {
            OutError = FString::Printf(TEXT("Unable to publish formal manifest: %s"), *FormalPath);
            return false;
        }
    }
    return true;
}
}

bool FAlsManifestWriter::WritePlanned(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets, FString& OutError)
{
    return WriteManifest(OutputDirectory, Assets, {}, TEXT("planned"), false, OutError);
}

bool FAlsManifestWriter::WriteComplete(const FString& OutputDirectory, const TArray<FAlsExportAsset>& Assets,
    const TArray<FAlsExportFile>& Files, FString& OutError)
{
    return WriteManifest(OutputDirectory, Assets, Files, TEXT("complete"), true, OutError);
}
