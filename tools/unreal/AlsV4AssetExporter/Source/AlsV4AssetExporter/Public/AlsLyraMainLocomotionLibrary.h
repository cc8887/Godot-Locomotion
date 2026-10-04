#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainLocomotionLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
class UBlendSpace;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainLocomotionLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original Main machine, ten real linked roots, native Sync and mixed
     *  LocomotionSM output on ALS81. Main curve copy uses the native linked API. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
};
