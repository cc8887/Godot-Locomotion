#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraFootPlantBindingLibrary.generated.h"
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraFootPlantBindingLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadBindings(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
};
