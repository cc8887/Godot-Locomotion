#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraAirLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraAirLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Five original Air providers, one linked instance and actual Main Sync, ALS81 pose evaluation. */
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadAirTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
