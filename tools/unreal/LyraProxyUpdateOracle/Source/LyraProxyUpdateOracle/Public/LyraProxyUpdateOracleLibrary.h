#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "LyraProxyUpdateOracleLibrary.generated.h"
class USkeletalMesh;
class USkeleton;
class UAnimSequence;
UCLASS()
class LYRAPROXYUPDATEORACLE_API ULyraProxyUpdateOracleLibrary final:public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadLayerBindingMatrix(const FString& RequestsJson);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static bool CopySourceNotifies(UAnimSequence* Source,UAnimSequence* TransientTarget);
    UFUNCTION(BlueprintCallable,Category="Lyra Whole Main")
    static FString ReadTrace(USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson);
};
