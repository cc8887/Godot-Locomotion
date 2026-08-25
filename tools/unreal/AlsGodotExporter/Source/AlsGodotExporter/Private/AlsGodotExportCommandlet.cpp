#include "AlsGodotExportCommandlet.h"

#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "Misc/Parse.h"

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
    if (!FParse::Param(*Params, TEXT("ReadyCheck")))
    {
        UE_LOG(LogAlsGodotExporter, Error, TEXT("ALS export commandlet requires -ReadyCheck, -DryRun, or -Export."));
        return 2;
    }

    const FEngineVersion EngineVersion = FEngineVersion::Current();
    UE_LOG(LogAlsGodotExporter, Display, TEXT("GODOT_ALS_EXPORTER_READY engine=%d.%d.%d plugin=1.0.0"),
        EngineVersion.GetMajor(), EngineVersion.GetMinor(), EngineVersion.GetPatch());
    return 0;
}
