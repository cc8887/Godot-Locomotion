#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMontageLibrary.generated.h"
class UAnimMontage;
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMontageLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadCatalog(const TArray<UAnimMontage*>& Montages);
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadSlotUpdateTrace(UClass* MainClass,USkeletalMesh* Mesh,
        const TArray<UAnimMontage*>& Montages,const FString& RequestsJson);
};
