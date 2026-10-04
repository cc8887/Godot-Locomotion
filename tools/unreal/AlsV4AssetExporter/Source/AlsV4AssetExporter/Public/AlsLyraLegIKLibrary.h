#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraLegIKLibrary.generated.h"
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraLegIKLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="ALS|Lyra")
    static FString ReadTrace(USkeleton* Skeleton,const TArray<UClass*>& LayerClasses,
        const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
