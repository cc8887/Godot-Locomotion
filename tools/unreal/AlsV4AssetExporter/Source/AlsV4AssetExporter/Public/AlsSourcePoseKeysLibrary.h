#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsSourcePoseKeysLibrary.generated.h"

class UAnimSequence;

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsSourcePoseKeysLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourcePoseKeys(UAnimSequence* Animation);
};
