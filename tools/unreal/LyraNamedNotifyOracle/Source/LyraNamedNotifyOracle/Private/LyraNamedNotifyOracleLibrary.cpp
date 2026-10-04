#include "LyraNamedNotifyOracleLibrary.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequenceBase.h"
#include "Animation/AnimNotifyQueue.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace NamedProbe
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_NAMED_NOTIFY_FAILED line=%d"),L);return {};}
struct FAccess:UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}
    static void Dispatch(UAnimInstance* A){(A->*&FAccess::TriggerAnimNotifies)(0,ENotifyTriggerMode::ForceAnimGraphOnly);}
};
struct FProxyAccess:FAnimInstanceProxy
{static void Init(FAnimInstanceProxy& P,UAnimInstance* A){(P.*&FProxyAccess::Initialize)(A);}};
void Field(USkeletalMeshComponent* C,const TCHAR* Name,UAnimInstance* A)
{FindFProperty<FObjectPropertyBase>(C->GetClass(),Name)->SetObjectPropertyValue_InContainer(C,A);}
}
FString ULyraNamedNotifyOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace NamedProbe;TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MC=LoadObject<UClass>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C"));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny"));if(!MC||!Mesh)return Fail(__LINE__);
    TMap<FString,TStrongObjectPtr<UAnimSequenceBase>> Assets;TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        auto T=TV->AsObject();TStrongObjectPtr<UWorld> World(NewObject<UWorld>());World->WorldType=EWorldType::GamePreview;
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(World.Get()));C->SetSkeletalMeshAsset(Mesh);
        TArray<TStrongObjectPtr<UAnimInstance>> Instances;TArray<TStrongObjectPtr<ULyraNamedOracleObserver>> Observers;
        TArray<FString> Rows;TArray<TSharedPtr<FJsonValue>> Frames;
        TMap<int32,TArray<TSharedPtr<FJsonValue>>> Actions,MethodActions;
        TFunction<void(const TSharedPtr<FJsonObject>&)> Command;
        for(int32 I=0;I<4;++I)
        {
            auto* A=NewObject<UAnimInstance>(C.Get(),T->GetBoolField(TEXT("original"))?MC:ULyraNamedOracleInstance::StaticClass());
            FProxyAccess::Init(FAccess::Proxy(A),A);A->bReceiveNotifiesFromLinkedInstances=false;A->bPropagateNotifiesToLinkedInstances=false;
            if(auto* Controlled=Cast<ULyraNamedOracleInstance>(A))Controlled->Invoke=[&,I](FName Name,bool Null)
            {Rows.Add(FString::Printf(TEXT("method:%d:%s:%s"),I,*Name.ToString(),Null?TEXT("null"):TEXT("object")));
             if(auto* Pending=MethodActions.Find(I)){auto Commands=MoveTemp(*Pending);MethodActions.Remove(I);for(const auto& V:Commands)Command(V->AsObject());}};
            Instances.Emplace(A);
        }
        Field(C.Get(),TEXT("AnimScriptInstance"),Instances[0].Get());
        FLinkedInstancesAdapter::AddLinkedInstance(C.Get(),Instances[1].Get());FLinkedInstancesAdapter::AddLinkedInstance(C.Get(),Instances[2].Get());
        Field(C.Get(),TEXT("PostProcessAnimInstance"),Instances[3].Get());
        Command=[&](const TSharedPtr<FJsonObject>& O)
        {
            FString Op=O->GetStringField(TEXT("op"));int32 Target=O->GetIntegerField(TEXT("target"));auto* A=Instances[Target].Get();
            if(Op==TEXT("flags")){A->bReceiveNotifiesFromLinkedInstances=O->GetBoolField(TEXT("receive"));A->bPropagateNotifiesToLinkedInstances=O->GetBoolField(TEXT("propagate"));return;}
            if(Op==TEXT("intercept")){CastChecked<ULyraNamedOracleInstance>(A)->Intercept=O->GetBoolField(TEXT("value"));return;}
            if(Op==TEXT("unlink")){FLinkedInstancesAdapter::RemoveLinkedInstance(C.Get(),A);return;}
            if(Op==TEXT("link")){FLinkedInstancesAdapter::AddLinkedInstance(C.Get(),A);return;}
            if(Op==TEXT("post")){Field(C.Get(),TEXT("PostProcessAnimInstance"),O->GetBoolField(TEXT("value"))?A:nullptr);return;}
            if(Op==TEXT("methodReact")){MethodActions.Add(Target,O->GetArrayField(TEXT("commands")));return;}
            if(Op==TEXT("dispatch")){FAccess::Dispatch(A);return;}
            int32 Owner=O->GetIntegerField(TEXT("owner"));auto* Observer=Observers[Owner].Get();
            if(Op==TEXT("kill")){Observer->MarkAsGarbage();return;}
            if(Op==TEXT("react")){Actions.Add(Owner,O->GetArrayField(TEXT("commands")));return;}
            FName Function(*("AnimNotify_"+O->GetStringField(TEXT("name"))));
            if(Op==TEXT("add"))A->AddExternalNotifyHandler(Observer,Function);
            else if(Op==TEXT("remove"))A->RemoveExternalNotifyHandler(Observer,Function);
        };
        for(int32 I=0;I<5;++I)
        {
            auto* Observer=NewObject<ULyraNamedOracleObserver>(C.Get());
            Observer->Invoke=[&,I](FName Name)
            {
                Rows.Add(FString::Printf(TEXT("external:%d:%s"),I,*Name.ToString()));
                if(auto* Pending=Actions.Find(I)){auto Commands=MoveTemp(*Pending);Actions.Remove(I);for(const auto& V:Commands)Command(V->AsObject());}
            };
            Observers.Emplace(Observer);
        }
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());Rows.Reset();auto F=FV->AsObject();
            for(const auto& O:F->GetArrayField(TEXT("commands")))Command(O->AsObject());
            int32 Sender=F->GetIntegerField(TEXT("sender"));auto* A=Instances[Sender].Get();A->ClearQueuedAnimEvents(true);
            for(const auto& WV:F->GetArrayField(TEXT("windows")))
            {
                auto W=WV->AsObject();FString Path=W->GetStringField(TEXT("asset"));if(!Assets.Contains(Path))Assets.Add(Path,TStrongObjectPtr<UAnimSequenceBase>(LoadObject<UAnimSequenceBase>(nullptr,*Path)));
                auto* Asset=Assets[Path].Get();if(!Asset)return Fail(__LINE__);float Time=W->GetNumberField(TEXT("current"));FAnimTickRecord Tick;Tick.TimeAccumulator=&Time;Tick.bActiveContext=true;
                FAnimNotifyContext Context(Tick);Asset->GetAnimNotifies(W->GetNumberField(TEXT("previous")),W->GetNumberField(TEXT("delta")),Context);
                A->NotifyQueue.AddAnimNotifies(true,Context.ActiveNotifies,1);
            }
            A->NotifyQueue.AnimNotifies.RemoveAll([](const FAnimNotifyEventReference& R){const auto* N=R.GetNotify();return N->Notify||N->NotifyStateClass;});
            FAccess::Dispatch(A);
            auto R=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Events;for(const auto& Row:Rows)Events.Add(MakeShared<FJsonValueString>(Row));R->SetArrayField(TEXT("events"),Events);Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
        FLinkedInstancesAdapter::ResetLinkedInstance(C.Get());Field(C.Get(),TEXT("AnimScriptInstance"),nullptr);Field(C.Get(),TEXT("PostProcessAnimInstance"),nullptr);
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Result;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result));return Result;
}
