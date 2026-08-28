#include "AlsGodotExportCommandlet.h"

#include "AlsAssetDiscovery.h"
#include "AlsExportPlanner.h"
#include "AlsFbxExporter.h"
#include "AlsAnimationMetadataReader.h"
#include "AlsManifestWriter.h"
#include "AlsOutputAuditor.h"
#include "AlsTextureExporter.h"
#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"

DEFINE_LOG_CATEGORY_STATIC(LogAlsGodotExporter, Log, All);

UAlsGodotExportCommandlet::UAlsGodotExportCommandlet()
{
    IsClient = false;
    IsEditor = true;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UAlsGodotExportCommandlet::Main(const FString& Params)
{
    if (FParse::Param(*Params, TEXT("ReadyCheck")))
    {
        FString SelfTestError;
        if (!FAlsAnimationMetadataReader::RunCurveKeySelfTest(SelfTestError))
        {
            UE_LOG(LogAlsGodotExporter, Error, TEXT("Curve export self-test failed: %s"), *SelfTestError);
            return 7;
        }
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=8"));
        const FEngineVersion EngineVersion = FEngineVersion::Current();
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_EXPORTER_READY engine=%d.%d.%d plugin=1.0.0"),
            EngineVersion.GetMajor(), EngineVersion.GetMinor(), EngineVersion.GetPatch());
        return 0;
    }

    const bool bDryRun = FParse::Param(*Params, TEXT("DryRun"));
    const bool bExport = FParse::Param(*Params, TEXT("Export"));
    if (bDryRun == bExport)
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("ALS export commandlet requires -ReadyCheck, -DryRun, or -Export."));
        return 2;
    }

    FString OutputDirectory;
    if (!FParse::Value(*Params, TEXT("Output="), OutputDirectory) || OutputDirectory.IsEmpty() || FPaths::IsRelative(OutputDirectory))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("-DryRun or -Export requires an absolute -Output path."));
        return 2;
    }
    OutputDirectory = FPaths::ConvertRelativePathToFull(OutputDirectory);

    TArray<FAlsExportAsset> Assets;
    FString Error;
    if (!FAlsAssetDiscovery::Discover(Assets, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Asset discovery failed: %s"), *Error);
        return 3;
    }

    int32 ExportableCount = 0;
    int32 ConfigCount = 0;
    if (!FAlsExportPlanner::Write(OutputDirectory, Assets, ExportableCount, ConfigCount, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Export plan failed: %s"), *Error);
        return 3;
    }

    if (!FAlsManifestWriter::WritePlanned(OutputDirectory, Assets, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Metadata extraction failed: %s"), *Error);
        return 5;
    }

    UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_P2A_PLAN_OK assets=%d exportable=%d config=%d excluded=0"),
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
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Asset export failed: %s"), *Error);
        return 4;
    }

    TArray<FAlsExportFile> Files;
    if (!FAlsOutputAuditor::Audit(OutputDirectory, Assets, NormalizedFbxKeys, Files, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Output audit failed: %s"), *Error);
        return 6;
    }
    if (!FAlsManifestWriter::WriteComplete(OutputDirectory, Assets, Files, Error))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("Formal manifest publication failed: %s"), *Error);
        return 6;
    }

    UE_LOG(LogAlsGodotExporter, Display,
        TEXT("GODOT_ALS_P2A_EXPORT_OK assets=%d files=%d fbx=%d textures=%d warnings=0"),
        Assets.Num(), Files.Num(), FbxFileCount, TextureFileCount);
    return 0;
}
