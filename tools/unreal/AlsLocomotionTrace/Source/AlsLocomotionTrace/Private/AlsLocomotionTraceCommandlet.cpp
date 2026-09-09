#include "AlsLocomotionTraceCommandlet.h"

#include "AlsTraceCharacter.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacterMovementComponent.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimCurveTypes.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Animation/AnimNode_RelevantAssetPlayerBase.h"
#include "Animation/AnimationAsset.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSequenceBase.h"
#include "Animation/BlendSpace.h"
#include "Components/BoxComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Engine/Engine.h"
#include "Engine/EngineBaseTypes.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Engine/CollisionProfile.h"
#include "HAL/FileManager.h"
#include "JsonObjectConverter.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Misc/FileHelper.h"
#include "Misc/PackageName.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Misc/SecureHash.h"
#include "Modules/ModuleManager.h"
#include "Notifies/AlsAnimNotifyState_SetLocomotionAction.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Settings/AlsCharacterSettings.h"
#include "Settings/AlsMovementSettings.h"
#include "State/AlsCrouchingState.h"
#include "State/AlsGroundedState.h"
#include "State/AlsLeanState.h"
#include "State/AlsLocomotionAnimationState.h"
#include "State/AlsMovementBaseState.h"
#include "State/AlsStandingState.h"
#include "State/AlsFeetState.h"
#include "State/AlsDynamicTransitionsState.h"
#include "State/AlsLayeringState.h"
#include "State/AlsRotateInPlaceState.h"
#include "State/AlsSpineState.h"
#include "State/AlsTurnInPlaceState.h"
#include "State/AlsTransitionsState.h"
#include "State/AlsViewAnimationState.h"
#include "UObject/UnrealType.h"

#include <charconv>
#include "Utility/AlsGameplayTags.h"
#include "Utility/AlsConstants.h"

#include <openssl/sha.h>

#include UE_INLINE_GENERATED_CPP_BY_NAME(AlsLocomotionTraceCommandlet)

IMPLEMENT_MODULE(FDefaultModuleImpl, AlsLocomotionTrace)

namespace AlsLocomotionTrace
{
constexpr double FixedDeltaSeconds{1.0 / 60.0};
constexpr float StationaryYawSpeedThreshold{0.1f};
constexpr float StationaryYawAccelerationThreshold{0.1f};
constexpr int32 SchemaVersion{1};
constexpr const TCHAR* LockedReferenceCommit{TEXT("b754d6f0f2bb03741d301f8fb88077ebfe561e17")};
constexpr const TCHAR* LockedPatchHash{TEXT("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f")};
constexpr const TCHAR* P4AimOverlayClassPath{
    TEXT("/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Rifle.AB_Als_Rifle_C")};
constexpr const TCHAR* P5aNativeTransitionStimulusContractSha256{
    TEXT("78cfb6aad01c29ac179f63515835aeeb6dd48b70dc42223cf81dea771e27f11d")};
constexpr ANSICHAR P5aNativeTransitionStimulusContractText[]{
    "ALS_P5A_NATIVE_TRANSITION_STIMULUS_V1\n"
    "class=/Script/ALS.AlsAnimationInstance\n"
    "feet=FeetState:FAlsFeetState\n"
    "left=Left:FAlsFootState\n"
    "right=Right:FAlsFootState\n"
    "target=TargetLocationWorldSpace:FVector\n"
    "lock=LockLocationWorldSpace:FVector\n"
    "relevant=LockAmount:float\n"
    "transitions=TransitionsState:FAlsTransitionsState\n"
    "allowed=bTransitionsAllowed:bool\n"
    "dynamic=DynamicTransitionsState:FAlsDynamicTransitionsState\n"
    "updated=bUpdatedThisFrame:bool\n"
    "delay=FrameDelay:int32\n"
    "function=RefreshDynamicTransitions:void()\n"};

struct FP5aNativeTransitionStimulusContract
{
    FStructProperty* FeetState{nullptr};
    FStructProperty* Left{nullptr};
    FStructProperty* Right{nullptr};
    FStructProperty* TargetLocationWorldSpace{nullptr};
    FStructProperty* LockLocationWorldSpace{nullptr};
    FFloatProperty* LockAmount{nullptr};
    FStructProperty* TransitionsState{nullptr};
    FBoolProperty* bTransitionsAllowed{nullptr};
    FStructProperty* DynamicTransitionsState{nullptr};
    FBoolProperty* bUpdatedThisFrame{nullptr};
    FIntProperty* FrameDelay{nullptr};
    UFunction* RefreshDynamicTransitions{nullptr};
};

struct FP5aNativeTransitionStimulusInput
{
    FAlsFootState Left;
    FAlsFootState Right;
};

struct FP5aNativeTransitionStimulusReceipt
{
    bool ObservedAllowTransitions{false};
    bool PreHookUpdatedThisFrame{false};
    int32 PreHookFrameDelay{0};
    bool PreHookTransitionActive{false};
    FVector ObservedLeftTarget{ForceInit};
    FVector ObservedLeftLock{ForceInit};
    float ObservedLeftLockAmount{0.0f};
    FVector ObservedRightTarget{ForceInit};
    FVector ObservedRightLock{ForceInit};
    float ObservedRightLockAmount{0.0f};
    bool PostHookUpdatedThisFrame{false};
    int32 PostHookFrameDelay{0};
    bool RestoreVerified{false};
};

struct FP5aNativeAssetAudit
{
    FString AssetObjectPath;
    FString AssetStableId;
    FString AssetPackageSha256;
    FString AssetClassPath;
    double DurationSeconds{0.0};
    bool bAuthoredLoop{false};
    FString MontageObjectPath;
    FString MontageStableId;
    FString SectionName;
    FString SlotName;
    int32 SegmentIndex{-1};
};

struct FP5aNativeEventAudit
{
    FString AssetStableId;
    FString StableEventId;
    FString OwnerKind;
    FString SourceClassPath;
    int32 SourceIndex{-1};
    int32 TrackIndex{-1};
    double TimeSeconds{0.0};
    double DurationSeconds{0.0};
    double TriggerWeightThreshold{0.0};
    FString TickMode;
};

struct FP5aNativeMarkerAudit
{
    FString AssetStableId;
    FString StableMarkerId;
    FString Name;
    int32 SourceIndex{-1};
    int32 TrackIndex{-1};
    double TimeSeconds{0.0};
};

struct FP5aNativeCurveInventory
{
    FString AssetObjectPath;
    FString AssetStableId;
    TArray<FString> CurveNames;
};

struct FP5aNativeReferenceAudit
{
    TArray<FP5aNativeAssetAudit> Assets;
    TArray<FP5aNativeEventAudit> Events;
    TArray<FP5aNativeMarkerAudit> Markers;
    TArray<FP5aNativeCurveInventory> CurveInventories;
    TArray<TSharedPtr<FJsonValue>> AuxiliaryAssets;
};

struct FP5aNativeActionOutcome
{
    FString NativeReason;
    FString Callback;
    bool bInterrupted{false};
};

struct FP5aObservedSource;

struct FP5aNativePlaybackSnapshot
{
    bool bCaptured{false};
    float Position{0.0f};
    float PreviousPosition{0.0f};
    float PlayRate{0.0f};
    float Weight{0.0f};
    FName SectionName{NAME_None};
};

struct FP5aNativeActionLifecycle
{
    TObjectPtr<UAnimMontage> Montage{nullptr};
    int32 InstanceId{INDEX_NONE};
    float PreCancelPosition{0.0f};
    bool bOnMontageStartedObserved{false};
    bool bOnMontageBlendingOutStartedObserved{false};
    bool bOnMontageEndedObserved{false};
    bool bEndedThisFrame{false};
    FP5aNativePlaybackSnapshot TerminalPlayback;
    FP5aNativePlaybackSnapshot EndedPlayback;
    float LastPlaybackPosition{0.0f};
    TArray<FP5aNativeActionOutcome> Outcomes;
    int32 EmittedOutcomeCount{0};
    int32 SemanticOutcomeCount{0};
    TFunction<void(const TCHAR*, bool, const FAnimMontageInstance*)> DiagnosticCallback;
    TFunction<void()> DiagnosticAfterUpdate;
};

struct FP5aFrameUpdateAudit
{
    int32 AnimationUpdates{0};
    int32 Evaluations{0};
    int32 PostUpdates{0};
    int32 MeshTicks{0};
};

struct FP5aSemanticGraphState
{
    float ActionWeight{0.0f};
    float ActionEventWeight{0.0f};
    float TransitionWeight{0.0f};
    bool bActionActive{false};
    bool bTransitionActive{false};
};

struct FP5aObservedNotifyState
{
    int32 InstanceId{INDEX_NONE};
    int32 MontageInstanceId{INDEX_NONE};
    bool bBranchingPoint{false};
    bool bTicked{true};
    const FP5aObservedSource* Source{nullptr};
    const FAnimNotifyEvent* Notify{nullptr};
    int32 SourceIndex{-1};
    float LastAnimationTime{0.0f};
    float PreviousAnimationTime{0.0f};
    float PlayRate{1.0f};
};

struct FP5aNativeNotifyObservationState
{
    TArray<FP5aObservedNotifyState> ActiveStates;
    TArray<FP5aObservedNotifyState> EndedStates;
};

struct FP5aObservedSource
{
    FString AssetObjectPath;
    FString AssetStableId;
    FString AssetPackageSha256;
    FString AssetClassPath;
    FString MontageObjectPath;
    FString MontageStableId;
    FString SectionName;
    FString SlotName;
    int32 SegmentIndex{-1};
};

struct FP5aSourceDefinition
{
    FString TraceSourceId;
    FString SourceKind;
    FP5aObservedSource Canonical;
    FString NativeRole;
    FP5aObservedSource Native;
    TObjectPtr<UAnimSequenceBase> CanonicalAsset{nullptr};
    TObjectPtr<UAnimSequenceBase> NativeAsset{nullptr};
    TArray<FString> NativeRoles;
    TArray<FP5aObservedSource> NativeVariants;
    TArray<TObjectPtr<UAnimSequenceBase>> NativeAssets;
};

struct FP5aPlaybackDefinition
{
    FString TraceSourceId;
    FString Lane;
    double PreviousTimeSeconds{0.0};
    double CurrentTimeSeconds{0.0};
    double FrameStartOffsetSeconds{0.0};
    double FrameEndOffsetSeconds{0.0};
    float Weight{0.0f};
    bool bLoop{false};
    bool bClosesAfterFrame{false};
};

struct FP5aFrameDefinition
{
    int32 FrameIndex{-1};
    double WindowStartSeconds{0.0};
    double WindowEndSeconds{0.0};
    double DeltaSeconds{0.0};
    FString LocomotionMode;
    FString RotationMode;
    FString Stance;
    bool bHasInput{false};
    float ActionBlendAmount{0.0f};
    float ActionModeBlendAmount{0.0f};
    FString ActionCommand;
    TArray<FP5aPlaybackDefinition> Playbacks;
    FP5aNativeTransitionStimulusInput TransitionStimulus;
};

struct FP5aCaseDefinition
{
    int32 Ordinal{-1};
    FString CaseId;
    TArray<FP5aFrameDefinition> Frames;
};

struct FP5aCanonicalAssetOracle
{
    TSharedPtr<FJsonObject> Value;
};

struct FP5aNativeRuntimeFrame
{
    TSharedPtr<FJsonObject> Value;
};

struct FP5aRawCase
{
    int32 Ordinal{-1};
    FString CaseId;
    TArray<FP5aNativeRuntimeFrame> Frames;
};

struct FP5aRawTrace
{
    FString TracePlanSha256;
    TSharedPtr<FJsonObject> Reference;
    TSharedPtr<FJsonObject> Snapshot;
    FP5aNativeReferenceAudit NativeReferenceAudit;
    TArray<FP5aRawCase> Cases;
};

struct FTraceCommand
{
    FVector2D MovementAxes{ForceInit};
    float ViewYaw{0.0f};
    float AimYaw{0.0f};
    FGameplayTag Gait{AlsGaitTags::Running};
    FGameplayTag Stance{AlsStanceTags::Standing};
    FGameplayTag RotationMode{AlsRotationModeTags::ViewDirection};
    bool bJumpPressed{false};
    bool bStandBlocked{false};
};

struct FSequenceDefinition
{
    FString Name;
    int32 FrameCount{0};
    TFunction<FTraceCommand(int32)> GetCommand;
};

struct FPortDirectionalSpeeds
{
    float Forward{0.0f};
    float Sideways{0.0f};
    float Backward{0.0f};
};

struct FPortStanceSpeeds
{
    FPortDirectionalSpeeds Walking;
    FPortDirectionalSpeeds Running;
    FPortDirectionalSpeeds Sprinting;
};

struct FPortSettings
{
    FPortStanceSpeeds Standing;
    FPortStanceSpeeds Crouching;
    float AnimatedWalkSpeed{0.0f};
    float AnimatedRunSpeed{0.0f};
    float AnimatedSprintSpeed{0.0f};
    float AnimatedCrouchSpeed{0.0f};
    float MovingSpeedThreshold{0.0f};
    float VelocityAngleStart{0.0f};
    float VelocityAngleEnd{0.0f};
    float LeanHalfLife{0.0f};
};

struct FPortState
{
    bool bInitialized{false};
    bool bGrounded{true};
    bool bJumpStartActive{false};
    float LandingRecoveryTime{0.0f};
    FVector2D SmoothedLean{ForceInit};
    float AnimationPhase{0.0f};
    float SmoothedTargetYaw{0.0f};
    float TargetYaw{0.0f};
};

struct FNativeObservationState
{
    float AnimationPhase{0.0f};
    int32 LandingRecoveryFramesRemaining{0};
    bool bJumpStartActive{false};
};

struct FPortResult
{
    FString LocomotionState;
    FGameplayTag Gait;
    FString AnimationState;
    FVector2D BlendCoordinates{ForceInit};
    float Stride{0.0f};
    float PlayRate{0.0f};
    FVector2D Lean{ForceInit};
    float AnimationPhase{0.0f};
    float TargetYaw{0.0f};
};

FString GetRequiredValue(const FString& Parameters, const TCHAR* Key)
{
    FString Value;
    if (!FParse::Value(*Parameters, Key, Value) || Value.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("Missing required argument %s"), Key);
        return {};
    }
    return Value.TrimQuotes();
}

bool ValidateReference(const FString& ReferenceRoot, const FString& LockedCommit, const FString& PatchHashes)
{
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_BEGIN root=%s"), *ReferenceRoot);
    if (!IFileManager::Get().DirectoryExists(*ReferenceRoot))
    {
        UE_LOG(LogTemp, Error, TEXT("Reference root does not exist: %s"), *ReferenceRoot);
        return false;
    }

    FString ActualCommit;
    const FString HeadPath{ReferenceRoot / TEXT(".git/HEAD")};
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_HEAD_BEGIN path=%s"), *HeadPath);
    if (!FFileHelper::LoadFileToString(ActualCommit, *HeadPath))
    {
        UE_LOG(LogTemp, Error, TEXT("Could not read detached git HEAD: %s"), *HeadPath);
        return false;
    }
    ActualCommit.TrimStartAndEndInline();
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_HEAD_OK commit=%s"), *ActualCommit);
    if (ActualCommit.StartsWith(TEXT("ref:")) || ActualCommit != LockedCommit)
    {
        UE_LOG(LogTemp, Error, TEXT("Locked commit mismatch. Expected %s, actual %s"), *LockedCommit, *ActualCommit);
        return false;
    }

    FString DescriptorText;
    const FString DescriptorPath{ReferenceRoot / TEXT("ALS.uplugin")};
    TSharedPtr<FJsonObject> Descriptor;
    if (!FFileHelper::LoadFileToString(DescriptorText, *DescriptorPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(DescriptorText), Descriptor) ||
        !Descriptor.IsValid() || Descriptor->GetStringField(TEXT("EngineVersion")) != TEXT("5.9.0"))
    {
        UE_LOG(LogTemp, Error, TEXT("ALS descriptor is not the locked UE 5.9 compatibility result: %s"), *DescriptorPath);
        return false;
    }

    if (PatchHashes != LockedPatchHash)
    {
        UE_LOG(LogTemp, Error, TEXT("Locked patch hash mismatch. Expected %s, got %s"), LockedPatchHash, *PatchHashes);
        return false;
    }
    return true;
}

bool IsLowercaseCommitSha(const FString& Commit)
{
    if (Commit.Len() != 40) return false;
    for (const TCHAR Character : Commit)
    {
        if (!FChar::IsHexDigit(Character) || (Character >= TEXT('A') && Character <= TEXT('F'))) return false;
    }
    return true;
}

bool IsLowercaseSha256(const FString& Hash)
{
    if (Hash.Len() != 64) return false;
    for (const TCHAR Character : Hash)
    {
        if (!FChar::IsHexDigit(Character) || (Character >= TEXT('A') && Character <= TEXT('F'))) return false;
    }
    return true;
}

FString Sha256Hex(const uint8* Data, const int64 Size)
{
    uint8 Digest[SHA256_DIGEST_LENGTH];
    if (Size < 0 || SHA256(Data, static_cast<size_t>(Size), Digest) == nullptr) return {};
    return BytesToHex(Digest, SHA256_DIGEST_LENGTH).ToLower();
}

bool ValidateP5aTracePlan(const FString& Path, const FString& ExpectedSha256,
                          TSharedPtr<FJsonObject>& Plan)
{
    if (Path.IsEmpty() || FPaths::IsRelative(Path) || !IsLowercaseSha256(ExpectedSha256))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A trace plan path or SHA-256 is invalid."));
        return false;
    }
    TArray<uint8> Bytes;
    if (!FFileHelper::LoadFileToArray(Bytes, *Path) || Bytes.IsEmpty())
    {
        UE_LOG(LogTemp, Error, TEXT("Could not read P5A trace plan: %s"), *Path);
        return false;
    }
    const FString ActualSha256{Sha256Hex(Bytes.GetData(), Bytes.Num())};
    if (ActualSha256 != ExpectedSha256)
    {
        UE_LOG(LogTemp, Error, TEXT("P5A trace plan SHA-256 mismatch."));
        return false;
    }
    constexpr uint8 UTF8_BOM[]{0xef, 0xbb, 0xbf};
    if (Bytes.Num() < 2 || Bytes.Last() != '\n' || Bytes[Bytes.Num() - 2] == '\n' ||
        Bytes.Contains('\r') || (Bytes.Num() >= 3 && Bytes[0] == UTF8_BOM[0] &&
            Bytes[1] == UTF8_BOM[1] && Bytes[2] == UTF8_BOM[2]))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A trace plan is not canonical UTF-8/LF JSON."));
        return false;
    }
    FString Text;
    FFileHelper::BufferToString(Text, Bytes.GetData(), Bytes.Num());
    double PlanSchemaVersion{0.0};
    FString PlanKind;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Plan) || !Plan.IsValid() ||
        !Plan->TryGetNumberField(TEXT("schemaVersion"), PlanSchemaVersion) || PlanSchemaVersion != 2.0 ||
        !Plan->TryGetStringField(TEXT("kind"), PlanKind) || PlanKind != TEXT("p5a_trace_plan"))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A trace plan root is invalid."));
        return false;
    }
    const TSharedPtr<FJsonObject>* Snapshot{nullptr};
    const TSharedPtr<FJsonObject>* Bindings{nullptr};
    double BindingsVersion{0.0};
    FString BindingsDigest;
    if (!Plan->TryGetObjectField(TEXT("snapshot"), Snapshot) || Snapshot == nullptr ||
        !(*Snapshot)->TryGetObjectField(TEXT("bindings"), Bindings) || Bindings == nullptr ||
        !(*Bindings)->TryGetNumberField(TEXT("version"), BindingsVersion) || BindingsVersion != 2.0 ||
        !(*Bindings)->TryGetStringField(TEXT("digest"), BindingsDigest) || BindingsDigest != TEXT("40f33e59692dfd38"))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A trace plan binding identity is invalid."));
        return false;
    }
    return true;
}

FString CreateP5aAssetStableId(const FString& ObjectPath);
FString CreateP5aPackageSha256(const UAnimationAsset& Asset);

bool ReadP5aObservedSource(const TSharedPtr<FJsonObject>& Object, const bool bNative,
                           FP5aObservedSource& Source)
{
    double SegmentIndex{-1.0};
    if (!Object.IsValid() ||
        !Object->TryGetStringField(TEXT("assetObjectPath"), Source.AssetObjectPath) ||
        !Object->TryGetStringField(TEXT("assetStableId"), Source.AssetStableId) ||
        !Object->TryGetStringField(TEXT("assetClassPath"), Source.AssetClassPath) ||
        !Object->TryGetStringField(TEXT("montageStableId"), Source.MontageStableId) ||
        !Object->TryGetStringField(TEXT("sectionName"), Source.SectionName) ||
        !Object->TryGetStringField(TEXT("slotName"), Source.SlotName) ||
        !Object->TryGetNumberField(TEXT("segmentIndex"), SegmentIndex) ||
        SegmentIndex != static_cast<double>(FMath::TruncToInt(SegmentIndex)))
    {
        return false;
    }
    Source.SegmentIndex = static_cast<int32>(SegmentIndex);
    if (bNative && (!Object->TryGetStringField(TEXT("assetPackageSha256"), Source.AssetPackageSha256) ||
        !Object->TryGetStringField(TEXT("montageObjectPath"), Source.MontageObjectPath)))
    {
        return false;
    }
    return IsLowercaseCommitSha(Source.AssetStableId) &&
        (!bNative || IsLowercaseSha256(Source.AssetPackageSha256));
}

bool ReadP5aVectorMeters(const TSharedPtr<FJsonObject>& Object, FVector& Value)
{
    double X{0.0};
    double Y{0.0};
    double Z{0.0};
    if (!Object.IsValid() || !Object->TryGetNumberField(TEXT("x"), X) ||
        !Object->TryGetNumberField(TEXT("y"), Y) || !Object->TryGetNumberField(TEXT("z"), Z) ||
        !FMath::IsFinite(X) || !FMath::IsFinite(Y) || !FMath::IsFinite(Z))
    {
        return false;
    }
    Value = FVector{X * 100.0, Y * 100.0, Z * 100.0};
    return true;
}

bool ReadP5aPlaybackArray(const TSharedPtr<FJsonObject>& P4Curves, const TCHAR* FieldName,
                          const TSet<FString>& SourceIds, TArray<FP5aPlaybackDefinition>& Output)
{
    const TArray<TSharedPtr<FJsonValue>>* Values{nullptr};
    if (!P4Curves.IsValid() || !P4Curves->TryGetArrayField(FieldName, Values) || Values == nullptr)
    {
        return false;
    }
    for (const TSharedPtr<FJsonValue>& Value : *Values)
    {
        const TSharedPtr<FJsonObject>* Object{nullptr};
        FP5aPlaybackDefinition Playback;
        Playback.Lane = FieldName;
        double Weight{0.0};
        if (!Value.IsValid() || !Value->TryGetObject(Object) || Object == nullptr || !Object->IsValid() ||
            !(*Object)->TryGetStringField(TEXT("traceSourceId"), Playback.TraceSourceId) ||
            !SourceIds.Contains(Playback.TraceSourceId) ||
            !(*Object)->TryGetNumberField(TEXT("previousUnwrappedTimeSeconds"), Playback.PreviousTimeSeconds) ||
            !(*Object)->TryGetNumberField(TEXT("currentUnwrappedTimeSeconds"), Playback.CurrentTimeSeconds) ||
            !(*Object)->TryGetNumberField(TEXT("frameStartOffsetSeconds"), Playback.FrameStartOffsetSeconds) ||
            !(*Object)->TryGetNumberField(TEXT("frameEndOffsetSeconds"), Playback.FrameEndOffsetSeconds) ||
            !(*Object)->TryGetNumberField(TEXT("weight"), Weight) ||
            !(*Object)->TryGetBoolField(TEXT("loop"), Playback.bLoop) ||
            !(*Object)->TryGetBoolField(TEXT("closesAfterFrame"), Playback.bClosesAfterFrame) ||
            !FMath::IsFinite(Playback.PreviousTimeSeconds) ||
            !FMath::IsFinite(Playback.CurrentTimeSeconds) || !FMath::IsFinite(Weight))
        {
            return false;
        }
        Playback.Weight = static_cast<float>(Weight);
        Output.Add(MoveTemp(Playback));
    }
    return true;
}

bool ParseP5aCaseDefinitions(const TSharedPtr<FJsonObject>& Plan,
                             TArray<FP5aSourceDefinition>& Sources,
                             TArray<FP5aCaseDefinition>& Cases)
{
    constexpr const TCHAR* ExpectedCaseIds[]{
        TEXT("grounded_marker_interval"), TEXT("authority_tie"),
        TEXT("standing_transition_left"), TEXT("standing_transition_right"),
        TEXT("crouching_transition_reuse"), TEXT("roll_default_section"),
        TEXT("montage_owned_notify"), TEXT("segment_sequence_notify_state")};
    constexpr int32 ExpectedFrameCounts[]{41, 3, 33, 33, 33, 104, 69, 58};
    constexpr int32 ExpectedTotalFrameCount{374};
    const TArray<TSharedPtr<FJsonValue>>* SourceValues{nullptr};
    const TArray<TSharedPtr<FJsonValue>>* CaseValues{nullptr};
    if (!Plan.IsValid() || !Plan->TryGetArrayField(TEXT("sources"), SourceValues) ||
        SourceValues == nullptr || SourceValues->Num() != 9 ||
        !Plan->TryGetArrayField(TEXT("cases"), CaseValues) || CaseValues == nullptr ||
        CaseValues->Num() != UE_ARRAY_COUNT(ExpectedCaseIds))
    {
        return false;
    }

    TSet<FString> SourceIds;
    int32 SourceOrdinal{0};
    for (const TSharedPtr<FJsonValue>& SourceValue : *SourceValues)
    {
        const TSharedPtr<FJsonObject>* SourceObject{nullptr};
        const TSharedPtr<FJsonObject>* LayoutKey{nullptr};
        const TSharedPtr<FJsonObject>* CanonicalEvidence{nullptr};
        const TArray<TSharedPtr<FJsonValue>>* NativeVariants{nullptr};
        FP5aSourceDefinition Source;
        if (!SourceValue.IsValid() || !SourceValue->TryGetObject(SourceObject) || SourceObject == nullptr ||
            !SourceObject->IsValid() ||
            !(*SourceObject)->TryGetStringField(TEXT("traceSourceId"), Source.TraceSourceId) ||
            !IsLowercaseCommitSha(Source.TraceSourceId) || SourceIds.Contains(Source.TraceSourceId) ||
            !(*SourceObject)->TryGetObjectField(TEXT("layoutKey"), LayoutKey) || LayoutKey == nullptr ||
            !(*LayoutKey)->TryGetStringField(TEXT("sourceKind"), Source.SourceKind) ||
            !(*SourceObject)->TryGetObjectField(TEXT("canonicalEvidence"), CanonicalEvidence) ||
            CanonicalEvidence == nullptr || !ReadP5aObservedSource(*CanonicalEvidence, false, Source.Canonical) ||
            !(*SourceObject)->TryGetArrayField(TEXT("nativeVariants"), NativeVariants) ||
            NativeVariants == nullptr || NativeVariants->IsEmpty() || NativeVariants->Num() > 2)
        {
            UE_LOG(LogTemp, Error, TEXT("P5A source structure invalid index=%d"), SourceOrdinal);
            return false;
        }
        const TSharedPtr<FJsonObject>* NativeObject{nullptr};
        if (!(*NativeVariants)[0]->TryGetObject(NativeObject) || NativeObject == nullptr ||
            !(*NativeObject)->TryGetStringField(TEXT("nativeRole"), Source.NativeRole) ||
            !ReadP5aObservedSource(*NativeObject, true, Source.Native))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A native variant structure invalid index=%d"), SourceOrdinal);
            return false;
        }
        Source.CanonicalAsset = LoadObject<UAnimSequenceBase>(nullptr, *Source.Canonical.AssetObjectPath);
        Source.NativeAsset = LoadObject<UAnimSequenceBase>(nullptr, *Source.Native.AssetObjectPath);
        if (!IsValid(Source.CanonicalAsset) || !IsValid(Source.NativeAsset) ||
            Source.CanonicalAsset->GetPathName() != Source.Canonical.AssetObjectPath ||
            Source.NativeAsset->GetPathName() != Source.Native.AssetObjectPath ||
            CreateP5aAssetStableId(Source.Canonical.AssetObjectPath) != Source.Canonical.AssetStableId ||
            CreateP5aAssetStableId(Source.Native.AssetObjectPath) != Source.Native.AssetStableId ||
            CreateP5aPackageSha256(*Source.NativeAsset) != Source.Native.AssetPackageSha256)
        {
            UE_LOG(LogTemp, Error,
                TEXT("P5A source physical evidence mismatch index=%d canonical=%s actualCanonical=%s native=%s actualNative=%s expectedPackage=%s actualPackage=%s"),
                SourceOrdinal, *Source.Canonical.AssetObjectPath,
                IsValid(Source.CanonicalAsset) ? *Source.CanonicalAsset->GetPathName() : TEXT("<null>"),
                *Source.Native.AssetObjectPath,
                IsValid(Source.NativeAsset) ? *Source.NativeAsset->GetPathName() : TEXT("<null>"),
                *Source.Native.AssetPackageSha256,
                IsValid(Source.NativeAsset) ? *CreateP5aPackageSha256(*Source.NativeAsset) : TEXT("<null>"));
            return false;
        }
        for (const TSharedPtr<FJsonValue>& NativeVariantValue : *NativeVariants)
        {
            const TSharedPtr<FJsonObject>* VariantObject{nullptr};
            FString VariantRole;
            FP5aObservedSource Variant;
            if (!NativeVariantValue->TryGetObject(VariantObject) || VariantObject == nullptr ||
                !(*VariantObject)->TryGetStringField(TEXT("nativeRole"), VariantRole) ||
                !ReadP5aObservedSource(*VariantObject, true, Variant))
            {
                return false;
            }
            UAnimSequenceBase* VariantAsset{LoadObject<UAnimSequenceBase>(nullptr, *Variant.AssetObjectPath)};
            if (!IsValid(VariantAsset) || VariantAsset->GetPathName() != Variant.AssetObjectPath ||
                CreateP5aAssetStableId(Variant.AssetObjectPath) != Variant.AssetStableId ||
                CreateP5aPackageSha256(*VariantAsset) != Variant.AssetPackageSha256 ||
                Source.NativeRoles.Contains(VariantRole))
            {
                return false;
            }
            Source.NativeRoles.Add(MoveTemp(VariantRole));
            Source.NativeVariants.Add(MoveTemp(Variant));
            Source.NativeAssets.Add(VariantAsset);
        }
        SourceIds.Add(Source.TraceSourceId);
        Sources.Add(MoveTemp(Source));
        ++SourceOrdinal;
    }

    int32 TotalFrameCount{0};
    for (int32 CaseIndex{0}; CaseIndex < CaseValues->Num(); ++CaseIndex)
    {
        const TSharedPtr<FJsonObject>* CaseObject{nullptr};
        const TArray<TSharedPtr<FJsonValue>>* FrameValues{nullptr};
        double Ordinal{-1.0};
        FP5aCaseDefinition Case;
        if (!(*CaseValues)[CaseIndex]->TryGetObject(CaseObject) || CaseObject == nullptr ||
            !(*CaseObject)->TryGetNumberField(TEXT("ordinal"), Ordinal) || Ordinal != CaseIndex ||
            !(*CaseObject)->TryGetStringField(TEXT("caseId"), Case.CaseId) ||
            Case.CaseId != ExpectedCaseIds[CaseIndex] ||
            !(*CaseObject)->TryGetArrayField(TEXT("frames"), FrameValues) || FrameValues == nullptr ||
            FrameValues->Num() != ExpectedFrameCounts[CaseIndex])
        {
            return false;
        }
        Case.Ordinal = CaseIndex;
        for (int32 FrameIndex{0}; FrameIndex < FrameValues->Num(); ++FrameIndex)
        {
            const TSharedPtr<FJsonObject>* FrameObject{nullptr};
            const TSharedPtr<FJsonObject>* Input{nullptr};
            const TSharedPtr<FJsonObject>* Window{nullptr};
            const TSharedPtr<FJsonObject>* Modes{nullptr};
            const TSharedPtr<FJsonObject>* P4Curves{nullptr};
            const TSharedPtr<FJsonObject>* ActionRequest{nullptr};
            const TSharedPtr<FJsonObject>* Probe{nullptr};
            double ParsedFrameIndex{-1.0};
            double ActionBlendAmount{0.0};
            double ActionModeBlendAmount{0.0};
            FP5aFrameDefinition Frame;
            if (!(*FrameValues)[FrameIndex]->TryGetObject(FrameObject) || FrameObject == nullptr ||
                !(*FrameObject)->TryGetNumberField(TEXT("frameIndex"), ParsedFrameIndex) ||
                ParsedFrameIndex != FrameIndex || !(*FrameObject)->TryGetObjectField(TEXT("input"), Input) ||
                Input == nullptr || !(*Input)->TryGetObjectField(TEXT("window"), Window) || Window == nullptr ||
                !(*Input)->TryGetObjectField(TEXT("modes"), Modes) || Modes == nullptr ||
                !(*Input)->TryGetObjectField(TEXT("p4Curves"), P4Curves) || P4Curves == nullptr ||
                !(*Input)->TryGetObjectField(TEXT("actionRequest"), ActionRequest) || ActionRequest == nullptr ||
                !(*Input)->TryGetObjectField(TEXT("transitionProbe"), Probe) || Probe == nullptr ||
                !(*Window)->TryGetNumberField(TEXT("startSeconds"), Frame.WindowStartSeconds) ||
                !(*Window)->TryGetNumberField(TEXT("endSeconds"), Frame.WindowEndSeconds) ||
                !(*Window)->TryGetNumberField(TEXT("deltaSeconds"), Frame.DeltaSeconds) ||
                !FMath::IsNearlyEqual(Frame.DeltaSeconds, FixedDeltaSeconds, 1.0e-7) ||
                !(*Modes)->TryGetStringField(TEXT("locomotionMode"), Frame.LocomotionMode) ||
                !(*Modes)->TryGetStringField(TEXT("rotationMode"), Frame.RotationMode) ||
                !(*Modes)->TryGetStringField(TEXT("stance"), Frame.Stance) ||
                !(*Modes)->TryGetBoolField(TEXT("hasInput"), Frame.bHasInput) ||
                !(*P4Curves)->TryGetNumberField(TEXT("actionBlendAmount"), ActionBlendAmount) ||
                !(*P4Curves)->TryGetNumberField(TEXT("actionModeBlendAmount"), ActionModeBlendAmount) ||
                !FMath::IsFinite(ActionBlendAmount) || ActionBlendAmount < 0.0 || ActionBlendAmount > 1.0 ||
                !FMath::IsFinite(ActionModeBlendAmount) ||
                ActionModeBlendAmount < 0.0 || ActionModeBlendAmount > 1.0 ||
                !(*ActionRequest)->TryGetStringField(TEXT("command"), Frame.ActionCommand) ||
                !ReadP5aPlaybackArray(*P4Curves, TEXT("base"), SourceIds, Frame.Playbacks) ||
                !ReadP5aPlaybackArray(*P4Curves, TEXT("turnBanks"), SourceIds, Frame.Playbacks) ||
                !ReadP5aPlaybackArray(*P4Curves, TEXT("rotateBanks"), SourceIds, Frame.Playbacks))
            {
                return false;
            }
            Frame.FrameIndex = FrameIndex;
            Frame.ActionBlendAmount = static_cast<float>(ActionBlendAmount);
            Frame.ActionModeBlendAmount = static_cast<float>(ActionModeBlendAmount);
            for (const TPair<FString, FAlsFootState*> Foot : {
                TPair<FString, FAlsFootState*>{TEXT("left"), &Frame.TransitionStimulus.Left},
                TPair<FString, FAlsFootState*>{TEXT("right"), &Frame.TransitionStimulus.Right}})
            {
                const TSharedPtr<FJsonObject>* FootObject{nullptr};
                const TSharedPtr<FJsonObject>* Target{nullptr};
                const TSharedPtr<FJsonObject>* Lock{nullptr};
                bool bRelevant{false};
                if (!(*Probe)->TryGetObjectField(Foot.Key, FootObject) || FootObject == nullptr ||
                    !(*FootObject)->TryGetObjectField(TEXT("targetMeters"), Target) || Target == nullptr ||
                    !(*FootObject)->TryGetObjectField(TEXT("lockMeters"), Lock) || Lock == nullptr ||
                    !(*FootObject)->TryGetBoolField(TEXT("relevant"), bRelevant) ||
                    !ReadP5aVectorMeters(*Target, Foot.Value->TargetLocationWorldSpace) ||
                    !ReadP5aVectorMeters(*Lock, Foot.Value->LockLocationWorldSpace))
                {
                    return false;
                }
                Foot.Value->LockAmount = bRelevant ? 1.0f : 0.0f;
            }
            Case.Frames.Add(MoveTemp(Frame));
            ++TotalFrameCount;
        }
        Cases.Add(MoveTemp(Case));
    }
    return TotalFrameCount == ExpectedTotalFrameCount;
}

bool ResolveP5aNativeTransitionStimulusContract(FP5aNativeTransitionStimulusContract& Contract)
{
    const FString ContractSha256{Sha256Hex(
        reinterpret_cast<const uint8*>(P5aNativeTransitionStimulusContractText),
        sizeof(P5aNativeTransitionStimulusContractText) - 1)};
    if (ContractSha256 != P5aNativeTransitionStimulusContractSha256) return false;

    UClass* AnimationClass{UAlsAnimationInstance::StaticClass()};
    Contract.FeetState = FindFProperty<FStructProperty>(AnimationClass, TEXT("FeetState"));
    Contract.TransitionsState = FindFProperty<FStructProperty>(AnimationClass, TEXT("TransitionsState"));
    Contract.DynamicTransitionsState = FindFProperty<FStructProperty>(AnimationClass, TEXT("DynamicTransitionsState"));
    if (Contract.FeetState == nullptr || Contract.FeetState->Struct != FAlsFeetState::StaticStruct() ||
        Contract.TransitionsState == nullptr || Contract.TransitionsState->Struct != FAlsTransitionsState::StaticStruct() ||
        Contract.DynamicTransitionsState == nullptr ||
        Contract.DynamicTransitionsState->Struct != FAlsDynamicTransitionsState::StaticStruct())
    {
        return false;
    }

    Contract.Left = FindFProperty<FStructProperty>(Contract.FeetState->Struct, TEXT("Left"));
    Contract.Right = FindFProperty<FStructProperty>(Contract.FeetState->Struct, TEXT("Right"));
    if (Contract.Left == nullptr || Contract.Right == nullptr ||
        Contract.Left->Struct != FAlsFootState::StaticStruct() ||
        Contract.Right->Struct != FAlsFootState::StaticStruct())
    {
        return false;
    }
    Contract.TargetLocationWorldSpace = FindFProperty<FStructProperty>(
        Contract.Left->Struct, TEXT("TargetLocationWorldSpace"));
    Contract.LockLocationWorldSpace = FindFProperty<FStructProperty>(
        Contract.Left->Struct, TEXT("LockLocationWorldSpace"));
    Contract.LockAmount = FindFProperty<FFloatProperty>(Contract.Left->Struct, TEXT("LockAmount"));
    Contract.bTransitionsAllowed = FindFProperty<FBoolProperty>(
        Contract.TransitionsState->Struct, TEXT("bTransitionsAllowed"));
    Contract.bUpdatedThisFrame = FindFProperty<FBoolProperty>(
        Contract.DynamicTransitionsState->Struct, TEXT("bUpdatedThisFrame"));
    Contract.FrameDelay = FindFProperty<FIntProperty>(
        Contract.DynamicTransitionsState->Struct, TEXT("FrameDelay"));
    Contract.RefreshDynamicTransitions = AnimationClass->FindFunctionByName(
        TEXT("RefreshDynamicTransitions"), EIncludeSuperFlag::IncludeSuper);
    return Contract.TargetLocationWorldSpace != nullptr &&
        Contract.TargetLocationWorldSpace->Struct == TBaseStructure<FVector>::Get() &&
        Contract.LockLocationWorldSpace != nullptr &&
        Contract.LockLocationWorldSpace->Struct == TBaseStructure<FVector>::Get() &&
        Contract.LockAmount != nullptr && Contract.bTransitionsAllowed != nullptr &&
        Contract.bUpdatedThisFrame != nullptr && Contract.FrameDelay != nullptr &&
        Contract.RefreshDynamicTransitions != nullptr &&
        Contract.RefreshDynamicTransitions->NumParms == 0 &&
        Contract.RefreshDynamicTransitions->GetReturnProperty() == nullptr;
}

bool ApplyP5aNativeTransitionStimulus(
    UAlsAnimationInstance* Instance,
    const FP5aNativeTransitionStimulusContract& Contract,
    const FP5aNativeTransitionStimulusInput& Input,
    const bool bPreHookTransitionActive,
    FP5aNativeTransitionStimulusReceipt& Receipt)
{
    if (!IsValid(Instance) || Contract.FeetState == nullptr || Contract.Left == nullptr ||
        Contract.Right == nullptr || Contract.TargetLocationWorldSpace == nullptr ||
        Contract.LockLocationWorldSpace == nullptr || Contract.LockAmount == nullptr ||
        Contract.TransitionsState == nullptr || Contract.bTransitionsAllowed == nullptr ||
        Contract.DynamicTransitionsState == nullptr || Contract.bUpdatedThisFrame == nullptr ||
        Contract.FrameDelay == nullptr || Contract.RefreshDynamicTransitions == nullptr)
    {
        return false;
    }

    void* FeetState{Contract.FeetState->ContainerPtrToValuePtr<void>(Instance)};
    void* LeftState{Contract.Left->ContainerPtrToValuePtr<void>(FeetState)};
    void* RightState{Contract.Right->ContainerPtrToValuePtr<void>(FeetState)};
    void* TransitionsState{Contract.TransitionsState->ContainerPtrToValuePtr<void>(Instance)};
    void* DynamicTransitionsState{Contract.DynamicTransitionsState->ContainerPtrToValuePtr<void>(Instance)};
    if (FeetState == nullptr || LeftState == nullptr || RightState == nullptr ||
        TransitionsState == nullptr || DynamicTransitionsState == nullptr || bPreHookTransitionActive)
    {
        return false;
    }

    FAlsFootState LeftBefore;
    FAlsFootState RightBefore;
    FMemory::Memcpy(&LeftBefore.TargetLocationWorldSpace,
        Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState),
        sizeof LeftBefore.TargetLocationWorldSpace);
    FMemory::Memcpy(&LeftBefore.LockLocationWorldSpace,
        Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState),
        sizeof LeftBefore.LockLocationWorldSpace);
    FMemory::Memcpy(&LeftBefore.LockAmount,
        Contract.LockAmount->ContainerPtrToValuePtr<void>(LeftState), sizeof LeftBefore.LockAmount);
    FMemory::Memcpy(&RightBefore.TargetLocationWorldSpace,
        Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState),
        sizeof RightBefore.TargetLocationWorldSpace);
    FMemory::Memcpy(&RightBefore.LockLocationWorldSpace,
        Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState),
        sizeof RightBefore.LockLocationWorldSpace);
    FMemory::Memcpy(&RightBefore.LockAmount,
        Contract.LockAmount->ContainerPtrToValuePtr<void>(RightState), sizeof RightBefore.LockAmount);

    Receipt.ObservedAllowTransitions =
        Contract.bTransitionsAllowed->GetPropertyValue_InContainer(TransitionsState);
    Receipt.PreHookUpdatedThisFrame =
        Contract.bUpdatedThisFrame->GetPropertyValue_InContainer(DynamicTransitionsState);
    Receipt.PreHookFrameDelay = Contract.FrameDelay->GetPropertyValue_InContainer(DynamicTransitionsState);
    Receipt.PreHookTransitionActive = bPreHookTransitionActive;
    if (!Receipt.ObservedAllowTransitions || Receipt.PreHookFrameDelay != 0)
    {
        return false;
    }

    {
        ON_SCOPE_EXIT
        {
            Contract.TargetLocationWorldSpace->CopyCompleteValue_InContainer(LeftState, &LeftBefore);
            Contract.LockLocationWorldSpace->CopyCompleteValue_InContainer(LeftState, &LeftBefore);
            Contract.LockAmount->SetPropertyValue_InContainer(LeftState, LeftBefore.LockAmount);
            Contract.TargetLocationWorldSpace->CopyCompleteValue_InContainer(RightState, &RightBefore);
            Contract.LockLocationWorldSpace->CopyCompleteValue_InContainer(RightState, &RightBefore);
            Contract.LockAmount->SetPropertyValue_InContainer(RightState, RightBefore.LockAmount);
        };

        Contract.TargetLocationWorldSpace->CopyCompleteValue_InContainer(LeftState, &Input.Left);
        Contract.LockLocationWorldSpace->CopyCompleteValue_InContainer(LeftState, &Input.Left);
        Contract.LockAmount->SetPropertyValue_InContainer(LeftState, Input.Left.LockAmount);
        Contract.TargetLocationWorldSpace->CopyCompleteValue_InContainer(RightState, &Input.Right);
        Contract.LockLocationWorldSpace->CopyCompleteValue_InContainer(RightState, &Input.Right);
        Contract.LockAmount->SetPropertyValue_InContainer(RightState, Input.Right.LockAmount);
        Contract.bUpdatedThisFrame->SetPropertyValue_InContainer(DynamicTransitionsState, false);

        Instance->UObject::ProcessEvent(Contract.RefreshDynamicTransitions, nullptr);

        FMemory::Memcpy(&Receipt.ObservedLeftTarget,
            Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState),
            sizeof Receipt.ObservedLeftTarget);
        FMemory::Memcpy(&Receipt.ObservedLeftLock,
            Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState),
            sizeof Receipt.ObservedLeftLock);
        Receipt.ObservedLeftLockAmount = Contract.LockAmount->GetPropertyValue_InContainer(LeftState);
        FMemory::Memcpy(&Receipt.ObservedRightTarget,
            Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState),
            sizeof Receipt.ObservedRightTarget);
        FMemory::Memcpy(&Receipt.ObservedRightLock,
            Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState),
            sizeof Receipt.ObservedRightLock);
        Receipt.ObservedRightLockAmount = Contract.LockAmount->GetPropertyValue_InContainer(RightState);
        Receipt.PostHookUpdatedThisFrame =
            Contract.bUpdatedThisFrame->GetPropertyValue_InContainer(DynamicTransitionsState);
        Receipt.PostHookFrameDelay = Contract.FrameDelay->GetPropertyValue_InContainer(DynamicTransitionsState);
    }

    FVector RestoredLeftTarget{ForceInit};
    FVector RestoredLeftLock{ForceInit};
    FVector RestoredRightTarget{ForceInit};
    FVector RestoredRightLock{ForceInit};
    FMemory::Memcpy(&RestoredLeftTarget,
        Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState), sizeof RestoredLeftTarget);
    FMemory::Memcpy(&RestoredLeftLock,
        Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(LeftState), sizeof RestoredLeftLock);
    const float RestoredLeftLockAmount{Contract.LockAmount->GetPropertyValue_InContainer(LeftState)};
    FMemory::Memcpy(&RestoredRightTarget,
        Contract.TargetLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState), sizeof RestoredRightTarget);
    FMemory::Memcpy(&RestoredRightLock,
        Contract.LockLocationWorldSpace->ContainerPtrToValuePtr<void>(RightState), sizeof RestoredRightLock);
    const float RestoredRightLockAmount{Contract.LockAmount->GetPropertyValue_InContainer(RightState)};
    Receipt.RestoreVerified =
        FMemory::Memcmp(&RestoredLeftTarget, &LeftBefore.TargetLocationWorldSpace, sizeof RestoredLeftTarget) == 0 &&
        FMemory::Memcmp(&RestoredLeftLock, &LeftBefore.LockLocationWorldSpace, sizeof RestoredLeftLock) == 0 &&
        FMemory::Memcmp(&RestoredLeftLockAmount, &LeftBefore.LockAmount, sizeof RestoredLeftLockAmount) == 0 &&
        FMemory::Memcmp(&RestoredRightTarget, &RightBefore.TargetLocationWorldSpace, sizeof RestoredRightTarget) == 0 &&
        FMemory::Memcmp(&RestoredRightLock, &RightBefore.LockLocationWorldSpace, sizeof RestoredRightLock) == 0 &&
        FMemory::Memcmp(&RestoredRightLockAmount, &RightBefore.LockAmount, sizeof RestoredRightLockAmount) == 0;
    return Receipt.RestoreVerified;
}

FP5aNativePlaybackSnapshot SnapshotP5aNativePlayback(
    UAnimInstance& Animation, const FP5aNativeActionLifecycle& Lifecycle)
{
    FP5aNativePlaybackSnapshot Snapshot;
    // Queued terminal delegates run before DispatchQueuedAnimEvents deletes invalid instances.
    // The montage lookup has already been cleared; its instance ID remains observable until disposal.
    if (const FAnimMontageInstance* Instance{Animation.GetMontageInstanceForID(Lifecycle.InstanceId)})
    {
        Snapshot.bCaptured = true;
        Snapshot.Position = Instance->GetPosition();
        Snapshot.PreviousPosition = Instance->GetPreviousPosition();
        Snapshot.PlayRate = Instance->GetPlayRate();
        Snapshot.Weight = Instance->GetWeight();
        Snapshot.SectionName = Lifecycle.Montage->GetSectionName(
            Lifecycle.Montage->GetSectionIndexFromPosition(Snapshot.Position));
    }
    return Snapshot;
}

bool StartP5aNativeRollThroughPublicAlsPath(
    AAlsTraceCharacter& Character, FP5aNativeActionLifecycle& Lifecycle)
{
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    UAnimMontage* Montage{Character.SelectRollMontage()};
    if (!IsValid(Animation) || !IsValid(Montage))
    {
        return false;
    }
    Lifecycle.Montage = Montage;
    const FAnimMontageInstance* PreStartInstance{Animation->GetActiveInstanceForMontage(Montage)};
    if (PreStartInstance != nullptr)
    {
        return false;
    }

    Character.BindTraceMontageStartedObserver(Montage, [&Lifecycle, Animation, Montage](UAnimMontage*)
    {
        Lifecycle.bOnMontageStartedObserved = true;
        Lifecycle.Outcomes.Add({TEXT("Started"), TEXT("MontageStarted"), false});
        if (Lifecycle.DiagnosticCallback)
            Lifecycle.DiagnosticCallback(TEXT("MontageStarted"), false, Animation->GetActiveInstanceForMontage(Montage));
    });
    {
        ON_SCOPE_EXIT { Character.UnbindTraceMontageStartedObserver(); };
        Character.StartRollingGrounded(1.0f);
    }

    const FAnimMontageInstance* PostStartInstance{Animation->GetActiveInstanceForMontage(Montage)};
    if (!Lifecycle.bOnMontageStartedObserved || PostStartInstance == nullptr)
    {
        return false;
    }
    Lifecycle.InstanceId = PostStartInstance->GetInstanceID();

    FOnMontageBlendingOutStarted OnMontageBlendingOutStarted;
    OnMontageBlendingOutStarted.BindLambda(
        [&Lifecycle, Montage, Animation](UAnimMontage* CallbackMontage, const bool bInterrupted)
        {
            if (CallbackMontage == Montage && Lifecycle.DiagnosticCallback)
                Lifecycle.DiagnosticCallback(TEXT("MontageBlendingOutStarted"), bInterrupted,
                    Animation->GetMontageInstanceForID(Lifecycle.InstanceId));
            if (CallbackMontage == Montage && bInterrupted &&
                !Lifecycle.bOnMontageBlendingOutStartedObserved)
            {
                Lifecycle.bOnMontageBlendingOutStartedObserved = true;
                Lifecycle.TerminalPlayback = SnapshotP5aNativePlayback(*Animation, Lifecycle);
                Lifecycle.Outcomes.Add({
                    TEXT("Cancelled"), TEXT("MontageBlendingOutStarted"), true});
            }
        });
    Animation->Montage_SetBlendingOutDelegate(OnMontageBlendingOutStarted, Montage);

    FOnMontageEnded OnMontageEnded;
    OnMontageEnded.BindLambda(
        [&Lifecycle, Montage, Animation](UAnimMontage* CallbackMontage, const bool bInterrupted)
        {
            if (CallbackMontage != Montage || Lifecycle.bOnMontageEndedObserved)
            {
                return;
            }
            Lifecycle.bOnMontageEndedObserved = true;
            Lifecycle.bEndedThisFrame = true;
            if (Lifecycle.DiagnosticCallback)
                Lifecycle.DiagnosticCallback(TEXT("MontageEnded"), bInterrupted,
                    Animation->GetMontageInstanceForID(Lifecycle.InstanceId));
            Lifecycle.EndedPlayback = SnapshotP5aNativePlayback(*Animation, Lifecycle);
            if (!bInterrupted)
            {
                Lifecycle.TerminalPlayback = Lifecycle.EndedPlayback;
                Lifecycle.Outcomes.Add({TEXT("Finished"), TEXT("MontageEnded"), false});
            }
        });
    Animation->Montage_SetEndDelegate(OnMontageEnded, Montage);
    return true;
}

bool CancelP5aNativeRollThroughMontageStop(
    UAlsAnimationInstance& Animation, FP5aNativeActionLifecycle& Lifecycle)
{
    UAnimMontage* Montage{Lifecycle.Montage.Get()};
    if (!IsValid(Montage))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A Roll cancel has no lifecycle Montage."));
        return false;
    }
    const FAnimMontageInstance* PreCancelInstance{Animation.GetActiveInstanceForMontage(Montage)};
    if (PreCancelInstance == nullptr)
    {
        UE_LOG(LogTemp, Error, TEXT("P5A Roll cancel has no active Montage instance position=%.6f playing=%d"),
            Animation.Montage_GetPosition(Montage), Animation.Montage_IsPlaying(Montage) ? 1 : 0);
        return false;
    }
    Lifecycle.PreCancelPosition = PreCancelInstance->GetPosition();
    Animation.Montage_Stop(Montage->BlendOut.GetBlendTime(), Montage);
    if (!Lifecycle.bOnMontageBlendingOutStartedObserved)
    {
        UE_LOG(LogTemp, Error, TEXT("P5A Roll cancel did not observe MontageBlendingOutStarted."));
    }
    return Lifecycle.bOnMontageBlendingOutStartedObserved;
}

void UnbindP5aNativeActionObservers(UAnimInstance* Animation, const FP5aNativeActionLifecycle& Lifecycle)
{
    if (!IsValid(Animation)) return;
    if (FAnimMontageInstance* Instance{Animation->GetMontageInstanceForID(Lifecycle.InstanceId)})
    {
        Instance->OnMontageEnded.Unbind();
        Instance->OnMontageBlendingOutStarted.Unbind();
    }
}

FP5aFrameUpdateAudit TickP5aWorld(UWorld& World, AAlsTraceCharacter& Character)
{
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const int16 PreviousUpdateCounter{Animation->GetUpdateCounter().Get()};
    UAlsTraceSkeletalMeshComponent* Mesh{Character.GetTraceMesh()};
    Mesh->ResetTracePipelineCounts();
    CommandletHelpers::TickEngine(&World, FixedDeltaSeconds);
    World.Tick(LEVELTICK_All, FixedDeltaSeconds);
    return {static_cast<int32>(Animation->GetUpdateCounter().Get()) - PreviousUpdateCounter,
        Mesh->GetTraceEvaluationCount(), Mesh->GetTracePostUpdateCount(), Mesh->GetTracePublicTickCount()};
}

bool GenerateP5aCase(
    UWorld& World, const int32 CaseOrdinal,
    AAlsTraceCharacter& Character,
    const FP5aFrameDefinition& Frame,
    const FP5aNativeTransitionStimulusContract& Contract,
    const FP5aNativeTransitionStimulusInput& Input,
    const bool bPreHookTransitionActive,
    FP5aNativeTransitionStimulusReceipt& Receipt,
    FP5aNativeActionLifecycle& ActionLifecycle, FP5aFrameUpdateAudit& FrameUpdateAudit)
{
    UAlsAnimationInstance* Instance{Cast<UAlsAnimationInstance>(Character.GetMesh()->GetAnimInstance())};
    bool bSuccess{IsValid(Instance)};
    if (bSuccess && Frame.ActionCommand == TEXT("Cancel"))
    {
        bSuccess = CancelP5aNativeRollThroughMontageStop(*Instance, ActionLifecycle);
    }
    if (!bSuccess) return false;
    FrameUpdateAudit = TickP5aWorld(World, Character);
    if (ActionLifecycle.DiagnosticAfterUpdate) ActionLifecycle.DiagnosticAfterUpdate();
    if (FrameUpdateAudit.AnimationUpdates != 1 || FrameUpdateAudit.Evaluations != 1 ||
        FrameUpdateAudit.PostUpdates != 1 || FrameUpdateAudit.MeshTicks != 1)
    {
        UE_LOG(LogTemp, Error, TEXT("P5A frame update audit failed: updates=%d evals=%d posts=%d ticks=%d"),
            FrameUpdateAudit.AnimationUpdates, FrameUpdateAudit.Evaluations,
            FrameUpdateAudit.PostUpdates, FrameUpdateAudit.MeshTicks);
        return false;
    }
    const bool bHasTransitionStimulus{
        Frame.TransitionStimulus.Left.LockAmount > 0.0f ||
        Frame.TransitionStimulus.Right.LockAmount > 0.0f};
    if (bSuccess && bHasTransitionStimulus && Frame.FrameIndex == 0 &&
        (CaseOrdinal == 2 || CaseOrdinal == 3 || CaseOrdinal == 4))
    {
        bSuccess = ApplyP5aNativeTransitionStimulus(
            Instance, Contract, Input, bPreHookTransitionActive, Receipt);
    }
    if (bSuccess && Frame.ActionCommand == TEXT("Start"))
    {
        bSuccess = StartP5aNativeRollThroughPublicAlsPath(Character, ActionLifecycle);
    }
    return bSuccess;
}

FString CreateP5aSha1(const FString& Value)
{
    const FTCHARToUTF8 Utf8(*Value);
    uint8 Digest[FSHA1::DigestSize];
    FSHA1::HashBuffer(Utf8.Get(), Utf8.Length(), Digest);
    return BytesToHex(Digest, UE_ARRAY_COUNT(Digest)).ToLower();
}

FString CreateP5aAssetStableId(const FString& ObjectPath)
{
    const FTCHARToUTF8 Utf8(*ObjectPath);
    uint8 Digest[FSHA1::DigestSize];
    FSHA1::HashBuffer(Utf8.Get(), Utf8.Length(), Digest);
    return BytesToHex(Digest, UE_ARRAY_COUNT(Digest)).ToLower();
}

FString CreateP5aEventStableId(
    const FString& AssetStableId, const int32 SourceIndex, const FString& SourceClassPath)
{
    return CreateP5aSha1(FString::Printf(
        TEXT("%s|timeline|%d|%s"), *AssetStableId, SourceIndex, *SourceClassPath));
}

FString CreateP5aMarkerStableId(
    const FString& AssetStableId, const int32 SourceIndex, const FString& Name)
{
    return CreateP5aSha1(FString::Printf(
        TEXT("%s|marker|%d|%s"), *AssetStableId, SourceIndex, *Name));
}

FString CreateP5aPackageSha256(const UAnimationAsset& Asset)
{
    FString PackageFilename;
    if (!FPackageName::DoesPackageExist(Asset.GetOutermost()->GetName(), &PackageFilename))
    {
        return {};
    }
    TArray<uint8> PackageBytes;
    if (!FFileHelper::LoadFileToArray(PackageBytes, *PackageFilename) || PackageBytes.IsEmpty())
    {
        return {};
    }
    uint8 Digest[SHA256_DIGEST_LENGTH];
    if (SHA256(PackageBytes.GetData(), static_cast<size_t>(PackageBytes.Num()), Digest) == nullptr)
    {
        return {};
    }
    return BytesToHex(Digest, SHA256_DIGEST_LENGTH).ToLower();
}

bool CollectP5aNativeReferenceAudit(FP5aNativeReferenceAudit& Audit)
{
    struct FPhysicalSource
    {
        const TCHAR* AssetObjectPath;
        const TCHAR* MontageObjectPath;
        const TCHAR* SectionName;
        const TCHAR* SlotName;
        int32 SegmentIndex;
    };
    static constexpr FPhysicalSource Sources[]{
        {TEXT("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose"), TEXT(""), TEXT(""), TEXT(""), -1},
        {TEXT("/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward"), TEXT(""), TEXT(""), TEXT(""), -1},
        {TEXT("/ALS/ALS/Animations/Base/A_Als_Crouch_Pose.A_Als_Crouch_Pose"), TEXT(""), TEXT(""), TEXT(""), -1},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Left.A_Als_Turn_90_Left"), TEXT(""), TEXT(""), TEXT(""), -1},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Left.A_Als_Rotate_90_Left"), TEXT(""), TEXT(""), TEXT(""), -1},
        {TEXT("/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Left.A_Als_Stand_DynamicTransition_Left"), TEXT(""), TEXT(""), TEXT("Transition"), -1},
        {TEXT("/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Left.A_Als_Crouch_DynamicTransition_Left"), TEXT(""), TEXT(""), TEXT("Transition"), -1},
        {TEXT("/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Right.A_Als_Stand_DynamicTransition_Right"), TEXT(""), TEXT(""), TEXT("Transition"), -1},
        {TEXT("/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Right.A_Als_Crouch_DynamicTransition_Right"), TEXT(""), TEXT(""), TEXT("Transition"), -1},
        {TEXT("/ALS/ALS/Animations/Actions/Roll/AM_Als_Roll.AM_Als_Roll"), TEXT("/ALS/ALS/Animations/Actions/Roll/AM_Als_Roll.AM_Als_Roll"), TEXT("Default"), TEXT("PostLocomotion"), -1},
        {TEXT("/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll"), TEXT("/ALS/ALS/Animations/Actions/Roll/AM_Als_Roll.AM_Als_Roll"), TEXT(""), TEXT("PostLocomotion"), 0},
    };
    static_assert(UE_ARRAY_COUNT(Sources) == 11);

    Audit = {};
    for (int32 AssetIndex{0}; AssetIndex < UE_ARRAY_COUNT(Sources); ++AssetIndex)
    {
        const FPhysicalSource& Source{Sources[AssetIndex]};
        UAnimationAsset* Asset{LoadObject<UAnimationAsset>(nullptr, Source.AssetObjectPath)};
        UAnimSequenceBase* SequenceBase{Cast<UAnimSequenceBase>(Asset)};
        if (!IsValid(Asset) || !IsValid(SequenceBase) || Asset->GetPathName() != Source.AssetObjectPath)
        {
            return false;
        }

        FP5aNativeAssetAudit& AssetAudit{Audit.Assets.AddDefaulted_GetRef()};
        AssetAudit.AssetObjectPath = Asset->GetPathName();
        AssetAudit.AssetStableId = CreateP5aAssetStableId(AssetAudit.AssetObjectPath);
        AssetAudit.AssetPackageSha256 = CreateP5aPackageSha256(*Asset);
        AssetAudit.AssetClassPath = Asset->GetClass()->GetPathName();
        AssetAudit.DurationSeconds = Asset->GetPlayLength();
        AssetAudit.bAuthoredLoop = SequenceBase->bLoop;
        AssetAudit.MontageObjectPath = Source.MontageObjectPath;
        AssetAudit.MontageStableId = AssetAudit.MontageObjectPath.IsEmpty()
            ? FString{} : CreateP5aAssetStableId(AssetAudit.MontageObjectPath);
        AssetAudit.SectionName = Source.SectionName;
        AssetAudit.SlotName = Source.SlotName;
        AssetAudit.SegmentIndex = Source.SegmentIndex;
        if (AssetAudit.AssetStableId.IsEmpty() || AssetAudit.AssetPackageSha256.IsEmpty() ||
            AssetAudit.AssetClassPath.IsEmpty() || !FMath::IsFinite(AssetAudit.DurationSeconds))
        {
            return false;
        }

        for (int32 SourceIndex{0}; SourceIndex < SequenceBase->Notifies.Num(); ++SourceIndex)
        {
            const FAnimNotifyEvent& NotifyEvent{SequenceBase->Notifies[SourceIndex]};
            const UObject* NotifyObject{IsValid(NotifyEvent.NotifyStateClass.Get())
                ? static_cast<const UObject*>(NotifyEvent.NotifyStateClass.Get())
                : static_cast<const UObject*>(NotifyEvent.Notify.Get())};
            if (!IsValid(NotifyObject))
            {
                return false;
            }
            FP5aNativeEventAudit& EventAudit{Audit.Events.AddDefaulted_GetRef()};
            EventAudit.AssetStableId = AssetAudit.AssetStableId;
            EventAudit.SourceClassPath = NotifyObject->GetClass()->GetPathName();
            EventAudit.StableEventId = CreateP5aEventStableId(
                EventAudit.AssetStableId, SourceIndex, EventAudit.SourceClassPath);
            EventAudit.OwnerKind = Cast<UAnimMontage>(Asset) != nullptr
                ? TEXT("MontageTimeline") : TEXT("SequenceTimeline");
            EventAudit.SourceIndex = SourceIndex;
            EventAudit.TrackIndex = NotifyEvent.TrackIndex;
            EventAudit.TimeSeconds = NotifyEvent.GetTime();
            EventAudit.DurationSeconds = NotifyEvent.GetDuration();
            EventAudit.TriggerWeightThreshold = NotifyEvent.TriggerWeightThreshold;
            EventAudit.TickMode = NotifyEvent.MontageTickType == EMontageNotifyTickType::Queued
                ? TEXT("Queued") : TEXT("BranchingPoint");
        }

        TArray<FAnimSyncMarker> AuthoredSyncMarkers;
        if (const UAnimSequence* Sequence{Cast<UAnimSequence>(Asset)})
        {
            AuthoredSyncMarkers = Sequence->AuthoredSyncMarkers;
            if (AssetIndex < 5)
            {
                FP5aNativeCurveInventory& Inventory{Audit.CurveInventories.AddDefaulted_GetRef()};
                Inventory.AssetObjectPath = AssetAudit.AssetObjectPath;
                Inventory.AssetStableId = AssetAudit.AssetStableId;
                if (const IAnimationDataModel* DataModel{Sequence->GetDataModel()})
                {
                    for (const FFloatCurve& Curve : DataModel->GetFloatCurves())
                    {
                        Inventory.CurveNames.Add(Curve.GetName().ToString());
                    }
                    Inventory.CurveNames.Sort();
                }
            }
        }
        else if (const UAnimMontage* Montage{Cast<UAnimMontage>(Asset)})
        {
            AuthoredSyncMarkers = Montage->MarkerData.AuthoredSyncMarkers;
        }
        for (int32 SourceIndex{0}; SourceIndex < AuthoredSyncMarkers.Num(); ++SourceIndex)
        {
            const FAnimSyncMarker& Marker{AuthoredSyncMarkers[SourceIndex]};
            FP5aNativeMarkerAudit& MarkerAudit{Audit.Markers.AddDefaulted_GetRef()};
            MarkerAudit.AssetStableId = AssetAudit.AssetStableId;
            MarkerAudit.Name = Marker.MarkerName.ToString();
            MarkerAudit.StableMarkerId = CreateP5aMarkerStableId(
                MarkerAudit.AssetStableId, SourceIndex, MarkerAudit.Name);
            MarkerAudit.SourceIndex = SourceIndex;
#if WITH_EDITORONLY_DATA
            MarkerAudit.TrackIndex = Marker.TrackIndex;
#else
            MarkerAudit.TrackIndex = 0;
#endif
            MarkerAudit.TimeSeconds = Marker.Time;
        }
    }

    return Audit.Assets.Num() == 11 && Audit.Events.Num() == 19 && Audit.Markers.Num() == 2 &&
        Audit.CurveInventories.Num() == 5;
}

void SetP5aFloatField(const TSharedRef<FJsonObject>& Object, const TCHAR* Name, const float Value)
{
    char Buffer[64]{};
    const auto Conversion{std::to_chars(Buffer, Buffer + UE_ARRAY_COUNT(Buffer) - 1, Value)};
    check(Conversion.ec == std::errc{});
    *Conversion.ptr = '\0';
    Object->SetField(Name, MakeShared<FJsonValueNumberString>(FString{ANSI_TO_TCHAR(Buffer)}));
}

TSharedRef<FJsonObject> P5aFloatVectorToJson(const FVector& Value)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    SetP5aFloatField(Object, TEXT("x"), static_cast<float>(Value.X / 100.0));
    SetP5aFloatField(Object, TEXT("y"), static_cast<float>(Value.Y / 100.0));
    SetP5aFloatField(Object, TEXT("z"), static_cast<float>(Value.Z / 100.0));
    return Object;
}

TSharedRef<FJsonObject> P5aNativeReferenceAuditToJson(const FP5aNativeReferenceAudit& Audit)
{
    TArray<TSharedPtr<FJsonValue>> Assets;
    for (const FP5aNativeAssetAudit& Item : Audit.Assets)
    {
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("assetObjectPath"), Item.AssetObjectPath);
        Value->SetStringField(TEXT("assetStableId"), Item.AssetStableId);
        Value->SetStringField(TEXT("assetPackageSha256"), Item.AssetPackageSha256);
        Value->SetStringField(TEXT("assetClassPath"), Item.AssetClassPath);
        SetP5aFloatField(Value, TEXT("durationSeconds"), static_cast<float>(Item.DurationSeconds));
        Value->SetBoolField(TEXT("authoredLoop"), Item.bAuthoredLoop);
        Value->SetStringField(TEXT("montageObjectPath"), Item.MontageObjectPath);
        Value->SetStringField(TEXT("montageStableId"), Item.MontageStableId);
        Value->SetStringField(TEXT("sectionName"), Item.SectionName);
        Value->SetStringField(TEXT("slotName"), Item.SlotName);
        Value->SetNumberField(TEXT("segmentIndex"), Item.SegmentIndex);
        Assets.Add(MakeShared<FJsonValueObject>(Value));
    }
    TArray<TSharedPtr<FJsonValue>> Events;
    for (const FP5aNativeEventAudit& Item : Audit.Events)
    {
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("assetStableId"), Item.AssetStableId);
        Value->SetStringField(TEXT("stableEventId"), Item.StableEventId);
        Value->SetStringField(TEXT("ownerKind"), Item.OwnerKind);
        Value->SetStringField(TEXT("sourceClassPath"), Item.SourceClassPath);
        Value->SetNumberField(TEXT("sourceIndex"), Item.SourceIndex);
        Value->SetNumberField(TEXT("trackIndex"), Item.TrackIndex);
        SetP5aFloatField(Value, TEXT("timeSeconds"), static_cast<float>(Item.TimeSeconds));
        SetP5aFloatField(Value, TEXT("durationSeconds"), static_cast<float>(Item.DurationSeconds));
        SetP5aFloatField(Value, TEXT("triggerWeightThreshold"),
            static_cast<float>(Item.TriggerWeightThreshold));
        Value->SetStringField(TEXT("tickMode"), Item.TickMode);
        Events.Add(MakeShared<FJsonValueObject>(Value));
    }
    TArray<TSharedPtr<FJsonValue>> Markers;
    for (const FP5aNativeMarkerAudit& Item : Audit.Markers)
    {
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("assetStableId"), Item.AssetStableId);
        Value->SetStringField(TEXT("stableMarkerId"), Item.StableMarkerId);
        Value->SetStringField(TEXT("name"), Item.Name);
        Value->SetNumberField(TEXT("sourceIndex"), Item.SourceIndex);
        Value->SetNumberField(TEXT("trackIndex"), Item.TrackIndex);
        SetP5aFloatField(Value, TEXT("timeSeconds"), static_cast<float>(Item.TimeSeconds));
        Markers.Add(MakeShared<FJsonValueObject>(Value));
    }
    TArray<TSharedPtr<FJsonValue>> CurveInventories;
    for (const FP5aNativeCurveInventory& Item : Audit.CurveInventories)
    {
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("assetObjectPath"), Item.AssetObjectPath);
        Value->SetStringField(TEXT("assetStableId"), Item.AssetStableId);
        TArray<TSharedPtr<FJsonValue>> CurveNames;
        for (const FString& CurveName : Item.CurveNames)
        {
            CurveNames.Add(MakeShared<FJsonValueString>(CurveName));
        }
        Value->SetArrayField(TEXT("curveNames"), CurveNames);
        CurveInventories.Add(MakeShared<FJsonValueObject>(Value));
    }
    const TSharedRef<FJsonObject> Result{MakeShared<FJsonObject>()};
    Result->SetArrayField(TEXT("assets"), Assets);
    Result->SetArrayField(TEXT("events"), Events);
    Result->SetArrayField(TEXT("markers"), Markers);
    Result->SetArrayField(TEXT("curveInventories"), CurveInventories);
    Result->SetArrayField(TEXT("auxiliaryAssets"), Audit.AuxiliaryAssets);
    return Result;
}

FString GaitName(const FGameplayTag& Tag)
{
    if (Tag == AlsGaitTags::Walking) return TEXT("Walking");
    if (Tag == AlsGaitTags::Sprinting) return TEXT("Sprinting");
    return TEXT("Running");
}

FString StanceName(const FGameplayTag& Tag)
{
    return Tag == AlsStanceTags::Crouching ? TEXT("Crouching") : TEXT("Standing");
}

FString RotationModeName(const FGameplayTag& Tag)
{
    if (Tag == AlsRotationModeTags::VelocityDirection) return TEXT("VelocityDirection");
    if (Tag == AlsRotationModeTags::Aiming) return TEXT("Aiming");
    return TEXT("LookingDirection");
}

template <typename T>
const T* GetAnimationState(const UAlsAnimationInstance* AnimationInstance, const TCHAR* PropertyName)
{
    if (!IsValid(AnimationInstance)) return nullptr;
    const FStructProperty* Property{FindFProperty<FStructProperty>(AnimationInstance->GetClass(), PropertyName)};
    if (Property == nullptr || Property->Struct != T::StaticStruct()) return nullptr;
    return Property->ContainerPtrToValuePtr<T>(AnimationInstance);
}

TSharedRef<FJsonObject> Vector2Object(const double X, const double Y)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), X);
    Object->SetNumberField(TEXT("y"), Y);
    return Object;
}

TArray<TSharedPtr<FJsonValue>> PatchHashArray()
{
    return {MakeShared<FJsonValueString>(LockedPatchHash)};
}

TSharedRef<FJsonObject> Vector3Object(const FVector& Value)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X / 100.0);
    Object->SetNumberField(TEXT("y"), Value.Y / 100.0);
    Object->SetNumberField(TEXT("z"), Value.Z / 100.0);
    return Object;
}

float DamperExactAlpha(const float HalfLife)
{
    return HalfLife <= 0.0f ? 1.0f : 1.0f - FMath::Exp2(-static_cast<float>(FixedDeltaSeconds) / HalfLife);
}

float NormalizeRadians(const float Angle)
{
    return FMath::DegreesToRadians(FMath::UnwindDegrees(FMath::RadiansToDegrees(Angle)));
}

float InterpolateAngleConstant(const float Current, const float Target, const float Speed)
{
    const float Delta{NormalizeRadians(Target - Current)};
    const float MaxDelta{Speed * static_cast<float>(FixedDeltaSeconds)};
    if (Speed <= 0.0f || FMath::Abs(Delta) <= MaxDelta)
    {
        return NormalizeRadians(Target);
    }
    return NormalizeRadians(Current + FMath::Sign(Delta) * MaxDelta);
}

float SampleDirectionalSpeed(const FPortDirectionalSpeeds& Speeds, const float LocalYaw,
                             const FPortSettings& Settings)
{
    const float Amount{1.0f - FMath::Clamp(
        (FMath::Abs(NormalizeRadians(LocalYaw)) - Settings.VelocityAngleStart) /
        (Settings.VelocityAngleEnd - Settings.VelocityAngleStart), 0.0f, 1.0f)};
    return FMath::Lerp(Speeds.Backward, Speeds.Forward, Amount);
}

FPortDirectionalSpeeds MakeDirectional(const float Forward, const float Backward,
                                       const float AngleStart, const float AngleEnd)
{
    const float Amount{1.0f - FMath::Clamp((PI * 0.5f - AngleStart) / (AngleEnd - AngleStart), 0.0f, 1.0f)};
    return {Forward, FMath::Lerp(Backward, Forward, Amount), Backward};
}

FPortSettings CreatePortSettings(const AAlsTraceCharacter& Character)
{
    const UAlsMovementSettings* MovementSettings{Character.GetTraceMovementSettings()};
    const UAlsAnimationInstanceSettings* AnimationSettings{Character.GetTraceAnimationSettings()};
    const UAlsCharacterSettings* CharacterSettings{Character.GetSettings()};
    const FAlsMovementStanceSettings* RotationSettings{MovementSettings->RotationModes.Find(AlsRotationModeTags::ViewDirection)};
    const FAlsMovementGaitSettings* Standing{RotationSettings->Stances.Find(AlsStanceTags::Standing)};
    const FAlsMovementGaitSettings* Crouching{RotationSettings->Stances.Find(AlsStanceTags::Crouching)};
    FPortSettings Settings;
    Settings.VelocityAngleStart = FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Min);
    Settings.VelocityAngleEnd = FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Max);
    Settings.Standing.Walking = MakeDirectional(Standing->WalkForwardSpeed / 100.0f, Standing->WalkBackwardSpeed / 100.0f,
                                                Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Standing.Running = MakeDirectional(Standing->RunForwardSpeed / 100.0f, Standing->RunBackwardSpeed / 100.0f,
                                                Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Standing.Sprinting = MakeDirectional(Standing->SprintSpeed / 100.0f, Standing->SprintSpeed / 100.0f,
                                                  Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Walking = MakeDirectional(Crouching->WalkForwardSpeed / 100.0f, Crouching->WalkForwardSpeed / 100.0f,
                                                 Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Running = MakeDirectional(Crouching->RunForwardSpeed / 100.0f, Crouching->RunForwardSpeed / 100.0f,
                                                 Settings.VelocityAngleStart, Settings.VelocityAngleEnd);
    Settings.Crouching.Sprinting = Settings.Crouching.Running;
    Settings.AnimatedWalkSpeed = AnimationSettings->Standing.AnimatedWalkSpeed / 100.0f;
    Settings.AnimatedRunSpeed = AnimationSettings->Standing.AnimatedRunSpeed / 100.0f;
    Settings.AnimatedSprintSpeed = AnimationSettings->Standing.AnimatedSprintSpeed / 100.0f;
    Settings.AnimatedCrouchSpeed = AnimationSettings->Crouching.AnimatedCrouchSpeed / 100.0f;
    Settings.MovingSpeedThreshold = CharacterSettings->MovingSpeedThreshold / 100.0f;
    Settings.LeanHalfLife = AnimationSettings->General.LeanInterpolationHalfLife;
    return Settings;
}

FGameplayTag ResolveMaximumGait(const FTraceCommand& Command, const FGameplayTag& ActualStance)
{
    if (Command.Gait != AlsGaitTags::Sprinting) return Command.Gait;
    if (ActualStance == AlsStanceTags::Crouching || Command.RotationMode == AlsRotationModeTags::Aiming)
    {
        return AlsGaitTags::Running;
    }
    const FVector2D Direction{Command.MovementAxes.GetSafeNormal()};
    if (Direction.IsNearlyZero() ||
        (Command.RotationMode == AlsRotationModeTags::ViewDirection && Direction.Y <= FMath::Cos(FMath::DegreesToRadians(50.0f))))
    {
        return AlsGaitTags::Running;
    }
    return AlsGaitTags::Sprinting;
}

FPortResult EvaluatePort(const AAlsTraceCharacter& Character, const FTraceCommand& Command,
                          const bool bJumpTransition, const FVector& ActualAcceleration,
                          const FPortSettings& Settings, FPortState& State)
{
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const bool bGrounded{Movement->IsMovingOnGround()};
    const bool bLanded{!State.bGrounded && bGrounded};
    const bool bJumped{State.bGrounded && !bGrounded && bJumpTransition && Command.bJumpPressed};
    const FVector LocalVelocity{Character.GetActorQuat().UnrotateVector(Character.GetVelocity()) / 100.0f};
    const FVector LocalAcceleration{Character.GetActorQuat().UnrotateVector(ActualAcceleration) / 100.0f};
    const FVector2D Velocity{LocalVelocity.Y, LocalVelocity.X};
    const FVector2D Acceleration{LocalAcceleration.Y, LocalAcceleration.X};
    const float Speed{static_cast<float>(FVector2D{Character.GetVelocity().X, Character.GetVelocity().Y}.Size() / 100.0)};
    const float RawLocalYaw{Speed > 0.0f
        ? static_cast<float>(FMath::Atan2(-Velocity.X, Velocity.Y))
        : 0.0f};
    // System.MathF canonicalizes the exact -pi boundary to +pi in AlsMath.
    const float LocalYaw{RawLocalYaw == -PI ? PI : RawLocalYaw};
    const FGameplayTag ActualStance{Character.GetStance()};
    const FPortStanceSpeeds& StanceSpeeds{ActualStance == AlsStanceTags::Crouching ? Settings.Crouching : Settings.Standing};
    const float WalkSpeed{SampleDirectionalSpeed(StanceSpeeds.Walking, LocalYaw, Settings)};
    const float RunSpeed{SampleDirectionalSpeed(StanceSpeeds.Running, LocalYaw, Settings)};
    const FGameplayTag MaximumGait{ResolveMaximumGait(Command, ActualStance)};
    const FGameplayTag ActualGait{Speed < WalkSpeed + 0.1f
        ? AlsGaitTags::Walking
        : (Speed < RunSpeed + 0.1f || MaximumGait != AlsGaitTags::Sprinting
            ? AlsGaitTags::Running : AlsGaitTags::Sprinting)};

    FString AnimationState{TEXT("Grounded")};
    if (!bGrounded)
    {
        State.LandingRecoveryTime = 0.0f;
        if (bJumped)
        {
            State.bJumpStartActive = true;
            AnimationState = TEXT("JumpStart");
        }
        else if (State.bJumpStartActive && Character.GetVelocity().Z > 0.0)
        {
            AnimationState = TEXT("JumpStart");
        }
        else
        {
            State.bJumpStartActive = false;
            AnimationState = TEXT("FallLoop");
        }
    }
    else if (bLanded)
    {
        State.bJumpStartActive = false;
        State.LandingRecoveryTime = FMath::Max(0.0f, 0.2f - static_cast<float>(FixedDeltaSeconds));
        AnimationState = TEXT("LandRecovery");
    }
    else if (State.LandingRecoveryTime > 0.0f)
    {
        State.LandingRecoveryTime = State.LandingRecoveryTime <= static_cast<float>(FixedDeltaSeconds) + 1.0e-6f
            ? 0.0f : State.LandingRecoveryTime - static_cast<float>(FixedDeltaSeconds);
        AnimationState = TEXT("LandRecovery");
    }
    else
    {
        State.bJumpStartActive = false;
    }

    const float ActorYaw{FMath::DegreesToRadians(
        static_cast<float>(Character.GetActorRotation().Yaw))};
    float TargetYaw{ActorYaw};
    const float HorizontalAcceleration{static_cast<float>(
        FVector2D{ActualAcceleration.X, ActualAcceleration.Y}.Size() / 100.0)};
    const bool bStationaryYawCandidate{
        bGrounded &&
        Speed <= StationaryYawSpeedThreshold &&
        HorizontalAcceleration <= StationaryYawAccelerationThreshold &&
        (Command.RotationMode == AlsRotationModeTags::ViewDirection ||
         Command.RotationMode == AlsRotationModeTags::Aiming)};
    if (bStationaryYawCandidate ||
        (Speed <= Settings.MovingSpeedThreshold &&
         Command.RotationMode == AlsRotationModeTags::VelocityDirection))
    {
        State.SmoothedTargetYaw = ActorYaw;
    }
    else
    {
        const float RawVelocityYaw{Speed > 1.0e-6f
            ? static_cast<float>(FMath::Atan2(Character.GetVelocity().Y, Character.GetVelocity().X))
            : ActorYaw};
        // Port output is converted from UE yaw to Godot yaw by negation. Preserve Core's +pi
        // canonical boundary by emitting -pi before that conversion.
        const float VelocityYaw{RawVelocityYaw == PI ? -PI : RawVelocityYaw};
        float SelectedTargetYaw;
        if (Command.RotationMode == AlsRotationModeTags::VelocityDirection ||
            (Command.RotationMode == AlsRotationModeTags::ViewDirection && ActualGait == AlsGaitTags::Sprinting))
        {
            SelectedTargetYaw = VelocityYaw;
        }
        else if (Command.RotationMode == AlsRotationModeTags::ViewDirection)
        {
            // P3A has no RotationYawOffsetCurve input. Its documented zero-curve fallback
            // follows view yaw; nativeActual remains an independent ALS observation.
            SelectedTargetYaw = NormalizeRadians(FMath::DegreesToRadians(Command.ViewYaw));
        }
        else
        {
            SelectedTargetYaw = FMath::DegreesToRadians(Command.AimYaw);
        }

        const float PreviousSmoothedTargetYaw{State.bInitialized ? State.SmoothedTargetYaw : ActorYaw};
        if (Command.RotationMode == AlsRotationModeTags::Aiming)
        {
            State.SmoothedTargetYaw = SelectedTargetYaw;
        }
        else
        {
            const float TargetYawSpeed{Command.RotationMode == AlsRotationModeTags::VelocityDirection
                ? FMath::DegreesToRadians(800.0f)
                : FMath::DegreesToRadians(500.0f)};
            State.SmoothedTargetYaw = InterpolateAngleConstant(
                PreviousSmoothedTargetYaw, SelectedTargetYaw, TargetYawSpeed);
        }
        const float Delta{NormalizeRadians(State.SmoothedTargetYaw - ActorYaw)};
        TargetYaw = NormalizeRadians(ActorYaw + Delta * DamperExactAlpha(0.1f));
    }

    const FPortDirectionalSpeeds& GaitSpeeds{ActualGait == AlsGaitTags::Walking ? StanceSpeeds.Walking :
        (ActualGait == AlsGaitTags::Running ? StanceSpeeds.Running : StanceSpeeds.Sprinting)};
    const float ReferenceSpeed{SampleDirectionalSpeed(GaitSpeeds, LocalYaw, Settings)};
    const float Stride{ReferenceSpeed > 1.0e-6f ? FMath::Clamp(Speed / ReferenceSpeed, 0.0f, 1.0f) : 0.0f};
    const float AnimatedSpeed{ActualStance == AlsStanceTags::Crouching ? Settings.AnimatedCrouchSpeed :
        (ActualGait == AlsGaitTags::Walking ? Settings.AnimatedWalkSpeed :
            (ActualGait == AlsGaitTags::Running ? Settings.AnimatedRunSpeed : Settings.AnimatedSprintSpeed))};
    const float Denominator{AnimatedSpeed * FMath::Max(Stride, 0.0001f)};
    const float RawPlayRate{Denominator > 1.0e-6f ? Speed / Denominator : 0.0f};
    const float PlayRate{FMath::Clamp(RawPlayRate, 0.0001f,
        ActualStance == AlsStanceTags::Crouching ? 2.0f : 3.0f)};

    FVector2D LeanTarget{ForceInit};
    const float Limit{FVector2D::DotProduct(Velocity, Acceleration) >= 0.0f
        ? Movement->GetMaxAcceleration() / 100.0f : Movement->GetMaxBrakingDeceleration() / 100.0f};
    if (Limit > 1.0e-6f) LeanTarget = Acceleration.GetClampedToMaxSize(Limit) / Limit;
    State.SmoothedLean = FMath::Lerp(State.SmoothedLean, LeanTarget, DamperExactAlpha(Settings.LeanHalfLife));

    if (bGrounded && Speed > Settings.MovingSpeedThreshold)
    {
        State.AnimationPhase = FMath::Fmod(State.AnimationPhase + static_cast<float>(FixedDeltaSeconds) * PlayRate, 1.0f);
    }
    State.bInitialized = true;
    State.bGrounded = bGrounded;
    State.TargetYaw = TargetYaw;
    return {bGrounded ? TEXT("Grounded") : TEXT("InAir"), ActualGait, AnimationState, Velocity,
            Stride, PlayRate, State.SmoothedLean, State.AnimationPhase, TargetYaw};
}

bool SaveJson(const FString& Path, const TSharedRef<FJsonObject>& Object)
{
    FString Json;
    const TSharedRef<TJsonWriter<TCHAR, TPrettyJsonPrintPolicy<TCHAR>>> Writer{
        TJsonWriterFactory<TCHAR, TPrettyJsonPrintPolicy<TCHAR>>::Create(&Json)};
    if (!FJsonSerializer::Serialize(Object, Writer)) return false;
    Json.ReplaceInline(TEXT("\r\n"), TEXT("\n"));
    Json.AppendChar(TEXT('\n'));
    return FFileHelper::SaveStringToFile(Json, *Path, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}

#include "AlsP5aNativeInventory.inl"
#include "AlsP5aNativeDiscovery.inl"
#include "AlsP5aNativeInventory.Tests.inl"

const FP5aSourceDefinition* FindP5aSource(
    const TArray<FP5aSourceDefinition>& Sources, const FString& TraceSourceId)
{
    return Sources.FindByPredicate([&TraceSourceId](const FP5aSourceDefinition& Source)
    {
        return Source.TraceSourceId == TraceSourceId;
    });
}

TSharedRef<FJsonObject> P5aObservedSourceToJson(
    const FP5aObservedSource* Source, const bool bIncludePackage)
{
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetStringField(TEXT("observedAssetObjectPath"), Source != nullptr ? Source->AssetObjectPath : FString{});
    Value->SetStringField(TEXT("observedAssetStableId"), Source != nullptr ? Source->AssetStableId : FString{});
    if (bIncludePackage)
    {
        Value->SetStringField(TEXT("observedAssetPackageSha256"),
            Source != nullptr ? Source->AssetPackageSha256 : FString{});
    }
    Value->SetStringField(TEXT("observedAssetClassPath"), Source != nullptr ? Source->AssetClassPath : FString{});
    if (bIncludePackage)
    {
        Value->SetStringField(TEXT("observedMontageObjectPath"),
            Source != nullptr ? Source->MontageObjectPath : FString{});
    }
    Value->SetStringField(TEXT("observedMontageStableId"), Source != nullptr ? Source->MontageStableId : FString{});
    Value->SetStringField(TEXT("observedSectionName"), Source != nullptr ? Source->SectionName : FString{});
    Value->SetStringField(TEXT("observedSlotName"), Source != nullptr ? Source->SlotName : FString{});
    Value->SetNumberField(TEXT("observedSegmentIndex"), Source != nullptr ? Source->SegmentIndex : -1);
    return Value;
}

TSharedRef<FJsonObject> P5aCurveValuesToJson(
    const float LeftIk, const float RightIk, const float LeftLock,
    const float RightLock, const float AllowTransitions)
{
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    SetP5aFloatField(Value, TEXT("leftIk"), LeftIk);
    SetP5aFloatField(Value, TEXT("rightIk"), RightIk);
    SetP5aFloatField(Value, TEXT("leftLock"), LeftLock);
    SetP5aFloatField(Value, TEXT("rightLock"), RightLock);
    SetP5aFloatField(Value, TEXT("allowTransitions"), AllowTransitions);
    return Value;
}

TSharedRef<FJsonObject> P5aCurveAuditToJson(const UAnimInstance& Animation, const FName CurveName)
{
    float ObservedValue{0.0f};
    const bool bPresent{Animation.GetCurveValue(CurveName, ObservedValue)};
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetBoolField(TEXT("present"), bPresent);
    SetP5aFloatField(Value, TEXT("value"), bPresent ? ObservedValue : 0.0f);
    return Value;
}

TSharedRef<FJsonObject> P5aEmptyMarkerToJson()
{
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetStringField(TEXT("stableMarkerId"), TEXT(""));
    Value->SetStringField(TEXT("name"), TEXT(""));
    Value->SetNumberField(TEXT("sourceIndex"), -1);
    Value->SetNumberField(TEXT("trackIndex"), -1);
    Value->SetNumberField(TEXT("timeSeconds"), 0.0);
    return Value;
}

TSharedRef<FJsonObject> P5aMarkerToJson(
    const UAnimSequence& Sequence, const FP5aObservedSource& Source, const int32 SourceIndex)
{
    if (!Sequence.AuthoredSyncMarkers.IsValidIndex(SourceIndex))
    {
        return P5aEmptyMarkerToJson();
    }
    const FAnimSyncMarker& Marker{Sequence.AuthoredSyncMarkers[SourceIndex]};
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetStringField(TEXT("assetStableId"), Source.AssetStableId);
    Value->SetStringField(TEXT("stableMarkerId"), CreateP5aMarkerStableId(
        Source.AssetStableId, SourceIndex, Marker.MarkerName.ToString()));
    Value->SetStringField(TEXT("name"), Marker.MarkerName.ToString());
    Value->SetNumberField(TEXT("sourceIndex"), SourceIndex);
#if WITH_EDITORONLY_DATA
    Value->SetNumberField(TEXT("trackIndex"), Marker.TrackIndex);
#else
    Value->SetNumberField(TEXT("trackIndex"), 0);
#endif
    SetP5aFloatField(Value, TEXT("timeSeconds"), Marker.Time);
    return Value;
}

float StepP5aSemanticWeight(const float Weight, const float Target, const float DeltaSeconds)
{
    const float Step{static_cast<float>(static_cast<double>(DeltaSeconds) / static_cast<double>(.2f))};
    return Target > Weight
        ? FMath::Min(Target, static_cast<float>(Weight + Step))
        : FMath::Max(Target, static_cast<float>(Weight - Step));
}

void UpdateP5aSemanticGraphState(
    FP5aSemanticGraphState& State, FP5aNativeActionLifecycle& Lifecycle,
    const float PreviousActionTime, const bool bTransitionActive)
{
    bool bStarted{false};
    bool bTerminated{false};
    bool bCancelled{false};
    int32 ProcessedOutcomeCount{Lifecycle.SemanticOutcomeCount};
    for (int32 Index{Lifecycle.SemanticOutcomeCount}; Index < Lifecycle.Outcomes.Num(); ++Index)
    {
        const FP5aNativeActionOutcome& Outcome{Lifecycle.Outcomes[Index]};
        bStarted |= Outcome.NativeReason == TEXT("Started");
        bTerminated |= Outcome.NativeReason == TEXT("Cancelled") ||
            Outcome.NativeReason == TEXT("Finished");
        bCancelled |= Outcome.NativeReason == TEXT("Cancelled");
        ProcessedOutcomeCount = Index + 1;
    }
    Lifecycle.SemanticOutcomeCount = ProcessedOutcomeCount;
    if (bStarted)
    {
        State.bActionActive = true;
        State.ActionWeight = StepP5aSemanticWeight(
            State.ActionWeight, 1.0f, static_cast<float>(FixedDeltaSeconds));
        State.ActionEventWeight = State.ActionWeight;
    }
    else if (bTerminated)
    {
        float ActiveDelta{0.0f};
        if (!bCancelled && IsValid(Lifecycle.Montage))
        {
            ActiveDelta = FMath::Clamp(
                Lifecycle.TerminalPlayback.Position - PreviousActionTime,
                0.0f, static_cast<float>(FixedDeltaSeconds));
        }
        State.ActionWeight = StepP5aSemanticWeight(State.ActionWeight, 1.0f, ActiveDelta);
        State.ActionEventWeight = State.ActionWeight;
        State.ActionWeight = StepP5aSemanticWeight(State.ActionWeight, 0.0f,
            static_cast<float>(FixedDeltaSeconds) - ActiveDelta);
        State.bActionActive = false;
    }
    else
    {
        State.ActionWeight = StepP5aSemanticWeight(State.ActionWeight,
            State.bActionActive ? 1.0f : 0.0f, static_cast<float>(FixedDeltaSeconds));
        State.ActionEventWeight = State.ActionWeight;
    }

    const bool bTransitionWasActive{State.bTransitionActive};
    State.bTransitionActive = bTransitionActive;
    if (bTransitionWasActive)
    {
        State.TransitionWeight = StepP5aSemanticWeight(State.TransitionWeight,
            bTransitionActive ? 1.0f : 0.0f, static_cast<float>(FixedDeltaSeconds));
    }
}

TSharedRef<FJsonObject> P5aCanonicalEventToJson(
    const FP5aSourceDefinition& Source, const FAnimNotifyEvent& Notify,
    const int32 SourceIndex, const FString& Phase, const int32 FrameEventOrdinal,
    const float FrameOffset, const float ObservedWeight, const FString& TerminationReason)
{
    const UObject* NotifyObject{IsValid(Notify.NotifyStateClass.Get())
        ? static_cast<const UObject*>(Notify.NotifyStateClass.Get())
        : static_cast<const UObject*>(Notify.Notify.Get())};
    check(IsValid(NotifyObject));
    const FString SourceClassPath{NotifyObject->GetClass()->GetPathName()};
    const bool bMontageTimeline{Cast<UAnimMontage>(Source.CanonicalAsset) != nullptr};
    const bool bSetAction{Source.SourceKind == TEXT("ActionMontage")};
    const bool bActionSequence{Source.SourceKind == TEXT("ActionSequence")};
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetObjectField(TEXT("source"), P5aObservedSourceToJson(&Source.Canonical, false));
    Value->SetStringField(TEXT("observedEventStableId"), CreateP5aEventStableId(
        Source.Canonical.AssetStableId, SourceIndex, SourceClassPath));
    Value->SetStringField(TEXT("observedEventAssetStableId"), Source.Canonical.AssetStableId);
    Value->SetStringField(TEXT("observedOwnerKind"),
        bMontageTimeline ? TEXT("MontageTimeline") : TEXT("SequenceTimeline"));
    Value->SetStringField(TEXT("observedSourceClassPath"), SourceClassPath);
    Value->SetNumberField(TEXT("observedSourceIndex"), SourceIndex);
    Value->SetNumberField(TEXT("observedTrackIndex"), Notify.TrackIndex);
    Value->SetStringField(TEXT("observedMontageStableId"), Source.Canonical.MontageStableId);
    Value->SetStringField(TEXT("observedOwnerSectionName"), Source.Canonical.SectionName);
    Value->SetNumberField(TEXT("observedSegmentIndex"), bActionSequence ? 0 : -1);
    Value->SetNumberField(TEXT("boundaryOrdinal"), bActionSequence ? 1 : 0);
    Value->SetStringField(TEXT("nativeInstanceOrdinal"), TEXT("1"));
    Value->SetStringField(TEXT("playbackCycle"), TEXT("0"));
    Value->SetNumberField(TEXT("frameEventOrdinal"), FrameEventOrdinal);
    SetP5aFloatField(Value, TEXT("observedFrameOffsetSeconds"), FrameOffset);
    SetP5aFloatField(Value, TEXT("observedWeight"), ObservedWeight);
    Value->SetStringField(TEXT("kind"), bSetAction ? TEXT("SetAction") : TEXT("Generic"));
    Value->SetStringField(TEXT("tickMode"),
        Notify.MontageTickType == EMontageNotifyTickType::Queued ? TEXT("Queued") : TEXT("BranchingPoint"));
    Value->SetStringField(TEXT("phase"), Phase);
    Value->SetStringField(TEXT("nativeTerminationReason"), TerminationReason);
    const TSharedRef<FJsonObject> Payload{MakeShared<FJsonObject>()};
    Payload->SetNumberField(TEXT("semanticId"), bSetAction ? 2 : 0);
    Payload->SetNumberField(TEXT("enumValue0"), bSetAction ? 1 : 0);
    Payload->SetNumberField(TEXT("enumValue1"), 0);
    Payload->SetNumberField(TEXT("enumValue2"), 0);
    Payload->SetNumberField(TEXT("scalarValue0"), 0);
    Payload->SetNumberField(TEXT("flags"), 0);
    Value->SetObjectField(TEXT("payload"), Payload);
    return Value;
}

void AddP5aCanonicalEvents(
    const FP5aSourceDefinition& Source, const double PreviousTime, const double CurrentTime,
    const float ObservedWeight, const bool bCancelled,
    TArray<TSharedPtr<FJsonValue>>& Events, TArray<TSharedPtr<FJsonValue>>& ActiveStates,
    int32& FrameEventOrdinal, const float FrameOffsetScale = 1.0f)
{
    UAnimSequenceBase* Asset{Source.CanonicalAsset.Get()};
    if (!IsValid(Asset)) return;
    FAnimNotifyContext NotifyContext;
    if (!bCancelled)
    {
        Asset->GetAnimNotifies(static_cast<float>(PreviousTime),
            static_cast<float>(CurrentTime - PreviousTime), NotifyContext);
    }
    TSet<const FAnimNotifyEvent*> WindowNotifies;
    for (const FAnimNotifyEventReference& Reference : NotifyContext.ActiveNotifies)
    {
        if (const FAnimNotifyEvent* Notify{Reference.GetNotify()}; Notify != nullptr)
        {
            WindowNotifies.Add(Notify);
        }
    }
    for (int32 SourceIndex{0}; SourceIndex < Asset->Notifies.Num(); ++SourceIndex)
    {
        const FAnimNotifyEvent& Notify{Asset->Notifies[SourceIndex]};
        const bool bState{IsValid(Notify.NotifyStateClass.Get())};
        const double StartTime{Notify.GetTime()};
        const double EndTime{StartTime + Notify.GetDuration()};
        const bool bForcedCancel{bCancelled && Source.SourceKind == TEXT("ActionMontage") && bState};
        if (!bState)
        {
            if (WindowNotifies.Contains(&Notify) && PreviousTime < StartTime && CurrentTime >= StartTime)
            {
                Events.Add(MakeShared<FJsonValueObject>(P5aCanonicalEventToJson(
                    Source, Notify, SourceIndex, TEXT("Trigger"), FrameEventOrdinal++,
                    static_cast<float>((StartTime - PreviousTime) * FrameOffsetScale),
                    ObservedWeight, TEXT("None"))));
            }
            continue;
        }
        const bool bInWindow{WindowNotifies.Contains(&Notify)};
        const bool bBegins{bInWindow && PreviousTime <= StartTime && CurrentTime > StartTime};
        const bool bEnds{bForcedCancel || (PreviousTime < EndTime && CurrentTime >= EndTime)};
        if (bBegins && !bForcedCancel)
        {
            Events.Add(MakeShared<FJsonValueObject>(P5aCanonicalEventToJson(
                Source, Notify, SourceIndex, TEXT("Begin"), FrameEventOrdinal++,
                static_cast<float>(FMath::Max(0.0, StartTime - PreviousTime) * FrameOffsetScale),
                ObservedWeight, TEXT("None"))));
        }
        if (bEnds)
        {
            Events.Add(MakeShared<FJsonValueObject>(P5aCanonicalEventToJson(
                Source, Notify, SourceIndex, TEXT("End"), FrameEventOrdinal++,
                bForcedCancel ? 0.0f :
                    static_cast<float>(FMath::Max(0.0, EndTime - PreviousTime) * FrameOffsetScale),
                ObservedWeight, bForcedCancel ? TEXT("Cancelled") : TEXT("None"))));
        }
        else if (bInWindow && CurrentTime > StartTime && PreviousTime < EndTime)
        {
            Events.Add(MakeShared<FJsonValueObject>(P5aCanonicalEventToJson(
                Source, Notify, SourceIndex, TEXT("Tick"), FrameEventOrdinal++,
                static_cast<float>(FixedDeltaSeconds) * FrameOffsetScale,
                ObservedWeight, TEXT("None"))));
            const TSharedRef<FJsonObject> Owner{MakeShared<FJsonObject>()};
            Owner->SetObjectField(TEXT("source"), P5aObservedSourceToJson(&Source.Canonical, false));
            const UObject* NotifyObject{Notify.NotifyStateClass.Get()};
            Owner->SetStringField(TEXT("observedEventStableId"), CreateP5aEventStableId(
                Source.Canonical.AssetStableId, SourceIndex, NotifyObject->GetClass()->GetPathName()));
            Owner->SetStringField(TEXT("nativeInstanceOrdinal"), TEXT("1"));
            Owner->SetStringField(TEXT("playbackCycle"), TEXT("0"));
            ActiveStates.Add(MakeShared<FJsonValueObject>(Owner));
        }
    }
}

TSharedRef<FJsonObject> P5aNativeTimelineEventToJson(
    const FP5aObservedSource& Source, const FAnimNotifyEvent& Notify,
    const int32 SourceIndex, const FString& Phase, const float FrameOffset)
{
    const UObject* NotifyObject{IsValid(Notify.NotifyStateClass.Get())
        ? static_cast<const UObject*>(Notify.NotifyStateClass.Get())
        : static_cast<const UObject*>(Notify.Notify.Get())};
    check(IsValid(NotifyObject));
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetObjectField(TEXT("source"), P5aObservedSourceToJson(&Source, true));
    Value->SetStringField(TEXT("observedEventStableId"), CreateP5aEventStableId(
        Source.AssetStableId, SourceIndex, NotifyObject->GetClass()->GetPathName()));
    Value->SetNumberField(TEXT("observedSourceIndex"), SourceIndex);
    Value->SetNumberField(TEXT("observedTrackIndex"), Notify.TrackIndex);
    SetP5aFloatField(Value, TEXT("observedFrameOffsetSeconds"), FrameOffset);
    Value->SetStringField(TEXT("phase"), Phase);
    return Value;
}

bool AddP5aNativeTimelineEvents(
    UAnimInstance& Animation, const TArray<FP5aSourceDefinition>& Sources,
    const FP5aNativeAuxiliaryInventory& Auxiliary,
    const FP5aNativeActionLifecycle& ActionLifecycle,
    FP5aNativeNotifyObservationState& ObservationState,
    TArray<TSharedPtr<FJsonValue>>& Timeline)
{
    const auto ResolveNotify{[&Sources, &Auxiliary, &Animation, &ObservationState, &ActionLifecycle](const FAnimNotifyEventReference& Reference,
                                        FP5aObservedNotifyState& Observed, const bool bRequireTiming = true)
    {
        const UObject* SourceObject{Reference.GetSourceObject()};
        const FAnimNotifyEvent* Notify{Reference.GetNotify()};
        if (!IsValid(SourceObject) || Notify == nullptr)
        {
            UE_LOG(LogTemp, Error, TEXT("P5A notify reference has no live source/event."));
            return false;
        }
        Observed.LastAnimationTime = Reference.GetCurrentAnimationTime();
        Observed.PreviousAnimationTime = Observed.LastAnimationTime - static_cast<float>(FixedDeltaSeconds);
        if (bRequireTiming)
        {
        bool bHasMontageContext{false};
        if (const UE::Anim::FAnimNotifyMontageInstanceContext* Context{
            Reference.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>()})
        {
            bHasMontageContext = true;
            Observed.MontageInstanceId = Context->MontageInstanceID;
        }
        else if (IsValid(Notify->NotifyStateClass) && !Notify->IsBranchingPoint())
        {
            // Native dispatch reuses the notify ID even when it replaces the reference's context.
            int32 MatchingPreviousStates{0};
            for (const FP5aObservedNotifyState& Previous : ObservationState.ActiveStates)
            {
                if (!Previous.bBranchingPoint && Previous.InstanceId == Reference.GetNotifyInstanceID() &&
                    Previous.Source->AssetObjectPath == SourceObject->GetPathName() && *Previous.Notify == *Notify)
                {
                    Observed.MontageInstanceId = Previous.MontageInstanceId;
                    Observed.PreviousAnimationTime = Previous.LastAnimationTime;
                    Observed.PlayRate = Previous.PlayRate;
                    ++MatchingPreviousStates;
                }
            }
            if (MatchingPreviousStates != 1 || Observed.MontageInstanceId == INDEX_NONE)
            {
                UE_LOG(LogTemp, Error, TEXT("P5A contextless state has no unique prior native identity: source=%s id=%d matches=%d"),
                    *SourceObject->GetPathName(), Reference.GetNotifyInstanceID(), MatchingPreviousStates);
                return false;
            }
        }
        // A contextless native active state can outlive its original montage. Its current
        // reference time is authoritative; the prior ID associates identity, not playback.
        if (bHasMontageContext)
        {
            const FAnimMontageInstance* Instance{Animation.GetMontageInstanceForID(Observed.MontageInstanceId)};
            const bool bUseEndedSnapshot{Instance == nullptr && ActionLifecycle.bEndedThisFrame &&
                Observed.MontageInstanceId == ActionLifecycle.InstanceId && ActionLifecycle.EndedPlayback.bCaptured};
            if (Instance == nullptr && !bUseEndedSnapshot)
            {
                UE_LOG(LogTemp, Error, TEXT("P5A notify instance missing id=%d source=%s time=%.9f"),
                    Observed.MontageInstanceId, *SourceObject->GetPathName(), Reference.GetCurrentAnimationTime());
                return false;
            }
            Observed.PlayRate = bUseEndedSnapshot ? ActionLifecycle.EndedPlayback.PlayRate : Instance->GetPlayRate();
            Observed.LastAnimationTime = bUseEndedSnapshot ? ActionLifecycle.EndedPlayback.Position : Instance->GetPosition();
            Observed.PreviousAnimationTime = bUseEndedSnapshot
                ? ActionLifecycle.EndedPlayback.PreviousPosition : Instance->GetPreviousPosition();
            if (const UAnimMontage* Montage{bUseEndedSnapshot ? ActionLifecycle.Montage.Get() : Instance->Montage.Get()})
            {
                int32 MatchingSegments{0};
                for (const FSlotAnimationTrack& Track : Montage->SlotAnimTracks)
                {
                    for (const FAnimSegment& Segment : Track.AnimTrack.AnimSegments)
                    {
                        if (Segment.GetAnimReference() == SourceObject)
                        {
                            ++MatchingSegments;
                            Observed.PlayRate *= Segment.GetValidPlayRate();
                            Observed.LastAnimationTime = Segment.ConvertTrackPosToAnimPos(Observed.LastAnimationTime);
                            Observed.PreviousAnimationTime = Segment.ConvertTrackPosToAnimPos(Observed.PreviousAnimationTime);
                        }
                    }
                }
                if (Montage != SourceObject && MatchingSegments != 1) return false;
            }
            else return false;
        }
        if (Observed.PlayRate <= 0.0f || (IsValid(Notify->NotifyStateClass) &&
            Notify->GetDuration() <= static_cast<float>(FixedDeltaSeconds) * Observed.PlayRate))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A notify rate/duration rejected source=%s rate=%.9f duration=%.9f"),
                *SourceObject->GetPathName(), Observed.PlayRate, Notify->GetDuration());
            return false;
        }
        }
        for (const FP5aSourceDefinition& Definition : Sources)
        {
            for (int32 VariantIndex{0}; VariantIndex < Definition.NativeAssets.Num(); ++VariantIndex)
            {
                UAnimSequenceBase* Asset{Definition.NativeAssets[VariantIndex]};
                if (Asset != SourceObject || !IsValid(Asset)) continue;
                for (int32 SourceIndex{0}; SourceIndex < Asset->Notifies.Num(); ++SourceIndex)
                {
                    if (!(Asset->Notifies[SourceIndex] == *Notify)) continue;
                    Observed.InstanceId = Reference.GetNotifyInstanceID();
                    Observed.Source = Definition.NativeVariants.IsValidIndex(VariantIndex)
                        ? &Definition.NativeVariants[VariantIndex] : &Definition.Native;
                    Observed.Notify = &Asset->Notifies[SourceIndex];
                    Observed.SourceIndex = SourceIndex;
                    return true;
                }
            }
            UAnimSequenceBase* Asset{Definition.NativeAsset.Get()};
            if (Asset != SourceObject || !IsValid(Asset)) continue;
            for (int32 SourceIndex{0}; SourceIndex < Asset->Notifies.Num(); ++SourceIndex)
            {
                if (!(Asset->Notifies[SourceIndex] == *Notify)) continue;
                Observed.InstanceId = Reference.GetNotifyInstanceID();
                Observed.Source = &Definition.Native;
                Observed.Notify = &Asset->Notifies[SourceIndex];
                Observed.SourceIndex = SourceIndex;
                return true;
            }
        }
        for (int32 AssetIndex{0}; AssetIndex < Auxiliary.Assets.Num(); ++AssetIndex)
        {
            const UAnimSequenceBase* Asset{Cast<UAnimSequenceBase>(Auxiliary.Assets[AssetIndex])};
            if (Asset != SourceObject || !IsValid(Asset)) continue;
            for (int32 SourceIndex{0}; SourceIndex < Asset->Notifies.Num(); ++SourceIndex)
            {
                if (!(Asset->Notifies[SourceIndex] == *Notify)) continue;
                Observed.InstanceId = Reference.GetNotifyInstanceID();
                Observed.Source = &Auxiliary.Sources[AssetIndex];
                Observed.Notify = &Asset->Notifies[SourceIndex];
                Observed.SourceIndex = SourceIndex;
                return true;
            }
        }
        const UAnimSequenceBase* SourceAsset{Cast<UAnimSequenceBase>(SourceObject)};
        UE_LOG(LogTemp, Error, TEXT("P5A notify identity unresolved source=%s notify=%s state=%s guid=%s time=%.9f rows=%d"),
            *SourceObject->GetPathName(), *GetPathNameSafe(Notify->Notify), *GetPathNameSafe(Notify->NotifyStateClass),
            *Notify->Guid.ToString(), Notify->GetTime(), SourceAsset != nullptr ? SourceAsset->Notifies.Num() : -1);
        return false;
    }};
    const auto ContainsInstance{[](const TArray<FP5aObservedNotifyState>& Values, const FP5aObservedNotifyState& Other)
    {
        return Values.ContainsByPredicate([&Other](const FP5aObservedNotifyState& Value)
        {
            return Value.bBranchingPoint == Other.bBranchingPoint &&
                Value.Source == Other.Source && Value.Notify == Other.Notify &&
                (Value.bBranchingPoint ? Value.MontageInstanceId == Other.MontageInstanceId
                    : Value.InstanceId == Other.InstanceId);
        });
    }};
    const auto FrameOffset{[](const FP5aObservedNotifyState& Value, const float BoundaryTime)
    {
        if (BoundaryTime <= UE_SMALL_NUMBER) return 0.0f;
        return FMath::Clamp((BoundaryTime - Value.PreviousAnimationTime) / Value.PlayRate,
            0.0f, static_cast<float>(FixedDeltaSeconds));
    }};

    TArray<FP5aObservedNotifyState> CurrentStates;
    TArray<FP5aObservedNotifyState> Triggered;
    // Branching states bypass NotifyQueue. Inspect the engine's reflected live state array.
    const FArrayProperty* BranchingStatesProperty{FindFProperty<FArrayProperty>(
        FAnimMontageInstance::StaticStruct(), TEXT("ActiveStateBranchingPoints"))};
    if (BranchingStatesProperty == nullptr) return false;
    for (const FAnimMontageInstance* MontageInstance : Animation.MontageInstances)
    {
        if (MontageInstance == nullptr || !IsValid(MontageInstance->Montage)) continue;
        const UAnimMontage* Montage{MontageInstance->Montage};
        const float PlayRate{MontageInstance->GetPlayRate()};
        if (PlayRate <= 0.0f) return false;
        for (const FAnimNotifyEvent& Notify : Montage->Notifies)
        {
            if (IsValid(Notify.NotifyStateClass) &&
                Notify.GetDuration() <= static_cast<float>(FixedDeltaSeconds) * PlayRate)
            {
                UE_LOG(LogTemp, Error, TEXT("P5A montage state is too short for frame snapshot observation: %s"),
                    *Montage->GetPathName());
                return false;
            }
        }
        for (const FSlotAnimationTrack& Track : Montage->SlotAnimTracks)
        {
            for (const FAnimSegment& Segment : Track.AnimTrack.AnimSegments)
            {
                const UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(Segment.GetAnimReference())};
                if (!IsValid(Sequence)) continue;
                const float SegmentRate{PlayRate * Segment.GetValidPlayRate()};
                if (SegmentRate <= 0.0f) return false;
                for (const FAnimNotifyEvent& Notify : Sequence->Notifies)
                {
                    if (IsValid(Notify.NotifyStateClass) &&
                        Notify.GetDuration() <= static_cast<float>(FixedDeltaSeconds) * SegmentRate) return false;
                }
            }
        }
        FScriptArrayHelper ActiveStateBranchingPoints{BranchingStatesProperty,
            BranchingStatesProperty->ContainerPtrToValuePtr<void>(MontageInstance)};
        for (int32 Index{0}; Index < ActiveStateBranchingPoints.Num(); ++Index)
        {
            if (MontageInstance->GetInstanceID() != ActionLifecycle.InstanceId) return false;
            const FAnimNotifyEvent* Notify{reinterpret_cast<const FAnimNotifyEvent*>(
                ActiveStateBranchingPoints.GetRawPtr(Index))};
            FP5aObservedNotifyState Observed;
            const FAnimNotifyEventReference Reference{Notify, Montage};
            if (!ResolveNotify(Reference, Observed)) return false;
            Observed.bBranchingPoint = true;
            Observed.bTicked = !ActionLifecycle.bOnMontageBlendingOutStartedObserved;
            Observed.MontageInstanceId = MontageInstance->GetInstanceID();
            Observed.PlayRate = PlayRate;
            Observed.LastAnimationTime = MontageInstance->GetPosition();
            Observed.PreviousAnimationTime = MontageInstance->GetPreviousPosition();
            CurrentStates.Add(Observed);
        }
    }
    for (const FAnimNotifyEventReference& Reference : Animation.NotifyQueue.AnimNotifies)
    {
        if (Reference.GetNotify() != nullptr && IsValid(Reference.GetNotify()->NotifyStateClass)) continue;
        FP5aObservedNotifyState Observed;
        if (!ResolveNotify(Reference, Observed))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A queued notify observation rejected source=%s"),
                *GetPathNameSafe(Reference.GetSourceObject()));
            return false;
        }
        Triggered.Add(Observed);
    }
    for (const FAnimNotifyEventReference& Reference : Animation.ActiveAnimNotifyEventReference)
    {
        FP5aObservedNotifyState Observed;
        if (!ResolveNotify(Reference, Observed)) return false;
        CurrentStates.Add(Observed);
    }
    for (const FAnimNotifyEventReference& Reference : Animation.NotifyQueue.AnimNotifies)
    {
        if (Reference.GetNotify() == nullptr || !IsValid(Reference.GetNotify()->NotifyStateClass)) continue;
        FP5aObservedNotifyState Queued;
        if (!ResolveNotify(Reference, Queued, false) ||
            (!ContainsInstance(CurrentStates, Queued) &&
                !ContainsInstance(ObservationState.ActiveStates, Queued) &&
                !ContainsInstance(ObservationState.EndedStates, Queued))) return false;
    }
    const AAlsCharacter* Owner{Cast<AAlsCharacter>(Animation.GetOwningActor())};
    for (const FP5aObservedNotifyState& Current : CurrentStates)
    {
        const UAlsAnimNotifyState_SetLocomotionAction* SetAction{
            Cast<UAlsAnimNotifyState_SetLocomotionAction>(Current.Notify->NotifyStateClass)};
        if (SetAction == nullptr) continue;
        const FStructProperty* ActionProperty{FindFProperty<FStructProperty>(SetAction->GetClass(), TEXT("LocomotionAction"))};
        if (!IsValid(Owner) || ActionProperty == nullptr ||
            Owner->GetLocomotionAction() != *ActionProperty->ContainerPtrToValuePtr<FGameplayTag>(SetAction)) return false;
    }
    for (const FP5aObservedNotifyState& Previous : ObservationState.ActiveStates)
    {
        const UAlsAnimNotifyState_SetLocomotionAction* SetAction{
            Cast<UAlsAnimNotifyState_SetLocomotionAction>(Previous.Notify->NotifyStateClass)};
        if (SetAction == nullptr || ContainsInstance(CurrentStates, Previous)) continue;
        const FStructProperty* ActionProperty{FindFProperty<FStructProperty>(SetAction->GetClass(), TEXT("LocomotionAction"))};
        if (!IsValid(Owner) || ActionProperty == nullptr ||
            Owner->GetLocomotionAction() == *ActionProperty->ContainerPtrToValuePtr<FGameplayTag>(SetAction)) return false;
    }
    // Native montage branching dispatch precedes the queued Trigger/End/Begin/Tick stages.
    // This is ordered frame-state evidence, not a generic interception of notify callbacks.
    for (const bool bBranchingPoint : {true, false})
    {
        if (!bBranchingPoint)
        {
            for (const FP5aObservedNotifyState& Observed : Triggered)
            {
                Timeline.Add(MakeShared<FJsonValueObject>(P5aNativeTimelineEventToJson(
                    *Observed.Source, *Observed.Notify, Observed.SourceIndex, TEXT("Trigger"),
                    FrameOffset(Observed, Observed.Notify->GetTriggerTime()))));
            }
        }
        for (const FP5aObservedNotifyState& Previous : ObservationState.ActiveStates)
        {
            if (Previous.bBranchingPoint != bBranchingPoint || ContainsInstance(CurrentStates, Previous)) continue;
            FP5aObservedNotifyState Ended{Previous};
            Ended.PreviousAnimationTime = Previous.LastAnimationTime;
            const bool bInterruptedAction{Previous.MontageInstanceId == ActionLifecycle.InstanceId &&
                ActionLifecycle.bOnMontageBlendingOutStartedObserved};
            const float EndOffset{bInterruptedAction ? 0.0f : FrameOffset(Ended, Ended.Notify->GetEndTriggerTime())};
            Timeline.Add(MakeShared<FJsonValueObject>(P5aNativeTimelineEventToJson(
                *Previous.Source, *Previous.Notify, Previous.SourceIndex, TEXT("End"),
                EndOffset)));
            ObservationState.EndedStates.Add(Previous);
        }
        for (const FP5aObservedNotifyState& Current : CurrentStates)
        {
            if (Current.bBranchingPoint != bBranchingPoint || ContainsInstance(ObservationState.ActiveStates, Current)) continue;
            Timeline.Add(MakeShared<FJsonValueObject>(P5aNativeTimelineEventToJson(
                *Current.Source, *Current.Notify, Current.SourceIndex, TEXT("Begin"),
                FrameOffset(Current, Current.Notify->GetTriggerTime()))));
        }
        for (const FP5aObservedNotifyState& Current : CurrentStates)
        {
            if (Current.bBranchingPoint != bBranchingPoint || !Current.bTicked) continue;
            Timeline.Add(MakeShared<FJsonValueObject>(P5aNativeTimelineEventToJson(
                *Current.Source, *Current.Notify, Current.SourceIndex, TEXT("Tick"),
                static_cast<float>(FixedDeltaSeconds))));
        }
    }
    ObservationState.ActiveStates = MoveTemp(CurrentStates);
    return true;
}

TSharedRef<FJsonObject> EvaluateP5aCanonicalCurves(
    const FP5aFrameDefinition& Frame, const TArray<FP5aSourceDefinition>& Sources,
    const FP5aSourceDefinition* TransitionSource, const float TransitionTime,
    const float ActionTime, const float ActionGraphWeight, const float TransitionGraphWeight,
    const bool bAuthored)
{
    const auto EvaluateCurve{[bAuthored](UAnimSequenceBase* CurveAsset, const float CurveTime,
                                const bool bLoop, const FName Name, const float MissingValue = 0.0f)
    {
        const FAnimExtractContext Context{CurveTime, false, {}, bLoop};
        if (!IsValid(CurveAsset) || !CurveAsset->HasCurveData(Name, bAuthored)) return MissingValue;
        return bAuthored ? CurveAsset->EvaluateCurveData(Name, Context, true)
            : CurveAsset->EvaluateCurveData(Name, Context, false);
    }};
    float BaseLeftLock{0.0f};
    float BaseRightLock{0.0f};
    float TurnLeftLock{0.0f};
    float TurnRightLock{0.0f};
    float RotateLeftLock{0.0f};
    float RotateRightLock{0.0f};
    float AllowTransitions{1.0f};
    for (const FP5aPlaybackDefinition& CurvePlayback : Frame.Playbacks)
    {
        const FP5aSourceDefinition* CurveSource{FindP5aSource(Sources, CurvePlayback.TraceSourceId)};
        UAnimSequenceBase* CurveAsset{CurveSource != nullptr ? CurveSource->CanonicalAsset.Get() : nullptr};
        const float CurveTime{static_cast<float>(CurvePlayback.CurrentTimeSeconds)};
        float* LeftLock{&BaseLeftLock};
        float* RightLock{&BaseRightLock};
        if (CurvePlayback.Lane == TEXT("turnBanks"))
        {
            LeftLock = &TurnLeftLock;
            RightLock = &TurnRightLock;
        }
        else if (CurvePlayback.Lane == TEXT("rotateBanks"))
        {
            LeftLock = &RotateLeftLock;
            RightLock = &RotateRightLock;
        }
        *LeftLock += EvaluateCurve(CurveAsset, CurveTime, CurvePlayback.bLoop,
            FName{TEXT("FootLock_L")}) * CurvePlayback.Weight;
        *RightLock += EvaluateCurve(CurveAsset, CurveTime, CurvePlayback.bLoop,
            FName{TEXT("FootLock_R")}) * CurvePlayback.Weight;
        const float LaneWeight{CurvePlayback.Lane == TEXT("base") ? 1.0f - Frame.ActionBlendAmount
            : Frame.ActionBlendAmount * (CurvePlayback.Lane == TEXT("turnBanks")
                ? 1.0f - Frame.ActionModeBlendAmount : Frame.ActionModeBlendAmount)};
        AllowTransitions += EvaluateCurve(CurveAsset, CurveTime, CurvePlayback.bLoop,
            FName{TEXT("Enable_Transition")}, 0.0f) * CurvePlayback.Weight * LaneWeight;
    }
    const float ActionLeftLock{FMath::Lerp(
        TurnLeftLock, RotateLeftLock, Frame.ActionModeBlendAmount)};
    const float ActionRightLock{FMath::Lerp(
        TurnRightLock, RotateRightLock, Frame.ActionModeBlendAmount)};
    const float LeftLock{FMath::Clamp(FMath::Lerp(
        BaseLeftLock, ActionLeftLock, Frame.ActionBlendAmount), 0.0f, 1.0f)};
    const float RightLock{FMath::Clamp(FMath::Lerp(
        BaseRightLock, ActionRightLock, Frame.ActionBlendAmount), 0.0f, 1.0f)};
    if (TransitionSource != nullptr)
    {
        AllowTransitions += EvaluateCurve(TransitionSource->CanonicalAsset, TransitionTime, false,
            FName{TEXT("Enable_Transition")}, 0.0f) * TransitionGraphWeight;
    }
    for (const FP5aSourceDefinition& ActionSource : Sources)
    {
        if (ActionSource.SourceKind != TEXT("ActionSequence")) continue;
        AllowTransitions += EvaluateCurve(ActionSource.CanonicalAsset, ActionTime, false,
            FName{TEXT("Enable_Transition")}, 0.0f) * ActionGraphWeight;
    }
    return P5aCurveValuesToJson(1.0f, 1.0f, LeftLock, RightLock,
        FMath::Clamp(AllowTransitions, 0.0f, 1.0f));
}

#include "AlsP5aCanonicalCurves.Tests.inl"

FP5aCanonicalAssetOracle EvaluateP5aCanonicalAssetOracle(
    const FP5aFrameDefinition& Frame, const int32 CaseOrdinal,
    const TArray<FP5aSourceDefinition>& Sources,
    UAlsAnimationInstance& Animation, const FP5aNativeActionLifecycle& ActionLifecycle,
    const float PreviousActionTime, const FP5aSourceDefinition* TransitionSource,
    const FAnimMontageInstance* TransitionInstance,
    const float ActionGraphWeight, const float ActionEventWeight,
    const float TransitionGraphWeight)
{
    FP5aCanonicalAssetOracle Result;
    const TSharedRef<FJsonObject> Oracle{MakeShared<FJsonObject>()};
    const FP5aPlaybackDefinition* Playback{Frame.Playbacks.IsEmpty() ? nullptr : &Frame.Playbacks[0]};
    const FP5aSourceDefinition* Source{
        Playback != nullptr ? FindP5aSource(Sources, Playback->TraceSourceId) : nullptr};
    UAnimSequenceBase* Asset{Source != nullptr ? Source->CanonicalAsset.Get() : nullptr};
    const float Time{Playback != nullptr ? static_cast<float>(Playback->CurrentTimeSeconds) : 0.0f};
    const FAnimMontageInstance* CurveActionInstance{IsValid(ActionLifecycle.Montage)
        ? Animation.GetInstanceForMontage(ActionLifecycle.Montage) : nullptr};
    const float CurveActionTime{CurveActionInstance != nullptr ? CurveActionInstance->GetPosition()
        : ActionLifecycle.TerminalPlayback.Position};
    const float CurveTransitionTime{TransitionInstance != nullptr ? TransitionInstance->GetPosition() : 0.0f};
    Oracle->SetObjectField(TEXT("curves"), EvaluateP5aCanonicalCurves(Frame, Sources, TransitionSource,
        CurveTransitionTime, CurveActionTime, ActionGraphWeight, TransitionGraphWeight, true));
    Oracle->SetObjectField(TEXT("compressedCurves"), EvaluateP5aCanonicalCurves(Frame, Sources, TransitionSource,
        CurveTransitionTime, CurveActionTime, ActionGraphWeight, TransitionGraphWeight, false));
    const TSharedRef<FJsonObject> GraphCurveWeights{MakeShared<FJsonObject>()};
    SetP5aFloatField(GraphCurveWeights, TEXT("action"), ActionGraphWeight);
    SetP5aFloatField(GraphCurveWeights, TEXT("transition"), TransitionGraphWeight);
    Oracle->SetObjectField(TEXT("graphCurveWeights"), GraphCurveWeights);

    const TSharedRef<FJsonObject> Sync{MakeShared<FJsonObject>()};
    Sync->SetBoolField(TEXT("active"), false);
    Sync->SetObjectField(TEXT("leader"), P5aObservedSourceToJson(nullptr, false));
    Sync->SetStringField(TEXT("nativeInstanceOrdinal"), TEXT("0"));
    Sync->SetObjectField(TEXT("previousMarker"), P5aEmptyMarkerToJson());
    Sync->SetObjectField(TEXT("nextMarker"), P5aEmptyMarkerToJson());
    Sync->SetStringField(TEXT("cycle"), TEXT("0"));
    Sync->SetNumberField(TEXT("phase"), 0.0);
    Sync->SetNumberField(TEXT("leftFootPhase"), 0.0);
    Sync->SetNumberField(TEXT("rightFootPhase"), 0.0);
    UAnimSequence* MarkerSequence{Cast<UAnimSequence>(Asset)};
    if (IsValid(MarkerSequence) && MarkerSequence->AuthoredSyncMarkers.Num() >= 2)
    {
        TArray<FName> MarkerNames;
        for (const FAnimSyncMarker& Marker : MarkerSequence->AuthoredSyncMarkers)
        {
            MarkerNames.AddUnique(Marker.MarkerName);
        }
        FMarkerPair PreviousMarker;
        FMarkerPair NextMarker;
        const float MarkerPairTime{CaseOrdinal == 1
            ? 0.0f
            : static_cast<float>(Playback->PreviousTimeSeconds)};
        MarkerSequence->GetMarkerIndicesForTime(
            MarkerPairTime, Playback->bLoop,
            MarkerNames, PreviousMarker, NextMarker);
        FMarkerPair PhasePreviousMarker;
        FMarkerPair PhaseNextMarker;
        MarkerSequence->GetMarkerIndicesForTime(
            Time, Playback->bLoop, MarkerNames, PhasePreviousMarker, PhaseNextMarker);
        const FMarkerSyncAnimPosition Position{MarkerSequence->GetMarkerSyncPositionFromMarkerIndicies(
            PhasePreviousMarker.MarkerIndex, PhaseNextMarker.MarkerIndex, Time, nullptr)};
        Sync->SetBoolField(TEXT("active"), true);
        Sync->SetObjectField(TEXT("leader"), P5aObservedSourceToJson(&Source->Canonical, false));
        Sync->SetStringField(TEXT("nativeInstanceOrdinal"), TEXT("1"));
        Sync->SetObjectField(TEXT("previousMarker"), P5aMarkerToJson(
            *MarkerSequence, Source->Canonical, PreviousMarker.MarkerIndex));
        Sync->SetObjectField(TEXT("nextMarker"), P5aMarkerToJson(
            *MarkerSequence, Source->Canonical, NextMarker.MarkerIndex));
        if (Playback->bClosesAfterFrame &&
            Playback->CurrentTimeSeconds > Playback->PreviousTimeSeconds)
        {
            SetP5aFloatField(Sync, TEXT("phase"), Position.PositionBetweenMarkers);
            const FString PreviousName{MarkerSequence->AuthoredSyncMarkers[
                PhasePreviousMarker.MarkerIndex].MarkerName.ToString()};
            SetP5aFloatField(Sync, TEXT("leftFootPhase"),
                PreviousName == TEXT("Left") ? Position.PositionBetweenMarkers :
                    1.0f - Position.PositionBetweenMarkers);
            SetP5aFloatField(Sync, TEXT("rightFootPhase"),
                PreviousName == TEXT("Right") ? Position.PositionBetweenMarkers :
                    1.0f - Position.PositionBetweenMarkers);
        }
    }
    Oracle->SetObjectField(TEXT("sync"), Sync);

    TArray<TSharedPtr<FJsonValue>> Events;
    TArray<TSharedPtr<FJsonValue>> ActiveStates;
    int32 FrameEventOrdinal{0};
    if (!Frame.Playbacks.IsEmpty())
    {
        const FP5aPlaybackDefinition& EventPlayback{Frame.Playbacks.Last()};
        const FP5aSourceDefinition* EventSource{
            FindP5aSource(Sources, EventPlayback.TraceSourceId)};
        if (EventSource != nullptr)
        {
            AddP5aCanonicalEvents(*EventSource,
                EventPlayback.PreviousTimeSeconds,
                EventPlayback.CurrentTimeSeconds, 1.0f, false,
                Events, ActiveStates, FrameEventOrdinal);
        }
    }
    FAnimMontageInstance* ActionInstance{IsValid(ActionLifecycle.Montage)
        ? Animation.GetInstanceForMontage(ActionLifecycle.Montage) : nullptr};
    const bool bActionOutcomePending{
        ActionLifecycle.Outcomes.Num() > ActionLifecycle.EmittedOutcomeCount};
    const bool bActionCancelled{bActionOutcomePending &&
        ActionLifecycle.Outcomes.Last().NativeReason == TEXT("Cancelled")};
    const bool bActionFinished{bActionOutcomePending &&
        ActionLifecycle.Outcomes.Last().NativeReason == TEXT("Finished")};
    const float CurrentActionTime{(bActionCancelled || bActionFinished)
        ? ActionLifecycle.TerminalPlayback.Position
        : (ActionInstance != nullptr ? ActionInstance->GetPosition() : PreviousActionTime)};
    if (IsValid(ActionLifecycle.Montage) &&
        (ActionInstance != nullptr || bActionCancelled || bActionFinished) &&
        CurrentActionTime >= PreviousActionTime)
    {
        for (const FP5aSourceDefinition& ActionSource : Sources)
        {
            if (ActionSource.SourceKind != TEXT("ActionMontage") &&
                ActionSource.SourceKind != TEXT("ActionSequence")) continue;
            AddP5aCanonicalEvents(ActionSource, PreviousActionTime, CurrentActionTime,
                ActionEventWeight, bActionCancelled, Events, ActiveStates, FrameEventOrdinal);
        }
    }
    if (TransitionSource != nullptr && TransitionInstance != nullptr)
    {
        AddP5aCanonicalEvents(*TransitionSource,
            TransitionInstance->GetPreviousPosition(), TransitionInstance->GetPosition(),
            TransitionGraphWeight, false, Events, ActiveStates, FrameEventOrdinal,
            1.0f / FMath::Max(TransitionInstance->GetPlayRate(), UE_SMALL_NUMBER));
    }
    Events.StableSort([](const TSharedPtr<FJsonValue>& Left, const TSharedPtr<FJsonValue>& Right)
    {
        const TSharedPtr<FJsonObject>* LeftObject{nullptr};
        const TSharedPtr<FJsonObject>* RightObject{nullptr};
        double LeftOffset{0.0};
        double RightOffset{0.0};
        return Left->TryGetObject(LeftObject) && LeftObject != nullptr &&
            Right->TryGetObject(RightObject) && RightObject != nullptr &&
            (*LeftObject)->TryGetNumberField(TEXT("observedFrameOffsetSeconds"), LeftOffset) &&
            (*RightObject)->TryGetNumberField(TEXT("observedFrameOffsetSeconds"), RightOffset) &&
            LeftOffset < RightOffset;
    });
    for (int32 EventIndex{0}; EventIndex < Events.Num(); ++EventIndex)
    {
        const TSharedPtr<FJsonObject>* EventObject{nullptr};
        if (Events[EventIndex]->TryGetObject(EventObject) && EventObject != nullptr)
        {
            (*EventObject)->SetNumberField(TEXT("frameEventOrdinal"), EventIndex);
        }
    }
    Oracle->SetArrayField(TEXT("events"), Events);
    Oracle->SetArrayField(TEXT("activeNotifyStates"), ActiveStates);
    Result.Value = Oracle;
    return Result;
}

TSharedRef<FJsonObject> P5aTransitionReceiptToJson(const FP5aNativeTransitionStimulusReceipt& Receipt)
{
    const auto FootToJson{[](const FVector& Target, const FVector& Lock, const float LockAmount)
    {
        const TSharedRef<FJsonObject> Foot{MakeShared<FJsonObject>()};
        Foot->SetObjectField(TEXT("observedTargetMeters"), P5aFloatVectorToJson(Target));
        Foot->SetObjectField(TEXT("observedLockMeters"), P5aFloatVectorToJson(Lock));
        SetP5aFloatField(Foot, TEXT("observedLockAmount"), LockAmount);
        return Foot;
    }};
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetStringField(TEXT("hookContractSha256"), P5aNativeTransitionStimulusContractSha256);
    Value->SetNumberField(TEXT("observedAllowTransitions"), Receipt.ObservedAllowTransitions ? 1.0 : 0.0);
    Value->SetBoolField(TEXT("preHookUpdatedThisFrame"), Receipt.PreHookUpdatedThisFrame);
    Value->SetNumberField(TEXT("preHookFrameDelay"), Receipt.PreHookFrameDelay);
    Value->SetBoolField(TEXT("preHookTransitionActive"), Receipt.PreHookTransitionActive);
    Value->SetObjectField(TEXT("left"), FootToJson(Receipt.ObservedLeftTarget,
        Receipt.ObservedLeftLock, Receipt.ObservedLeftLockAmount));
    Value->SetObjectField(TEXT("right"), FootToJson(Receipt.ObservedRightTarget,
        Receipt.ObservedRightLock, Receipt.ObservedRightLockAmount));
    Value->SetBoolField(TEXT("postHookUpdatedThisFrame"), Receipt.PostHookUpdatedThisFrame);
    Value->SetNumberField(TEXT("postHookFrameDelay"), Receipt.PostHookFrameDelay);
    Value->SetBoolField(TEXT("restoreVerified"), Receipt.RestoreVerified);
    return Value;
}

FP5aNativeRuntimeFrame CollectP5aNativeRuntimeFrame(
    AAlsTraceCharacter& Character, const FP5aFrameDefinition& Frame,
    const TArray<FP5aSourceDefinition>& Sources, const FP5aCanonicalAssetOracle& CanonicalOracle,
    const FP5aNativeTransitionStimulusReceipt* TransitionReceipt,
    const FP5aNativeTransitionStimulusContract& StimulusContract,
    FP5aNativeActionLifecycle& ActionLifecycle, const float PreviousActionTime,
    FP5aNativeNotifyObservationState& NotifyObservationState, const FP5aFrameUpdateAudit& FrameUpdateAudit,
    const FP5aNativeAuxiliaryInventory& Auxiliary)
{
    FP5aNativeRuntimeFrame Result;
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const TSharedRef<FJsonObject> Actual{MakeShared<FJsonObject>()};
    Actual->SetObjectField(TEXT("canonicalAssetOracle"), CanonicalOracle.Value.ToSharedRef());
    const TSharedRef<FJsonObject> UpdateAudit{MakeShared<FJsonObject>()};
    UpdateAudit->SetNumberField(TEXT("animationUpdates"), FrameUpdateAudit.AnimationUpdates);
    UpdateAudit->SetNumberField(TEXT("evaluations"), FrameUpdateAudit.Evaluations);
    UpdateAudit->SetNumberField(TEXT("postUpdates"), FrameUpdateAudit.PostUpdates);
    UpdateAudit->SetNumberField(TEXT("meshTicks"), FrameUpdateAudit.MeshTicks);
    Actual->SetObjectField(TEXT("frameUpdateAudit"), UpdateAudit);

    const TSharedRef<FJsonObject> CurveAudit{MakeShared<FJsonObject>()};
    CurveAudit->SetObjectField(TEXT("leftIk"), P5aCurveAuditToJson(*Animation, UAlsConstants::FootLeftIkCurveName()));
    CurveAudit->SetObjectField(TEXT("rightIk"), P5aCurveAuditToJson(*Animation, UAlsConstants::FootRightIkCurveName()));
    CurveAudit->SetObjectField(TEXT("leftLock"), P5aCurveAuditToJson(*Animation, UAlsConstants::FootLeftLockCurveName()));
    CurveAudit->SetObjectField(TEXT("rightLock"), P5aCurveAuditToJson(*Animation, UAlsConstants::FootRightLockCurveName()));
    CurveAudit->SetObjectField(TEXT("allowTransitions"), P5aCurveAuditToJson(*Animation, UAlsConstants::AllowTransitionsCurveName()));
    Actual->SetObjectField(TEXT("animGraphCurveAudit"), CurveAudit);

    TArray<TSharedPtr<FJsonValue>> Receipts;
    if (TransitionReceipt != nullptr)
    {
        Receipts.Add(MakeShared<FJsonValueObject>(P5aTransitionReceiptToJson(*TransitionReceipt)));
    }
    Actual->SetArrayField(TEXT("transitionStimulusReceipts"), Receipts);

    const FP5aSourceDefinition* TransitionSource{Sources.FindByPredicate([Animation](const FP5aSourceDefinition& Source)
    {
        if (Source.SourceKind != TEXT("Transition")) return false;
        for (UAnimSequenceBase* Asset : Source.NativeAssets)
        {
            if (Animation->DynamicMontage_IsPlayingFrom(Asset)) return true;
        }
        return false;
    })};
    const FP5aObservedSource* TransitionObserved{nullptr};
    UAnimSequenceBase* TransitionAsset{nullptr};
    FString TransitionRole;
    if (TransitionSource != nullptr)
    {
        for (int32 Index{0}; Index < TransitionSource->NativeAssets.Num(); ++Index)
        {
            if (Animation->DynamicMontage_IsPlayingFrom(TransitionSource->NativeAssets[Index]))
            {
                TransitionObserved = &TransitionSource->NativeVariants[Index];
                TransitionAsset = TransitionSource->NativeAssets[Index];
                TransitionRole = TransitionSource->NativeRoles[Index];
                break;
            }
        }
    }
    UAnimMontage* TransitionMontage{nullptr};
    FAnimMontageInstance* TransitionInstance{nullptr};
    if (IsValid(TransitionAsset) && Animation->IsPlayingSlotAnimation(
        TransitionAsset, UAlsConstants::TransitionSlotName(), TransitionMontage))
    {
        TransitionInstance = Animation->GetActiveInstanceForMontage(TransitionMontage);
    }
    const TSharedRef<FJsonObject> DynamicTransition{MakeShared<FJsonObject>()};
    DynamicTransition->SetBoolField(TEXT("active"), TransitionSource != nullptr);
    DynamicTransition->SetObjectField(TEXT("source"),
        P5aObservedSourceToJson(TransitionObserved, true));
    DynamicTransition->SetStringField(TEXT("nativeInstanceOrdinal"), TransitionSource != nullptr ? TEXT("1") : TEXT("0"));
    DynamicTransition->SetStringField(TEXT("foot"),
        TransitionRole.Contains(TEXT("right")) ? TEXT("Right") : TEXT("Left"));
    DynamicTransition->SetBoolField(TEXT("activatedAfterUpdate"), TransitionReceipt != nullptr);
    SetP5aFloatField(DynamicTransition, TEXT("previousTimeSeconds"),
        TransitionInstance != nullptr ? TransitionInstance->GetPreviousPosition() : 0.0f);
    SetP5aFloatField(DynamicTransition, TEXT("currentTimeSeconds"),
        TransitionInstance != nullptr ? TransitionInstance->GetPosition() : 0.0f);
    SetP5aFloatField(DynamicTransition, TEXT("playRate"),
        TransitionInstance != nullptr ? TransitionInstance->GetPlayRate() : 0.0f);
    SetP5aFloatField(DynamicTransition, TEXT("observedBlendInSeconds"),
        IsValid(TransitionMontage) ? TransitionMontage->BlendIn.GetBlendTime() : 0.0f);
    SetP5aFloatField(DynamicTransition, TEXT("observedBlendOutSeconds"),
        IsValid(TransitionMontage) ? TransitionMontage->BlendOut.GetBlendTime() : 0.0f);
    SetP5aFloatField(DynamicTransition, TEXT("observedEffectiveWeight"),
        TransitionInstance != nullptr ? TransitionInstance->GetWeight() : 0.0f);
    Actual->SetObjectField(TEXT("dynamicTransition"), DynamicTransition);

    FAnimMontageInstance* ActionInstance{IsValid(ActionLifecycle.Montage)
        ? Animation->GetInstanceForMontage(ActionLifecycle.Montage) : nullptr};
    const FP5aSourceDefinition* MontageSource{Sources.FindByPredicate([](const FP5aSourceDefinition& Source)
    {
        return Source.SourceKind == TEXT("ActionMontage");
    })};
    const FP5aSourceDefinition* SegmentSource{Sources.FindByPredicate([](const FP5aSourceDefinition& Source)
    {
        return Source.SourceKind == TEXT("ActionSequence");
    })};
    const bool bActionClosingThisFrame{ActionLifecycle.Outcomes.Num() > ActionLifecycle.EmittedOutcomeCount &&
        ActionLifecycle.Outcomes.Last().NativeReason != TEXT("Started")};
    if (bActionClosingThisFrame && !ActionLifecycle.TerminalPlayback.bCaptured)
    {
        UE_LOG(LogTemp, Error, TEXT("P5A terminal callback lacks a live native value snapshot."));
        return Result;
    }
    const bool bActionPlaying{ActionInstance != nullptr &&
        !ActionLifecycle.bOnMontageBlendingOutStartedObserved &&
        !ActionLifecycle.bOnMontageEndedObserved};
    const float GameplayActionTime{ActionInstance != nullptr ? ActionInstance->GetPosition() : 0.0f};
    const bool bActionPlaybackPresent{bActionPlaying || bActionClosingThisFrame};
    const float ClosingActionTime{bActionClosingThisFrame
        ? ActionLifecycle.TerminalPlayback.Position : GameplayActionTime};
    const float ObservedMontageTime{bActionClosingThisFrame ? ClosingActionTime : GameplayActionTime};
    FName CurrentSectionName{NAME_None};
    int32 SegmentIndex{INDEX_NONE};
    float PreviousClipTime{0.0f};
    float CurrentClipTime{0.0f};
    if (bActionPlaybackPresent && IsValid(ActionLifecycle.Montage))
    {
        CurrentSectionName = bActionClosingThisFrame ? ActionLifecycle.TerminalPlayback.SectionName
            : ActionInstance != nullptr
            ? ActionInstance->GetCurrentSection()
            : ActionLifecycle.Montage->GetSectionName(
                ActionLifecycle.Montage->GetSectionIndexFromPosition(ObservedMontageTime));
        for (const FSlotAnimationTrack& SlotTrack : ActionLifecycle.Montage->SlotAnimTracks)
        {
            if (SlotTrack.SlotName != FName{TEXT("PostLocomotion")}) continue;
            const FAnimSegment* Segment{SlotTrack.AnimTrack.GetSegmentAtTime(ObservedMontageTime)};
            if (Segment == nullptr) continue;
            SegmentIndex = SlotTrack.AnimTrack.AnimSegments.IndexOfByPredicate(
                [Segment](const FAnimSegment& Candidate) { return &Candidate == Segment; });
            PreviousClipTime = Segment->ConvertTrackPosToAnimPos(PreviousActionTime);
            CurrentClipTime = Segment->ConvertTrackPosToAnimPos(ObservedMontageTime);
            break;
        }
    }
    const TSharedRef<FJsonObject> ActionPlayback{MakeShared<FJsonObject>()};
    ActionPlayback->SetStringField(TEXT("status"), bActionClosingThisFrame
        ? TEXT("ClosingThisFrame") : (bActionPlaying ? TEXT("Playing") : TEXT("Inactive")));
    ActionPlayback->SetObjectField(TEXT("montageSource"), P5aObservedSourceToJson(
        (bActionPlaying || bActionClosingThisFrame) && MontageSource != nullptr ? &MontageSource->Native : nullptr, true));
    ActionPlayback->SetObjectField(TEXT("segmentSource"), P5aObservedSourceToJson(
        (bActionPlaying || bActionClosingThisFrame) && SegmentSource != nullptr ? &SegmentSource->Native : nullptr, true));
    ActionPlayback->SetStringField(TEXT("nativeInstanceOrdinal"),
        bActionPlaying || bActionClosingThisFrame ? TEXT("1") : TEXT("0"));
    ActionPlayback->SetStringField(TEXT("currentSectionName"),
        bActionPlaybackPresent ? CurrentSectionName.ToString() : FString{});
    ActionPlayback->SetNumberField(TEXT("segmentIndex"), SegmentIndex);
    SetP5aFloatField(ActionPlayback, TEXT("previousMontageTimeSeconds"),
        bActionPlaybackPresent ? PreviousActionTime : 0.0f);
    SetP5aFloatField(ActionPlayback, TEXT("currentMontageTimeSeconds"),
        bActionPlaybackPresent ? ClosingActionTime : 0.0f);
    SetP5aFloatField(ActionPlayback, TEXT("previousClipTimeSeconds"),
        bActionPlaybackPresent ? PreviousClipTime : 0.0f);
    SetP5aFloatField(ActionPlayback, TEXT("currentClipTimeSeconds"),
        bActionPlaybackPresent ? CurrentClipTime : 0.0f);
    SetP5aFloatField(ActionPlayback, TEXT("finalSegmentDeltaSeconds"),
        bActionClosingThisFrame && ActionLifecycle.bOnMontageEndedObserved
            ? FMath::Max(0.0f, ClosingActionTime - PreviousActionTime) : 0.0f);
    SetP5aFloatField(ActionPlayback, TEXT("playRate"),
        bActionPlaybackPresent
            ? (bActionClosingThisFrame ? ActionLifecycle.TerminalPlayback.PlayRate : ActionInstance->GetPlayRate())
            : 0.0f);
    Actual->SetObjectField(TEXT("actionPlayback"), ActionPlayback);

    const TSharedRef<FJsonObject> Visual{MakeShared<FJsonObject>()};
    const bool bVisualContributing{ActionInstance != nullptr};
    Visual->SetBoolField(TEXT("contributing"), bVisualContributing);
    Visual->SetObjectField(TEXT("montageSource"), P5aObservedSourceToJson(
        bVisualContributing && MontageSource != nullptr ? &MontageSource->Native : nullptr, true));
    Visual->SetStringField(TEXT("nativeInstanceOrdinal"), bVisualContributing ? TEXT("1") : TEXT("0"));
    SetP5aFloatField(Visual, TEXT("observedMontageTimeSeconds"),
        bVisualContributing ? GameplayActionTime : 0.0f);
    SetP5aFloatField(Visual, TEXT("observedBlendInSeconds"),
        bVisualContributing && IsValid(ActionLifecycle.Montage)
            ? ActionLifecycle.Montage->BlendIn.GetBlendTime() : 0.0f);
    SetP5aFloatField(Visual, TEXT("observedBlendOutSeconds"),
        bVisualContributing && IsValid(ActionLifecycle.Montage)
            ? ActionLifecycle.Montage->BlendOut.GetBlendTime() : 0.0f);
    Visual->SetNumberField(TEXT("observedBlendInOption"),
        bVisualContributing && IsValid(ActionLifecycle.Montage)
            ? static_cast<int32>(ActionLifecycle.Montage->BlendIn.GetBlendOption()) : -1);
    Visual->SetNumberField(TEXT("observedBlendOutOption"),
        bVisualContributing && IsValid(ActionLifecycle.Montage)
            ? static_cast<int32>(ActionLifecycle.Montage->BlendOut.GetBlendOption()) : -1);
    SetP5aFloatField(Visual, TEXT("observedEffectiveWeight"), ActionInstance != nullptr
        ? ActionInstance->GetWeight() : 0.0f);
    Actual->SetObjectField(TEXT("actionVisualContribution"), Visual);

    TArray<TSharedPtr<FJsonValue>> NativeTimeline;
    if (!AddP5aNativeTimelineEvents(*Animation, Sources, Auxiliary, ActionLifecycle, NotifyObservationState, NativeTimeline)) return Result;
    Actual->SetArrayField(TEXT("nativeRuntimeTimeline"), NativeTimeline);
    TArray<TSharedPtr<FJsonValue>> Outcomes;
    for (int32 OutcomeIndex{ActionLifecycle.EmittedOutcomeCount};
         OutcomeIndex < ActionLifecycle.Outcomes.Num(); ++OutcomeIndex)
    {
        const FP5aNativeActionOutcome& NativeOutcome{ActionLifecycle.Outcomes[OutcomeIndex]};
        const TSharedRef<FJsonObject> Outcome{MakeShared<FJsonObject>()};
        Outcome->SetObjectField(TEXT("actionSource"), P5aObservedSourceToJson(&MontageSource->Native, true));
        Outcome->SetStringField(TEXT("nativeInstanceOrdinal"), TEXT("1"));
        Outcome->SetStringField(TEXT("nativeReason"), NativeOutcome.NativeReason);
        Outcome->SetStringField(TEXT("callback"), NativeOutcome.Callback);
        Outcome->SetBoolField(TEXT("interrupted"), NativeOutcome.bInterrupted);
        Outcomes.Add(MakeShared<FJsonValueObject>(Outcome));
    }
    ActionLifecycle.EmittedOutcomeCount = ActionLifecycle.Outcomes.Num();
    Actual->SetArrayField(TEXT("actionOutcomes"), Outcomes);

    const TSharedRef<FJsonObject> StateAfter{MakeShared<FJsonObject>()};
    StateAfter->SetBoolField(TEXT("actionPlaying"), bActionPlaying);
    StateAfter->SetObjectField(TEXT("actionSource"), P5aObservedSourceToJson(
        bActionPlaying && MontageSource != nullptr ? &MontageSource->Native : nullptr, true));
    StateAfter->SetStringField(TEXT("actionNativeInstanceOrdinal"), bActionPlaying ? TEXT("1") : TEXT("0"));
    SetP5aFloatField(StateAfter, TEXT("actionTimeSeconds"),
        bActionPlaying ? GameplayActionTime : 0.0f);
    StateAfter->SetBoolField(TEXT("transitionPlaying"), TransitionSource != nullptr);
    StateAfter->SetObjectField(TEXT("transitionSource"), P5aObservedSourceToJson(
        TransitionObserved, true));
    StateAfter->SetStringField(TEXT("transitionNativeInstanceOrdinal"), TransitionSource != nullptr ? TEXT("1") : TEXT("0"));
    SetP5aFloatField(StateAfter, TEXT("transitionTimeSeconds"),
        TransitionInstance != nullptr ? TransitionInstance->GetPosition() : 0.0f);
    void* DynamicTransitionsState{
        StimulusContract.DynamicTransitionsState->ContainerPtrToValuePtr<void>(Animation)};
    StateAfter->SetNumberField(TEXT("transitionCooldownFrames"),
        StimulusContract.FrameDelay->GetPropertyValue_InContainer(DynamicTransitionsState));
    StateAfter->SetStringField(TEXT("transitionFoot"),
        TransitionRole.Contains(TEXT("right")) ? TEXT("Right") : TEXT("Left"));
    Actual->SetObjectField(TEXT("stateAfter"), StateAfter);
    if (bActionPlaying)
    {
        ActionLifecycle.LastPlaybackPosition = GameplayActionTime;
    }
    Result.Value = Actual;
    return Result;
}

TSharedRef<FJsonObject> P5aRawTraceToJson(const FP5aRawTrace& Trace)
{
    const TSharedRef<FJsonObject> Root{MakeShared<FJsonObject>()};
    Root->SetNumberField(TEXT("schemaVersion"), 2);
    Root->SetStringField(TEXT("kind"), TEXT("p5a_trace"));
    Root->SetStringField(TEXT("representation"), TEXT("native_raw"));
    Root->SetStringField(TEXT("tracePlanSha256"), Trace.TracePlanSha256);
    Root->SetObjectField(TEXT("reference"), Trace.Reference.ToSharedRef());
    Root->SetObjectField(TEXT("snapshot"), Trace.Snapshot.ToSharedRef());
    Root->SetStringField(TEXT("provenance"), TEXT("als_runtime"));
    Root->SetObjectField(TEXT("nativeReferenceAudit"), P5aNativeReferenceAuditToJson(Trace.NativeReferenceAudit));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (const FP5aRawCase& Case : Trace.Cases)
    {
        const TSharedRef<FJsonObject> CaseObject{MakeShared<FJsonObject>()};
        CaseObject->SetNumberField(TEXT("ordinal"), Case.Ordinal);
        CaseObject->SetStringField(TEXT("caseId"), Case.CaseId);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 FrameIndex{0}; FrameIndex < Case.Frames.Num(); ++FrameIndex)
        {
            const TSharedRef<FJsonObject> FrameObject{MakeShared<FJsonObject>()};
            FrameObject->SetNumberField(TEXT("frameIndex"), FrameIndex);
            FrameObject->SetObjectField(TEXT("nativeActual"), Case.Frames[FrameIndex].Value.ToSharedRef());
            Frames.Add(MakeShared<FJsonValueObject>(FrameObject));
        }
        CaseObject->SetArrayField(TEXT("frames"), Frames);
        Cases.Add(MakeShared<FJsonValueObject>(CaseObject));
    }
    Root->SetArrayField(TEXT("cases"), Cases);
    return Root;
}

TArray<FSequenceDefinition> CreateSequenceDefinitions()
{
    TArray<FSequenceDefinition> Definitions;
    Definitions.Add({TEXT("idle_gaits"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        if (Frame >= 30 && Frame < 90) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Walking; }
        else if (Frame >= 90 && Frame < 150) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Running; }
        else if (Frame >= 150 && Frame < 210) { Command.MovementAxes.Y = 1.0; Command.Gait = AlsGaitTags::Sprinting; }
        return Command;
    }});
    Definitions.Add({TEXT("directions"), 240, [](const int32 Frame)
    {
        static const FVector2D Directions[]{{0, 1}, {0, -1}, {-1, 0}, {1, 0}, {-1, 1}, {1, 1}, {-1, -1}, {1, -1}};
        FTraceCommand Command;
        Command.MovementAxes = Directions[FMath::Clamp(Frame / 30, 0, 7)].GetSafeNormal();
        return Command;
    }});
    Definitions.Add({TEXT("crouch_clearance"), 210, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes.Y = Frame < 90 || Frame >= 160 ? 0.75 : 0.0;
        Command.Stance = Frame < 30 || Frame >= 120 ? AlsStanceTags::Standing : AlsStanceTags::Crouching;
        Command.bStandBlocked = Frame >= 90 && Frame < 160;
        return Command;
    }});
    Definitions.Add({TEXT("rotation_modes"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes = FVector2D{0.65, 0.75}.GetSafeNormal();
        Command.ViewYaw = Frame * 0.75f;
        Command.AimYaw = Command.ViewYaw + 20.0f;
        if (Frame < 80) Command.RotationMode = AlsRotationModeTags::VelocityDirection;
        else if (Frame < 160) Command.RotationMode = AlsRotationModeTags::ViewDirection;
        else Command.RotationMode = AlsRotationModeTags::Aiming;
        return Command;
    }});
    Definitions.Add({TEXT("jump_land"), 240, [](const int32 Frame)
    {
        FTraceCommand Command;
        Command.MovementAxes.Y = Frame < 180 ? 0.65 : 0.0;
        Command.bJumpPressed = Frame == 30;
        return Command;
    }});
    return Definitions;
}

UWorld* CreateTraceWorld()
{
    const UWorld::InitializationValues InitializationValues{UWorld::InitializationValues()
        .AllowAudioPlayback(false)
        .RequiresHitProxies(false)
        .CreatePhysicsScene(true)
        .CreateNavigation(false)
        .CreateAISystem(false)
        .ShouldSimulatePhysics(true)
        .SetTransactional(false)};
    UWorld* World{UWorld::CreateWorld(EWorldType::Game, false, FName{TEXT("AlsLocomotionTraceWorld")}, nullptr,
        false, ERHIFeatureLevel::Num, &InitializationValues)};
    if (!IsValid(World)) return nullptr;
    FWorldContext& Context{GEngine->CreateNewWorldContext(EWorldType::Game)};
    Context.SetCurrentWorld(World);

    AActor* Floor{World->SpawnActor<AActor>()};
    UBoxComponent* FloorCollision{NewObject<UBoxComponent>(Floor, TEXT("TraceFloor"))};
    Floor->SetRootComponent(FloorCollision);
    FloorCollision->SetBoxExtent(FVector{5000.0, 5000.0, 10.0});
    FloorCollision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
    FloorCollision->RegisterComponent();
    Floor->SetActorLocation(FVector{0.0, 0.0, -10.0});
    World->InitializeActorsForPlay(FURL{});
    World->BeginPlay();
    return World;
}

void DestroyTraceWorld(UWorld* World)
{
    if (!IsValid(World)) return;
    GEngine->DestroyWorldContext(World);
    World->DestroyWorld(false);
}

void TickTraceCharacter(AAlsTraceCharacter& Character)
{
    Character.Tick(FixedDeltaSeconds);
    CastChecked<UAlsCharacterMovementComponent>(Character.GetCharacterMovement())->TickComponent(
        FixedDeltaSeconds, LEVELTICK_All, nullptr);
    Character.GetMesh()->TickAnimation(FixedDeltaSeconds, false);
    Character.GetMesh()->RefreshBoneTransforms();
}

bool RunP5aNativeActionLifecycleSelfTest(UWorld& World)
{
    const auto SpawnTraceCharacter{[&World](const double X)
    {
        FActorSpawnParameters SpawnParameters;
        SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
        AAlsTraceCharacter* Character{World.SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
            FVector{X, 0.0, 92.0}, FRotator::ZeroRotator, SpawnParameters)};
        AAlsTraceController* Controller{World.SpawnActor<AAlsTraceController>()};
        if (IsValid(Character) && !Character->HasActorBegunPlay())
        {
            Character->DispatchBeginPlay();
        }
        if (IsValid(Character) && IsValid(Controller))
        {
            Controller->Possess(Character);
        }
        return TPair<AAlsTraceCharacter*, AAlsTraceController*>{Character, Controller};
    }};
    const auto HasExactOutcome{[](const FP5aNativeActionLifecycle& Lifecycle,
                                  const TCHAR* Reason, const TCHAR* Callback, const bool bInterrupted)
    {
        int32 Matches{0};
        for (const FP5aNativeActionOutcome& Outcome : Lifecycle.Outcomes)
        {
            Matches += Outcome.NativeReason == Reason && Outcome.Callback == Callback &&
                Outcome.bInterrupted == bInterrupted ? 1 : 0;
        }
        return Matches == 1;
    }};

    TPair<AAlsTraceCharacter*, AAlsTraceController*> CancelPair{SpawnTraceCharacter(0.0)};
    if (!IsValid(CancelPair.Key) || !IsValid(CancelPair.Value) ||
        !IsValid(CancelPair.Key->GetTraceAnimationInstance()))
    {
        return false;
    }
    for (int32 Warmup{0}; Warmup < 15; ++Warmup)
    {
        TickP5aWorld(World, *CancelPair.Key);
    }
    FP5aNativeActionLifecycle CancelLifecycle;
    const TWeakObjectPtr<UAlsAnimationInstance> CancelAnimation{CancelPair.Key->GetTraceAnimationInstanceMutable()};
    ON_SCOPE_EXIT
    {
        UnbindP5aNativeActionObservers(CancelAnimation.Get(), CancelLifecycle);
    };
    if (!StartP5aNativeRollThroughPublicAlsPath(*CancelPair.Key, CancelLifecycle) ||
        !CancelP5aNativeRollThroughMontageStop(
            *CancelPair.Key->GetTraceAnimationInstanceMutable(), CancelLifecycle) ||
        !HasExactOutcome(CancelLifecycle, TEXT("Started"), TEXT("MontageStarted"), false) ||
        !HasExactOutcome(CancelLifecycle, TEXT("Cancelled"), TEXT("MontageBlendingOutStarted"), true))
    {
        return false;
    }
    UnbindP5aNativeActionObservers(CancelPair.Key->GetTraceAnimationInstanceMutable(), CancelLifecycle);
    CancelPair.Key->Destroy();
    CancelPair.Value->Destroy();

    TPair<AAlsTraceCharacter*, AAlsTraceController*> FinishPair{SpawnTraceCharacter(500.0)};
    if (!IsValid(FinishPair.Key) || !IsValid(FinishPair.Value) ||
        !IsValid(FinishPair.Key->GetTraceAnimationInstance()))
    {
        return false;
    }
    for (int32 Warmup{0}; Warmup < 15; ++Warmup)
    {
        TickP5aWorld(World, *FinishPair.Key);
    }
    FP5aNativeActionLifecycle FinishLifecycle;
    const TWeakObjectPtr<UAlsAnimationInstance> FinishAnimation{FinishPair.Key->GetTraceAnimationInstanceMutable()};
    ON_SCOPE_EXIT
    {
        UnbindP5aNativeActionObservers(FinishAnimation.Get(), FinishLifecycle);
    };
    if (!StartP5aNativeRollThroughPublicAlsPath(*FinishPair.Key, FinishLifecycle))
    {
        return false;
    }
    for (int32 Frame{0}; Frame < 120 && !FinishLifecycle.bOnMontageEndedObserved; ++Frame)
    {
        TickP5aWorld(World, *FinishPair.Key);
    }
    return HasExactOutcome(FinishLifecycle, TEXT("Started"), TEXT("MontageStarted"), false) &&
        HasExactOutcome(FinishLifecycle, TEXT("Finished"), TEXT("MontageEnded"), false) &&
        !FinishLifecycle.bOnMontageBlendingOutStartedObserved;
}

bool ValidateRequiredAssets()
{
    constexpr const TCHAR* CharacterSettingsPath{TEXT("/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default")};
    constexpr const TCHAR* MovementSettingsPath{TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal")};
    constexpr const TCHAR* AnimationSettingsPath{TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default")};
    constexpr const TCHAR* SkeletalMeshPath{TEXT("/ALS/ALS/Character/SKM_Als.SKM_Als")};
    constexpr const TCHAR* AnimationClassPath{TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C")};
    const TPair<const TCHAR*, bool> Assets[]
    {
		{CharacterSettingsPath, LoadObject<UAlsCharacterSettings>(nullptr, CharacterSettingsPath) != nullptr},
		{MovementSettingsPath, LoadObject<UAlsMovementSettings>(nullptr, MovementSettingsPath) != nullptr},
		{AnimationSettingsPath, LoadObject<UAlsAnimationInstanceSettings>(nullptr, AnimationSettingsPath) != nullptr},
		{SkeletalMeshPath, LoadObject<USkeletalMesh>(nullptr, SkeletalMeshPath) != nullptr},
		{AnimationClassPath, StaticLoadClass(UAlsAnimationInstance::StaticClass(), nullptr, AnimationClassPath) != nullptr},
    };
    for (const auto& Asset : Assets)
    {
        UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_ASSET_BEGIN path=%s"), Asset.Key);
        if (!Asset.Value)
        {
            UE_LOG(LogTemp, Error, TEXT("Required ALS asset has the wrong type or could not be loaded: %s"), Asset.Key);
            return false;
        }
        UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_ASSET_OK path=%s"), Asset.Key);
    }
    return true;
}

bool ValidateSequenceDefinitions()
{
    const TArray<FSequenceDefinition> Definitions{CreateSequenceDefinitions()};
    constexpr const TCHAR* ExpectedNames[]{TEXT("idle_gaits"), TEXT("directions"), TEXT("crouch_clearance"),
                                           TEXT("rotation_modes"), TEXT("jump_land")};
    constexpr int32 ExpectedFrameCounts[]{240, 240, 210, 240, 240};
    if (Definitions.Num() != UE_ARRAY_COUNT(ExpectedNames)) return false;
    for (int32 Index{0}; Index < Definitions.Num(); ++Index)
    {
        if (Definitions[Index].Name != ExpectedNames[Index] || Definitions[Index].FrameCount != ExpectedFrameCounts[Index])
        {
            return false;
        }
    }
    return true;
}

TSharedRef<FJsonObject> CreateSettingsDocument(const FString& Commit, const AAlsTraceCharacter& Character)
{
    const UAlsCharacterSettings* CharacterSettings{Character.GetSettings()};
    const UAlsMovementSettings* MovementSettings{Character.GetTraceMovementSettings()};
    const UAlsAnimationInstanceSettings* AnimationSettings{Character.GetTraceAnimationSettings()};
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const FAlsMovementStanceSettings* RotationSettings{MovementSettings->RotationModes.Find(AlsRotationModeTags::ViewDirection)};
    const FAlsMovementGaitSettings* Standing{RotationSettings->Stances.Find(AlsStanceTags::Standing)};
    const FAlsMovementGaitSettings* Crouching{RotationSettings->Stances.Find(AlsStanceTags::Crouching)};

    const TSharedRef<FJsonObject> Values{MakeShared<FJsonObject>()};
    Values->SetNumberField(TEXT("accelerationSmoothingHalfLife"), 0.1);
    Values->SetNumberField(TEXT("animatedCrouchSpeed"), AnimationSettings->Crouching.AnimatedCrouchSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedRunSpeed"), AnimationSettings->Standing.AnimatedRunSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedSprintSpeed"), AnimationSettings->Standing.AnimatedSprintSpeed / 100.0);
    Values->SetNumberField(TEXT("animatedWalkSpeed"), AnimationSettings->Standing.AnimatedWalkSpeed / 100.0);
    Values->SetNumberField(TEXT("crouchedHalfHeight"), Movement->GetCrouchedHalfHeight() / 100.0);
    Values->SetNumberField(TEXT("crouchRunForwardSpeed"), Crouching->RunForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("crouchWalkForwardSpeed"), Crouching->WalkForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("gravity"), FMath::Abs(Movement->GetGravityZ()) / 100.0);
    Values->SetNumberField(TEXT("initialMaxAcceleration"), Movement->GetMaxAcceleration() / 100.0);
    Values->SetNumberField(TEXT("initialMaxBrakingDeceleration"), Movement->GetMaxBrakingDeceleration() / 100.0);
    Values->SetNumberField(TEXT("jumpSpeed"), Movement->JumpZVelocity / 100.0);
    Values->SetNumberField(TEXT("landingRecoveryDuration"), 0.2);
    Values->SetNumberField(TEXT("leanHalfLife"), AnimationSettings->General.LeanInterpolationHalfLife);
    Values->SetNumberField(TEXT("movingSpeedThreshold"), CharacterSettings->MovingSpeedThreshold / 100.0);
    Values->SetNumberField(TEXT("playRateMaximum"), 3.0);
    Values->SetNumberField(TEXT("playRateMinimum"), 0.0);
    Values->SetNumberField(TEXT("rotationInterpolationHalfLife"), 0.1);
    Values->SetNumberField(TEXT("runBackwardSpeed"), Standing->RunBackwardSpeed / 100.0);
    Values->SetNumberField(TEXT("runForwardSpeed"), Standing->RunForwardSpeed / 100.0);
    Values->SetNumberField(TEXT("sprintSpeed"), Standing->SprintSpeed / 100.0);
    Values->SetNumberField(TEXT("standingHalfHeight"), Character.GetCapsuleComponent()->GetUnscaledCapsuleHalfHeight() / 100.0);
    Values->SetNumberField(TEXT("targetYawInterpolationSpeed"), 12.0);
    Values->SetNumberField(TEXT("velocityAngleInterpolationEnd"), FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Max));
    Values->SetNumberField(TEXT("velocityAngleInterpolationStart"), FMath::DegreesToRadians(MovementSettings->VelocityAngleToSpeedInterpolationRange.Min));
    Values->SetNumberField(TEXT("velocitySmoothingHalfLife"), 0.1);
    Values->SetNumberField(TEXT("walkBackwardSpeed"), Standing->WalkBackwardSpeed / 100.0);
    Values->SetNumberField(TEXT("walkForwardSpeed"), Standing->WalkForwardSpeed / 100.0);

    const TSharedRef<FJsonObject> Sources{MakeShared<FJsonObject>()};
	Sources->SetStringField(TEXT("animation"), TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default"));
	Sources->SetStringField(TEXT("character"), TEXT("/ALS/ALS/Data/Character/CS_Als_Default"));
	Sources->SetStringField(TEXT("movement"), TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal"));
    Sources->SetStringField(TEXT("portDefaults"), TEXT("accelerationSmoothingHalfLife,landingRecoveryDuration,playRateMaximum,playRateMinimum,rotationInterpolationHalfLife,targetYawInterpolationSpeed,velocitySmoothingHalfLife"));

    const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
    Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
    Document->SetStringField(TEXT("kind"), TEXT("settings"));
    Document->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    Document->SetStringField(TEXT("referenceCommit"), Commit);
    Document->SetNumberField(TEXT("schemaVersion"), SchemaVersion);
    Document->SetObjectField(TEXT("sources"), Sources);
    Document->SetObjectField(TEXT("values"), Values);
    return Document;
}

TSharedRef<FJsonObject> CreateCommandObject(const FTraceCommand& Command)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("aimYaw"), FMath::DegreesToRadians(Command.AimYaw));
    Object->SetBoolField(TEXT("jumpPressed"), Command.bJumpPressed);
    Object->SetObjectField(TEXT("movementAxes"), Vector2Object(Command.MovementAxes.X, Command.MovementAxes.Y));
    Object->SetStringField(TEXT("requestedGait"), GaitName(Command.Gait));
    Object->SetStringField(TEXT("requestedRotationMode"), RotationModeName(Command.RotationMode));
    Object->SetStringField(TEXT("requestedStance"), StanceName(Command.Stance));
    Object->SetBoolField(TEXT("standBlocked"), Command.bStandBlocked);
    Object->SetNumberField(TEXT("viewYaw"), FMath::DegreesToRadians(Command.ViewYaw));
    return Object;
}

TSharedRef<FJsonObject> SnapshotFrame(AAlsTraceCharacter& Character, const FTraceCommand& Command,
                                       const int32 Frame, const FVector& Origin, const bool bWasGrounded,
                                       FNativeObservationState& NativeState,
                                       const FPortSettings& PortSettings, FPortState& PortState,
                                       FVector& PreviousActualVelocity)
{
    const UAlsCharacterMovementComponent* Movement{Character.GetTraceMovement()};
    const UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstance()};
    const FAlsStandingState* Standing{GetAnimationState<FAlsStandingState>(Animation, TEXT("StandingState"))};
    const FAlsCrouchingState* Crouching{GetAnimationState<FAlsCrouchingState>(Animation, TEXT("CrouchingState"))};
    const FAlsLeanState* Lean{GetAnimationState<FAlsLeanState>(Animation, TEXT("LeanState"))};
    const FAlsLocomotionAnimationState* Locomotion{GetAnimationState<FAlsLocomotionAnimationState>(Animation, TEXT("LocomotionState"))};
    const bool bGrounded{Movement->IsMovingOnGround()};
    const FVector ActualVelocity{Character.GetVelocity()};
    const FVector ActualAcceleration{(ActualVelocity - PreviousActualVelocity) / FixedDeltaSeconds};
    const FVector LocalVelocity{Character.GetActorQuat().UnrotateVector(Character.GetVelocity())};
    const float Stride{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->StrideBlendAmount : 0.0f)
        : (Standing != nullptr ? Standing->StrideBlendAmount : 0.0f)};
    const float PlayRate{Character.GetStance() == AlsStanceTags::Crouching
        ? (Crouching != nullptr ? Crouching->PlayRate : 1.0f)
        : (Standing != nullptr ? Standing->PlayRate : 1.0f)};
    NativeState.AnimationPhase = FMath::Fmod(
        NativeState.AnimationPhase + static_cast<float>(FixedDeltaSeconds) * PlayRate, 1.0f);
    const bool bJumpTransition{!bGrounded && bWasGrounded};
    const bool bLandingTransition{bGrounded && !bWasGrounded};
    if (!bGrounded)
    {
        NativeState.LandingRecoveryFramesRemaining = 0;
    }
    else if (bLandingTransition)
    {
        NativeState.LandingRecoveryFramesRemaining = 13;
    }
    const bool bAcceptedJump{bJumpTransition && Command.bJumpPressed};
    if (bAcceptedJump)
    {
        NativeState.bJumpStartActive = true;
    }
    else if (bGrounded || ActualVelocity.Z <= 0.0)
    {
        NativeState.bJumpStartActive = false;
    }
    const FString ObservedAnimationState{!bGrounded
        ? (NativeState.bJumpStartActive ? TEXT("JumpStart") : TEXT("FallLoop"))
        : (NativeState.LandingRecoveryFramesRemaining > 0 ? TEXT("LandRecovery") : TEXT("Grounded"))};
    const FPortResult Port{EvaluatePort(
        Character, Command, bJumpTransition, ActualAcceleration, PortSettings, PortState)};

    const TSharedRef<FJsonObject> PhysicalActual{MakeShared<FJsonObject>()};
    PhysicalActual->SetObjectField(TEXT("acceleration"), Vector3Object(ActualAcceleration));
    PhysicalActual->SetBoolField(TEXT("grounded"), bGrounded);
    PhysicalActual->SetBoolField(TEXT("jumpTransition"), bJumpTransition);
    PhysicalActual->SetNumberField(TEXT("maxAcceleration"), Movement->GetMaxAcceleration() / 100.0);
    PhysicalActual->SetNumberField(TEXT("maxBrakingDeceleration"), Movement->GetMaxBrakingDeceleration() / 100.0);
    PhysicalActual->SetObjectField(TEXT("position"), Vector3Object(Character.GetActorLocation() - Origin));
    PhysicalActual->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    PhysicalActual->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    PhysicalActual->SetObjectField(TEXT("velocity"), Vector3Object(Character.GetVelocity()));
    PhysicalActual->SetNumberField(TEXT("yaw"), FMath::DegreesToRadians(Character.GetActorRotation().Yaw));

    const TSharedRef<FJsonObject> NativeActual{MakeShared<FJsonObject>()};
    NativeActual->SetObjectField(TEXT("blendCoordinates"), Vector2Object(LocalVelocity.Y / 100.0, LocalVelocity.X / 100.0));
    NativeActual->SetStringField(TEXT("gait"), GaitName(Character.GetGait()));
    NativeActual->SetObjectField(TEXT("lean"), Vector2Object(Lean != nullptr ? Lean->RightAmount : 0.0, Lean != nullptr ? Lean->ForwardAmount : 0.0));
    NativeActual->SetStringField(TEXT("locomotionState"), bGrounded ? TEXT("Grounded") : TEXT("InAir"));
    NativeActual->SetStringField(TEXT("observedAnimationState"), ObservedAnimationState);
    NativeActual->SetNumberField(TEXT("playRate"), PlayRate);
    NativeActual->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    NativeActual->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    NativeActual->SetNumberField(TEXT("stride"), Stride);
    NativeActual->SetNumberField(TEXT("synthesizedAnimationPhase"), NativeState.AnimationPhase);
    NativeActual->SetNumberField(TEXT("targetYaw"), FMath::DegreesToRadians(Locomotion != nullptr ? Locomotion->TargetYawAngleWorldSpace : Character.GetActorRotation().Yaw));
    if (NativeState.LandingRecoveryFramesRemaining > 0)
    {
        --NativeState.LandingRecoveryFramesRemaining;
    }

    const TSharedRef<FJsonObject> PortExpected{MakeShared<FJsonObject>()};
    PortExpected->SetNumberField(TEXT("animationPhase"), Port.AnimationPhase);
    PortExpected->SetStringField(TEXT("animationState"), Port.AnimationState);
    PortExpected->SetObjectField(TEXT("blendCoordinates"), Vector2Object(Port.BlendCoordinates.X, Port.BlendCoordinates.Y));
    PortExpected->SetStringField(TEXT("gait"), GaitName(Port.Gait));
    PortExpected->SetObjectField(TEXT("lean"), Vector2Object(Port.Lean.X, Port.Lean.Y));
    PortExpected->SetStringField(TEXT("locomotionState"), Port.LocomotionState);
    PortExpected->SetNumberField(TEXT("playRate"), Port.PlayRate);
    PortExpected->SetStringField(TEXT("rotationMode"), RotationModeName(Character.GetRotationMode()));
    PortExpected->SetStringField(TEXT("stance"), StanceName(Character.GetStance()));
    PortExpected->SetNumberField(TEXT("stride"), Port.Stride);
    PortExpected->SetNumberField(TEXT("targetYaw"), Port.TargetYaw);

    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("command"), CreateCommandObject(Command));
    Object->SetNumberField(TEXT("index"), Frame);
    Object->SetObjectField(TEXT("nativeActual"), NativeActual);
    Object->SetObjectField(TEXT("physicalActual"), PhysicalActual);
    Object->SetObjectField(TEXT("portExpected"), PortExpected);
    Object->SetNumberField(TEXT("tick"), Frame);
    Object->SetNumberField(TEXT("time"), Frame * FixedDeltaSeconds);
    PreviousActualVelocity = ActualVelocity;
    return Object;
}

bool GenerateSequence(UWorld* World, const FSequenceDefinition& Definition, const FString& Commit,
                      const FString& OutputDirectory)
{
    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AAlsTraceCharacter* Character{World->SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
        FVector{0.0, 0.0, 92.0}, FRotator::ZeroRotator, SpawnParameters)};
    AAlsTraceController* Controller{World->SpawnActor<AAlsTraceController>()};
    if (IsValid(Character) && !Character->HasActorBegunPlay()) Character->DispatchBeginPlay();
    if (IsValid(Controller) && IsValid(Character)) Controller->Possess(Character);
    if (!IsValid(Character) || !IsValid(Controller) || !IsValid(Character->GetTraceAnimationInstance()))
    {
        USkeletalMeshComponent* Mesh{IsValid(Character) ? Character->GetMesh() : nullptr};
        const UAnimInstance* MeshAnimationInstance{IsValid(Mesh) ? Mesh->GetAnimInstance() : nullptr};
        UE_LOG(LogTemp, Error,
            TEXT("Could not spawn trace character with UAlsAnimationInstance for %s: character=%d mesh=%d registered=%d animClass=%s animInstance=%s cachedAlsInstance=%d"),
            *Definition.Name, IsValid(Character) ? 1 : 0, IsValid(Mesh) ? 1 : 0,
            IsValid(Mesh) && Mesh->IsRegistered() ? 1 : 0,
            IsValid(Mesh) && IsValid(Mesh->GetAnimClass()) ? *Mesh->GetAnimClass()->GetPathName() : TEXT("<null>"),
            IsValid(MeshAnimationInstance) ? *MeshAnimationInstance->GetClass()->GetPathName() : TEXT("<null>"),
            IsValid(Character) && IsValid(Character->GetTraceAnimationInstance()) ? 1 : 0);
        return false;
    }

    for (int32 Warmup{0}; Warmup < 15; ++Warmup) TickTraceCharacter(*Character);
    const FVector Origin{Character->GetActorLocation()};
    TArray<TSharedPtr<FJsonValue>> Frames;
    Frames.Reserve(Definition.FrameCount);
    AActor* ClearanceBlocker{nullptr};
    bool bWasGrounded{Character->GetTraceMovement()->IsMovingOnGround()};
    FNativeObservationState NativeState;
    const FPortSettings PortSettings{CreatePortSettings(*Character)};
    FPortState PortState;
    FVector PreviousActualVelocity{Character->GetVelocity()};

    for (int32 Frame{0}; Frame < Definition.FrameCount; ++Frame)
    {
        FTraceCommand Command{Definition.GetCommand(Frame)};
        Command.MovementAxes = Command.MovementAxes.GetClampedToMaxSize(1.0f);
        if (Command.bStandBlocked && !IsValid(ClearanceBlocker))
        {
            ClearanceBlocker = World->SpawnActor<AActor>();
            UBoxComponent* Collision{NewObject<UBoxComponent>(ClearanceBlocker, TEXT("ClearanceBlocker"))};
            ClearanceBlocker->SetRootComponent(Collision);
            Collision->SetBoxExtent(FVector{80.0, 80.0, 10.0});
            Collision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
            Collision->RegisterComponent();
            const UCapsuleComponent* Capsule{Character->GetCapsuleComponent()};
            const double CrouchedCapsuleTop{Capsule->GetComponentLocation().Z + Capsule->GetScaledCapsuleHalfHeight()};
            ClearanceBlocker->SetActorLocation(FVector{Character->GetActorLocation().X, Character->GetActorLocation().Y,
                CrouchedCapsuleTop + 15.0});
        }
        else if (!Command.bStandBlocked && IsValid(ClearanceBlocker))
        {
            ClearanceBlocker->Destroy();
            ClearanceBlocker = nullptr;
        }

        Character->SetDesiredGait(Command.Gait);
        Character->SetDesiredStance(Command.Stance);
        Character->SetDesiredRotationMode(Command.RotationMode);
        Character->SetDesiredAiming(Command.RotationMode == AlsRotationModeTags::Aiming);
        Controller->SetControlRotation(FRotator{0.0,
            Command.RotationMode == AlsRotationModeTags::Aiming ? Command.AimYaw : Command.ViewYaw, 0.0});

        const FVector MovementDirection{Command.MovementAxes.Y, Command.MovementAxes.X, 0.0};
        if (!MovementDirection.IsNearlyZero())
        {
            Character->AddMovementInput(MovementDirection.GetSafeNormal(), MovementDirection.Size(), true);
        }
        if (Command.bJumpPressed) Character->Jump();

        TickTraceCharacter(*Character);
        const bool bGrounded{Character->GetTraceMovement()->IsMovingOnGround()};
        Frames.Add(MakeShared<FJsonValueObject>(SnapshotFrame(*Character, Command, Frame, Origin,
            bWasGrounded, NativeState, PortSettings, PortState, PreviousActualVelocity)));
        bWasGrounded = bGrounded;
    }

    Controller->UnPossess();
    Controller->Destroy();
    Character->Destroy();
    const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
    Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
    Document->SetArrayField(TEXT("frames"), Frames);
    Document->SetStringField(TEXT("kind"), TEXT("trace"));
    Document->SetStringField(TEXT("name"), Definition.Name);
    Document->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    Document->SetStringField(TEXT("referenceCommit"), Commit);
    Document->SetNumberField(TEXT("schemaVersion"), SchemaVersion);
    return SaveJson(OutputDirectory / FString::Printf(TEXT("trace_%s.json"), *Definition.Name), Document);
}

struct FP4CaseDefinition
{
    FString CaseId;
    FString Category;
    FString Stance;
    FString Direction;
    FString Phase;
    FString SourceAnimationObjectPath{TEXT("/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle")};
    TArray<FString> SourceCurveNames{TEXT("FootLock_L"), TEXT("FootLock_R")};
    float ViewYaw{0.0f};
    float ViewPitch{0.0f};
    float AimYaw{0.0f};
    float AimPitch{0.0f};
    FString RotationMode{TEXT("LookingDirection")};
    FVector FloorNormal{0.0, 1.0, 0.0};
    int32 PlatformId{-1};
    int64 PlatformColliderId{-1};
    FVector PlatformPosition{ForceInit};
    FQuat PlatformRotation{FQuat::Identity};
    int32 PreviousPlatformId{-1};
    int64 PreviousPlatformColliderId{-1};
    FVector PreviousPlatformPosition{ForceInit};
    FQuat PreviousPlatformRotation{FQuat::Identity};
    uint8 bInitialLocked{0};
    FVector LeftOrigin{-0.2, 0.9, 0.0};
    FVector RightOrigin{0.2, 0.9, 0.0};
    FVector LeftHitPosition{-0.2, 0.0, 0.0};
    FVector RightHitPosition{0.2, 0.0, 0.0};
    uint8 bValidHits{0};
    float LeftIkWeight{0.0f};
    float RightIkWeight{0.0f};
    float LeftLockCurve{0.0f};
    float RightLockCurve{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
};

struct FP4FootLockSnapshot
{
    FVector LeftWorld{ForceInit};
    FVector RightWorld{ForceInit};
    FVector LeftBase{ForceInit};
    FVector RightBase{ForceInit};
    FQuat LeftWorldRotation{FQuat::Identity};
    FQuat RightWorldRotation{FQuat::Identity};
    FQuat LeftBaseRotation{FQuat::Identity};
    FQuat RightBaseRotation{FQuat::Identity};
    float LeftAmount{0.0f};
    float RightAmount{0.0f};
};

struct FP4PlatformSnapshot
{
    UPrimitiveComponent* Component{nullptr};
    int32 PlatformId{-1};
    int64 ColliderId{-1};
    FTransform Transform{FTransform::Identity};
};

struct FP4FootHitCapture
{
    uint8 bValid{0};
    uint8 bWalkable{0};
    FVector Position{ForceInit};
    FVector Normal{0.0, 1.0, 0.0};
    int32 PlatformId{-1};
    int64 ColliderId{-1};
    FVector PlatformPosition{ForceInit};
    FQuat PlatformRotation{FQuat::Identity};
    FVector PointVelocity{ForceInit};
};

struct FP4StimulusCapture
{
    FVector CharacterPosition{ForceInit};
    float CharacterYaw{0.0f};
    float ViewYaw{0.0f};
    float ViewPitch{0.0f};
    float AimYaw{0.0f};
    float AimPitch{0.0f};
    FString RotationMode;
    FString Stance;
    uint8 bGrounded{0};
    float Speed{0.0f};
    float Acceleration{0.0f};
    FVector FloorNormal{0.0, 1.0, 0.0};
    FP4PlatformSnapshot CurrentPlatform;
    FP4PlatformSnapshot PreviousPlatform;
    FVector PlatformAngularVelocity{ForceInit};
    FP4FootLockSnapshot InitialLocks;
    FVector LeftFootOrigin{ForceInit};
    FVector RightFootOrigin{ForceInit};
    FP4FootHitCapture LeftFootHit;
    FP4FootHitCapture RightFootHit;
    float LeftIkWeight{0.0f};
    float RightIkWeight{0.0f};
    float LeftLockCurve{0.0f};
    float RightLockCurve{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
    float PreviousYawPhase{0.0f};
    float YawPhasePlayRate{0.0f};
    int32 TransitionFrameDelta{0};
    uint8 bLeftPlatformRemoved{0};
    int32 LeftRemovedPlatformId{-1};
    int64 LeftRemovedColliderId{-1};
    uint8 bRightPlatformRemoved{0};
    int32 RightRemovedPlatformId{-1};
    int64 RightRemovedColliderId{-1};
};

struct FP4RuntimePlayerObservation
{
    FString SourceAnimationObjectPath;
    FString InstanceClassPath;
    FString NodePropertyName;
    int32 NodePropertyOrdinal{-1};
    float BlendWeight{0.0f};
    float CurrentTimeSeconds{0.0f};
    float PreviousTimeSeconds{0.0f};
    float PreviousYawCurve{0.0f};
    float CurrentYawCurve{0.0f};
    float PreviousPhase{0.0f};
    float CurrentPhase{0.0f};
    float PhasePlayRate{0.0f};
};

struct FP4NativeRuntimeCapture
{
    FP4PlatformSnapshot CurrentPlatform;
    FP4PlatformSnapshot PreviousPlatform;
    FVector FloorNormal{0.0, 1.0, 0.0};
    FP4FootHitCapture LeftFootHit;
    FP4FootHitCapture RightFootHit;
};

struct FP4CaseRuntimeOwner
{
    explicit FP4CaseRuntimeOwner(UWorld& InWorld) : World(InWorld) {}

    ~FP4CaseRuntimeOwner()
    {
        if (IsValid(Controller)) Controller->UnPossess();
        Destroy(Controller);
        Destroy(Character);
        for (AActor* Actor : SceneActors) Destroy(Actor);
    }

    void Destroy(AActor* Actor) const
    {
        if (IsValid(Actor) && !Actor->IsActorBeingDestroyed()) World.DestroyActor(Actor);
    }

    UWorld& World;
    TArray<AActor*> SceneActors;
    AAlsTraceCharacter* Character{nullptr};
    AAlsTraceController* Controller{nullptr};
};

bool GetP4SelectionIds(const FString& RuntimeSourceAnimationObjectPath, const FString& Category,
                       int32& TurnAnimationId, int32& TurnCurveId, int32& TurnNominalDegrees,
                       int32& RotateAnimationId, int32& RotateCurveId);

struct FP4TickEvidence
{
    int32 AnimationUpdateCount{0};
    int32 AnimationEvaluationCount{0};
    int32 AnimationPostUpdateCount{0};
    int32 PublicAnimationTickCount{0};
};

FP4TickEvidence LastP4TickEvidence;

TSharedRef<FJsonObject> PortVector3Object(const FVector& Value)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X);
    Object->SetNumberField(TEXT("y"), Value.Y);
    Object->SetNumberField(TEXT("z"), Value.Z);
    return Object;
}

TSharedRef<FJsonObject> QuaternionObject(const FQuat& Source)
{
    FQuat Value{Source.GetNormalized()};
    if (Value.W < 0.0) Value = Value * -1.0;
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("x"), Value.X);
    Object->SetNumberField(TEXT("y"), Value.Y);
    Object->SetNumberField(TEXT("z"), Value.Z);
    Object->SetNumberField(TEXT("w"), Value.W);
    return Object;
}

TSharedRef<FJsonObject> TransformObject(const FVector& Position, const FQuat& Rotation)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("position"), PortVector3Object(Position));
    Object->SetObjectField(TEXT("rotation"), QuaternionObject(Rotation));
    return Object;
}

TSharedRef<FJsonObject> P4ReferenceObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("repository"), TEXT("https://github.com/Sixze/ALS-Refactored.git"));
    Object->SetStringField(TEXT("commit"), LockedReferenceCommit);
    Object->SetStringField(TEXT("targetEngine"), TEXT("5.9.0"));
    Object->SetArrayField(TEXT("patchHashes"), PatchHashArray());
    return Object;
}

TSharedRef<FJsonObject> P4CoordinateSystemObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("source"), TEXT("UE5_XForward_YRight_ZUp"));
    Object->SetStringField(TEXT("target"), TEXT("Godot_XRight_YUp_ZBack"));
    Object->SetStringField(TEXT("distanceUnit"), TEXT("meter"));
    Object->SetStringField(TEXT("angleUnit"), TEXT("radian"));
    Object->SetStringField(TEXT("quaternionSign"), TEXT("canonical_nonnegative_w"));
    return Object;
}

TSharedRef<FJsonObject> P4SourcesObject(const UClass& AimOverlayClass)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("characterSettings"), TEXT("/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default"));
    Object->SetStringField(TEXT("movementSettings"), TEXT("/ALS/ALS/Data/Character/Movement/MS_Als_Normal.MS_Als_Normal"));
    Object->SetStringField(TEXT("animationSettings"), TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default"));
    Object->SetStringField(TEXT("animationBlueprint"), TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));
    Object->SetStringField(TEXT("aimOverlayAnimationBlueprint"), AimOverlayClass.GetPathName());
    Object->SetStringField(TEXT("skeletalMesh"), TEXT("/ALS/ALS/Character/SKM_Als.SKM_Als"));
    Object->SetStringField(TEXT("controlRig"), TEXT("/ALS/ALS/Character/CR_Als.CR_Als_C"));
    return Object;
}

TSharedRef<FJsonObject> P4TolerancesObject()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("positionMeters"), 0.001);
    Object->SetNumberField(TEXT("rotationRadians"), 0.0017453292519943296);
    return Object;
}

TSharedRef<FJsonObject> P4FootHitObject(const FP4CaseDefinition& Definition, const bool bLeft)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("valid"), Definition.bValidHits);
    Object->SetNumberField(TEXT("walkable"), Definition.bValidHits);
    Object->SetObjectField(TEXT("position"), PortVector3Object(bLeft ? Definition.LeftHitPosition : Definition.RightHitPosition));
    Object->SetObjectField(TEXT("normal"), PortVector3Object(Definition.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Definition.PlatformId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Definition.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Definition.PlatformRotation));
    Object->SetNumberField(TEXT("colliderId"), Definition.bValidHits ? Definition.PlatformColliderId : -1);
    Object->SetObjectField(TEXT("pointVelocity"), PortVector3Object(FVector::ZeroVector));
    return Object;
}

TSharedRef<FJsonObject> P4StimulusObject(const FP4CaseDefinition& Definition)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("characterPosition"), PortVector3Object(FVector::ZeroVector));
    Object->SetNumberField(TEXT("characterYaw"), 0.0);
    Object->SetNumberField(TEXT("viewYaw"), Definition.ViewYaw);
    Object->SetNumberField(TEXT("viewPitch"), Definition.ViewPitch);
    Object->SetNumberField(TEXT("aimYaw"), Definition.AimYaw);
    Object->SetNumberField(TEXT("aimPitch"), Definition.AimPitch);
    Object->SetStringField(TEXT("rotationMode"), Definition.RotationMode);
    Object->SetStringField(TEXT("stance"), Definition.Stance);
    Object->SetNumberField(TEXT("grounded"), 1);
    Object->SetNumberField(TEXT("speed"), 0.0);
    Object->SetNumberField(TEXT("acceleration"), 0.0);
    Object->SetObjectField(TEXT("floorNormal"), PortVector3Object(Definition.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Definition.PlatformId);
    Object->SetNumberField(TEXT("platformColliderId"), Definition.PlatformColliderId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Definition.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Definition.PlatformRotation));
    Object->SetObjectField(TEXT("platformAngularVelocity"), PortVector3Object(FVector::ZeroVector));
    Object->SetNumberField(TEXT("previousPlatformId"), Definition.PreviousPlatformId);
    Object->SetNumberField(TEXT("previousPlatformColliderId"), Definition.PreviousPlatformColliderId);
    Object->SetObjectField(TEXT("previousPlatformPosition"), PortVector3Object(Definition.PreviousPlatformPosition));
    Object->SetObjectField(TEXT("previousPlatformRotation"), QuaternionObject(Definition.PreviousPlatformRotation));
    const auto CreateInitialLock{[&Definition](const bool bLeft)
    {
        const TSharedRef<FJsonObject> Lock{MakeShared<FJsonObject>()};
        Lock->SetObjectField(TEXT("localPosition"), PortVector3Object(bLeft ? Definition.LeftOrigin : Definition.RightOrigin));
        Lock->SetObjectField(TEXT("localRotation"), QuaternionObject(FQuat::Identity));
        Lock->SetNumberField(TEXT("amount"), Definition.bInitialLocked ? 1.0 : 0.0);
        Lock->SetNumberField(TEXT("locked"), Definition.bInitialLocked);
        return Lock;
    }};
    Object->SetObjectField(TEXT("initialLeftLock"), CreateInitialLock(true));
    Object->SetObjectField(TEXT("initialRightLock"), CreateInitialLock(false));
    Object->SetObjectField(TEXT("leftFootOrigin"), PortVector3Object(Definition.LeftOrigin));
    Object->SetObjectField(TEXT("rightFootOrigin"), PortVector3Object(Definition.RightOrigin));
    Object->SetObjectField(TEXT("leftFootHit"), P4FootHitObject(Definition, true));
    Object->SetObjectField(TEXT("rightFootHit"), P4FootHitObject(Definition, false));
    Object->SetNumberField(TEXT("leftIkWeight"), Definition.LeftIkWeight);
    Object->SetNumberField(TEXT("rightIkWeight"), Definition.RightIkWeight);
    Object->SetNumberField(TEXT("leftLockCurve"), Definition.LeftLockCurve);
    Object->SetNumberField(TEXT("rightLockCurve"), Definition.RightLockCurve);
    Object->SetNumberField(TEXT("previousYawCurve"), Definition.PreviousYawCurve);
    Object->SetNumberField(TEXT("currentYawCurve"), Definition.CurrentYawCurve);
    return Object;
}

FVector ToPortPosition(const FVector& UePosition)
{
    return FVector{UePosition.Y / 100.0, UePosition.Z / 100.0, -UePosition.X / 100.0};
}

FVector ToPortDirection(const FVector& UeDirection)
{
    return FVector{UeDirection.Y, UeDirection.Z, -UeDirection.X};
}

FQuat ToPortRotation(const FQuat& UeRotation)
{
    const FVector XAxis{ToPortDirection(UeRotation.RotateVector(FVector::RightVector))};
    const FVector YAxis{ToPortDirection(UeRotation.RotateVector(FVector::UpVector))};
    const FVector ZAxis{ToPortDirection(UeRotation.RotateVector(-FVector::ForwardVector))};
    FMatrix Matrix{FMatrix::Identity};
    Matrix.SetAxes(&XAxis, &YAxis, &ZAxis);
    FQuat Result{Matrix};
    Result.Normalize();
    if (Result.W < 0.0) Result = Result * -1.0;
    return Result;
}

int64 P4ColliderId(const UPrimitiveComponent* Component, const UPrimitiveComponent* Primary,
                   const UPrimitiveComponent* Secondary)
{
    if (Component == nullptr) return -1;
    if (Component == Primary) return 10;
    if (Component == Secondary) return 20;
    return 100;
}

FP4PlatformSnapshot CaptureP4Platform(UPrimitiveComponent* Component, const UPrimitiveComponent* Primary,
                                      const UPrimitiveComponent* Secondary)
{
    FP4PlatformSnapshot Snapshot;
    Snapshot.Component = Component;
    Snapshot.PlatformId = Component == Primary ? 1 : Component == Secondary ? 2 : -1;
    Snapshot.ColliderId = P4ColliderId(Component, Primary, Secondary);
    if (Component != nullptr) Snapshot.Transform = Component->GetComponentTransform();
    return Snapshot;
}

FP4FootLockSnapshot CaptureP4FootLocks(const FAlsFeetState& Feet)
{
    FP4FootLockSnapshot Snapshot;
    Snapshot.LeftWorld = Feet.Left.LockLocationWorldSpace;
    Snapshot.RightWorld = Feet.Right.LockLocationWorldSpace;
    Snapshot.LeftBase = FVector{Feet.Left.LockLocationMovementBaseSpace};
    Snapshot.RightBase = FVector{Feet.Right.LockLocationMovementBaseSpace};
    Snapshot.LeftWorldRotation = Feet.Left.LockRotationWorldSpace;
    Snapshot.RightWorldRotation = Feet.Right.LockRotationWorldSpace;
    Snapshot.LeftBaseRotation = FQuat{Feet.Left.LockRotationMovementBaseSpace};
    Snapshot.RightBaseRotation = FQuat{Feet.Right.LockRotationMovementBaseSpace};
    Snapshot.LeftAmount = Feet.Left.LockAmount;
    Snapshot.RightAmount = Feet.Right.LockAmount;
    return Snapshot;
}

FP4FootHitCapture CaptureP4FootHit(UWorld& World, AAlsTraceCharacter& Character, const FVector& Origin,
                                  const FP4PlatformSnapshot& CurrentPlatform,
                                  const FP4PlatformSnapshot& PreviousPlatform,
                                  const UPrimitiveComponent* Primary, const UPrimitiveComponent* Secondary)
{
    FP4FootHitCapture Capture;
    FHitResult Hit;
    FCollisionQueryParams QueryParams{SCENE_QUERY_STAT(P4GoldenFootHit), false, &Character};
    if (!World.LineTraceSingleByChannel(Hit, Origin + FVector::UpVector * 50.0,
        Origin - FVector::UpVector * 150.0, ECC_Visibility, QueryParams))
    {
        return Capture;
    }

    UPrimitiveComponent* Component{Hit.GetComponent()};
    Capture.bValid = 1;
    Capture.bWalkable = Character.GetCharacterMovement()->IsWalkable(Hit) ? 1 : 0;
    Capture.Position = ToPortPosition(Hit.ImpactPoint);
    Capture.Normal = ToPortDirection(Hit.ImpactNormal).GetSafeNormal();
    Capture.ColliderId = P4ColliderId(Component, Primary, Secondary);
    if (Component == CurrentPlatform.Component)
    {
        Capture.PlatformId = CurrentPlatform.PlatformId;
        Capture.PlatformPosition = ToPortPosition(CurrentPlatform.Transform.GetLocation());
        Capture.PlatformRotation = ToPortRotation(CurrentPlatform.Transform.GetRotation());
        if (PreviousPlatform.Component == CurrentPlatform.Component)
        {
            const FVector LinearVelocity{
                (CurrentPlatform.Transform.GetLocation() - PreviousPlatform.Transform.GetLocation()) / FixedDeltaSeconds};
            const FQuat RotationDelta{CurrentPlatform.Transform.GetRotation() * PreviousPlatform.Transform.GetRotation().Inverse()};
            const FVector AngularVelocity{RotationDelta.GetNormalized().ToRotationVector() / FixedDeltaSeconds};
            Capture.PointVelocity = ToPortDirection(LinearVelocity + FVector::CrossProduct(
                AngularVelocity, Hit.ImpactPoint - CurrentPlatform.Transform.GetLocation())) / 100.0;
        }
    }
    return Capture;
}

bool CaptureP4Stimulus(UWorld& World, AAlsTraceCharacter& Character, const FP4CaseDefinition& Definition,
                       const FP4PlatformSnapshot& PreviousPlatform, const FP4FootLockSnapshot& InitialLocks,
                       UPrimitiveComponent* Primary, UPrimitiveComponent* Secondary,
                       const FP4RuntimePlayerObservation& RuntimePlayer, const int32 TransitionFrameDelta,
                       FP4StimulusCapture& Capture)
{
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
    if (Feet == nullptr) return false;
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    if (Mesh->GetBoneIndex(TEXT("ik_foot_l")) < 0 || Mesh->GetBoneIndex(TEXT("ik_foot_r")) < 0)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 IK foot bone is unavailable case=%s"), *Definition.CaseId);
        return false;
    }

    const float CapsuleHalfHeight{Character.GetCapsuleComponent()->GetScaledCapsuleHalfHeight()};
    Capture.CharacterPosition = ToPortPosition(
        Character.GetActorLocation() - FVector::UpVector * CapsuleHalfHeight);
    Capture.CharacterYaw = -FMath::DegreesToRadians(Character.GetActorRotation().Yaw);
    const FRotator ViewRotation{Character.GetViewState().Rotation};
    Capture.ViewYaw = -FMath::DegreesToRadians(ViewRotation.Yaw);
    Capture.ViewPitch = FMath::DegreesToRadians(ViewRotation.Pitch);
    Capture.AimYaw = Capture.ViewYaw;
    Capture.AimPitch = Capture.ViewPitch;
    Capture.RotationMode = Definition.RotationMode;
    Capture.Stance = Definition.Stance;
    const UCharacterMovementComponent* Movement{Character.GetCharacterMovement()};
    Capture.bGrounded = Movement->IsMovingOnGround() ? 1 : 0;
    Capture.Speed = Movement->Velocity.Size2D() / 100.0f;
    Capture.Acceleration = Movement->GetCurrentAcceleration().Size2D() / 100.0f;
    Capture.FloorNormal = Movement->CurrentFloor.bBlockingHit
        ? ToPortDirection(Movement->CurrentFloor.HitResult.ImpactNormal).GetSafeNormal()
        : FVector{0.0, 1.0, 0.0};
    Capture.PreviousPlatform = PreviousPlatform;
    Capture.CurrentPlatform = CaptureP4Platform(Character.GetMovementBase(), Primary, Secondary);
    if (Capture.CurrentPlatform.Component == Capture.PreviousPlatform.Component &&
        Capture.CurrentPlatform.Component != nullptr)
    {
        const FQuat RotationDelta{Capture.CurrentPlatform.Transform.GetRotation() *
            Capture.PreviousPlatform.Transform.GetRotation().Inverse()};
        Capture.PlatformAngularVelocity = ToPortDirection(
            RotationDelta.GetNormalized().ToRotationVector() / FixedDeltaSeconds);
    }
    Capture.InitialLocks = InitialLocks;
    const FVector LeftOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_l"), EBoneSpaces::WorldSpace)};
    const FVector RightOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_r"), EBoneSpaces::WorldSpace)};
    Capture.LeftFootOrigin = ToPortPosition(LeftOrigin);
    Capture.RightFootOrigin = ToPortPosition(RightOrigin);
    Capture.LeftFootHit = CaptureP4FootHit(World, Character, LeftOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.RightFootHit = CaptureP4FootHit(World, Character, RightOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.LeftIkWeight = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootLeftIkCurveName()), 0.0f, 1.0f);
    Capture.RightIkWeight = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootRightIkCurveName()), 0.0f, 1.0f);
    Capture.LeftLockCurve = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootLeftLockCurveName()), 0.0f, 1.0f);
    Capture.RightLockCurve = FMath::Clamp(Animation->GetCurveValue(UAlsConstants::FootRightLockCurveName()), 0.0f, 1.0f);
    Capture.PreviousYawCurve = RuntimePlayer.PreviousYawCurve;
    Capture.CurrentYawCurve = RuntimePlayer.CurrentYawCurve;
    Capture.PreviousYawPhase = RuntimePlayer.PreviousPhase;
    Capture.YawPhasePlayRate = RuntimePlayer.PhasePlayRate;
    Capture.TransitionFrameDelta = TransitionFrameDelta;
    const bool bPlatformRemoved{Definition.CaseId == TEXT("platform_release") &&
        Capture.PreviousPlatform.PlatformId >= 0 && Capture.PreviousPlatform.ColliderId >= 0 &&
        Capture.CurrentPlatform.PlatformId < 0};
    Capture.bLeftPlatformRemoved = bPlatformRemoved ? 1 : 0;
    Capture.LeftRemovedPlatformId = bPlatformRemoved ? Capture.PreviousPlatform.PlatformId : -1;
    Capture.LeftRemovedColliderId = bPlatformRemoved ? Capture.PreviousPlatform.ColliderId : -1;
    Capture.bRightPlatformRemoved = bPlatformRemoved ? 1 : 0;
    Capture.RightRemovedPlatformId = bPlatformRemoved ? Capture.PreviousPlatform.PlatformId : -1;
    Capture.RightRemovedColliderId = bPlatformRemoved ? Capture.PreviousPlatform.ColliderId : -1;
    return true;
}

bool CaptureP4NativeRuntime(UWorld& World, AAlsTraceCharacter& Character,
                            const FP4PlatformSnapshot& PreviousPlatform,
                            UPrimitiveComponent* Primary, UPrimitiveComponent* Secondary,
                            FP4NativeRuntimeCapture& Capture)
{
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    if (Mesh == nullptr || Mesh->GetBoneIndex(TEXT("ik_foot_l")) < 0 ||
        Mesh->GetBoneIndex(TEXT("ik_foot_r")) < 0)
    {
        return false;
    }
    const UCharacterMovementComponent* Movement{Character.GetCharacterMovement()};
    Capture.PreviousPlatform = PreviousPlatform;
    Capture.CurrentPlatform = CaptureP4Platform(Character.GetMovementBase(), Primary, Secondary);
    Capture.FloorNormal = Movement->CurrentFloor.bBlockingHit
        ? ToPortDirection(Movement->CurrentFloor.HitResult.ImpactNormal).GetSafeNormal()
        : FVector{0.0, 1.0, 0.0};
    const FVector LeftOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_l"), EBoneSpaces::WorldSpace)};
    const FVector RightOrigin{Mesh->GetBoneLocation(TEXT("ik_foot_r"), EBoneSpaces::WorldSpace)};
    Capture.LeftFootHit = CaptureP4FootHit(World, Character, LeftOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    Capture.RightFootHit = CaptureP4FootHit(World, Character, RightOrigin, Capture.CurrentPlatform,
        Capture.PreviousPlatform, Primary, Secondary);
    return true;
}

TSharedRef<FJsonObject> P4FootHitObject(const FP4FootHitCapture& Capture)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("valid"), Capture.bValid);
    Object->SetNumberField(TEXT("walkable"), Capture.bWalkable);
    Object->SetObjectField(TEXT("position"), PortVector3Object(Capture.Position));
    Object->SetObjectField(TEXT("normal"), PortVector3Object(Capture.Normal));
    Object->SetNumberField(TEXT("platformId"), Capture.PlatformId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(Capture.PlatformPosition));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(Capture.PlatformRotation));
    Object->SetNumberField(TEXT("colliderId"), Capture.ColliderId);
    Object->SetObjectField(TEXT("pointVelocity"), PortVector3Object(Capture.PointVelocity));
    return Object;
}

TSharedRef<FJsonObject> P4StimulusObject(const FP4StimulusCapture& Capture)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("characterPosition"), PortVector3Object(Capture.CharacterPosition));
    Object->SetNumberField(TEXT("characterYaw"), Capture.CharacterYaw);
    Object->SetNumberField(TEXT("viewYaw"), Capture.ViewYaw);
    Object->SetNumberField(TEXT("viewPitch"), Capture.ViewPitch);
    Object->SetNumberField(TEXT("aimYaw"), Capture.AimYaw);
    Object->SetNumberField(TEXT("aimPitch"), Capture.AimPitch);
    Object->SetStringField(TEXT("rotationMode"), Capture.RotationMode);
    Object->SetStringField(TEXT("stance"), Capture.Stance);
    Object->SetNumberField(TEXT("grounded"), Capture.bGrounded);
    Object->SetNumberField(TEXT("speed"), Capture.Speed);
    Object->SetNumberField(TEXT("acceleration"), Capture.Acceleration);
    Object->SetObjectField(TEXT("floorNormal"), PortVector3Object(Capture.FloorNormal));
    Object->SetNumberField(TEXT("platformId"), Capture.CurrentPlatform.PlatformId);
    Object->SetNumberField(TEXT("platformColliderId"), Capture.CurrentPlatform.ColliderId);
    Object->SetObjectField(TEXT("platformPosition"), PortVector3Object(
        ToPortPosition(Capture.CurrentPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("platformRotation"), QuaternionObject(
        ToPortRotation(Capture.CurrentPlatform.Transform.GetRotation())));
    Object->SetObjectField(TEXT("platformAngularVelocity"), PortVector3Object(Capture.PlatformAngularVelocity));
    Object->SetNumberField(TEXT("previousPlatformId"), Capture.PreviousPlatform.PlatformId);
    Object->SetNumberField(TEXT("previousPlatformColliderId"), Capture.PreviousPlatform.ColliderId);
    Object->SetObjectField(TEXT("previousPlatformPosition"), PortVector3Object(
        ToPortPosition(Capture.PreviousPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("previousPlatformRotation"), QuaternionObject(
        ToPortRotation(Capture.PreviousPlatform.Transform.GetRotation())));
    const auto InitialLock{[](const FVector& Position, const FQuat& Rotation, const float Amount)
    {
        const TSharedRef<FJsonObject> Lock{MakeShared<FJsonObject>()};
        Lock->SetObjectField(TEXT("localPosition"), PortVector3Object(Amount > 0.0f ? ToPortPosition(Position) : FVector::ZeroVector));
        Lock->SetObjectField(TEXT("localRotation"), QuaternionObject(Amount > 0.0f ? ToPortRotation(Rotation) : FQuat::Identity));
        Lock->SetNumberField(TEXT("amount"), Amount);
        Lock->SetNumberField(TEXT("locked"), Amount > 0.0f ? 1 : 0);
        return Lock;
    }};
    const bool bPlatformLock{Capture.PreviousPlatform.PlatformId >= 0};
    Object->SetObjectField(TEXT("initialLeftLock"), InitialLock(
        bPlatformLock ? Capture.InitialLocks.LeftBase : Capture.InitialLocks.LeftWorld,
        bPlatformLock ? Capture.InitialLocks.LeftBaseRotation : Capture.InitialLocks.LeftWorldRotation,
        Capture.InitialLocks.LeftAmount));
    Object->SetObjectField(TEXT("initialRightLock"), InitialLock(
        bPlatformLock ? Capture.InitialLocks.RightBase : Capture.InitialLocks.RightWorld,
        bPlatformLock ? Capture.InitialLocks.RightBaseRotation : Capture.InitialLocks.RightWorldRotation,
        Capture.InitialLocks.RightAmount));
    Object->SetObjectField(TEXT("leftFootOrigin"), PortVector3Object(Capture.LeftFootOrigin));
    Object->SetObjectField(TEXT("rightFootOrigin"), PortVector3Object(Capture.RightFootOrigin));
    Object->SetObjectField(TEXT("leftFootHit"), P4FootHitObject(Capture.LeftFootHit));
    Object->SetObjectField(TEXT("rightFootHit"), P4FootHitObject(Capture.RightFootHit));
    Object->SetNumberField(TEXT("leftIkWeight"), Capture.LeftIkWeight);
    Object->SetNumberField(TEXT("rightIkWeight"), Capture.RightIkWeight);
    Object->SetNumberField(TEXT("leftLockCurve"), Capture.LeftLockCurve);
    Object->SetNumberField(TEXT("rightLockCurve"), Capture.RightLockCurve);
    Object->SetNumberField(TEXT("previousYawCurve"), Capture.PreviousYawCurve);
    Object->SetNumberField(TEXT("currentYawCurve"), Capture.CurrentYawCurve);
    Object->SetNumberField(TEXT("previousYawPhase"), Capture.PreviousYawPhase);
    Object->SetNumberField(TEXT("yawPhasePlayRate"), Capture.YawPhasePlayRate);
    Object->SetNumberField(TEXT("transitionFrameDelta"), Capture.TransitionFrameDelta);
    const TSharedRef<FJsonObject> RemovalSignals{MakeShared<FJsonObject>()};
    RemovalSignals->SetNumberField(TEXT("leftRemoved"), Capture.bLeftPlatformRemoved);
    RemovalSignals->SetNumberField(TEXT("leftPlatformId"), Capture.LeftRemovedPlatformId);
    RemovalSignals->SetNumberField(TEXT("leftColliderId"), Capture.LeftRemovedColliderId);
    RemovalSignals->SetNumberField(TEXT("rightRemoved"), Capture.bRightPlatformRemoved);
    RemovalSignals->SetNumberField(TEXT("rightPlatformId"), Capture.RightRemovedPlatformId);
    RemovalSignals->SetNumberField(TEXT("rightColliderId"), Capture.RightRemovedColliderId);
    Object->SetObjectField(TEXT("platformRemovalSignals"), RemovalSignals);
    return Object;
}

TSharedPtr<FJsonObject> P4BoneTransformObject(USkeletalMeshComponent& Mesh, const FName BoneName)
{
    const int32 BoneIndex{Mesh.GetBoneIndex(BoneName)};
    if (BoneIndex < 0)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required bone is unavailable bone=%s"), *BoneName.ToString());
        return nullptr;
    }
    const TArray<FTransform> LocalTransforms{Mesh.GetBoneSpaceTransforms()};
    if (!LocalTransforms.IsValidIndex(BoneIndex))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required bone transform is unavailable bone=%s index=%d count=%d"),
            *BoneName.ToString(), BoneIndex, LocalTransforms.Num());
        return nullptr;
    }
    const FTransform& Transform{LocalTransforms[BoneIndex]};
    return TransformObject(ToPortPosition(Transform.GetLocation()), ToPortRotation(Transform.GetRotation()));
}

TSharedPtr<FJsonObject> P4BoneComponentTransformObject(USkeletalMeshComponent& Mesh, const FName BoneName)
{
    const int32 BoneIndex{Mesh.GetBoneIndex(BoneName)};
    const TArray<FTransform>& ComponentTransforms{Mesh.GetComponentSpaceTransforms()};
    if (BoneIndex < 0 || !ComponentTransforms.IsValidIndex(BoneIndex))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required component-space bone transform is unavailable bone=%s index=%d count=%d"),
            *BoneName.ToString(), BoneIndex, ComponentTransforms.Num());
        return nullptr;
    }
    const FTransform& Transform{ComponentTransforms[BoneIndex]};
    return TransformObject(ToPortPosition(Transform.GetLocation()), ToPortRotation(Transform.GetRotation()));
}

TSharedRef<FJsonObject> P4CaseSourceObject(const FP4CaseDefinition& Definition,
                                           const FString& SourceAnimationObjectPath)
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("animationObjectPath"), SourceAnimationObjectPath);
    Object->SetStringField(TEXT("curveSourceObjectPath"), SourceAnimationObjectPath);
    TArray<TSharedPtr<FJsonValue>> CurveNames;
    for (const FString& CurveName : Definition.SourceCurveNames)
    {
        CurveNames.Add(MakeShared<FJsonValueString>(CurveName));
    }
    Object->SetArrayField(TEXT("curveNames"), CurveNames);
    return Object;
}

TSharedPtr<FJsonObject> P4NativeActualObject(AAlsTraceCharacter& Character, const FP4CaseDefinition& Definition,
                                             const float PreviousLeftFootLockAmount,
                                             const float PreviousRightFootLockAmount,
                                             const FP4FootLockSnapshot& PreviousFootLocks,
                                             const float TurnPhase, const float RotatePhase,
                                             const float TurnAccumulatedYaw, const float RotateAccumulatedYaw,
                                             const FString& LeftFootReleaseReason,
                                             const FString& RightFootReleaseReason,
                                             const bool bSourceSelectionVerified,
                                             const FP4RuntimePlayerObservation& RuntimePlayer,
                                             const int32 TransitionFrameDelta,
                                             const FP4NativeRuntimeCapture& RuntimeCapture)
{
    const UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstance()};
    const FAlsViewAnimationState* View{GetAnimationState<FAlsViewAnimationState>(Animation, TEXT("ViewState"))};
    const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(Animation, TEXT("SpineState"))};
    const FAlsLayeringState* Layering{GetAnimationState<FAlsLayeringState>(Animation, TEXT("LayeringState"))};
    const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
    const FAlsTurnInPlaceState* Turn{GetAnimationState<FAlsTurnInPlaceState>(Animation, TEXT("TurnInPlaceState"))};
    const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(Animation, TEXT("RotateInPlaceState"))};
    const FAlsMovementBaseState* MovementBase{GetAnimationState<FAlsMovementBaseState>(Animation, TEXT("MovementBase"))};
    if (View == nullptr || Spine == nullptr || Layering == nullptr || Feet == nullptr || Turn == nullptr ||
        Rotate == nullptr || MovementBase == nullptr)
    {
        UE_LOG(LogTemp, Error,
            TEXT("P4 native ALS state unavailable case=%s view=%d spine=%d layering=%d feet=%d turn=%d rotate=%d base=%d"),
            *Definition.CaseId, View != nullptr, Spine != nullptr, Layering != nullptr, Feet != nullptr,
            Turn != nullptr, Rotate != nullptr, MovementBase != nullptr);
        return nullptr;
    }

    const bool bTurnCase{Definition.Category == TEXT("Turn")};
    const bool bTurnActive{bTurnCase && Animation->GetCurrentActiveMontage() != nullptr};
    const int32 TurnDirection{bTurnActive ? (View->YawAngle < 0.0f ? 1 : -1) : 0};
    int32 TurnAnimationId;
    int32 TurnCurveId;
    int32 TurnNominalDegrees;
    int32 RotateAnimationId;
    int32 RotateCurveId;
    const bool bSelectionMapped{GetP4SelectionIds(RuntimePlayer.SourceAnimationObjectPath, Definition.Category,
        TurnAnimationId, TurnCurveId, TurnNominalDegrees, RotateAnimationId, RotateCurveId)};
    const bool bRotateActive{Definition.Category == TEXT("Rotate") &&
        (Rotate->bRotatingLeft || Rotate->bRotatingRight)};
    if ((bTurnActive || bRotateActive) && !bSelectionMapped)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 observed runtime source has no locked ID mapping category=%s path=%s"),
            *Definition.Category, *RuntimePlayer.SourceAnimationObjectPath);
        return nullptr;
    }
    const int32 RotateDirection{bRotateActive ? (Rotate->bRotatingLeft ? 1 : -1) : 0};
    if (!bTurnActive)
    {
        TurnAnimationId = -1;
        TurnCurveId = -1;
        TurnNominalDegrees = 0;
    }
    if (!bRotateActive)
    {
        RotateAnimationId = -1;
        RotateCurveId = -1;
    }
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    // UE positive yaw maps to negative Godot yaw after the (+Y,+Z,-X) basis conversion.
    Object->SetNumberField(TEXT("viewYaw"), -FMath::DegreesToRadians(View->YawAngle));
    Object->SetNumberField(TEXT("viewPitch"), FMath::DegreesToRadians(View->PitchAngle));
    Object->SetNumberField(TEXT("spineYaw"), -FMath::DegreesToRadians(Spine->FinalYawAngle));
    Object->SetNumberField(TEXT("headBlendAmount"), Layering->HeadBlendAmount);
    Object->SetNumberField(TEXT("turnUpdated"), Turn->bUpdatedThisFrame ? 1 : 0);
    Object->SetNumberField(TEXT("turnAnimationId"), TurnAnimationId);
    Object->SetNumberField(TEXT("turnCurveId"), TurnCurveId);
    Object->SetNumberField(TEXT("turnNominalDegrees"), TurnNominalDegrees);
    Object->SetNumberField(TEXT("turnActive"), bTurnActive ? 1 : 0);
    Object->SetNumberField(TEXT("turnDirection"), TurnDirection);
    Object->SetNumberField(TEXT("rotateAnimationId"), RotateAnimationId);
    Object->SetNumberField(TEXT("rotateCurveId"), RotateCurveId);
    Object->SetNumberField(TEXT("rotateDirection"), RotateDirection);
    Object->SetNumberField(TEXT("rotateActive"), bRotateActive ? 1 : 0);
    Object->SetNumberField(TEXT("rotateLeft"), Rotate->bRotatingLeft ? 1 : 0);
    Object->SetNumberField(TEXT("rotateRight"), Rotate->bRotatingRight ? 1 : 0);
    Object->SetNumberField(TEXT("playRate"), bTurnCase ? Turn->PlayRate : Rotate->PlayRate);
    Object->SetNumberField(TEXT("turnPhase"), TurnPhase);
    Object->SetNumberField(TEXT("rotatePhase"), RotatePhase);
    Object->SetNumberField(TEXT("turnAccumulatedYaw"), TurnAccumulatedYaw);
    Object->SetNumberField(TEXT("rotateAccumulatedYaw"), RotateAccumulatedYaw);
    Object->SetStringField(TEXT("runtimeSourceAnimationObjectPath"), RuntimePlayer.SourceAnimationObjectPath);
    Object->SetStringField(TEXT("runtimeInstanceClassPath"), RuntimePlayer.InstanceClassPath);
    Object->SetStringField(TEXT("runtimeNodePropertyName"), RuntimePlayer.NodePropertyName);
    Object->SetNumberField(TEXT("runtimeNodePropertyOrdinal"), RuntimePlayer.NodePropertyOrdinal);
    Object->SetNumberField(TEXT("runtimeSourceWeight"), RuntimePlayer.BlendWeight);
    Object->SetNumberField(TEXT("runtimeSourceTimeSeconds"), RuntimePlayer.CurrentTimeSeconds);
    Object->SetNumberField(TEXT("runtimePreviousSourceTimeSeconds"), RuntimePlayer.PreviousTimeSeconds);
    Object->SetNumberField(TEXT("runtimePreviousYawCurve"), RuntimePlayer.PreviousYawCurve);
    Object->SetNumberField(TEXT("runtimeCurrentYawCurve"), RuntimePlayer.CurrentYawCurve);
    Object->SetNumberField(TEXT("sourceSelectionVerified"), bSourceSelectionVerified ? 1 : 0);
    Object->SetNumberField(TEXT("rotationYawSpeed"),
        Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName()));
    Object->SetNumberField(TEXT("feetValid"), Feet->bValid ? 1 : 0);
    Object->SetNumberField(TEXT("movementBaseChanged"), MovementBase->bBaseChanged ? 1 : 0);
    Object->SetNumberField(TEXT("hasRelativeBaseLocation"), MovementBase->bHasRelativeLocation ? 1 : 0);
    Object->SetNumberField(TEXT("hasRelativeBaseRotation"), MovementBase->bHasRelativeRotation ? 1 : 0);
    Object->SetNumberField(TEXT("movementBaseDeltaYaw"), -FMath::DegreesToRadians(MovementBase->DeltaRotation.Yaw));
    Object->SetNumberField(TEXT("previousLeftFootLockAmount"), PreviousLeftFootLockAmount);
    Object->SetNumberField(TEXT("previousRightFootLockAmount"), PreviousRightFootLockAmount);
    Object->SetNumberField(TEXT("leftFootLockAmount"), Feet->Left.LockAmount);
    Object->SetNumberField(TEXT("rightFootLockAmount"), Feet->Right.LockAmount);
    const int32 LockedPlatformId{LeftFootReleaseReason != TEXT("None")
        ? RuntimeCapture.PreviousPlatform.PlatformId
        : RuntimeCapture.LeftFootHit.PlatformId};
    Object->SetNumberField(TEXT("leftFootPlatformId"), Feet->Left.LockAmount > UE_KINDA_SMALL_NUMBER
        ? LockedPlatformId : -1);
    Object->SetNumberField(TEXT("rightFootPlatformId"), Feet->Right.LockAmount > UE_KINDA_SMALL_NUMBER
        ? LockedPlatformId : -1);
    Object->SetStringField(TEXT("leftFootReleaseReason"), LeftFootReleaseReason);
    Object->SetStringField(TEXT("rightFootReleaseReason"), RightFootReleaseReason);
    Object->SetNumberField(TEXT("transitionFrameDelta"), TransitionFrameDelta);
    Object->SetNumberField(TEXT("animationUpdateCount"), LastP4TickEvidence.AnimationUpdateCount);
    Object->SetNumberField(TEXT("animationEvaluationCount"), LastP4TickEvidence.AnimationEvaluationCount);
    Object->SetNumberField(TEXT("animationPostUpdateCount"), LastP4TickEvidence.AnimationPostUpdateCount);
    Object->SetNumberField(TEXT("publicAnimationTickCount"), LastP4TickEvidence.PublicAnimationTickCount);
    Object->SetStringField(TEXT("footHitProvenance"), TEXT("derived_transition"));
    Object->SetStringField(TEXT("releaseReasonProvenance"), TEXT("derived_transition"));
    Object->SetNumberField(TEXT("movementBaseId"), RuntimeCapture.CurrentPlatform.PlatformId);
    Object->SetNumberField(TEXT("movementBaseColliderId"), RuntimeCapture.CurrentPlatform.ColliderId);
    Object->SetObjectField(TEXT("movementBasePosition"), PortVector3Object(
        ToPortPosition(RuntimeCapture.CurrentPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("movementBaseRotation"), QuaternionObject(
        ToPortRotation(RuntimeCapture.CurrentPlatform.Transform.GetRotation())));
    Object->SetNumberField(TEXT("previousMovementBaseId"), RuntimeCapture.PreviousPlatform.PlatformId);
    Object->SetNumberField(TEXT("previousMovementBaseColliderId"), RuntimeCapture.PreviousPlatform.ColliderId);
    Object->SetObjectField(TEXT("previousMovementBasePosition"), PortVector3Object(
        ToPortPosition(RuntimeCapture.PreviousPlatform.Transform.GetLocation())));
    Object->SetObjectField(TEXT("previousMovementBaseRotation"), QuaternionObject(
        ToPortRotation(RuntimeCapture.PreviousPlatform.Transform.GetRotation())));
    Object->SetObjectField(TEXT("actualFloorNormal"), PortVector3Object(RuntimeCapture.FloorNormal));
    Object->SetObjectField(TEXT("actualLeftFootHit"), P4FootHitObject(RuntimeCapture.LeftFootHit));
    Object->SetObjectField(TEXT("actualRightFootHit"), P4FootHitObject(RuntimeCapture.RightFootHit));
    Object->SetObjectField(TEXT("previousLeftLockWorldPosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.LeftWorld)));
    Object->SetObjectField(TEXT("previousRightLockWorldPosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.RightWorld)));
    Object->SetObjectField(TEXT("previousLeftLockBasePosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.LeftBase)));
    Object->SetObjectField(TEXT("previousRightLockBasePosition"), PortVector3Object(ToPortPosition(PreviousFootLocks.RightBase)));
    Object->SetObjectField(TEXT("leftLockWorldPosition"), PortVector3Object(ToPortPosition(Feet->Left.LockLocationWorldSpace)));
    Object->SetObjectField(TEXT("rightLockWorldPosition"), PortVector3Object(ToPortPosition(Feet->Right.LockLocationWorldSpace)));
    Object->SetObjectField(TEXT("leftLockBasePosition"), PortVector3Object(ToPortPosition(FVector{Feet->Left.LockLocationMovementBaseSpace})));
    Object->SetObjectField(TEXT("rightLockBasePosition"), PortVector3Object(ToPortPosition(FVector{Feet->Right.LockLocationMovementBaseSpace})));
    USkeletalMeshComponent* Mesh{Character.GetMesh()};
    const TSharedPtr<FJsonObject> Pelvis{P4BoneTransformObject(*Mesh, TEXT("pelvis"))};
    const TSharedPtr<FJsonObject> LeftFoot{P4BoneTransformObject(*Mesh, TEXT("foot_l"))};
    const TSharedPtr<FJsonObject> RightFoot{P4BoneTransformObject(*Mesh, TEXT("foot_r"))};
    const TSharedPtr<FJsonObject> PelvisComponent{P4BoneComponentTransformObject(*Mesh, TEXT("pelvis"))};
    const TSharedPtr<FJsonObject> LeftFootComponent{P4BoneComponentTransformObject(*Mesh, TEXT("foot_l"))};
    const TSharedPtr<FJsonObject> RightFootComponent{P4BoneComponentTransformObject(*Mesh, TEXT("foot_r"))};
    const TSharedPtr<FJsonObject> Head{P4BoneTransformObject(*Mesh, TEXT("head"))};
    const TSharedPtr<FJsonObject> SpineBone{P4BoneTransformObject(*Mesh, TEXT("spine_03"))};
    const TSharedPtr<FJsonObject> UpperBody{P4BoneTransformObject(*Mesh, TEXT("spine_01"))};
    const TSharedPtr<FJsonObject> HeadComponent{P4BoneComponentTransformObject(*Mesh, TEXT("head"))};
    const TSharedPtr<FJsonObject> SpineComponent{P4BoneComponentTransformObject(*Mesh, TEXT("spine_03"))};
    const TSharedPtr<FJsonObject> UpperBodyComponent{P4BoneComponentTransformObject(*Mesh, TEXT("spine_01"))};
    if (!Pelvis.IsValid() || !LeftFoot.IsValid() || !RightFoot.IsValid() ||
        !PelvisComponent.IsValid() || !LeftFootComponent.IsValid() || !RightFootComponent.IsValid() ||
        !Head.IsValid() || !SpineBone.IsValid() || !UpperBody.IsValid() || !HeadComponent.IsValid() ||
        !SpineComponent.IsValid() || !UpperBodyComponent.IsValid()) return nullptr;
    Object->SetObjectField(TEXT("pelvis"), Pelvis.ToSharedRef());
    Object->SetObjectField(TEXT("leftFoot"), LeftFoot.ToSharedRef());
    Object->SetObjectField(TEXT("rightFoot"), RightFoot.ToSharedRef());
    Object->SetObjectField(TEXT("pelvisComponent"), PelvisComponent.ToSharedRef());
    Object->SetObjectField(TEXT("leftFootComponent"), LeftFootComponent.ToSharedRef());
    Object->SetObjectField(TEXT("rightFootComponent"), RightFootComponent.ToSharedRef());
    Object->SetObjectField(TEXT("head"), Head.ToSharedRef());
    Object->SetObjectField(TEXT("spine"), SpineBone.ToSharedRef());
    Object->SetObjectField(TEXT("upperBody"), UpperBody.ToSharedRef());
    Object->SetObjectField(TEXT("headComponent"), HeadComponent.ToSharedRef());
    Object->SetObjectField(TEXT("spineComponent"), SpineComponent.ToSharedRef());
    Object->SetObjectField(TEXT("upperBodyComponent"), UpperBodyComponent.ToSharedRef());
    return Object;
}

TSharedRef<FJsonObject> EmptyP4FootOutput()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetObjectField(TEXT("position"), PortVector3Object(FVector::ZeroVector));
    Object->SetObjectField(TEXT("rotation"), QuaternionObject(FQuat::Identity));
    Object->SetNumberField(TEXT("lockAmount"), 0.0);
    Object->SetNumberField(TEXT("platformId"), -1);
    return Object;
}

TSharedRef<FJsonObject> EmptyP4PortExpected()
{
    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetNumberField(TEXT("aimRelativeYaw"), 0.0);
    Object->SetNumberField(TEXT("aimRelativePitch"), 0.0);
    Object->SetNumberField(TEXT("headWeight"), 0.0);
    Object->SetNumberField(TEXT("spineWeight"), 0.0);
    Object->SetNumberField(TEXT("upperBodyWeight"), 0.0);
    Object->SetNumberField(TEXT("spineResidualYaw"), 0.0);
    Object->SetNumberField(TEXT("turnAnimationId"), -1);
    Object->SetNumberField(TEXT("turnCurveId"), -1);
    Object->SetNumberField(TEXT("turnPhase"), 0.0);
    Object->SetNumberField(TEXT("turnPlayRate"), 0.0);
    Object->SetNumberField(TEXT("turnNominalDegrees"), 0);
    Object->SetNumberField(TEXT("turnDirection"), 0);
    Object->SetNumberField(TEXT("turnActive"), 0);
    Object->SetNumberField(TEXT("turnYawDelta"), 0.0);
    Object->SetNumberField(TEXT("rotateAnimationId"), -1);
    Object->SetNumberField(TEXT("rotateCurveId"), -1);
    Object->SetNumberField(TEXT("rotatePhase"), 0.0);
    Object->SetNumberField(TEXT("rotatePlayRate"), 0.0);
    Object->SetNumberField(TEXT("rotateDirection"), 0);
    Object->SetNumberField(TEXT("rotateActive"), 0);
    Object->SetNumberField(TEXT("rotateYawDelta"), 0.0);
    Object->SetObjectField(TEXT("pelvisOffset"), PortVector3Object(FVector::ZeroVector));
    Object->SetObjectField(TEXT("leftFoot"), EmptyP4FootOutput());
    Object->SetObjectField(TEXT("rightFoot"), EmptyP4FootOutput());
    Object->SetStringField(TEXT("leftReleaseReason"), TEXT("None"));
    Object->SetStringField(TEXT("rightReleaseReason"), TEXT("None"));
    return Object;
}

TArray<FP4CaseDefinition> CreateP4CaseDefinitions()
{
    TArray<FP4CaseDefinition> Cases;
    const auto AddAim{[&Cases](const TCHAR* Id, const TCHAR* Direction, const float Yaw, const float Pitch)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Aim"); Value.Stance = TEXT("Standing");
        Value.Direction = Direction; Value.Phase = TEXT("steady"); Value.RotationMode = TEXT("Aiming");
        Value.ViewYaw = Yaw; Value.AimYaw = Yaw; Value.ViewPitch = Pitch; Value.AimPitch = Pitch;
        Value.SourceAnimationObjectPath = TEXT("/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look");
        Value.SourceCurveNames = {TEXT("Layering_Head")};
        Cases.Add(Value);
    }};
    AddAim(TEXT("aim_center"), TEXT("Center"), 0.0f, 0.0f);
    AddAim(TEXT("aim_up"), TEXT("Up"), 0.0f, PI / 3.0f);
    AddAim(TEXT("aim_down"), TEXT("Down"), 0.0f, -PI / 3.0f);
    AddAim(TEXT("aim_left"), TEXT("Left"), -PI / 3.0f, 0.0f);
    AddAim(TEXT("aim_right"), TEXT("Right"), PI / 3.0f, 0.0f);

    const auto AddTurn{[&Cases](const TCHAR* Stance, const TCHAR* Direction, const int32 Angle)
    {
        FP4CaseDefinition Value;
        Value.CaseId = FString::Printf(TEXT("turn_%s_%s_%d"), *FString{Stance}.ToLower(), *FString{Direction}.ToLower(), Angle);
        Value.Category = TEXT("Turn"); Value.Stance = Stance; Value.Direction = Direction; Value.Phase = TEXT("playing");
        const float Sign{FString{Direction} == TEXT("Left") ? -1.0f : 1.0f};
        Value.ViewYaw = Sign * FMath::DegreesToRadians(Angle == 180 ? 170.0f : 90.0f);
        Value.AimYaw = Value.ViewYaw; Value.PreviousYawCurve = Sign; Value.CurrentYawCurve = Sign;
        const FString StancePrefix{FString{Stance} == TEXT("Crouching") ? TEXT("Crouch_") : TEXT("")};
        const FString AssetName{FString::Printf(TEXT("A_Als_%sTurn_%d_%s"), *StancePrefix, Angle, Direction)};
        Value.SourceAnimationObjectPath = FString::Printf(TEXT("/ALS/ALS/Animations/TurnInPlace/%s.%s"), *AssetName, *AssetName);
        Value.SourceCurveNames = {TEXT("RotationYawSpeed")};
        Cases.Add(Value);
    }};
    for (const TCHAR* Stance : {TEXT("Standing"), TEXT("Crouching")})
    {
        AddTurn(Stance, TEXT("Left"), 90); AddTurn(Stance, TEXT("Right"), 90);
        AddTurn(Stance, TEXT("Left"), 180); AddTurn(Stance, TEXT("Right"), 180);
    }

    const auto AddRotate{[&Cases](const TCHAR* Stance, const TCHAR* Direction)
    {
        FP4CaseDefinition Value;
        Value.CaseId = FString::Printf(TEXT("rotate_%s_%s"), *FString{Stance}.ToLower(), *FString{Direction}.ToLower());
        Value.Category = TEXT("Rotate"); Value.Stance = Stance; Value.Direction = Direction; Value.Phase = TEXT("playing");
        Value.RotationMode = TEXT("Aiming");
        const float Sign{FString{Direction} == TEXT("Left") ? -1.0f : 1.0f};
        Value.ViewYaw = Sign * FMath::DegreesToRadians(60.0f); Value.AimYaw = Value.ViewYaw;
        Value.PreviousYawCurve = Sign; Value.CurrentYawCurve = Sign;
        const FString StancePrefix{FString{Stance} == TEXT("Crouching") ? TEXT("Crouch_") : TEXT("")};
        const FString AssetName{FString::Printf(TEXT("A_Als_%sRotate_90_%s"), *StancePrefix, Direction)};
        Value.SourceAnimationObjectPath = FString::Printf(TEXT("/ALS/ALS/Animations/RotateInPlace/%s.%s"), *AssetName, *AssetName);
        Value.SourceCurveNames = {TEXT("RotationYawSpeed")};
        Cases.Add(Value);
    }};
    for (const TCHAR* Stance : {TEXT("Standing"), TEXT("Crouching")})
    {
        AddRotate(Stance, TEXT("Left")); AddRotate(Stance, TEXT("Right"));
    }

    const auto AddFeet{[&Cases](const TCHAR* Id, const FVector& Normal, const double LeftHeight, const double RightHeight)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Feet"); Value.Stance = TEXT("Standing");
        Value.Direction = TEXT("None"); Value.Phase = TEXT("steady"); Value.bValidHits = 1;
        Value.PlatformColliderId = 100;
        Value.FloorNormal = Normal.GetSafeNormal(); Value.LeftHitPosition.Y = LeftHeight; Value.RightHitPosition.Y = RightHeight;
        Value.LeftIkWeight = 1.0f; Value.RightIkWeight = 1.0f;
        Cases.Add(Value);
    }};
    AddFeet(TEXT("feet_flat"), FVector{0.0, 1.0, 0.0}, 0.0, 0.0);
    AddFeet(TEXT("feet_slope"), FVector{0.0, 0.9396926208, 0.3420201433}, 0.0, 0.0);
    AddFeet(TEXT("feet_stairs"), FVector{0.0, 1.0, 0.0}, 0.2, 0.0);

    const auto AddPlatform{[&Cases](const TCHAR* Id, const TCHAR* Phase)
    {
        FP4CaseDefinition Value;
        Value.CaseId = Id; Value.Category = TEXT("Platform"); Value.Stance = TEXT("Standing");
        Value.Direction = TEXT("None"); Value.Phase = Phase; Value.bValidHits = 1;
        Value.PlatformId = 1; Value.PlatformColliderId = 10;
        Value.PreviousPlatformId = 1; Value.PreviousPlatformColliderId = 10; Value.bInitialLocked = 1;
        Value.LeftOrigin = FVector{-0.2, 0.13, 0.0}; Value.RightOrigin = FVector{0.2, 0.13, 0.0};
        Value.LeftIkWeight = 1.0f; Value.RightIkWeight = 1.0f;
        Value.LeftLockCurve = 1.0f; Value.RightLockCurve = 1.0f;
        Cases.Add(Value);
    }};
    AddPlatform(TEXT("platform_translate"), TEXT("hold"));
    Cases.Last().PlatformPosition = FVector{0.1, 0.0, 0.0};
    Cases.Last().LeftHitPosition.X += 0.1; Cases.Last().RightHitPosition.X += 0.1;
    AddPlatform(TEXT("platform_rotate"), TEXT("hold"));
    Cases.Last().PlatformRotation = FQuat{FVector{0.0, 1.0, 0.0}, FMath::DegreesToRadians(10.0f)};
    AddPlatform(TEXT("platform_base_change"), TEXT("release"));
    Cases.Last().PlatformId = 2; Cases.Last().PlatformColliderId = 20;
    AddPlatform(TEXT("platform_teleport"), TEXT("release"));
    Cases.Last().PlatformPosition = FVector{2.0, 0.0, 0.0};
    AddPlatform(TEXT("platform_release"), TEXT("release"));
    Cases.Last().LeftLockCurve = 0.0f; Cases.Last().RightLockCurve = 0.0f;
    return Cases;
}

const UAlsTurnInPlaceSettings* SelectP4TurnSettings(const UAlsAnimationInstanceSettings& Settings,
                                                    const FP4CaseDefinition& Definition)
{
    const bool bLeft{Definition.Direction == TEXT("Left")};
    const bool bTurn180{FMath::Abs(Definition.ViewYaw) > FMath::DegreesToRadians(130.0f)};
    if (Definition.Stance == TEXT("Crouching"))
    {
        return bTurn180
            ? (bLeft ? Settings.TurnInPlace.CrouchingTurn180Left : Settings.TurnInPlace.CrouchingTurn180Right)
            : (bLeft ? Settings.TurnInPlace.CrouchingTurn90Left : Settings.TurnInPlace.CrouchingTurn90Right);
    }
    return bTurn180
        ? (bLeft ? Settings.TurnInPlace.StandingTurn180Left : Settings.TurnInPlace.StandingTurn180Right)
        : (bLeft ? Settings.TurnInPlace.StandingTurn90Left : Settings.TurnInPlace.StandingTurn90Right);
}

bool GetP4SelectionIds(const FString& RuntimeSourceAnimationObjectPath, const FString& Category,
                       int32& TurnAnimationId, int32& TurnCurveId, int32& TurnNominalDegrees,
                       int32& RotateAnimationId, int32& RotateCurveId)
{
    TurnAnimationId = -1;
    TurnCurveId = -1;
    TurnNominalDegrees = 0;
    RotateAnimationId = -1;
    RotateCurveId = -1;
    struct FLockedSelection
    {
        const TCHAR* Path;
        const TCHAR* Category;
        int32 AnimationId;
        int32 NominalDegrees;
    };
    static constexpr FLockedSelection Selections[]{
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Left.A_Als_Turn_90_Left"), TEXT("Turn"), 101, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Right.A_Als_Turn_90_Right"), TEXT("Turn"), 100, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_180_Left.A_Als_Turn_180_Left"), TEXT("Turn"), 103, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_180_Right.A_Als_Turn_180_Right"), TEXT("Turn"), 102, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_90_Left.A_Als_Crouch_Turn_90_Left"), TEXT("Turn"), 105, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_90_Right.A_Als_Crouch_Turn_90_Right"), TEXT("Turn"), 104, 90},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_180_Left.A_Als_Crouch_Turn_180_Left"), TEXT("Turn"), 107, 180},
        {TEXT("/ALS/ALS/Animations/TurnInPlace/A_Als_Crouch_Turn_180_Right.A_Als_Crouch_Turn_180_Right"), TEXT("Turn"), 106, 180},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Left.A_Als_Rotate_90_Left"), TEXT("Rotate"), 109, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Right.A_Als_Rotate_90_Right"), TEXT("Rotate"), 108, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Crouch_Rotate_90_Left.A_Als_Crouch_Rotate_90_Left"), TEXT("Rotate"), 111, 90},
        {TEXT("/ALS/ALS/Animations/RotateInPlace/A_Als_Crouch_Rotate_90_Right.A_Als_Crouch_Rotate_90_Right"), TEXT("Rotate"), 110, 90},
    };
    for (const FLockedSelection& Selection : Selections)
    {
        if (Category == Selection.Category && RuntimeSourceAnimationObjectPath == Selection.Path)
        {
            if (Category == TEXT("Turn"))
            {
                TurnAnimationId = Selection.AnimationId;
                TurnCurveId = Selection.AnimationId + 100;
                TurnNominalDegrees = Selection.NominalDegrees;
            }
            else
            {
                RotateAnimationId = Selection.AnimationId;
                RotateCurveId = Selection.AnimationId + 100;
            }
            return true;
        }
    }
    return Category != TEXT("Turn") && Category != TEXT("Rotate");
}

int32 SelectUniqueP4RuntimeCandidateIndex(const int32 CandidateCount)
{
    return CandidateCount == 1 ? 0 : INDEX_NONE;
}

bool ObserveP4RuntimeAssetPlayer(USkeletalMeshComponent& Mesh, UAnimationAsset& ExpectedAsset,
                                 const FName CurveName, FP4RuntimePlayerObservation& Observation)
{
    struct FCandidate
    {
        const FAnimNode_AssetPlayerRelevancyBase* Player{nullptr};
        const FStructProperty* Property{nullptr};
        UAnimInstance* Instance{nullptr};
        int32 Ordinal{-1};
    };
    TArray<FCandidate, TInlineAllocator<2>> Candidates;
    TArray<UAnimInstance*, TInlineAllocator<8>> Instances;
    if (UAnimInstance* Main{Mesh.GetAnimInstance()}; IsValid(Main)) Instances.Add(Main);
    const USkeletalMeshComponent& ConstMesh{Mesh};
    for (UAnimInstance* Linked : ConstMesh.GetLinkedAnimInstances())
    {
        if (IsValid(Linked)) Instances.AddUnique(Linked);
    }
    for (UAnimInstance* Instance : Instances)
    {
        const IAnimClassInterface* Interface{IAnimClassInterface::GetFromClass(Instance->GetClass())};
        if (Interface == nullptr) continue;
        int32 Ordinal{0};
        for (const FStructProperty* NodeProperty : Interface->GetAnimNodeProperties())
        {
            if (NodeProperty->Struct->IsChildOf(FAnimNode_AssetPlayerRelevancyBase::StaticStruct()))
            {
                const FAnimNode_AssetPlayerRelevancyBase* Player{
                    NodeProperty->ContainerPtrToValuePtr<FAnimNode_AssetPlayerRelevancyBase>(Instance)};
                if (Player != nullptr && Player->GetAnimAsset() == &ExpectedAsset &&
                    Player->GetCachedBlendWeight() > UE_KINDA_SMALL_NUMBER)
                {
                    Candidates.Add({Player, NodeProperty, Instance, Ordinal});
                }
            }
            ++Ordinal;
        }
    }
    const int32 SelectedCandidateIndex{SelectUniqueP4RuntimeCandidateIndex(Candidates.Num())};
    if (SelectedCandidateIndex == INDEX_NONE)
    {
        UE_LOG(LogTemp, Error,
            TEXT("P4 runtime asset player selection is not unique instance=%s asset=%s candidates=%d"),
            TEXT("<all-active-instances>"), *ExpectedAsset.GetPathName(), Candidates.Num());
        return false;
    }

    const FCandidate& Candidate{Candidates[SelectedCandidateIndex]};
    const FAnimNode_AssetPlayerRelevancyBase* BestPlayer{Candidate.Player};

    const FDeltaTimeRecord* DeltaRecord{BestPlayer->GetDeltaTimeRecord()};
    if (DeltaRecord == nullptr || !DeltaRecord->IsPreviousValid() || ExpectedAsset.GetPlayLength() <= UE_SMALL_NUMBER)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 runtime asset player time unavailable asset=%s"), *ExpectedAsset.GetPathName());
        return false;
    }
    Observation.SourceAnimationObjectPath = ExpectedAsset.GetPathName();
    Observation.InstanceClassPath = Candidate.Instance->GetClass()->GetPathName();
    Observation.NodePropertyName = Candidate.Property->GetName();
    Observation.NodePropertyOrdinal = Candidate.Ordinal;
    Observation.BlendWeight = BestPlayer->GetCachedBlendWeight();
    Observation.CurrentTimeSeconds = BestPlayer->GetAccumulatedTime();
    Observation.PreviousTimeSeconds = DeltaRecord->GetPrevious();
    const float Duration{ExpectedAsset.GetPlayLength()};
    Observation.CurrentPhase = Observation.CurrentTimeSeconds / Duration;
    Observation.PreviousPhase = Observation.PreviousTimeSeconds / Duration;
    float PhaseDelta{Observation.CurrentPhase - Observation.PreviousPhase};
    if (PhaseDelta < 0.0f && BestPlayer->IsLooping()) PhaseDelta += 1.0f;
    Observation.PhasePlayRate = PhaseDelta / static_cast<float>(FixedDeltaSeconds);
    if (!CurveName.IsNone())
    {
        const UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(&ExpectedAsset)};
        if (!IsValid(Sequence)) return false;
        Observation.PreviousYawCurve = Sequence->EvaluateCurveData(
            CurveName, FAnimExtractContext{Observation.PreviousTimeSeconds});
        Observation.CurrentYawCurve = Sequence->EvaluateCurveData(
            CurveName, FAnimExtractContext{Observation.CurrentTimeSeconds});
    }
    return true;
}

bool RunP4RuntimeSelectionSelfTest()
{
    // Two observations of the same asset at different phases are ambiguous unless node identity narrows them first.
    return SelectUniqueP4RuntimeCandidateIndex(1) == 0 &&
        SelectUniqueP4RuntimeCandidateIndex(0) == INDEX_NONE &&
        SelectUniqueP4RuntimeCandidateIndex(2) == INDEX_NONE;
}

FP4TickEvidence TickP4World(UWorld& World, AAlsTraceCharacter& Character, const FRotator* ForcedView = nullptr)
{
    if (ForcedView != nullptr)
    {
        Character.SetTraceViewRotation(*ForcedView);
    }
    UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
    const int16 PreviousUpdateCounter{Animation->GetUpdateCounter().Get()};
    UAlsTraceSkeletalMeshComponent* Mesh{Character.GetTraceMesh()};
    Mesh->ResetTracePipelineCounts();
    CommandletHelpers::TickEngine(&World, FixedDeltaSeconds);
    World.Tick(LEVELTICK_All, FixedDeltaSeconds);
    const int16 CurrentUpdateCounter{Animation->GetUpdateCounter().Get()};
    const int32 UpdateCount{static_cast<int32>(CurrentUpdateCounter) - PreviousUpdateCounter};
    LastP4TickEvidence = FP4TickEvidence{
        UpdateCount,
        Mesh->GetTraceEvaluationCount(),
        Mesh->GetTracePostUpdateCount(),
        Mesh->GetTracePublicTickCount(),
    };
    return LastP4TickEvidence;
}

UBoxComponent* SpawnP4Surface(UWorld& World, TArray<AActor*>& Actors, const TCHAR* Name,
                              const FVector& Location, const FVector& Extent,
                              const FRotator& Rotation = FRotator::ZeroRotator,
                              const bool bVisibilityQueryOnly = false)
{
    AActor* Actor{World.SpawnActor<AActor>()};
    if (!IsValid(Actor)) return nullptr;
    UBoxComponent* Collision{NewObject<UBoxComponent>(Actor, Name)};
    Actor->SetRootComponent(Collision);
    Collision->SetBoxExtent(Extent);
    if (bVisibilityQueryOnly)
    {
        Collision->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
        Collision->SetCollisionResponseToAllChannels(ECR_Ignore);
        Collision->SetCollisionResponseToChannel(ECC_Visibility, ECR_Block);
    }
    else
    {
        Collision->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
    }
    Collision->SetMobility(EComponentMobility::Movable);
    Collision->RegisterComponent();
    Actor->SetActorLocationAndRotation(Location, Rotation);
    Actors.Add(Actor);
    return Collision;
}

TPair<int32, int32> CaptureP4WorldCleanupBaseline(UWorld& World)
{
    int32 ActorCount{0};
    int32 CollisionCount{0};
    for (TActorIterator<AActor> Iterator{&World}; Iterator; ++Iterator)
    {
        AActor* Actor{*Iterator};
        if (!IsValid(Actor) || Actor->IsActorBeingDestroyed()) continue;
        ++ActorCount;
        TArray<UPrimitiveComponent*> Components;
        Actor->GetComponents(Components);
        for (const UPrimitiveComponent* Component : Components)
        {
            if (IsValid(Component) && Component->GetCollisionEnabled() != ECollisionEnabled::NoCollision)
            {
                ++CollisionCount;
            }
        }
    }
    return {ActorCount, CollisionCount};
}

bool RunP4CleanupSelfTest(UWorld& World)
{
    const TPair<int32, int32> Baseline{CaptureP4WorldCleanupBaseline(World)};
    for (int32 FailureStage{0}; FailureStage < 3; ++FailureStage)
    {
        {
            FP4CaseRuntimeOwner Owner{World};
            if (SpawnP4Surface(World, Owner.SceneActors, TEXT("P4CleanupSelfTestSurface"),
                FVector{0.0, 0.0, -500.0}, FVector{10.0, 10.0, 10.0}) == nullptr)
            {
                return false;
            }
            if (FailureStage > 0)
            {
                FActorSpawnParameters Parameters;
                Parameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
                Owner.Character = World.SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
                    FVector{0.0, 0.0, -400.0}, FRotator::ZeroRotator, Parameters);
                Owner.Controller = World.SpawnActor<AAlsTraceController>();
                if (!IsValid(Owner.Character) || !IsValid(Owner.Controller)) return false;
                if (!Owner.Character->HasActorBegunPlay()) Owner.Character->DispatchBeginPlay();
                Owner.Controller->Possess(Owner.Character);
                if (FailureStage > 1)
                {
                    Owner.Character->GetMesh()->SetAnimInstanceClass(nullptr);
                    if (Owner.Character->GetMesh()->GetAnimInstance() != nullptr) return false;
                }
            }
        }
        if (CaptureP4WorldCleanupBaseline(World) != Baseline)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 cleanup self-test leaked world state stage=%d"), FailureStage);
            return false;
        }
    }
    UE_LOG(LogTemp, Display, TEXT("P4_CLEANUP_SELF_TEST_OK stages=3"));
    return true;
}

bool TickUntilP4FootLock(UWorld& World, AAlsTraceCharacter& Character, const int32 MaximumFrames)
{
    for (int32 Frame{0}; Frame < MaximumFrames; ++Frame)
    {
        TickP4World(World, Character);
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(
            Character.GetTraceAnimationInstance(), TEXT("FeetState"))};
        if (Feet != nullptr && Feet->bValid &&
            FMath::Max(Feet->Left.LockAmount, Feet->Right.LockAmount) >= 0.99f)
        {
            return true;
        }
    }
    return false;
}

TSharedPtr<FJsonObject> GenerateP4Case(UWorld* World, const FP4CaseDefinition& Definition,
                                      UClass& AimOverlayClass)
{
    if (LoadObject<UAnimationAsset>(nullptr, *Definition.SourceAnimationObjectPath) == nullptr)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 source animation is unavailable case=%s path=%s"),
            *Definition.CaseId, *Definition.SourceAnimationObjectPath);
        return nullptr;
    }

    FP4CaseRuntimeOwner CaseOwner{*World};
    TArray<AActor*>& SceneActors{CaseOwner.SceneActors};
    UBoxComponent* PrimaryMovementBase{nullptr};
    UBoxComponent* SecondaryMovementBase{nullptr};
    FVector CharacterLocation{0.0, 0.0, 92.0};
    if (Definition.Category == TEXT("Feet") && Definition.CaseId == TEXT("feet_slope"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4Slope"),
            FVector{0.0, 0.0, 5.0}, FVector{100.0, 100.0, 5.0}, FRotator{0.0, 0.0, 20.0});
        CharacterLocation.Z = 112.0;
    }
    else if (Definition.Category == TEXT("Feet") && Definition.CaseId == TEXT("feet_stairs"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4StairLeft"),
            FVector{0.0, -22.0, 10.0}, FVector{60.0, 18.0, 10.0});
        SecondaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4StairRight"),
            FVector{0.0, 22.0, 2.5}, FVector{60.0, 18.0, 2.5});
        CharacterLocation.Z = 112.0;
    }
    else if (Definition.Category == TEXT("Platform"))
    {
        PrimaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4PlatformPrimary"),
            FVector{0.0, 0.0, 20.0}, FVector{250.0, 250.0, 20.0});
        SecondaryMovementBase = SpawnP4Surface(*World, SceneActors, TEXT("P4PlatformSecondary"),
            FVector{0.0, 0.0, -40.0}, FVector{250.0, 250.0, 20.0});
        CharacterLocation.Z = 132.0;
    }

    FActorSpawnParameters SpawnParameters;
    SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AAlsTraceCharacter* Character{World->SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
        CharacterLocation, FRotator::ZeroRotator, SpawnParameters)};
    AAlsTraceController* Controller{World->SpawnActor<AAlsTraceController>()};
    CaseOwner.Character = Character;
    CaseOwner.Controller = Controller;
    if (IsValid(Character) && !Character->HasActorBegunPlay()) Character->DispatchBeginPlay();
    if (IsValid(Controller) && IsValid(Character)) Controller->Possess(Character);
    if (!IsValid(Character) || !IsValid(Controller) || !IsValid(Character->GetTraceAnimationInstance()))
    {
        return nullptr;
    }
    if (Definition.Category == TEXT("Platform") && PrimaryMovementBase != nullptr)
    {
        Character->SetBase(PrimaryMovementBase);
    }
    const FGameplayTag CaseStance{Definition.Stance == TEXT("Crouching")
        ? AlsStanceTags::Crouching
        : AlsStanceTags::Standing};
    Character->ApplyTraceDesiredState(AlsRotationModeTags::ViewDirection, false,
        CaseStance, AlsOverlayModeTags::Default);
    Controller->SetControlRotation(FRotator::ZeroRotator);
    Character->SetTraceViewRotation(FRotator::ZeroRotator);
    for (int32 Warmup{0}; Warmup < 20; ++Warmup) TickP4World(*World, *Character);

    if (Definition.Category == TEXT("Feet"))
    {
        const auto TraceFoot{[&](const FVector& PortOrigin, UPrimitiveComponent* ExpectedComponent)
        {
            const FVector Center{-PortOrigin.Z * 100.0, PortOrigin.X * 100.0, PortOrigin.Y * 100.0};
            FHitResult Hit;
            FCollisionQueryParams QueryParams{SCENE_QUERY_STAT(P4FootSurface), false, Character};
            const bool bHit{World->LineTraceSingleByChannel(Hit, Center + FVector{0.0, 0.0, 50.0},
                Center - FVector{0.0, 0.0, 150.0}, ECC_Visibility, QueryParams)};
            return bHit && (ExpectedComponent == nullptr || Hit.GetComponent() == ExpectedComponent);
        }};
        const bool bLeftHit{TraceFoot(Definition.LeftOrigin, PrimaryMovementBase)};
        const bool bRightHit{TraceFoot(Definition.RightOrigin,
            Definition.CaseId == TEXT("feet_stairs") ? SecondaryMovementBase : PrimaryMovementBase)};
        if (!bLeftHit || !bRightHit)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 foot query surface mismatch case=%s left=%d right=%d"),
                *Definition.CaseId, bLeftHit, bRightHit);
            return nullptr;
        }
    }

    const bool bAiming{Definition.RotationMode == TEXT("Aiming")};
    if (bAiming)
    {
        Character->GetMesh()->LinkAnimClassLayers(&AimOverlayClass);
    }
    Character->ApplyTraceDesiredState(bAiming ? AlsRotationModeTags::Aiming : AlsRotationModeTags::ViewDirection,
        bAiming, CaseStance, bAiming ? AlsOverlayModeTags::Rifle : AlsOverlayModeTags::Default);
    const FRotator CaseView{FMath::RadiansToDegrees(Definition.ViewPitch),
        FMath::RadiansToDegrees(bAiming ? Definition.AimYaw : Definition.ViewYaw), 0.0};

    float PreviousLeftFootLockAmount{0.0f};
    float PreviousRightFootLockAmount{0.0f};
    FP4FootLockSnapshot PreviousFootLocks;
    FP4PlatformSnapshot PreviousPlatformSnapshot;
    UPrimitiveComponent* const TracePlatformPrimary{
        Definition.Category == TEXT("Platform") ? PrimaryMovementBase : nullptr};
    UPrimitiveComponent* const TracePlatformSecondary{
        Definition.Category == TEXT("Platform") ? SecondaryMovementBase : nullptr};
    float TurnPhase{0.0f};
    float RotatePhase{0.0f};
    float RotateAccumulatedYaw{0.0f};
    FP4RuntimePlayerObservation RuntimePlayer;
    int32 TransitionFrameDelta{0};
    bool bSourceSelectionVerified{false};
    FString LeftFootReleaseReason{TEXT("None")};
    FString RightFootReleaseReason{TEXT("None")};
    const float InitialActorYaw{static_cast<float>(Character->GetActorRotation().Yaw)};
    FString SourceAnimationObjectPath{Definition.SourceAnimationObjectPath};
    const UAlsTurnInPlaceSettings* SelectedTurnSettings{nullptr};
    bool bSampleReady{true};
    if (Definition.Category == TEXT("Turn"))
    {
        SelectedTurnSettings = SelectP4TurnSettings(*Character->GetTraceAnimationSettings(), Definition);
        if (!IsValid(SelectedTurnSettings) || !IsValid(SelectedTurnSettings->Sequence))
        {
            UE_LOG(LogTemp, Error, TEXT("P4 turn settings sequence is unavailable case=%s"), *Definition.CaseId);
            bSampleReady = false;
        }
        else
        {
            SourceAnimationObjectPath = SelectedTurnSettings->Sequence->GetPathName();
            if (SourceAnimationObjectPath != Definition.SourceAnimationObjectPath)
            {
                UE_LOG(LogTemp, Error, TEXT("P4 turn settings source mismatch case=%s expected=%s actual=%s"),
                    *Definition.CaseId, *Definition.SourceAnimationObjectPath, *SourceAnimationObjectPath);
                bSampleReady = false;
            }
        }
    }
    if (bAiming)
    {
        bSampleReady = false;
        const FRotator NeutralView{FRotator::ZeroRotator};
        Controller->SetControlRotation(NeutralView);
        Character->SetTraceViewRotation(NeutralView);
        for (int32 Frame{0}; Frame < 60; ++Frame)
        {
            TickP4World(*World, *Character, &NeutralView);
            const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(
                Character->GetTraceAnimationInstance(), TEXT("SpineState"))};
            const float PoseAiming{Character->GetTraceAnimationInstance()->GetCurveValue(
                UAlsConstants::PoseAimingCurveName())};
            if (Character->GetRotationMode() == AlsRotationModeTags::Aiming && Spine != nullptr &&
                Spine->bSpineRotationAllowed && Spine->SpineAmount >= 0.75f && PoseAiming >= 0.75f)
            {
                bSampleReady = true;
                break;
            }
        }
    }

    Controller->SetControlRotation(CaseView);
    Character->SetTraceViewRotation(CaseView);
    if (Definition.Category == TEXT("Turn"))
    {
        const bool bTurnSettingsReady{bSampleReady};
        bSampleReady = false;
        for (int32 Frame{0}; bTurnSettingsReady && Frame < 120; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstanceMutable()};
            UAnimMontage* Montage{Animation->GetCurrentActiveMontage()};
            if (IsValid(Montage) && Montage->SlotAnimTracks.Num() > 0 &&
                Montage->SlotAnimTracks[0].AnimTrack.AnimSegments.Num() > 0)
            {
                const UAnimSequenceBase* MontageSequence{
                    Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0].GetAnimReference()};
                if (MontageSequence != SelectedTurnSettings->Sequence)
                {
                    UE_LOG(LogTemp, Error,
                        TEXT("P4 active turn montage source mismatch case=%s expected=%s actual=%s"),
                        *Definition.CaseId, *SourceAnimationObjectPath,
                        IsValid(MontageSequence) ? *MontageSequence->GetPathName() : TEXT("<null>"));
                    break;
                }
                TurnPhase = Montage->GetPlayLength() > UE_SMALL_NUMBER
                    ? Animation->Montage_GetPosition(Montage) / Montage->GetPlayLength()
                    : 0.0f;
                const float RotationYawSpeed{
                    Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName())};
                if (TurnPhase >= 0.05f && TurnPhase <= 0.8f && FMath::Abs(RotationYawSpeed) > 1.0e-4f)
                {
                    const FAlsTurnInPlaceState* TurnState{GetAnimationState<FAlsTurnInPlaceState>(
                        Animation, TEXT("TurnInPlaceState"))};
                    FAnimMontageInstance* MontageInstance{Animation->GetActiveInstanceForMontage(Montage)};
                    const FAnimSegment& Segment{Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0]};
                    float SequenceTime{0.0f};
                    float PreviousTime{0.0f};
                    UAnimSequenceBase* CurrentSequence{MontageInstance != nullptr
                        ? Segment.GetAnimationData(MontageInstance->GetPosition(), SequenceTime) : nullptr};
                    UAnimSequenceBase* PreviousSequence{MontageInstance != nullptr
                        ? Segment.GetAnimationData(MontageInstance->GetPreviousPosition(), PreviousTime) : nullptr};
                    const float Duration{SelectedTurnSettings->Sequence->GetPlayLength()};
                    if (CurrentSequence != SelectedTurnSettings->Sequence || PreviousSequence != CurrentSequence ||
                        Duration <= UE_SMALL_NUMBER)
                    {
                        UE_LOG(LogTemp, Error, TEXT("P4 turn runtime source time unavailable case=%s"), *Definition.CaseId);
                        break;
                    }
                    RuntimePlayer.SourceAnimationObjectPath = CurrentSequence->GetPathName();
                    RuntimePlayer.InstanceClassPath = Animation->GetClass()->GetPathName();
                    RuntimePlayer.NodePropertyName = TEXT("ActiveTurnMontageSlot0Segment0");
                    RuntimePlayer.NodePropertyOrdinal = 0;
                    RuntimePlayer.BlendWeight = MontageInstance->GetWeight();
                    RuntimePlayer.CurrentTimeSeconds = SequenceTime;
                    RuntimePlayer.PreviousTimeSeconds = PreviousTime;
                    RuntimePlayer.CurrentPhase = SequenceTime / Duration;
                    RuntimePlayer.PreviousPhase = PreviousTime / Duration;
                    RuntimePlayer.PhasePlayRate = (RuntimePlayer.CurrentPhase - RuntimePlayer.PreviousPhase) /
                        static_cast<float>(FixedDeltaSeconds);
                    RuntimePlayer.PreviousYawCurve = CurrentSequence->EvaluateCurveData(
                        UAlsConstants::RotationYawSpeedCurveName(), FAnimExtractContext{PreviousTime});
                    RuntimePlayer.CurrentYawCurve = CurrentSequence->EvaluateCurveData(
                        UAlsConstants::RotationYawSpeedCurveName(), FAnimExtractContext{SequenceTime});
                    TurnPhase = RuntimePlayer.CurrentPhase;
                    bSourceSelectionVerified = RuntimePlayer.SourceAnimationObjectPath == SourceAnimationObjectPath &&
                        FMath::Abs(RuntimePlayer.CurrentYawCurve) > 1.0e-4f && TurnState != nullptr;
                    bSampleReady = bSourceSelectionVerified;
                    break;
                }
            }
        }
    }
    else if (Definition.Category == TEXT("Aim"))
    {
        const bool bAimingReady{bSampleReady};
        bSampleReady = false;
        for (int32 Frame{0}; bAimingReady && Frame < 10; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(
                Character->GetTraceAnimationInstance(), TEXT("SpineState"))};
            if (Spine != nullptr && Spine->bSpineRotationAllowed && Spine->SpineAmount >= 0.75f)
            {
                bSampleReady = (Definition.Direction != TEXT("Left") && Definition.Direction != TEXT("Right")) ||
                    FMath::Abs(Spine->FinalYawAngle) >= 1.0f;
                if (bSampleReady)
                {
                    UAnimationAsset* AimAsset{LoadObject<UAnimationAsset>(nullptr, *Definition.SourceAnimationObjectPath)};
                    bSourceSelectionVerified = IsValid(AimAsset) &&
                        ObserveP4RuntimeAssetPlayer(*Character->GetMesh(), *AimAsset, NAME_None, RuntimePlayer) &&
                        Character->GetRotationMode() == AlsRotationModeTags::Aiming &&
                        Character->GetMesh()->GetLinkedAnimLayerInstanceByClass(&AimOverlayClass) != nullptr;
                    bSampleReady = bSourceSelectionVerified;
                    break;
                }
            }
        }
    }
    else if (Definition.Category == TEXT("Rotate"))
    {
        const bool bAimingReady{bSampleReady};
        bSampleReady = false;
        UAnimSequenceBase* RotateSequence{LoadObject<UAnimSequenceBase>(nullptr, *Definition.SourceAnimationObjectPath)};
        for (int32 Frame{0}; bAimingReady && IsValid(RotateSequence) && Frame < 60; ++Frame)
        {
            TickP4World(*World, *Character, &CaseView);
            const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(
                Character->GetTraceAnimationInstance(), TEXT("RotateInPlaceState"))};
            const bool bExpectedDirection{Rotate != nullptr &&
                (Definition.Direction == TEXT("Left") ? Rotate->bRotatingLeft : Rotate->bRotatingRight)};
            const float RotationYawSpeed{Character->GetTraceAnimationInstance()->GetCurveValue(
                UAlsConstants::RotationYawSpeedCurveName())};
            // ALS applies this curve as degrees per second to actor yaw. Convert the public runtime
            // sample to Godot radians while preserving every sampled frame in the trace window.
            RotateAccumulatedYaw -= FMath::DegreesToRadians(
                RotationYawSpeed * static_cast<float>(FixedDeltaSeconds));
            if (bExpectedDirection && FMath::Abs(RotationYawSpeed) > 1.0e-4f)
            {
                bSourceSelectionVerified = ObserveP4RuntimeAssetPlayer(*Character->GetMesh(), *RotateSequence,
                    UAlsConstants::RotationYawSpeedCurveName(), RuntimePlayer) &&
                    RuntimePlayer.SourceAnimationObjectPath == Definition.SourceAnimationObjectPath &&
                    FMath::Abs(RuntimePlayer.CurrentYawCurve) > 1.0e-4f;
                RotatePhase = RuntimePlayer.CurrentPhase;
                bSampleReady = bSourceSelectionVerified;
                if (bSampleReady) break;
            }
        }
    }
    else
    {
        bSampleReady = TickUntilP4FootLock(*World, *Character, 180);
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(
            Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
        if (Feet != nullptr)
        {
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousLeftFootLockAmount = PreviousFootLocks.LeftAmount;
            PreviousRightFootLockAmount = PreviousFootLocks.RightAmount;
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(),
                TracePlatformPrimary, TracePlatformSecondary);
        }
    }

    if (bSampleReady && Definition.Category == TEXT("Platform") && PrimaryMovementBase != nullptr)
    {
        TransitionFrameDelta = 1;
        AActor* PrimaryActor{PrimaryMovementBase->GetOwner()};
        if (Definition.CaseId == TEXT("platform_translate"))
        {
            for (int32 Frame{0}; Frame < 9; ++Frame)
            {
                PrimaryActor->AddActorWorldOffset(FVector{0.0, 1.0, 0.0});
                TickP4World(*World, *Character);
            }
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldOffset(FVector{0.0, 1.0, 0.0});
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_rotate"))
        {
            for (int32 Frame{0}; Frame < 9; ++Frame)
            {
                PrimaryActor->AddActorWorldRotation(FRotator{0.0, -1.0, 0.0});
                TickP4World(*World, *Character);
            }
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldRotation(FRotator{0.0, -1.0, 0.0});
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_base_change") && SecondaryMovementBase != nullptr)
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            SecondaryMovementBase->GetOwner()->SetActorTransform(PrimaryActor->GetActorTransform());
            PrimaryMovementBase->SetCollisionEnabled(ECollisionEnabled::NoCollision);
            Character->SetBase(SecondaryMovementBase);
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_teleport"))
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            PreviousFootLocks = CaptureP4FootLocks(*Feet);
            PreviousPlatformSnapshot = CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
            PrimaryActor->AddActorWorldOffset(FVector{0.0, 200.0, 0.0}, false, nullptr, ETeleportType::TeleportPhysics);
            Character->GetTraceAnimationInstanceMutable()->MarkTeleported();
            TickP4World(*World, *Character);
        }
        else if (Definition.CaseId == TEXT("platform_release"))
        {
            const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
            if (Feet == nullptr || Feet->Left.LockAmount <= 1.0e-4f || Feet->Right.LockAmount <= 1.0e-4f)
            {
                bSampleReady = false;
            }
            else
            {
                PreviousFootLocks = CaptureP4FootLocks(*Feet);
                PreviousLeftFootLockAmount = PreviousFootLocks.LeftAmount;
                PreviousRightFootLockAmount = PreviousFootLocks.RightAmount;
                PreviousPlatformSnapshot = CaptureP4Platform(
                    Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary);
                Character->SetBase(static_cast<UPrimitiveComponent*>(nullptr));
                PrimaryActor->Destroy();
                TickP4World(*World, *Character);
                const FAlsFeetState* ReleasedFeet{GetAnimationState<FAlsFeetState>(
                    Character->GetTraceAnimationInstance(), TEXT("FeetState"))};
                bSampleReady = ReleasedFeet != nullptr && PreviousPlatformSnapshot.PlatformId >= 0 &&
                    PreviousPlatformSnapshot.ColliderId >= 0 &&
                    CaptureP4Platform(Character->GetMovementBase(), TracePlatformPrimary, TracePlatformSecondary).PlatformId < 0;
                LeftFootReleaseReason = TEXT("PlatformRemoved");
                RightFootReleaseReason = TEXT("PlatformRemoved");
            }
        }
    }
    else if (bSampleReady && Definition.Category == TEXT("Feet"))
    {
        TickP4World(*World, *Character);
    }

    if (!bSampleReady)
    {
        const UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstance()};
        const FAlsViewAnimationState* View{GetAnimationState<FAlsViewAnimationState>(Animation, TEXT("ViewState"))};
        const FAlsSpineState* Spine{GetAnimationState<FAlsSpineState>(Animation, TEXT("SpineState"))};
        const FAlsTurnInPlaceState* Turn{GetAnimationState<FAlsTurnInPlaceState>(Animation, TEXT("TurnInPlaceState"))};
        const FAlsFeetState* Feet{GetAnimationState<FAlsFeetState>(Animation, TEXT("FeetState"))};
        const FAlsRotateInPlaceState* Rotate{GetAnimationState<FAlsRotateInPlaceState>(
            Animation, TEXT("RotateInPlaceState"))};
        UE_LOG(LogTemp, Error,
            TEXT("P4 native sample did not reach required ALS state case=%s viewYaw=%.3f viewPitch=%.3f characterViewYaw=%.3f actorYaw=%.3f aiming=%d poseAiming=%.3f spineAllowed=%d spineAmount=%.3f spineYaw=%.3f rotateL=%d rotateR=%d rotationYawSpeed=%.6f turnDelay=%.3f turnRate=%.3f montage=%d feetValid=%d lockL=%.3f lockR=%.3f curveIkL=%.3f curveIkR=%.3f curveLockL=%.3f curveLockR=%.3f"),
            *Definition.CaseId, View != nullptr ? View->YawAngle : -999.0f,
            View != nullptr ? View->PitchAngle : -999.0f, Character->GetViewState().Rotation.Yaw,
            Character->GetActorRotation().Yaw, Character->GetRotationMode() == AlsRotationModeTags::Aiming,
            Animation->GetCurveValue(UAlsConstants::PoseAimingCurveName()),
            Spine != nullptr && Spine->bSpineRotationAllowed, Spine != nullptr ? Spine->SpineAmount : -1.0f,
            Spine != nullptr ? Spine->FinalYawAngle : -999.0f,
            Rotate != nullptr && Rotate->bRotatingLeft, Rotate != nullptr && Rotate->bRotatingRight,
            Animation->GetCurveValue(UAlsConstants::RotationYawSpeedCurveName()),
            Turn != nullptr ? Turn->ActivationDelay : -1.0f,
            Turn != nullptr ? Turn->PlayRate : -1.0f, Animation->GetCurrentActiveMontage() != nullptr,
            Feet != nullptr && Feet->bValid, Feet != nullptr ? Feet->Left.LockAmount : -1.0f,
            Feet != nullptr ? Feet->Right.LockAmount : -1.0f,
            Animation->GetCurveValue(UAlsConstants::FootLeftIkCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootRightIkCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootLeftLockCurveName()),
            Animation->GetCurveValue(UAlsConstants::FootRightLockCurveName()));
        return nullptr;
    }

    FP4StimulusCapture Stimulus;
    if (!CaptureP4Stimulus(*World, *Character, Definition, PreviousPlatformSnapshot, PreviousFootLocks,
        TracePlatformPrimary, TracePlatformSecondary, RuntimePlayer, TransitionFrameDelta, Stimulus))
    {
        return nullptr;
    }
    if (Definition.CaseId == TEXT("platform_base_change"))
    {
        if (Stimulus.PreviousPlatform.PlatformId < 0 || Stimulus.CurrentPlatform.PlatformId < 0 ||
            Stimulus.PreviousPlatform.PlatformId == Stimulus.CurrentPlatform.PlatformId ||
            Stimulus.PreviousPlatform.ColliderId == Stimulus.CurrentPlatform.ColliderId)
        {
            UE_LOG(LogTemp, Error,
                TEXT("P4 platform base change was not observed previous=%d/%lld current=%d/%lld"),
                Stimulus.PreviousPlatform.PlatformId, Stimulus.PreviousPlatform.ColliderId,
                Stimulus.CurrentPlatform.PlatformId, Stimulus.CurrentPlatform.ColliderId);
            return nullptr;
        }
        LeftFootReleaseReason = TEXT("BaseChanged");
        RightFootReleaseReason = TEXT("BaseChanged");
    }
    else if (Definition.CaseId == TEXT("platform_teleport"))
    {
        const float TranslationMeters{static_cast<float>(FVector::Distance(
            Stimulus.CurrentPlatform.Transform.GetLocation(), Stimulus.PreviousPlatform.Transform.GetLocation()) / 100.0f)};
        if (TranslationMeters <= 1.0f)
        {
            UE_LOG(LogTemp, Error, TEXT("P4 platform teleport transition was not observed distance=%.3f"), TranslationMeters);
            return nullptr;
        }
        LeftFootReleaseReason = TEXT("Teleported");
        RightFootReleaseReason = TEXT("Teleported");
    }
    FP4NativeRuntimeCapture NativeRuntime;
    if (!CaptureP4NativeRuntime(*World, *Character, PreviousPlatformSnapshot,
        TracePlatformPrimary, TracePlatformSecondary, NativeRuntime))
    {
        return nullptr;
    }
    const float AccumulatedYaw{static_cast<float>(-FMath::DegreesToRadians(FMath::FindDeltaAngleDegrees(
        InitialActorYaw, Character->GetActorRotation().Yaw)))};
    const TSharedPtr<FJsonObject> NativeActual{
        P4NativeActualObject(*Character, Definition, PreviousLeftFootLockAmount, PreviousRightFootLockAmount,
            PreviousFootLocks, TurnPhase, RotatePhase,
            Definition.Category == TEXT("Turn") ? AccumulatedYaw : 0.0f,
            Definition.Category == TEXT("Rotate") ? RotateAccumulatedYaw : 0.0f,
            LeftFootReleaseReason, RightFootReleaseReason, bSourceSelectionVerified,
            RuntimePlayer, TransitionFrameDelta, NativeRuntime)};
    if (!NativeActual.IsValid())
    {
        return nullptr;
    }

    const TSharedRef<FJsonObject> Object{MakeShared<FJsonObject>()};
    Object->SetStringField(TEXT("caseId"), Definition.CaseId);
    Object->SetStringField(TEXT("category"), Definition.Category);
    Object->SetStringField(TEXT("stance"), Definition.Stance);
    Object->SetStringField(TEXT("direction"), Definition.Direction);
    Object->SetStringField(TEXT("phase"), Definition.Phase);
    Object->SetStringField(TEXT("provenance"), TEXT("port_oracle_v1"));
    Object->SetObjectField(TEXT("source"), P4CaseSourceObject(Definition, SourceAnimationObjectPath));
    Object->SetObjectField(TEXT("stimulus"), P4StimulusObject(Stimulus));
    Object->SetObjectField(TEXT("nativeActual"), NativeActual.ToSharedRef());
    Object->SetObjectField(TEXT("portExpected"), EmptyP4PortExpected());
    return Object;
}

bool GenerateP4Documents(UWorld* World, const FString& OutputDirectory)
{
    if (!RunP4RuntimeSelectionSelfTest() || !RunP4CleanupSelfTest(*World))
    {
        UE_LOG(LogTemp, Error, TEXT("P4 commandlet self-test failed"));
        return false;
    }
    UClass* AimOverlayClass{LoadObject<UClass>(nullptr, P4AimOverlayClassPath)};
    if (!IsValid(AimOverlayClass) || AimOverlayClass->GetPathName() != P4AimOverlayClassPath)
    {
        UE_LOG(LogTemp, Error, TEXT("P4 required overlay animation class is unavailable or mismatched expected=%s actual=%s"),
            P4AimOverlayClassPath, IsValid(AimOverlayClass) ? *AimOverlayClass->GetPathName() : TEXT("<null>"));
        return false;
    }
    const TArray<FP4CaseDefinition> Definitions{CreateP4CaseDefinitions()};
    const TCHAR* Names[]{TEXT("aim"), TEXT("turn"), TEXT("rotate"), TEXT("feet"), TEXT("platform")};
    bool bSuccess{Definitions.Num() == 25};
    for (const TCHAR* Name : Names)
    {
        TArray<TSharedPtr<FJsonValue>> Cases;
        const FString Category{FString{Name}.Left(1).ToUpper() + FString{Name}.Mid(1)};
        for (const FP4CaseDefinition& Definition : Definitions)
        {
            if (Definition.Category == Category)
            {
                const TSharedPtr<FJsonObject> Case{GenerateP4Case(World, Definition, *AimOverlayClass)};
                if (!Case.IsValid())
                {
                    bSuccess = false;
                }
                else
                {
                    Cases.Add(MakeShared<FJsonValueObject>(Case));
                }
            }
        }

        const TSharedRef<FJsonObject> Document{MakeShared<FJsonObject>()};
        Document->SetNumberField(TEXT("schemaVersion"), 1);
        Document->SetStringField(TEXT("kind"), TEXT("p4_pose_trace"));
        Document->SetStringField(TEXT("name"), Name);
        Document->SetNumberField(TEXT("fixedDeltaSeconds"), FixedDeltaSeconds);
        Document->SetObjectField(TEXT("reference"), P4ReferenceObject());
        Document->SetObjectField(TEXT("coordinateSystem"), P4CoordinateSystemObject());
        Document->SetObjectField(TEXT("sources"), P4SourcesObject(*AimOverlayClass));
        Document->SetObjectField(TEXT("tolerances"), P4TolerancesObject());
        Document->SetStringField(TEXT("nativeProvenance"), TEXT("als_runtime"));
        Document->SetStringField(TEXT("portProvenance"), TEXT("port_oracle_v1"));
        Document->SetArrayField(TEXT("cases"), Cases);
        bSuccess &= SaveJson(OutputDirectory / FString::Printf(TEXT("trace_p4_%s.json"), Name), Document);
    }
    return bSuccess;
}

bool GenerateP5aDocuments(
    UWorld& World, const FString& OutputPath, const FString& TracePlanSha256,
    const TSharedPtr<FJsonObject>& Plan,
    const FP5aNativeTransitionStimulusContract& StimulusContract,
    const FP5aNativeReferenceAudit& NativeReferenceAudit,
    const FP5aNativeAuxiliaryInventory& Auxiliary, const bool bDiscovery = false)
{
    if (!FModuleManager::Get().LoadModule(TEXT("ALSCamera")))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A requires the ALSCamera runtime module before loading Roll assets."));
        return false;
    }
    TArray<FP5aSourceDefinition> Sources;
    TArray<FP5aCaseDefinition> Definitions;
    if (!ParseP5aCaseDefinitions(Plan, Sources, Definitions))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A case/source schedule parsing failed."));
        return false;
    }
    const TSharedPtr<FJsonObject>* Reference{nullptr};
    const TSharedPtr<FJsonObject>* Snapshot{nullptr};
    if (!Plan->TryGetObjectField(TEXT("reference"), Reference) || Reference == nullptr ||
        !Plan->TryGetObjectField(TEXT("snapshot"), Snapshot) || Snapshot == nullptr)
    {
        return false;
    }

    FP5aRawTrace Trace;
    Trace.TracePlanSha256 = TracePlanSha256;
    Trace.Reference = *Reference;
    Trace.Snapshot = *Snapshot;
    Trace.NativeReferenceAudit = NativeReferenceAudit;
    FP5aNativeDiscovery Discovery;
    if (!bDiscovery) Discovery.FormalAuxiliary = &Auxiliary;
    for (const FP5aCaseDefinition& Definition : Definitions)
    {
        FActorSpawnParameters SpawnParameters;
        SpawnParameters.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
        AAlsTraceCharacter* Character{World.SpawnActor<AAlsTraceCharacter>(AAlsTraceCharacter::StaticClass(),
            FVector{Definition.Ordinal * 500.0, 0.0, 92.0}, FRotator::ZeroRotator, SpawnParameters)};
        AAlsTraceController* Controller{World.SpawnActor<AAlsTraceController>()};
        if (IsValid(Character) && !Character->HasActorBegunPlay())
        {
            Character->DispatchBeginPlay();
        }
        if (IsValid(Character) && IsValid(Controller))
        {
            Controller->Possess(Character);
        }
        if (!IsValid(Character) || !IsValid(Controller) ||
            !IsValid(Character->GetTraceAnimationInstance()))
        {
            return false;
        }
        Character->GetMesh()->VisibilityBasedAnimTickOption =
            EVisibilityBasedAnimTickOption::AlwaysTickPoseAndRefreshBones;
        const FP5aFrameDefinition& InitialFrame{Definition.Frames[0]};
        const FGameplayTag InitialStance{InitialFrame.Stance == TEXT("Crouching")
            ? AlsStanceTags::Crouching : AlsStanceTags::Standing};
        const FGameplayTag InitialRotationMode{InitialFrame.RotationMode == TEXT("Aiming")
            ? AlsRotationModeTags::Aiming : (InitialFrame.RotationMode == TEXT("VelocityDirection")
                ? AlsRotationModeTags::VelocityDirection : AlsRotationModeTags::ViewDirection)};
        Character->ApplyTraceDesiredState(InitialRotationMode,
            InitialFrame.RotationMode == TEXT("Aiming"), InitialStance, AlsOverlayModeTags::Default);
        for (int32 Warmup{0}; Warmup < 60; ++Warmup)
        {
            TickP5aWorld(World, *Character);
            if (Warmup == 14 || Warmup == 59)
            {
                UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstanceMutable()};
                const void* Transitions{StimulusContract.TransitionsState->ContainerPtrToValuePtr<void>(Animation)};
                UE_LOG(LogTemp, Display, TEXT("P5A warmup case=%s updates=%d allowed=%d curve=%.9f"),
                    *Definition.CaseId, Warmup + 1,
                    StimulusContract.bTransitionsAllowed->GetPropertyValue_InContainer(Transitions),
                    Animation->GetCurveValue(UAlsConstants::AllowTransitionsCurveName()));
            }
        }

        FP5aRawCase RawCase;
        RawCase.Ordinal = Definition.Ordinal;
        RawCase.CaseId = Definition.CaseId;
        FP5aNativeActionLifecycle ActionLifecycle;
        TArray<TSharedPtr<FJsonValue>> DiagnosticFrames;
        TArray<TSharedPtr<FJsonValue>> DiagnosticCallbacks;
        TSharedPtr<FJsonObject> DiagnosticFrame;
        int32 DiagnosticFrameIndex{INDEX_NONE};
        const TCHAR* DiagnosticPhase{TEXT("update")};
        if (bDiscovery)
        {
            ActionLifecycle.DiagnosticCallback = [&](const TCHAR* Callback, const bool bInterrupted,
                                                      const FAnimMontageInstance* Instance)
            {
                const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
                Value->SetStringField(TEXT("callback"), Callback);
                Value->SetBoolField(TEXT("interrupted"), bInterrupted);
                Value->SetNumberField(TEXT("frameIndex"), DiagnosticFrameIndex);
                Value->SetNumberField(TEXT("engineFrameCounter"), static_cast<double>(GFrameCounter));
                Value->SetStringField(TEXT("phase"), DiagnosticPhase);
                Value->SetObjectField(TEXT("snapshot"), P5aDiscoveryMontageSnapshot(Instance, ActionLifecycle.Montage));
                DiagnosticCallbacks.Add(MakeShared<FJsonValueObject>(Value));
            };
            ActionLifecycle.DiagnosticAfterUpdate = [&]()
            {
                DiagnosticFrame->SetObjectField(TEXT("rollPostUpdateBeforeStart"), P5aDiscoveryMontageSnapshot(
                    Character->GetTraceAnimationInstanceMutable()->GetMontageInstanceForID(ActionLifecycle.InstanceId),
                    ActionLifecycle.Montage));
                DiagnosticPhase = TEXT("postUpdateStart");
            };
        }
        const TWeakObjectPtr<UAlsAnimationInstance> ObservedAnimation{Character->GetTraceAnimationInstanceMutable()};
        ON_SCOPE_EXIT
        {
            UnbindP5aNativeActionObservers(ObservedAnimation.Get(), ActionLifecycle);
        };
        FP5aSemanticGraphState SemanticGraphState;
        FP5aNativeNotifyObservationState NotifyObservationState;
        for (const FP5aFrameDefinition& Frame : Definition.Frames)
        {
            ActionLifecycle.bEndedThisFrame = false;
            if (bDiscovery)
            {
                DiagnosticFrameIndex = Frame.FrameIndex;
                DiagnosticPhase = Frame.ActionCommand == TEXT("Cancel") ? TEXT("preUpdateCancelOrUpdate") : TEXT("update");
                DiagnosticCallbacks.Reset();
                DiagnosticFrame = MakeShared<FJsonObject>();
                DiagnosticFrame->SetNumberField(TEXT("frameIndex"), Frame.FrameIndex);
                DiagnosticFrame->SetStringField(TEXT("actionCommand"), Frame.ActionCommand);
                DiagnosticFrame->SetObjectField(TEXT("rollBeforeCommandAndUpdate"), P5aDiscoveryMontageSnapshot(
                    Character->GetTraceAnimationInstanceMutable()->GetMontageInstanceForID(ActionLifecycle.InstanceId),
                    ActionLifecycle.Montage));
            }
            const FGameplayTag FrameStance{Frame.Stance == TEXT("Crouching")
                ? AlsStanceTags::Crouching : AlsStanceTags::Standing};
            const FGameplayTag FrameRotationMode{Frame.RotationMode == TEXT("Aiming")
                ? AlsRotationModeTags::Aiming : (Frame.RotationMode == TEXT("VelocityDirection")
                    ? AlsRotationModeTags::VelocityDirection : AlsRotationModeTags::ViewDirection)};
            Character->ApplyTraceDesiredState(FrameRotationMode,
                Frame.RotationMode == TEXT("Aiming"), FrameStance, AlsOverlayModeTags::Default);

            UAlsAnimationInstance* Animation{Character->GetTraceAnimationInstanceMutable()};
            FAnimMontageInstance* BeforeActionInstance{IsValid(ActionLifecycle.Montage)
                ? Animation->GetInstanceForMontage(ActionLifecycle.Montage) : nullptr};
            const float PreviousActionTime{BeforeActionInstance != nullptr
                ? BeforeActionInstance->GetPosition() : ActionLifecycle.LastPlaybackPosition};
            const bool bTransitionWasActive{Sources.ContainsByPredicate([Animation](const FP5aSourceDefinition& Source)
            {
                if (Source.SourceKind != TEXT("Transition")) return false;
                for (UAnimSequenceBase* Asset : Source.NativeAssets)
                {
                    if (Animation->DynamicMontage_IsPlayingFrom(Asset)) return true;
                }
                return false;
            })};
            FP5aNativeTransitionStimulusReceipt Receipt;
            FP5aFrameUpdateAudit FrameUpdateAudit;
            if (!GenerateP5aCase(World, Definition.Ordinal, *Character, Frame, StimulusContract,
                Frame.TransitionStimulus, bTransitionWasActive, Receipt, ActionLifecycle, FrameUpdateAudit))
            {
                UE_LOG(LogTemp, Error, TEXT("P5A frame generation failed case=%s frame=%d allowed=%d updated=%d delay=%d active=%d"),
                    *Definition.CaseId, Frame.FrameIndex, Receipt.ObservedAllowTransitions,
                    Receipt.PreHookUpdatedThisFrame, Receipt.PreHookFrameDelay, Receipt.PreHookTransitionActive);
                return false;
            }

            if (bDiscovery)
            {
                const TSharedRef<FJsonObject> Audit{MakeShared<FJsonObject>()};
                Audit->SetNumberField(TEXT("animationUpdates"), FrameUpdateAudit.AnimationUpdates);
                Audit->SetNumberField(TEXT("evaluations"), FrameUpdateAudit.Evaluations);
                Audit->SetNumberField(TEXT("postUpdates"), FrameUpdateAudit.PostUpdates);
                Audit->SetNumberField(TEXT("meshTicks"), FrameUpdateAudit.MeshTicks);
                DiagnosticFrame->SetObjectField(TEXT("frameUpdateAudit"), Audit);
                DiagnosticFrame->SetArrayField(TEXT("callbacks"), DiagnosticCallbacks);
                DiagnosticFrame->SetObjectField(TEXT("rollAfterUpdateAndCommand"), P5aDiscoveryMontageSnapshot(
                    Animation->GetMontageInstanceForID(ActionLifecycle.InstanceId), ActionLifecycle.Montage));
                if (!Discovery.ObserveRuntime(*Character, Sources, DiagnosticFrame.ToSharedRef()))
                {
                    UE_LOG(LogTemp, Error, TEXT("P5A native discovery observation failed case=%s frame=%d"),
                        *Definition.CaseId, Frame.FrameIndex);
                    return false;
                }
                DiagnosticFrames.Add(MakeShared<FJsonValueObject>(DiagnosticFrame.ToSharedRef()));
                ++Discovery.MeasuredFrameCount;
                continue;
            }

            const TSharedRef<FJsonObject> ClosureObservation{MakeShared<FJsonObject>()};
            if (!Discovery.ObserveRuntime(*Character, Sources, ClosureObservation))
            {
                UE_LOG(LogTemp, Error, TEXT("P5A formal runtime closure failed case=%s frame=%d"),
                    *Definition.CaseId, Frame.FrameIndex);
                return false;
            }

            const FP5aSourceDefinition* ActiveTransitionSource{nullptr};
            FAnimMontageInstance* ActiveTransitionInstance{nullptr};
            for (const FP5aSourceDefinition& Source : Sources)
            {
                if (Source.SourceKind != TEXT("Transition")) continue;
                for (UAnimSequenceBase* Asset : Source.NativeAssets)
                {
                    UAnimMontage* TransitionMontage{nullptr};
                    if (Animation->IsPlayingSlotAnimation(
                        Asset, UAlsConstants::TransitionSlotName(), TransitionMontage))
                    {
                        const FAnimMontageInstance* TransitionInstance{
                            Animation->GetActiveInstanceForMontage(TransitionMontage)};
                        check(TransitionInstance != nullptr);
                        ActiveTransitionSource = &Source;
                        ActiveTransitionInstance = Animation->GetActiveInstanceForMontage(TransitionMontage);
                        break;
                    }
                }
                if (ActiveTransitionInstance != nullptr) break;
            }
            UpdateP5aSemanticGraphState(SemanticGraphState, ActionLifecycle,
                PreviousActionTime, ActiveTransitionInstance != nullptr);
            const FP5aCanonicalAssetOracle CanonicalOracle{EvaluateP5aCanonicalAssetOracle(
                Frame, Definition.Ordinal, Sources, *Animation, ActionLifecycle, PreviousActionTime,
                ActiveTransitionSource, ActiveTransitionInstance,
                SemanticGraphState.ActionWeight, SemanticGraphState.ActionEventWeight,
                SemanticGraphState.TransitionWeight)};
            const bool bHasReceipt{Frame.TransitionStimulus.Left.LockAmount > 0.0f ||
                Frame.TransitionStimulus.Right.LockAmount > 0.0f};
            RawCase.Frames.Add(CollectP5aNativeRuntimeFrame(*Character, Frame, Sources,
                CanonicalOracle, bHasReceipt ? &Receipt : nullptr, StimulusContract,
                ActionLifecycle, PreviousActionTime, NotifyObservationState, FrameUpdateAudit, Auxiliary));
            if (!RawCase.Frames.Last().Value.IsValid())
            {
                UE_LOG(LogTemp, Error, TEXT("P5A runtime observation failed case=%s frame=%d"),
                    *Definition.CaseId, Frame.FrameIndex);
                return false;
            }
        }
        UnbindP5aNativeActionObservers(ObservedAnimation.Get(), ActionLifecycle);
        Controller->UnPossess();
        Controller->Destroy();
        Character->Destroy();
        if (bDiscovery)
        {
            const TSharedRef<FJsonObject> Case{MakeShared<FJsonObject>()};
            Case->SetStringField(TEXT("caseId"), Definition.CaseId);
            Case->SetNumberField(TEXT("ordinal"), Definition.Ordinal);
            Case->SetArrayField(TEXT("frames"), DiagnosticFrames);
            Discovery.Cases.Add(MakeShared<FJsonValueObject>(Case));
        }
        else Trace.Cases.Add(MoveTemp(RawCase));
    }
    if (bDiscovery) return Discovery.Save(OutputPath, TracePlanSha256);
    return Trace.Cases.Num() == 8 && SaveJson(OutputPath, P5aRawTraceToJson(Trace));
}
}

UAlsLocomotionTraceCommandlet::UAlsLocomotionTraceCommandlet()
{
    IsClient = false;
    IsEditor = true;
    IsServer = false;
    LogToConsole = true;
    ShowErrorCount = true;
}

int32 UAlsLocomotionTraceCommandlet::Main(const FString& Parameters)
{
    using namespace AlsLocomotionTrace;
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_MAIN_BEGIN"));
    const FString OutputDirectory{GetRequiredValue(Parameters, TEXT("Output="))};
    const FString ReferenceRoot{GetRequiredValue(Parameters, TEXT("ReferenceRoot="))};
    const FString Commit{GetRequiredValue(Parameters, TEXT("ReferenceCommit="))};
    const FString PatchHashes{GetRequiredValue(Parameters, TEXT("PatchHashes="))};
    const bool bReadyCheck{FParse::Param(*Parameters, TEXT("ReadyCheck"))};
    FString TraceKind;
    FParse::Value(*Parameters, TEXT("TraceKind="), TraceKind);
    const bool bP3{TraceKind.IsEmpty() || TraceKind == TEXT("P3")};
    const bool bP4{TraceKind == TEXT("P4")};
    const bool bP5aDiscovery{TraceKind == TEXT("P5ADiscovery")};
    const bool bP5a{TraceKind == TEXT("P5A") || bP5aDiscovery};
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_ARGUMENTS_OK ready=%d"), bReadyCheck ? 1 : 0);
    if (OutputDirectory.IsEmpty() || ReferenceRoot.IsEmpty() || Commit.IsEmpty() || PatchHashes.IsEmpty()) return 2;
    if (!bP3 && !bP4 && !bP5a) return 2;
    if (bP5aDiscovery && bReadyCheck) return 2;
    if (!IsLowercaseCommitSha(Commit) || Commit != LockedReferenceCommit ||
        !ValidateReference(ReferenceRoot, Commit, PatchHashes)) return 3;
    UE_LOG(LogTemp, Display, TEXT("P3_TRACE_REFERENCE_OK"));
    if (bP5a && !FModuleManager::Get().LoadModule(TEXT("ALSCamera")))
    {
        UE_LOG(LogTemp, Error, TEXT("P5A requires the ALSCamera runtime module before asset validation."));
        return 4;
    }
    if (!ValidateSequenceDefinitions() || !ValidateRequiredAssets()) return 4;

    TSharedPtr<FJsonObject> P5aTracePlan;
    FString P5aTracePlanSha256;
    FP5aNativeTransitionStimulusContract P5aStimulusContract;
    FP5aNativeReferenceAudit P5aNativeReferenceAudit;
    FP5aNativeAuxiliaryInventory P5aAuxiliary;
    if (bP5a)
    {
        const FString P5aTracePlanPath{GetRequiredValue(Parameters, TEXT("P5ATracePlan="))};
        P5aTracePlanSha256 = GetRequiredValue(Parameters, TEXT("P5ATracePlanSha256="));
        if (!ValidateP5aTracePlan(P5aTracePlanPath, P5aTracePlanSha256, P5aTracePlan) ||
            !ResolveP5aNativeTransitionStimulusContract(P5aStimulusContract) ||
            !CollectP5aNativeReferenceAudit(P5aNativeReferenceAudit))
        {
            return 4;
        }
        TArray<FP5aSourceDefinition> ValidatedSources;
        TArray<FP5aCaseDefinition> ValidatedCases;
        if (!P5aAuxiliary.ReadAndValidate(P5aTracePlan) ||
            !ParseP5aCaseDefinitions(P5aTracePlan, ValidatedSources, ValidatedCases) ||
            !P5aAuxiliary.IsDisjointFrom(ValidatedSources))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A auxiliary native inventory rejected."));
            return 4;
        }
        P5aNativeReferenceAudit.AuxiliaryAssets = P5aAuxiliary.AuditRows;
    }
    const FString OutputParent{bP5a ? FPaths::GetPath(OutputDirectory) : OutputDirectory};
    if (OutputParent.IsEmpty() || !IFileManager::Get().MakeDirectory(*OutputParent, true)) return 5;

    const FString ProbePath{OutputParent / TEXT(".write_probe")};
    if (!FFileHelper::SaveStringToFile(TEXT("ready"), *ProbePath) || !IFileManager::Get().Delete(*ProbePath)) return 6;
    if (bReadyCheck)
    {
        if (bP5a)
        {
            if (!RunP5aNativeAssetClosureSelfTest(P5aAuxiliary)) return 7;
            if (!RunP5aNativeCurveSemanticsSelfTest(P5aTracePlan)) return 7;
            FApp::SetUseFixedTimeStep(true);
            FApp::SetFixedDeltaTime(FixedDeltaSeconds);
            UWorld* ReadyWorld{CreateTraceWorld()};
            const bool bActionLifecycleReady{IsValid(ReadyWorld) &&
                RunP5aNativeActionLifecycleSelfTest(*ReadyWorld)};
            DestroyTraceWorld(ReadyWorld);
            if (!bActionLifecycleReady)
            {
                UE_LOG(LogTemp, Error, TEXT("P5A native Action lifecycle self-test failed."));
                return 7;
            }
            UE_LOG(LogTemp, Display, TEXT("P5A_TRACE_READY_OK cases=8 commit=%s"), *Commit);
        }
        else if (bP4) { UE_LOG(LogTemp, Display, TEXT("P4_TRACE_READY_OK")); }
        else { UE_LOG(LogTemp, Display, TEXT("P3_TRACE_READY_OK")); }
        return 0;
    }

    FApp::SetUseFixedTimeStep(true);
    FApp::SetFixedDeltaTime(FixedDeltaSeconds);
    UWorld* World{CreateTraceWorld()};
    if (!IsValid(World)) return 7;
    bool bSuccess{true};
    if (bP5a)
    {
        bSuccess = GenerateP5aDocuments(*World, OutputDirectory, P5aTracePlanSha256,
            P5aTracePlan, P5aStimulusContract, P5aNativeReferenceAudit, P5aAuxiliary, bP5aDiscovery);
    }
    else if (bP4)
    {
        bSuccess = GenerateP4Documents(World, OutputDirectory);
    }
    else
    {
        const TArray<FSequenceDefinition> Definitions{CreateSequenceDefinitions()};
        for (const FSequenceDefinition& Definition : Definitions)
        {
            bSuccess &= GenerateSequence(World, Definition, Commit, OutputDirectory);
        }

        if (bSuccess)
        {
            AAlsTraceCharacter* SettingsCharacter{World->SpawnActor<AAlsTraceCharacter>()};
            bSuccess = IsValid(SettingsCharacter) && SaveJson(OutputDirectory / TEXT("p3_locomotion_settings.json"),
                CreateSettingsDocument(Commit, *SettingsCharacter));
        }
    }
    DestroyTraceWorld(World);
    if (!bSuccess) return 8;
    if (bP5aDiscovery) { UE_LOG(LogTemp, Display, TEXT("P5A_NATIVE_DISCOVERY_COMPLETE cases=8 frames=374 commit=%s"), *Commit); }
    else if (bP5a) { UE_LOG(LogTemp, Display, TEXT("P5A_TRACE_GENERATION_OK cases=8 commit=%s"), *Commit); }
    else if (bP4) { UE_LOG(LogTemp, Display, TEXT("P4_TRACE_GENERATION_OK cases=25 commit=%s"), *Commit); }
    else { UE_LOG(LogTemp, Display, TEXT("P3_TRACE_GENERATION_OK sequences=5 commit=%s"), *Commit); }
    return 0;
}
