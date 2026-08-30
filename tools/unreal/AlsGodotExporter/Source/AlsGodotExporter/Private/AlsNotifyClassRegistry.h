#pragma once

#include "AlsExportTypes.h"

struct FAnimNotifyEvent;

class FAlsNotifyClassRegistry
{
public:
    static bool Export(
        const FAnimNotifyEvent& NotifyEvent,
        const FString& AssetStableId,
        int32 SourceIndex,
        FAlsExportedTimelineEntry& OutEntry,
        FString& OutError);
};
