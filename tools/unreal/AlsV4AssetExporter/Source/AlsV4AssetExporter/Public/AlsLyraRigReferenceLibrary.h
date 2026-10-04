#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraRigReferenceLibrary.generated.h"
class USkeleton;

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraRigReferenceLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadReference(UClass* RigClass, USkeleton* Skeleton);
};
