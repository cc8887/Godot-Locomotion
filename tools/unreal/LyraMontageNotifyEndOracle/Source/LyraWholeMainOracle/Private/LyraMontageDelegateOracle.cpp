#include "LyraWholeMainOracleLibrary.h"
#include "LyraMontageDelegateListener.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"
#include "Serialization/JsonSerializer.h"

void ULyraMontageDelegateListener::Out(UAnimMontage* M,bool I){if(Record)Record(0,M,I,NAME_None,false);}
void ULyraMontageDelegateListener::In(UAnimMontage* M){if(Record)Record(1,M,false,NAME_None,false);}
void ULyraMontageDelegateListener::Section(UAnimMontage* M,FName S,bool L){if(Record)Record(2,M,false,S,L);}
void ULyraMontageDelegateListener::End(UAnimMontage* M,bool I){if(Record)Record(3,M,I,NAME_None,false);}

namespace LyraMontageEvents
{
struct FAccess:UAnimInstance
{
    static void Weight(UAnimInstance* A,float D){(A->*&FAccess::Montage_UpdateWeight)(D);}
    static void Advance(UAnimInstance* A,float D){(A->*&FAccess::Montage_Advance)(D);}
    static void Dispatch(UAnimInstance* A)
    {
        // Exercise native Montage delegates separately from resource Notify side
        // effects. No authored graph, asset, delegate container or flag is patched.
        A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (A->*&FAccess::DispatchQueuedAnimEvents)();
    }
};
bool Phase(UAnimInstance* A)
{
    auto* P=FindFProperty<FBoolProperty>(A->GetClass(),TEXT("bQueueMontageEvents"));check(P);
    return P->GetPropertyValue_InContainer(A);
}
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_MONTAGE_DELEGATES_FAILED line=%d"),Line);return {};}
struct FRecorder
{
    UAnimInstance* A=nullptr;
    FString Stage;
    TArray<UAnimMontage*> Assets;
    TArray<TSharedPtr<FJsonValue>> Calls;
    void Record(int32 Kind,UAnimMontage* M,int32 Instance,bool Interrupted=false,FName Section=NAME_None,bool Looped=false,const FString& Listener=TEXT("instance"))
    {
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("kind"),Kind);R->SetNumberField(TEXT("asset"),Assets.IndexOfByKey(M));
        R->SetNumberField(TEXT("instance"),Instance);R->SetBoolField(TEXT("interrupted"),Interrupted);
        R->SetStringField(TEXT("section"),Section==NAME_None?TEXT(""):Section.ToString());R->SetBoolField(TEXT("looped"),Looped);
        R->SetStringField(TEXT("listener"),Listener);R->SetStringField(TEXT("stage"),Stage);R->SetBoolField(TEXT("queuing"),Phase(A));
        Calls.Add(MakeShared<FJsonValueObject>(R));
    }
    void Bind(UAnimMontage* M,int32 Instance)
    {
        auto* I=A->GetActiveInstanceForMontage(M);check(I);
        I->OnMontageBlendingOutStarted.BindLambda([this,Instance](UAnimMontage* P,bool Interrupted){Record(0,P,Instance,Interrupted);});
        I->OnMontageBlendedInEnded.BindLambda([this,Instance](UAnimMontage* P){Record(1,P,Instance);});
        I->OnMontageEnded.BindLambda([this,Instance](UAnimMontage* P,bool Interrupted){Record(3,P,Instance,Interrupted);});
        I->OnMontageSectionChanged.BindLambda([this,Instance](UAnimMontage* P,FName S,bool L){Record(2,P,Instance,false,S,L);});
    }
};
void BindGlobals(UAnimInstance* A,ULyraMontageDelegateListener* L)
{
    A->OnMontageBlendingOut.AddDynamic(L,&ULyraMontageDelegateListener::Out);
    A->OnMontageBlendedIn.AddDynamic(L,&ULyraMontageDelegateListener::In);
    A->OnMontageSectionChanged.AddDynamic(L,&ULyraMontageDelegateListener::Section);
    A->OnMontageEnded.AddDynamic(L,&ULyraMontageDelegateListener::End);
}
void Queue(UAnimInstance* A,FRecorder& R,UAnimMontage* M,int32 Kind,int32 Instance)
{
    if(Kind==0){FOnMontageBlendingOutStarted D;D.BindLambda([&R,Instance](UAnimMontage* P,bool I){R.Record(0,P,Instance,I);});A->QueueMontageBlendingOutEvent({M,true,D});}
    if(Kind==1){FOnMontageBlendedInEnded D;D.BindLambda([&R,Instance](UAnimMontage* P){R.Record(1,P,Instance);});A->QueueMontageBlendedInEvent({M,D});}
    if(Kind==2){FOnMontageSectionChanged D;D.BindLambda([&R,Instance](UAnimMontage* P,FName S,bool L){R.Record(2,P,Instance,false,S,L);});A->QueueMontageSectionChangedEvent({M,100+Instance,TEXT("Other"),true,D});}
    if(Kind==3){FOnMontageEnded D;D.BindLambda([&R,Instance](UAnimMontage* P,bool I){R.Record(3,P,Instance,I);});A->QueueMontageEndedEvent({M,100+Instance,false,D});}
}
}

FString ULyraWholeMainOracleLibrary::ReadMontageEvents(const FString& RequestsJson)
{
    using namespace LyraMontageEvents;
    TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Manny)return Fail(__LINE__);
    TArray<UAnimMontage*> Assets;
    for(const auto& V:Q->GetArrayField(TEXT("assets")))
    {
        auto* M=LoadObject<UAnimMontage>(nullptr,*V->AsString());if(!M)return Fail(__LINE__);Assets.Add(M);
        for(const auto& T:M->SlotAnimTracks)for(const auto& S:T.AnimTrack.AnimSegments)
            if(auto* Sequence=Cast<UAnimSequence>(S.GetAnimReference().Get()))Sequence->WaitOnExistingCompression(true);
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& V:Q->GetArrayField(TEXT("traces")))
    {
        auto T=V->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        auto* C=World->SpawnActor<ACharacter>(FVector(0,0,90),FRotator::ZeroRotator);if(!C)return Fail(__LINE__);
        auto* Mesh=C->GetMesh();Mesh->SetSkeletalMeshAsset(Manny);Mesh->SetRelativeLocation(FVector(0,0,-90));Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());
        auto* A=Mesh->GetAnimInstance();if(!A)return Fail(__LINE__);A->SetRootMotionMode(ERootMotionMode::RootMotionFromMontagesOnly);
        FRecorder R;R.A=A;R.Assets=Assets;auto* L=NewObject<ULyraMontageDelegateListener>(C);
        L->Record=[&R](int32 K,UAnimMontage* M,bool I,FName S,bool Loop){R.Record(K,M,0,I,S,Loop,TEXT("global"));};BindGlobals(A,L);
        const int32 Kernel=T->GetIntegerField(TEXT("kernel"));TArray<TSharedPtr<FJsonValue>> Frames;
        if(Kernel>=0)
        {
            auto Row=MakeShared<FJsonObject>();R.Stage=TEXT("queue");FAccess::Advance(A,0);
            if(Kernel==0)
            {
                FOnMontageEnded End;End.BindLambda([&](UAnimMontage* M,bool I){
                    R.Record(3,M,1,I);A->OnMontageEnded.RemoveDynamic(L,&ULyraMontageDelegateListener::End);
                    auto* Next=NewObject<ULyraMontageDelegateListener>(C);Next->Record=[&R](int32 K,UAnimMontage* P,bool Interrupted,FName S,bool Loop){R.Record(K,P,0,Interrupted,S,Loop,TEXT("new-global"));};
                    A->OnMontageEnded.AddDynamic(Next,&ULyraMontageDelegateListener::End);
                });
                A->QueueMontageEndedEvent({Assets[0],101,false,End});Queue(A,R,Assets[0],2,1);Queue(A,R,Assets[0],1,1);Queue(A,R,Assets[0],0,1);
            }
            else
            {
                FOnMontageBlendingOutStarted Out;Out.BindLambda([&](UAnimMontage* M,bool I){
                    R.Record(0,M,1,I);
                    if(Kernel==1){Queue(A,R,M,3,2);R.Record(0,M,1,I,NAME_None,false,TEXT("return"));}
                    else{FAccess::Advance(A,0);Queue(A,R,M,0,2);Queue(A,R,M,3,2);}
                });
                A->QueueMontageBlendingOutEvent({Assets[0],true,Out});Queue(A,R,Assets[0],3,1);
            }
            Row->SetNumberField(TEXT("callsBefore"),R.Calls.Num());R.Stage=TEXT("dispatch");FAccess::Dispatch(A);
            Row->SetBoolField(TEXT("phaseAfterFirst"),Phase(A));Row->SetArrayField(TEXT("first"),R.Calls);R.Calls.Reset();
            R.Stage=TEXT("dispatch2");FAccess::Dispatch(A);Row->SetBoolField(TEXT("phaseAfterSecond"),Phase(A));Row->SetArrayField(TEXT("second"),R.Calls);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        else
        {
            int32 Serial=0;
            for(const auto& FV:T->GetArrayField(TEXT("frames")))
            {
                const auto F=FV->AsObject();auto Row=MakeShared<FJsonObject>();R.Calls.Reset();R.Stage=TEXT("requests");
                Row->SetBoolField(TEXT("before"),Phase(A));
                for(const auto& CV:F->GetArrayField(TEXT("commands")))
                {
                    auto Command=CV->AsObject();const int32 Asset=Command->GetIntegerField(TEXT("asset"));if(!Assets.IsValidIndex(Asset))return Fail(__LINE__);
                    auto* M=Assets[Asset];
                    if(Command->GetBoolField(TEXT("stop")))A->Montage_Stop(Command->GetNumberField(TEXT("blend")),M);
                    else{if(A->Montage_Play(M,Command->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,Command->GetNumberField(TEXT("start")),Command->GetBoolField(TEXT("stopGroup")))<=0)return Fail(__LINE__);R.Bind(M,++Serial);}
                }
                const float D=F->GetNumberField(TEXT("delta"));R.Stage=TEXT("weight");FAccess::Weight(A,D);Row->SetBoolField(TEXT("afterWeight"),Phase(A));
                R.Stage=TEXT("advance");FAccess::Advance(A,D);A->ConsumeExtractedRootMotion(1);Row->SetBoolField(TEXT("afterAdvance"),Phase(A));
                Row->SetArrayField(TEXT("beforeDispatch"),R.Calls);R.Calls.Reset();
                if(F->GetBoolField(TEXT("dispatch"))){R.Stage=TEXT("dispatch");FAccess::Dispatch(A);}
                Row->SetBoolField(TEXT("afterDispatch"),Phase(A));Row->SetArrayField(TEXT("dispatched"),R.Calls);Frames.Add(MakeShared<FJsonValueObject>(Row));
            }
        }
        auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),T->GetStringField(TEXT("name")));Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
        L->Record=nullptr;R.Stage=TEXT("cleanup");A->OnMontageBlendingOut.Clear();A->OnMontageBlendedIn.Clear();A->OnMontageSectionChanged.Clear();A->OnMontageEnded.Clear();
        World->DestroyWorld(false);
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Output;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Output));return Output;
}
