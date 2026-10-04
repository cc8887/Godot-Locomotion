#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainCachePoseLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainCachePoseLibrary:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original three Main/provider caches with an ALS81 sequence input.
     * Capture complete data, scope lifetime and repeated-read sampling counts. */
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
