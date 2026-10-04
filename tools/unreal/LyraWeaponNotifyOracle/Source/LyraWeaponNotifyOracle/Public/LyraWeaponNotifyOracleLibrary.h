#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWeaponNotifyOracleLibrary.generated.h"
UCLASS()
class LYRAWEAPONNOTIFYORACLE_API ULyraWeaponNotifyOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Weapon Notify Oracle")
    static FString ReadPolicy();
    UFUNCTION(BlueprintCallable,Category="Lyra Weapon Notify Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
