#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraAdditivesLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraAdditivesLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Original linked FullBodyAdditives state machine on ALS81. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton, const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
};
