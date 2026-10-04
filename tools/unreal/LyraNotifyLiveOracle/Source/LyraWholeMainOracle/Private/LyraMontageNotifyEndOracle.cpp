#include "LyraWholeMainOracleLibrary.h"
#include "LyraNotifyEndProbeState.h"
#include "LyraMontageDelegateListener.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "UObject/StrongObjectPtr.h"
#include "Serialization/JsonSerializer.h"

void ULyraNotifyEndProbeState::NotifyEnd(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference&)
{if(Record)Record(Tag);}
bool ULyraNotifyEndProbeInstance::ShouldTriggerAnimNotifyState(const UAnimNotifyState* State) const
{const auto* Probe=Cast<ULyraNotifyEndProbeState>(State);return !Probe||Probe->Tag!=SkippedTag;}

namespace LyraNotifyEnd
{
// Explicit instantiation permits calling the real private container dispatcher
// without changing engine source or dispatching unrelated resource Tick events.
struct FDispatchTag{using Type=void(UAnimInstance::*)();friend Type Access(FDispatchTag);};
template<class Tag,typename Tag::Type Member>struct FPrivateAccess
{friend typename Tag::Type Access(Tag){return Member;}};
template struct FPrivateAccess<FDispatchTag,&UAnimInstance::TriggerQueuedMontageEvents>;
struct FTag:UE::Anim::IAnimNotifyEventContextDataInterface
{
    DECLARE_NOTIFY_CONTEXT_INTERFACE(FTag)
public:
    int32 Id;explicit FTag(int32 In):Id(In){}
};
IMPLEMENT_NOTIFY_CONTEXT_INTERFACE(FTag)
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_NOTIFY_END_FAILED line=%d"),Line);return {};}
TArray<TSharedPtr<FJsonValue>> Snapshot(UAnimInstance* A)
{
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 I=0;I<A->ActiveAnimNotifyState.Num();I++)
    {
        const auto& Ref=A->ActiveAnimNotifyEventReference[I];auto Row=MakeShared<FJsonObject>();
        const auto* Tag=Ref.GetContextData<FTag>();const auto* Context=Ref.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>();
        Row->SetNumberField(TEXT("id"),Tag?Tag->Id:-1);Row->SetNumberField(TEXT("instance"),Context?Context->MontageInstanceID:0);
        Row->SetBoolField(TEXT("direct"),Cast<UAnimMontage>(A->ActiveAnimNotifyState[I].NotifyStateClass->GetOuter())!=nullptr);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    return Rows;
}
}

FString ULyraWholeMainOracleLibrary::ReadMontageNotifyEnd(const FString& RequestsJson)
{
    using namespace LyraNotifyEnd;
    TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Manny)return Fail(__LINE__);
    TArray<UAnimMontage*> Assets;
    for(const auto& V:Q->GetArrayField(TEXT("assets")))
    {auto* M=LoadObject<UAnimMontage>(nullptr,*V->AsString());if(!M)return Fail(__LINE__);Assets.Add(M);}
    auto* Sequence=LoadObject<UAnimSequence>(nullptr,*Q->GetStringField(TEXT("sequence")));if(!Sequence)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& V:Q->GetArrayField(TEXT("cases")))
    {
        auto Request=V->AsObject();const FString Mode=Request->GetStringField(TEXT("mode"));
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        auto* Character=World->SpawnActor<ACharacter>();if(!Character)return Fail(__LINE__);
        auto* Mesh=Character->GetMesh();Mesh->SetSkeletalMeshAsset(Manny);Mesh->SetAnimInstanceClass(ULyraNotifyEndProbeInstance::StaticClass());
        auto* A=Cast<ULyraNotifyEndProbeInstance>(Mesh->GetAnimInstance());
        if(!A)return Fail(__LINE__);
        TArray<TSharedPtr<FJsonValue>> Calls;
        auto* Listener=NewObject<ULyraMontageDelegateListener>(Character);auto* Next=NewObject<ULyraMontageDelegateListener>(Character);
        auto Record=[&](const FString& Who,int32 Id,int32 Instance)
        {auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("listener"),Who);R->SetNumberField(TEXT("id"),Id);R->SetNumberField(TEXT("instance"),Instance);
         R->SetArrayField(TEXT("active"),Snapshot(A));Calls.Add(MakeShared<FJsonValueObject>(R));};
        Listener->Record=[&](int32,UAnimMontage*,bool,FName,bool){Record(TEXT("global"),-1,0);};
        Next->Record=[&](int32,UAnimMontage*,bool,FName,bool){Record(TEXT("new-global"),-1,0);};
        A->OnMontageEnded.AddDynamic(Listener,&ULyraMontageDelegateListener::End);
        TFunction<void(int32)> End;
        End=[&](int32 Instance)
        {FOnMontageEnded D;D.BindLambda([&,Instance](UAnimMontage*,bool){Record(TEXT("instance"),-1,Instance);});
         A->QueueMontageEndedEvent({Assets[0],Instance,false,D});};
        TFunction<void(const TSharedPtr<FJsonObject>&)> Append;
        Append=[&](const TSharedPtr<FJsonObject>& R)
        {
            const int32 Id=R->GetIntegerField(TEXT("id")),Instance=R->GetIntegerField(TEXT("instance"));
            const bool Direct=R->GetBoolField(TEXT("direct"));UAnimNotifyState* State=nullptr;
            if(R->GetBoolField(TEXT("original")))
            {const int32 Asset=R->GetIntegerField(TEXT("asset")),Index=R->GetIntegerField(TEXT("notify"));
             if(!Assets.IsValidIndex(Asset)||!Assets[Asset]->Notifies.IsValidIndex(Index))return;
             State=Assets[Asset]->Notifies[Index].NotifyStateClass;}
            else
            {
                auto* Probe=NewObject<ULyraNotifyEndProbeState>(Direct?static_cast<UObject*>(Assets[0]):Sequence,NAME_None,RF_Transient);
                Probe->Tag=Id;Probe->Record=[&,Instance](int32 Tag)
                {
                    Record(TEXT("notify"),Tag,Instance);
                    if(Mode==TEXT("clear")){A->ActiveAnimNotifyState.Reset();A->ActiveAnimNotifyEventReference.Reset();}
                    if(Mode==TEXT("nested")&&Tag==2)End(2);
                    if(Mode==TEXT("append")&&Tag==0)
                    {auto Child=MakeShared<FJsonObject>();Child->SetNumberField(TEXT("id"),2);Child->SetNumberField(TEXT("instance"),1);
                     Child->SetBoolField(TEXT("direct"),true);Child->SetBoolField(TEXT("context"),true);Child->SetBoolField(TEXT("original"),false);Child->SetBoolField(TEXT("trigger"),true);Append(Child);}
                    if(Mode==TEXT("rebind"))
                    {A->OnMontageEnded.RemoveDynamic(Listener,&ULyraMontageDelegateListener::End);A->OnMontageEnded.AddDynamic(Next,&ULyraMontageDelegateListener::End);}
                };
                if(!R->GetBoolField(TEXT("trigger")))A->SkippedTag=Id;State=Probe;
            }
            if(!State)return;
            FAnimNotifyEvent Event;Event.NotifyStateClass=State;Event.SetDuration(.4f);A->ActiveAnimNotifyState.Add(Event);
            FAnimNotifyEventReference Ref(&A->ActiveAnimNotifyState.Last(),Cast<UAnimSequenceBase>(State->GetOuter()));
            Ref.AddContextData<FTag>(Id);
            if(R->GetBoolField(TEXT("context")))Ref.AddContextData<UE::Anim::FAnimNotifyMontageInstanceContext>(Instance);
            A->ActiveAnimNotifyEventReference.Add(Ref);
            // Reallocation of active event copies must not leave stale pointers.
            for(int32 I=0;I<A->ActiveAnimNotifyEventReference.Num();I++)A->ActiveAnimNotifyEventReference[I].SetNotify(&A->ActiveAnimNotifyState[I]);
        };
        for(const auto& R:Request->GetArrayField(TEXT("states")))Append(R->AsObject());
        auto Result=MakeShared<FJsonObject>();Result->SetStringField(TEXT("mode"),Mode);Result->SetArrayField(TEXT("before"),Snapshot(A));
        if(Mode==TEXT("queued"))
        {
            // Montage_Advance establishes queueing even for an empty bank.
            struct FQueueAccess:UAnimInstance{static void Begin(UAnimInstance* I){(I->*&FQueueAccess::Montage_Advance)(0);}};
            FQueueAccess::Begin(A);End(1);Result->SetArrayField(TEXT("pendingCalls"),Calls);(A->*Access(FDispatchTag{}))();
        }
        else End(1);
        Result->SetArrayField(TEXT("calls"),Calls);Result->SetArrayField(TEXT("after"),Snapshot(A));Traces.Add(MakeShared<FJsonValueObject>(Result));
        // Disable test callbacks before normal world teardown.
        A->ActiveAnimNotifyState.Reset();A->ActiveAnimNotifyEventReference.Reset();A->OnMontageEnded.Clear();World->DestroyWorld(false);
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("cases"),Traces);FString Json;
    FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
