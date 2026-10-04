#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsHandControlLibrary.generated.h"

class UAnimSequence;
class USkeleton;

UCLASS()
class ALSV4ASSETEXPORTER_API UAlsHandControlLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadHandRetargetPose(UAnimSequence* Animation, double TimeSeconds,
                                       float HandFKWeight, float Alpha);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadTwoBoneHandPose(UAnimSequence* Animation, double TimeSeconds,
                                      bool RightHand, bool RetargetFirst, float Alpha,
                                      FVector EffectorOffset);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadWeaponSpaceSamples(UAnimSequence* Animation);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadWeaponSpaceCopyPose(USkeleton* Skeleton, const FString& LocalPoseJson, float Alpha);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadLogicalHandChain(USkeleton* Skeleton, UClass* LayerClass, const FString& LocalPoseJson,
        float HandFKWeight, float RetargetAlpha, float RightAlpha, float LeftAlpha);
};
