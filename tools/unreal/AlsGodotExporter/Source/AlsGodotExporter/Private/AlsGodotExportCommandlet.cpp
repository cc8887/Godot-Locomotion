#include "AlsGodotExportCommandlet.h"

#include "AlsAssetDiscovery.h"
#include "AlsExportPlanner.h"
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
        const FEngineVersion EngineVersion = FEngineVersion::Current();
        UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_EXPORTER_READY engine=%d.%d.%d plugin=1.0.0"),
            EngineVersion.GetMajor(), EngineVersion.GetMinor(), EngineVersion.GetPatch());
        return 0;
    }

    if (!FParse::Param(*Params, TEXT("DryRun")))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("ALS export commandlet requires -ReadyCheck, -DryRun, or -Export."));
        return 2;
    }

    FString OutputDirectory;
    if (!FParse::Value(*Params, TEXT("Output="), OutputDirectory) || OutputDirectory.IsEmpty() || FPaths::IsRelative(OutputDirectory))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("-DryRun requires an absolute -Output path."));
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

    UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_P2A_PLAN_OK assets=%d exportable=%d config=%d excluded=0"),
        Assets.Num(), ExportableCount, ConfigCount);
    return 0;
}
