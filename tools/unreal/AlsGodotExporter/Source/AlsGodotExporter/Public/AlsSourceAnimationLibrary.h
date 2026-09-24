#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsSourceAnimationLibrary.generated.h"

class UAnimSequence;
class UAnimSequenceBase;
class USkeleton;
class UBlendSpace;

/** Read-only source metadata and explicitly raw, non-additive pose evaluation. */
UCLASS()
class ALSGODOTEXPORTER_API UAlsSourceAnimationLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourceFloatCurves(UAnimSequenceBase* Animation);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadAssetFloatCurveValues(UAnimSequenceBase* Animation, float TimeSeconds);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRawSamplingCases();

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRawBlendSpacePose(UBlendSpace* BlendSpace, float Pitch, float NormalizedTime);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRawBlendSpacePose2D(UBlendSpace* BlendSpace, float X, float Y, float NormalizedTime);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadBlendSpaceTriangulationReference(UBlendSpace* BlendSpace, const FString& InputsJson);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourceAnimationMetadata(UAnimSequence* Animation);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSkeletonPoseMetadata(USkeleton* Skeleton);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRawBonePose(UAnimSequence* Animation, double TimeSeconds,
                                  bool ShouldRetarget, bool ExtractRootMotion, bool IgnoreRootLock);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRawAnimationPose(UAnimSequence* Animation, double TimeSeconds,
                                       bool ShouldRetarget, bool ExtractRootMotion, bool IgnoreRootLock);
};
