#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "AlsAnimationGraphLibrary.generated.h"

class UAnimBlueprint;
class UCharacterMovementComponent;
class UPrimitiveComponent;
class AActor;
class UWorld;
class UAlsAnimationInstanceSettings;

/** Read-only baked state machines and their original transition curves. */
UCLASS()
class ALSGODOTEXPORTER_API UAlsAnimationGraphLibrary final : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /** Actual original Parent refresh functions on transient game-world instances. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportMovementParentTrace(const FString& RequestPath, const FString& OutputPath);

    /** Full precision movement settings and referenced curves; no asset writes. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadMovementSettings(UAlsAnimationInstanceSettings* Settings);

    /** Actual weapon notify functions, parent transition playback and native Slot poses. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRefactoredTransitionTrace(const FString& RequestPath, const FString& OutputPath);

    /** Original Default Overlay AnimGraph, including its linked layer, property access and Sync ticking. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRefactoredDefaultOverlayTrace(const FString& RequestPath, const FString& OutputPath);

    /** Original parent View/Spine update and Head callbacks on transient game-world instances. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRefactoredViewTrace(const FString& RequestPath, const FString& OutputPath);

    /** Original linked Layering graph with controlled parent states and cached linked input poses. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRefactoredLayerGraphTrace(const FString& RequestPath, const FString& OutputPath);

    /** Compiled node identities, reflected defaults and native deferred cache order. Never compiles or saves assets. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadCompiledAnimationGraph(UAnimBlueprint* Blueprint);

    /** Real camera component ticks in a clear scene; records spatial inputs and outputs. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportCameraComponentTrace(const FString& OutputPath);

    /** Unmodified Refactored Camera AnimGraph; controlled input tags, no assets saved. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportCameraGraphTrace(const FString& RequestPath, const FString& OutputPath);

    /** Actual isolated native joint scene steps and effective global solver settings. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportPhysicsJointSolverReference(const FString& OutputPath);

    /** Live Chaos joint settings and native angular utility observations. No assets saved. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportPhysicsJointReference(const FString& OutputPath);

    /** Original PhysicsAssets plus native reference-pose body mass and inertia. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportPhysicsAssets(const FString& OutputPath);

    /** Actual Refactored RefreshInAir in a transient game world with capsule sweeps. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportGroundPrediction(const FString& OutputPath);

    /** Actual pelvis spring and foot trace units with transient collision geometry. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportFootEnvironment(const FString& OutputPath);

    /** Actual Control Rig per-item IK, including weighted hierarchy writes. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRigTwoBoneIk(const FString& OutputPath);

    /** Actual foot nodes in the authored ApplyFootIk order on a transient hierarchy. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportLegRig(const FString& OutputPath);

    /** Actual Refactored foot control units on a transient rig hierarchy. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportRefactoredFootControls(const FString& OutputPath);

    /** Replay actual platform inputs through the original foot location unit. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ReplayFootOffsetLocations(const FString& RequestPath, const FString& OutputPath);

    /** Original CR_Als VM with captured pre-rig pose/targets and independent platform collision. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ReplayRefactoredFootRig(const FString& RequestPath, const FString& OutputPath, bool bClosedFeedback = false);

    /** Original Refactored Character/AnimBP/physics in an isolated ticking world; platform and input commands only. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ReplayRefactoredCharacterPlatform(const FString& RequestPath, const FString& OutputPath);

    /** Actual ALS-Refactored foot-lock functions on transient Editor instances. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportBasedFootLockTrace(const FString& RequestPath, const FString& OutputPath);

    /** Set protected native inputs on a transient probe only; CalcVelocity remains the engine implementation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool SetMovementProbeInput(UCharacterMovementComponent* Component, FVector Acceleration, float Analog);

    /** Advance the engine's actual based movement on transient Editor probes only. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool AdvanceMovementProbeBase(UCharacterMovementComponent* Component, UPrimitiveComponent* Base, float DeltaSeconds, bool Initialize);

    /** Bypass viewport placement factories, which are unavailable in cold commandlets. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static AActor* SpawnMovementProbeActor(UWorld* World, TSubclassOf<AActor> ActorClass, FVector Location);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static FString ReadBakedStateMachines(UAnimBlueprint* Blueprint);

    /** Read original Aim sampling settings and native oracle; never saves assets. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportAimSampling(const FString& OutputPath);

    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportAimStateTrace(const FString& RequestPath, const FString& OutputPath);

    /** Actual compiled Overlay machines; controlled committed curve inputs, no asset saves. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportOverlayStateTrace(const FString& RequestPath, const FString& OutputPath);

    /** Actual mantling root motion source on transient characters; never saves assets. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportMantlingRootMotionTrace(const FString& OutputPath);

    /** Original montage Advance and ALS native states on transient characters. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportMantlingBranchTrace(const FString& OutputPath);

    /** Original complete V4 AnimGraph, controlled properties; no Character/UpdateGraph simulation. */
    UFUNCTION(BlueprintCallable, Category="ALS Export")
    static bool ExportFullGraphTrace(const FString& RequestPath, const FString& OutputPath);
};
