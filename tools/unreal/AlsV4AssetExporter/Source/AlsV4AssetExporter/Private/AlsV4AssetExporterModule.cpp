#include "Modules/ModuleManager.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "AlsLyraControlRigLibrary.h"

class FAlsV4AssetExporterModule final : public IModuleInterface
{
public:
    void StartupModule() override
    {
        if (IsRunningCommandlet() && FParse::Param(FCommandLine::Get(), TEXT("AlsRawTrackDataModel")))
        {
            // Register after the Sequencer provider, before project CDOs load
            // animation assets. Its serialized classes must remain available.
            FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationData"));
            check(UAlsLyraControlRigLibrary::PreferRawTrackDataModel(true));
            UE_LOG(LogTemp, Display, TEXT("LYRA_RAW_TRACK_DATA_MODEL_STARTUP"));
        }
    }
    void ShutdownModule() override
    { if (IsRunningCommandlet()) UAlsLyraControlRigLibrary::PreferRawTrackDataModel(false); }
};
IMPLEMENT_MODULE(FAlsV4AssetExporterModule, AlsV4AssetExporter)
