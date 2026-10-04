#include "AlsLyraGraphLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendSpace.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimSubsystemInstance.h"
#include "Animation/AnimRootMotionProvider.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNodes/AnimNode_ApplyAdditive.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "Engine/SkeletalMesh.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "JsonObjectConverter.h"

namespace LyraMainLeanProbe
{
void Number(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, float Value)
{
    uint32 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value); Row->SetNumberField(FString(Name) + TEXT("Bits"), Bits);
}
struct FInstanceAccess : UAnimInstance
{ static FAnimInstanceProxy& Proxy(UAnimInstance* Instance) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); } };
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta)
    { (Proxy.*&FProxyAccess::UpdateCounter).Increment(); (Proxy.*&FProxyAccess::PreUpdate)(Instance, Delta); }
};
struct FSyncProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize; using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate; using FAnimInstanceProxy::UpdateAnimation;
};
struct FNodeAccess : FAnimNode_BlendSpacePlayerBase
{
    static const auto& Samples(const FAnimNode_BlendSpacePlayerBase& Node) { return Node.*&FNodeAccess::BlendSampleDataCache; }
    static int32 Cache(const FAnimNode_BlendSpacePlayerBase& Node) { return Node.*&FNodeAccess::CachedTriangulationIndex; }
    static const auto& Delta(const FAnimNode_AssetPlayerBase& Node) { return Node.*&FNodeAccess::DeltaTimeRecord; }
};
void Double(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, double Value)
{
    uint64 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value);
    Row->SetStringField(FString(Name) + TEXT("Bits"), FString::Printf(TEXT("%016llx"), static_cast<unsigned long long>(Bits)));
}
struct FLeanBasePose : FAnimNode_Base
{
    UAnimSequence* Sequence = nullptr; double Time = 0; float Weight = 0;
    bool GenerateRoot = false; FDeltaTimeRecord RootRange;
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override { Weight = Context.GetFinalBlendWeight(); }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        FAnimationPoseData Data(Output); Sequence->GetAnimationPose(Data, FAnimExtractContext(Time, false));
        if (GenerateRoot && Sequence->HasRootMotion())
            UE::Anim::IAnimRootMotionProvider::Get()->SampleRootMotion(RootRange, *Sequence, true, Output.CustomAttributes);
    }
};
}

static FString ReadMainLeanDataTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    UBlendSpace* Source, const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson,
    const TArray<UAnimSequence*>* BaseSequences = nullptr)
{
    using namespace LyraMainLeanProbe;
    const auto* Interface = MainClass ? IAnimClassInterface::GetFromClass(MainClass) : nullptr;
    TSharedPtr<FJsonObject> Requests;
    if (!Interface || !Mesh || !Skeleton || !Source || Skeleton->GetReferenceSkeleton().GetNum() != 81 ||
        Sequences.Num() != 3 || Source->GetBlendSamples().Num() != 3 ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    TStrongObjectPtr<UBlendSpace> Space(DuplicateObject<UBlendSpace>(Source, GetTransientPackage()));
    Space->ClearFlags(RF_Public | RF_Standalone); Space->SetFlags(RF_Transient);
    for (int32 Index = 0; Index < 3; ++Index)
        if (!Sequences[Index] || Sequences[Index]->GetSkeleton() != Skeleton ||
            !Space->ReplaceSampleAnimation(Index, Sequences[Index])) return {};
    Space->SetSkeleton(Skeleton); Space->ValidateSampleData(); Space->ResampleData();
    if (Space->GetBlendSamples().Num() != 3) return {};
    const auto& Properties = Interface->GetAnimNodeProperties();
    const auto& NodeIndices = Requests->GetArrayField(TEXT("nodeIndices"));
    if (NodeIndices.Num() != 3) return {};
    auto* AngleProperty = FindFProperty<FNumericProperty>(MainClass, TEXT("AdditiveLeanAngle"));
    if (!AngleProperty || !AngleProperty->IsFloatingPoint()) return {};
    const bool Compose = BaseSequences != nullptr;
    if (Compose) FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
    auto* RotationFunction = MainClass->FindFunctionByName(TEXT("UpdateRotationData"));
    auto* WorldRotation = FindFProperty<FStructProperty>(MainClass, TEXT("WorldRotation"));
    auto* YawDelta = FindFProperty<FNumericProperty>(MainClass, TEXT("YawDeltaSinceLastUpdate"));
    auto* YawSpeed = FindFProperty<FNumericProperty>(MainClass, TEXT("YawDeltaSpeed"));
    auto* First = FindFProperty<FBoolProperty>(MainClass, TEXT("IsFirstUpdate"));
    auto* Crouching = FindFProperty<FBoolProperty>(MainClass, TEXT("IsCrouching"));
    auto* Ads = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsADS"));
    if (Compose && (!RotationFunction || RotationFunction->NumParms != 0 || !WorldRotation ||
        WorldRotation->Struct != TBaseStructure<FRotator>::Get() || !YawDelta || !YawSpeed || !First || !Crouching || !Ads || BaseSequences->Num() != 3)) return {};
    auto RotationRow = [&](UAnimInstance* Main)
    {
        const auto Row = MakeShared<FJsonObject>(); const auto& Rotation = *WorldRotation->ContainerPtrToValuePtr<FRotator>(Main);
        Double(Row, TEXT("pitch"), Rotation.Pitch); Double(Row, TEXT("yaw"), Rotation.Yaw); Double(Row, TEXT("roll"), Rotation.Roll);
        Double(Row, TEXT("yawDelta"), YawDelta->GetFloatingPointPropertyValue(YawDelta->ContainerPtrToValuePtr<void>(Main)));
        Double(Row, TEXT("yawSpeed"), YawSpeed->GetFloatingPointPropertyValue(YawSpeed->ContainerPtrToValuePtr<void>(Main)));
        Double(Row, TEXT("angle"), AngleProperty->GetFloatingPointPropertyValue(AngleProperty->ContainerPtrToValuePtr<void>(Main)));
        Row->SetBoolField(TEXT("first"), First->GetPropertyValue_InContainer(Main)); return Row;
    };
    TArray<TSharedPtr<FJsonValue>> CompositionGraph;
    if (Compose)
        for (const auto& Property : Properties)
        {
            if (Property->Struct != FAnimNode_ApplyAdditive::StaticStruct()) continue;
            const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(MainClass->GetDefaultObject());
            const int32 AdditiveIndex = Properties.Num() - 1 - Node->Additive.LinkID;
            if (AdditiveIndex != 22 && AdditiveIndex != 16 && AdditiveIndex != 12) continue;
            const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("index"), Properties.Num() - 1 - Properties.IndexOfByKey(Property));
            Row->SetNumberField(TEXT("additiveIndex"), AdditiveIndex);
            Row->SetNumberField(TEXT("baseIndex"), Properties.Num() - 1 - Node->Base.LinkID);
            if (!Properties.IsValidIndex(Node->Base.LinkID) || Properties[Node->Base.LinkID]->Struct != FAnimNode_LinkedAnimLayer::StaticStruct()) return {};
            const auto* Linked = Properties[Node->Base.LinkID]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(MainClass->GetDefaultObject());
            Row->SetStringField(TEXT("layer"), Linked->Layer.ToString());
            const auto Settings = MakeShared<FJsonObject>();
            if (!FJsonObjectConverter::UStructToJsonObject(Property->Struct, Node, Settings)) return {};
            Row->SetObjectField(TEXT("settings"), Settings); CompositionGraph.Add(MakeShared<FJsonValueObject>(Row));
        }
    if (Compose && CompositionGraph.Num() != 3) return {};
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Requests->GetArrayField(TEXT("traces")))
    {
        const auto Trace = TraceValue->AsObject();
        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr,
            false, ERHIFeatureLevel::Num, &Initialization)); if (!World.IsValid()) return {};
        struct FCleanup { UWorld* World; ~FCleanup() { World->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        auto* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return {};
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh, GetTransientPackage()));
        Carrier->ClearFlags(RF_Public | RF_Standalone); Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner));
        Owner->SetRootComponent(Component.Get()); Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        Component->SetSkeletalMesh(Carrier.Get()); Component->SetAnimInstanceClass(MainClass); Component->RegisterComponent();
        auto* Main = Component->GetAnimInstance(); if (!Main) return {};
        auto& Proxy = FInstanceAccess::Proxy(Main); Carrier->SetSkeleton(Skeleton);
        TArray<FBoneIndexType> Required; for (int32 Bone = 0; Bone < 81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
        Proxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
        Proxy.GetRequiredBones().SetUseRAWData(true); Proxy.GetRequiredBones().SetDisableRetargeting(false);
        FAnimNode_BlendSpacePlayer* Nodes[3]; FPoseLink Links[3]; bool Initialized[3] = {false, false, false};
        FAnimNode_ApplyAdditive* Roots[3] = {nullptr, nullptr, nullptr}; FLeanBasePose Bases[3];
        for (int32 Index = 0; Index < 3; ++Index)
        {
            const int32 PropertyIndex = Properties.Num() - 1 - static_cast<int32>(NodeIndices[Index]->AsNumber());
            if (!Properties.IsValidIndex(PropertyIndex) || Properties[PropertyIndex]->Struct != FAnimNode_BlendSpacePlayer::StaticStruct()) return {};
            Nodes[Index] = Properties[PropertyIndex]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main);
            if (Nodes[Index]->GetBlendSpace() != Source || !Nodes[Index]->SetBlendSpace(Space.Get())) return {};
            Links[Index].LinkID = PropertyIndex; Links[Index].SetLinkNode(Nodes[Index]);
            if (Compose)
            {
                if (!(*BaseSequences)[Index] || (*BaseSequences)[Index]->GetSkeleton() != Skeleton || (*BaseSequences)[Index]->IsValidAdditive()) return {};
                Bases[Index].Sequence = (*BaseSequences)[Index];
                for (const auto& Property : Properties)
                {
                    if (Property->Struct != FAnimNode_ApplyAdditive::StaticStruct()) continue;
                    auto* Root = Property->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
                    if (Root->Additive.LinkID != PropertyIndex) continue;
                    if (Roots[Index]) return {}; Roots[Index] = Root; Root->Base.SetLinkNode(&Bases[Index]);
                    Links[Index].LinkID = Properties.IndexOfByKey(Property); Links[Index].SetLinkNode(Root);
                }
                if (!Roots[Index]) return {};
            }
        }
        TStrongObjectPtr<UAnimInstance> SyncInstance(NewObject<UAnimInstance>(Component.Get()));
        FSyncProxy Sync(SyncInstance.Get()); Sync.Initialize(SyncInstance.Get());
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const FMemMark Mark(FMemStack::Get()); const auto Frame = FrameValue->AsObject();
            const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            if (!Compose) AngleProperty->SetFloatingPointPropertyValue(AngleProperty->ContainerPtrToValuePtr<void>(Main), Frame->GetNumberField(TEXT("angle")));
            else
            {
                const auto& Rotation = Frame->GetArrayField(TEXT("actorRotation")); if (Rotation.Num() != 3) return {};
                Owner->SetActorRotation(FRotator(Rotation[0]->AsNumber(), Rotation[1]->AsNumber(), Rotation[2]->AsNumber()));
                First->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("first")));
                Crouching->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("crouchingAtRotation")));
                Ads->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("adsAtRotation")));
                const auto& Times = Frame->GetArrayField(TEXT("baseTimes")); if (Times.Num() != 3) return {};
                const TArray<TSharedPtr<FJsonValue>>* Generate = nullptr;
                const TArray<TSharedPtr<FJsonValue>>* Previous = nullptr;
                const TArray<TSharedPtr<FJsonValue>>* RootDeltas = nullptr;
                const bool HasRoot = Frame->TryGetArrayField(TEXT("generateRootMotion"), Generate);
                if (HasRoot && (!Frame->TryGetArrayField(TEXT("baseRootPrevious"), Previous) ||
                    !Frame->TryGetArrayField(TEXT("baseRootDelta"), RootDeltas) || Generate->Num() != 3 || Previous->Num() != 3 || RootDeltas->Num() != 3)) return {};
                for (int32 Index = 0; Index < 3; ++Index)
                {
                    Bases[Index].Time = Times[Index]->AsNumber(); Bases[Index].GenerateRoot = HasRoot && (*Generate)[Index]->AsBool();
                    if (HasRoot) Bases[Index].RootRange.Set(static_cast<float>((*Previous)[Index]->AsNumber()), static_cast<float>((*RootDeltas)[Index]->AsNumber()));
                }
            }
            FProxyAccess::Pre(Proxy, Main, Delta); Sync.PreUpdate(SyncInstance.Get(), Delta);
            TSharedPtr<FJsonObject> RotationBefore, RotationAfter, ActorRotation;
            if (Compose)
            {
                Interface->ForEachSubsystem(Main, [&](const FAnimSubsystemInstanceContext& InContext)
                {
                    if (InContext.SubsystemStruct == FAnimSubsystem_PropertyAccess::StaticStruct())
                    {
                        FAnimSubsystemUpdateContext GameContext(InContext, Main, Delta);
                        InContext.Subsystem.OnPreUpdate_GameThread(GameContext);
                        InContext.Subsystem.OnPostUpdate_GameThread(GameContext);
                        FAnimSubsystemParallelUpdateContext WorkerContext(InContext, Proxy, Delta);
                        InContext.Subsystem.OnPreUpdate_WorkerThread(WorkerContext);
                    }
                    return EAnimSubsystemEnumeration::Continue;
                });
                RotationBefore = RotationRow(Main); Main->ProcessEvent(RotationFunction, nullptr); RotationAfter = RotationRow(Main);
                const auto Rotation = Owner->GetActorRotation(); ActorRotation = MakeShared<FJsonObject>();
                Double(ActorRotation, TEXT("pitch"), Rotation.Pitch); Double(ActorRotation, TEXT("yaw"), Rotation.Yaw); Double(ActorRotation, TEXT("roll"), Rotation.Roll);
            }
            FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Root(&Sync, Delta, &Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Root, Root);
            const auto& Active = Frame->GetArrayField(TEXT("active")); const auto& Reset = Frame->GetArrayField(TEXT("initialize"));
            TSharedPtr<FJsonObject> Rows[3];
            for (int32 Index = 0; Index < 3; ++Index)
            {
                if (!Initialized[Index] || Reset[Index]->AsBool())
                { Links[Index].Initialize(FAnimationInitializeContext(&Proxy)); Links[Index].CacheBones(FAnimationCacheBonesContext(&Proxy)); Initialized[Index] = true; }
                Rows[Index] = MakeShared<FJsonObject>(); Number(Rows[Index], TEXT("before"), Nodes[Index]->GetAccumulatedTime());
                Rows[Index]->SetBoolField(TEXT("active"), Active[Index]->AsBool());
            }
            for (const auto& Order : Frame->GetArrayField(TEXT("order")))
            {
                const int32 Index = static_cast<int32>(Order->AsNumber()); if (Index < 0 || Index >= 3) return {};
                if (Active[Index]->AsBool())
                {
                    auto Context = FAnimationUpdateContext(&Proxy, Delta, &Shared).FractionalWeight(static_cast<float>(Frame->GetArrayField(TEXT("weights"))[Index]->AsNumber()));
                    Context.SetNodeId(static_cast<int32>(NodeIndices[Index]->AsNumber())); Links[Index].Update(Context);
                }
            }
            Sync.UpdateAnimation(); Sync.PostUpdate(SyncInstance.Get());
            TArray<TSharedPtr<FJsonValue>> NodeRows;
            for (int32 Index = 0; Index < 3; ++Index)
            {
                auto& Node = *Nodes[Index]; const auto Row = Rows[Index];
                Number(Row, TEXT("time"), Node.GetAccumulatedTime()); Number(Row, TEXT("pin"), static_cast<float>(Node.GetPosition().X));
                Number(Row, TEXT("cachedWeight"), Node.GetCachedBlendWeight());
                Row->SetNumberField(TEXT("cache"), FNodeAccess::Cache(Node));
                const auto& D = FNodeAccess::Delta(Node); Number(Row, TEXT("previous"), D.GetPrevious()); Number(Row, TEXT("delta"), D.Delta);
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const auto& Sample : FNodeAccess::Samples(Node))
                {
                    const auto S = MakeShared<FJsonObject>(); S->SetNumberField(TEXT("index"), Sample.SampleDataIndex);
                    Number(S, TEXT("weight"), Sample.TotalWeight); Number(S, TEXT("weightRate"), Sample.WeightRate);
                    Number(S, TEXT("rate"), Sample.SamplePlayRate);
                    PRAGMA_DISABLE_DEPRECATION_WARNINGS
                    Number(S, TEXT("time"), Sample.Time); Number(S, TEXT("previous"), Sample.PreviousTime);
                    PRAGMA_ENABLE_DEPRECATION_WARNINGS
                    Number(S, TEXT("deltaPrevious"), Sample.DeltaTimeRecord.GetPrevious()); Number(S, TEXT("delta"), Sample.DeltaTimeRecord.Delta);
                    Samples.Add(MakeShared<FJsonValueObject>(S));
                }
                Row->SetArrayField(TEXT("samples"), Samples);
                if (Active[Index]->AsBool())
                {
                    FPoseContext Pose(&Proxy, !Compose); Links[Index].Evaluate(Pose); Row->SetObjectField(TEXT("output"),
                        LyraCyclePoseProbe::PoseData(Pose.Pose, Pose.Curve, Pose.CustomAttributes, Skeleton->GetReferenceSkeleton()));
                    if (Compose)
                    {
                        Number(Row, TEXT("alpha"), Roots[Index]->ActualAlpha); Number(Row, TEXT("baseWeight"), Bases[Index].Weight);
                        FPoseContext Base(&Proxy); Bases[Index].Evaluate_AnyThread(Base);
                        Row->SetObjectField(TEXT("base"), LyraCyclePoseProbe::PoseData(Base.Pose, Base.Curve, Base.CustomAttributes, Skeleton->GetReferenceSkeleton()));
                    }
                }
                NodeRows.Add(MakeShared<FJsonValueObject>(Row));
            }
            const auto Row = MakeShared<FJsonObject>(); Row->SetArrayField(TEXT("nodes"), NodeRows);
            if (Compose) { Row->SetObjectField(TEXT("rotationBefore"), RotationBefore); Row->SetObjectField(TEXT("rotation"), RotationAfter); Row->SetObjectField(TEXT("actorRotation"), ActorRotation); }
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("hz"), Trace->GetNumberField(TEXT("hz")));
        Result->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Result)); Component->UnregisterComponent();
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("angleType"), AngleProperty->GetCPPType());
    if (Compose) { Result->SetArrayField(TEXT("compositionGraph"), CompositionGraph); Result->SetStringField(TEXT("rotationType"), WorldRotation->GetCPPType(nullptr, 0)); Result->SetStringField(TEXT("yawDeltaType"), YawDelta->GetCPPType()); Result->SetStringField(TEXT("yawSpeedType"), YawSpeed->GetCPPType()); }
    Result->SetArrayField(TEXT("traces"), Traces); FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadMainLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    UBlendSpace* Source, const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadMainLeanDataTrace(MainClass, Mesh, Skeleton, Source, Sequences, RequestsJson); }

FString UAlsLyraGraphLibrary::ReadMainLeanCompositionTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    UBlendSpace* Source, const TArray<UAnimSequence*>& Sequences, const TArray<UAnimSequence*>& BaseSequences, const FString& RequestsJson)
{ return ReadMainLeanDataTrace(MainClass, Mesh, Skeleton, Source, Sequences, RequestsJson, &BaseSequences); }
