#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraControlRigLibrary.generated.h"

class USkeletalMesh;
class USkeleton;
class UAnimSequence;
class UIKRetargeter;

/** Transient-only ALS control-rig extension. Never saves a Skeleton or sequence. */
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraControlRigLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    // Commandlet-only preference. Keep AnimationData loaded for serialized
    // models, but avoid silently converting ordinary raw tracks to Sequencer.
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool PreferRawTrackDataModel(bool Enabled);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRootYawNodeSettings(UClass* AnimationClass);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadHandBasisCalibration(USkeletalMesh* SourceMesh, USkeletalMesh* TargetMesh,
                                            UIKRetargeter* Retargeter);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static USkeleton* CreateWeaponSkeleton(USkeleton* Source, USkeleton* Target, FQuat HandBasisRotation);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static UAnimSequence* CreateWeaponSequence(UAnimSequence* Source, UAnimSequence* Target,
                                              USkeleton* ExtendedSkeleton, FQuat HandBasisRotation,
                                              UAnimSequence* ExtendedBase);
};
