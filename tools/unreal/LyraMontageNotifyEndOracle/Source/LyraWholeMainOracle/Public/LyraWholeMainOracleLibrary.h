#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraWholeMainOracleLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class LYRAWHOLEMAINORACLE_API ULyraWholeMainOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadMontageNotifyEnd(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadMontageBankCallbacks(const FString& RequestsJson);

	UFUNCTION(BlueprintCallable, Category="Lyra|Oracle")
	static FString ReadMontageImmediateCallbacks(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadMontageEvents(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadLayerBindingMatrix(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static bool CopySourceNotifies(UAnimSequence* Source,UAnimSequence* TransientTarget);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadTrace(USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
