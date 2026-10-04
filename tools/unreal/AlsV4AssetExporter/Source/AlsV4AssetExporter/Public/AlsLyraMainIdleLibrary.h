#pragma once
#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraMainIdleLibrary.generated.h"
class USkeletalMesh;
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraMainIdleLibrary final:public UBlueprintFunctionLibrary
{
 GENERATED_BODY()
public:
 UFUNCTION(BlueprintCallable,Category="ALS Export")
 static FString ReadMainIdleTrace(UClass* MainClass,USkeletalMesh* Mesh,const FString& RequestsJson);
};
