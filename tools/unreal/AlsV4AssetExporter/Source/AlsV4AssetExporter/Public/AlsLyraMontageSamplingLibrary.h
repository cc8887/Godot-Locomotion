#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMontageSamplingLibrary.generated.h"
class UAnimMontage;
class UAnimSequence;
class USkeleton;
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMontageSamplingLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTimeTrace(UClass* MainClass, USkeletalMesh* Mesh,
        const TArray<UAnimMontage*>& Montages, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrackSamples(USkeleton* Skeleton, const TArray<UAnimMontage*>& Montages,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
};
