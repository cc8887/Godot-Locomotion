#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraAimingLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraAimingLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& AimSequences, const TArray<UAnimSequence*>& BaseSequences, const FString& RequestsJson);
};
