#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraLocomotionLibrary.generated.h"
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraLocomotionLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original main machine, original linked state graphs and native Sync.
     *  Captures update/selection history on the source skeleton; no pose substitute. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMachineTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
};
