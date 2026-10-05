#include "AlsV4AssetExportCommandlet.h"

#include "AlsAnimationMetadataReader.h"
#include "AlsAssetDiscovery.h"
#include "AlsCompositeAssetReader.h"
#include "AlsExportPlanner.h"
#include "AlsFbxExporter.h"
#include "AlsManifestWriter.h"
#include "AlsOutputAuditor.h"
#include "AlsTextureExporter.h"
#include "Misc/EngineVersion.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogAlsV4AssetExporter, Log, All);

UAlsV4AssetExportCommandlet::UAlsV4AssetExportCommandlet()
{
    IsClient = false;
    IsEditor = true;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UAlsV4AssetExportCommandlet::Main(const FString& Params)
{
    const FEngineVersion Version = FEngineVersion::Current();
    if (Version.GetMajor() != 5 || Version.GetMinor() != 8)
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("ALS V4 asset exporter requires UE 5.8."));
        return 2;
    }

    if (FParse::Param(*Params, TEXT("ReadyCheck")))
    {
        int32 CurveCases = 0;
        int32 CompositeCases = 0;
        FString Error;
        if (!FAlsAnimationMetadataReader::RunCurveKeySelfTest(CurveCases, Error) ||
            !FAlsCompositeAssetReader::RunSelfTest(CompositeCases, Error))
        {
            UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Asset exporter self-test failed: %s"), *Error);
            return 3;
        }
        UE_LOG(LogAlsV4AssetExporter, Display, TEXT("GODOT_ALS_EXPORTER_READY"));
        return 0;
    }

    const bool bDryRun = FParse::Param(*Params, TEXT("DryRun"));
    const bool bExport = FParse::Param(*Params, TEXT("Export"));
    if (bDryRun == bExport)
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Specify exactly one of -DryRun or -Export."));
        return 2;
    }

    FString OutputDirectory;
    if (!FParse::Value(*Params, TEXT("Output="), OutputDirectory) ||
        OutputDirectory.IsEmpty() || FPaths::IsRelative(OutputDirectory))
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("An absolute -Output path is required."));
        return 2;
    }
    OutputDirectory = FPaths::ConvertRelativePathToFull(OutputDirectory);

    FString ContentRoot = TEXT("/Game/AdvancedLocomotionV4");
    FParse::Value(*Params, TEXT("ContentRoot="), ContentRoot);

    TArray<FAlsExportAsset> Assets;
    FString Error;
    if (!FAlsAssetDiscovery::Discover(ContentRoot, Assets, Error))
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Asset discovery failed: %s"), *Error);
        return 3;
    }

    int32 ExportableCount = 0;
    int32 ConfigCount = 0;
    if (!FAlsExportPlanner::Write(OutputDirectory, Assets, ExportableCount, ConfigCount, Error) ||
        !FAlsManifestWriter::WritePlanned(OutputDirectory, ContentRoot, Assets, Error))
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Asset plan failed: %s"), *Error);
        return 4;
    }

    UE_LOG(LogAlsV4AssetExporter, Display, TEXT("GODOT_ALS_P2A_PLAN_OK assets=%d exportable=%d config=%d excluded=0"),
        Assets.Num(), ExportableCount, ConfigCount);
    if (bDryRun)
    {
        return 0;
    }

    int32 FbxFileCount = 0;
    int32 TextureFileCount = 0;
    TArray<FString> NormalizedFbxKeys;
    if (!FAlsFbxExporter::Export(OutputDirectory, Assets, FbxFileCount, NormalizedFbxKeys, Error) ||
        !FAlsTextureExporter::Export(OutputDirectory, Assets, TextureFileCount, Error))
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Asset export failed: %s"), *Error);
        return 5;
    }

    TArray<FAlsExportFile> Files;
    if (!FAlsOutputAuditor::Audit(OutputDirectory, Assets, NormalizedFbxKeys, Files, Error) ||
        !FAlsManifestWriter::WriteComplete(OutputDirectory, ContentRoot, Assets, Files, Error))
    {
        UE_LOG(LogAlsV4AssetExporter, Error, TEXT("Output audit failed: %s"), *Error);
        return 6;
    }

    UE_LOG(LogAlsV4AssetExporter, Display,
        TEXT("GODOT_ALS_P2A_EXPORT_OK assets=%d files=%d fbx=%d textures=%d warnings=0"),
        Assets.Num(), Files.Num(), FbxFileCount, TextureFileCount);
    return 0;
}
