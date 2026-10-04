#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWeaponEquipmentOracleLibrary.generated.h"
UCLASS()
class LYRAWEAPONEQUIPMENTORACLE_API ULyraWeaponEquipmentOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Weapon Montage Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
