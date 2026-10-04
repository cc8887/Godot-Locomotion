#include "LyraWeaponNotifyOracleLibrary.h"
#include "Equipment/LyraEquipmentManagerComponent.h"
#include "Equipment/LyraEquipmentDefinition.h"
#include "Equipment/LyraEquipmentInstance.h"
#include "Weapons/LyraWeaponInstance.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Components/SkeletalMeshComponent.h"
#include "GameFramework/Pawn.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/Blueprint.h"
#include "Engine/World.h"
#include "EdGraph/EdGraph.h"
#include "K2Node_CallFunction.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/StrongObjectPtr.h"

namespace {
FString Text(TSharedPtr<FJsonObject> Object){FString Result;FJsonSerializer::Serialize(Object,TJsonWriterFactory<>::Create(&Result));return Result;}
const TCHAR* Names[]={TEXT("Pistol"),TEXT("Rifle"),TEXT("Shotgun")};
FString DefinitionPath(int32 Index){return FString::Printf(TEXT("/ShooterCore/Weapons/%s/WID_%s.WID_%s_C"),Names[Index],Names[Index],Names[Index]);}
}
FString ULyraWeaponNotifyOracleLibrary::ReadPolicy()
{
    auto Blueprint=LoadObject<UBlueprint>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/AnimNotifies/AN_PlayWeaponMontage.AN_PlayWeaponMontage"));if(!Blueprint)return {};
    TArray<TSharedPtr<FJsonValue>> Calls,Equipment;
    for(auto Graph:Blueprint->FunctionGraphs)for(auto Node:Graph->Nodes)if(auto Call=Cast<UK2Node_CallFunction>(Node))
    {
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("function"),Call->GetFunctionName().ToString());Row->SetStringField(TEXT("node"),Call->NodeGuid.ToString());
        TArray<TSharedPtr<FJsonValue>> Pins;
        for(auto Pin:Call->Pins)
        {
            auto Value=MakeShared<FJsonObject>();Value->SetStringField(TEXT("name"),Pin->PinName.ToString());Value->SetBoolField(TEXT("input"),Pin->Direction==EGPD_Input);
            Value->SetStringField(TEXT("defaultValue"),Pin->DefaultValue);Value->SetStringField(TEXT("defaultObject"),Pin->DefaultObject?Pin->DefaultObject->GetPathName():FString());
            TArray<TSharedPtr<FJsonValue>> Links;for(auto Link:Pin->LinkedTo)Links.Add(MakeShared<FJsonValueString>(Link->GetOwningNode()->NodeGuid.ToString()+TEXT(":")+Link->PinName.ToString()));
            Value->SetArrayField(TEXT("links"),Links);Pins.Add(MakeShared<FJsonValueObject>(Value));
        }
        Row->SetArrayField(TEXT("pins"),Pins);Calls.Add(MakeShared<FJsonValueObject>(Row));
    }
    for(int32 I=0;I<3;I++)
    {
        auto Class=LoadClass<ULyraEquipmentDefinition>(nullptr,*DefinitionPath(I));if(!Class)return {};
        auto CDO=Class->GetDefaultObject<ULyraEquipmentDefinition>();auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("name"),Names[I]);Row->SetStringField(TEXT("definition"),Class->GetPathName());
        Row->SetStringField(TEXT("instanceType"),CDO->InstanceType->GetPathName());TArray<TSharedPtr<FJsonValue>> Actors;
        for(const auto& Info:CDO->ActorsToSpawn)
        {
            auto Actor=MakeShared<FJsonObject>();Actor->SetStringField(TEXT("class"),Info.ActorToSpawn->GetPathName());Actor->SetStringField(TEXT("socket"),Info.AttachSocket.ToString());Actor->SetStringField(TEXT("transform"),Info.AttachTransform.ToString());Actors.Add(MakeShared<FJsonValueObject>(Actor));
        }
        Row->SetArrayField(TEXT("actors"),Actors);Equipment.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("calls"),Calls);Result->SetArrayField(TEXT("equipment"),Equipment);return Text(Result);
}
FString ULyraWeaponNotifyOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    TSharedPtr<FJsonObject> Request;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Request))return {};
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(const auto& InputValue:Request->GetArrayField(TEXT("calls")))
    {
        auto Input=InputValue->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).RequiresHitProxies(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::PIE,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return {};
        auto Pawn=World->SpawnActor<APawn>();auto Mesh=NewObject<USkeletalMeshComponent>(Pawn);Pawn->AddInstanceComponent(Mesh);Pawn->SetRootComponent(Mesh);
        auto CharacterSkin=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny"));if(!CharacterSkin)return {};
        Mesh->SetSkeletalMesh(CharacterSkin);Mesh->SetAnimationMode(EAnimationMode::AnimationBlueprint);Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());Mesh->RegisterComponent();
        auto Manager=NewObject<ULyraEquipmentManagerComponent>(Pawn);Pawn->AddInstanceComponent(Manager);Manager->RegisterComponent();
        const int32 Kind=Input->GetIntegerField(TEXT("kind")),Scenario=Input->GetIntegerField(TEXT("scenario"));
        auto Class=LoadClass<ULyraEquipmentDefinition>(nullptr,*DefinitionPath(Kind));if(!Class)return {};
        ULyraEquipmentInstance* First=nullptr;ULyraEquipmentInstance* Second=nullptr;
        if(Scenario!=1)First=Manager->EquipItem(Class);
        if(Scenario==2)Second=Manager->EquipItem(LoadClass<ULyraEquipmentDefinition>(nullptr,*DefinitionPath((Kind+1)%3)));
        auto Asset=LoadObject<UAnimMontage>(nullptr,*Input->GetStringField(TEXT("asset")));const int32 Index=Input->GetIntegerField(TEXT("index"));if(!Asset||!Asset->Notifies.IsValidIndex(Index))return {};
        auto CharacterAnimation=Mesh->GetAnimInstance();if(!CharacterAnimation)return {};
        const float LeaderLength=CharacterAnimation->Montage_Play(Asset,1.f);if(LeaderLength<=0)return {};
        auto Notify=Asset->Notifies[Index].Notify;if(!Notify)return {};
        auto Follower=First&&First->GetSpawnedActors().Num()?First->GetSpawnedActors()[0]->FindComponentByClass<USkeletalMeshComponent>():nullptr;
        if(Scenario==3&&Follower)Follower->SetAnimInstanceClass(nullptr);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("kind"),Kind);Row->SetNumberField(TEXT("scenario"),Scenario);Row->SetStringField(TEXT("object"),Notify->GetPathName());
        Row->SetBoolField(TEXT("returnValue"),Notify->Received_Notify(Mesh,Asset,FAnimNotifyEventReference(&Asset->Notifies[Index],Asset)));
        Row->SetNumberField(TEXT("leaderLength"),LeaderLength);TArray<TSharedPtr<FJsonValue>> Weapons;
        for(auto Instance:Manager->GetEquipmentInstancesOfType(ULyraWeaponInstance::StaticClass()))for(auto Actor:Instance->GetSpawnedActors())
        {
            auto Weapon=MakeShared<FJsonObject>();auto Component=Actor->FindComponentByClass<USkeletalMeshComponent>();auto Animation=Component?Component->GetAnimInstance():nullptr;
            Weapon->SetStringField(TEXT("actorClass"),Actor->GetClass()->GetPathName());Weapon->SetStringField(TEXT("mesh"),Component&&Component->GetSkeletalMeshAsset()?Component->GetSkeletalMeshAsset()->GetPathName():FString());
            Weapon->SetStringField(TEXT("animationClass"),Animation?Animation->GetClass()->GetPathName():FString());
            auto Montage=Animation?Animation->GetCurrentActiveMontage():nullptr;auto State=Montage?Animation->GetActiveInstanceForMontage(Montage):nullptr;
            Weapon->SetStringField(TEXT("montage"),Montage?Montage->GetPathName():FString());Weapon->SetNumberField(TEXT("rate"),State?State->GetPlayRate():0);Weapon->SetNumberField(TEXT("position"),State?State->GetPosition():0);
            Weapon->SetBoolField(TEXT("following"),State&&State->GetMontageSyncLeader()!=nullptr);Weapons.Add(MakeShared<FJsonValueObject>(Weapon));
        }
        Row->SetArrayField(TEXT("weapons"),Weapons);Rows.Add(MakeShared<FJsonValueObject>(Row));
        World->DestroyWorld(false);
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("calls"),Rows);return Text(Result);
}
