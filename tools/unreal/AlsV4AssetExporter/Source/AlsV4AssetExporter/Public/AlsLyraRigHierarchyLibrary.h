#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraRigHierarchyLibrary.generated.h"

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraRigHierarchyLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadHierarchy(UClass* RigClass, const FString& RequestsJson);
};
