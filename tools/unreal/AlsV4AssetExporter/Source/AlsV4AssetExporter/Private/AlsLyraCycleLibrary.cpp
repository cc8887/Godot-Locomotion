#include "AlsLyraGraphLibrary.h"
#include "AlsLyraMainObservationProbe.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimExecutionContext.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimNode_StateResult.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Animation/AnimStateMachineTypes.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimNodeReference.h"
#include "Animation/AnimNodeFunctionRef.h"
#include "Animation/AnimNodeData.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendProfile.h"
#include "Animation/BlendSpace.h"
#include "Animation/AnimCurveCompressionCodec_UniformIndexable.h"
#include "Animation/AnimBoneCompressionCodec.h"
#include "Misc/Base64.h"
#include "AnimationCompression.h"
#include "AnimEncoding.h"
#include "Animation/AnimSubsystem_NodeRelevancy.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNodes/AnimNode_ApplyAdditive.h"
#include "BoneControllers/AnimNode_OrientationWarping.h"
#include "BoneControllers/AnimNode_StrideWarping.h"
#include "Modules/ModuleManager.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "Engine/SkeletalMesh.h"
#include "GameFramework/Actor.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/StructOnScope.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "JsonObjectConverter.h"

namespace LyraCycleProbe
{
void Number(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, float Value)
{
    uint32 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value); Row->SetNumberField(FString(Name) + TEXT("Bits"), Bits);
}
void Double(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, double Value)
{
    uint64 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value); Row->SetStringField(FString(Name) + TEXT("Bits"), FString::Printf(TEXT("%016llx"), Bits));
}
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
};
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta)
    { (Proxy.*&FProxyAccess::UpdateCounter).Increment(); (Proxy.*&FProxyAccess::PreUpdate)(Instance, Delta); }
};
// An isolated native proxy owns this controlled source traversal's common
// sync scope. Its empty root lets UpdateAnimation perform exactly one sync tick.
struct FSyncProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate;
    using FAnimInstanceProxy::UpdateAnimation;
};
struct FNodeAccess : FAnimNode_AssetPlayerBase
{
    static float Time(const FAnimNode_AssetPlayerBase& Node) { return Node.*&FNodeAccess::InternalTimeAccumulator; }
    static float* TimeAddress(FAnimNode_AssetPlayerBase& Node) { return &(Node.*&FNodeAccess::InternalTimeAccumulator); }
    static FMarkerTickRecord& Marker(FAnimNode_AssetPlayerBase& Node) { return Node.*&FNodeAccess::MarkerTickRecord; }
    static const FDeltaTimeRecord& Delta(const FAnimNode_AssetPlayerBase& Node) { return Node.*&FNodeAccess::DeltaTimeRecord; }
    static FName FunctionName(const FAnimNode_Base& Node, FName Name)
    {
        using FRead=const FAnimNodeFunctionRef& (FAnimNode_Base::*)(UE::Anim::FNodeDataId,const UObject*) const;
        const FRead Read=&FNodeAccess::GetData<FAnimNodeFunctionRef>;
        return (Node.*Read)(UE::Anim::FNodeDataId(Name,&Node,FAnimNode_Base::StaticStruct()),nullptr).GetFunctionName();
    }
    static FName UpdateName(const FAnimNode_Base& Node) { return FunctionName(Node,TEXT("UpdateFunction")); }
};
struct FLeanAccess : FAnimNode_BlendSpacePlayerBase
{
    static const auto& Samples(const FAnimNode_BlendSpacePlayerBase& Node) { return Node.*&FLeanAccess::BlendSampleDataCache; }
};
struct FStopMachineAccess : FAnimNode_StateMachine
{
    // Explicit state-machine observation boundary, not a replacement rule.
    static void Current(FAnimNode_StateMachine& Machine,int32 State)
    { Machine.*&FStopMachineAccess::CurrentState=State; }
};
struct FRequests : UE::Anim::IInertializationRequester
{
    TArray<float>& Durations;
    TArray<TSharedPtr<FJsonValue>>* Details;
    FRequests(TArray<float>& InDurations, TArray<TSharedPtr<FJsonValue>>* InDetails=nullptr) : Durations(InDurations),Details(InDetails) {}
    virtual void RequestInertialization(float Duration, const UBlendProfile*) override { Durations.Add(Duration); }
    virtual void RequestInertialization(const FInertializationRequest& Request) override
    {
        Durations.Add(Request.Duration);
        if (Details)
        {
            const auto R=MakeShared<FJsonObject>(); Number(R,TEXT("duration"),Request.Duration);
            R->SetBoolField(TEXT("useBlendMode"),Request.bUseBlendMode);
            R->SetNumberField(TEXT("blendMode"),static_cast<int32>(Request.BlendMode));
            R->SetStringField(TEXT("profile"),Request.BlendProfile ? Request.BlendProfile->GetPathName() : TEXT(""));
            Details->Add(MakeShared<FJsonValueObject>(R));
        }
    }
    virtual void AddDebugRecord(const FAnimInstanceProxy&, int32) override {}
    virtual FName GetTag() const override { return TEXT("CycleProbe"); }
};
bool Set(UObject* Object, const TCHAR* Name, const TSharedPtr<FJsonValue>& Value)
{
    FProperty* Property = Object->GetClass()->FindPropertyByName(Name);
    if (auto* Boolean = CastField<FBoolProperty>(Property)) { Boolean->SetPropertyValue_InContainer(Object, Value->AsBool()); return true; }
    if (auto* Enum = CastField<FEnumProperty>(Property))
    { Enum->GetUnderlyingProperty()->SetIntPropertyValue(Enum->ContainerPtrToValuePtr<void>(Object), static_cast<int64>(Value->AsNumber())); return true; }
    if (auto* Numeric = CastField<FNumericProperty>(Property))
    {
        void* Address = Numeric->ContainerPtrToValuePtr<void>(Object);
        if (Numeric->IsFloatingPoint()) Numeric->SetFloatingPointPropertyValue(Address, Value->AsNumber());
        else Numeric->SetIntPropertyValue(Address, static_cast<int64>(Value->AsNumber()));
        return true;
    }
    UE_LOG(LogTemp, Error, TEXT("LYRA_CYCLE_PROBE_FAIL property=%s"), Name); return false;
}
bool Bind(UAnimInstance* Layer, const TSharedPtr<FJsonObject>& Bindings, const TMap<FString, UAnimSequence*>* Transient = nullptr)
{
    auto Load = [Transient](const FString& Path) -> UAnimSequence*
    { return Transient ? Transient->FindRef(Path) : LoadObject<UAnimSequence>(nullptr, *Path); };
    for (const auto& Group : Bindings->Values)
    {
        if (auto* AssetProperty = FindFProperty<FObjectPropertyBase>(Layer->GetClass(), *Group.Key))
        {
            auto* Asset = Load(Group.Value->AsString()); if (!Asset) return false;
            AssetProperty->SetObjectPropertyValue_InContainer(Layer, Asset); continue;
        }
        auto* Property = FindFProperty<FStructProperty>(Layer->GetClass(), *Group.Key); if (!Property) return false;
        void* Address = Property->ContainerPtrToValuePtr<void>(Layer);
        for (const auto& Binding : Group.Value->AsObject()->Values)
        {
            FObjectPropertyBase* Field = nullptr;
            for (TFieldIterator<FObjectPropertyBase> It(Property->Struct); It; ++It)
                if (It->GetName().StartsWith(FString(Binding.Key) + TEXT("_"), ESearchCase::IgnoreCase)) Field = *It;
            auto* Asset = Load(Binding.Value->AsString()); if (!Field || !Asset) return false;
            Field->SetObjectPropertyValue(Field->ContainerPtrToValuePtr<void>(Address), Asset);
        }
    }
    return true;
}
int32 LayerRoot(const IAnimClassInterface* Class, FName Name)
{
    for (const auto& Function : Class->GetAnimBlueprintFunctions())
        if (Function.Name == Name)
        {
            const int32 PropertyIndex = Class->GetAnimNodeProperties().IndexOfByKey(Function.OutputPoseNodeProperty);
            return PropertyIndex < 0 ? INDEX_NONE : Class->GetAnimNodeProperties().Num() - 1 - PropertyIndex;
        }
    return INDEX_NONE;
}
int32 CycleRoot(const IAnimClassInterface* Class) { return LayerRoot(Class, TEXT("FullBody_CycleState")); }
}

FString UAlsLyraGraphLibrary::ReadCycleLayerGraph(UClass* LayerClass)
{ return ReadAnimationLayerGraph(LayerClass, TEXT("FullBody_CycleState")); }

FString UAlsLyraGraphLibrary::ReadAnimationLayerGraph(UClass* LayerClass, FName LayerName, bool IncludeStateRoots)
{
    using namespace LyraCycleProbe;
    const auto* Class = LayerClass ? IAnimClassInterface::GetFromClass(LayerClass) : nullptr; if (!Class) return {};
    const auto& Properties = Class->GetAnimNodeProperties();
    const int32 RootIndex = LayerRoot(Class, LayerName); if (RootIndex < 0) return {};
    TSet<int32> Visited; TArray<TSharedPtr<FJsonValue>> Nodes;
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Address) -> TSharedPtr<FJsonValue>
        {
            if (auto* Object = CastField<FObjectPropertyBase>(Property))
            { auto* Value = Object->GetObjectPropertyValue(Address); return MakeShared<FJsonValueString>(Value ? Value->GetPathName() : TEXT("")); }
            return nullptr;
        });
    TFunction<bool(int32)> Visit = [&](int32 Index)
    {
        if (Visited.Contains(Index)) return true;
        const int32 PropertyIndex = Properties.Num() - 1 - Index;
        if (!Properties.IsValidIndex(PropertyIndex)) return false; Visited.Add(Index);
        const auto* Property = Properties[PropertyIndex]; const void* Node = Property->ContainerPtrToValuePtr<void>(LayerClass->GetDefaultObject());
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("index"), Index);
        Row->SetStringField(TEXT("type"), Property->Struct->GetPathName());
        const auto Settings = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(Property->Struct, Node, Settings, 0, 0, &ExportObject)) return false;
        Row->SetObjectField(TEXT("settings"), Settings);
        if (IncludeStateRoots)
        {
            const auto* Base=static_cast<const FAnimNode_Base*>(Node);
            const auto Functions=MakeShared<FJsonObject>();
            Functions->SetStringField(TEXT("initialUpdate"),FNodeAccess::FunctionName(*Base,TEXT("InitialUpdateFunction")).ToString());
            Functions->SetStringField(TEXT("becomeRelevant"),FNodeAccess::FunctionName(*Base,TEXT("BecomeRelevantFunction")).ToString());
            Functions->SetStringField(TEXT("update"),FNodeAccess::UpdateName(*Base).ToString());
            Row->SetObjectField(TEXT("functions"),Functions);
        }
        TArray<TSharedPtr<FJsonValue>> Links; TArray<int32> Children;
        auto Link = [&](const FString& Pin, const FPoseLinkBase* PoseLink)
        {
            if (PoseLink->LinkID < 0) return;
            const int32 Child = Properties.Num() - 1 - PoseLink->LinkID;
            const auto L = MakeShared<FJsonObject>(); L->SetStringField(TEXT("pin"), Pin); L->SetNumberField(TEXT("index"), Child);
            L->SetNumberField(TEXT("propertyIndex"), PoseLink->LinkID);
            Links.Add(MakeShared<FJsonValueObject>(L)); Children.Add(Child);
        };
        for (TFieldIterator<FProperty> Field(Property->Struct); Field; ++Field)
        {
            if (const auto* Struct = CastField<FStructProperty>(*Field); Struct && Struct->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))
                Link(Field->GetName(), Struct->ContainerPtrToValuePtr<FPoseLinkBase>(Node));
            else if (const auto* Array = CastField<FArrayProperty>(*Field))
                if (const auto* Inner = CastField<FStructProperty>(Array->Inner); Inner && Inner->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))
                {
                    FScriptArrayHelper Values(Array, Array->ContainerPtrToValuePtr<void>(Node));
                    for (int32 I = 0; I < Values.Num(); ++I) Link(FString::Printf(TEXT("%s[%d]"), *Field->GetName(), I), reinterpret_cast<const FPoseLinkBase*>(Values.GetRawPtr(I)));
                }
        }
        if (IncludeStateRoots && Property->Struct==FAnimNode_StateMachine::StaticStruct())
        {
            const auto* Machine=static_cast<const FAnimNode_StateMachine*>(Node);
            const auto& Definitions=Class->GetBakedStateMachines();
            if (!Definitions.IsValidIndex(Machine->StateMachineIndexInClass)) return false;
            const auto& Definition=Definitions[Machine->StateMachineIndexInClass];
            Row->SetNumberField(TEXT("machineIndex"),Machine->StateMachineIndexInClass);
            Row->SetStringField(TEXT("machineName"),Definition.MachineName.ToString());
            for (const auto& State : Definition.States)
                if (State.StateRootNodeIndex!=INDEX_NONE)
                {
                    const auto L=MakeShared<FJsonObject>();L->SetStringField(TEXT("pin"),FString::Printf(TEXT("State[%s]"),*State.StateName.ToString()));
                    L->SetNumberField(TEXT("index"),State.StateRootNodeIndex);
                    L->SetNumberField(TEXT("propertyIndex"),Properties.Num()-1-State.StateRootNodeIndex);
                    L->SetBoolField(TEXT("compiledState"),true);
                    Links.Add(MakeShared<FJsonValueObject>(L)); Children.Add(State.StateRootNodeIndex);
                }
        }
        Row->SetArrayField(TEXT("links"), Links); Nodes.Add(MakeShared<FJsonValueObject>(Row));
        for (int32 Child : Children) if (!Visit(Child)) return false; return true;
    };
    if (!Visit(RootIndex)) return {};
    const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("root"), RootIndex); Result->SetArrayField(TEXT("nodes"), Nodes);
    Result->SetNumberField(TEXT("rootPropertyIndex"), Properties.Num() - 1 - RootIndex);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadAnimationLayerDefaults(UClass* AnimationClass)
{
    if (!AnimationClass || !IAnimClassInterface::GetFromClass(AnimationClass)) return {};
    const auto Fields=MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> Assets; TSet<FString> Seen;
    const auto ExportObject=FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [&](FProperty* Property,const void* Address) -> TSharedPtr<FJsonValue>
        {
            if (const auto* Object=CastField<FObjectPropertyBase>(Property))
            {
                const auto* Value=Object->GetObjectPropertyValue(Address);const FString Path=Value ? Value->GetPathName() : TEXT("");
                if (Value && Value->IsA<UAnimationAsset>() && !Seen.Contains(Path))
                { Seen.Add(Path);Assets.Add(MakeShared<FJsonValueString>(Path)); }
                return MakeShared<FJsonValueString>(Path);
            }
            return nullptr;
        });
    const auto* Defaults=AnimationClass->GetDefaultObject();
    for (TFieldIterator<FProperty> It(AnimationClass);It;++It)
    {
        auto* Property=*It;
        if (!Cast<UAnimBlueprintGeneratedClass>(Property->GetOwnerClass())) continue;
        if (const auto* Struct=CastField<FStructProperty>(Property);Struct && Struct->Struct->IsChildOf(FAnimNode_Base::StaticStruct())) continue;
        const auto Value=FJsonObjectConverter::UPropertyToJsonValue(Property,Property->ContainerPtrToValuePtr<void>(Defaults),0,0,&ExportObject);
        if (!Value) return {};
        const auto Field=MakeShared<FJsonObject>();Field->SetStringField(TEXT("type"),Property->GetCPPType());Field->SetField(TEXT("value"),Value);
        Fields->SetObjectField(Property->GetName(),Field);
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetObjectField(TEXT("fields"),Fields);Result->SetArrayField(TEXT("animationAssets"),Assets);
    FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}

static FString ReadCycleTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson, bool LayerMode,
    USkeleton* EvaluationSkeleton = nullptr, const TArray<UAnimSequence*>* EvaluationSequences = nullptr,
    UBlendSpace* LeanSpace = nullptr, const TArray<UAnimSequence*>* LeanSequences = nullptr)
{
    using namespace LyraCycleProbe;
    TSharedPtr<FJsonObject> Input;
    const auto* MainInterface = MainClass ? IAnimClassInterface::GetFromClass(MainClass) : nullptr;
    if (!MainInterface || !Mesh || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Input)) return {};
    const bool FullPose = EvaluationSkeleton != nullptr;
    bool ObserveMain = false; Input->TryGetBoolField(TEXT("observeMain"), ObserveMain);
    bool CompleteMain = false; Input->TryGetBoolField(TEXT("completeMain"), CompleteMain);
    if (CompleteMain && !ObserveMain) return {};
    const bool MainCycle = LeanSpace != nullptr;
    TStrongObjectPtr<UBlendSpace> AdaptedLean;
    if (MainCycle)
    {
        if (!FullPose || !CompleteMain || !LeanSequences || LeanSequences->Num() != 3 || LeanSpace->GetBlendSamples().Num() != 3) return {};
        AdaptedLean.Reset(DuplicateObject<UBlendSpace>(LeanSpace, GetTransientPackage()));
        AdaptedLean->ClearFlags(RF_Public | RF_Standalone); AdaptedLean->SetFlags(RF_Transient);
        for (int32 Index = 0; Index < 3; ++Index)
            if (!(*LeanSequences)[Index] || (*LeanSequences)[Index]->GetSkeleton() != EvaluationSkeleton ||
                !AdaptedLean->ReplaceSampleAnimation(Index, (*LeanSequences)[Index])) return {};
        AdaptedLean->SetSkeleton(EvaluationSkeleton); AdaptedLean->ValidateSampleData(); AdaptedLean->ResampleData();
    }
    TMap<FString, UAnimSequence*> Transient; TMap<const UAnimSequence*, FString> Paths;
    if (FullPose)
    {
        if (!LayerMode || !EvaluationSequences || EvaluationSkeleton->GetReferenceSkeleton().GetNum() != 81) return {};
        const auto& Names = Input->GetArrayField(TEXT("sequencePaths"));
        if (Names.Num() != EvaluationSequences->Num()) return {};
        for (int32 Index = 0; Index < Names.Num(); ++Index)
        {
            auto* Sequence = (*EvaluationSequences)[Index];
            if (!Sequence || Sequence->GetSkeleton() != EvaluationSkeleton || Sequence->IsValidAdditive()) return {};
            Transient.Add(Names[Index]->AsString(), Sequence); Paths.Add(Sequence, Names[Index]->AsString());
        }
        FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
    }
    auto AssetPath = [&Paths, FullPose](const UAnimSequenceBase* Sequence) -> FString
    { return Sequence ? (FullPose ? Paths.FindRef(Cast<UAnimSequence>(Sequence)) : Sequence->GetPathName()) : TEXT(""); };
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Input->GetArrayField(TEXT("traces")))
    {
        const auto Trace = TraceValue->AsObject();
        auto* LayerClass = LoadObject<UClass>(nullptr, *Trace->GetStringField(TEXT("class")));
        const auto* LayerInterface = LayerClass ? IAnimClassInterface::GetFromClass(LayerClass) : nullptr;
        if (!LayerInterface) return {};
        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr,
            false, ERHIFeatureLevel::Num, &Initialization)); if (!World.IsValid()) return {};
        struct FCleanup { UWorld* World; ~FCleanup() { World->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        AActor* Owner = ObserveMain ? World->SpawnActor<ACharacter>(Spawn) : World->SpawnActor<AActor>(Spawn); if (!Owner) return {};
        if (CompleteMain)
        {
            auto* Controller = World->SpawnActor<APlayerController>(Spawn); if (!Controller) return {};
            Controller->Possess(CastChecked<ACharacter>(Owner));
        }
        // Register/link against the original compatible Manny carrier first.
        // Only its transient duplicate changes skeleton for the isolated ALS
        // evaluation; geometry is never ticked or rendered in this probe.
        TStrongObjectPtr<USkeletalMesh> Carrier(FullPose ? DuplicateObject<USkeletalMesh>(Mesh, GetTransientPackage()) : nullptr);
        if (Carrier.IsValid()) { Carrier->ClearFlags(RF_Public | RF_Standalone); Carrier->SetFlags(RF_Transient); }
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
        Component->bUseRefPoseOnInitAnim = true; Component->SetDisablePostProcessBlueprint(true);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision); Component->SetSkeletalMesh(FullPose ? Carrier.Get() : Mesh); Component->SetAnimInstanceClass(MainClass);
        if (ObserveMain) Component->SetupAttachment(Owner->GetRootComponent()); else Owner->SetRootComponent(Component.Get());
        Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
        auto* Main = Component->GetAnimInstance(); if (!Main || !Component->IsRegistered()) return {};
        Main->LinkAnimClassLayers(LayerClass);
        auto* Layer = Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
        if (!Layer || Layer->GetClass() != LayerClass || !Bind(Layer, Trace->GetObjectField(TEXT("bindings")), FullPose ? &Transient : nullptr)) return {};
        const int32 NodeIndex = static_cast<int32>(Trace->GetNumberField(TEXT("nodeIndex")));
        const auto& Properties = LayerInterface->GetAnimNodeProperties();
        const int32 PropertyIndex = Properties.Num() - 1 - NodeIndex;
        if (!Properties.IsValidIndex(PropertyIndex) || !Properties[PropertyIndex]->Struct->IsChildOf(FAnimNode_SequencePlayer::StaticStruct())) return {};
        auto& Node = *Properties[PropertyIndex]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Layer);
        const int32 TraversalIndex = LayerMode ? CycleRoot(LayerInterface) : NodeIndex;
        if (TraversalIndex < 0) return {};
        FPoseLink SourceLink; SourceLink.LinkID = LayerMode ? Properties.Num() - 1 - TraversalIndex : TraversalIndex;
        SourceLink.SetLinkNode(Properties[Properties.Num() - 1 - TraversalIndex]->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
        FPoseLink MainLink; FAnimNode_BlendSpacePlayer* Lean = nullptr;
        if (MainCycle)
        {
            const auto& MainProperties = MainInterface->GetAnimNodeProperties();
            const int32 AdditiveIndex = MainProperties.Num() - 1 - 17, LeanIndex = MainProperties.Num() - 1 - 16;
            if (!MainProperties.IsValidIndex(AdditiveIndex) || !MainProperties.IsValidIndex(LeanIndex) ||
                MainProperties[AdditiveIndex]->Struct != FAnimNode_ApplyAdditive::StaticStruct() ||
                MainProperties[LeanIndex]->Struct != FAnimNode_BlendSpacePlayer::StaticStruct()) return {};
            auto* Additive = MainProperties[AdditiveIndex]->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
            const int32 BaseIndex = MainProperties.Num() - 1 - 15;
            if (Additive->Base.LinkID != BaseIndex || Additive->Additive.LinkID != LeanIndex ||
                MainProperties[BaseIndex]->Struct != FAnimNode_LinkedAnimLayer::StaticStruct()) return {};
            auto* Linked = MainProperties[BaseIndex]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            if (Linked->Layer != TEXT("FullBody_CycleState") || Linked->GetTargetInstance<UAnimInstance>() != Layer) return {};
            Lean = MainProperties[LeanIndex]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main);
            Lean->SetBlendSpace(AdaptedLean.Get());
            MainLink.LinkID = AdditiveIndex; MainLink.SetLinkNode(Additive);
        }
        FAnimNode_SequenceEvaluator* HipFire = nullptr; FAnimNode_LayeredBoneBlend* Blend = nullptr;
        FAnimNode_OrientationWarping* Orientation = nullptr; FAnimNode_StrideWarping* Stride = nullptr;
        TStrongObjectPtr<UBlendProfile> AdaptedMask;
        if (LayerMode)
        {
            const int32 HipFireIndex = static_cast<int32>(Trace->GetNumberField(TEXT("hipFireIndex")));
            HipFire = Properties[Properties.Num() - 1 - HipFireIndex]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
            for (const auto* Property : Properties) if (Property->Struct == FAnimNode_LayeredBoneBlend::StaticStruct())
            {
                auto* Candidate = Property->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
                if (Candidate->BasePose.LinkID == PropertyIndex) Blend = Candidate;
            }
            if (!HipFire || !Blend) return {};
            auto& Marker = FNodeAccess::Marker(*HipFire); Marker.PreviousMarker.TimeToMarker = 0; Marker.NextMarker.TimeToMarker = 0;
        }
        if (FullPose)
        {
            TSharedPtr<FJsonObject> Closure;
            if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(UAlsLyraGraphLibrary::ReadCycleLayerGraph(LayerClass)), Closure)) return {};
            for (const auto& Value : Closure->GetArrayField(TEXT("nodes")))
            {
                const auto& Entry = Value->AsObject(); const int32 Index = Properties.Num() - 1 - static_cast<int32>(Entry->GetNumberField(TEXT("index")));
                if (Entry->GetStringField(TEXT("type")) == FAnimNode_OrientationWarping::StaticStruct()->GetPathName())
                    Orientation = Properties[Index]->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(Layer);
                if (Entry->GetStringField(TEXT("type")) == FAnimNode_StrideWarping::StaticStruct()->GetPathName())
                    Stride = Properties[Index]->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(Layer);
            }
            if (!Orientation || !Stride || Blend->BlendMasks.Num() != 1 || !Blend->BlendMasks[0]) return {};
            TArray<FBoneReference> Spines;
            for (auto Bone : Orientation->SpineBones)
            {
                if (Bone.BoneName == TEXT("spine_04") || Bone.BoneName == TEXT("spine_05")) Bone.BoneName = TEXT("spine_03");
                if (!Spines.ContainsByPredicate([Bone](const FBoneReference& Existing) { return Existing.BoneName == Bone.BoneName; })) Spines.Add(Bone);
            }
            Orientation->SpineBones = Spines;
            AdaptedMask.Reset(NewObject<UBlendProfile>(GetTransientPackage()));
            AdaptedMask->OwningSkeleton = EvaluationSkeleton; AdaptedMask->Mode = EBlendProfileMode::BlendMask;
            const auto& Reference = EvaluationSkeleton->GetReferenceSkeleton();
            for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
            {
                float Weight = 0;
                for (const auto& Entry : Blend->BlendMasks[0]->ProfileEntries)
                    if (Entry.BoneReference.BoneName == Reference.GetBoneName(Bone)) { Weight = Entry.BlendScale; break; }
                AdaptedMask->SetBoneBlendScale(Bone, Weight, false, true);
            }
            Blend->BlendMasks[0] = AdaptedMask.Get(); Blend->InvalidatePerBoneBlendWeights();
        }
        auto* Function = Layer->FindFunction(TEXT("UpdateCycleAnim"));
        auto* ContextProperty = Function ? FindFProperty<FStructProperty>(Function, TEXT("Context")) : nullptr;
        auto* NodeProperty = Function ? FindFProperty<FStructProperty>(Function, TEXT("Node")) : nullptr;
        auto* AlphaProperty = FindFProperty<FNumericProperty>(LayerClass, TEXT("StrideWarpingCycleAlpha"));
        auto* ClampProperty = FindFProperty<FStructProperty>(LayerClass, TEXT("PlayRateClampCycle"));
        if (!ContextProperty || !NodeProperty || !AlphaProperty || !ClampProperty || ContextProperty->Struct != FAnimUpdateContext::StaticStruct() || NodeProperty->Struct != FAnimNodeReference::StaticStruct()) return {};
        auto& MainProxy = FInstanceAccess::Proxy(Main); auto& LayerProxy = FInstanceAccess::Proxy(Layer);
        if (FullPose)
        {
            Carrier->SetSkeleton(EvaluationSkeleton);
            TArray<FBoneIndexType> Required;
            for (int32 Bone = 0; Bone < 81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
            LayerProxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *EvaluationSkeleton);
            LayerProxy.GetRequiredBones().SetUseRAWData(true); LayerProxy.GetRequiredBones().SetDisableRetargeting(false);
            if (MainCycle)
            {
                MainProxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *EvaluationSkeleton);
                MainProxy.GetRequiredBones().SetUseRAWData(true); MainProxy.GetRequiredBones().SetDisableRetargeting(false);
            }
        }
        TStrongObjectPtr<UAnimInstance> SyncInstance(NewObject<UAnimInstance>(Component.Get()));
        FSyncProxy SyncProxy(SyncInstance.Get()); SyncProxy.Initialize(SyncInstance.Get());
        auto& Storage = FNodeAccess::Marker(Node); Storage.PreviousMarker.TimeToMarker = 0; Storage.NextMarker.TimeToMarker = 0;
        TArray<TSharedPtr<FJsonValue>> Frames;
        bool Initialized = false;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const FMemMark FrameMark(FMemStack::Get());
            const auto Frame = FrameValue->AsObject(); const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            if (FullPose)
            {
                const auto Rotation = Frame->GetArrayField(TEXT("relativeRotation"));
                Component->SetRelativeRotation(FQuat(Rotation[0]->AsNumber(), Rotation[1]->AsNumber(), Rotation[2]->AsNumber(), Rotation[3]->AsNumber()));
            }
            TSharedPtr<FJsonObject> Observation;
            if (ObserveMain)
            {
                Observation = LyraMainObservationProbe::Tick(Main, CastChecked<ACharacter>(Owner), MainProxy, MainInterface,
                    Frame->GetObjectField(TEXT("observation")), Delta);
                if (!Observation) return {};
                const auto Transform = Component->GetComponentTransform(); const auto Rotation = Transform.GetRotation(); const auto Position = Transform.GetLocation();
                const auto ComponentInput = MakeShared<FJsonObject>();
                ComponentInput->SetArrayField(TEXT("rotation"), {MakeShared<FJsonValueNumber>(Rotation.X), MakeShared<FJsonValueNumber>(Rotation.Y), MakeShared<FJsonValueNumber>(Rotation.Z), MakeShared<FJsonValueNumber>(Rotation.W)});
                ComponentInput->SetArrayField(TEXT("position"), {MakeShared<FJsonValueNumber>(Position.X), MakeShared<FJsonValueNumber>(Position.Y), MakeShared<FJsonValueNumber>(Position.Z)});
                Observation->SetObjectField(TEXT("componentInput"), ComponentInput);
            }
            else for (const auto& Variable : Frame->GetObjectField(TEXT("main"))->Values) if (!Set(Main, *Variable.Key, Variable.Value)) return {};
            if (LayerMode) for (const auto& Variable : Frame->GetObjectField(TEXT("layer"))->Values) if (!Set(Layer, *Variable.Key, Variable.Value)) return {};
            if (!ObserveMain) FProxyAccess::Pre(MainProxy, Main, Delta);
            FProxyAccess::Pre(LayerProxy, Layer, Delta);
            SyncProxy.PreUpdate(SyncInstance.Get(), Delta);
            FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Root(&SyncProxy, Delta, &Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Root, Root);
            auto* TraversalProxy = MainCycle ? &MainProxy : &LayerProxy;
            auto& TraversalLink = MainCycle ? MainLink : SourceLink;
            auto Context = FAnimationUpdateContext(TraversalProxy, Delta, &Shared).FractionalWeight(static_cast<float>(Frame->GetNumberField(TEXT("weight"))));
            Context.SetNodeId(MainCycle ? 17 : NodeIndex);
            if (!Initialized || Frame->GetBoolField(TEXT("reinitialize")))
            { TraversalLink.Initialize(FAnimationInitializeContext(TraversalProxy)); if (LayerMode) TraversalLink.CacheBones(FAnimationCacheBonesContext(TraversalProxy)); Initialized = true; }
            const auto Row = MakeShared<FJsonObject>(); Number(Row, TEXT("before"), Node.GetAccumulatedTime());
            if (ObserveMain) Row->SetObjectField(TEXT("observation"), Observation);
            Row->SetStringField(TEXT("beforeAsset"), AssetPath(Node.GetSequence()));
            const bool Active = !LayerMode || Frame->GetBoolField(TEXT("active"));
            if (LayerMode) Number(Row, TEXT("hipFireBefore"), HipFire->GetAccumulatedTime());
            TArray<float> Durations;
            UE::Anim::TScopedGraphMessage<FRequests> RequestScope(Context, Durations);
            // The real engine pose link invokes the compiled update callback
            // and then updates the sequence node, in production order.
            if (Active) TraversalLink.Update(Context);
            auto* Sequence = Cast<UAnimSequence>(Node.GetSequence());
            if (!Sequence) { UE_LOG(LogTemp, Error, TEXT("LYRA_CYCLE_PROBE_FAIL callback asset class=%s"), *LayerClass->GetPathName()); return {}; }
            Row->SetStringField(TEXT("asset"), AssetPath(Sequence)); Number(Row, TEXT("playRate"), Node.GetPlayRate());
            const float Length = Sequence->GetPlayLength();
            Number(Row, TEXT("length"), Length);
            Number(Row, TEXT("rootDistance"), static_cast<float>(Sequence->ExtractRootMotionFromRange(0, Length, FAnimExtractContext()).GetTranslation().Size2D()));
            const auto& Clamp = *ClampProperty->ContainerPtrToValuePtr<FVector2D>(Layer);
            Double(Row, TEXT("clampMin"), Clamp.X); Double(Row, TEXT("clampMax"), Clamp.Y);
            Double(Row, TEXT("strideAlpha"), AlphaProperty->GetFloatingPointPropertyValue(AlphaProperty->ContainerPtrToValuePtr<void>(Layer)));
            Row->SetStringField(TEXT("strideType"), AlphaProperty->GetCPPType());
            TArray<TSharedPtr<FJsonValue>> RequestRows;
            for (float Duration : Durations) { const auto R = MakeShared<FJsonObject>(); Number(R, TEXT("duration"), Duration); RequestRows.Add(MakeShared<FJsonValueObject>(R)); }
            Row->SetArrayField(TEXT("inertia"), RequestRows);
            Number(Row, TEXT("prepared"), Node.GetAccumulatedTime());
            if (LayerMode)
            {
                Row->SetBoolField(TEXT("active"), Active); Number(Row, TEXT("cycleWeight"), Node.GetCachedBlendWeight());
                const float BlendWeight = Blend->BlendWeights[0]; Number(Row, TEXT("blendWeight"), BlendWeight);
                const bool HipFireActive = Active && FAnimWeight::IsRelevant(BlendWeight);
                Row->SetBoolField(TEXT("hipFireActive"), HipFireActive);
                Number(Row, TEXT("hipFireWeight"), HipFire->GetCachedBlendWeight());
                Row->SetStringField(TEXT("hipFireAsset"), AssetPath(HipFire->GetSequence()));
                Number(Row, TEXT("hipFirePrepared"), HipFire->GetAccumulatedTime());
                Number(Row, TEXT("hipFireExplicit"), HipFire->GetExplicitTime());
            }
            SyncProxy.UpdateAnimation(); SyncProxy.PostUpdate(SyncInstance.Get());
            if (MainCycle)
            {
                const auto L = MakeShared<FJsonObject>(); const auto& D = FNodeAccess::Delta(*Lean);
                Number(L, TEXT("time"), Lean->GetAccumulatedTime()); Number(L, TEXT("pin"), Lean->GetPosition().X);
                Number(L, TEXT("cachedWeight"), Lean->GetCachedBlendWeight()); Number(L, TEXT("previous"), D.GetPrevious()); Number(L, TEXT("delta"), D.Delta);
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const auto& S : FLeanAccess::Samples(*Lean))
                {
                    const auto V = MakeShared<FJsonObject>(); V->SetNumberField(TEXT("index"), S.SampleDataIndex);
                    Number(V, TEXT("weight"), S.TotalWeight); Number(V, TEXT("weightRate"), S.WeightRate);
                    PRAGMA_DISABLE_DEPRECATION_WARNINGS
                    Number(V, TEXT("time"), S.Time); Number(V, TEXT("previous"), S.PreviousTime);
                    PRAGMA_ENABLE_DEPRECATION_WARNINGS
                    Number(V, TEXT("deltaPrevious"), S.DeltaTimeRecord.GetPrevious()); Number(V, TEXT("delta"), S.DeltaTimeRecord.Delta);
                    Samples.Add(MakeShared<FJsonValueObject>(V));
                }
                L->SetArrayField(TEXT("samples"), Samples); Row->SetObjectField(TEXT("lean"), L);
            }
            if (LayerMode)
            {
                const auto& HD = FNodeAccess::Delta(*HipFire); const auto& HM = FNodeAccess::Marker(*HipFire);
                Number(Row, TEXT("hipFireTime"), HipFire->GetAccumulatedTime()); Number(Row, TEXT("hipFirePrevious"), HD.GetPrevious()); Number(Row, TEXT("hipFireDelta"), HD.Delta);
                Row->SetNumberField(TEXT("hipFireMarkerPrevious"), HM.PreviousMarker.MarkerIndex); Row->SetNumberField(TEXT("hipFireMarkerNext"), HM.NextMarker.MarkerIndex);
                Number(Row, TEXT("hipFireMarkerPreviousDistance"), HM.PreviousMarker.MarkerIndex == -2 ? 0 : HM.PreviousMarker.TimeToMarker);
                Number(Row, TEXT("hipFireMarkerNextDistance"), HM.NextMarker.MarkerIndex == -2 ? 0 : HM.NextMarker.TimeToMarker);
            }
            const auto& TickDelta = FNodeAccess::Delta(Node);
            Number(Row, TEXT("time"), Node.GetAccumulatedTime()); Number(Row, TEXT("previous"), TickDelta.GetPrevious()); Number(Row, TEXT("delta"), TickDelta.Delta);
            Row->SetNumberField(TEXT("markerPrevious"), Storage.PreviousMarker.MarkerIndex); Row->SetNumberField(TEXT("markerNext"), Storage.NextMarker.MarkerIndex);
            Number(Row, TEXT("markerPreviousDistance"), Storage.PreviousMarker.MarkerIndex == -2 ? 0 : Storage.PreviousMarker.TimeToMarker);
            Number(Row, TEXT("markerNextDistance"), Storage.NextMarker.MarkerIndex == -2 ? 0 : Storage.NextMarker.TimeToMarker);
            if (FullPose)
            {
                Number(Row, TEXT("orientationAngle"), Orientation->LocomotionAngle);
                Number(Row, TEXT("orientationAlpha"), Orientation->GetAlpha());
                Number(Row, TEXT("strideSpeed"), Stride->LocomotionSpeed);
                Number(Row, TEXT("strideNodeAlpha"), Stride->GetAlpha());
                Row->SetArrayField(TEXT("orientationDirection"), {MakeShared<FJsonValueNumber>(Orientation->LocomotionDirection.X),
                    MakeShared<FJsonValueNumber>(Orientation->LocomotionDirection.Y), MakeShared<FJsonValueNumber>(Orientation->LocomotionDirection.Z)});
                if (Active)
                {
                    FPoseContext Pose(TraversalProxy); TraversalLink.Evaluate(Pose);
                    const auto Output = LyraCyclePoseProbe::PoseData(Pose.Pose, Pose.Curve, Pose.CustomAttributes, EvaluationSkeleton->GetReferenceSkeleton());
                    if (!Output) return {}; Row->SetObjectField(TEXT("output"), Output);
                }
            }
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("profile"), Trace->GetStringField(TEXT("profile")));
        Result->SetNumberField(TEXT("hz"), Trace->GetNumberField(TEXT("hz"))); Result->SetArrayField(TEXT("frames"), Frames);
        if (FullPose)
        {
            const auto Types = MakeShared<FJsonObject>();
            for (const FName Name : {FName(TEXT("LocalVelocityDirectionAngle")), FName(TEXT("LocalVelocityDirectionAngleWithOffset")), FName(TEXT("DisplacementSpeed"))})
            {
                const auto* Property = MainClass->FindPropertyByName(Name); if (!Property) return {};
                Types->SetStringField(Name.ToString(), Property->GetCPPType());
            }
            Types->SetStringField(TEXT("StrideWarpingCycleAlpha"), AlphaProperty->GetCPPType());
            Result->SetObjectField(TEXT("propertyTypes"), Types);
        }
        Traces.Add(MakeShared<FJsonValueObject>(Result)); Component->UnregisterComponent();
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadCycleSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadCycleTrace(MainClass, Mesh, RequestsJson, false); }
FString UAlsLyraGraphLibrary::ReadCycleLayerTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadCycleTrace(MainClass, Mesh, RequestsJson, true); }
FString UAlsLyraGraphLibrary::ReadCycleRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadCycleTrace(MainClass, Mesh, RequestsJson, true, Skeleton, &Sequences); }
FString UAlsLyraGraphLibrary::ReadMainCycleLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace, const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadCycleTrace(MainClass, Mesh, RequestsJson, true, Skeleton, &Sequences, LeanSpace, &LeanSequences); }


FString UAlsLyraGraphLibrary::ReadDistanceSequenceData(UAnimSequence* Sequence, FName CurveName)
{
    using namespace LyraCycleProbe;
    if (!Sequence) return {};
    // Loading an editor sequence may start asynchronous compression. Freeze
    // this read's actual codec before exporting data used by native callbacks.
    // This accepts the existing in-memory result; it does not save the asset.
    Sequence->WaitOnExistingCompression(true);
    FAnimCurveBufferAccess Access(Sequence, CurveName);
    if (!Access.IsValid() || Access.GetNumSamples() < 2) return {};
    const auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("path"), Sequence->GetPathName());
    Number(Result, TEXT("length"), Sequence->GetPlayLength()); Number(Result, TEXT("rateScale"), Sequence->RateScale);
    Number(Result, TEXT("range"), Access.GetValue(Access.GetNumSamples()-1) - Access.GetValue(0));
    TArray<TSharedPtr<FJsonValue>> Samples;
    for (int32 I = 0; I < Access.GetNumSamples(); ++I)
    {
        const auto S = MakeShared<FJsonObject>(); Number(S, TEXT("time"), Access.GetTime(I)); Number(S, TEXT("value"), Access.GetValue(I));
        Samples.Add(MakeShared<FJsonValueObject>(S));
    }
    Result->SetArrayField(TEXT("samples"), Samples);
    if (Sequence->IsCurveCompressedDataValid())
    {
        auto Data = Sequence->GetCompressedData();
        auto* Codec = Cast<UAnimCurveCompressionCodec_UniformIndexable>(Data.Get().CurveCompressionCodec);
        const float* Buffer = nullptr; int32 Count = 0; float Rate = 0;
        if (!Codec || !Codec->GetCurveBufferAndSamples(Data.Get(), CurveName, Buffer, Count, Rate) || Count != Access.GetNumSamples()) return {};
        Result->SetStringField(TEXT("evaluation"), TEXT("UniformIndexable")); Number(Result, TEXT("sampleRate"), Rate);
    }
    else
    {
        const auto* Curve = static_cast<const FFloatCurve*>(Sequence->GetCurveData().GetCurveData(CurveName)); if (!Curve) return {};
        const auto Settings = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(), &Curve->FloatCurve, Settings, 0, 0)) return {};
        Result->SetStringField(TEXT("evaluation"), TEXT("RawRichCurve")); Result->SetObjectField(TEXT("richCurve"), Settings);
    }
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadRootCompressionData(UAnimSequence* Sequence)
{
    using namespace LyraCycleProbe;
    if (!Sequence || !Sequence->GetSkeleton()) return {};
    Sequence->WaitOnExistingCompression(true); auto Data = Sequence->GetCompressedData();
    const auto& D = Data.Get(); if (!Sequence->IsBoneCompressedDataValid() || !D.BoneCompressionCodec) return {};
    const FString Codec = D.BoneCompressionCodec->GetClass()->GetPathName();
    if (Codec != TEXT("/Script/Engine.AnimCompress_PerTrackCompression") &&
        Codec != TEXT("/Script/Engine.AnimCompress_BitwiseCompressOnly") &&
        Codec != TEXT("/Script/Engine.AnimCompress_RemoveLinearKeys")) return {};
    const auto Row = MakeShared<FJsonObject>();
    Row->SetStringField(TEXT("codec"), Codec);
    Row->SetStringField(TEXT("payload"), FBase64::Encode(D.CompressedByteStream.GetData(), D.CompressedByteStream.Num()));
    Row->SetNumberField(TEXT("rootTrack"), D.GetTrackIndexFromSkeletonIndex(0));
    Number(Row, TEXT("length"), Sequence->GetPlayLength());
    const auto Rate = Sequence->GetSamplingFrameRate();
    Row->SetNumberField(TEXT("frameRateNumerator"), Rate.Numerator);
    Row->SetNumberField(TEXT("frameRateDenominator"), Rate.Denominator);
    Row->SetNumberField(TEXT("interpolation"), static_cast<int32>(Sequence->Interpolation));
    if (!D.CompressedDataStructure) return {};
    const auto& Legacy = static_cast<const FUECompressedAnimData&>(*D.CompressedDataStructure);
    const int32 RootTrack = D.GetTrackIndexFromSkeletonIndex(0);
    if (RootTrack < 0) return {};
    Row->SetNumberField(TEXT("keyEncoding"), static_cast<int32>(Legacy.KeyEncodingFormat));
    Row->SetNumberField(TEXT("compressedNumberOfKeys"), Legacy.CompressedNumberOfKeys);
    Row->SetNumberField(TEXT("numberOfFrames"), FMath::RoundToInt(Rate.AsFrameTime(Sequence->GetPlayLength()).AsDecimal()));
    TArray<TSharedPtr<FJsonValue>> Channels;
    for (int32 Channel = 0; Channel < 3; ++Channel)
    {
        const auto C = MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> Keys, Frames;
        int32 Offset = INDEX_NONE, Count = 1, Format = ACF_Identity, Flags = 0, Stride = 0, Fixed = 0;
        const bool PerTrack = Legacy.KeyEncodingFormat == AKF_PerTrackCompression;
        const bool MissingScale = Channel == 2 && !Legacy.CompressedScaleOffsets.IsValid();
        if (!MissingScale)
        {
            if (PerTrack)
            {
                Offset = Channel == 2 ? Legacy.CompressedScaleOffsets.GetOffsetData(RootTrack,0) : Legacy.CompressedTrackOffsets[RootTrack*2+Channel];
                if (Offset != INDEX_NONE)
                    FAnimationCompression_PerTrackUtils::DecomposeHeader(*reinterpret_cast<const int32*>(Legacy.CompressedByteStream.GetData()+Offset), Format, Count, Flags, Stride, Fixed);
            }
            else
            {
                Offset = Channel == 2 ? Legacy.CompressedScaleOffsets.GetOffsetData(RootTrack,0) : Legacy.CompressedTrackOffsets[RootTrack*4+Channel*2];
                Count = Channel == 2 ? Legacy.CompressedScaleOffsets.GetOffsetData(RootTrack,1) : Legacy.CompressedTrackOffsets[RootTrack*4+Channel*2+1];
                Format = Channel == 0 ? Legacy.TranslationCompressionFormat : Channel == 1 ? Legacy.RotationCompressionFormat : Legacy.ScaleCompressionFormat;
                if (Count == 1) Format = Channel == 1 ? ACF_Float96NoW : ACF_None;
                Stride = Channel == 1 ? CompressedRotationStrides[Format]*CompressedRotationNum[Format] :
                    Channel == 0 ? CompressedTranslationStrides[Format]*CompressedTranslationNum[Format] : CompressedScaleStrides[Format]*CompressedScaleNum[Format];
                Fixed = Format == ACF_IntervalFixed32NoW && Count > 1 ? 24 : 0;
            }
        }
        const uint8* Top = Offset == INDEX_NONE ? nullptr : Legacy.CompressedByteStream.GetData()+Offset+(PerTrack ? 4 : 0);
        for (int32 Key = 0; Key < Count; ++Key)
        {
            FQuat4f Q = FQuat4f::Identity; FVector3f V = MissingScale ? FVector3f::OneVector : FVector3f::ZeroVector;
            const uint8* K = Top ? Top+Fixed+Key*Stride : nullptr;
            if (Top && PerTrack)
            {
                if (Channel == 1) FAnimationCompression_PerTrackUtils::DecompressRotation(Format,Flags,Q,Top,K);
                else if (Channel == 0) FAnimationCompression_PerTrackUtils::DecompressTranslation(Format,Flags,V,Top,K);
                else FAnimationCompression_PerTrackUtils::DecompressScale(Format,Flags,V,Top,K);
            }
            else if (Top)
            {
                if (Channel == 1)
                {
#define ROOT_QUAT(F) case F: DecompressRotation<F>(Q,Top,K); break
                    switch (Format) { ROOT_QUAT(ACF_None); ROOT_QUAT(ACF_Float96NoW); ROOT_QUAT(ACF_Fixed48NoW); ROOT_QUAT(ACF_Fixed32NoW); ROOT_QUAT(ACF_IntervalFixed32NoW); ROOT_QUAT(ACF_Float32NoW); ROOT_QUAT(ACF_Identity); default: return {}; }
#undef ROOT_QUAT
                }
                else
                {
#define ROOT_VECTOR(F) case F: if (Channel == 0) DecompressTranslation<F>(V,Top,K); else DecompressScale<F>(V,Top,K); break
                    switch (Format) { ROOT_VECTOR(ACF_None); ROOT_VECTOR(ACF_Float96NoW); ROOT_VECTOR(ACF_Fixed48NoW); ROOT_VECTOR(ACF_IntervalFixed32NoW); ROOT_VECTOR(ACF_Identity); default: return {}; }
#undef ROOT_VECTOR
                }
            }
            TArray<TSharedPtr<FJsonValue>> Values = {MakeShared<FJsonValueNumber>(Channel == 1 ? Q.X : V.X),MakeShared<FJsonValueNumber>(Channel == 1 ? Q.Y : V.Y),MakeShared<FJsonValueNumber>(Channel == 1 ? Q.Z : V.Z)};
            if (Channel == 1) Values.Add(MakeShared<FJsonValueNumber>(Q.W));
            Keys.Add(MakeShared<FJsonValueArray>(Values));
        }
        if (Top && Count > 1 && (PerTrack ? (Flags & 8) != 0 : Legacy.KeyEncodingFormat == AKF_VariableKeyLerp))
        {
            const uint8* Table = Align(Top+Fixed+Count*Stride,4);
            for (int32 Key = 0; Key < Count; ++Key)
                Frames.Add(MakeShared<FJsonValueNumber>(Legacy.CompressedNumberOfKeys > 255 ? reinterpret_cast<const uint16*>(Table)[Key] : Table[Key]));
        }
        C->SetNumberField(TEXT("offset"),Offset); C->SetNumberField(TEXT("format"),Format); C->SetNumberField(TEXT("flags"),Flags);
        C->SetArrayField(TEXT("keys"),Keys); C->SetArrayField(TEXT("frames"),Frames);
        Channels.Add(MakeShared<FJsonValueObject>(C));
    }
    Row->SetArrayField(TEXT("channels"),Channels);
    TArray<TSharedPtr<FJsonValue>> Probes;
    for (double Fraction : {0., .00123456789, .013579, .1, .37, .5, .75, .9999999, 1.})
    {
        const double Time = Sequence->GetPlayLength()*Fraction;
        FTransform Pose = Sequence->ExtractRootTrackTransform(FAnimExtractContext(Time), nullptr);
        const auto P = MakeShared<FJsonObject>(); P->SetNumberField(TEXT("time"), Time);
        const auto T = Pose.GetTranslation(); const auto Q = Pose.GetRotation(); const auto S = Pose.GetScale3D();
        P->SetArrayField(TEXT("position"), {MakeShared<FJsonValueNumber>(T.X),MakeShared<FJsonValueNumber>(T.Y),MakeShared<FJsonValueNumber>(T.Z)});
        P->SetArrayField(TEXT("rotation"), {MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});
        P->SetArrayField(TEXT("scale"), {MakeShared<FJsonValueNumber>(S.X),MakeShared<FJsonValueNumber>(S.Y),MakeShared<FJsonValueNumber>(S.Z)});
        Probes.Add(MakeShared<FJsonValueObject>(P));
    }
    Row->SetArrayField(TEXT("probes"), Probes); FString Json;
    FJsonSerializer::Serialize(Row, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadDistanceCurveProbe(UAnimSequence* Sequence, FName CurveName, const FString& TimesJson)
{
    using namespace LyraCycleProbe;
    TArray<TSharedPtr<FJsonValue>> Times; if (!Sequence || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(TimesJson), Times)) return {};
    const auto* Curve = static_cast<const FFloatCurve*>(Sequence->GetCurveData().GetCurveData(CurveName)); if (!Curve) return {};
    TArray<TSharedPtr<FJsonValue>> Rows;
    for (const auto& T : Times)
    {
        const float Time = static_cast<float>(T->AsNumber()); const auto R = MakeShared<FJsonObject>();
        Number(R, TEXT("time"), Time); Number(R, TEXT("value"), Sequence->EvaluateCurveData(CurveName, FAnimExtractContext(static_cast<double>(Time))));
        Number(R, TEXT("raw"), Curve->FloatCurve.Eval(Time)); Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("samples"), Rows); Result->SetBoolField(TEXT("compressedValid"), Sequence->IsCurveCompressedDataValid());
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

#include "AlsLyraGroundPivotProbe.inl"

static FString ReadStartTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson,
    USkeleton* EvaluationSkeleton = nullptr, const TArray<UAnimSequence*>* EvaluationSequences = nullptr,
    UBlendSpace* LeanSpace = nullptr, const TArray<UAnimSequence*>* LeanSequences = nullptr, bool JointCycle = false,
    bool StopSource = false, bool StopMain = false, bool JointStop = false, bool StateRoots = false, bool GraphHistory = false, bool JointPivot = false)
{
    using namespace LyraCycleProbe;
    TSharedPtr<FJsonObject> Input;
    if (!MainClass || !Mesh || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Input)) return {};
    const bool FullPose = EvaluationSkeleton != nullptr;
    const bool MainStart = LeanSpace != nullptr;
    const bool RunMain = MainStart || StopMain;
    if (StopMain && (!StopSource || !FullPose || MainStart || JointCycle)) return {};
    if (JointCycle && !MainStart) return {};
    if (JointStop && (!JointCycle || StopSource || StopMain || !FullPose)) return {};
    if (StateRoots && !JointStop) return {};
    if (GraphHistory && !StateRoots) return {};
    if (JointPivot && !GraphHistory) return {};
    const auto* MainInterface = MainClass ? IAnimClassInterface::GetFromClass(MainClass) : nullptr;
    TStrongObjectPtr<UBlendSpace> AdaptedLean;
    if (MainStart)
    {
        if (!FullPose || !MainInterface || !LeanSequences || LeanSequences->Num() != 3 || LeanSpace->GetBlendSamples().Num() != 3) return {};
        AdaptedLean.Reset(DuplicateObject<UBlendSpace>(LeanSpace, GetTransientPackage()));
        AdaptedLean->ClearFlags(RF_Public | RF_Standalone); AdaptedLean->SetFlags(RF_Transient);
        for (int32 Index = 0; Index < 3; ++Index)
            if (!(*LeanSequences)[Index] || (*LeanSequences)[Index]->GetSkeleton() != EvaluationSkeleton ||
                !AdaptedLean->ReplaceSampleAnimation(Index, (*LeanSequences)[Index])) return {};
        AdaptedLean->SetSkeleton(EvaluationSkeleton); AdaptedLean->ValidateSampleData(); AdaptedLean->ResampleData();
    }
    TMap<FString, UAnimSequence*> Transient; TMap<const UAnimSequence*, FString> Paths;
    if (FullPose)
    {
        if (!EvaluationSequences || EvaluationSkeleton->GetReferenceSkeleton().GetNum() != 81) return {};
        const auto& Names = Input->GetArrayField(TEXT("sequencePaths"));
        if (Names.Num() != EvaluationSequences->Num()) return {};
        for (int32 Index = 0; Index < Names.Num(); ++Index)
        {
            auto* Sequence = (*EvaluationSequences)[Index];
            if (!Sequence || Sequence->GetSkeleton() != EvaluationSkeleton || Sequence->IsValidAdditive()) return {};
            // SetSkeleton/controller edits can restart compression. Settle it
            // before distance callbacks read the default compressed codec.
            Sequence->WaitOnExistingCompression(true);
            Transient.Add(Names[Index]->AsString(), Sequence); Paths.Add(Sequence, Names[Index]->AsString());
        }
        FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
    }
    auto AssetPath = [&Paths, FullPose](const UAnimSequenceBase* Sequence) -> FString
    { return Sequence ? (FullPose ? Paths.FindRef(Cast<UAnimSequence>(Sequence)) : Sequence->GetPathName()) : TEXT(""); };
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Input->GetArrayField(TEXT("traces")))
    {
        const auto Trace = TraceValue->AsObject(); auto* LayerClass = LoadObject<UClass>(nullptr, *Trace->GetStringField(TEXT("class")));
        const auto* Interface = LayerClass ? IAnimClassInterface::GetFromClass(LayerClass) : nullptr; if (!Interface) return {};
        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr, false, ERHIFeatureLevel::Num, &Initialization));
        if (!World.IsValid()) return {};
        struct FCleanup { UWorld* W; ~FCleanup() { W->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        AActor* Owner = MainStart || StopSource ? World->SpawnActor<ACharacter>(Spawn) : World->SpawnActor<AActor>(Spawn); if (!Owner) return {};
        if (RunMain)
        {
            auto* Controller = World->SpawnActor<APlayerController>(Spawn); if (!Controller) return {};
            Controller->Possess(CastChecked<ACharacter>(Owner));
        }
        TStrongObjectPtr<USkeletalMesh> Carrier(FullPose ? DuplicateObject<USkeletalMesh>(Mesh, GetTransientPackage()) : nullptr);
        if (Carrier.IsValid()) { Carrier->ClearFlags(RF_Public | RF_Standalone); Carrier->SetFlags(RF_Transient); }
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
        Component->bUseRefPoseOnInitAnim = true; Component->SetDisablePostProcessBlueprint(true);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision); Component->SetSkeletalMesh(FullPose ? Carrier.Get() : Mesh); Component->SetAnimInstanceClass(MainClass);
        if (RunMain) Component->SetupAttachment(Owner->GetRootComponent()); else Owner->SetRootComponent(Component.Get());
        Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
        auto* Main = Component->GetAnimInstance(); if (!Main || !Component->IsRegistered()) return {};
        Main->LinkAnimClassLayers(LayerClass); auto* Layer = Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
        if (!Layer || Layer->GetClass() != LayerClass || !Bind(Layer, Trace->GetObjectField(TEXT("bindings")), FullPose ? &Transient : nullptr)) return {};
        const auto& Properties = Interface->GetAnimNodeProperties(); const int32 NodeIndex = static_cast<int32>(Trace->GetNumberField(TEXT("nodeIndex")));
        const int32 PropertyIndex = Properties.Num()-1-NodeIndex;
        if (!Properties.IsValidIndex(PropertyIndex) || Properties[PropertyIndex]->Struct != FAnimNode_SequenceEvaluator::StaticStruct()) return {};
        auto& Node = *Properties[PropertyIndex]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
        FPoseLink Link; Link.LinkID = PropertyIndex; Link.SetLinkNode(&Node);
        auto& MainProxy = FInstanceAccess::Proxy(Main); auto& LayerProxy = FInstanceAccess::Proxy(Layer);
        FPoseLink MainLink; FAnimNode_BlendSpacePlayer* Lean = nullptr;
        FPoseLink StopLink; FAnimNode_SequenceEvaluator* StopNode=nullptr;
        FAnimNode_SequenceEvaluator* StopHip=nullptr; FAnimNode_LayeredBoneBlend* StopBlend=nullptr;
        FAnimNode_StateMachine* StopMachine = nullptr; int32 StopMachineProperty = INDEX_NONE;
        if (StopMain || JointStop)
        {
            const auto& MP=MainInterface->GetAnimNodeProperties();
            const int32 R=MP.Num()-1-18, L=MP.Num()-1-19;
            if (!MP.IsValidIndex(R) || !MP.IsValidIndex(L) || MP[R]->Struct!=FAnimNode_StateResult::StaticStruct() ||
                MP[L]->Struct!=FAnimNode_LinkedAnimLayer::StaticStruct()) return {};
            auto* State=MP[R]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);
            auto* Linked=MP[L]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            if (State->Result.LinkID!=L || State->GetStateIndex()!=3 || Linked->Layer!=TEXT("FullBody_StopState") ||
                Linked->GetTargetInstance<UAnimInstance>()!=Layer) return {};
            if (StateRoots && FNodeAccess::UpdateName(*State)!=FName(TEXT("UpdateStopState"))) return {};
            auto& StateLink=StopMain ? MainLink : StopLink;
            StateLink.LinkID=R; StateLink.SetLinkNode(State);
            for (int32 I=0; I<MP.Num(); ++I)
                if (MP[I]->Struct==FAnimNode_StateMachine::StaticStruct())
                {
                    auto* M=MP[I]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Main);
                    if (M->StateMachineIndexInClass==0) { StopMachine=M; StopMachineProperty=I; break; }
                }
            if (!StopMachine) return {};
        }
        if (MainStart)
        {
            const auto& MainProperties = MainInterface->GetAnimNodeProperties();
            const int32 AdditiveIndex = MainProperties.Num()-1-13, LeanIndex = MainProperties.Num()-1-12, BaseIndex = MainProperties.Num()-1-11;
            if (!MainProperties.IsValidIndex(AdditiveIndex) || !MainProperties.IsValidIndex(LeanIndex) || !MainProperties.IsValidIndex(BaseIndex) ||
                MainProperties[AdditiveIndex]->Struct != FAnimNode_ApplyAdditive::StaticStruct() ||
                MainProperties[LeanIndex]->Struct != FAnimNode_BlendSpacePlayer::StaticStruct() ||
                MainProperties[BaseIndex]->Struct != FAnimNode_LinkedAnimLayer::StaticStruct()) return {};
            auto* Additive = MainProperties[AdditiveIndex]->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
            auto* Linked = MainProperties[BaseIndex]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            if (Additive->Base.LinkID != BaseIndex || Additive->Additive.LinkID != LeanIndex ||
                Linked->Layer != TEXT("FullBody_StartState") || Linked->GetTargetInstance<UAnimInstance>() != Layer) return {};
            Lean = MainProperties[LeanIndex]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main); Lean->SetBlendSpace(AdaptedLean.Get());
            MainLink.LinkID = AdditiveIndex; MainLink.SetLinkNode(Additive);
        }
        FAnimNode_SequenceEvaluator* HipFire = nullptr; FAnimNode_LayeredBoneBlend* Blend = nullptr;
        FAnimNode_OrientationWarping* Orientation = nullptr; FAnimNode_StrideWarping* Stride = nullptr;
        FPoseLink JointLink; FAnimNode_SequencePlayer* JointSource = nullptr;
        FAnimNode_SequenceEvaluator* JointHip = nullptr; FAnimNode_LayeredBoneBlend* JointBlend = nullptr;
        FAnimNode_OrientationWarping* JointOrientation = nullptr; FAnimNode_StrideWarping* JointStride = nullptr;
        FAnimNode_BlendSpacePlayer* JointLean = nullptr;
        FGroundPivotProbe PivotProbe;
        if (JointPivot && !PivotProbe.Setup(Main,Layer,EvaluationSkeleton,AdaptedLean.Get(),AssetPath)) return {};
        TStrongObjectPtr<UBlendProfile> AdaptedMask;
        if (FullPose)
        {
            const FName ProviderName=StopSource ? TEXT("FullBody_StopState") : TEXT("FullBody_StartState");
            const int32 RootIndex = LayerRoot(Interface, ProviderName); if (RootIndex < 0) return {};
            Link.LinkID = Properties.Num()-1-RootIndex; Link.SetLinkNode(Properties[Link.LinkID]->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
            TSharedPtr<FJsonObject> Closure;
            if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(
                UAlsLyraGraphLibrary::ReadAnimationLayerGraph(LayerClass, ProviderName)), Closure)) return {};
            for (const auto& Value : Closure->GetArrayField(TEXT("nodes")))
            {
                const auto Entry = Value->AsObject(); const int32 Index = Properties.Num()-1-static_cast<int32>(Entry->GetNumberField(TEXT("index")));
                if (Properties[Index]->Struct == FAnimNode_LayeredBoneBlend::StaticStruct()) Blend = Properties[Index]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
                if (Properties[Index]->Struct == FAnimNode_OrientationWarping::StaticStruct()) Orientation = Properties[Index]->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(Layer);
                if (Properties[Index]->Struct == FAnimNode_StrideWarping::StaticStruct()) Stride = Properties[Index]->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(Layer);
            }
            const int32 HipIndex = Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("hipFireIndex")));
            if (!Properties.IsValidIndex(HipIndex) || Properties[HipIndex]->Struct != FAnimNode_SequenceEvaluator::StaticStruct() ||
                !Blend || Blend->BasePose.LinkID != PropertyIndex || Blend->BlendPoses.Num() != 1 || Blend->BlendPoses[0].LinkID != HipIndex ||
                (!StopSource && (!Orientation || !Stride)) || (StopSource && (Orientation || Stride)) ||
                Blend->BlendMasks.Num() != 1 || !Blend->BlendMasks[0]) return {};
            HipFire = Properties[HipIndex]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
            auto& M = FNodeAccess::Marker(*HipFire); M.PreviousMarker.TimeToMarker = 0; M.NextMarker.TimeToMarker = 0;
            TArray<FBoneReference> Spines;
            for (auto Bone : Orientation ? Orientation->SpineBones : TArray<FBoneReference>())
            {
                if (Bone.BoneName == TEXT("spine_04") || Bone.BoneName == TEXT("spine_05")) Bone.BoneName = TEXT("spine_03");
                if (!Spines.ContainsByPredicate([Bone](const FBoneReference& Existing) { return Existing.BoneName == Bone.BoneName; })) Spines.Add(Bone);
            }
            if (Orientation) Orientation->SpineBones = Spines;
            AdaptedMask.Reset(NewObject<UBlendProfile>(GetTransientPackage()));
            AdaptedMask->OwningSkeleton = EvaluationSkeleton; AdaptedMask->Mode = EBlendProfileMode::BlendMask;
            const auto& Reference = EvaluationSkeleton->GetReferenceSkeleton();
            for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
            {
                float Weight = 0;
                for (const auto& Entry : Blend->BlendMasks[0]->ProfileEntries)
                    if (Entry.BoneReference.BoneName == Reference.GetBoneName(Bone)) { Weight = Entry.BlendScale; break; }
                AdaptedMask->SetBoneBlendScale(Bone, Weight, false, true);
            }
            Blend->BlendMasks[0] = AdaptedMask.Get(); Blend->InvalidatePerBoneBlendWeights();
            if (JointCycle)
            {
                const auto& MP = MainInterface->GetAnimNodeProperties();
                const int32 A = MP.Num()-1-17, L = MP.Num()-1-16, B = MP.Num()-1-15;
                if (!MP.IsValidIndex(A) || !MP.IsValidIndex(L) || !MP.IsValidIndex(B) ||
                    MP[A]->Struct != FAnimNode_ApplyAdditive::StaticStruct() || MP[L]->Struct != FAnimNode_BlendSpacePlayer::StaticStruct() ||
                    MP[B]->Struct != FAnimNode_LinkedAnimLayer::StaticStruct()) return {};
                auto* Add = MP[A]->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
                auto* Linked = MP[B]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
                if (Add->Base.LinkID != B || Add->Additive.LinkID != L || Linked->Layer != TEXT("FullBody_CycleState") ||
                    Linked->GetTargetInstance<UAnimInstance>() != Layer) return {};
                JointLean = MP[L]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main); JointLean->SetBlendSpace(AdaptedLean.Get());
                JointLink.LinkID = A; JointLink.SetLinkNode(Add);
                const int32 S = Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("cycleNodeIndex")));
                const int32 H = Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("cycleHipFireIndex")));
                if (!Properties.IsValidIndex(S) || !Properties.IsValidIndex(H) ||
                    Properties[S]->Struct != FAnimNode_SequencePlayer::StaticStruct() || Properties[H]->Struct != FAnimNode_SequenceEvaluator::StaticStruct()) return {};
                JointSource = Properties[S]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Layer);
                JointHip = Properties[H]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
                TSharedPtr<FJsonObject> CycleClosure;
                if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(UAlsLyraGraphLibrary::ReadCycleLayerGraph(LayerClass)),CycleClosure)) return {};
                for (const auto& V : CycleClosure->GetArrayField(TEXT("nodes")))
                {
                    const int32 I = Properties.Num()-1-static_cast<int32>(V->AsObject()->GetNumberField(TEXT("index")));
                    if (Properties[I]->Struct == FAnimNode_LayeredBoneBlend::StaticStruct()) JointBlend = Properties[I]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
                    if (Properties[I]->Struct == FAnimNode_OrientationWarping::StaticStruct()) JointOrientation = Properties[I]->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(Layer);
                    if (Properties[I]->Struct == FAnimNode_StrideWarping::StaticStruct()) JointStride = Properties[I]->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(Layer);
                }
                if (!JointBlend || !JointOrientation || !JointStride || JointBlend->BasePose.LinkID != S ||
                    JointBlend->BlendPoses.Num()!=1 || JointBlend->BlendPoses[0].LinkID!=H || JointBlend->BlendMasks.Num()!=1 ||
                    !JointBlend->BlendMasks[0]) return {};
                JointBlend->BlendMasks[0] = AdaptedMask.Get(); JointBlend->InvalidatePerBoneBlendWeights();
                JointOrientation->SpineBones = Spines;
                for (auto* Source : {static_cast<FAnimNode_AssetPlayerBase*>(JointSource),static_cast<FAnimNode_AssetPlayerBase*>(JointHip)})
                { auto& JointMarker = FNodeAccess::Marker(*Source); JointMarker.PreviousMarker.TimeToMarker=0; JointMarker.NextMarker.TimeToMarker=0; }
            }
            if (JointStop)
            {
                const int32 S=Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("stopNodeIndex")));
                const int32 H=Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("stopHipFireIndex")));
                if (!Properties.IsValidIndex(S) || !Properties.IsValidIndex(H) ||
                    Properties[S]->Struct!=FAnimNode_SequenceEvaluator::StaticStruct() ||
                    Properties[H]->Struct!=FAnimNode_SequenceEvaluator::StaticStruct()) return {};
                StopNode=Properties[S]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
                StopHip=Properties[H]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
                TSharedPtr<FJsonObject> StopClosure;
                if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(
                    UAlsLyraGraphLibrary::ReadAnimationLayerGraph(LayerClass,TEXT("FullBody_StopState"))),StopClosure) ||
                    StopClosure->GetArrayField(TEXT("nodes")).Num()!=4) return {};
                for (const auto& V : StopClosure->GetArrayField(TEXT("nodes")))
                {
                    const int32 I=Properties.Num()-1-static_cast<int32>(V->AsObject()->GetNumberField(TEXT("index")));
                    if (Properties[I]->Struct==FAnimNode_LayeredBoneBlend::StaticStruct())
                        StopBlend=Properties[I]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
                }
                if (!StopBlend || StopBlend->BasePose.LinkID!=S || StopBlend->BlendPoses.Num()!=1 ||
                    StopBlend->BlendPoses[0].LinkID!=H || StopBlend->BlendMasks.Num()!=1 || !StopBlend->BlendMasks[0]) return {};
                StopBlend->BlendMasks[0]=AdaptedMask.Get(); StopBlend->InvalidatePerBoneBlendWeights();
                for (auto* Source : {static_cast<FAnimNode_AssetPlayerBase*>(StopNode),static_cast<FAnimNode_AssetPlayerBase*>(StopHip)})
                { auto& StopMarker=FNodeAccess::Marker(*Source); StopMarker.PreviousMarker.TimeToMarker=0; StopMarker.NextMarker.TimeToMarker=0; }
            }
            if (StateRoots)
            {
                const auto& MP=MainInterface->GetAnimNodeProperties();
                for (const int32 StateRootIndex : {10,14})
                {
                    const int32 P=MP.Num()-1-StateRootIndex, Child=MP.Num()-1-(StateRootIndex==10 ? 13 : 17);
                    if (!MP.IsValidIndex(P) || MP[P]->Struct!=FAnimNode_StateResult::StaticStruct()) return {};
                    auto* State=MP[P]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);
                    if (State->Result.LinkID!=Child || State->GetStateIndex()!=(StateRootIndex==10 ? 1 : 2) ||
                        FNodeAccess::UpdateName(*State)!=(StateRootIndex==10 ? FName(TEXT("UpdateStartState")) : NAME_None)) return {};
                    auto& RootLink=StateRootIndex==10 ? MainLink : JointLink;
                    RootLink.LinkID=P; RootLink.SetLinkNode(State);
                }
            }
            Carrier->SetSkeleton(EvaluationSkeleton); TArray<FBoneIndexType> Required;
            for (int32 Bone = 0; Bone < 81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
            LayerProxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *EvaluationSkeleton);
            LayerProxy.GetRequiredBones().SetUseRAWData(true); LayerProxy.GetRequiredBones().SetDisableRetargeting(false);
            if (RunMain)
            {
                MainProxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *EvaluationSkeleton);
                MainProxy.GetRequiredBones().SetUseRAWData(true); MainProxy.GetRequiredBones().SetDisableRetargeting(false);
            }
        }
        TStrongObjectPtr<UAnimInstance> SyncInstance(NewObject<UAnimInstance>(Component.Get()));
        FSyncProxy Sync(SyncInstance.Get()); Sync.Initialize(SyncInstance.Get());
        auto& Marker = FNodeAccess::Marker(Node); Marker.PreviousMarker.TimeToMarker = 0; Marker.NextMarker.TimeToMarker = 0;
        auto Scalar = [&](const TCHAR* Name, const TSharedPtr<FJsonObject>& Row) -> bool
        {
            auto* P = FindFProperty<FNumericProperty>(LayerClass, Name); if (!P || !P->IsFloatingPoint()) return false;
            Double(Row, Name, P->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Layer))); return true;
        };
        const auto Policy = MakeShared<FJsonObject>();
        for (const auto* Name : {TEXT("StrideWarpingBlendInStartOffset"), TEXT("StrideWarpingBlendInDurationScaled")}) if (!Scalar(Name, Policy)) return {};
        auto* Clamp = FindFProperty<FStructProperty>(LayerClass, TEXT("PlayRateClampStartsPivots"));
        auto* CurveName = FindFProperty<FNameProperty>(LayerClass, TEXT("LocomotionDistanceCurveName")); if (!Clamp || !CurveName) return {};
        const auto& Range = *Clamp->ContainerPtrToValuePtr<FVector2D>(Layer); Double(Policy, TEXT("clampMin"), Range.X); Double(Policy, TEXT("clampMax"), Range.Y);
        Policy->SetStringField(TEXT("curveName"), CurveName->GetPropertyValue_InContainer(Layer).ToString());
        bool Initialized = false, JointInitialized = false, StopInitialized=false; TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const FMemMark FrameMark(FMemStack::Get());
            const auto Frame = FrameValue->AsObject(); const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            auto StartDirection=[&]() -> int64
            {
                auto* Direction=FindFProperty<FNumericProperty>(MainClass,TEXT("StartDirection"));
                check(Direction); return Direction->GetSignedIntPropertyValue(Direction->ContainerPtrToValuePtr<void>(Main));
            };
            const int64 StartDirectionBefore=GraphHistory ? StartDirection() : 0;
            if (!RunMain) for (const auto& Field : Frame->GetObjectField(TEXT("main"))->Values) if (!Set(Main, *Field.Key, Field.Value)) return {};
            if (FullPose)
            {
                for (const auto& Field : Frame->GetObjectField(TEXT("layer"))->Values) if (!Set(Layer, *Field.Key, Field.Value)) return {};
                const auto R = Frame->GetArrayField(TEXT("relativeRotation"));
                Component->SetRelativeRotation(FQuat(R[0]->AsNumber(), R[1]->AsNumber(), R[2]->AsNumber(), R[3]->AsNumber()));
            }
            TSharedPtr<FJsonObject> Observation;
            if (RunMain)
            {
                const auto ObservationInput=Frame->GetObjectField(TEXT("observation"));
                if (JointStop)
                {
                    auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode")); if (!Mode) return {};
                    // Preserve the actual previous graph callback's mode. This
                    // capture must exercise feedback into the next Main macro.
                    ObservationInput->SetNumberField(TEXT("mode"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
                }
                Observation = LyraMainObservationProbe::Tick(Main, CastChecked<ACharacter>(Owner), MainProxy, MainInterface,
                    ObservationInput, Delta); if (!Observation) return {};
                const auto Transform = Component->GetComponentTransform(); const auto R = Transform.GetRotation(); const auto P = Transform.GetLocation();
                const auto ComponentInput = MakeShared<FJsonObject>();
                ComponentInput->SetArrayField(TEXT("rotation"),{MakeShared<FJsonValueNumber>(R.X),MakeShared<FJsonValueNumber>(R.Y),MakeShared<FJsonValueNumber>(R.Z),MakeShared<FJsonValueNumber>(R.W)});
                ComponentInput->SetArrayField(TEXT("position"),{MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)});
                Observation->SetObjectField(TEXT("componentInput"),ComponentInput);
            }
            else FProxyAccess::Pre(MainProxy, Main, Delta);
            TSharedPtr<FJsonObject> MovementInput;
            if (StopSource || JointStop)
            {
                auto* Movement = CastChecked<ACharacter>(Owner)->GetCharacterMovement();
                const auto Authored = JointStop ? Frame->GetObjectField(TEXT("stop"))->GetObjectField(TEXT("movement")) : Frame->GetObjectField(TEXT("movement"));
                auto* VelocityProperty = FindFProperty<FStructProperty>(Movement->GetClass(), TEXT("LastUpdateVelocity"));
                if (!VelocityProperty || VelocityProperty->Struct != TBaseStructure<FVector>::Get()) return {};
                const auto& V = Authored->GetArrayField(TEXT("lastUpdateVelocity")); if (V.Num()!=3) return {};
                *VelocityProperty->ContainerPtrToValuePtr<FVector>(Movement) = FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber());
                for (const auto& Field : Authored->Values)
                    if (Field.Key != TEXT("lastUpdateVelocity") && !Set(Movement,*Field.Key,Field.Value)) return {};
                MovementInput = MakeShared<FJsonObject>();
                const FVector Velocity = Movement->GetLastUpdateVelocity();
                MovementInput->SetArrayField(TEXT("lastUpdateVelocity"), {MakeShared<FJsonValueNumber>(Velocity.X),MakeShared<FJsonValueNumber>(Velocity.Y),MakeShared<FJsonValueNumber>(Velocity.Z)});
                MovementInput->SetBoolField(TEXT("separate"),Movement->bUseSeparateBrakingFriction);
                Number(MovementInput,TEXT("brakingFriction"),Movement->BrakingFriction);
                Number(MovementInput,TEXT("groundFriction"),Movement->GroundFriction);
                Number(MovementInput,TEXT("factor"),Movement->BrakingFrictionFactor);
                Number(MovementInput,TEXT("deceleration"),Movement->BrakingDecelerationWalking);
                Interface->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& Subsystem)
                {
                    if (Subsystem.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct())
                    {
                        FAnimSubsystemUpdateContext Game(Subsystem,Layer,Delta);
                        Subsystem.Subsystem.OnPreUpdate_GameThread(Game);
                        Subsystem.Subsystem.OnPostUpdate_GameThread(Game);
                        FAnimSubsystemParallelUpdateContext Worker(Subsystem,LayerProxy,Delta);
                        Subsystem.Subsystem.OnPreUpdate_WorkerThread(Worker);
                    }
                    return EAnimSubsystemEnumeration::Continue;
                });
            }
            FProxyAccess::Pre(LayerProxy, Layer, Delta); Sync.PreUpdate(SyncInstance.Get(), Delta);
            FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Root(&Sync, Delta, &Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Root, Root);
            auto* TraversalProxy = RunMain ? &MainProxy : &LayerProxy;
            auto& TraversalLink = RunMain ? MainLink : Link;
            auto Context = FAnimationUpdateContext(TraversalProxy, Delta, &Shared).FractionalWeight(static_cast<float>(Frame->GetNumberField(TEXT("weight"))));
            Context.SetNodeId(StopMain || StateRoots ? StopMachineProperty : MainStart ? 13 : NodeIndex);
            if (StopMain)
            {
                const int32 Current=static_cast<int32>(Frame->GetNumberField(TEXT("machineCurrent")));
                if (Current!=2 && Current!=3) return {};
                FStopMachineAccess::Current(*StopMachine,Current);
                const float Weight=static_cast<float>(Frame->GetNumberField(TEXT("previousStopWeight")));
                MainProxy.RecordStateWeight(0,3,Weight,0); MainProxy.FlipBufferWriteIndex();
                MainProxy.RecordStateWeight(0,3,Weight,0);
                if (MainProxy.GetRecordedStateWeight(0,3)!=Weight) return {};
            }
            if (!Initialized || Frame->GetBoolField(TEXT("reinitialize")))
            { TraversalLink.Initialize(FAnimationInitializeContext(TraversalProxy)); if (FullPose) TraversalLink.CacheBones(FAnimationCacheBonesContext(TraversalProxy)); Initialized = true; }
            TSharedPtr<FJsonObject> CycleRow;
            TSharedPtr<FJsonObject> CycleFrame;
            TSharedPtr<FJsonObject> StopRow;
            TSharedPtr<FJsonObject> StopFrame;
            if (JointStop)
            {
                StopFrame=Frame->GetObjectField(TEXT("stop")); StopRow=MakeShared<FJsonObject>();
                const int32 Current=static_cast<int32>(StopFrame->GetNumberField(TEXT("machineCurrent")));
                if (StateRoots ? Current<0 || Current>11 : Current!=2 && Current!=3) return {};
                FStopMachineAccess::Current(*StopMachine,Current);
                const float W=static_cast<float>(StopFrame->GetNumberField(TEXT("previousStopWeight")));
                MainProxy.RecordStateWeight(0,3,W,0); MainProxy.FlipBufferWriteIndex(); MainProxy.RecordStateWeight(0,3,W,0);
                if (MainProxy.GetRecordedStateWeight(0,3)!=W) return {};
                if (!StopInitialized || StopFrame->GetBoolField(TEXT("reinitialize")))
                { StopLink.Initialize(FAnimationInitializeContext(&MainProxy)); StopLink.CacheBones(FAnimationCacheBonesContext(&MainProxy)); StopInitialized=true; }
                StopRow->SetStringField(TEXT("beforeAsset"),AssetPath(StopNode->GetSequence()));
                Number(StopRow,TEXT("before"),FNodeAccess::Time(*StopNode)); Number(StopRow,TEXT("explicitBefore"),StopNode->GetExplicitTime());
                Number(StopRow,TEXT("hipFireBefore"),FNodeAccess::Time(*StopHip));
            }
            if (StateRoots)
            {
                const auto StateInput=Frame->GetObjectField(TEXT("stateRoots"));
                if (static_cast<int32>(StateInput->GetNumberField(TEXT("current")))!=StopMachine->GetCurrentState()) return {};
                for (int32 Buffer=0; Buffer<2; ++Buffer)
                {
                    MainProxy.RecordStateWeight(0,1,static_cast<float>(StateInput->GetNumberField(TEXT("previousStartWeight"))),0);
                    MainProxy.RecordStateWeight(0,2,static_cast<float>(StateInput->GetNumberField(TEXT("previousCycleWeight"))),0);
                    MainProxy.RecordStateWeight(0,3,static_cast<float>(StateInput->GetNumberField(TEXT("previousStopWeight"))),0);
                    MainProxy.FlipBufferWriteIndex();
                }
            }
            if (JointCycle)
            {
                CycleFrame=Frame->GetObjectField(TEXT("cycle")); CycleRow=MakeShared<FJsonObject>();
                if (!JointInitialized || CycleFrame->GetBoolField(TEXT("reinitialize")))
                { JointLink.Initialize(FAnimationInitializeContext(&MainProxy)); JointLink.CacheBones(FAnimationCacheBonesContext(&MainProxy)); JointInitialized=true; }
                Number(CycleRow,TEXT("before"),JointSource->GetAccumulatedTime());
                CycleRow->SetStringField(TEXT("beforeAsset"),AssetPath(JointSource->GetSequence()));
                Number(CycleRow,TEXT("hipFireBefore"),JointHip->GetAccumulatedTime());
            }
            TSharedPtr<FJsonObject> PivotFrame;
            if(JointPivot) { PivotFrame=Frame->GetObjectField(TEXT("pivot"));PivotProbe.Begin(MainProxy,PivotFrame); }
            const auto Row = MakeShared<FJsonObject>();
            if (StopSource || JointStop)
            {
                const auto StopOutput=JointStop ? StopRow : Row;
                StopOutput->SetObjectField(TEXT("movement"),MovementInput);
                auto* Function = Layer->FindFunction(TEXT("GetPredictedStopDistance")); if (!Function) return {};
                FStructOnScope Parameters(Function); Layer->ProcessEvent(Function,Parameters.GetStructMemory());
                auto* Result = FindFProperty<FDoubleProperty>(Function,TEXT("ReturnValue")); if (!Result) return {};
                Double(StopOutput,TEXT("predictedDistance"),Result->GetPropertyValue_InContainer(Parameters.GetStructMemory()));
                auto* Should = Layer->FindFunction(TEXT("ShouldDistanceMatchStop")); if (!Should) return {};
                FStructOnScope ShouldParameters(Should); Layer->ProcessEvent(Should,ShouldParameters.GetStructMemory());
                auto* ShouldResult = FindFProperty<FBoolProperty>(Should,TEXT("ReturnValue")); if (!ShouldResult) return {};
                StopOutput->SetBoolField(TEXT("shouldMatch"),ShouldResult->GetPropertyValue_InContainer(ShouldParameters.GetStructMemory()));
                for (const auto* Name : {TEXT("HasVelocity"),TEXT("HasAcceleration")})
                { auto* P=FindFProperty<FBoolProperty>(MainClass,Name); if (!P) return {}; StopOutput->SetBoolField(Name,P->GetPropertyValue_InContainer(Main)); }
            }
            if (RunMain) Row->SetObjectField(TEXT("observation"),Observation);
            Row->SetStringField(TEXT("beforeAsset"), AssetPath(Node.GetSequence()));
            Number(Row, TEXT("before"), FNodeAccess::Time(Node)); Number(Row, TEXT("explicitBefore"), Node.GetExplicitTime());
            const bool Active = Frame->GetBoolField(TEXT("active")); Row->SetBoolField(TEXT("active"), Active);
            if (FullPose) Number(Row, TEXT("hipFireBefore"), FNodeAccess::Time(*HipFire));
            TArray<float> Durations; UE::Anim::TScopedGraphMessage<FRequests> Requests(Context, Durations);
            TArray<TSharedPtr<FJsonValue>> StateUpdates;
            if (JointCycle)
            {
                const auto& Order=Frame->GetArrayField(TEXT("order"));
                TSet<int32> Visited;
                if (Order.Num()!=(JointPivot ? 4 : JointStop ? 3 : 2)) return {};
                for (const auto& V : Order)
                {
                    const int32 I=static_cast<int32>(V->AsNumber());
                    if (Visited.Contains(I)) return {}; Visited.Add(I);
                    TSharedPtr<FJsonObject> StateUpdate;
                    const bool StateActive=I==2 ? Active : I==1 ? CycleFrame->GetBoolField(TEXT("active")) : I==0 && JointPivot ? PivotFrame->GetBoolField(TEXT("active")) : I==3 && JointStop && StopFrame->GetBoolField(TEXT("active"));
                    if (StateRoots && StateActive)
                    {
                        auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode")); if (!Mode) return {};
                        StateUpdate=MakeShared<FJsonObject>(); StateUpdate->SetNumberField(TEXT("root"),I);
                        StateUpdate->SetNumberField(TEXT("modeBefore"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
                        if (GraphHistory) StateUpdate->SetNumberField(TEXT("startBefore"),StartDirection());
                    }
                    if (I==2) { if (Active) TraversalLink.Update(Context); }
                    else if (I==1)
                    { if (CycleFrame->GetBoolField(TEXT("active"))) { auto C=FAnimationUpdateContext(&MainProxy,Delta,&Shared).FractionalWeight(static_cast<float>(CycleFrame->GetNumberField(TEXT("weight")))); C.SetNodeId(StateRoots ? StopMachineProperty : 17); JointLink.Update(C); } }
                    else if (I==3 && JointStop)
                    {
                        if (StopFrame->GetBoolField(TEXT("active")))
                        { auto C=FAnimationUpdateContext(&MainProxy,Delta,&Shared).FractionalWeight(static_cast<float>(StopFrame->GetNumberField(TEXT("weight")))); C.SetNodeId(StopMachineProperty); StopLink.Update(C); }
                    }
                    else if (I==0 && JointPivot)
                    { if(PivotFrame->GetBoolField(TEXT("active"))) { auto C=FAnimationUpdateContext(&MainProxy,Delta,&Shared).FractionalWeight(static_cast<float>(PivotFrame->GetNumberField(TEXT("weight"))));C.SetNodeId(StopMachineProperty);PivotProbe.Link.Update(C); } }
                    else return {};
                    if (StateUpdate)
                    {
                        auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode"));
                        StateUpdate->SetNumberField(TEXT("modeAfter"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
                        if (GraphHistory)
                        {
                            StateUpdate->SetNumberField(TEXT("startAfter"),StartDirection());
                            const auto* StartState=MainInterface->GetAnimNodeProperties()[MainInterface->GetAnimNodeProperties().Num()-1-10]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);
                            StateUpdate->SetBoolField(TEXT("startBecameRelevant"),I==2 && Main->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(*StartState).HasJustBecomeRelevant());
                        }
                        StateUpdates.Add(MakeShared<FJsonValueObject>(StateUpdate));
                    }
                }
                Number(CycleRow,TEXT("prepared"),JointSource->GetAccumulatedTime()); Number(CycleRow,TEXT("playRate"),JointSource->GetPlayRate());
                Number(CycleRow,TEXT("cachedWeight"),JointSource->GetCachedBlendWeight());
                CycleRow->SetStringField(TEXT("asset"),AssetPath(JointSource->GetSequence()));
                if (!Scalar(TEXT("StrideWarpingCycleAlpha"),CycleRow)) return {};
            }
            else if (Active) TraversalLink.Update(Context);
            if (StateRoots)
            {
                Row->SetArrayField(TEXT("stateUpdates"),StateUpdates);
                const auto Observed=MakeShared<FJsonObject>(); Observed->SetNumberField(TEXT("current"),StopMachine->GetCurrentState());
                Number(Observed,TEXT("previousStartWeight"),MainProxy.GetRecordedStateWeight(0,1));
                Number(Observed,TEXT("previousCycleWeight"),MainProxy.GetRecordedStateWeight(0,2));
                Number(Observed,TEXT("previousStopWeight"),MainProxy.GetRecordedStateWeight(0,3));
                Row->SetObjectField(TEXT("stateRoots"),Observed);
                if (GraphHistory)
                {
                    Row->SetNumberField(TEXT("startDirectionBeforeGraph"),StartDirectionBefore);
                    Row->SetNumberField(TEXT("startDirectionAfterGraph"),StartDirection());
                }
            }
            if (StopMain || JointStop)
            {
                const auto StopOutput=JointStop ? StopRow : Row;
                auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode")); if (!Mode) return {};
                StopOutput->SetNumberField(TEXT("rootYawModeAfterGraph"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
                StopOutput->SetNumberField(TEXT("machineCurrent"),StopMachine->GetCurrentState());
                Number(StopOutput,TEXT("previousStopWeight"),MainProxy.GetRecordedStateWeight(0,3));
            }
            Number(Row, TEXT("prepared"), FNodeAccess::Time(Node)); Number(Row, TEXT("explicit"), Node.GetExplicitTime());
            Number(Row, TEXT("cachedWeight"), Node.GetCachedBlendWeight());
            if (!Scalar(TEXT("StrideWarpingStartAlpha"), Row)) return {};
            Row->SetStringField(TEXT("asset"), AssetPath(Node.GetSequence()));
            Row->SetBoolField(TEXT("becameRelevant"), Active && Layer->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(Node).HasJustBecomeRelevant());
            if (!RunMain && !Durations.IsEmpty()) return {};
            if (RunMain)
            {
                TArray<TSharedPtr<FJsonValue>> RequestRows;
                for (float Duration : Durations) { const auto R = MakeShared<FJsonObject>(); Number(R,TEXT("duration"),Duration); RequestRows.Add(MakeShared<FJsonValueObject>(R)); }
                Row->SetArrayField(TEXT("inertia"),RequestRows);
            }
            if (JointStop)
            {
                Number(StopRow,TEXT("prepared"),FNodeAccess::Time(*StopNode)); Number(StopRow,TEXT("explicit"),StopNode->GetExplicitTime());
                Number(StopRow,TEXT("cachedWeight"),StopNode->GetCachedBlendWeight()); StopRow->SetStringField(TEXT("asset"),AssetPath(StopNode->GetSequence()));
                StopRow->SetBoolField(TEXT("becameRelevant"),StopFrame->GetBoolField(TEXT("active")) &&
                    Layer->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(*StopNode).HasJustBecomeRelevant());
            }
            if(JointPivot) PivotProbe.Prepared();
            Sync.UpdateAnimation(); Sync.PostUpdate(SyncInstance.Get());
            if(JointPivot)
            { if(!PivotProbe.Finish(Sync,MainProxy,EvaluationSkeleton)) return {};Row->SetObjectField(TEXT("pivot"),PivotProbe.Row); }
            if (JointStop)
            {
                const bool StopActive=StopFrame->GetBoolField(TEXT("active")); StopRow->SetBoolField(TEXT("active"),StopActive);
                Number(StopRow,TEXT("time"),FNodeAccess::Time(*StopNode));
                const auto& SD=FNodeAccess::Delta(*StopNode); const auto& SM=FNodeAccess::Marker(*StopNode);
                Number(StopRow,TEXT("previous"),SD.GetPrevious()); Number(StopRow,TEXT("delta"),SD.Delta);
                StopRow->SetNumberField(TEXT("markerPrevious"),SM.PreviousMarker.MarkerIndex); StopRow->SetNumberField(TEXT("markerNext"),SM.NextMarker.MarkerIndex);
                Number(StopRow,TEXT("markerPreviousDistance"),SM.PreviousMarker.MarkerIndex==-2?0:SM.PreviousMarker.TimeToMarker);
                Number(StopRow,TEXT("markerNextDistance"),SM.NextMarker.MarkerIndex==-2?0:SM.NextMarker.TimeToMarker);
                Number(StopRow,TEXT("blendWeight"),StopBlend->BlendWeights[0]); Number(StopRow,TEXT("hipFireWeight"),StopHip->GetCachedBlendWeight());
                StopRow->SetBoolField(TEXT("hipFireActive"),StopActive && FAnimWeight::IsRelevant(StopBlend->BlendWeights[0]));
                StopRow->SetStringField(TEXT("hipFireAsset"),AssetPath(StopHip->GetSequence())); Number(StopRow,TEXT("hipFireTime"),FNodeAccess::Time(*StopHip));
                const auto& HD=FNodeAccess::Delta(*StopHip); const auto& HM=FNodeAccess::Marker(*StopHip);
                Number(StopRow,TEXT("hipFirePrevious"),HD.GetPrevious()); Number(StopRow,TEXT("hipFireDelta"),HD.Delta);
                StopRow->SetNumberField(TEXT("hipFireMarkerPrevious"),HM.PreviousMarker.MarkerIndex); StopRow->SetNumberField(TEXT("hipFireMarkerNext"),HM.NextMarker.MarkerIndex);
                Number(StopRow,TEXT("hipFireMarkerPreviousDistance"),HM.PreviousMarker.MarkerIndex==-2?0:HM.PreviousMarker.TimeToMarker);
                Number(StopRow,TEXT("hipFireMarkerNextDistance"),HM.NextMarker.MarkerIndex==-2?0:HM.NextMarker.TimeToMarker);
                if (StopActive)
                {
                    const auto* Group=Sync.GetSyncGroupMapRead().Find(TEXT("Stop"));
                    if (!Group || Group->ActivePlayers.Num()!=1 || Group->ActivePlayers[0].SourceAsset!=StopNode->GetSequence()) return {};
                    Number(StopRow,TEXT("rate"),Group->ActivePlayers[0].PlayRateMultiplier);
                    FPoseContext Pose(&MainProxy); StopLink.Evaluate(Pose);
                    const auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,EvaluationSkeleton->GetReferenceSkeleton());
                    if (!Output) return {}; StopRow->SetObjectField(TEXT("output"),Output);
                }
                Row->SetObjectField(TEXT("stop"),StopRow);
            }
            if (JointCycle)
            {
                const bool CycleActive=CycleFrame->GetBoolField(TEXT("active"));
                CycleRow->SetBoolField(TEXT("active"),CycleActive);
                Number(CycleRow,TEXT("time"),JointSource->GetAccumulatedTime());
                const auto& CD=FNodeAccess::Delta(*JointSource); const auto& CM=FNodeAccess::Marker(*JointSource);
                Number(CycleRow,TEXT("previous"),CD.GetPrevious()); Number(CycleRow,TEXT("delta"),CD.Delta);
                CycleRow->SetNumberField(TEXT("markerPrevious"),CM.PreviousMarker.MarkerIndex); CycleRow->SetNumberField(TEXT("markerNext"),CM.NextMarker.MarkerIndex);
                Number(CycleRow,TEXT("markerPreviousDistance"),CM.PreviousMarker.MarkerIndex==-2?0:CM.PreviousMarker.TimeToMarker);
                Number(CycleRow,TEXT("markerNextDistance"),CM.NextMarker.MarkerIndex==-2?0:CM.NextMarker.TimeToMarker);
                Number(CycleRow,TEXT("blendWeight"),JointBlend->BlendWeights[0]); Number(CycleRow,TEXT("hipFireWeight"),JointHip->GetCachedBlendWeight());
                CycleRow->SetBoolField(TEXT("hipFireActive"),CycleActive && FAnimWeight::IsRelevant(JointBlend->BlendWeights[0]));
                CycleRow->SetStringField(TEXT("hipFireAsset"),AssetPath(JointHip->GetSequence()));
                Number(CycleRow,TEXT("hipFireTime"),JointHip->GetAccumulatedTime());
                const auto& HD=FNodeAccess::Delta(*JointHip); Number(CycleRow,TEXT("hipFirePrevious"),HD.GetPrevious()); Number(CycleRow,TEXT("hipFireDelta"),HD.Delta);
                Number(CycleRow,TEXT("orientationAngle"),JointOrientation->LocomotionAngle); Number(CycleRow,TEXT("strideSpeed"),JointStride->LocomotionSpeed); Number(CycleRow,TEXT("strideNodeAlpha"),JointStride->GetAlpha());
                const auto L=MakeShared<FJsonObject>(); const auto& LD=FNodeAccess::Delta(*JointLean);
                Number(L,TEXT("time"),JointLean->GetAccumulatedTime()); Number(L,TEXT("pin"),JointLean->GetPosition().X);
                Number(L,TEXT("cachedWeight"),JointLean->GetCachedBlendWeight()); Number(L,TEXT("previous"),LD.GetPrevious()); Number(L,TEXT("delta"),LD.Delta);
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const auto& S : FLeanAccess::Samples(*JointLean))
                {
                    const auto V=MakeShared<FJsonObject>(); V->SetNumberField(TEXT("index"),S.SampleDataIndex);
                    Number(V,TEXT("weight"),S.TotalWeight); Number(V,TEXT("weightRate"),S.WeightRate);
                    PRAGMA_DISABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("time"),S.Time); Number(V,TEXT("previous"),S.PreviousTime);
                    PRAGMA_ENABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("deltaPrevious"),S.DeltaTimeRecord.GetPrevious()); Number(V,TEXT("delta"),S.DeltaTimeRecord.Delta); Samples.Add(MakeShared<FJsonValueObject>(V));
                }
                L->SetArrayField(TEXT("samples"),Samples); CycleRow->SetObjectField(TEXT("lean"),L);
                if (CycleActive)
                { FPoseContext Pose(&MainProxy); JointLink.Evaluate(Pose); const auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,EvaluationSkeleton->GetReferenceSkeleton()); if (!Output) return {}; CycleRow->SetObjectField(TEXT("output"),Output); }
                Row->SetObjectField(TEXT("cycle"),CycleRow);
            }
            if (MainStart)
            {
                const auto L = MakeShared<FJsonObject>(); const auto& D = FNodeAccess::Delta(*Lean);
                Number(L,TEXT("time"),Lean->GetAccumulatedTime()); Number(L,TEXT("pin"),Lean->GetPosition().X);
                Number(L,TEXT("cachedWeight"),Lean->GetCachedBlendWeight()); Number(L,TEXT("previous"),D.GetPrevious()); Number(L,TEXT("delta"),D.Delta);
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const auto& S : FLeanAccess::Samples(*Lean))
                {
                    const auto V = MakeShared<FJsonObject>(); V->SetNumberField(TEXT("index"),S.SampleDataIndex);
                    Number(V,TEXT("weight"),S.TotalWeight); Number(V,TEXT("weightRate"),S.WeightRate);
                    PRAGMA_DISABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("time"),S.Time); Number(V,TEXT("previous"),S.PreviousTime);
                    PRAGMA_ENABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("deltaPrevious"),S.DeltaTimeRecord.GetPrevious()); Number(V,TEXT("delta"),S.DeltaTimeRecord.Delta);
                    Samples.Add(MakeShared<FJsonValueObject>(V));
                }
                L->SetArrayField(TEXT("samples"),Samples); Row->SetObjectField(TEXT("lean"),L);
            }
            if (FullPose)
            {
                Number(Row, TEXT("blendWeight"), Blend->BlendWeights[0]);
                Number(Row, TEXT("hipFireWeight"), HipFire->GetCachedBlendWeight());
                Row->SetBoolField(TEXT("hipFireActive"), Active && FAnimWeight::IsRelevant(Blend->BlendWeights[0]));
                Row->SetStringField(TEXT("hipFireAsset"), AssetPath(HipFire->GetSequence()));
                Number(Row, TEXT("hipFireTime"), FNodeAccess::Time(*HipFire));
                const auto& HD = FNodeAccess::Delta(*HipFire); const auto& HM = FNodeAccess::Marker(*HipFire);
                Number(Row, TEXT("hipFirePrevious"), HD.GetPrevious()); Number(Row, TEXT("hipFireDelta"), HD.Delta);
                Row->SetNumberField(TEXT("hipFireMarkerPrevious"), HM.PreviousMarker.MarkerIndex); Row->SetNumberField(TEXT("hipFireMarkerNext"), HM.NextMarker.MarkerIndex);
                Number(Row, TEXT("hipFireMarkerPreviousDistance"), HM.PreviousMarker.MarkerIndex == -2 ? 0 : HM.PreviousMarker.TimeToMarker);
                Number(Row, TEXT("hipFireMarkerNextDistance"), HM.NextMarker.MarkerIndex == -2 ? 0 : HM.NextMarker.TimeToMarker);
                if (Orientation) { Number(Row, TEXT("orientationAngle"), Orientation->LocomotionAngle); Number(Row, TEXT("orientationAlpha"), Orientation->GetAlpha()); }
                if (Stride) { Number(Row, TEXT("strideSpeed"), Stride->LocomotionSpeed); Number(Row, TEXT("strideNodeAlpha"), Stride->GetAlpha()); }
                if (Active)
                {
                    FPoseContext Pose(TraversalProxy); TraversalLink.Evaluate(Pose);
                    const auto Output = LyraCyclePoseProbe::PoseData(Pose.Pose, Pose.Curve, Pose.CustomAttributes, EvaluationSkeleton->GetReferenceSkeleton());
                    if (!Output) return {}; Row->SetObjectField(TEXT("output"), Output);
                }
            }
            Number(Row, TEXT("time"), FNodeAccess::Time(Node)); const auto& D = FNodeAccess::Delta(Node);
            double ProbeFrame = -1;
            if (Trace->TryGetNumberField(TEXT("probeFrame"), ProbeFrame) && Frames.Num() == static_cast<int32>(ProbeFrame))
            {
                auto* Sequence = Cast<UAnimSequence>(Node.GetSequence()); if (!Sequence) return {};
                float T = static_cast<float>(Row->GetNumberField(TEXT("explicitBefore"))); TArray<TSharedPtr<FJsonValue>> Values;
                const FName Curve = CurveName->GetPropertyValue_InContainer(Layer);
                for (int32 I = 0; I < 3; ++I)
                {
                    const auto V = MakeShared<FJsonObject>(); Number(V, TEXT("time"), T);
                    Number(V, TEXT("value"), Sequence->EvaluateCurveData(Curve, FAnimExtractContext(static_cast<double>(T))));
                    Values.Add(MakeShared<FJsonValueObject>(V)); T += 1.f/30.f;
                }
                Row->SetArrayField(TEXT("distanceProbe"), Values); Number(Row, TEXT("contextDelta"), Context.GetDeltaTime());
            }
            Number(Row, TEXT("previous"), D.GetPrevious()); Number(Row, TEXT("delta"), D.Delta);
            Row->SetNumberField(TEXT("markerPrevious"), Marker.PreviousMarker.MarkerIndex); Row->SetNumberField(TEXT("markerNext"), Marker.NextMarker.MarkerIndex);
            Number(Row, TEXT("markerPreviousDistance"), Marker.PreviousMarker.MarkerIndex == -2 ? 0 : Marker.PreviousMarker.TimeToMarker);
            Number(Row, TEXT("markerNextDistance"), Marker.NextMarker.MarkerIndex == -2 ? 0 : Marker.NextMarker.TimeToMarker);
            if (Active)
            {
                const auto* Group = Sync.GetSyncGroupMapRead().Find(StopSource ? TEXT("Stop") : TEXT("Locomotion"));
                int32 Expected=JointCycle && CycleFrame->GetBoolField(TEXT("active")) ? 2 : 1;
                if(JointPivot) for(const auto& Source : PivotProbe.Row->GetArrayField(TEXT("sources"))) Expected+=Source->AsObject()->GetBoolField(TEXT("tickRegistered")) ? 1 : 0;
                if (!Group || Group->ActivePlayers.Num() != Expected) return {};
                const auto* Tick=Group->ActivePlayers.FindByPredicate([&Node](const FAnimTickRecord& R){return R.SourceAsset==Node.GetSequence();});
                if (!Tick) return {}; Number(Row, TEXT("rate"),Tick->PlayRateMultiplier);
            }
            if (JointPivot)
            {
                for (const auto& Pair : {TPair<TSharedPtr<FJsonObject>,FAnimNode_SequenceEvaluator*>(Row,HipFire),
                    TPair<TSharedPtr<FJsonObject>,FAnimNode_SequenceEvaluator*>(CycleRow,JointHip),
                    TPair<TSharedPtr<FJsonObject>,FAnimNode_SequenceEvaluator*>(StopRow,StopHip)})
                { const auto H=MakeShared<FJsonObject>();FGroundPivotProbe::Clock(*Pair.Value,H);Pair.Key->SetObjectField(TEXT("hipClock"),H); }
            }
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("profile"), Trace->GetStringField(TEXT("profile")));
        Result->SetNumberField(TEXT("hz"), Trace->GetNumberField(TEXT("hz"))); Result->SetObjectField(TEXT("policy"), Policy); Result->SetArrayField(TEXT("frames"), Frames);
        if (StateRoots)
        {
            TArray<TSharedPtr<FJsonValue>> Bindings; const auto& MP=MainInterface->GetAnimNodeProperties();
            for (const int32 RootIndex : {10,14,18})
            {
                const auto* State=MP[MP.Num()-1-RootIndex]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);
                const auto Binding=MakeShared<FJsonObject>(); Binding->SetNumberField(TEXT("node"),RootIndex);
                Binding->SetNumberField(TEXT("state"),State->GetStateIndex());
                Binding->SetStringField(TEXT("update"),FNodeAccess::UpdateName(*State).ToString());
                if (GraphHistory) Binding->SetStringField(TEXT("becomeRelevant"),FNodeAccess::FunctionName(*State,TEXT("BecomeRelevantFunction")).ToString());
                Binding->SetStringField(TEXT("entry"),State->GetStateEntryFunction().GetFunctionName().ToString());
                Binding->SetStringField(TEXT("exit"),State->GetStateExitFunction().GetFunctionName().ToString());
                Bindings.Add(MakeShared<FJsonValueObject>(Binding));
            }
            Result->SetArrayField(TEXT("stateRootBindings"),Bindings);
        }
        Traces.Add(MakeShared<FJsonValueObject>(Result)); Component->UnregisterComponent();
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadStartSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadStartTrace(MainClass, Mesh, RequestsJson); }

FString UAlsLyraGraphLibrary::ReadStopSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,nullptr,nullptr,nullptr,nullptr,false,true); }

static FString ReadPivotTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson, bool RunMachine,
    USkeleton* EvaluationSkeleton=nullptr, const TArray<UAnimSequence*>* EvaluationSequences=nullptr,
    UBlendSpace* LeanSpace=nullptr, const TArray<UAnimSequence*>* LeanSequences=nullptr)
{
    using namespace LyraCycleProbe;
    TSharedPtr<FJsonObject> Input;
    if (!MainClass || !Mesh || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input)) return {};
    const bool FullPose=EvaluationSkeleton!=nullptr;
    const bool RunMain=LeanSpace!=nullptr;
    auto CaptureFailure=[RunMain](int32 Line)->FString
    { if (RunMain) UE_LOG(LogTemp,Display,TEXT("LYRA_MAIN_PIVOT_CAPTURE_REJECT line=%d"),Line); return {}; };
    const auto* MainInterface=IAnimClassInterface::GetFromClass(MainClass);
    TStrongObjectPtr<UBlendSpace> AdaptedLean;
    if (RunMain)
    {
        if (!FullPose || !RunMachine || !MainInterface || !LeanSequences || LeanSequences->Num()!=3 || LeanSpace->GetBlendSamples().Num()!=3) return CaptureFailure(__LINE__);
        AdaptedLean.Reset(DuplicateObject<UBlendSpace>(LeanSpace,GetTransientPackage()));
        AdaptedLean->ClearFlags(RF_Public|RF_Standalone); AdaptedLean->SetFlags(RF_Transient);
        for (int32 I=0; I<3; ++I)
            if (!(*LeanSequences)[I] || (*LeanSequences)[I]->GetSkeleton()!=EvaluationSkeleton ||
                !AdaptedLean->ReplaceSampleAnimation(I,(*LeanSequences)[I])) return CaptureFailure(__LINE__);
        AdaptedLean->SetSkeleton(EvaluationSkeleton); AdaptedLean->ValidateSampleData(); AdaptedLean->ResampleData();
    }
    TMap<FString,UAnimSequence*> Transient; TMap<const UAnimSequence*,FString> Paths;
    if (FullPose)
    {
        if (!RunMachine || !EvaluationSequences || EvaluationSkeleton->GetReferenceSkeleton().GetNum()!=81) return CaptureFailure(__LINE__);
        const auto& Names=Input->GetArrayField(TEXT("sequencePaths")); if (Names.Num()!=EvaluationSequences->Num()) return CaptureFailure(__LINE__);
        for (int32 I=0; I<Names.Num(); ++I)
        {
            auto* Sequence=(*EvaluationSequences)[I];
            if (!Sequence || Sequence->GetSkeleton()!=EvaluationSkeleton || Sequence->IsValidAdditive()) return CaptureFailure(__LINE__);
            Sequence->WaitOnExistingCompression(true); Transient.Add(Names[I]->AsString(),Sequence); Paths.Add(Sequence,Names[I]->AsString());
        }
        FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
    }
    auto AssetPath=[&Paths,FullPose](const UAnimSequenceBase* Sequence)->FString
    { return Sequence ? (FullPose ? Paths.FindRef(Cast<UAnimSequence>(Sequence)) : Sequence->GetPathName()) : TEXT(""); };
    auto VectorField=[](UObject* Object,const TCHAR* Name)->FVector*
    {
        auto* P=FindFProperty<FStructProperty>(Object->GetClass(),Name);
        return P && P->Struct==TBaseStructure<FVector>::Get() ? P->ContainerPtrToValuePtr<FVector>(Object) : nullptr;
    };
    auto VectorValue=[](const TArray<TSharedPtr<FJsonValue>>& V)->FVector
    { check(V.Num()==3); return FVector(V[0]->AsNumber(),V[1]->AsNumber(),V[2]->AsNumber()); };
    auto SetFields=[&](UObject* Object,const TSharedPtr<FJsonObject>& Fields)->bool
    {
        for (const auto& Field : Fields->Values)
            if (auto* V=VectorField(Object,*Field.Key)) *V=VectorValue(Field.Value->AsArray());
            else if (!Set(Object,*Field.Key,Field.Value)) return false;
        return true;
    };
    auto Scalar=[](UObject* Object,const TCHAR* Name,const TSharedPtr<FJsonObject>& Row)->bool
    {
        auto* P=FindFProperty<FNumericProperty>(Object->GetClass(),Name);
        if (!P || !P->IsFloatingPoint()) return false;
        Double(Row,Name,P->GetFloatingPointPropertyValue(P->ContainerPtrToValuePtr<void>(Object))); return true;
    };
    auto WriteVector=[](const TSharedPtr<FJsonObject>& Row,const TCHAR* Name,const FVector& V)
    {
        const auto R=MakeShared<FJsonObject>(); Double(R,TEXT("x"),V.X); Double(R,TEXT("y"),V.Y); Double(R,TEXT("z"),V.Z);
        Row->SetObjectField(Name,R);
    };
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Input->GetArrayField(TEXT("traces")))
    {
        const auto Trace=TraceValue->AsObject(); auto* LayerClass=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));
        const auto* Interface=LayerClass ? IAnimClassInterface::GetFromClass(LayerClass) : nullptr; if (!Interface) return CaptureFailure(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
        if (!World.IsValid()) return CaptureFailure(__LINE__);
        struct FCleanup { UWorld* W; ~FCleanup(){ W->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=World->SpawnActor<ACharacter>(Spawn); if (!Owner) return CaptureFailure(__LINE__);
        if (RunMain)
        {
            auto* Controller=World->SpawnActor<APlayerController>(Spawn); if (!Controller) return CaptureFailure(__LINE__);
            Controller->Possess(Owner);
        }
        TStrongObjectPtr<USkeletalMesh> Carrier(FullPose ? DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()) : nullptr);
        if (Carrier.IsValid()) { Carrier->ClearFlags(RF_Public|RF_Standalone); Carrier->SetFlags(RF_Transient); }
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        Component->bUseRefPoseOnInitAnim=true; Component->SetDisablePostProcessBlueprint(true);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision); Component->SetSkeletalMesh(FullPose ? Carrier.Get() : Mesh); Component->SetAnimInstanceClass(MainClass);
        Component->SetupAttachment(Owner->GetRootComponent()); Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
        auto* Main=Component->GetAnimInstance(); if (!Main) return CaptureFailure(__LINE__);
        Main->LinkAnimClassLayers(LayerClass); auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
        if (!Layer || Layer->GetClass()!=LayerClass || !Bind(Layer,Trace->GetObjectField(TEXT("bindings")),FullPose ? &Transient : nullptr)) return CaptureFailure(__LINE__);
        auto& MainProxy=FInstanceAccess::Proxy(Main); auto& LayerProxy=FInstanceAccess::Proxy(Layer);
        const auto& Properties=Interface->GetAnimNodeProperties(); const auto& Indices=Trace->GetArrayField(TEXT("nodeIndices"));
        if (Indices.Num()!=2) return CaptureFailure(__LINE__);
        FAnimNode_SequenceEvaluator* Nodes[2]; FPoseLink Links[2]; bool Initialized[2]={false,false};
        for (int32 I=0; I<2; ++I)
        {
            const int32 Index=static_cast<int32>(Indices[I]->AsNumber()), Property=Properties.Num()-1-Index;
            if (!Properties.IsValidIndex(Property) || Properties[Property]->Struct!=FAnimNode_SequenceEvaluator::StaticStruct()) return CaptureFailure(__LINE__);
            Nodes[I]=Properties[Property]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
            if (FNodeAccess::UpdateName(*Nodes[I])!=TEXT("UpdatePivotAnim") ||
                FNodeAccess::FunctionName(*Nodes[I],TEXT("BecomeRelevantFunction"))!=TEXT("SetUpPivotAnim")) return CaptureFailure(__LINE__);
            Links[I].LinkID=Property; Links[I].SetLinkNode(Nodes[I]);
            auto& Marker=FNodeAccess::Marker(*Nodes[I]); Marker.PreviousMarker.TimeToMarker=0; Marker.NextMarker.TimeToMarker=0;
        }
        const int32 RuleIndex=static_cast<int32>(Trace->GetNumberField(TEXT("ruleIndex"))), RuleProperty=Properties.Num()-1-RuleIndex;
        if (!Properties.IsValidIndex(RuleProperty) || Properties[RuleProperty]->Struct!=FAnimNode_TransitionResult::StaticStruct()) return CaptureFailure(__LINE__);
        auto* Rule=Properties[RuleProperty]->ContainerPtrToValuePtr<FAnimNode_TransitionResult>(Layer);
        struct FVisitTap : FAnimNode_Base
        {
            FPoseLink Child; int32 Initializations=0, Visits=0; float Weight=0; bool Inertial=false;
            TSharedPtr<FJsonObject> Output; const FReferenceSkeleton* Reference=nullptr;
            TFunction<void(const FAnimationUpdateContext&)> BeforeUpdate;
            virtual void Initialize_AnyThread(const FAnimationInitializeContext& Context) override
            { ++Initializations; Child.Initialize(Context); }
            virtual void CacheBones_AnyThread(const FAnimationCacheBonesContext& Context) override { Child.CacheBones(Context); }
            virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
            { ++Visits; Weight=Context.GetFinalBlendWeight(); Inertial=Context.GetMessage<UE::Anim::FAnimInertializationSyncScope>()!=nullptr;
              if (BeforeUpdate) BeforeUpdate(Context); Child.Update(Context); }
            virtual void Evaluate_AnyThread(FPoseContext& Pose) override
            { Child.Evaluate(Pose); if (Reference) Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,*Reference); }
        } Taps[2];
        // Restore transient instrumentation before the component/world and
        // stack taps are released, including early returns during capture.
        struct FRestoreStateLinks
        {
            FAnimNode_StateResult* States[2]={nullptr,nullptr}; FPoseLink Original[2];
            ~FRestoreStateLinks() { for (int32 I=0; I<2; ++I) if (States[I]) States[I]->Result=Original[I]; }
        } Restore;
        FAnimNode_StateMachine* Machine=nullptr; FPoseLink MachineLink; bool MachineInitialized=false;
        if (RunMachine)
        {
            const int32 Index=static_cast<int32>(Trace->GetNumberField(TEXT("machineNode"))), Property=Properties.Num()-1-Index;
            if (!Properties.IsValidIndex(Property) || Properties[Property]->Struct!=FAnimNode_StateMachine::StaticStruct()) return CaptureFailure(__LINE__);
            Machine=Properties[Property]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Layer);
            Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(Interface));
            if (Machine->StateMachineIndexInClass!=3) return CaptureFailure(__LINE__);
            const auto& Definition=Interface->GetBakedStateMachines()[3]; if (Definition.States.Num()!=2) return CaptureFailure(__LINE__);
            for (int32 I=0; I<2; ++I)
            {
                const int32 RootProperty=Properties.Num()-1-Definition.States[I].StateRootNodeIndex;
                if (!Properties.IsValidIndex(RootProperty) || Properties[RootProperty]->Struct!=FAnimNode_StateResult::StaticStruct()) return CaptureFailure(__LINE__);
                auto* State=Properties[RootProperty]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Layer);
                Restore.States[I]=State; Restore.Original[I]=State->Result;
                Taps[I].Child=State->Result; State->Result.SetLinkNode(&Taps[I]);
            }
            MachineLink.LinkID=Property; MachineLink.SetLinkNode(Machine);
        }
        FPoseLink ProviderLink; FAnimNode_LayeredBoneBlend* Blend=nullptr; FAnimNode_SequenceEvaluator* HipFire=nullptr;
        FAnimNode_OrientationWarping* Orientations[2]={nullptr,nullptr}; FAnimNode_StrideWarping* Strides[2]={nullptr,nullptr};
        TStrongObjectPtr<UBlendProfile> AdaptedMask;
        if (FullPose)
        {
            const int32 Root=LayerRoot(Interface,TEXT("FullBody_PivotState")); if (Root<0) return CaptureFailure(__LINE__);
            ProviderLink.LinkID=Properties.Num()-1-Root; ProviderLink.SetLinkNode(Properties[ProviderLink.LinkID]->ContainerPtrToValuePtr<FAnimNode_Base>(Layer));
            const int32 B=Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("blendNode")));
            const int32 H=Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("hipFireIndex")));
            if (!Properties.IsValidIndex(B) || !Properties.IsValidIndex(H) ||
                Properties[B]->Struct!=FAnimNode_LayeredBoneBlend::StaticStruct() || Properties[H]->Struct!=FAnimNode_SequenceEvaluator::StaticStruct()) return CaptureFailure(__LINE__);
            Blend=Properties[B]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer); HipFire=Properties[H]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
            if (Blend->BlendPoses.Num()!=1 || Blend->BlendPoses[0].LinkID!=H || Blend->bUpdateBasePoseFirst ||
                Blend->BasePose.LinkID!=Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("machineNode"))) ||
                Blend->BlendMasks.Num()!=1 || !Blend->BlendMasks[0]) return CaptureFailure(__LINE__);
            AdaptedMask.Reset(NewObject<UBlendProfile>(GetTransientPackage()));
            AdaptedMask->OwningSkeleton=EvaluationSkeleton; AdaptedMask->Mode=EBlendProfileMode::BlendMask;
            const auto& Reference=EvaluationSkeleton->GetReferenceSkeleton();
            for (int32 Bone=0; Bone<Reference.GetNum(); ++Bone)
            {
                float Weight=0; for (const auto& Entry : Blend->BlendMasks[0]->ProfileEntries)
                    if (Entry.BoneReference.BoneName==Reference.GetBoneName(Bone)) { Weight=Entry.BlendScale; break; }
                AdaptedMask->SetBoneBlendScale(Bone,Weight,false,true);
            }
            Blend->BlendMasks[0]=AdaptedMask.Get(); Blend->InvalidatePerBoneBlendWeights();
            auto& HM=FNodeAccess::Marker(*HipFire); HM.PreviousMarker.TimeToMarker=0; HM.NextMarker.TimeToMarker=0;
            for (int32 I=0; I<2; ++I)
            {
                const int32 O=Properties.Num()-1-static_cast<int32>(Trace->GetArrayField(TEXT("orientationNodes"))[I]->AsNumber());
                const int32 S=Properties.Num()-1-static_cast<int32>(Trace->GetArrayField(TEXT("strideNodes"))[I]->AsNumber());
                if (!Properties.IsValidIndex(O) || !Properties.IsValidIndex(S) || Properties[O]->Struct!=FAnimNode_OrientationWarping::StaticStruct() ||
                    Properties[S]->Struct!=FAnimNode_StrideWarping::StaticStruct()) return CaptureFailure(__LINE__);
                Orientations[I]=Properties[O]->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(Layer);
                Strides[I]=Properties[S]->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(Layer);
                TArray<FBoneReference> Spines;
                for (auto Bone : Orientations[I]->SpineBones)
                {
                    if (Bone.BoneName==TEXT("spine_04") || Bone.BoneName==TEXT("spine_05")) Bone.BoneName=TEXT("spine_03");
                    if (!Spines.ContainsByPredicate([Bone](const FBoneReference& Other){ return Other.BoneName==Bone.BoneName; })) Spines.Add(Bone);
                }
                Orientations[I]->SpineBones=Spines; Taps[I].Reference=&Reference;
            }
            Carrier->SetSkeleton(EvaluationSkeleton); TArray<FBoneIndexType> Required;
            for (int32 Bone=0; Bone<81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
            LayerProxy.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*EvaluationSkeleton);
            LayerProxy.GetRequiredBones().SetUseRAWData(true); LayerProxy.GetRequiredBones().SetDisableRetargeting(false);
        }
        FPoseLink MainLink; FAnimNode_StateResult* MainState=nullptr; FAnimNode_BlendSpacePlayer* MainLean=nullptr;
        int32 MainMachineProperty=INDEX_NONE; FVisitTap MainTap;
        struct FRestoreMainLink
        {
            FAnimNode_StateResult* State=nullptr; FPoseLink Original;
            ~FRestoreMainLink() { if (State) State->Result=Original; }
        } RestoreMain;
        if (RunMain)
        {
            const auto& MP=MainInterface->GetAnimNodeProperties();
            const int32 S=MP.Num()-1-20, A=MP.Num()-1-23, L=MP.Num()-1-21, B=MP.Num()-1-22;
            if (!MP.IsValidIndex(S) || !MP.IsValidIndex(A) || !MP.IsValidIndex(L) || !MP.IsValidIndex(B) ||
                MP[S]->Struct!=FAnimNode_StateResult::StaticStruct() || MP[A]->Struct!=FAnimNode_ApplyAdditive::StaticStruct() ||
                MP[L]->Struct!=FAnimNode_LinkedAnimLayer::StaticStruct() || MP[B]->Struct!=FAnimNode_BlendSpacePlayer::StaticStruct()) return CaptureFailure(__LINE__);
            MainState=MP[S]->ContainerPtrToValuePtr<FAnimNode_StateResult>(Main);
            auto* Add=MP[A]->ContainerPtrToValuePtr<FAnimNode_ApplyAdditive>(Main);
            auto* Linked=MP[L]->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            if (MainState->Result.LinkID!=A || MainState->GetStateIndex()!=4 || Add->Base.LinkID!=L || Add->Additive.LinkID!=B ||
                Linked->Layer!=TEXT("FullBody_PivotState") || Linked->GetTargetInstance<UAnimInstance>()!=Layer ||
                FNodeAccess::UpdateName(*MainState)!=TEXT("UpdatePivotState") ||
                FNodeAccess::FunctionName(*MainState,TEXT("BecomeRelevantFunction"))!=TEXT("SetUpPivotState")) return CaptureFailure(__LINE__);
            MainLean=MP[B]->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(Main); MainLean->SetBlendSpace(AdaptedLean.Get());
            MainLink.LinkID=S; MainLink.SetLinkNode(MainState);
            RestoreMain.State=MainState; RestoreMain.Original=MainState->Result;
            MainTap.Child=MainState->Result; MainState->Result.SetLinkNode(&MainTap);
            for (int32 I=0; I<MP.Num(); ++I) if (MP[I]->Struct==FAnimNode_StateMachine::StaticStruct() &&
                MP[I]->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Main)->StateMachineIndexInClass==0) { MainMachineProperty=I; break; }
            if (MainMachineProperty<0) return CaptureFailure(__LINE__);
            TArray<FBoneIndexType> Required; for (int32 I=0; I<81; ++I) Required.Add(static_cast<FBoneIndexType>(I));
            MainProxy.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*EvaluationSkeleton);
            MainProxy.GetRequiredBones().SetUseRAWData(true); MainProxy.GetRequiredBones().SetDisableRetargeting(false);
        }
        auto SharedState=[&]()->TSharedPtr<FJsonObject>
        {
            const auto R=MakeShared<FJsonObject>(); auto* A=VectorField(Layer,TEXT("PivotStartingAcceleration")); if (!A) return nullptr;
            WriteVector(R,TEXT("acceleration"),*A);
            if (!Scalar(Layer,TEXT("TimeAtPivotStop"),R) || !Scalar(Layer,TEXT("StrideWarpingPivotAlpha"),R) ||
                !Scalar(Main,TEXT("LastPivotTime"),R)) return nullptr;
            return R;
        };
        const auto Policy=MakeShared<FJsonObject>();
        if (!Scalar(Layer,TEXT("StrideWarpingBlendInStartOffset"),Policy) || !Scalar(Layer,TEXT("StrideWarpingBlendInDurationScaled"),Policy)) return CaptureFailure(__LINE__);
        auto* Clamp=FindFProperty<FStructProperty>(LayerClass,TEXT("PlayRateClampStartsPivots"));
        auto* Curve=FindFProperty<FNameProperty>(LayerClass,TEXT("LocomotionDistanceCurveName")); if (!Clamp || !Curve) return CaptureFailure(__LINE__);
        const auto& Range=*Clamp->ContainerPtrToValuePtr<FVector2D>(Layer);
        Double(Policy,TEXT("clampMin"),Range.X); Double(Policy,TEXT("clampMax"),Range.Y);
        Policy->SetStringField(TEXT("curveName"),Curve->GetPropertyValue_InContainer(Layer).ToString());
        auto* PredictionClass=LoadObject<UClass>(nullptr,TEXT("/Script/AnimationLocomotionLibraryRuntime.AnimCharacterMovementLibrary"));
        auto* Prediction=PredictionClass ? PredictionClass->FindFunctionByName(TEXT("PredictGroundMovementPivotLocation")) : nullptr;
        if (!Prediction) return CaptureFailure(__LINE__);
        TStrongObjectPtr<UAnimInstance> SyncInstance(NewObject<UAnimInstance>(Component.Get()));
        FSyncProxy Sync(SyncInstance.Get()); Sync.Initialize(SyncInstance.Get()); TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const FMemMark FrameMark(FMemStack::Get()); const auto Frame=FrameValue->AsObject();
            const float Delta=static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            TSharedPtr<FJsonObject> Observation;
            if (RunMain)
            {
                const auto Authored=Frame->GetObjectField(TEXT("observation"));
                auto* Mode=FindFProperty<FNumericProperty>(MainClass,TEXT("RootYawOffsetMode")); if (!Mode) return CaptureFailure(__LINE__);
                Authored->SetNumberField(TEXT("mode"),Mode->GetSignedIntPropertyValue(Mode->ContainerPtrToValuePtr<void>(Main)));
                Observation=LyraMainObservationProbe::Tick(Main,Owner,MainProxy,MainInterface,Authored,Delta);
                if (!Observation) return CaptureFailure(__LINE__);
            }
            else if (!SetFields(Main,Frame->GetObjectField(TEXT("main")))) return CaptureFailure(__LINE__);
            if (FullPose)
            {
                if (!SetFields(Layer,Frame->GetObjectField(TEXT("layer")))) return CaptureFailure(__LINE__);
                const auto& Q=Frame->GetArrayField(TEXT("relativeRotation"));
                Component->SetRelativeRotation(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber()));
            }
            auto* Movement=Owner->GetCharacterMovement(); const auto MovementInput=Frame->GetObjectField(TEXT("movement"));
            auto* Acceleration=VectorField(Movement,TEXT("Acceleration")); auto* Velocity=VectorField(Movement,TEXT("LastUpdateVelocity"));
            if (!Acceleration || !Velocity) return CaptureFailure(__LINE__);
            if (!RunMain) *Acceleration=VectorValue(MovementInput->GetArrayField(TEXT("acceleration")));
            *Velocity=VectorValue(MovementInput->GetArrayField(TEXT("lastUpdateVelocity")));
            Movement->GroundFriction=static_cast<float>(MovementInput->GetNumberField(TEXT("groundFriction")));
            if (!RunMain) FProxyAccess::Pre(MainProxy,Main,Delta);
            Interface->ForEachSubsystem(Layer,[&](const FAnimSubsystemInstanceContext& Subsystem)
            {
                if (Subsystem.SubsystemStruct==FAnimSubsystem_PropertyAccess::StaticStruct())
                {
                    FAnimSubsystemUpdateContext Game(Subsystem,Layer,Delta);
                    Subsystem.Subsystem.OnPreUpdate_GameThread(Game); Subsystem.Subsystem.OnPostUpdate_GameThread(Game);
                    FAnimSubsystemParallelUpdateContext Worker(Subsystem,LayerProxy,Delta); Subsystem.Subsystem.OnPreUpdate_WorkerThread(Worker);
                }
                return EAnimSubsystemEnumeration::Continue;
            });
            FProxyAccess::Pre(LayerProxy,Layer,Delta); Sync.PreUpdate(SyncInstance.Get(),Delta);
            FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Root(&Sync,Delta,&Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Root,Root);
            FAnimationUpdateContext Context(&LayerProxy,Delta,&Shared);
            if (RunMachine)
            {
                for (auto& Tap : Taps) { Tap.Initializations=0; Tap.Visits=0; Tap.Weight=0; Tap.Inertial=false; Tap.Output.Reset(); }
                if (!MachineInitialized || Frame->GetBoolField(TEXT("reinitialize")))
                { auto& RootLink=RunMain ? MainLink : FullPose ? ProviderLink : MachineLink;
                  auto* InitProxy=RunMain ? &MainProxy : &LayerProxy;
                  RootLink.Initialize(FAnimationInitializeContext(InitProxy)); RootLink.CacheBones(FAnimationCacheBonesContext(InitProxy)); MachineInitialized=true; }
            }
            const auto Row=MakeShared<FJsonObject>(); const auto Before=SharedState(); if (!Before) return CaptureFailure(__LINE__);
            if (RunMain)
            {
                const auto Transform=Component->GetComponentTransform(); const auto Q=Transform.GetRotation(); const auto P=Transform.GetLocation();
                const auto C=MakeShared<FJsonObject>();
                C->SetArrayField(TEXT("rotation"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});
                C->SetArrayField(TEXT("position"),{MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)});
                Observation->SetObjectField(TEXT("componentInput"),C); Row->SetObjectField(TEXT("observation"),Observation);
                auto* Direction=FindFProperty<FNumericProperty>(MainClass,TEXT("PivotInitialDirection")); if (!Direction) return CaptureFailure(__LINE__);
                const auto History=MakeShared<FJsonObject>();
                History->SetNumberField(TEXT("directionBefore"),Direction->GetSignedIntPropertyValue(Direction->ContainerPtrToValuePtr<void>(Main)));
                if (!Scalar(Main,TEXT("LastPivotTime"),History)) return CaptureFailure(__LINE__);
                History->SetBoolField(TEXT("becameRelevant"),false);
                History->SetNumberField(TEXT("directionAfterRoot"),History->GetNumberField(TEXT("directionBefore")));
                Double(History,TEXT("timeAfterRoot"),History->GetNumberField(TEXT("LastPivotTime")));
                Row->SetObjectField(TEXT("mainPivot"),History);
                MainTap.BeforeUpdate=[&,Row,History,Direction](const FAnimationUpdateContext&)
                {
                    History->SetNumberField(TEXT("directionAfterRoot"),Direction->GetSignedIntPropertyValue(Direction->ContainerPtrToValuePtr<void>(Main)));
                    auto* Time=FindFProperty<FDoubleProperty>(MainClass,TEXT("LastPivotTime")); check(Time);
                    Double(History,TEXT("timeAfterRoot"),Time->GetPropertyValue_InContainer(Main));
                    History->SetBoolField(TEXT("becameRelevant"),Main->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(*MainState).HasJustBecomeRelevant());
                    const auto Updated=SharedState(); check(Updated); Row->SetObjectField(TEXT("beforeShared"),Updated);
                    Rule->GetEvaluateGraphExposedInputs().Execute(Context); Row->SetBoolField(TEXT("ruleBefore"),Rule->bCanEnterTransition);
                };
            }
            if (FullPose) Number(Row,TEXT("hipFireBefore"),FNodeAccess::Time(*HipFire));
            Row->SetObjectField(TEXT("beforeShared"),Before);
            Rule->GetEvaluateGraphExposedInputs().Execute(Context); Row->SetBoolField(TEXT("ruleBefore"),Rule->bCanEnterTransition);
            FStructOnScope Params(Prediction);
            auto* PA=FindFProperty<FStructProperty>(Prediction,TEXT("Acceleration")); auto* PV=FindFProperty<FStructProperty>(Prediction,TEXT("Velocity"));
            auto* PF=FindFProperty<FFloatProperty>(Prediction,TEXT("GroundFriction")); auto* PR=FindFProperty<FStructProperty>(Prediction,TEXT("ReturnValue"));
            if (!PA || !PV || !PF || !PR) return CaptureFailure(__LINE__);
            *PA->ContainerPtrToValuePtr<FVector>(Params.GetStructMemory())=Movement->GetCurrentAcceleration();
            *PV->ContainerPtrToValuePtr<FVector>(Params.GetStructMemory())=Movement->GetLastUpdateVelocity();
            PF->SetPropertyValue_InContainer(Params.GetStructMemory(),Movement->GroundFriction);
            PredictionClass->GetDefaultObject()->ProcessEvent(Prediction,Params.GetStructMemory());
            const auto Location=*PR->ContainerPtrToValuePtr<FVector>(Params.GetStructMemory()); WriteVector(Row,TEXT("predictedLocation"),Location);
            Double(Row,TEXT("predictedDistance"),Location.Size2D()); Number(Row,TEXT("groundFriction"),Movement->GroundFriction);
            TArray<TSharedPtr<FJsonObject>> Sources; Sources.SetNum(2); TArray<float> Durations;
            TArray<TSharedPtr<FJsonValue>> RequestDetails;
            UE::Anim::TScopedGraphMessage<FRequests> Requests(Context,Durations,RunMachine ? &RequestDetails : nullptr);
            TArray<TSharedPtr<FJsonObject>> BeforeSources;
            if (RunMachine)
            {
                for (int32 I=0; I<2; ++I)
                {
                    const auto R=MakeShared<FJsonObject>(); auto& Node=*Nodes[I];
                    R->SetStringField(TEXT("beforeAsset"),AssetPath(Node.GetSequence()));
                    Number(R,TEXT("before"),FNodeAccess::Time(Node)); Number(R,TEXT("explicitBefore"),Node.GetExplicitTime()); BeforeSources.Add(R);
                }
                Row->SetNumberField(TEXT("stateBefore"),Machine->GetCurrentState());
                Number(Row,TEXT("elapsedBefore"),Machine->GetCurrentStateElapsedTime());
                if (Frame->GetBoolField(TEXT("active")))
                {
                    auto C=FAnimationUpdateContext(RunMain ? &MainProxy : &LayerProxy,Delta,&Shared)
                        .FractionalWeight(static_cast<float>(Frame->GetNumberField(TEXT("weight"))));
                    C.SetNodeId(RunMain ? MainMachineProperty : FullPose ? ProviderLink.LinkID : Properties.Num()-1-static_cast<int32>(Trace->GetNumberField(TEXT("machineNode"))));
                    if (RunMain) MainLink.Update(C); else if (FullPose) ProviderLink.Update(C); else MachineLink.Update(C);
                }
                Row->SetNumberField(TEXT("state"),Machine->GetCurrentState()); Number(Row,TEXT("elapsed"),Machine->GetCurrentStateElapsedTime());
                Number(Row,TEXT("stateWeightA"),Machine->GetStateWeight(0)); Number(Row,TEXT("stateWeightB"),Machine->GetStateWeight(1));
                Row->SetArrayField(TEXT("requests"),RequestDetails);
            }
            TSet<int32> Visited;
            TArray<TSharedPtr<FJsonValue>> Order;
            if (RunMachine)
            {
                for (int32 I=0; I<2; ++I) if (Taps[I].Visits>0) Order.Add(MakeShared<FJsonValueNumber>(I));
                for (int32 I=0; I<2; ++I) if (Taps[I].Visits==0) Order.Add(MakeShared<FJsonValueNumber>(I));
                Row->SetArrayField(TEXT("order"),Order);
            }
            else Order=Frame->GetArrayField(TEXT("order"));
            for (const auto& Value : Order)
            {
                const int32 I=static_cast<int32>(Value->AsNumber()); if (I<0 || I>1 || Visited.Contains(I)) return CaptureFailure(__LINE__); Visited.Add(I);
                const auto Source=RunMachine ? nullptr : Frame->GetArrayField(TEXT("sources"))[I]->AsObject();
                const auto R=RunMachine ? BeforeSources[I] : MakeShared<FJsonObject>(); Sources[I]=R;
                auto& Node=*Nodes[I];
                if (!RunMachine && (!Initialized[I] || Source->GetBoolField(TEXT("reinitialize"))))
                { Links[I].Initialize(FAnimationInitializeContext(&LayerProxy)); Initialized[I]=true; }
                if (!RunMachine)
                {
                    R->SetStringField(TEXT("beforeAsset"),Node.GetSequence() ? Node.GetSequence()->GetPathName() : TEXT(""));
                    Number(R,TEXT("before"),FNodeAccess::Time(Node)); Number(R,TEXT("explicitBefore"),Node.GetExplicitTime());
                }
                const bool Active=RunMachine ? Taps[I].Visits>0 : Source->GetBoolField(TEXT("active")); R->SetBoolField(TEXT("active"),Active);
                if (Active && !RunMachine)
                { auto C=Context.FractionalWeight(static_cast<float>(Source->GetNumberField(TEXT("weight")))); C.SetNodeId(static_cast<int32>(Indices[I]->AsNumber())); Links[I].Update(C); }
                if (RunMachine)
                { R->SetNumberField(TEXT("initializations"),Taps[I].Initializations); R->SetNumberField(TEXT("visits"),Taps[I].Visits);
                  Number(R,TEXT("visitWeight"),Taps[I].Weight); R->SetBoolField(TEXT("inertialScope"),Taps[I].Inertial); }
                R->SetBoolField(TEXT("becameRelevant"),Active && Layer->GetSubsystem<FAnimSubsystemInstance_NodeRelevancy>().GetNodeRelevancy(Node).HasJustBecomeRelevant());
                R->SetStringField(TEXT("asset"),AssetPath(Node.GetSequence()));
                Number(R,TEXT("prepared"),FNodeAccess::Time(Node)); Number(R,TEXT("explicit"),Node.GetExplicitTime()); Number(R,TEXT("cachedWeight"),Node.GetCachedBlendWeight());
                if (FullPose)
                { Number(R,TEXT("orientationAngle"),Orientations[I]->LocomotionAngle); Number(R,TEXT("orientationAlpha"),Orientations[I]->GetAlpha());
                  Number(R,TEXT("strideSpeed"),Strides[I]->LocomotionSpeed); Number(R,TEXT("strideAlpha"),Strides[I]->GetAlpha()); }
                const auto After=SharedState(); if (!After) return CaptureFailure(__LINE__); R->SetObjectField(TEXT("shared"),After);
            }
            if (Visited.Num()!=2) return CaptureFailure(__LINE__);
            const auto Final=SharedState(); if (!Final) return CaptureFailure(__LINE__); Row->SetObjectField(TEXT("shared"),Final);
            Rule->GetEvaluateGraphExposedInputs().Execute(Context); Row->SetBoolField(TEXT("ruleAfter"),Rule->bCanEnterTransition);
            Sync.UpdateAnimation(); Sync.PostUpdate(SyncInstance.Get());
            if (FullPose)
            {
                Number(Row,TEXT("blendWeight"),Blend->BlendWeights[0]); Number(Row,TEXT("hipFireWeight"),HipFire->GetCachedBlendWeight());
                Row->SetBoolField(TEXT("hipFireActive"),Frame->GetBoolField(TEXT("active")) && FAnimWeight::IsRelevant(Blend->BlendWeights[0]));
                Row->SetStringField(TEXT("hipFireAsset"),AssetPath(HipFire->GetSequence())); Number(Row,TEXT("hipFireTime"),FNodeAccess::Time(*HipFire));
                const auto& D=FNodeAccess::Delta(*HipFire); const auto& M=FNodeAccess::Marker(*HipFire);
                Number(Row,TEXT("hipFirePrevious"),D.GetPrevious()); Number(Row,TEXT("hipFireDelta"),D.Delta);
                Row->SetNumberField(TEXT("hipFireMarkerPrevious"),M.PreviousMarker.MarkerIndex); Row->SetNumberField(TEXT("hipFireMarkerNext"),M.NextMarker.MarkerIndex);
                Number(Row,TEXT("hipFireMarkerPreviousDistance"),M.PreviousMarker.MarkerIndex==-2 ? 0 : M.PreviousMarker.TimeToMarker);
                Number(Row,TEXT("hipFireMarkerNextDistance"),M.NextMarker.MarkerIndex==-2 ? 0 : M.NextMarker.TimeToMarker);
                if (Frame->GetBoolField(TEXT("active")))
                {
                    FPoseContext Pose(RunMain ? &MainProxy : &LayerProxy);
                    if (RunMain) MainLink.Evaluate(Pose); else ProviderLink.Evaluate(Pose);
                    const auto Output=LyraCyclePoseProbe::PoseData(Pose.Pose,Pose.Curve,Pose.CustomAttributes,EvaluationSkeleton->GetReferenceSkeleton());
                    const auto MachineOutput=Taps[Machine->GetCurrentState()].Output;
                    if (!Output || !MachineOutput) return CaptureFailure(__LINE__); Row->SetObjectField(TEXT("output"),Output); Row->SetObjectField(TEXT("machineOutput"),MachineOutput);
                }
            }
            if (RunMain)
            {
                const auto L=MakeShared<FJsonObject>(); const auto& D=FNodeAccess::Delta(*MainLean);
                Number(L,TEXT("time"),MainLean->GetAccumulatedTime()); Number(L,TEXT("pin"),MainLean->GetPosition().X);
                Number(L,TEXT("cachedWeight"),MainLean->GetCachedBlendWeight()); Number(L,TEXT("previous"),D.GetPrevious()); Number(L,TEXT("delta"),D.Delta);
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const auto& S : FLeanAccess::Samples(*MainLean))
                {
                    const auto V=MakeShared<FJsonObject>(); V->SetNumberField(TEXT("index"),S.SampleDataIndex);
                    Number(V,TEXT("weight"),S.TotalWeight); Number(V,TEXT("weightRate"),S.WeightRate);
                    PRAGMA_DISABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("time"),S.Time); Number(V,TEXT("previous"),S.PreviousTime);
                    PRAGMA_ENABLE_DEPRECATION_WARNINGS
                    Number(V,TEXT("deltaPrevious"),S.DeltaTimeRecord.GetPrevious()); Number(V,TEXT("delta"),S.DeltaTimeRecord.Delta);
                    Samples.Add(MakeShared<FJsonValueObject>(V));
                }
                L->SetArrayField(TEXT("samples"),Samples); Row->SetObjectField(TEXT("lean"),L);
                const auto H=Row->GetObjectField(TEXT("mainPivot"));
                auto* Direction=FindFProperty<FNumericProperty>(MainClass,TEXT("PivotInitialDirection"));
                H->SetNumberField(TEXT("directionAfter"),Direction->GetSignedIntPropertyValue(Direction->ContainerPtrToValuePtr<void>(Main)));
                auto* Time=FindFProperty<FDoubleProperty>(MainClass,TEXT("LastPivotTime")); Double(H,TEXT("timeAfter"),Time->GetPropertyValue_InContainer(Main));
            }
            TArray<TSharedPtr<FJsonValue>> Rows;
            for (int32 I=0; I<2; ++I)
            {
                auto& Node=*Nodes[I]; const auto R=Sources[I]; const auto& D=FNodeAccess::Delta(Node); const auto& M=FNodeAccess::Marker(Node);
                Number(R,TEXT("time"),FNodeAccess::Time(Node)); Number(R,TEXT("previous"),D.GetPrevious()); Number(R,TEXT("delta"),D.Delta);
                R->SetNumberField(TEXT("markerPrevious"),M.PreviousMarker.MarkerIndex); R->SetNumberField(TEXT("markerNext"),M.NextMarker.MarkerIndex);
                Number(R,TEXT("markerPreviousDistance"),M.PreviousMarker.MarkerIndex==-2 ? 0 : M.PreviousMarker.TimeToMarker);
                Number(R,TEXT("markerNextDistance"),M.NextMarker.MarkerIndex==-2 ? 0 : M.NextMarker.TimeToMarker);
                if (RunMain) R->SetBoolField(TEXT("tickRegistered"),R->GetBoolField(TEXT("active")) && Node.GetSequence()!=nullptr);
                if (R->GetBoolField(TEXT("active")))
                {
                    if (RunMain && !Node.GetSequence())
                    { Number(R,TEXT("rate"),0); R->SetNumberField(TEXT("leader"),-1); }
                    else
                    {
                        const auto* Group=Sync.GetSyncGroupMapRead().Find(TEXT("Locomotion")); if (!Group) return CaptureFailure(__LINE__);
                        const auto* Tick=Group->ActivePlayers.FindByPredicate([&](const FAnimTickRecord& T){return T.TimeAccumulator==FNodeAccess::TimeAddress(Node);});
                        if (!Tick) return CaptureFailure(__LINE__);
                        Number(R,TEXT("rate"),Tick->PlayRateMultiplier); R->SetNumberField(TEXT("leader"),Group->GroupLeaderIndex);
                    }
                }
                Rows.Add(MakeShared<FJsonValueObject>(R));
            }
            TArray<TSharedPtr<FJsonValue>> Inertia;
            for (float Duration : Durations) { const auto R=MakeShared<FJsonObject>(); Number(R,TEXT("duration"),Duration); Inertia.Add(MakeShared<FJsonValueObject>(R)); }
            Row->SetArrayField(TEXT("inertia"),Inertia); Row->SetArrayField(TEXT("sources"),Rows); Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto R=MakeShared<FJsonObject>(); R->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));
        R->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz"))); R->SetObjectField(TEXT("policy"),Policy); R->SetArrayField(TEXT("frames"),Frames);
        if (RunMain)
        {
            const auto Binding=MakeShared<FJsonObject>(); Binding->SetNumberField(TEXT("stateRoot"),20);
            Binding->SetNumberField(TEXT("applyAdditive"),23); Binding->SetNumberField(TEXT("linked"),21); Binding->SetNumberField(TEXT("lean"),22);
            Binding->SetNumberField(TEXT("state"),MainState->GetStateIndex());
            Binding->SetStringField(TEXT("becomeRelevant"),FNodeAccess::FunctionName(*MainState,TEXT("BecomeRelevantFunction")).ToString());
            Binding->SetStringField(TEXT("update"),FNodeAccess::UpdateName(*MainState).ToString()); R->SetObjectField(TEXT("mainPivotBinding"),Binding);
        }
        Traces.Add(MakeShared<FJsonValueObject>(R)); Component->UnregisterComponent();
    }
    const auto R=MakeShared<FJsonObject>(); R->SetArrayField(TEXT("traces"),Traces);
    FString Json; FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadPivotSourceTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadPivotTrace(MainClass,Mesh,RequestsJson,false); }

FString UAlsLyraGraphLibrary::ReadPivotMachineTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{ return ReadPivotTrace(MainClass,Mesh,RequestsJson,true); }

FString UAlsLyraGraphLibrary::ReadPivotRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadPivotTrace(MainClass,Mesh,RequestsJson,true,Skeleton,&Sequences); }

FString UAlsLyraGraphLibrary::ReadMainPivotTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadPivotTrace(MainClass,Mesh,RequestsJson,true,Skeleton,&Sequences,LeanSpace,&LeanSequences); }

FString UAlsLyraGraphLibrary::ReadStopRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,nullptr,nullptr,false,true); }

FString UAlsLyraGraphLibrary::ReadMainStopRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,nullptr,nullptr,false,true,true); }

FString UAlsLyraGraphLibrary::ReadStartRuntimePoseTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass, Mesh, RequestsJson, Skeleton, &Sequences); }

FString UAlsLyraGraphLibrary::ReadMainStartLeanTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences); }

FString UAlsLyraGraphLibrary::ReadMainSourceScopeTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences,true); }

FString UAlsLyraGraphLibrary::ReadMainSourceScopeStopTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences,true,false,false,true); }

FString UAlsLyraGraphLibrary::ReadMainStateRootsTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences,true,false,false,true,true); }

FString UAlsLyraGraphLibrary::ReadMainStateHistoryTrace(UClass* MainClass, USkeletalMesh* Mesh, USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences, UBlendSpace* LeanSpace,
    const TArray<UAnimSequence*>& LeanSequences, const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences,true,false,false,true,true,true); }

FString UAlsLyraGraphLibrary::ReadMainGroundScopeTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences,UBlendSpace* LeanSpace,const TArray<UAnimSequence*>& LeanSequences,const FString& RequestsJson)
{ return ReadStartTrace(MainClass,Mesh,RequestsJson,Skeleton,&Sequences,LeanSpace,&LeanSequences,true,false,false,true,true,true,true); }
