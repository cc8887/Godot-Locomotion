#pragma once
#include "Animation/AnimInstance.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraProxyPhaseOracleLibrary.generated.h"

// Records entry into the original Proxy worker gate; no Blueprint worker math.
UCLASS(Transient)
class LYRAPROXYPHASEORACLE_API ULyraProxyProbeInstance final:public UAnimInstance
{
    GENERATED_BODY()
public:
    int32 WorkerCalls=0;
    virtual void NativeThreadSafeUpdateAnimation(float DeltaSeconds) override
    {Super::NativeThreadSafeUpdateAnimation(DeltaSeconds);++WorkerCalls;}
};

UCLASS()
class LYRAPROXYPHASEORACLE_API ULyraProxyPhaseOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadProxyPhases(const FString& RequestsJson);
};
