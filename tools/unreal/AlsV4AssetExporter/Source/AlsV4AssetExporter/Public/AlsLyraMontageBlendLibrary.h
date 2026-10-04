#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMontageBlendLibrary.generated.h"
class UAnimMontage;
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMontageBlendLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadBlendTrace(UClass* MainClass, USkeletalMesh* Mesh,
        const TArray<UAnimMontage*>& Montages, const FString& RequestsJson);
};
