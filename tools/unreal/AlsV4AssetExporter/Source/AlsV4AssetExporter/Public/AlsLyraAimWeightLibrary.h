#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraAimWeightLibrary.generated.h"
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraAimWeightLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original provider pre-graph weight function and exposed Aiming pins. No pose evaluation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
};
