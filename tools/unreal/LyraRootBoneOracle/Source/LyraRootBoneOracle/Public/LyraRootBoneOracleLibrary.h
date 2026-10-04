#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraRootBoneOracleLibrary.generated.h"
UCLASS()
class LYRAROOTBONEORACLE_API ULyraRootBoneOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Startup")
    static FString ReadRootBones(const FString& RequestsJson);
};
