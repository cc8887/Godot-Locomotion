#pragma once
#include "Animation/AnimInstance.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraNamedNotifyOracleLibrary.generated.h"
UCLASS()
class LYRANAMEDNOTIFYORACLE_API ULyraNamedNotifyOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Named Notify") static FString ReadTrace(const FString& RequestsJson);
};
UCLASS()
class ULyraNamedOracleObserver final:public UObject
{
    GENERATED_BODY()
public:
    TFunction<void(FName)> Invoke;
    UFUNCTION() void AnimNotify_SaveAttack(){Invoke(TEXT("SaveAttack"));}
    UFUNCTION() void AnimNotify_ResetCombo(){Invoke(TEXT("ResetCombo"));}
};
// Controlled class methods test the engine dispatcher; original generated
// Lyra classes are loaded separately and never extended or saved.
UCLASS()
class ULyraNamedOracleInstance final:public UAnimInstance
{
    GENERATED_BODY()
public:
    TFunction<void(FName,bool)> Invoke;
    bool Intercept=false;
    virtual bool HandleNotify(const FAnimNotifyEvent& Event) override{return Intercept;}
    UFUNCTION() void AnimNotify_SaveAttack(){Invoke(TEXT("SaveAttack"),true);}
    UFUNCTION() void AnimNotify_ResetCombo(UAnimNotify* Notify){Invoke(TEXT("ResetCombo"),Notify==nullptr);}
};
