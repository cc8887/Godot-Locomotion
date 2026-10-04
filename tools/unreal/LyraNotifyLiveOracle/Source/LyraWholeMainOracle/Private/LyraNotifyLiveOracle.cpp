#include "LyraWholeMainOracleLibrary.h"
#include "LyraNotifyLiveProbe.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Animation/AnimationInstanceScope.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "HAL/IConsoleManager.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

void ULyraNotifyLiveState::NotifyBegin(USkeletalMeshComponent*,UAnimSequenceBase*,float S,const FAnimNotifyEventReference& R){if(Record)Record(2,S,R);}
void ULyraNotifyLiveState::NotifyTick(USkeletalMeshComponent*,UAnimSequenceBase*,float S,const FAnimNotifyEventReference& R){if(Record)Record(3,S,R);}
void ULyraNotifyLiveState::NotifyEnd(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference& R){if(Record)Record(1,0,R);}
void ULyraNotifyLiveInstant::Notify(USkeletalMeshComponent*,UAnimSequenceBase*,const FAnimNotifyEventReference& R){if(Record)Record(R);}
bool ULyraNotifyLiveInstance::ShouldTriggerAnimNotifyState(const UAnimNotifyState* S)const
{const auto* P=Cast<ULyraNotifyLiveState>(S);return !P||P->Policy!=SkippedPolicy;}
namespace LyraNotifyLive
{
struct FTag:UE::Anim::IAnimNotifyEventContextDataInterface
{
    DECLARE_NOTIFY_CONTEXT_INTERFACE(FTag)
public:
    int32 Policy,Handle;FTag(int32 P,int32 H):Policy(P),Handle(H){}
};
IMPLEMENT_NOTIFY_CONTEXT_INTERFACE(FTag)
TSharedPtr<FJsonObject> State(const FAnimNotifyEventReference& R)
{
    const auto* T=R.GetContextData<FTag>();const auto* M=R.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>();
    const auto* A=R.GetContextData<UE::Anim::FAnimNotifyAssetPlayerInstanceContext>();auto J=MakeShared<FJsonObject>();
    J->SetNumberField(TEXT("policy"),T?T->Policy:-1);J->SetNumberField(TEXT("handle"),T?T->Handle:-1);
    J->SetNumberField(TEXT("instance"),R.GetNotifyInstanceID());J->SetNumberField(TEXT("sourceKind"),M?2:A?1:0);
    J->SetNumberField(TEXT("source"),M?uint32(M->MontageInstanceID):A?A->AssetPlayerInstanceID:0);return J;
}
TArray<TSharedPtr<FJsonValue>> Snapshot(UAnimInstance* A)
{TArray<TSharedPtr<FJsonValue>> R;for(const auto& Ref:A->ActiveAnimNotifyEventReference)R.Add(MakeShared<FJsonValueObject>(State(Ref)));return R;}
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_NOTIFY_LIVE_FAILED line=%d"),L);return {};}
}
FString ULyraWholeMainOracleLibrary::ReadNotifyLive(const FString& RequestsJson)
{
    using namespace LyraNotifyLive;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Manny)return Fail(__LINE__);
    auto* Cvar=IConsoleManager::Get().FindConsoleVariable(TEXT("a.Notifies.DispatchPolicy"));if(!Cvar)return Fail(__LINE__);
    const int32 OldPolicy=Cvar->GetInt();Cvar->Set(1,ECVF_SetByCode);
    TArray<TSharedPtr<FJsonValue>> Results;
    for(const auto& Case:Q->GetArrayField(TEXT("cases")))
    {
        auto Request=Case->AsObject();const FString Action=Request->GetStringField(TEXT("action"));
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        auto* Actor=World->SpawnActor<ACharacter>();if(!Actor)return Fail(__LINE__);auto* Mesh=Actor->GetMesh();
        Mesh->SetSkeletalMeshAsset(Manny);Mesh->SetAnimInstanceClass(ULyraNotifyLiveInstance::StaticClass());
        auto* A=Cast<ULyraNotifyLiveInstance>(Mesh->GetAnimInstance());if(!A)return Fail(__LINE__);
        TStrongObjectPtr<UAnimSequence> Source(NewObject<UAnimSequence>());Source->SetSkeleton(Manny->GetSkeleton());Source->Notifies.SetNum(6);
        TArray<TSharedPtr<FJsonValue>> Calls;bool Recording=false,Mutated=false;
        TFunction<void(const TSharedPtr<FJsonObject>&)> Append;
        TFunction<FAnimNotifyEventReference(const TSharedPtr<FJsonObject>&)> Ref;
        TFunction<void(int32,float,const FAnimNotifyEventReference&)> Record;
        TFunction<void()> Child;
        Record=[&](int32 Kind,float Seconds,const FAnimNotifyEventReference& R)
        {
            if(!Recording)return;auto J=MakeShared<FJsonObject>();J->SetNumberField(TEXT("kind"),Kind);
            J->SetNumberField(TEXT("seconds"),Seconds);J->SetObjectField(TEXT("state"),State(R));J->SetArrayField(TEXT("active"),Snapshot(A));Calls.Add(MakeShared<FJsonValueObject>(J));
            const auto* T=R.GetContextData<FTag>();const int32 Handle=T?T->Handle:-1;
            if(!Mutated&&((Action==TEXT("begin-clear")&&Kind==2)||(Action==TEXT("tick-clear")&&Kind==3)||(Action==TEXT("instant-clear")&&Kind==0&&T&&T->Policy==4)))
            {Mutated=true;A->ActiveAnimNotifyState.Reset();A->ActiveAnimNotifyEventReference.Reset();}
            if(!Mutated&&((Action==TEXT("tick-append")&&Kind==3)||(Action==TEXT("end-append")&&Kind==1)))
            {Mutated=true;Append(Request->GetObjectField(TEXT("append")));}
            if(!Mutated&&((Action==TEXT("nested-tick")&&Kind==3&&Handle==0)||(Action==TEXT("nested-begin")&&Kind==2)))
            {Mutated=true;Child();}
        };
        for(int32 P=0;P<6;P++)
        {
            auto& E=Source->Notifies[P];E.SetDuration(P<4?.4f:0);E.NotifyName=P==5?FName(TEXT("ProbeNamed")):FName(*FString::Printf(TEXT("Probe%d"),P));
            if(P<4){auto* S=NewObject<ULyraNotifyLiveState>(Source.Get());S->Policy=P;S->NotifyStateBehaviorFlags=P==2?uint8(EAnimNotifyStateBehaviorFlags::NoMergeOnConcurrentPlay):0;S->Record=Record;E.NotifyStateClass=S;}
            if(P==4){auto* N=NewObject<ULyraNotifyLiveInstant>(Source.Get());N->Record=[&](const FAnimNotifyEventReference& R){Record(0,0,R);};E.Notify=N;}
        }
        Ref=[&](const TSharedPtr<FJsonObject>& R)
        {
            const int32 P=R->GetIntegerField(TEXT("policy")),H=R->GetIntegerField(TEXT("handle")),K=R->GetIntegerField(TEXT("sourceKind")),I=R->GetIntegerField(TEXT("source"));
            FAnimNotifyEventReference Value(&Source->Notifies[P],Source.Get());Value.AddContextData<FTag>(P,H);
            if(K==1)Value.AddContextData<UE::Anim::FAnimNotifyAssetPlayerInstanceContext>(uint32(I));
            if(K==2)Value.AddContextData<UE::Anim::FAnimNotifyMontageInstanceContext>(I);
            Value.SetNotifyInstanceID(R->GetIntegerField(TEXT("instance")));return Value;
        };
        Append=[&](const TSharedPtr<FJsonObject>& R)
        {
            auto Value=Ref(R);A->ActiveAnimNotifyState.Add(*Value.GetNotify());A->ActiveAnimNotifyEventReference.Add(Value);
            for(int32 I=0;I<A->ActiveAnimNotifyState.Num();I++)A->ActiveAnimNotifyEventReference[I].SetNotify(&A->ActiveAnimNotifyState[I]);
        };
        for(const auto& R:Request->GetArrayField(TEXT("before")))Append(R->AsObject());
        A->SkippedPolicy=Request->GetIntegerField(TEXT("skippedPolicy"));
        for(const auto& R:Request->GetArrayField(TEXT("queued")))A->NotifyQueue.AnimNotifies.Add(Ref(R->AsObject()));
        A->Named=[&]{for(const auto& R:A->NotifyQueue.AnimNotifies){const auto* T=R.GetContextData<FTag>();if(T&&T->Policy==5){Record(0,0,R);break;}}};
        Child=[&]{A->NotifyQueue.AnimNotifies.Reset();for(const auto& R:Request->GetArrayField(TEXT("child")))A->NotifyQueue.AnimNotifies.Add(Ref(R->AsObject()));A->TriggerAnimNotifies(.02f,UAnimInstance::ENotifyTriggerMode::ForceAllSources);};
        auto Witness=[&]{auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("policy"),4);R->SetNumberField(TEXT("handle"),999);R->SetNumberField(TEXT("sourceKind"),0);R->SetNumberField(TEXT("source"),0);R->SetNumberField(TEXT("instance"),-1);auto V=Ref(R);A->TriggerSingleAnimNotify(V);return V.GetNotifyInstanceID();};
        auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("before"),Snapshot(A));Result->SetNumberField(TEXT("nextInstance"),Witness()+1);
        PRAGMA_DISABLE_DEPRECATION_WARNINGS
        A->bNeedsUpdate=Request->GetBoolField(TEXT("skipGraph"));
        PRAGMA_ENABLE_DEPRECATION_WARNINGS
        Recording=true;const int32 Mode=Request->GetIntegerField(TEXT("mode"));
        if(Mode==4)A->EndNotifyStates();else A->TriggerAnimNotifies(.02f,static_cast<UAnimInstance::ENotifyTriggerMode>(Mode));Recording=false;
        Result->SetArrayField(TEXT("calls"),Calls);Result->SetArrayField(TEXT("after"),Snapshot(A));Result->SetNumberField(TEXT("allocator"),Witness());Results.Add(MakeShared<FJsonValueObject>(Result));
        A->ActiveAnimNotifyState.Reset();A->ActiveAnimNotifyEventReference.Reset();A->Named=nullptr;World->DestroyWorld(false);
    }
    Cvar->Set(OldPolicy,ECVF_SetByCode);auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("cases"),Results);FString Json;
    FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
