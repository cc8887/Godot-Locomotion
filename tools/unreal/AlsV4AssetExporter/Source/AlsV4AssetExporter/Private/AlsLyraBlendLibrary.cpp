#include "AlsLyraGraphLibrary.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_StateResult.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/StructOnScope.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraBlendProbe
{
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}
TSharedRef<FJsonObject> Atom(const FTransform& Value)
{
    const auto Result = MakeShared<FJsonObject>();
    const FVector P = Value.GetTranslation(), S = Value.GetScale3D(); const FQuat Q = Value.GetRotation();
    Result->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
    Result->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
    Result->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z})); return Result;
}
bool ReadAtom(const TSharedPtr<FJsonObject>& Row, FTransform& Value)
{
    const auto& P = Row->GetArrayField(TEXT("position")); const auto& Q = Row->GetArrayField(TEXT("rotation"));
    const auto& S = Row->GetArrayField(TEXT("scale"));
    if (P.Num() != 3 || Q.Num() != 4 || S.Num() != 3) return false;
    Value = FTransform(FQuat(Q[0]->AsNumber(), Q[1]->AsNumber(), Q[2]->AsNumber(), Q[3]->AsNumber()),
        FVector(P[0]->AsNumber(), P[1]->AsNumber(), P[2]->AsNumber()), FVector(S[0]->AsNumber(), S[1]->AsNumber(), S[2]->AsNumber()));
    return !Value.ContainsNaN() && Value.IsRotationNormalized();
}
TArray<TSharedPtr<FJsonValue>> Pose(const FCompactPose& Value)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const auto Bone : Value.ForEachBoneIndex()) Result.Add(MakeShared<FJsonValueObject>(Atom(Value[Bone])));
    return Result;
}
TSharedRef<FJsonObject> Curves(const FBlendedCurve& Value)
{
    const auto Result = MakeShared<FJsonObject>();
    Value.ForEachElement([&](const auto& Curve) { Result->SetNumberField(Curve.Name.ToString(), Curve.Value); });
    return Result;
}
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance)
    { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
};
struct FProxyAccess : FAnimInstanceProxy
{
    static void Tick(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta)
    { (Proxy.*&FProxyAccess::UpdateCounter).Increment(); (Proxy.*&FProxyAccess::PreUpdate)(Instance, Delta); }
};
struct FMachineAccess : FAnimNode_StateMachine
{
    static void ReplaceRoots(FAnimNode_StateMachine& Node, TArray<FAnimNode_StateResult>& Roots)
    {
        auto& Links = Node.*&FMachineAccess::StatePoseLinks;
        for (int32 State = 0; State < Roots.Num(); ++State)
            if (!(Node.*&FMachineAccess::GetMachineDescription)()->States[State].bIsAConduit) Links[State].SetLinkNode(&Roots[State]);
    }
    static TArray<TSharedPtr<FJsonValue>> Active(const FAnimNode_StateMachine& Node)
    {
        TArray<TSharedPtr<FJsonValue>> Result;
        for (const auto& Transition : Node.*&FMachineAccess::ActiveTransitionArray)
        {
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("previous"), Transition.PreviousState); Row->SetNumberField(TEXT("next"), Transition.NextState);
            Row->SetNumberField(TEXT("duration"), Transition.CrossfadeDuration); Row->SetNumberField(TEXT("elapsed"), Transition.ElapsedTime);
            Row->SetNumberField(TEXT("alpha"), Transition.Alpha); Row->SetBoolField(TEXT("inertial"), Transition.LogicType == ETransitionLogicType::TLT_Inertialization);
            Result.Add(MakeShared<FJsonValueObject>(Row));
        }
        return Result;
    }
};
struct FSource : FAnimNode_Base
{
    FCompactPose Input; FBlendedCurve InputCurves;
    int32 State = -1; TArray<TSharedPtr<FJsonValue>>* Updates = nullptr;
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    {
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("state"), State);
        Row->SetNumberField(TEXT("weight"), Context.GetFinalBlendWeight()); Row->SetBoolField(TEXT("active"), Context.IsActive());
        Row->SetBoolField(TEXT("inertial"), Context.GetMessage<UE::Anim::FAnimInertializationSyncScope>() != nullptr);
        Updates->Add(MakeShared<FJsonValueObject>(Row));
    }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    { Output.Pose.CopyBonesFrom(Input); Output.Curve.CopyFrom(InputCurves); }
};
struct FDriver : FAnimNode_Base
{
    FAnimNode_StateMachine* Machine = nullptr; const FBakedAnimationStateMachine* Definition = nullptr;
    TArray<FAnimNode_StateResult>* Roots = nullptr;
    int32 Edge = -1; float Adjustment = 0; TArray<int32> Path;
    TArray<TSharedPtr<FJsonValue>> Before; TSharedPtr<FJsonObject> BeforeCurves;
    virtual void Initialize_AnyThread(const FAnimationInitializeContext& Context) override
    { Machine->Initialize_AnyThread(Context); FMachineAccess::ReplaceRoots(*Machine, *Roots); }
    virtual void CacheBones_AnyThread(const FAnimationCacheBonesContext& Context) override { Machine->CacheBones_AnyThread(Context); }
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    {
        if (Edge >= 0)
        {
            FStructOnScope Storage(FAnimationPotentialTransition::StaticStruct());
            auto& Potential = *reinterpret_cast<FAnimationPotentialTransition*>(Storage.GetStructMemory());
            Potential.TargetState = Definition->Transitions[Edge].NextState; Potential.CrossfadeTimeAdjustment = Adjustment;
            for (int32 Index : Path) Potential.SourceTransitionIndices.Add(Index);
            Machine->TransitionToState(Context, Definition->Transitions[Edge], &Potential);
        }
        Machine->Update_AnyThread(Context);
    }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    { Machine->Evaluate_AnyThread(Output); Before = Pose(Output.Pose); BeforeCurves = Curves(Output.Curve); }
};
struct FInertia : FAnimNode_Inertialization
{
    TArray<TSharedPtr<FJsonValue>> Requests;
    virtual void RequestInertialization(const FInertializationRequest& Request) override
    {
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("duration"), Request.Duration);
        Row->SetBoolField(TEXT("useBlendMode"), Request.bUseBlendMode);
        Row->SetNumberField(TEXT("blendMode"), static_cast<int32>(Request.BlendMode));
        Row->SetStringField(TEXT("profile"), Request.BlendProfile ? Request.BlendProfile->GetPathName() : TEXT(""));
        Requests.Add(MakeShared<FJsonValueObject>(Row)); FAnimNode_Inertialization::RequestInertialization(Request);
    }
};
}

FString UAlsLyraGraphLibrary::ReadLocomotionBlendTrace(UClass* AnimationClass, USkeletalMesh* Mesh,
    USkeleton* Skeleton, const FString& RequestsJson)
{
    using namespace LyraBlendProbe;
    const IAnimClassInterface* Class = AnimationClass ? IAnimClassInterface::GetFromClass(AnimationClass) : nullptr;
    TSharedPtr<FJsonObject> Requests;
    if (!Class || !Mesh || !Skeleton || Skeleton->GetReferenceSkeleton().GetNum() != 81 ||
        Class->GetBakedStateMachines().Num() != 1 || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    const auto& Definition = Class->GetBakedStateMachines()[0];
    const auto& Templates = Requests->GetArrayField(TEXT("templates"));
    if (Definition.States.Num() != 12 || Templates.Num() != 12) return {};
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Requests->GetArrayField(TEXT("traces")))
    {
        const FMemMark Mark(FMemStack::Get());
        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr,
            false, ERHIFeatureLevel::Num, &Initialization));
        if (!World.IsValid()) return {};
        struct FWorldCleanup { UWorld* Value; ~FWorldCleanup() { Value->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        AActor* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return {};
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
        Component->bUseRefPoseOnInitAnim = true; Component->SetDisablePostProcessBlueprint(true);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision); Component->SetTeleportDistanceThreshold(500.f);
        Component->SetSkeletalMesh(Mesh); Component->SetAnimInstanceClass(AnimationClass);
        Owner->SetRootComponent(Component.Get()); Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
        UAnimInstance* Instance = Component->GetAnimInstance(); if (!Instance || Instance->GetClass() != AnimationClass) return {};
        FAnimInstanceProxy& Proxy = FInstanceAccess::Proxy(Instance);
        FAnimNode_StateMachine* Machine = nullptr; FAnimNode_Inertialization* Template = nullptr;
        for (const FStructProperty* Property : Class->GetAnimNodeProperties())
        {
            if (Property->Struct == FAnimNode_StateMachine::StaticStruct()) Machine = Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Instance);
            if (Property->Struct == FAnimNode_Inertialization::StaticStruct()) Template = Property->ContainerPtrToValuePtr<FAnimNode_Inertialization>(Instance);
        }
        if (!Machine || !Template) return {};
        Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(Class));
        TArray<FBoneIndexType> Required;
        for (int32 Bone = 0; Bone < 81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
        Proxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
        TArray<FSource> Sources; Sources.SetNum(12); TArray<FAnimNode_StateResult> Roots; Roots.SetNum(12);
        TArray<TSharedPtr<FJsonValue>> Updates;
        for (int32 State = 0; State < 12; ++State)
        {
            Sources[State].State = State; Sources[State].Updates = &Updates;
            Sources[State].Input.SetBoneContainer(&Proxy.GetRequiredBones()); Sources[State].InputCurves.InitFrom(Proxy.GetRequiredBones());
            Roots[State].Result.SetLinkNode(&Sources[State]);
            if (!Definition.States[State].bIsAConduit)
            {
                // Keep real StateResult roots during initial entry; subsequent
                // controlled source roots have no original node callbacks.
                auto* Root = Proxy.GetMutableNodeFromIndex<FAnimNode_StateResult>(Definition.States[State].StateRootNodeIndex);
                if (!Root) return {};
                Root->Result.SetLinkNode(&Sources[State]);
            }
        }
        for (const auto& State : Definition.States)
            for (const auto& Exit : State.Transitions)
                if (Exit.CanTakeDelegateIndex != INDEX_NONE)
                    Proxy.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(Exit.CanTakeDelegateIndex)->NativeTransitionDelegate.BindLambda([] { return false; });
        FDriver Driver; Driver.Machine = Machine; Driver.Definition = &Definition; Driver.Roots = &Roots;
        FInertia Node; static_cast<FAnimNode_Inertialization&>(Node) = *Template;
        Node.Source.SetLinkNode(&Driver);
        Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy)); Node.CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            const auto Frame = FrameValue->AsObject(); const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            FTransform ComponentTransform; if (!ReadAtom(Frame->GetObjectField(TEXT("component")), ComponentTransform)) return {};
            Component->SetWorldTransform(ComponentTransform); FProxyAccess::Tick(Proxy, Instance, Delta);
            const int32 Variant = static_cast<int32>(Frame->GetNumberField(TEXT("variant")));
            for (int32 State = 0; State < 12; ++State)
            {
                if (Definition.States[State].bIsAConduit) continue;
                const auto& Poses = Templates[State]->AsObject()->GetArrayField(TEXT("poses"));
                if (!Poses.IsValidIndex(Variant) || Poses[Variant]->AsArray().Num() != 81) return {};
                for (const auto Bone : Sources[State].Input.ForEachBoneIndex())
                    if (!ReadAtom(Poses[Variant]->AsArray()[Bone.GetInt()]->AsObject(), Sources[State].Input[Bone])) return {};
                Sources[State].InputCurves.Empty();
                for (const auto& Curve : Frame->GetArrayField(TEXT("curves"))[State]->AsObject()->Values)
                    Sources[State].InputCurves.Set(FName(FString(Curve.Key.ToView())), static_cast<float>(Curve.Value->AsNumber()));
            }
            Driver.Edge = static_cast<int32>(Frame->GetNumberField(TEXT("edge")));
            Driver.Adjustment = static_cast<float>(Frame->GetNumberField(TEXT("adjustment")));
            Driver.Path.Reset(); for (const auto& Index : Frame->GetArrayField(TEXT("path"))) Driver.Path.Add(static_cast<int32>(Index->AsNumber()));
            Updates.Reset(); Node.Requests.Reset();
            FAnimationUpdateSharedContext Shared; Node.Update_AnyThread(FAnimationUpdateContext(&Proxy, Delta, &Shared));
            FPoseContext Output(&Proxy); Node.Evaluate_AnyThread(Output);
            const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("state"), Machine->GetCurrentState());
            Row->SetNumberField(TEXT("elapsed"), Machine->GetCurrentStateElapsedTime());
            TArray<TSharedPtr<FJsonValue>> Weights;
            for (int32 State = 0; State < 12; ++State) Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetStateWeight(State)));
            Row->SetArrayField(TEXT("weights"), Weights); Row->SetArrayField(TEXT("active"), FMachineAccess::Active(*Machine));
            Row->SetArrayField(TEXT("updates"), Updates); Row->SetArrayField(TEXT("requests"), Node.Requests);
            Row->SetArrayField(TEXT("before"), Driver.Before); Row->SetObjectField(TEXT("beforeCurves"), Driver.BeforeCurves.ToSharedRef());
            Row->SetArrayField(TEXT("after"), Pose(Output.Pose)); Row->SetObjectField(TEXT("afterCurves"), Curves(Output.Curve));
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Trace = MakeShared<FJsonObject>(); Trace->SetNumberField(TEXT("hz"), TraceValue->AsObject()->GetNumberField(TEXT("hz")));
        Trace->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Trace)); Component->UnregisterComponent();
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("class"), AnimationClass->GetPathName());
    Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}
