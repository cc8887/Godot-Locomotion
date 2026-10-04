#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraStartupOracleLibrary.generated.h"
UCLASS()
class LYRASTARTUPORACLE_API ULyraStartupOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Startup")
    static FString ReadStartup(const FString& RequestsJson);
};
