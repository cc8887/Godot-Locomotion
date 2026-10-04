#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainSlotCompositionDebugLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimMontage;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainSlotCompositionDebugLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimMontage*>& Montages, const TArray<UAnimSequence*>& Sequences, const TArray<UAnimSequence*>& RecoverySequences, const FString& RequestsJson);
};
