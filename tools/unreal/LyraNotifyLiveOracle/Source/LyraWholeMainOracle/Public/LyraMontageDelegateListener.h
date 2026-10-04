#pragma once
#include "CoreMinimal.h"
#include "UObject/Object.h"
#include "LyraMontageDelegateListener.generated.h"
class UAnimMontage;
UCLASS()
class ULyraMontageDelegateListener final:public UObject
{
    GENERATED_BODY()
public:
    TFunction<void(int32,UAnimMontage*,bool,FName,bool)> Record;
    UFUNCTION() void Out(UAnimMontage* Montage,bool bInterrupted);
    UFUNCTION() void In(UAnimMontage* Montage);
    UFUNCTION() void End(UAnimMontage* Montage,bool bInterrupted);
    UFUNCTION() void Section(UAnimMontage* Montage,FName SectionName,bool bLooped);
};
