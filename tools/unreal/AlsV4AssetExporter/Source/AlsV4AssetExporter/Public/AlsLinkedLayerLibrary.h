#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLinkedLayerLibrary.generated.h"

class USkeletalMesh;

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLinkedLayerLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadLinkedLayerClass(UClass* AnimationClass);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadLinkedLayerBindingSequence(UClass* MainClass, USkeletalMesh* Mesh,
                                                  const TArray<UClass*>& LayerClasses);
};
