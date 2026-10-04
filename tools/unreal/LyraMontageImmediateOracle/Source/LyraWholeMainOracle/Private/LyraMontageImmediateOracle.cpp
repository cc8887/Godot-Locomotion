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

namespace LyraImmediateMontage
{
struct FAccess:UAnimInstance
{
    static void Weight(UAnimInstance* A,float D){(A->*&FAccess::Montage_UpdateWeight)(D);}
    static void Advance(UAnimInstance* A,float D){(A->*&FAccess::Montage_Advance)(D);}
    static TArray<FAnimMontageInstance*>& Instances(UAnimInstance* A){return A->*&FAccess::MontageInstances;}
    static void Dispatch(UAnimInstance* A)
    {
        A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (A->*&FAccess::DispatchQueuedAnimEvents)();
    }
};
bool Phase(UAnimInstance* A)
{auto* P=FindFProperty<FBoolProperty>(A->GetClass(),TEXT("bQueueMontageEvents"));check(P);return P->GetPropertyValue_InContainer(A);}
struct FBank
{
    UAnimInstance* A=nullptr;
    ULyraMontageDelegateListener* OldGlobal=nullptr;
    ULyraMontageDelegateListener* NewGlobal=nullptr;
    TArray<UAnimMontage*> Assets;
    TMap<int32,int32> Ids;
    FString Stage,Mode;
    int32 Serial=0;
    TArray<TSharedPtr<FJsonValue>> Calls;
    int32 Root()
    {auto* I=A->GetRootMotionMontageInstance();return I?Ids.FindChecked(I->GetInstanceID()):0;}
    TArray<TSharedPtr<FJsonValue>> Snapshot()
    {
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(auto* I:FAccess::Instances(A))if(I&&I->Montage)
        {
            auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("instance"),Ids.FindChecked(I->GetInstanceID()));
            R->SetNumberField(TEXT("asset"),Assets.IndexOfByKey(I->Montage));R->SetNumberField(TEXT("position"),I->GetPosition());
            R->SetNumberField(TEXT("weight"),I->GetWeight());R->SetNumberField(TEXT("desired"),I->GetDesiredWeight());
            R->SetBoolField(TEXT("playing"),I->IsPlaying());R->SetBoolField(TEXT("stopped"),I->IsStopped());
            R->SetNumberField(TEXT("blendTime"),I->GetBlendTime());Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        return Rows;
    }
    void Record(int32 Kind,UAnimMontage* M,int32 Id,bool Interrupted,const FString& Listener)
    {
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("kind"),Kind);R->SetNumberField(TEXT("asset"),Assets.IndexOfByKey(M));
        R->SetNumberField(TEXT("instance"),Id);R->SetBoolField(TEXT("interrupted"),Interrupted);
        R->SetStringField(TEXT("listener"),Listener);R->SetStringField(TEXT("stage"),Stage);
        R->SetBoolField(TEXT("queuing"),Phase(A));R->SetNumberField(TEXT("rootOwner"),Root());
        R->SetArrayField(TEXT("live"),Snapshot());Calls.Add(MakeShared<FJsonValueObject>(R));
    }
    void Callback(int32 Kind,UAnimMontage* M,int32 Id,bool Interrupted)
    {
        Record(Kind,M,Id,Interrupted,TEXT("instance"));
        const int32 Trigger=Mode==TEXT("in-stop-earlier")?2:1;
        if(Id!=Trigger)return;
        if(Kind==1)
        {
            if(Mode==TEXT("in-play"))Play(1,false);
            else if(Mode==TEXT("in-play-many"))for(int32 N=0;N<9;++N)Play(1,false);
            else if(Mode==TEXT("in-play-root"))Play(2,false);
            else if(Mode==TEXT("in-stop-self")||Mode==TEXT("in-rebind"))
            {
                auto* I=A->GetActiveInstanceForMontage(M);check(I);
                if(Mode==TEXT("in-rebind"))
                {
                    I->OnMontageBlendingOutStarted.BindLambda([this,Id](UAnimMontage* P,bool B){Record(0,P,Id,B,TEXT("rebound"));});
                    A->OnMontageBlendedIn.RemoveDynamic(OldGlobal,&ULyraMontageDelegateListener::In);
                    A->OnMontageBlendedIn.AddDynamic(NewGlobal,&ULyraMontageDelegateListener::In);
                }
                I->Stop(FAlphaBlend(0.f));
            }
            else if(Mode==TEXT("in-stop-other")||Mode==TEXT("in-stop-earlier"))
            {if(auto* I=A->GetActiveInstanceForMontage(Assets[1]))I->Stop(FAlphaBlend(0.f));}
            else return;
            Record(Kind,M,Id,Interrupted,TEXT("return"));
        }
        if(Kind==0&&Mode==TEXT("request-out-play"))
        {Play(1,true);Record(Kind,M,Id,Interrupted,TEXT("return"));}
        if(Kind==0&&Mode==TEXT("request-out-stop"))
        {if(auto* I=A->GetActiveInstanceForMontage(Assets[1]))I->Stop(FAlphaBlend(.05f));Record(Kind,M,Id,Interrupted,TEXT("return"));}
    }
    void Play(int32 Asset,bool StopGroup)
    {
        auto* M=Assets[Asset];check(A->Montage_Play(M,1,EMontagePlayReturnType::MontageLength,0,StopGroup)>0);
        auto* I=A->GetActiveInstanceForMontage(M);check(I);const int32 Id=++Serial;Ids.Add(I->GetInstanceID(),Id);
        I->OnMontageBlendingOutStarted.BindLambda([this,Id](UAnimMontage* P,bool B){Callback(0,P,Id,B);});
        I->OnMontageBlendedInEnded.BindLambda([this,Id](UAnimMontage* P){Callback(1,P,Id,false);});
        I->OnMontageEnded.BindLambda([this,Id](UAnimMontage* P,bool B){Callback(3,P,Id,B);});
    }
};
}

FString ULyraWholeMainOracleLibrary::ReadMontageImmediateCallbacks(const FString& RequestsJson)
{
    using namespace LyraImmediateMontage;
    TSharedPtr<FJsonObject> Q;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return {};
    auto* Manny=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Manny)return {};
    TArray<UAnimMontage*> Assets;
    for(const auto& V:Q->GetArrayField(TEXT("assets")))
    {
        auto* M=LoadObject<UAnimMontage>(nullptr,*V->AsString());if(!M)return {};Assets.Add(M);
        for(const auto& T:M->SlotAnimTracks)for(const auto& S:T.AnimTrack.AnimSegments)
            if(auto* Seq=Cast<UAnimSequence>(S.GetAnimReference().Get()))Seq->WaitOnExistingCompression(true);
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& V:Q->GetArrayField(TEXT("traces")))
    {
        const auto T=V->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return {};
        auto* C=World->SpawnActor<ACharacter>(FVector(0,0,90),FRotator::ZeroRotator);if(!C)return {};
        auto* Mesh=C->GetMesh();Mesh->SetSkeletalMeshAsset(Manny);Mesh->SetAnimInstanceClass(UAnimInstance::StaticClass());
        auto* A=Mesh->GetAnimInstance();if(!A)return {};
        FBank B;B.A=A;B.Assets=Assets;B.Mode=T->GetStringField(TEXT("mode"));
        auto* L=NewObject<ULyraMontageDelegateListener>(C);auto* Next=NewObject<ULyraMontageDelegateListener>(C);
        B.OldGlobal=L;B.NewGlobal=Next;
        L->Record=[&B](int32 K,UAnimMontage* M,bool I,FName,bool){B.Record(K,M,0,I,TEXT("global"));};
        Next->Record=[&B](int32 K,UAnimMontage* M,bool I,FName,bool){B.Record(K,M,0,I,TEXT("global-rebound"));};
        A->OnMontageBlendingOut.AddDynamic(L,&ULyraMontageDelegateListener::Out);
        A->OnMontageBlendedIn.AddDynamic(L,&ULyraMontageDelegateListener::In);
        A->OnMontageEnded.AddDynamic(L,&ULyraMontageDelegateListener::End);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            const auto F=FV->AsObject();auto R=MakeShared<FJsonObject>();B.Calls.Reset();B.Stage=TEXT("requests");
            for(const auto& P:F->GetArrayField(TEXT("plays")))B.Play(P->AsNumber(),false);
            if(F->GetBoolField(TEXT("stop")))A->Montage_Stop(.2f,Assets[0]);
            const float D=F->GetNumberField(TEXT("delta"));B.Stage=TEXT("weight");FAccess::Weight(A,D);
            B.Stage=TEXT("advance");FAccess::Advance(A,D);A->ConsumeExtractedRootMotion(1);
            R->SetArrayField(TEXT("before"),B.Snapshot());R->SetNumberField(TEXT("beforeRoot"),B.Root());
            R->SetArrayField(TEXT("immediate"),B.Calls);B.Calls.Reset();B.Stage=TEXT("dispatch");FAccess::Dispatch(A);
            R->SetArrayField(TEXT("calls"),B.Calls);R->SetArrayField(TEXT("after"),B.Snapshot());
            R->SetNumberField(TEXT("afterRoot"),B.Root());R->SetBoolField(TEXT("queuing"),Phase(A));Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("name"),T->GetStringField(TEXT("name")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
        L->Record=nullptr;Next->Record=nullptr;A->OnMontageBlendingOut.Clear();A->OnMontageBlendedIn.Clear();A->OnMontageEnded.Clear();World->DestroyWorld(false);
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Output;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Output));return Output;
}
