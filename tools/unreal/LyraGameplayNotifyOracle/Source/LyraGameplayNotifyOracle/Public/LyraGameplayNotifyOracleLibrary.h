#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraGameplayNotifyOracleLibrary.generated.h"
UCLASS()
class LYRAGAMEPLAYNOTIFYORACLE_API ULyraGameplayNotifyOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Gameplay Notify Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
