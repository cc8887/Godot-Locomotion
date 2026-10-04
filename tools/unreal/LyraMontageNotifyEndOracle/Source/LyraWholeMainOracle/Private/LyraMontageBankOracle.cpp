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

namespace LyraMontageBank
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
    TArray<UAnimMontage*> Assets;
    TMap<int32,int32> Ids;
    FString Stage,Mode;
    int32 Serial=0;
    TArray<TSharedPtr<FJsonValue>> Calls;
    TArray<TSharedPtr<FJsonValue>> Snapshot()
    {
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(auto* I:FAccess::Instances(A))if(I&&I->Montage)
        {
            auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("instance"),Ids.FindChecked(I->GetInstanceID()));
            R->SetNumberField(TEXT("asset"),Assets.IndexOfByKey(I->Montage));R->SetNumberField(TEXT("position"),I->GetPosition());
            R->SetNumberField(TEXT("weight"),I->GetWeight());R->SetNumberField(TEXT("desired"),I->GetDesiredWeight());
            R->SetBoolField(TEXT("playing"),I->IsPlaying());R->SetBoolField(TEXT("stopped"),I->IsStopped());
            R->SetNumberField(TEXT("blendTime"),I->GetBlendTime());
            Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        return Rows;
    }
    void Record(int32 Kind,UAnimMontage* M,int32 Id,bool Interrupted,const FString& Listener)
    {
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("kind"),Kind);R->SetNumberField(TEXT("asset"),Assets.IndexOfByKey(M));
        R->SetNumberField(TEXT("instance"),Id);R->SetBoolField(TEXT("interrupted"),Interrupted);
        R->SetStringField(TEXT("listener"),Listener);R->SetStringField(TEXT("stage"),Stage);
        R->SetBoolField(TEXT("queuing"),Phase(A));R->SetArrayField(TEXT("live"),Snapshot());Calls.Add(MakeShared<FJsonValueObject>(R));
    }
    void Callback(int32 Kind,UAnimMontage* M,int32 Id,bool Interrupted)
    {
        Record(Kind,M,Id,Interrupted,TEXT("instance"));
        if(Id!=1)return;
        if(Kind==3&&Mode==TEXT("ended-play")){Play(1,true);Record(Kind,M,Id,Interrupted,TEXT("return"));}
        if(Kind==0&&Mode==TEXT("out-stop"))
        {
            if(auto* Other=A->GetActiveInstanceForMontage(Assets[1]))Other->Stop(FAlphaBlend(.05f));
            Record(Kind,M,Id,Interrupted,TEXT("return"));
        }
        if(Kind==0&&Mode==TEXT("out-play")){Play(1,true);Record(Kind,M,Id,Interrupted,TEXT("return"));}
    }
    void Play(int32 Asset,bool StopGroup)
    {
        auto* M=Assets[Asset];check(A->Montage_Play(M,1,EMontagePlayReturnType::MontageLength,0,StopGroup)>0);
        auto* I=A->GetActiveInstanceForMontage(M);check(I);const int32 Id=++Serial;Ids.Add(I->GetInstanceID(),Id);
        I->OnMontageBlendingOutStarted.BindLambda([this,Id](UAnimMontage* P,bool Interrupted){Callback(0,P,Id,Interrupted);});
        I->OnMontageBlendedInEnded.BindLambda([this,Id](UAnimMontage* P){Callback(1,P,Id,false);});
        I->OnMontageEnded.BindLambda([this,Id](UAnimMontage* P,bool Interrupted){Callback(3,P,Id,Interrupted);});
    }
};
}

FString ULyraWholeMainOracleLibrary::ReadMontageBankCallbacks(const FString& RequestsJson)
{
    using namespace LyraMontageBank;
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
        auto* L=NewObject<ULyraMontageDelegateListener>(C);
        L->Record=[&B](int32 K,UAnimMontage* M,bool I,FName,bool){B.Record(K,M,0,I,TEXT("global"));};
        A->OnMontageBlendingOut.AddDynamic(L,&ULyraMontageDelegateListener::Out);
        A->OnMontageBlendedIn.AddDynamic(L,&ULyraMontageDelegateListener::In);
        A->OnMontageEnded.AddDynamic(L,&ULyraMontageDelegateListener::End);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            const auto F=FV->AsObject();auto R=MakeShared<FJsonObject>();B.Calls.Reset();B.Stage=TEXT("requests");
            for(const auto& P:F->GetArrayField(TEXT("plays")))B.Play(P->AsNumber(),false);
            const float D=F->GetNumberField(TEXT("delta"));B.Stage=TEXT("weight");FAccess::Weight(A,D);
            B.Stage=TEXT("advance");FAccess::Advance(A,D);A->ConsumeExtractedRootMotion(1);
            B.Stage=TEXT("pre-dispatch");
            if(F->GetBoolField(TEXT("stop")))
            {
                auto* I=A->GetActiveInstanceForMontage(Assets[0]);check(I);A->Montage_Stop(.2f,Assets[0]);
                if(B.Mode==TEXT("captured-rebind"))I->OnMontageBlendingOutStarted.BindLambda([&B](UAnimMontage* M,bool Interrupted){B.Record(0,M,1,Interrupted,TEXT("rebound"));});
            }
            R->SetArrayField(TEXT("before"),B.Snapshot());R->SetArrayField(TEXT("immediate"),B.Calls);B.Calls.Reset();
            B.Stage=TEXT("dispatch");FAccess::Dispatch(A);
            R->SetArrayField(TEXT("calls"),B.Calls);R->SetArrayField(TEXT("after"),B.Snapshot());R->SetBoolField(TEXT("queuing"),Phase(A));
            Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("name"),T->GetStringField(TEXT("name")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
        L->Record=nullptr;A->OnMontageBlendingOut.Clear();A->OnMontageBlendedIn.Clear();A->OnMontageEnded.Clear();World->DestroyWorld(false);
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Output;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Output));return Output;
}
