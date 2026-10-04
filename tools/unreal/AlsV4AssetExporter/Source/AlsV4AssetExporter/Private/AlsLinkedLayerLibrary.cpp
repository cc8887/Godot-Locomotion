#include "AlsLinkedLayerLibrary.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendProfile.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
TArray<TSharedPtr<FJsonValue>> Names(const TArray<FName>& Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const FName Value : Values) Result.Add(MakeShared<FJsonValueString>(Value.ToString()));
    return Result;
}
FString Path(const UObject* Value) { return Value ? Value->GetPathName() : FString(); }
}

FString UAlsLinkedLayerLibrary::ReadLinkedLayerClass(UClass* AnimationClass)
{
    if (!AnimationClass) return {};
    const IAnimClassInterface* Interface = IAnimClassInterface::GetFromClass(AnimationClass);
    if (!Interface) return {};
    const UObject* Defaults = AnimationClass->GetDefaultObject();
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("class"), AnimationClass->GetPathName());
    Result->SetStringField(TEXT("skeleton"), Path(Interface->GetTargetSkeleton()));
    TArray<TSharedPtr<FJsonValue>> Functions, Nodes;
    for (const FAnimBlueprintFunction& Function : Interface->GetAnimBlueprintFunctions())
    {
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("name"), Function.Name.ToString());
        Row->SetStringField(TEXT("group"), Function.Group.IsNone() ? FString() : Function.Group.ToString());
        Row->SetBoolField(TEXT("implemented"), Function.bImplemented);
        Row->SetArrayField(TEXT("inputPoses"), Names(Function.InputPoseNames));
        TArray<TSharedPtr<FJsonValue>> Properties;
        for (const auto& Input : Function.InputPropertyData)
        {
            const TSharedRef<FJsonObject> Property = MakeShared<FJsonObject>();
            Property->SetStringField(TEXT("name"), Input.Name.ToString());
            Property->SetStringField(TEXT("functionType"), Input.FunctionProperty ? Input.FunctionProperty->GetCPPType() : FString());
            Property->SetStringField(TEXT("classType"), Input.ClassProperty ? Input.ClassProperty->GetCPPType() : FString());
            Properties.Add(MakeShared<FJsonValueObject>(Property));
        }
        Row->SetArrayField(TEXT("inputProperties"), Properties);
        const FAnimGraphBlendOptions* Blend = Interface->GetGraphBlendOptions().Find(Function.Name);
        const FAnimGraphBlendOptions DefaultBlend;
        if (!Blend) Blend = &DefaultBlend;
        Row->SetNumberField(TEXT("blendInTime"), Blend->BlendInTime);
        Row->SetNumberField(TEXT("blendOutTime"), Blend->BlendOutTime);
        Row->SetStringField(TEXT("blendInProfile"), Path(Blend->BlendInProfile));
        Row->SetStringField(TEXT("blendOutProfile"), Path(Blend->BlendOutProfile));
        Functions.Add(MakeShared<FJsonValueObject>(Row));
    }
    for (const FStructProperty* Property : Interface->GetLinkedAnimLayerNodeProperties())
    {
        const FAnimNode_LinkedAnimLayer* Node = Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Defaults);
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("node"), Property->GetName());
        Row->SetStringField(TEXT("layer"), Node->Layer.ToString());
        Row->SetStringField(TEXT("interface"), Path(Node->Interface.Get()));
        Row->SetStringField(TEXT("instanceClass"), Path(Node->InstanceClass.Get()));
        Row->SetArrayField(TEXT("inputPoses"), Names(Node->InputPoseNames));
        Row->SetBoolField(TEXT("receiveNotifies"), Node->bReceiveNotifiesFromLinkedInstances);
        Row->SetBoolField(TEXT("propagateNotifies"), Node->bPropagateNotifiesToLinkedInstances);
        Nodes.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("functions"), Functions);
    Result->SetArrayField(TEXT("linkedNodes"), Nodes);
    if (const UAnimInstance* Instance = Cast<UAnimInstance>(Defaults))
    {
        Result->SetBoolField(TEXT("receiveNotifies"), Instance->GetReceiveNotifiesFromLinkedInstances());
        Result->SetBoolField(TEXT("propagateNotifies"), Instance->GetPropagateNotifiesToLinkedInstances());
        Result->SetBoolField(TEXT("useMainMontageData"), Instance->IsUsingMainInstanceMontageEvaluationData());
    }
    FString Json;
    FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json));
    return Json;
}

FString UAlsLinkedLayerLibrary::ReadLinkedLayerBindingSequence(UClass* MainClass, USkeletalMesh* Mesh,
                                                              const TArray<UClass*>& LayerClasses)
{
    if (!MainClass || !MainClass->IsChildOf(UAnimInstance::StaticClass()) || !Mesh || LayerClasses.IsEmpty()) return {};
    const IAnimClassInterface* MainInterface = IAnimClassInterface::GetFromClass(MainClass);
    if (!MainInterface) return {};
    for (UClass* LayerClass : LayerClasses)
        if (!LayerClass || !LayerClass->IsChildOf(UAnimInstance::StaticClass()) || !IAnimClassInterface::GetFromClass(LayerClass)) return {};
    const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &Initialization));
    if (!World.IsValid()) return {};
    struct FWorldCleanup { UWorld* Value; ~FWorldCleanup() { Value->DestroyWorld(false); } } Cleanup{World.Get()};
    FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
    AActor* Owner = World->SpawnActor<AActor>(Spawn);
    if (!Owner) return {};
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
    Component->bUseRefPoseOnInitAnim = true;
    Component->SetDisablePostProcessBlueprint(true);
    Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMesh(Mesh);
    Component->SetAnimInstanceClass(MainClass);
    Owner->SetRootComponent(Component.Get());
    Owner->AddInstanceComponent(Component.Get());
    Component->RegisterComponent();
    UAnimInstance* Main = Component->GetAnimInstance();
    if (!Component->IsRegistered() || !Main || Main->GetClass() != MainClass) return {};
    TMap<const UAnimInstance*, int32> Identities;
    TArray<TSharedPtr<FJsonValue>> Steps;
    for (UClass* LayerClass : LayerClasses)
    {
        Main->LinkAnimClassLayers(LayerClass);
        const TSharedRef<FJsonObject> Step = MakeShared<FJsonObject>();
        Step->SetStringField(TEXT("class"), LayerClass->GetPathName());
        Step->SetNumberField(TEXT("linkedInstances"), static_cast<const USkeletalMeshComponent*>(Component.Get())->GetLinkedAnimInstances().Num());
        TArray<TSharedPtr<FJsonValue>> Nodes;
        for (const FStructProperty* Property : MainInterface->GetLinkedAnimLayerNodeProperties())
        {
            const FAnimNode_LinkedAnimLayer* Node = Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            const UAnimInstance* Target = Node->GetTargetInstance<UAnimInstance>();
            if (!Target) return {};
            if (!Identities.Contains(Target)) Identities.Add(Target, Identities.Num());
            const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("node"), Property->GetName());
            Row->SetStringField(TEXT("layer"), Node->Layer.ToString());
            Row->SetNumberField(TEXT("owner"), Identities.FindChecked(Target));
            Row->SetStringField(TEXT("class"), Target->GetClass()->GetPathName());
            Row->SetBoolField(TEXT("receiveNotifies"), Target->GetReceiveNotifiesFromLinkedInstances());
            Row->SetBoolField(TEXT("propagateNotifies"), Target->GetPropagateNotifiesToLinkedInstances());
            Nodes.Add(MakeShared<FJsonValueObject>(Row));
        }
        Step->SetArrayField(TEXT("nodes"), Nodes);
        Steps.Add(MakeShared<FJsonValueObject>(Step));
    }
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("mainClass"), MainClass->GetPathName());
    Result->SetStringField(TEXT("mesh"), Mesh->GetPathName());
    Result->SetArrayField(TEXT("steps"), Steps);
    Component->UnregisterComponent();
    FString Json;
    FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json));
    return Json;
}
