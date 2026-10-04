#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWholeMainOracleLibrary.generated.h"
UCLASS()
class LYRAWHOLEMAINORACLE_API ULyraWholeMainOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Default Main")
    static FString ReadDefaultMain(const FString& RequestsJson);
};
