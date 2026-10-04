#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWeaponMontageOracleLibrary.generated.h"
UCLASS()
class LYRAWEAPONMONTAGEORACLE_API ULyraWeaponMontageOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Weapon Montage Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
