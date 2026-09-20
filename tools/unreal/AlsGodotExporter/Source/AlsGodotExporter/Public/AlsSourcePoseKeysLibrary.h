#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsSourcePoseKeysLibrary.generated.h"

class UAnimSequence;

/** Read source track channels without pose evaluation, retargeting, or asset writes. */
UCLASS()
class ALSGODOTEXPORTER_API UAlsSourcePoseKeysLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourcePoseKeys(UAnimSequence* Animation);
};
