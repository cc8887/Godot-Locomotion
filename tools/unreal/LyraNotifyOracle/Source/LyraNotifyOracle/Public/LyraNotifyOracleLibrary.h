#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraNotifyOracleLibrary.generated.h"

UCLASS()
class LYRANOTIFYORACLE_API ULyraNotifyOracleLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="Lyra Notify Oracle")
    static FString ReadSyncQueueOwnership(UAnimSequence* Sequence);
    UFUNCTION(BlueprintCallable, Category="Lyra Notify Oracle")
    static FString ReadQueueTrace(const FString& RequestsJson);
};
