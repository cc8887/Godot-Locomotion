#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraEmoteOracleLibrary.generated.h"
UCLASS()
class LYRAEMOTEORACLE_API ULyraEmoteOracleLibrary:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Read Only") static FString ReadPolicy();
    UFUNCTION(BlueprintCallable,Category="Lyra Read Only") static FString ReadTrace(const FString& RequestsJson);
};
UCLASS()
class ULyraEmoteProbeListener:public UObject
{
    GENERATED_BODY()
public:
    int32 Calls=0;
    UFUNCTION() void Movement(float DeltaSeconds,FVector OldLocation,FVector OldVelocity){++Calls;}
};
