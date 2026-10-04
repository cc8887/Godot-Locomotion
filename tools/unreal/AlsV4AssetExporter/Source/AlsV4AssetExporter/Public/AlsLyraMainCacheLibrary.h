#pragma once
#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainCacheLibrary.generated.h"

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainCacheLibrary : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Actual Main/FullBody_Aiming Save/UseCachedPose nodes. Controlled Slot
     * source contexts; update traversal only, not a complete Main oracle. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTrace(UClass* MainClass, const FString& RequestsJson);
};
