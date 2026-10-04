#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraRigIkMathLibrary.generated.h"

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraRigIkMathLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(const FString& RequestsJson);
};
