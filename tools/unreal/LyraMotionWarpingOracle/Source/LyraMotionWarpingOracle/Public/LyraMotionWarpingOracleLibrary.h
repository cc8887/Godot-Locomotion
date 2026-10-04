#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraMotionWarpingOracleLibrary.generated.h"
UCLASS()
class LYRAMOTIONWARPINGORACLE_API ULyraMotionWarpingOracleLibrary:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Read Only") static FString ReadUsage();
    UFUNCTION(BlueprintCallable,Category="Lyra Read Only") static FString ReadTrace(const FString& RequestsJson);
};
