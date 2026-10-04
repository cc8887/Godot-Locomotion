#include "AlsLyraSlotPoseLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "AnimNodes/AnimNode_Slot.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraSlotPoseProbe
{
FString Fail(int32 Line)
{ UE_LOG(LogTemp,Error,TEXT("LYRA_SLOT_POSE_CAPTURE_FAILED line=%d"),Line);return {}; }
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* A) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A); }
    static void Tick(UAnimInstance* A,float D)
    {
        A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (A->*&FInstanceAccess::Montage_UpdateWeight)(D);(A->*&FInstanceAccess::Montage_Advance)(D);
        (A->*&FInstanceAccess::UpdateMontageEvaluationData)();
    }
};
struct FProxyAccess : FAnimInstanceProxy
{
    static const TArray<FMontageEvaluationState>& Frozen(const FAnimInstanceProxy& P)
    { const auto Read=static_cast<const TArray<FMontageEvaluationState>&(FAnimInstanceProxy::*)()const>(&FProxyAccess::GetMontageEvaluationData);return (P.*Read)(); }
    static void Bones(FAnimInstanceProxy& P,USkeleton* S)
    {
        TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
        (P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    { (P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D); }
};
// Controlled real track input, without advancing any additional animation clock.
struct FTrackInput : FAnimNode_Base
{
    UAnimMontage* Montage=nullptr;float Position=.27f;FDeltaTimeRecord Delta;
    int32 Updates=0,Evaluations=0;
    void Update_AnyThread(const FAnimationUpdateContext&) override { ++Updates; }
    void Evaluate_AnyThread(FPoseContext& C) override
    {
        ++Evaluations;FAnimationPoseData Data(C);
        Montage->SlotAnimTracks[0].AnimTrack.GetAnimationPose(Data,FAnimExtractContext(static_cast<double>(Position),Montage->HasRootMotion(),Delta));
        FBlendedCurve Curves;Curves.InitFrom(C.Pose.GetBoneContainer());Montage->EvaluateCurveData(Curves,Position);C.Curve.Combine(Curves);
    }
};
}

FString UAlsLyraSlotPoseLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimMontage*>& Originals,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraSlotPoseProbe;
    TSharedPtr<FJsonObject> Q;const auto* Class=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!Class||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    const auto& Mapping=Q->GetObjectField(TEXT("sequenceIndices"));
    for(auto* S:Sequences){if(!S||S->GetSkeleton()!=Skeleton)return Fail(__LINE__);S->WaitOnExistingCompression(true);}
    TArray<TStrongObjectPtr<UBlendProfile>> Profiles;TMap<const UBlendProfile*,UBlendProfile*> ProfileMap;
    TArray<TStrongObjectPtr<UAnimMontage>> Storage;TArray<UAnimMontage*> Montages;
    for(auto* Original:Originals)
    {
        if(!Original)return Fail(__LINE__);
        Storage.Emplace(DuplicateObject<UAnimMontage>(Original,GetTransientPackage()));auto* M=Storage.Last().Get();
        M->ClearFlags(RF_Public|RF_Standalone);M->SetFlags(RF_Transient);M->SetSkeleton(Skeleton);
        for(auto& Slot:M->SlotAnimTracks)
        {
            Skeleton->SetSlotGroupName(Slot.SlotName,Original->GetGroupName());
            if(Slot.AnimTrack.AnimSegments.Num()!=1)return Fail(__LINE__);
            auto& Segment=Slot.AnimTrack.AnimSegments[0];const UAnimSequenceBase* A=Segment.GetAnimReference();
            if(!A||!Mapping->HasField(A->GetPathName()))return Fail(__LINE__);
            const int32 Index=Mapping->GetNumberField(A->GetPathName());if(!Sequences.IsValidIndex(Index))return Fail(__LINE__);
            Segment.SetAnimReference(Sequences[Index]);
        }
        auto Adapt=[&](const UBlendProfile* OriginalProfile)->UBlendProfile*
        {
            if(!OriginalProfile)return nullptr;if(auto** Found=ProfileMap.Find(OriginalProfile))return *Found;
            Profiles.Emplace(NewObject<UBlendProfile>(GetTransientPackage()));auto* P=Profiles.Last().Get();P->OwningSkeleton=Skeleton;P->Mode=OriginalProfile->Mode;
            for(int32 B=0;B<81;++B)
            {
                const auto Name=Skeleton->GetReferenceSkeleton().GetBoneName(B);const int32 Index=OriginalProfile->GetEntryIndex(Name);
                if(Index!=INDEX_NONE)P->SetBoneBlendScale(B,OriginalProfile->GetEntryBlendScale(Index),false,true);
            }
            ProfileMap.Add(OriginalProfile,P);return P;
        };
        M->BlendProfileIn=Adapt(Original->BlendProfileIn);M->BlendProfileOut=Adapt(Original->BlendProfileOut);Montages.Add(M);
    }
    const int32 Nodes[]={81,71,2,74,84};const FName Names[]={TEXT("UpperBody"),TEXT("UpperBodyAdditive"),TEXT("FullBodyAdditivePreAim"),TEXT("AdditiveHitReact"),TEXT("FullBody")};
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        const auto T=TV->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=World->SpawnActor<ACharacter>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& Proxy=FInstanceAccess::Proxy(Main);Carrier->SetSkeleton(Skeleton);FProxyAccess::Bones(Proxy,Skeleton);
        if(Main->RootMotionMode!=ERootMotionMode::RootMotionFromMontagesOnly)return Fail(__LINE__);
        FTrackInput Inputs[5];FAnimNode_Slot* Slots[5];const auto& Properties=Class->GetAnimNodeProperties();
        for(int32 S=0;S<5;++S)
        {
            auto* P=Properties[Properties.Num()-1-Nodes[S]];if(P->Struct!=FAnimNode_Slot::StaticStruct())return Fail(__LINE__);
            Slots[S]=P->ContainerPtrToValuePtr<FAnimNode_Slot>(Main);if(Slots[S]->SlotName!=Names[S]||Slots[S]->bAlwaysUpdateSourcePose)return Fail(__LINE__);
            Slots[S]->Source.SetLinkNode(&Inputs[S]);Slots[S]->Initialize_AnyThread(FAnimationInitializeContext(&Proxy));Slots[S]->CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
        }
        TArray<TSharedPtr<FJsonValue>> Frames;int32 FI=0;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());const auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));
            FProxyAccess::Pre(Proxy,Main,D);FInstanceAccess::Tick(Main,D);
            TArray<TSharedPtr<FJsonValue>> Frozen;
            for(const auto& E:FProxyAccess::Frozen(Proxy))
            {
                auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("asset"),Montages.IndexOfByKey(E.Montage.Get()));R->SetNumberField(TEXT("position"),E.MontagePosition);
                R->SetNumberField(TEXT("weight"),E.BlendInfo.GetBlendedValue());R->SetNumberField(TEXT("previous"),E.DeltaTimeRecord.GetPrevious());R->SetNumberField(TEXT("delta"),E.DeltaTimeRecord.Delta);
                R->SetBoolField(TEXT("profile"),E.ActiveBlendProfile!=nullptr);Frozen.Add(MakeShared<FJsonValueObject>(R));
            }
            TArray<TSharedPtr<FJsonValue>> Rows;const bool Sample=F->GetBoolField(TEXT("sample"));const int32 Base=F->GetNumberField(TEXT("basis"));
            if(!Montages.IsValidIndex(Base)||Montages[Base]->SlotAnimTracks[0].AnimTrack.IsAdditive())return Fail(__LINE__);
            for(int32 S=0;S<5;++S)
            {
                Inputs[S].Montage=Montages[Base];Inputs[S].Delta.Set(.233f,.037f);const int32 Prior=Inputs[S].Evaluations;
                Slots[S]->Update_AnyThread(FAnimationUpdateContext(&Proxy,D));float SlotWeight=0,SourceWeight=0,TotalWeight=0;Proxy.GetSlotWeight(Names[S],SlotWeight,SourceWeight,TotalWeight);
                auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("slot"),S);R->SetNumberField(TEXT("slotWeight"),SlotWeight);R->SetNumberField(TEXT("sourceWeight"),SourceWeight);R->SetNumberField(TEXT("totalWeight"),TotalWeight);
                if(Sample)
                {
                    FPoseContext Output(&Proxy);Slots[S]->Evaluate_AnyThread(Output);
                    R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Output.Pose,Output.Curve,Output.CustomAttributes,Skeleton->GetReferenceSkeleton()));
                    R->SetNumberField(TEXT("sourceEvaluations"),Inputs[S].Evaluations-Prior);
                }
                Rows.Add(MakeShared<FJsonValueObject>(R));
            }
            auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("frozen"),Frozen);R->SetArrayField(TEXT("slots"),Rows);Frames.Add(MakeShared<FJsonValueObject>(R));
            for(const auto& CV:F->GetArrayField(TEXT("commands")))
            {
                const auto Command=CV->AsObject();const int32 Asset=Command->GetNumberField(TEXT("asset"));if(!Montages.IsValidIndex(Asset))return Fail(__LINE__);
                bool InstanceStop=false;Command->TryGetBoolField(TEXT("instanceStop"),InstanceStop);
                if(InstanceStop)
                {for(int32 I=Main->MontageInstances.Num()-1;I>=0;--I){auto* A=Main->MontageInstances[I];if(A&&A->IsValid()&&A->Montage==Montages[Asset]){A->Stop(FAlphaBlend(Montages[Asset]->BlendOut,Command->GetNumberField(TEXT("blend"))));break;}}}
                else if(Command->GetBoolField(TEXT("stop")))Main->Montage_Stop(Command->GetNumberField(TEXT("blend")),Montages[Asset]);
                else if(Main->Montage_Play(Montages[Asset],Command->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,Command->GetNumberField(TEXT("start")),Command->GetBoolField(TEXT("stopGroup")))<=0)return Fail(__LINE__);
            }
            ++FI;
        }
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);R->SetNumberField(TEXT("rootMotionMode"),(int32)ERootMotionMode::RootMotionFromMontagesOnly);
    FString Result;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result));return Result;
}
