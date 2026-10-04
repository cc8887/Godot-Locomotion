#include "LyraGameplayNotifyOracleLibrary.h"
#include "AbilitySystemComponent.h"
#include "Animation/AnimSequenceBase.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifyQueue.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/StrongObjectPtr.h"

FString ULyraGameplayNotifyOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    TSharedPtr<FJsonObject> Requests;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    const UWorld::InitializationValues Init=UWorld::InitializationValues().AllowAudioPlayback(false)
        .RequiresHitProxies(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::PIE,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid())return {};
    AActor* Actors[2];USkeletalMeshComponent* Meshes[3];UAbilitySystemComponent* Receivers[2];
    TArray<TSharedPtr<FJsonValue>> Events,Calls;
    int32 CurrentCall=-1;
    for(int32 Role=0;Role<2;Role++)
    {
        Actors[Role]=World->SpawnActor<AActor>();
        Meshes[Role]=NewObject<USkeletalMeshComponent>(Actors[Role]);Actors[Role]->AddInstanceComponent(Meshes[Role]);
        Receivers[Role]=NewObject<UAbilitySystemComponent>(Actors[Role]);Actors[Role]->AddInstanceComponent(Receivers[Role]);
        Receivers[Role]->RegisterComponent();
        Receivers[Role]->InitAbilityActorInfo(Actors[Role],Actors[Role]);
        const auto Delegate=FGameplayEventTagMulticastDelegate::FDelegate::CreateLambda(
            [&,Role](FGameplayTag Tag,const FGameplayEventData* Data)
            {
                auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("call"),CurrentCall);Row->SetNumberField(TEXT("actor"),Role);
                Row->SetStringField(TEXT("tag"),Tag.ToString());Row->SetStringField(TEXT("payloadEventTag"),Data->EventTag.ToString());
                Row->SetNumberField(TEXT("magnitude"),Data->EventMagnitude);
                Row->SetBoolField(TEXT("defaultPayload"),!Data->Instigator&&!Data->Target&&!Data->OptionalObject&&!Data->OptionalObject2&&
                    !Data->ContextHandle.IsValid()&&Data->InstigatorTags.IsEmpty()&&Data->TargetTags.IsEmpty()&&Data->TargetData.Num()==0);
                Events.Add(MakeShared<FJsonValueObject>(Row));
            });
        Receivers[Role]->AddGameplayEventTagContainerDelegate(FGameplayTagContainer(),Delegate);
    }
    TStrongObjectPtr<USkeletalMeshComponent> Orphan(NewObject<USkeletalMeshComponent>(World.Get()));Meshes[2]=Orphan.Get();
    TMap<FString,TStrongObjectPtr<UAnimSequenceBase>> Assets;
    for(const auto& Value:Requests->GetArrayField(TEXT("calls")))
    {
        auto Input=Value->AsObject();const FString Path=Input->GetStringField(TEXT("asset"));
        auto Found=Assets.Find(Path);
        if(!Found){auto Asset=LoadObject<UAnimSequenceBase>(nullptr,*Path);if(!Asset)return {};Assets.Add(Path,TStrongObjectPtr<UAnimSequenceBase>(Asset));Found=Assets.Find(Path);}
        auto Asset=Found->Get();const int32 Index=Input->GetIntegerField(TEXT("index")),Role=Input->GetIntegerField(TEXT("actor"));
        if(!Asset->Notifies.IsValidIndex(Index)||Role<0||Role>2||!Asset->Notifies[Index].Notify)return {};
        const auto& Definition=Asset->Notifies[Index];CurrentCall++;
        // Execute the actual generated Blueprint function on the actual authored
        // notify object. No replacement Notify class or copied BP algorithm.
        const bool Result=Definition.Notify->Received_Notify(Meshes[Role],Asset,FAnimNotifyEventReference(&Definition,Asset));
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("object"),Definition.Notify->GetPathName());
        Row->SetStringField(TEXT("class"),Definition.Notify->GetClass()->GetPathName());Row->SetBoolField(TEXT("returnValue"),Result);
        Row->SetNumberField(TEXT("eventCount"),Events.Num());Calls.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("calls"),Calls);Result->SetArrayField(TEXT("events"),Events);
    FString Text;auto Writer=TJsonWriterFactory<>::Create(&Text);const bool Success=FJsonSerializer::Serialize(Result,Writer);
    World->DestroyWorld(false);return Success?Text:FString();
}
