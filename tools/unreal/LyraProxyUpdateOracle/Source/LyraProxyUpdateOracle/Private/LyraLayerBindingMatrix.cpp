#include "LyraProxyUpdateOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraLayerBindingMatrix
{
FString Fail(int32 Line) { UE_LOG(LogTemp, Error, TEXT("LYRA_LAYER_BINDING_MATRIX_FAILED line=%d"), Line); return {}; }
FString Json(const TSharedRef<FJsonObject>& Object)
{ FString Result; FJsonSerializer::Serialize(Object, TJsonWriterFactory<>::Create(&Result)); return Result; }

struct FClassSnapshot
{
    UClass* Class;
    IAnimClassInterface* Interface;
    TArray<FAnimBlueprintFunction> Functions;
    bool Receive, Propagate;
    explicit FClassSnapshot(UClass* Value) : Class(Value), Interface(IAnimClassInterface::GetFromClass(Value)),
        Functions(Interface->GetAnimBlueprintFunctions())
    {
        const auto* Defaults = CastChecked<UAnimInstance>(Class->GetDefaultObject());
        Receive = Defaults->GetReceiveNotifiesFromLinkedInstances();
        Propagate = Defaults->GetPropagateNotifiesToLinkedInstances();
    }
    void Restore() const
    {
        // Controlled commandlet fixture only. No asset save or package dirtying.
        const_cast<TArray<FAnimBlueprintFunction>&>(Interface->GetAnimBlueprintFunctions()) = Functions;
        auto* Defaults = CastChecked<UAnimInstance>(Class->GetDefaultObject());
        Defaults->SetReceiveNotifiesFromLinkedInstances(Receive);
        Defaults->SetPropagateNotifiesToLinkedInstances(Propagate);
    }
    ~FClassSnapshot()
    { Restore(); if (auto* Generated = Cast<UBlueprintGeneratedClass>(Class)) Generated->UpdateCustomPropertyListForPostConstruction(); }
};
struct FNodeSnapshot
{
    FAnimNode_LinkedAnimLayer* Node;
    FName Layer;
    TSubclassOf<UAnimInstance> InstanceClass;
    TSubclassOf<UAnimLayerInterface> Interface;
    bool Receive, Propagate;
    explicit FNodeSnapshot(FAnimNode_LinkedAnimLayer* Value) : Node(Value), Layer(Value->Layer),
        InstanceClass(Value->InstanceClass), Interface(Value->Interface),
        Receive(Value->bReceiveNotifiesFromLinkedInstances), Propagate(Value->bPropagateNotifiesToLinkedInstances) {}
    void Restore() const
    {
        Node->Layer = Layer; Node->InstanceClass = InstanceClass; Node->Interface = Interface;
        Node->bReceiveNotifiesFromLinkedInstances = Receive; Node->bPropagateNotifiesToLinkedInstances = Propagate;
    }
    ~FNodeSnapshot() { Restore(); }
};

TSharedRef<FJsonObject> ClassDefinition(UClass* Class)
{
    auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("name"), Class->GetPathName());
    auto* Defaults = CastChecked<UAnimInstance>(Class->GetDefaultObject());
    Result->SetBoolField(TEXT("receiveNotifies"), Defaults->GetReceiveNotifiesFromLinkedInstances());
    Result->SetBoolField(TEXT("propagateNotifies"), Defaults->GetPropagateNotifiesToLinkedInstances());
    TArray<TSharedPtr<FJsonValue>> Functions;
    for (const auto& Function : IAnimClassInterface::GetFromClass(Class)->GetAnimBlueprintFunctions())
    {
        auto Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("name"), Function.Name.ToString());
        Row->SetStringField(TEXT("group"), Function.Group.IsNone() ? FString() : Function.Group.ToString());
        Row->SetBoolField(TEXT("implemented"), Function.bImplemented);
        Functions.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("functions"), Functions); return Result;
}

TArray<TSharedPtr<FJsonValue>> CallDefinitions(UClass* Class)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const auto* Property : IAnimClassInterface::GetFromClass(Class)->GetLinkedAnimLayerNodeProperties())
    {
        const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Class->GetDefaultObject());
        auto Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("node"), Property->GetName()); Row->SetStringField(TEXT("function"), Node->Layer.ToString());
        Row->SetStringField(TEXT("defaultClass"), Node->InstanceClass ? Node->InstanceClass->GetPathName() : FString());
        Row->SetBoolField(TEXT("hasInterface"), Node->Interface != nullptr);
        Row->SetBoolField(TEXT("receiveNotifies"), Node->bReceiveNotifiesFromLinkedInstances);
        Row->SetBoolField(TEXT("propagateNotifies"), Node->bPropagateNotifiesToLinkedInstances);
        Result.Add(MakeShared<FJsonValueObject>(Row));
    }
    return Result;
}

TSharedRef<FJsonObject> Observe(UAnimInstance* Main, USkeletalMeshComponent* Component,
    TMap<const UAnimInstance*, int32>& Identities, TArray<TStrongObjectPtr<UAnimInstance>>& KeepAlive)
{
    auto Result = MakeShared<FJsonObject>();
    Result->SetNumberField(TEXT("linkedInstances"), static_cast<const USkeletalMeshComponent*>(Component)->GetLinkedAnimInstances().Num());
    TArray<TSharedPtr<FJsonValue>> Nodes;
    for (const auto* Property : IAnimClassInterface::GetFromClass(Main->GetClass())->GetLinkedAnimLayerNodeProperties())
    {
        const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
        auto* Target = Node->GetTargetInstance<UAnimInstance>();
        auto Row = MakeShared<FJsonObject>(); Row->SetStringField(TEXT("node"), Property->GetName());
        if (Target && !Identities.Contains(Target))
        { Identities.Add(Target, Identities.Num()); KeepAlive.Emplace(Target); }
        Row->SetNumberField(TEXT("owner"), Target ? Identities.FindChecked(Target) : -1);
        Row->SetStringField(TEXT("kind"), !Target ? TEXT("Unbound") : Target == Main ? TEXT("Self") : TEXT("External"));
        Row->SetStringField(TEXT("class"), Target ? Target->GetClass()->GetPathName() : FString());
        Row->SetBoolField(TEXT("receiveNotifies"), Target && Target->GetReceiveNotifiesFromLinkedInstances());
        Row->SetBoolField(TEXT("propagateNotifies"), Target && Target->GetPropagateNotifiesToLinkedInstances());
        Nodes.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("nodes"), Nodes); return Result;
}
}

FString ULyraProxyUpdateOracleLibrary::ReadLayerBindingMatrix(const FString& RequestsJson)
{
    using namespace LyraLayerBindingMatrix;
    TSharedPtr<FJsonObject> Request;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Request)) return Fail(__LINE__);
    TMap<FString, UClass*> Classes;
    TArray<TUniquePtr<FClassSnapshot>> ClassSnapshots;
    TArray<TUniquePtr<FNodeSnapshot>> NodeSnapshots;
    for (const auto& Entry : Request->GetObjectField(TEXT("classes"))->Values)
    {
        auto* Class = LoadObject<UClass>(nullptr, *Entry.Value->AsString());
        if (!Class || !Class->IsChildOf(UAnimInstance::StaticClass()) || !IAnimClassInterface::GetFromClass(Class)) return Fail(__LINE__);
        Classes.Add(FString(Entry.Key), Class); ClassSnapshots.Add(MakeUnique<FClassSnapshot>(Class));
    }
    auto* MainClass = Classes.FindRef(TEXT("main"));
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, *Request->GetStringField(TEXT("mesh")));
    if (!MainClass || !Mesh) return Fail(__LINE__);
    auto* MainInterface = IAnimClassInterface::GetFromClass(MainClass);
    for (const auto* Property : MainInterface->GetLinkedAnimLayerNodeProperties())
        NodeSnapshots.Add(MakeUnique<FNodeSnapshot>(Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(MainClass->GetDefaultObject())));

    auto Original = MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> OriginalClasses;
    for (const auto& Entry : Classes) OriginalClasses.Add(MakeShared<FJsonValueObject>(ClassDefinition(Entry.Value)));
    Original->SetArrayField(TEXT("classes"), OriginalClasses); Original->SetArrayField(TEXT("calls"), CallDefinitions(MainClass));
    const FString Before = Json(Original);
    TArray<TSharedPtr<FJsonValue>> Results;
    for (const auto& CaseValue : Request->GetArrayField(TEXT("cases")))
    {
        for (const auto& Snapshot : ClassSnapshots) Snapshot->Restore();
        for (const auto& Snapshot : NodeSnapshots) Snapshot->Restore();
        const auto Case = CaseValue->AsObject();
        const TSharedPtr<FJsonObject>* Overrides = nullptr;
        if (Case->TryGetObjectField(TEXT("functions"), Overrides))
            for (const auto& ClassEntry : (*Overrides)->Values)
            {
                auto* Class = Classes.FindRef(FString(ClassEntry.Key)); if (!Class) return Fail(__LINE__);
                auto& Functions = const_cast<TArray<FAnimBlueprintFunction>&>(IAnimClassInterface::GetFromClass(Class)->GetAnimBlueprintFunctions());
                for (const auto& Entry : ClassEntry.Value->AsObject()->Values)
                {
                    auto* Function = Functions.FindByPredicate([&](const auto& F) { return F.Name == FName(*Entry.Key); });
                    if (!Function) return Fail(__LINE__);
                    const auto Value = Entry.Value->AsObject(); FString Group; bool Implemented;
                    if (Value->TryGetStringField(TEXT("group"), Group)) Function->Group = FName(*Group);
                    if (Value->TryGetBoolField(TEXT("implemented"), Implemented)) Function->bImplemented = Implemented;
                }
            }
        if (Case->TryGetObjectField(TEXT("classFlags"), Overrides))
            for (const auto& Entry : (*Overrides)->Values)
            {
                auto* Class = Classes.FindRef(FString(Entry.Key)); if (!Class) return Fail(__LINE__);
                auto* Defaults = CastChecked<UAnimInstance>(Class->GetDefaultObject());
                Defaults->SetReceiveNotifiesFromLinkedInstances(Entry.Value->AsObject()->GetBoolField(TEXT("receiveNotifies")));
                Defaults->SetPropagateNotifiesToLinkedInstances(Entry.Value->AsObject()->GetBoolField(TEXT("propagateNotifies")));
            }
        if (Case->TryGetObjectField(TEXT("nodes"), Overrides))
            for (const auto& Entry : (*Overrides)->Values)
            {
                const FStructProperty* Property = nullptr;
                for (const auto* Candidate : MainInterface->GetLinkedAnimLayerNodeProperties())
                    if (Candidate->GetName() == FString(Entry.Key)) Property = Candidate;
                if (!Property) return Fail(__LINE__);
                auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(MainClass->GetDefaultObject());
                const auto Value = Entry.Value->AsObject(); FString Name; bool Flag;
                if (Value->TryGetStringField(TEXT("function"), Name))
                { if (!Node->InputPoseNames.IsEmpty()) return Fail(__LINE__); Node->Layer = FName(*Name); }
                if (Value->TryGetStringField(TEXT("defaultClass"), Name))
                { if (!Name.IsEmpty() && !Classes.Contains(Name)) return Fail(__LINE__); Node->InstanceClass = Name.IsEmpty() ? nullptr : Classes.FindRef(Name); }
                if (Value->TryGetBoolField(TEXT("hasInterface"), Flag) && !Flag) Node->Interface = nullptr;
                if (Value->TryGetBoolField(TEXT("receiveNotifies"), Flag)) Node->bReceiveNotifiesFromLinkedInstances = Flag;
                if (Value->TryGetBoolField(TEXT("propagateNotifies"), Flag)) Node->bPropagateNotifiesToLinkedInstances = Flag;
            }

        // Blueprint classes cache the CDO fields differing from native defaults.
        // Rebuild that original cache after controlled CDO edits, and restore it
        // again on every exit. Otherwise NewObject ignores newly changed flags.
        for (const auto& Entry : Classes)
            if (auto* Generated = Cast<UBlueprintGeneratedClass>(Entry.Value)) Generated->UpdateCustomPropertyListForPostConstruction();
        auto Result = MakeShared<FJsonObject>(); Result->SetStringField(TEXT("name"), Case->GetStringField(TEXT("name")));
        TArray<TSharedPtr<FJsonValue>> Definitions;
        for (const auto& Entry : Classes) Definitions.Add(MakeShared<FJsonValueObject>(ClassDefinition(Entry.Value)));
        Result->SetArrayField(TEXT("classes"), Definitions); Result->SetArrayField(TEXT("calls"), CallDefinitions(MainClass));

        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr, false, ERHIFeatureLevel::Num, &Initialization));
        if (!World.IsValid()) return Fail(__LINE__);
        struct FWorldCleanup { UWorld* World; ~FWorldCleanup() { World->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        auto* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
        Component->bUseRefPoseOnInitAnim = true; Component->SetDisablePostProcessBlueprint(true);
        Component->SetCollisionEnabled(ECollisionEnabled::NoCollision); Component->SetSkeletalMesh(Mesh); Component->SetAnimInstanceClass(MainClass);
        Owner->SetRootComponent(Component.Get()); Owner->AddInstanceComponent(Component.Get()); Component->RegisterComponent();
        auto* Main = Component->GetAnimInstance(); if (!Main) return Fail(__LINE__);
        TMap<const UAnimInstance*, int32> Identities; Identities.Add(Main, 0);
        TArray<TStrongObjectPtr<UAnimInstance>> KeepAlive; KeepAlive.Emplace(Main);
        TArray<TSharedPtr<FJsonValue>> Steps;
        auto First = Observe(Main, Component.Get(), Identities, KeepAlive); First->SetStringField(TEXT("operation"), TEXT("initialize"));
        First->SetStringField(TEXT("requestedClass"), TEXT("")); Steps.Add(MakeShared<FJsonValueObject>(First));
        for (const auto& Operation : Case->GetArrayField(TEXT("operations")))
        {
            const auto Op = Operation->AsObject(); const FString Kind = Op->GetStringField(TEXT("kind")), Alias = Op->GetStringField(TEXT("class"));
            auto* Class = Alias.IsEmpty() ? nullptr : Classes.FindRef(Alias); if (!Alias.IsEmpty() && !Class) return Fail(__LINE__);
            if (Kind == TEXT("link")) Main->LinkAnimClassLayers(Class);
            else if (Kind == TEXT("unlink")) Main->UnlinkAnimClassLayers(Class);
            else return Fail(__LINE__);
            auto Step = Observe(Main, Component.Get(), Identities, KeepAlive);
            Step->SetStringField(TEXT("operation"), Kind); Step->SetStringField(TEXT("requestedClass"), Class ? Class->GetPathName() : FString());
            Steps.Add(MakeShared<FJsonValueObject>(Step));
        }
        Result->SetArrayField(TEXT("steps"), Steps); Results.Add(MakeShared<FJsonValueObject>(Result));
        Component->UnregisterComponent(); KeepAlive.Empty();
    }
    for (const auto& Snapshot : ClassSnapshots) Snapshot->Restore();
    for (const auto& Snapshot : NodeSnapshots) Snapshot->Restore();
    for (const auto& Entry : Classes)
        if (auto* Generated = Cast<UBlueprintGeneratedClass>(Entry.Value)) Generated->UpdateCustomPropertyListForPostConstruction();
    auto After = MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> RestoredClasses;
    for (const auto& Entry : Classes) RestoredClasses.Add(MakeShared<FJsonValueObject>(ClassDefinition(Entry.Value)));
    After->SetArrayField(TEXT("classes"), RestoredClasses); After->SetArrayField(TEXT("calls"), CallDefinitions(MainClass));
    if (Before != Json(After)) return Fail(__LINE__);
    auto Output = MakeShared<FJsonObject>(); Output->SetNumberField(TEXT("schemaVersion"), 1);
    Output->SetStringField(TEXT("mainClass"), MainClass->GetPathName()); Output->SetBoolField(TEXT("metadataRestored"), true);
    Output->SetBoolField(TEXT("posesEvaluated"), false); Output->SetArrayField(TEXT("cases"), Results); return Json(Output);
}
