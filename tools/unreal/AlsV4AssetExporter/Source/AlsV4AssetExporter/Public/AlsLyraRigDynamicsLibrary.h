#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraRigDynamicsLibrary.generated.h"

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraRigDynamicsLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(const FString& RequestsJson);
};
