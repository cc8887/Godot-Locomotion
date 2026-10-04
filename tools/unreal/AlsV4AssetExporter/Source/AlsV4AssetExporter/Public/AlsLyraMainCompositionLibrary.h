#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainCompositionLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainCompositionLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original Main nodes 3/0 and 76/72, their real compiled input handlers,
     * ALS name-mapped mask and full data channels. Explicit source boundaries
     * exclude Slots, Aiming, inertia, SkeletalControls and final ControlRig. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
};
