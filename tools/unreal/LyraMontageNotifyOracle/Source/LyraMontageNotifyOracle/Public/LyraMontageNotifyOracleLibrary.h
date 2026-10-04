#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraMontageNotifyOracleLibrary.generated.h"
UCLASS()
class LYRAMONTAGENOTIFYORACLE_API ULyraMontageNotifyOracleLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Montage Notify Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
