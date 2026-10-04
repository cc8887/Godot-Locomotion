#include "AlsLyraGraphLibrary.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimStateMachineTypes.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_TransitionResult.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/StructOnScope.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

namespace
{
struct FRuleInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance)
    { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
};
struct FRuleMachineAccess : FAnimNode_StateMachine
{
    static void SetObservation(FAnimNode_StateMachine& Node, int32 State, float Time)
    { Node.*&FRuleMachineAccess::CurrentState = State; Node.*&FRuleMachineAccess::ElapsedTime = Time; }
    static bool Select(FAnimNode_StateMachine& Node, const FAnimationUpdateContext& Context,
        const FBakedAnimationState& State, FAnimationPotentialTransition& Result)
    {
        TArray<int32, TInlineAllocator<4>> Visited;
        return (Node.*&FRuleMachineAccess::FindValidTransition)(Context, State, Result, Visited);
    }
};
bool SetRuleField(UObject* Instance, const FString& Name, const TSharedPtr<FJsonValue>& Value)
{
    FProperty* Property = FindFProperty<FProperty>(Instance->GetClass(), *Name);
    if (!Property) return false;
    void* Address = Property->ContainerPtrToValuePtr<void>(Instance);
    if (auto* Bool = CastField<FBoolProperty>(Property)) { Bool->SetPropertyValue(Address, Value->AsBool()); return true; }
    if (auto* Enum = CastField<FEnumProperty>(Property))
    { Enum->GetUnderlyingProperty()->SetIntPropertyValue(Address, static_cast<int64>(Value->AsNumber())); return true; }
    if (auto* Number = CastField<FNumericProperty>(Property))
    {
        if (Number->IsFloatingPoint()) Number->SetFloatingPointPropertyValue(Address, Value->AsNumber());
        else Number->SetIntPropertyValue(Address, static_cast<int64>(Value->AsNumber()));
        return true;
    }
    if (auto* Struct = CastField<FStructProperty>(Property); Struct && Struct->Struct == TBaseStructure<FVector>::Get())
    {
        const auto& Values = Value->AsArray(); if (Values.Num() != 3) return false;
        *static_cast<FVector*>(Address) = FVector(Values[0]->AsNumber(), Values[1]->AsNumber(), Values[2]->AsNumber());
        return true;
    }
    return false;
}
}

FString UAlsLyraGraphLibrary::ReadRuntimeGraph(UClass* AnimationClass)
{
    const IAnimClassInterface* Class = AnimationClass ? IAnimClassInterface::GetFromClass(AnimationClass) : nullptr;
    if (!Class) return {};
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Value) -> TSharedPtr<FJsonValue>
        {
            if (const auto* ObjectProperty = CastField<FObjectPropertyBase>(Property))
            {
                const UObject* Object = ObjectProperty->GetObjectPropertyValue(Value);
                return MakeShared<FJsonValueString>(Object ? Object->GetPathName() : TEXT(""));
            }
            return nullptr;
        });
    TArray<TSharedPtr<FJsonValue>> Machines, Nodes;
    int32 Index = 0;
    for (const auto& Machine : Class->GetBakedStateMachines())
    {
        const auto Data = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(FBakedAnimationStateMachine::StaticStruct(),
            &Machine, Data, 0, 0, &ExportObject)) return {};
        Data->SetNumberField(TEXT("machineIndex"), Index++);
        Machines.Add(MakeShared<FJsonValueObject>(Data));
    }
    UObject* CDO = AnimationClass->GetDefaultObject();
    const auto& Properties = Class->GetAnimNodeProperties();
    for (int32 NodeIndex = 0; NodeIndex < Properties.Num(); ++NodeIndex)
    {
        const FStructProperty* Property = Properties[NodeIndex];
        if (Property->Struct != FAnimNode_StateMachine::StaticStruct() &&
            Property->Struct != FAnimNode_Inertialization::StaticStruct()) continue;
        const auto Data = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(Property->Struct,
            Property->ContainerPtrToValuePtr<void>(CDO), Data, 0, 0, &ExportObject)) return {};
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("nodeIndex"), NodeIndex);
        Row->SetStringField(TEXT("property"), Property->GetName());
        Row->SetStringField(TEXT("type"), Property->Struct->GetPathName());
        Row->SetObjectField(TEXT("settings"), Data);
        Nodes.Add(MakeShared<FJsonValueObject>(Row));
    }
    const auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("class"), AnimationClass->GetPathName());
    Result->SetArrayField(TEXT("machines"), Machines); Result->SetArrayField(TEXT("nodes"), Nodes);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadLocomotionRuleProbe(UClass* AnimationClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{
    const IAnimClassInterface* Class = AnimationClass ? IAnimClassInterface::GetFromClass(AnimationClass) : nullptr;
    TSharedPtr<FJsonObject> Requests;
    if (!Class || !Mesh || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests) ||
        Class->GetBakedStateMachines().Num() != 1) return {};
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
    Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMesh(Mesh); Component->SetAnimInstanceClass(AnimationClass);
    Owner->SetRootComponent(Component.Get()); Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
    UAnimInstance* Instance = Component->GetAnimInstance(); if (!Instance || Instance->GetClass() != AnimationClass) return {};
    FAnimInstanceProxy& Proxy = FRuleInstanceAccess::Proxy(Instance);
    FAnimNode_StateMachine* Machine = nullptr;
    for (const FStructProperty* Property : Class->GetAnimNodeProperties())
        if (Property->Struct == FAnimNode_StateMachine::StaticStruct())
            Machine = Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Instance);
    if (!Machine || Machine->StateMachineIndexInClass != 0) return {};
    Machine->CacheMachineDescription(const_cast<IAnimClassInterface*>(Class));
    TArray<TSharedPtr<FJsonValue>> Rows;
    const auto& Definition = Class->GetBakedStateMachines()[0];
    for (const auto& Value : Requests->GetArrayField(TEXT("rows")))
    {
        const auto& Request = Value->AsObject(); const int32 State = static_cast<int32>(Request->GetNumberField(TEXT("state")));
        if (!Definition.States.IsValidIndex(State)) return {};
        for (const auto& Field : Request->GetObjectField(TEXT("fields"))->Values)
            if (!SetRuleField(Instance, FString(Field.Key.ToView()), Field.Value))
            { UE_LOG(LogTemp, Error, TEXT("Cannot set rule input %s"), *FString(Field.Key.ToView())); return {}; }
        FRuleMachineAccess::SetObservation(*Machine, State, static_cast<float>(Request->GetNumberField(TEXT("elapsed"))));
        FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Context(&Proxy, 0.f, &Shared);
        TArray<TSharedPtr<FJsonValue>> Rules;
        for (const auto& Exit : Definition.States[State].Transitions)
        {
            if (Exit.bAutomaticRemainingTimeRule) continue;
            auto* Node = Proxy.GetMutableNodeFromIndex<FAnimNode_TransitionResult>(Exit.CanTakeDelegateIndex);
            if (!Node) return {};
            Node->GetEvaluateGraphExposedInputs().Execute(Context);
            const auto Rule = MakeShared<FJsonObject>(); Rule->SetNumberField(TEXT("edge"), Exit.TransitionIndex);
            Rule->SetBoolField(TEXT("result"), Node->bCanEnterTransition); Rules.Add(MakeShared<FJsonValueObject>(Rule));
        }
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("state"), State);
        Row->SetArrayField(TEXT("rules"), Rules);
        if (!Definition.States[State].bIsAConduit)
        {
            // The installed engine does not export this struct's constructor.
            // Reflected construction calls its actual native struct ops.
            FStructOnScope TransitionStorage(FAnimationPotentialTransition::StaticStruct());
            auto& Transition = *reinterpret_cast<FAnimationPotentialTransition*>(TransitionStorage.GetStructMemory());
            const bool Found = FRuleMachineAccess::Select(*Machine, Context, Definition.States[State], Transition);
            const auto Selection = MakeShared<FJsonObject>(); Selection->SetBoolField(TEXT("found"), Found);
            if (Found)
            {
                Selection->SetNumberField(TEXT("edge"), Transition.TransitionRule->TransitionIndex);
                Selection->SetNumberField(TEXT("next"), Transition.TargetState);
                Selection->SetNumberField(TEXT("crossfadeAdjustment"), Transition.CrossfadeTimeAdjustment);
                TArray<TSharedPtr<FJsonValue>> Path;
                for (int32 Edge : Transition.SourceTransitionIndices) Path.Add(MakeShared<FJsonValueNumber>(Edge));
                Selection->SetArrayField(TEXT("path"), Path);
            }
            Row->SetObjectField(TEXT("selection"), Selection);
        }
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("class"), AnimationClass->GetPathName());
    Result->SetArrayField(TEXT("rows"), Rows); Component->UnregisterComponent();
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}
