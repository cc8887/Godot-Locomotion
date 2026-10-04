#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraRootMovementOracleLibrary.generated.h"
UCLASS()
class LYRAROOTMOVEMENTORACLE_API ULyraRootMovementOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Root Movement Oracle")
    static FString ReadTrace(const FString& RequestsJson);
};
