#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraIdleRuleLibrary.generated.h"
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraIdleRuleLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadIdleRules(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson);
};
