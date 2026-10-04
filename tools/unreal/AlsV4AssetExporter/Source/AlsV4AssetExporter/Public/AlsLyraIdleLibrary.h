#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraIdleLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraIdleLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadIdleTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
