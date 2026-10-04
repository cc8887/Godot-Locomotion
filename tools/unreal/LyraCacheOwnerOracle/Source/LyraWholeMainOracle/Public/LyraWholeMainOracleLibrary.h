#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWholeMainOracleLibrary.generated.h"
UCLASS()
class LYRAWHOLEMAINORACLE_API ULyraWholeMainOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadLayerFallback(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadMainPhases(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadGraphPhases(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadSequenceInitialization(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadBlendSpaceInitialization(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadSkeletalInitialization(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Layer")
    static FString ReadCacheLifecycle(const FString& RequestsJson);
};
