#include "AlsAnimationGraphLibrary.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "EdGraph/EdGraphNode.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_Root.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNodeSpaceConversions.h"
#include "Animation/Skeleton.h"
#include "AnimNodes/AnimNode_BlendSpaceEvaluator.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Editor.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Misc/EngineVersion.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/StructOnScope.h"
#include "Utility/AlsPrivateMemberAccessor.h"

ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsFullGraphUpdateMontage, &UAnimInstance::UpdateMontage,
    void (UAnimInstance::*)(float))
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsFullGraphSyncMontage, &UAnimInstance::UpdateMontageSyncGroup,
    void (UAnimInstance::*)())

namespace
{
// Legal pointer-to-member access to protected diagnostics on the real objects.
// No downcast to a fake derived instance and no changes to engine source/layout.
struct FProxyAccess : FAnimInstanceProxy
{
    static const TArray<FMontageEvaluationState>& Montages(const FAnimInstanceProxy& Proxy)
    { return (Proxy.*static_cast<const TArray<FMontageEvaluationState>&(FAnimInstanceProxy::*)() const>(&FProxyAccess::GetMontageEvaluationData))(); }
    static int32 Index(const FAnimInstanceProxy& Proxy, FName Name)
    { return (Proxy.*static_cast<int32(FAnimInstanceProxy::*)(FName) const>(&FProxyAccess::GetStateMachineIndex))(Name); }
    static void Pre(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta)
    { (Proxy.*&FProxyAccess::PreUpdate)(Instance, Delta); }
    static void Post(FAnimInstanceProxy& Proxy, UAnimInstance* Instance)
    { (Proxy.*&FProxyAccess::PostUpdate)(Instance); }
    static void UpdateRoot(FAnimInstanceProxy& Proxy)
    { (Proxy.*&FProxyAccess::UpdateAnimation)(); }
    static void EvaluateRoot(FAnimInstanceProxy& Proxy, FPoseContext& Pose)
    { (Proxy.*&FProxyAccess::EvaluateAnimation)(Pose); }
    static void Feedback(FAnimInstanceProxy& Proxy, const FPoseContext& Pose)
    {
        FAnimationEvaluationContext Context;
        Context.Curve.CopyFrom(Pose.Curve);
        (Proxy.*&FProxyAccess::UpdateCurvesToEvaluationContext)(Context);
    }
    static TMap<FName, float>& Curves(FAnimInstanceProxy& Proxy)
    { return (Proxy.*static_cast<TMap<FName, float>&(FAnimInstanceProxy::*)(EAnimCurveType)>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve); }
};
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance)
    { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
    static void TickMontages(UAnimInstance* Instance, float Delta)
    {
        FAlsFullGraphUpdateMontage::Access(Instance, Delta);
        FAlsFullGraphSyncMontage::Access(Instance);
        (Instance->*&FInstanceAccess::UpdateMontageEvaluationData)();
    }
    static void PreUpdate(UAnimInstance* Instance, float Delta)
    { (Instance->*&FInstanceAccess::PreUpdateAnimation)(Delta); }
};
struct FMachineAccess : FAnimNode_StateMachine
{
    static const TArray<FAnimationActiveTransitionEntry>& Edges(const FAnimNode_StateMachine& Node)
    { return Node.*&FMachineAccess::ActiveTransitionArray; }
    static const TArray<int32>& Updates(const FAnimNode_StateMachine& Node)
    { return Node.*&FMachineAccess::StatesUpdated; }
};
struct FBlendAccess : FAnimNode_BlendSpacePlayerBase
{
    static const TArray<FBlendSampleData>& Samples(const FAnimNode_BlendSpacePlayerBase& Node)
    { return Node.*&FBlendAccess::BlendSampleDataCache; }
};
struct FAimTraceRoot : FAnimNode_Base
{
    FPoseLink Aim;
    bool Relevant = true, Inactive = false;
    float Weight = 1;
    virtual void Initialize_AnyThread(const FAnimationInitializeContext& Context) override { Aim.Initialize(Context); }
    virtual void CacheBones_AnyThread(const FAnimationCacheBonesContext& Context) override { Aim.CacheBones(Context); }
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    {
        if (Relevant)
        {
            auto Child = Context.FractionalWeight(Weight);
            Aim.Update(Inactive ? Child.AsInactive() : Child);
        }
    }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    { if (Relevant) Aim.Evaluate(Output); else Output.ResetToRefPose(); }
};
bool SetNumber(UObject* Object, FName Name, double Value)
{
    FProperty* Property = Object->GetClass()->FindPropertyByName(Name);
    if (!Property) return false;
    void* Data = Property->ContainerPtrToValuePtr<void>(Object);
    FNumericProperty* Numeric = CastField<FNumericProperty>(Property);
    if (auto* Enum = CastField<FEnumProperty>(Property)) Numeric = Enum->GetUnderlyingProperty();
    if (!Numeric) return false;
    if (Numeric->IsFloatingPoint()) Numeric->SetFloatingPointPropertyValue(Data, Value);
    else Numeric->SetIntPropertyValue(Data, static_cast<int64>(Value));
    return true;
}
TArray<TSharedPtr<FJsonValue>> AimTraceNumbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}
void WritePose(FPoseContext& Output, const TSharedRef<FJsonObject>& Row)
{
    TArray<TSharedPtr<FJsonValue>> Poses;
    for (const FCompactPoseBoneIndex Bone : Output.Pose.ForEachBoneIndex())
    {
        const FTransform& T = Output.Pose[Bone]; const FVector P = T.GetTranslation(), S = T.GetScale3D(); const FQuat Q = T.GetRotation();
        const auto Value = MakeShared<FJsonObject>();
        Value->SetArrayField(TEXT("position"), AimTraceNumbers({P.X, P.Y, P.Z}));
        Value->SetArrayField(TEXT("rotation"), AimTraceNumbers({Q.X, Q.Y, Q.Z, Q.W}));
        Value->SetArrayField(TEXT("scale"), AimTraceNumbers({S.X, S.Y, S.Z}));
        Poses.Add(MakeShared<FJsonValueObject>(Value));
    }
    const auto Curves = MakeShared<FJsonObject>();
    Output.Curve.ForEachElement([&](const auto& Curve) { Curves->SetNumberField(Curve.Name.ToString(), Curve.Value); });
    Row->SetArrayField(TEXT("pose"), Poses); Row->SetObjectField(TEXT("curves"), Curves);
}

// Installed only during the one authored evaluation, after native Update.
// Forward the same context directly to the original node; never reevaluate a
// cached source or introduce another initialization/update traversal.
struct FFullGraphStageProbe : FAnimNode_Base
{
    FPoseLink* Link = nullptr;
    FAnimNode_Base* Original = nullptr;
    FString Name;
    TSharedPtr<FJsonObject> Captured;
    int32 Evaluations = 0;
    void Install()
    {
        Original = Link->GetLinkNode(); check(Original);
        Captured.Reset(); Evaluations = 0; Link->SetLinkNode(this);
    }
    void Restore() { Link->SetLinkNode(Original); }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        Original->Evaluate_AnyThread(Output);
        Captured = MakeShared<FJsonObject>(); WritePose(Output, Captured.ToSharedRef());
        Captured->SetNumberField(TEXT("evaluations"), ++Evaluations);
    }
};
}

namespace
{
// Only value properties may be supplied; node links, assets and object references
// are never replaced. Struct member keys use the exact reflected UE field names.
bool SetFullGraphValue(FProperty* Property, void* Container, const TSharedPtr<FJsonValue>& Value)
{
    if (!Property || !Value || Property->ArrayDim != 1) return false;
    void* Data = Property->ContainerPtrToValuePtr<void>(Container);
    if (auto* Boolean = CastField<FBoolProperty>(Property))
    {
        if (Value->Type != EJson::Boolean) return false;
        Boolean->SetPropertyValue(Data, Value->AsBool()); return true;
    }
    FNumericProperty* Numeric = CastField<FNumericProperty>(Property);
    UEnum* Enum = nullptr;
    if (auto* Byte = CastField<FByteProperty>(Property)) Enum = Byte->Enum;
    if (auto* EnumProperty = CastField<FEnumProperty>(Property))
    { Enum = EnumProperty->GetEnum(); Numeric = EnumProperty->GetUnderlyingProperty(); }
    if (Numeric)
    {
        if (Value->Type != EJson::Number || !FMath::IsFinite(Value->AsNumber())) return false;
        const double Number = Value->AsNumber();
        if (Numeric->IsFloatingPoint()) Numeric->SetFloatingPointPropertyValue(Data, Number);
        else
        {
            if (Number < MIN_int32 || Number > MAX_int32 || Number != FMath::FloorToDouble(Number)) return false;
            const int64 Integer = static_cast<int64>(Number);
            if (Enum && !Enum->IsValidEnumValue(Integer)) return false;
            Numeric->SetIntPropertyValue(Data, Integer);
        }
        return true;
    }
    auto* Struct = CastField<FStructProperty>(Property);
    if (!Struct || Struct->Struct->IsChildOf(FAnimNode_Base::StaticStruct()) || Value->Type != EJson::Object) return false;
    for (const auto& Pair : Value->AsObject()->Values)
        if (!SetFullGraphValue(FindFProperty<FProperty>(Struct->Struct, *Pair.Key), Data, Pair.Value)) return false;
    return true;
}
}

bool UAlsAnimationGraphLibrary::ExportFullGraphTrace(const FString& RequestPath, const FString& OutputPath)
{
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || RequestPath == OutputPath) return false;
    FString RequestText; TSharedPtr<FJsonObject> Request;
    if (!FFileHelper::LoadFileToString(RequestText, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestText), Request) ||
        Request->GetIntegerField(TEXT("schemaVersion")) != 1) return false;
    auto* Blueprint = LoadObject<UAnimBlueprint>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    const auto* Class = Blueprint && Blueprint->GeneratedClass ? IAnimClassInterface::GetFromClass(Blueprint->GeneratedClass) : nullptr;
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!Class || !Mesh || !Mesh->GetSkeleton() || !World || Class->GetAnimBlueprintFunctions().IsEmpty()) return false;
    bool RunIdleControls = false; Request->TryGetBoolField(TEXT("runIdleControls"), RunIdleControls);
    FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
    Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    UClass* OwnerClass = RunIdleControls ? LoadClass<AActor>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP_C")) : AActor::StaticClass();
    if (!OwnerClass) return false;
    AActor* Owner = World->SpawnActor<AActor>(OwnerClass, Spawn); if (!Owner) return false;
    Owner->SetActorTickEnabled(false);
    ON_SCOPE_EXIT { World->DestroyActor(Owner); };
    TArray<FBoneIndexType> Required; TArray<TSharedPtr<FJsonValue>> Names, Traces;
    const auto& Reference = Mesh->GetSkeleton()->GetReferenceSkeleton();
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
    { Required.Add(static_cast<FBoneIndexType>(Bone)); Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Bone).ToString())); }
    if (Required.Num() != 79) return false;
    bool CaptureStages = false; Request->TryGetBoolField(TEXT("captureStages"), CaptureStages);
    bool CaptureUpperStages = false; Request->TryGetBoolField(TEXT("captureUpperStages"), CaptureUpperStages);
    if (CaptureUpperStages && !CaptureStages) return false;
    bool DispatchStop = false; Request->TryGetBoolField(TEXT("dispatchStopNotifies"), DispatchStop);
    if (RunIdleControls && !DispatchStop) return false;
    int32 Total = 0;
    for (const auto& TraceValue : Request->GetArrayField(TEXT("traces")))
    {
        const auto TraceInput = TraceValue->AsObject();
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner)); Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get(), Blueprint->GeneratedClass));
        Instance->InitializeAnimation(true);
        if (RunIdleControls)
        {
            auto* Character = FindFProperty<FObjectPropertyBase>(Instance->GetClass(), TEXT("Character"));
            if (!Character || !Owner->IsA(Character->PropertyClass) || !Owner->GetRootComponent()) return false;
            Character->SetObjectPropertyValue_InContainer(Instance.Get(), Owner);
        }
        if (DispatchStop) Instance->RootMotionMode = ERootMotionMode::NoRootMotionExtraction;
        auto& Proxy = FInstanceAccess::Proxy(Instance.Get()); auto& Bones = Proxy.GetRequiredBones();
        Bones.InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Mesh->GetSkeleton());
        Bones.SetUseRAWData(true); Bones.SetUseSourceData(false); Bones.SetDisableRetargeting(false);
        auto* RootProperty = CastField<FStructProperty>(Class->GetAnimBlueprintFunctions()[0].OutputPoseNodeProperty);
        if (!RootProperty || RootProperty->Struct != FAnimNode_Root::StaticStruct() ||
            RootProperty->ContainerPtrToValuePtr<FAnimNode_Root>(Instance.Get()) != Proxy.GetRootNode()) return false;
        // The optional probes wrap evaluation only, with original nodes/contexts.
        TArray<TUniquePtr<FFullGraphStageProbe>> StageProbes;
        if (CaptureStages)
        {
            auto* Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
            for (const bool Raw : {true, false})
            {
                const FString Path = Blueprint->GetPathName() + (Raw ?
                    TEXT(":BaseLayer.AnimGraphNode_Inertialization_0") : TEXT(":BaseLayer.AnimGraphNode_Root_0"));
                auto* EditorNode = FindObject<UEdGraphNode>(nullptr, *Path);
                if (!Generated || !EditorNode) return false;
                const int32 Index = Generated->GetNodeIndexFromGuid(EditorNode->NodeGuid);
                if (!Class->GetAnimNodeProperties().IsValidIndex(Index)) return false;
                auto* Property = Class->GetAnimNodeProperties()[Index];
                auto Probe = MakeUnique<FFullGraphStageProbe>();
                if (Raw && Property->Struct == FAnimNode_Inertialization::StaticStruct())
                    Probe->Link = &Property->ContainerPtrToValuePtr<FAnimNode_Inertialization>(Instance.Get())->Source;
                else if (!Raw && Property->Struct == FAnimNode_Root::StaticStruct())
                    Probe->Link = &Property->ContainerPtrToValuePtr<FAnimNode_Root>(Instance.Get())->Result;
                else return false;
                Probe->Name = Raw ? TEXT("MainMovement") : TEXT("BaseLayer");
                StageProbes.Add(MoveTemp(Probe));
            }
            if (CaptureUpperStages)
                for (const bool PostAim : {false, true})
                {
                    const FString Path = Blueprint->GetPathName() + (PostAim ?
                        TEXT(":AnimGraph.AnimGraphNode_LocalToComponentSpace_1") :
                        TEXT(":AnimGraph.AnimGraphNode_SaveCachedPose_16"));
                    auto* EditorNode = FindObject<UEdGraphNode>(nullptr, *Path);
                    if (!Generated || !EditorNode) return false;
                    const int32 Index = Generated->GetNodeIndexFromGuid(EditorNode->NodeGuid);
                    if (!Class->GetAnimNodeProperties().IsValidIndex(Index)) return false;
                    auto* Property = Class->GetAnimNodeProperties()[Index];
                    auto Probe = MakeUnique<FFullGraphStageProbe>();
                    if (PostAim && Property->Struct == FAnimNode_ConvertLocalToComponentSpace::StaticStruct())
                        Probe->Link = &Property->ContainerPtrToValuePtr<FAnimNode_ConvertLocalToComponentSpace>(Instance.Get())->LocalPose;
                    else if (!PostAim && Property->Struct == FAnimNode_SaveCachedPose::StaticStruct())
                        Probe->Link = &Property->ContainerPtrToValuePtr<FAnimNode_SaveCachedPose>(Instance.Get())->Pose;
                    else return false;
                    Probe->Name = PostAim ? TEXT("PostAim") : TEXT("PostLayering");
                    StageProbes.Add(MoveTemp(Probe));
                }
        }
        TArray<TSharedPtr<FJsonValue>> Rows; int32 Serial = 0;
        bool MovingBranchClosed = false;
        auto SetIdle = [&](const TCHAR* Name, const TSharedPtr<FJsonValue>& Value)
        { return SetFullGraphValue(FindFProperty<FProperty>(Instance->GetClass(), Name), Instance.Get(), Value); };
        auto CallCheck = [&](const TCHAR* Name, bool* ReturnValue = nullptr)
        {
            UFunction* Function = Instance->FindFunction(FName(Name));
            if (!Function || Function->NumParms != (ReturnValue ? 1 : 0)) return false;
            FStructOnScope Parameters(Function);
            auto* Return = ReturnValue ? FindFProperty<FBoolProperty>(Function, TEXT("ReturnValue")) : nullptr;
            if (ReturnValue && (!Return || !Return->HasAnyPropertyFlags(CPF_ReturnParm))) return false;
            Instance->ProcessEvent(Function, Parameters.GetStructMemory());
            if (ReturnValue) *ReturnValue = Return->GetPropertyValue_InContainer(Parameters.GetStructMemory());
            return true;
        };
        for (const auto& FrameValue : TraceInput->GetArrayField(TEXT("frames")))
        {
            const FMemMark Mark(FMemStack::Get());
            const TGuardValue<uint64> GlobalFrame(GFrameCounter, static_cast<uint64>(300000 + ++Serial));
            const auto Input = FrameValue->AsObject(); const double Delta = Input->GetNumberField(TEXT("delta"));
            if (!FMath::IsFinite(Delta) || Delta < 0 || Delta > 1) return false;
            const auto Properties = Input->GetObjectField(TEXT("properties"));
            if (RunIdleControls)
                for (const TCHAR* Name : {TEXT("Rotate_L"), TEXT("Rotate_R"), TEXT("RotateRate"), TEXT("RotationScale"), TEXT("ElapsedDelayTime")})
                    if (Properties->HasField(Name)) return false; // Preserve original instance history.
            for (const auto& Pair : Properties->Values)
                if (!SetFullGraphValue(FindFProperty<FProperty>(Instance->GetClass(), *Pair.Key), Instance.Get(), Pair.Value))
                { UE_LOG(LogTemp, Error, TEXT("ALS_FULL_GRAPH_BAD_PROPERTY %s frame=%d"), *Pair.Key, Serial); return false; }
            const auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("input"), Input);
            Row->SetNumberField(TEXT("serial"), Serial);
            const auto PreviousCurves = MakeShared<FJsonObject>();
            for (const auto& Pair : FProxyAccess::Curves(Proxy)) PreviousCurves->SetNumberField(Pair.Key.ToString(), Pair.Value);
            Row->SetObjectField(TEXT("previousCurves"), PreviousCurves);
            // Use the instance boundary as well: it resets native notification
            // dispatch guards and queued events before the proxy's PreUpdate.
            if (DispatchStop) FInstanceAccess::PreUpdate(Instance.Get(), static_cast<float>(Delta));
            else FProxyAccess::Pre(Proxy, Instance.Get(), static_cast<float>(Delta));
            // UAnimInstance::UpdateAnimation freezes montage evaluation before
            // Native/BlueprintUpdateAnimation. New Turn requests affect the next
            // frozen evaluation, including the outgoing Stop blend.
            if (DispatchStop) FInstanceAccess::TickMontages(Instance.Get(), static_cast<float>(Delta));
            if (RunIdleControls)
            {
                const double Yaw = Input->GetNumberField(TEXT("characterYawDegrees"));
                if (!FMath::IsFinite(Yaw)) return false;
                Owner->SetActorRotation(FRotator(0, Yaw, 0), ETeleportType::TeleportPhysics);
                // Only the authored Grounded idle branch is run here. Movement
                // input formulas, DynamicTransitionCheck and physics stay outside
                // this probe. The DoOnce moving reset survives air/re-entry.
                if (Properties->GetIntegerField(TEXT("MovementState")) == 1)
                {
                    if (Properties->GetBoolField(TEXT("ShouldMove")))
                    {
                        if (!MovingBranchClosed &&
                            (!SetIdle(TEXT("ElapsedDelayTime"), MakeShared<FJsonValueNumber>(0)) ||
                             !SetIdle(TEXT("Rotate_L"), MakeShared<FJsonValueBoolean>(false)) ||
                             !SetIdle(TEXT("Rotate_R"), MakeShared<FJsonValueBoolean>(false)))) return false;
                        MovingBranchClosed = true;
                    }
                    else
                    {
                        MovingBranchClosed = false;
                        bool CanRotate = false, CanTurn = false;
                        if (!CallCheck(TEXT("CanRotateInPlace"), &CanRotate)) return false;
                        if (CanRotate) { if (!CallCheck(TEXT("RotateInPlaceCheck"))) return false; }
                        else if (!SetIdle(TEXT("Rotate_L"), MakeShared<FJsonValueBoolean>(false)) ||
                                 !SetIdle(TEXT("Rotate_R"), MakeShared<FJsonValueBoolean>(false))) return false;
                        if (!CallCheck(TEXT("CanTurnInPlace"), &CanTurn)) return false;
                        if (CanTurn) { if (!CallCheck(TEXT("TurnInPlaceCheck"))) return false; }
                        else if (!SetIdle(TEXT("ElapsedDelayTime"), MakeShared<FJsonValueNumber>(0))) return false;
                    }
                }
                const auto Idle = MakeShared<FJsonObject>();
                for (const TCHAR* Name : {TEXT("Rotate_L"), TEXT("Rotate_R")})
                {
                    auto* Field = FindFProperty<FBoolProperty>(Instance->GetClass(), Name); if (!Field) return false;
                    Idle->SetBoolField(Name, Field->GetPropertyValue_InContainer(Instance.Get()));
                }
                for (const TCHAR* Name : {TEXT("RotateRate"), TEXT("RotationScale"), TEXT("ElapsedDelayTime")})
                {
                    auto* Field = FindFProperty<FNumericProperty>(Instance->GetClass(), Name);
                    if (!Field || !Field->IsFloatingPoint()) return false;
                    Idle->SetNumberField(Name, Field->GetFloatingPointPropertyValue(Field->ContainerPtrToValuePtr<void>(Instance.Get())));
                }
                Row->SetObjectField(TEXT("idleControl"), Idle);
            }
            FProxyAccess::UpdateRoot(Proxy);
            if (DispatchStop) Instance->PostUpdateAnimation();
            else
            {
                Proxy.FlipBufferWriteIndex();
                Instance->NotifyQueue.AnimNotifies.Reset(); Instance->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
                FProxyAccess::Post(Proxy, Instance.Get());
            }
            if (DispatchStop)
            {
                TArray<TSharedPtr<FJsonValue>> Evaluations;
                for (const auto& E : FProxyAccess::Montages(Proxy))
                {
                    const auto* Montage = E.Montage.Get();
                    if (!Montage || Montage->SlotAnimTracks.Num() != 1 ||
                        Montage->SlotAnimTracks[0].AnimTrack.AnimSegments.Num() != 1) return false;
                    const auto& Track = Montage->SlotAnimTracks[0];
                    const UAnimSequenceBase* Asset = Track.AnimTrack.AnimSegments[0].GetAnimReference();
                    if (!Asset) return false;
                    auto Item = MakeShared<FJsonObject>();
                    Item->SetStringField(TEXT("asset"), Asset->GetPathName());
                    Item->SetStringField(TEXT("slot"), Track.SlotName.ToString());
                    Item->SetNumberField(TEXT("position"), E.MontagePosition);
                    Item->SetNumberField(TEXT("weight"), E.BlendInfo.GetBlendedValue());
                    Evaluations.Add(MakeShared<FJsonValueObject>(Item));
                }
                Row->SetArrayField(TEXT("montageEvaluations"), Evaluations);
            }
            TArray<TSharedPtr<FJsonValue>> Machines, Players;
            for (auto* Property : Class->GetAnimNodeProperties())
            {
                if (Property->Struct->IsChildOf(FAnimNode_StateMachine::StaticStruct()))
                {
                    auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Instance.Get());
                    const auto& Definition = Class->GetBakedStateMachines()[Node->StateMachineIndexInClass];
                    const auto Item = MakeShared<FJsonObject>();
                    Item->SetStringField(TEXT("name"), Definition.MachineName.ToString());
                    Item->SetNumberField(TEXT("index"), Node->StateMachineIndexInClass);
                    Item->SetNumberField(TEXT("current"), Node->GetCurrentState());
                    Item->SetNumberField(TEXT("elapsed"), Node->GetCurrentStateElapsedTime());
                    TArray<TSharedPtr<FJsonValue>> Weights, Recorded, Edges;
                    for (int32 State = 0; State < Definition.States.Num(); ++State)
                    {
                        Weights.Add(MakeShared<FJsonValueNumber>(Node->GetCurrentState() >= 0 ? Node->GetStateWeight(State) : 0));
                        Recorded.Add(MakeShared<FJsonValueNumber>(Proxy.GetRecordedStateWeight(Node->StateMachineIndexInClass, State)));
                    }
                    for (const auto& Edge : FMachineAccess::Edges(*Node))
                    {
                        const auto Data = MakeShared<FJsonObject>();
                        Data->SetNumberField(TEXT("from"), Edge.PreviousState); Data->SetNumberField(TEXT("to"), Edge.NextState);
                        Data->SetNumberField(TEXT("duration"), Edge.CrossfadeDuration); Data->SetNumberField(TEXT("elapsed"), Edge.ElapsedTime);
                        Data->SetNumberField(TEXT("alpha"), Edge.Alpha); Edges.Add(MakeShared<FJsonValueObject>(Data));
                    }
                    Item->SetArrayField(TEXT("weights"), Weights); Item->SetArrayField(TEXT("recorded"), Recorded);
                    Item->SetArrayField(TEXT("transitions"), Edges); Machines.Add(MakeShared<FJsonValueObject>(Item));
                }
                else if (Property->Struct->IsChildOf(FAnimNode_AssetPlayerBase::StaticStruct()))
                {
                    auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_AssetPlayerBase>(Instance.Get());
                    const auto Item = MakeShared<FJsonObject>(); Item->SetStringField(TEXT("node"), Property->GetName());
                    Item->SetStringField(TEXT("type"), Property->Struct->GetName());
                    Item->SetNumberField(TEXT("weight"), Node->GetCachedBlendWeight());
                    Item->SetNumberField(TEXT("time"), Node->GetAccumulatedTime());
                    Item->SetStringField(TEXT("group"), Node->GetGroupName().ToString());
                    if (Property->Struct->IsChildOf(FAnimNode_BlendSpacePlayerBase::StaticStruct()))
                    {
                        const auto* Blend = Property->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayerBase>(Instance.Get());
                        TArray<TSharedPtr<FJsonValue>> Samples;
                        for (const auto& Sample : FBlendAccess::Samples(*Blend))
                        {
                            const auto Data = MakeShared<FJsonObject>();
                            Data->SetNumberField(TEXT("index"), Sample.SampleDataIndex);
                            Data->SetNumberField(TEXT("weight"), Sample.TotalWeight);
                            Data->SetNumberField(TEXT("seconds"), Sample.Time);
                            Samples.Add(MakeShared<FJsonValueObject>(Data));
                        }
                        Item->SetArrayField(TEXT("samples"), Samples);
                    }
                    Players.Add(MakeShared<FJsonValueObject>(Item));
                }
            }
            Row->SetArrayField(TEXT("machines"), Machines); Row->SetArrayField(TEXT("players"), Players);
            FPoseContext Pose(&Proxy, true); FBlendedHeapCurve Curve;
            UE::Anim::FHeapAttributeContainer Attributes;
            FParallelEvaluationData Evaluation{Curve, Pose.Pose, Attributes};
            // The exported instance entry owns the native cached-pose scope.
            // Calling Proxy.EvaluateAnimation directly is invalid for SaveCachedPose.
            Instance->PreEvaluateAnimation();
            {
                for (auto& Probe : StageProbes) Probe->Install();
                ON_SCOPE_EXIT { for (auto& Probe : StageProbes) Probe->Restore(); };
                Instance->ParallelEvaluateAnimation(false, Mesh, Evaluation);
            }
            Pose.Curve.CopyFrom(Curve); WritePose(Pose, Row);
            if (CaptureStages)
            {
                const auto Stages = MakeShared<FJsonObject>();
                for (const auto& Probe : StageProbes)
                {
                    if (!Probe->Captured || Probe->Evaluations != 1) return false;
                    Stages->SetObjectField(Probe->Name, Probe->Captured);
                }
                Row->SetObjectField(TEXT("stages"), Stages);
            }
            // Use the real final-curve publication API, including absence/removal.
            FProxyAccess::Feedback(Proxy, Pose);
            if (DispatchStop)
            {
                // Execute the original authored Blueprint Stop consumers through
                // native notify dispatch, not copied playback parameters or a
                // command schedule injected from the Godot run. Other gameplay
                // callbacks are outside this controlled-scene probe.
                Instance->NotifyQueue.AnimNotifies.RemoveAll([](const FAnimNotifyEventReference& Event)
                {
                    const auto* Notify = Event.GetNotify();
                    return !Notify || Notify->Notify || Notify->NotifyStateClass ||
                        (Notify->NotifyName != FName(TEXT("->N Stop L")) && Notify->NotifyName != FName(TEXT("->N Stop R")));
                });
                TArray<TSharedPtr<FJsonValue>> Dispatched;
                for (const auto& Event : Instance->NotifyQueue.AnimNotifies)
                {
                    const auto* Notify = Event.GetNotify();
                    if (!Instance->FindFunction(Notify->GetNotifyEventName(Event.GetMirrorDataTable()))) return false;
                    Dispatched.Add(MakeShared<FJsonValueString>(Notify->NotifyName.ToString()));
                }
                Row->SetArrayField(TEXT("stopNotifies"), Dispatched);
                Instance->DispatchQueuedAnimEvents();
            }
            Rows.Add(MakeShared<FJsonValueObject>(Row)); ++Total;
        }
        const auto Trace = MakeShared<FJsonObject>(); Trace->SetStringField(TEXT("name"), TraceInput->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"), Rows); Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("source"), Blueprint->GetPathName()); Result->SetStringField(TEXT("mesh"), Mesh->GetPathName());
    Result->SetStringField(TEXT("engine"), FEngineVersion::Current().ToString());
    Result->SetStringField(TEXT("requestDigest"), Request->GetStringField(TEXT("requestDigest")));
    Result->SetBoolField(TEXT("dispatchStopNotifies"), DispatchStop);
    Result->SetBoolField(TEXT("runIdleControls"), RunIdleControls);
    Result->SetBoolField(TEXT("captureUpperStages"), CaptureUpperStages);
    Result->SetStringField(TEXT("scope"), RunIdleControls ?
        TEXT("Whole V4 AnimGraph with original CanRotate/CanTurn, Rotate/TurnInPlaceCheck and TurnInPlace functions, continuous native montage history and Stop notify consumers. Controlled global properties and actor yaw; RAW UE cm. Excludes Character motor, full Blueprint UpdateGraph, DynamicTransitionCheck, other gameplay callbacks and physics.") : DispatchStop ?
        TEXT("Whole V4 AnimGraph with continuous native montage history and original Stop notify consumers; controlled properties; RAW local pose, UE cm. Excludes Character motor, Blueprint UpdateGraph, other gameplay callbacks and physics.") :
        TEXT("Unmodified whole V4 AnimGraph; controlled properties; RAW retargeted local pose, UE cm; native final curve feedback. Excludes Character motor, Blueprint UpdateGraph, notify gameplay, physics and montage commands."));
    Result->SetArrayField(TEXT("names"), Names); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json;
    if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_FULL_GRAPH_NATIVE_OK traces=%d frames=%d bones=79 assets_saved=0"), Traces.Num(), Total);
    return true;
}

bool UAlsAnimationGraphLibrary::ExportAimStateTrace(const FString& RequestPath, const FString& OutputPath)
{
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || RequestPath == OutputPath) return false;
    FString RequestText; TSharedPtr<FJsonObject> Request;
    if (!FFileHelper::LoadFileToString(RequestText, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestText), Request) ||
        Request->GetIntegerField(TEXT("schemaVersion")) != 1) return false;
    auto* Blueprint = LoadObject<UAnimBlueprint>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    if (!Blueprint || !Blueprint->GeneratedClass || !Mesh || !Mesh->GetSkeleton()) return false;
    const auto* Class = IAnimClassInterface::GetFromClass(Blueprint->GeneratedClass);
    if (!Class) return false;
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!World) return false;
    FActorSpawnParameters Spawn;
    Spawn.ObjectFlags |= RF_Transient;
    Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AActor* Owner = World->SpawnActor<AActor>(Spawn);
    if (!Owner) return false;
    ON_SCOPE_EXIT { World->DestroyActor(Owner); };
    const FReferenceSkeleton& Reference = Mesh->GetSkeleton()->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required; TArray<TSharedPtr<FJsonValue>> Names, Traces;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
    { Required.Add(static_cast<FBoneIndexType>(Bone)); Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Bone).ToString())); }
    if (Required.Num() != 79) return false;
    const TCHAR* MachineNames[] = {TEXT("Aim Offset Behavior States"), TEXT("Look Towards Input States"), TEXT("Look Towards Camera States")};
    int32 TotalFrames = 0, PoseFrames = 0;
    for (const auto& TraceValue : Request->GetArrayField(TEXT("traces")))
    {
        const auto TraceInput = TraceValue->AsObject();
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner));
        Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get(), Blueprint->GeneratedClass));
        Instance->InitializeAnimation(true);
        auto& Proxy = FInstanceAccess::Proxy(Instance.Get());
        auto& Bones = Proxy.GetRequiredBones();
        Bones.InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Mesh->GetSkeleton());
        Bones.SetUseRAWData(true); Bones.SetUseSourceData(false); Bones.SetDisableRetargeting(false);
        FAnimNode_StateMachine* Machines[3]; int32 MachineIndices[3];
        TArray<int32> EvaluatorIndices; TArray<FAnimNode_AssetPlayerBase*> Evaluators;
        for (int32 M = 0; M < 3; ++M)
        {
            MachineIndices[M] = FProxyAccess::Index(Proxy, MachineNames[M]);
            Machines[M] = Proxy.GetMutableNodeFromIndex<FAnimNode_StateMachine>(MachineIndices[M]);
            if (!Machines[M]) return false;
            if (M == 0) continue;
            const auto& Definition = Class->GetBakedStateMachines()[Machines[M]->StateMachineIndexInClass];
            for (const auto& State : Definition.States)
            {
                if (State.PlayerNodeIndices.Num() != 1) return false;
                int32 Index = State.PlayerNodeIndices[0];
                auto* Player = Proxy.GetMutableNodeFromIndex<FAnimNode_AssetPlayerBase>(Index);
                if (!Player) return false;
                EvaluatorIndices.Add(Index); Evaluators.Add(Player);
            }
        }
        if (Evaluators.Num() != 7) return false;
        FAimTraceRoot Root; Root.Aim.SetLinkNode(Machines[0]);
        // Isolate only the transient instance's outer route. The original Aim
        // nodes/rules are untouched; normal proxy entry points own traversal
        // counters, deferred initialization, cache bones and native sync ticks.
        if (Class->GetAnimBlueprintFunctions().IsEmpty()) return false;
        auto* RootProperty = CastField<FStructProperty>(Class->GetAnimBlueprintFunctions()[0].OutputPoseNodeProperty);
        if (!RootProperty || RootProperty->Struct != FAnimNode_Root::StaticStruct()) return false;
        auto* NativeRoot = RootProperty->ContainerPtrToValuePtr<FAnimNode_Root>(Instance.Get());
        if (NativeRoot != Proxy.GetRootNode()) return false;
        const FPoseLink SavedLink = NativeRoot->Result;
        ON_SCOPE_EXIT { NativeRoot->Result = SavedLink; };
        NativeRoot->Result = FPoseLink();
        NativeRoot->Result.SetLinkNode(&Root);
        TArray<TSharedPtr<FJsonValue>> Rows;
        int32 Serial = 0;
        for (const auto& FrameValue : TraceInput->GetArrayField(TEXT("frames")))
        {
            const FMemMark Mark(FMemStack::Get());
            const TGuardValue<uint64> GlobalFrame(GFrameCounter, static_cast<uint64>(100000 + ++Serial));
            const auto Input = FrameValue->AsObject();
            const float Delta = static_cast<float>(Input->GetNumberField(TEXT("delta")));
            Root.Relevant = Input->GetBoolField(TEXT("relevant")); Root.Weight = static_cast<float>(Input->GetNumberField(TEXT("weight")));
            Root.Inactive = Input->GetBoolField(TEXT("inactive"));
            auto* HasInput = FindFProperty<FBoolProperty>(Instance->GetClass(), TEXT("HasMovementInput"));
            auto* Angle = FindFProperty<FStructProperty>(Instance->GetClass(), TEXT("SmoothedAimingAngle"));
            if (!HasInput || !Angle || Angle->Struct != TBaseStructure<FVector2D>::Get() ||
                !SetNumber(Instance.Get(), TEXT("RotationMode"), Input->GetNumberField(TEXT("mode")))) return false;
            HasInput->SetPropertyValue_InContainer(Instance.Get(), Input->GetBoolField(TEXT("hasInput")));
            *Angle->ContainerPtrToValuePtr<FVector2D>(Instance.Get()) = FVector2D(Input->GetNumberField(TEXT("yaw")), Input->GetNumberField(TEXT("pitch")));
            for (const TCHAR* Field : {TEXT("InputYawOffsetTime"), TEXT("LeftYawTime"), TEXT("RightYawTime"), TEXT("ForwardYawTime")})
                if (!SetNumber(Instance.Get(), Field, Input->GetNumberField(Field))) return false;
            FProxyAccess::Pre(Proxy, Instance.Get(), Delta);
            FProxyAccess::UpdateRoot(Proxy);
            Proxy.FlipBufferWriteIndex();
            const auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("input"), Input);
            Row->SetNumberField(TEXT("serial"), Serial);
            TArray<TSharedPtr<FJsonValue>> MachineRows, SourceRows;
            for (int32 M = 0; M < 3; ++M)
            {
                const auto* Machine = Machines[M]; const auto Value = MakeShared<FJsonObject>();
                Value->SetNumberField(TEXT("compiledIndex"), MachineIndices[M]);
                Value->SetNumberField(TEXT("nativeIndex"), Machine->StateMachineIndexInClass);
                Value->SetNumberField(TEXT("current"), Machine->GetCurrentState());
                Value->SetNumberField(TEXT("elapsed"), Machine->GetCurrentStateElapsedTime());
                TArray<TSharedPtr<FJsonValue>> Weights, Recorded, Edges, UpdatedStates;
                const auto& Definition = Class->GetBakedStateMachines()[Machine->StateMachineIndexInClass];
                float Sum = 0;
                for (int32 S = 0; S < Definition.States.Num(); ++S)
                {
                    const float RecordedWeight = Proxy.GetRecordedStateWeight(Machine->StateMachineIndexInClass, S);
                    Sum += RecordedWeight;
                    Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetCurrentState() >= 0 ? Machine->GetStateWeight(S) : 0));
                    Recorded.Add(MakeShared<FJsonValueNumber>(RecordedWeight));
                }
                Value->SetBoolField(TEXT("updated"), Sum > 0);
                if (Sum > 0) for (int32 S : FMachineAccess::Updates(*Machine)) UpdatedStates.Add(MakeShared<FJsonValueNumber>(S));
                for (const auto& Edge : FMachineAccess::Edges(*Machine))
                {
                    const auto Data = MakeShared<FJsonObject>();
                    Data->SetNumberField(TEXT("from"), Edge.PreviousState); Data->SetNumberField(TEXT("to"), Edge.NextState);
                    Data->SetNumberField(TEXT("duration"), Edge.CrossfadeDuration); Data->SetNumberField(TEXT("elapsed"), Edge.ElapsedTime);
                    Data->SetNumberField(TEXT("alpha"), Edge.Alpha); Data->SetBoolField(TEXT("active"), Edge.bActive);
                    TArray<TSharedPtr<FJsonValue>> Indices;
                    for (int32 Index : Edge.SourceTransitionIndices) Indices.Add(MakeShared<FJsonValueNumber>(Index));
                    Data->SetArrayField(TEXT("edges"), Indices); Edges.Add(MakeShared<FJsonValueObject>(Data));
                }
                Value->SetArrayField(TEXT("weights"), Weights); Value->SetArrayField(TEXT("recorded"), Recorded);
                Value->SetArrayField(TEXT("transitions"), Edges); Value->SetArrayField(TEXT("updates"), UpdatedStates);
                MachineRows.Add(MakeShared<FJsonValueObject>(Value));
            }
            for (int32 E = 0; E < 7; ++E)
            {
                const auto Value = MakeShared<FJsonObject>(); auto* Player = Evaluators[E];
                Value->SetNumberField(TEXT("compiledIndex"), EvaluatorIndices[E]); Value->SetNumberField(TEXT("weight"), Player->GetCachedBlendWeight());
                Value->SetNumberField(TEXT("time"), Player->GetAccumulatedTime());
                if (E < 2)
                {
                    const auto* Node = Proxy.GetNodeFromIndex<FAnimNode_SequenceEvaluatorBase>(EvaluatorIndices[E]);
                    if (!Node) return false;
                    Value->SetNumberField(TEXT("inputTime"), Node->GetExplicitTime());
                }
                else
                {
                    const auto* Node = Proxy.GetNodeFromIndex<FAnimNode_BlendSpaceEvaluator>(EvaluatorIndices[E]);
                    if (!Node) return false;
                    Value->SetNumberField(TEXT("inputTime"), Node->NormalizedTime); Value->SetNumberField(TEXT("pitch"), Node->GetPosition().X);
                    TArray<TSharedPtr<FJsonValue>> Samples;
                    for (const auto& Sample : FBlendAccess::Samples(*Node))
                    {
                        auto Data = MakeShared<FJsonObject>(); Data->SetNumberField(TEXT("index"), Sample.SampleDataIndex);
                        Data->SetNumberField(TEXT("weight"), Sample.TotalWeight); Data->SetNumberField(TEXT("seconds"), Sample.Time);
                        Samples.Add(MakeShared<FJsonValueObject>(Data));
                    }
                    Value->SetArrayField(TEXT("samples"), Samples);
                }
                SourceRows.Add(MakeShared<FJsonValueObject>(Value));
            }
            Row->SetArrayField(TEXT("machines"), MachineRows); Row->SetArrayField(TEXT("evaluators"), SourceRows);
            if (Root.Relevant)
            {
                FPoseContext Pose(&Proxy, true);
                FProxyAccess::EvaluateRoot(Proxy, Pose);
                WritePose(Pose, Row); ++PoseFrames;
            }
            Rows.Add(MakeShared<FJsonValueObject>(Row)); ++TotalFrames;
        }
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetStringField(TEXT("name"), TraceInput->GetStringField(TEXT("name")));
        Trace->SetNumberField(TEXT("hz"), TraceInput->GetIntegerField(TEXT("hz")));
        Trace->SetArrayField(TEXT("frames"), Rows); Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("source"), Blueprint->GetPathName());
    Result->SetStringField(TEXT("evaluation"), TEXT("Actual Aim compiled subgraph; native proxy update/tick/evaluate; RAW retargeted 79 bones"));
    Result->SetObjectField(TEXT("bindings"), Request->GetObjectField(TEXT("bindings")));
    Result->SetArrayField(TEXT("names"), Names); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))) return false;
    if (!FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_AIM_STATE_NATIVE_OK traces=%d frames=%d poses=%d assets_saved=0"), Traces.Num(), TotalFrames, PoseFrames);
    return true;
}

namespace
{
struct FOverlayTraceInertial : FAnimNode_Inertialization
{
    TArray<float> Durations;
    virtual void RequestInertialization(const FInertializationRequest& Request) override
    { Durations.Add(Request.Duration); FAnimNode_Inertialization::RequestInertialization(Request); }
};
struct FOverlayPoseTap : FAnimNode_Base
{
    FPoseLink Source;
    TSharedPtr<FJsonObject> Row;
    virtual void Initialize_AnyThread(const FAnimationInitializeContext& Context) override { Source.Initialize(Context); }
    virtual void CacheBones_AnyThread(const FAnimationCacheBonesContext& Context) override { Source.CacheBones(Context); }
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override { Source.Update(Context); }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        Source.Evaluate(Output);
        if (Row.IsValid()) { const auto Raw = MakeShared<FJsonObject>(); WritePose(Output, Raw); Row->SetObjectField(TEXT("beforeInertial"), Raw); }
    }
};
bool SetEnumSymbol(UObject* Object, FName Name, const FString& Symbol)
{
    FProperty* Property = Object->GetClass()->FindPropertyByName(Name);
    const auto* Byte = CastField<FByteProperty>(Property); const auto* EnumProperty = CastField<FEnumProperty>(Property);
    UEnum* Enum = Byte ? Byte->Enum.Get() : EnumProperty ? EnumProperty->GetEnum() : nullptr;
    if (!Enum) return false;
    const int64 Value = Enum->GetValueByNameString(Symbol);
    return Value != INDEX_NONE && SetNumber(Object, Name, static_cast<double>(Value));
}
bool SetOverlayStruct(UObject* Object, FName Name, const TArray<FString>& Fields, const TArray<TSharedPtr<FJsonValue>>& Values)
{
    auto* Property = FindFProperty<FStructProperty>(Object->GetClass(), Name);
    if (!Property || Fields.Num() != Values.Num()) return false;
    void* Data = Property->ContainerPtrToValuePtr<void>(Object);
    for (int32 Index = 0; Index < Fields.Num(); ++Index)
    {
        auto* Field = FindFProperty<FNumericProperty>(Property->Struct, *Fields[Index]);
        if (!Field || !Field->IsFloatingPoint()) return false;
        Field->SetFloatingPointPropertyValue(Field->ContainerPtrToValuePtr<void>(Data), Values[Index]->AsNumber());
    }
    return true;
}
}

bool UAlsAnimationGraphLibrary::ExportOverlayStateTrace(const FString& RequestPath, const FString& OutputPath)
{
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || RequestPath == OutputPath) return false;
    FString RequestText; TSharedPtr<FJsonObject> Request;
    if (!FFileHelper::LoadFileToString(RequestText, *RequestPath) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestText), Request) ||
        Request->GetIntegerField(TEXT("schemaVersion")) != 1) return false;
    auto* Blueprint = LoadObject<UAnimBlueprint>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    const auto* Class = Blueprint && Blueprint->GeneratedClass ? IAnimClassInterface::GetFromClass(Blueprint->GeneratedClass) : nullptr;
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!Class || !Mesh || !Mesh->GetSkeleton() || !World || Class->GetAnimNotifies().Num() <= 15) return false;
    const auto* SourceNode = FindObject<UEdGraphNode>(nullptr, *(Blueprint->GetPathName() + TEXT(":OverlayLayer.AnimGraphNode_Inertialization_0")));
    auto* Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
    const auto* InertialTemplate = SourceNode && Generated ? Generated->GetPropertyInstance<FAnimNode_Inertialization>(Generated->GetDefaultObject(), SourceNode->NodeGuid) : nullptr;
    if (!InertialTemplate) return false;
    FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient; Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AActor* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return false;
    ON_SCOPE_EXIT { World->DestroyActor(Owner); };
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Mesh->GetSkeleton()->GetReferenceSkeleton().GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    if (Required.Num() != 79) return false;
    const TCHAR* MachineNames[] = {TEXT("Overlay States"), TEXT("Rifle States"), TEXT("Pistol 1H States"), TEXT("Pistol 2H States"), TEXT("Bow States")};
    const TCHAR* OverlaySymbols[] = {TEXT("NewEnumerator0"), TEXT("NewEnumerator12"), TEXT("NewEnumerator13"), TEXT("NewEnumerator14"), TEXT("NewEnumerator15"),
        TEXT("NewEnumerator1"), TEXT("NewEnumerator5"), TEXT("NewEnumerator10"), TEXT("NewEnumerator6"), TEXT("NewEnumerator7"), TEXT("NewEnumerator8"), TEXT("NewEnumerator9"), TEXT("NewEnumerator11")};
    const TCHAR* RotationSymbols[] = {TEXT("NewEnumerator0"), TEXT("NewEnumerator1"), TEXT("NewEnumerator3")};
    bool CapturePose = false; Request->TryGetBoolField(TEXT("capturePose"), CapturePose);
    TArray<TSharedPtr<FJsonValue>> Traces, NotifyDefinitions; int32 TotalFrames = 0, Requests = 0;
    for (int32 Index = 8; Index <= 15; ++Index)
    {
        const auto Item = MakeShared<FJsonObject>(); Item->SetNumberField(TEXT("index"), Index);
        Item->SetStringField(TEXT("name"), Class->GetAnimNotifies()[Index].NotifyName.ToString());
        NotifyDefinitions.Add(MakeShared<FJsonValueObject>(Item));
    }
    for (const auto& TraceValue : Request->GetArrayField(TEXT("traces")))
    {
        const auto TraceInput = TraceValue->AsObject(); const int32 Kind = TraceInput->GetIntegerField(TEXT("machine")); if (Kind < 0 || Kind >= 5 || (CapturePose && Kind != 0)) return false;
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner)); Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get(), Blueprint->GeneratedClass)); Instance->InitializeAnimation(true);
        auto& Proxy = FInstanceAccess::Proxy(Instance.Get()); auto& Bones = Proxy.GetRequiredBones();
        Bones.InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Mesh->GetSkeleton()); Bones.SetUseRAWData(true); Bones.SetUseSourceData(false); Bones.SetDisableRetargeting(false);
        const int32 CompiledIndex = FProxyAccess::Index(Proxy, MachineNames[Kind]); auto* Machine = Proxy.GetMutableNodeFromIndex<FAnimNode_StateMachine>(CompiledIndex);
        if (!Machine || Class->GetAnimBlueprintFunctions().IsEmpty()) return false;
        FOverlayTraceInertial Inertial;
        static_cast<FAnimNode_Inertialization&>(Inertial) = *InertialTemplate;
        FOverlayPoseTap PoseTap; PoseTap.Source.SetLinkNode(Machine);
        if (CapturePose) Inertial.Source.SetLinkNode(&PoseTap); else Inertial.Source.SetLinkNode(Machine);
        FAimTraceRoot Root; Root.Aim.SetLinkNode(&Inertial);
        auto* RootProperty = CastField<FStructProperty>(Class->GetAnimBlueprintFunctions()[0].OutputPoseNodeProperty);
        if (!RootProperty || RootProperty->Struct != FAnimNode_Root::StaticStruct()) return false;
        auto* NativeRoot = RootProperty->ContainerPtrToValuePtr<FAnimNode_Root>(Instance.Get()); if (NativeRoot != Proxy.GetRootNode()) return false;
        const FPoseLink SavedLink = NativeRoot->Result; ON_SCOPE_EXIT { NativeRoot->Result = SavedLink; };
        NativeRoot->Result = FPoseLink(); NativeRoot->Result.SetLinkNode(&Root);
        TArray<TSharedPtr<FJsonValue>> Rows; int32 Serial = 0;
        for (const auto& FrameValue : TraceInput->GetArrayField(TEXT("frames")))
        {
            const FMemMark Mark(FMemStack::Get()); const TGuardValue<uint64> GlobalFrame(GFrameCounter, static_cast<uint64>(200000 + ++Serial));
            const auto Input = FrameValue->AsObject(); const float Delta = static_cast<float>(Input->GetNumberField(TEXT("delta")));
            const int32 Overlay = Input->GetIntegerField(TEXT("overlay")), Mode = Input->GetIntegerField(TEXT("mode"));
            if (Overlay < 0 || Overlay >= 13 || Mode < 0 || Mode >= 3) return false;
            Root.Relevant = Input->GetBoolField(TEXT("relevant")); Root.Inactive = Input->GetBoolField(TEXT("inactive")); Root.Weight = static_cast<float>(Input->GetNumberField(TEXT("weight")));
            if (!SetEnumSymbol(Instance.Get(), TEXT("OverlayState"), OverlaySymbols[Overlay]) || !SetEnumSymbol(Instance.Get(), TEXT("RotationMode"), RotationSymbols[Mode]) ||
                !SetEnumSymbol(Instance.Get(), TEXT("Gait"), FString::Printf(TEXT("NewEnumerator%d"), Input->GetIntegerField(TEXT("gait")))) ||
                !SetEnumSymbol(Instance.Get(), TEXT("MovementState"), FString::Printf(TEXT("NewEnumerator%d"), Input->GetIntegerField(TEXT("movement"))))) return false;
            auto* Moving = FindFProperty<FBoolProperty>(Instance->GetClass(), TEXT("IsMoving")); if (!Moving) return false;
            Moving->SetPropertyValue_InContainer(Instance.Get(), Input->GetBoolField(TEXT("moving")));
            if (CapturePose)
            {
                if (!SetNumber(Instance.Get(), TEXT("BasePose_N"), Input->GetNumberField(TEXT("baseN"))) ||
                    !SetNumber(Instance.Get(), TEXT("BasePose_CLF"), Input->GetNumberField(TEXT("baseClf"))) ||
                    !SetNumber(Instance.Get(), TEXT("LandPrediction"), Input->GetNumberField(TEXT("landPrediction"))) ||
                    !SetNumber(Instance.Get(), TEXT("AimSweepTime"), Input->GetNumberField(TEXT("aimSweepTime"))) ||
                    !SetNumber(Instance.Get(), TEXT("OverlayOverrideState"), Input->GetNumberField(TEXT("override"))) ||
                    !SetOverlayStruct(Instance.Get(), TEXT("RelativeAccelerationAmount"), {TEXT("X"), TEXT("Y"), TEXT("Z")}, Input->GetArrayField(TEXT("acceleration"))) ||
                    !SetOverlayStruct(Instance.Get(), TEXT("VelocityBlend"), {TEXT("F_3_2154ABAD4BD15DAC904154B63D704219"), TEXT("B_5_0A0855774CB13BB3E4B0A6847E7154F6"),
                        TEXT("L_8_DFEBB8584D28F158D2562CA60EB07B6D"), TEXT("R_9_79E6E09B4A52B442B9FE6DB7192CFBEE")}, Input->GetArrayField(TEXT("velocity")))) return false;
            }
            FProxyAccess::Pre(Proxy, Instance.Get(), Delta);
            // Explicitly controlled committed history isolates the transition
            // predicates from the final curve feedback integration tested later.
            FProxyAccess::Curves(Proxy).Add(TEXT("Enable_Transition"), static_cast<float>(Input->GetNumberField(TEXT("enable"))));
            FProxyAccess::Curves(Proxy).Add(TEXT("RotationAmount"), static_cast<float>(Input->GetNumberField(TEXT("rotation"))));
            if (CapturePose)
            {
                FProxyAccess::Curves(Proxy).Add(TEXT("Weight_Gait"), static_cast<float>(Input->GetNumberField(TEXT("weightGait"))));
                FProxyAccess::Curves(Proxy).Add(TEXT("Weight_InAir"), static_cast<float>(Input->GetNumberField(TEXT("weightInAir"))));
            }
            Inertial.Durations.Reset(); FProxyAccess::UpdateRoot(Proxy); Proxy.FlipBufferWriteIndex();
            // Drain last frame's copied diagnostics. The proxy's real queue is
            // reset by native PreUpdate and copied by native PostUpdate.
            Instance->NotifyQueue.AnimNotifies.Reset(); Instance->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
            FProxyAccess::Post(Proxy, Instance.Get());
            const auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("input"), Input); Row->SetNumberField(TEXT("serial"), Serial);
            Row->SetNumberField(TEXT("current"), Machine->GetCurrentState()); Row->SetNumberField(TEXT("elapsed"), Machine->GetCurrentStateElapsedTime());
            TArray<TSharedPtr<FJsonValue>> Weights, Edges, Updates, Notifies, Durations;
            const auto& Definition = Class->GetBakedStateMachines()[Machine->StateMachineIndexInClass];
            for (int32 State = 0; State < Definition.States.Num(); ++State) Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetCurrentState() >= 0 ? Machine->GetStateWeight(State) : 0));
            if (Root.Relevant) for (int32 State : FMachineAccess::Updates(*Machine)) Updates.Add(MakeShared<FJsonValueNumber>(State));
            for (const auto& Edge : FMachineAccess::Edges(*Machine))
            {
                const auto Data = MakeShared<FJsonObject>(); Data->SetNumberField(TEXT("from"), Edge.PreviousState); Data->SetNumberField(TEXT("to"), Edge.NextState);
                Data->SetNumberField(TEXT("duration"), Edge.CrossfadeDuration); Data->SetNumberField(TEXT("elapsed"), Edge.ElapsedTime); Data->SetNumberField(TEXT("alpha"), Edge.Alpha);
                TArray<TSharedPtr<FJsonValue>> Indices; for (int32 Index : Edge.SourceTransitionIndices) Indices.Add(MakeShared<FJsonValueNumber>(Index));
                Data->SetArrayField(TEXT("edges"), Indices); Edges.Add(MakeShared<FJsonValueObject>(Data));
            }
            for (const auto& Event : Instance->NotifyQueue.AnimNotifies)
            {
                const auto* Notify = Event.GetNotify(); if (!Notify) continue;
                for (int32 Index = 8; Index <= 15; ++Index) if (Notify->NotifyName == Class->GetAnimNotifies()[Index].NotifyName)
                    Notifies.Add(MakeShared<FJsonValueNumber>(Index));
            }
            for (float Duration : Inertial.Durations) { Durations.Add(MakeShared<FJsonValueNumber>(Duration)); ++Requests; }
            Row->SetArrayField(TEXT("weights"), Weights); Row->SetArrayField(TEXT("transitions"), Edges); Row->SetArrayField(TEXT("updates"), Updates);
            Row->SetArrayField(TEXT("notifies"), Notifies); Row->SetArrayField(TEXT("inertialization"), Durations);
            // Evaluation keeps cache/pose history real, but this oracle only
            // claims state updates, transitions, requests and generated notifies.
            if (Root.Relevant)
            {
                PoseTap.Row = Row;
                FPoseContext Pose(&Proxy, true); FProxyAccess::EvaluateRoot(Proxy, Pose);
                if (CapturePose)
                {
                    WritePose(Pose, Row); TArray<TSharedPtr<FJsonValue>> Times;
                    for (const auto& SourceValue : Request->GetArrayField(TEXT("sources")))
                    {
                        const auto Source = SourceValue->AsObject(); const int32 Index = Source->GetIntegerField(TEXT("index"));
                        const float Time = Source->GetBoolField(TEXT("evaluator")) ? Proxy.GetNodeFromIndex<FAnimNode_SequenceEvaluator>(Index)->GetExplicitTime() :
                            Proxy.GetNodeFromIndex<FAnimNode_SequencePlayer>(Index)->GetAccumulatedTime();
                        Times.Add(MakeShared<FJsonValueNumber>(Time));
                    }
                    Row->SetArrayField(TEXT("sourceTimes"), Times);
                }
            }
            Rows.Add(MakeShared<FJsonValueObject>(Row)); ++TotalFrames;
        }
        const auto Trace = MakeShared<FJsonObject>(); Trace->SetStringField(TEXT("name"), TraceInput->GetStringField(TEXT("name")));
        Trace->SetNumberField(TEXT("machine"), Kind); Trace->SetNumberField(TEXT("hz"), TraceInput->GetIntegerField(TEXT("hz")));
        Trace->SetNumberField(TEXT("compiledIndex"), CompiledIndex); Trace->SetNumberField(TEXT("nativeIndex"), Machine->StateMachineIndexInClass);
        Trace->SetArrayField(TEXT("frames"), Rows); Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1); Result->SetStringField(TEXT("source"), Blueprint->GetPathName());
    Result->SetStringField(TEXT("evaluation"), TEXT("Actual compiled Overlay machines with native inertialization; controlled committed curve inputs; state/transition/notify oracle, not final pose acceptance"));
    if (CapturePose)
    {
        Result->SetStringField(TEXT("evaluation"), TEXT("Actual complete Overlay layer pose; controlled frame inputs; native source times isolate pose composition from cross-graph clock integration"));
        TArray<TSharedPtr<FJsonValue>> Names;
        for (const auto Bone : Required) Names.Add(MakeShared<FJsonValueString>(Mesh->GetSkeleton()->GetReferenceSkeleton().GetBoneName(Bone).ToString()));
        Result->SetArrayField(TEXT("bones"), Names); Result->SetArrayField(TEXT("sources"), Request->GetArrayField(TEXT("sources")));
    }
    Result->SetStringField(TEXT("bindingDigest"), Request->GetStringField(TEXT("bindingDigest"))); Result->SetArrayField(TEXT("notifies"), NotifyDefinitions); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_OVERLAY_STATE_NATIVE_OK traces=%d frames=%d inertial_requests=%d assets_saved=0"), Traces.Num(), TotalFrames, Requests);
    return true;
}
