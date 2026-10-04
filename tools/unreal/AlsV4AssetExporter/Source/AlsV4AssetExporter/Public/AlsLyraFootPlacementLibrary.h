#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraFootPlacementLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraFootPlacementLibrary:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="ALS Export")
    static FString ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UClass*>& LayerClasses,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
