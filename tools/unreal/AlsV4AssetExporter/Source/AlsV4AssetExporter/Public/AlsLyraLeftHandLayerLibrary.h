#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraLeftHandLayerLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraLeftHandLayerLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original four-node layer, real Update callback and native Main curve
     * copy, with controlled input leaves on the existing ALS81 skeleton. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
};
