#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsLyraGraphLibrary.generated.h"

class USkeletalMesh;
class USkeleton;
class UAnimSequence;
class UBlendSpace;

/** Read the running class's compiled state machines and node settings. */
UCLASS()
class ALSV4ASSETEXPORTER_API UAlsLyraGraphLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Real Character/Movement property access and original ordered Main observation functions. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainObservationTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRuntimeGraph(UClass* AnimationClass);
    /** Compiled source ownership and folded settings, without ticking or saving. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourceNodes(UClass* AnimationClass);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadSourceCurveTrace(UAnimSequence* Animation, const FString& TimesJson);
    /** Transient additive fixture with source-only, base-only and shared curves. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static UAnimSequence* CreateSourceCurveFixture(UAnimSequence* Animation);
    /** Actual standalone evaluators register through the proxy's real sync scope. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadEvaluatorSyncTrace(const FString& RequestsJson);
    /** Invoke original Cycle callback on a registered linked instance, then tick its real source. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCycleSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Actual Start evaluator relevance/update callbacks and source tick, plus immutable distance data. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadStartSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Original Stop evaluator callbacks read the actual CharacterMovement snapshot and property access batches. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadStopSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Both original Pivot occurrences on one linked owner and common native Sync. Explicit visit observations. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadPivotSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Original PivotSM selects its own state and traverses its unmodified child roots. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadPivotMachineTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Original Pivot root, actual machine and independent warps, then outer HipFire on ALS81. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadPivotRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Actual Main StateResult20 -> ApplyAdditive23 -> Linked21/Lean22, full Main update and one Sync. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainPivotTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadStopRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Actual Main Stop state result/Linked provider, complete Main update and explicit machine context. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainStopRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadDistanceSequenceData(UAnimSequence* Sequence, FName CurveName);
    /** Direct default/raw curve evaluation for independent precision diagnostics. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadDistanceCurveProbe(UAnimSequence* Sequence, FName CurveName, const FString& TimesJson);
    /** Immutable default compressed root payload and independent static probes. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRootCompressionData(UAnimSequence* Sequence);
    /** Full compiled Cycle closure and real root traversal, without pose evaluation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCycleLayerGraph(UClass* LayerClass);
    /** Compiled closure of a named animation interface implementation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadAnimationLayerGraph(UClass* LayerClass, FName LayerName, bool IncludeStateRoots = false);
    /** Blueprint-owned defaults, including ordered animation arrays and nested cardinal structs. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadAnimationLayerDefaults(UClass* AnimationClass);
    /** Real original Start provider root and common Sync on transient ALS81. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadStartRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Original Main Start ApplyAdditive -> real linked Start provider and Lean, one common Sync. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainStartLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    /** Two real Main roots in traversal order, one Main update and common Sync. Explicit state weights. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainSourceScopeTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);

    /** Start/Cycle/Stop actual roots on one registered Main/provider and common Sync. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainSourceScopeStopTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    /** Actual StateResult10/14/18 callbacks in one ground source scope; explicit machine observations. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainStateRootsTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainStateHistoryTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    /** Four actual ground state roots on one Main/provider, one native Sync. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainGroundScopeTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences,UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences,const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCycleLayerTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Original compiled linked Cycle root update + common Sync + Evaluate on transient ALS81. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCycleRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
                                             const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Original Main Cycle ApplyAdditive -> real linked provider and Lean, one common Sync. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainCycleLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
        const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
        const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson);
    /** Original Main Lean nodes, exposed pins and a single native Sync on transient ALS81. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
                                    UBlendSpace* Source, const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Original RotationData and three compiled ApplyAdditive roots; explicit ALS source base-pose boundary. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMainLeanCompositionTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
                                    UBlendSpace* Source, const TArray<UAnimSequence*>& Sequences,
                                    const TArray<UAnimSequence*>& BaseSequences, const FString& RequestsJson);
    /** Original Cycle LayeredBoneBlend, adapted mask and raw ALS81 sequence leaves. No warp evaluation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCycleLayerPoseTrace(USkeleton* Skeleton, UClass* LayerClass,
                                           const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    /** Actual raw root-track/range/provider extraction on transient ALS sources. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadRootMotionTrace(const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson);
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadLocomotionRuleProbe(UClass* AnimationClass, USkeletalMesh* Mesh, const FString& RequestsJson);
    /** Real compiled main machine + final inertia, with explicitly supplied state poses. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadLocomotionBlendTrace(UClass* AnimationClass, USkeletalMesh* Mesh,
                                           USkeleton* Skeleton, const FString& RequestsJson);
};
