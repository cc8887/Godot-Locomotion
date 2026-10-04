#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraNotifyDispatchOracleLibrary.generated.h"
UCLASS()
class LYRANOTIFYDISPATCHORACLE_API ULyraNotifyDispatchOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Notify Dispatch") static FString ReadPolicy(const FString& ClassesJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Notify Dispatch") static FString ReadTrace(const FString& RequestsJson);
};
UCLASS()
class ULyraNamedNotifyObserver final:public UObject
{
    GENERATED_BODY()
public:
    TArray<FName> Events;
    UFUNCTION() void AnimNotify_SaveAttack(){Events.Add(TEXT("SaveAttack"));}
    UFUNCTION() void AnimNotify_ResetCombo(){Events.Add(TEXT("ResetCombo"));}
};
