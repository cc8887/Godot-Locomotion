#include "AlsLyraMainSlotsLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "AnimNodes/AnimNode_Slot.h"
#include "AnimNodes/AnimNode_ApplyAdditive.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_UseCachedPose.h"
#include "UObject/UnrealType.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraMainSlotsProbe
{
FString Fail(int32 Line)
{ UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_SLOTS_CAPTURE_FAILED line=%d"),Line);return {}; }
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
// Actual Main operators/caches and Slots; controlled linked-layer leaves.
template<class T>T* Node(const IAnimClassInterface* I,UAnimInstance* A,int32 N)
{auto* P=I->GetAnimNodeProperties()[I->GetAnimNodeProperties().Num()-1-N];return P->Struct==T::StaticStruct()?P->ContainerPtrToValuePtr<T>(A):nullptr;}
struct FSlotAccess:FAnimNode_Slot
{static FSlotNodeWeightInfo Weights(const FAnimNode_Slot& N){return N.*&FSlotAccess::WeightData;}};
struct FTap:FAnimNode_Base
{
    FString Kind;int32 Id=0;FPoseLink Child;TArray<TSharedPtr<FJsonValue>>* Events=nullptr;int32* LastCache=nullptr;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{Child.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{Child.CacheBones(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {
        if(Kind==TEXT("cache"))*LastCache=Id;
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("kind"),Kind);R->SetNumberField(TEXT("id"),Id);
        R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());R->SetNumberField(TEXT("root"),C.GetRootMotionWeightModifier());
        R->SetBoolField(TEXT("active"),C.IsActive());R->SetBoolField(TEXT("shared"),C.GetSharedContext()!=nullptr);Events->Add(MakeShared<FJsonValueObject>(R));
        if(Child.GetLinkNode())Child.Update(C);
    }
};
struct FAiming:FTap
{
    FPoseLink First,Second;float Blend=0;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{First.Initialize(C);Second.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{First.CacheBones(C);Second.CacheBones(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {
        FTap::Update_AnyThread(C);bool A=Blend<1-ZERO_ANIMWEIGHT_THRESH,B=Blend>ZERO_ANIMWEIGHT_THRESH;
        if(A)First.Update(A&&B?C.FractionalWeight(1-Blend):C);
        if(B)Second.Update(A&&B?C.FractionalWeight(Blend):C);
    }
};
}

FString UAlsLyraMainSlotsLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimMontage*>& Originals,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraMainSlotsProbe;
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
        auto* PC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));const auto* ProviderInterface=PC?IAnimClassInterface::GetFromClass(PC):nullptr;
        if(!ProviderInterface)return Fail(__LINE__);TStrongObjectPtr<UAnimInstance> Provider(NewObject<UAnimInstance>(C.Get(),PC,NAME_None,RF_Transient));
        auto* SplitCache=Node<FAnimNode_SaveCachedPose>(Class,Main,78);auto* LocCache=Node<FAnimNode_SaveCachedPose>(Class,Main,83);
        auto* InputCache=Node<FAnimNode_SaveCachedPose>(ProviderInterface,Provider.Get(),78);
        auto* P0=Node<FAnimNode_UseCachedPose>(ProviderInterface,Provider.Get(),76);auto* P1=Node<FAnimNode_UseCachedPose>(ProviderInterface,Provider.Get(),75);
        auto* InputRead=Node<FAnimNode_UseCachedPose>(Class,Main,77);auto* UpperRead=Node<FAnimNode_UseCachedPose>(Class,Main,82);auto* BaseRead=Node<FAnimNode_UseCachedPose>(Class,Main,80);
        auto* Split=Node<FAnimNode_LayeredBoneBlend>(Class,Main,0);auto* Dynamic=Node<FAnimNode_ApplyAdditive>(Class,Main,3);auto* Recovery=Node<FAnimNode_ApplyAdditive>(Class,Main,76);
        auto* DynamicWeight=FindFProperty<FDoubleProperty>(MainClass,TEXT("UpperbodyDynamicAdditiveWeight"));
        if(!SplitCache||!LocCache||!InputCache||!P0||!P1||!InputRead||!UpperRead||!BaseRead||!Split||!Dynamic||!Recovery||!DynamicWeight)return Fail(__LINE__);
        P0->LinkToCachingNode.SetLinkNode(InputCache);P1->LinkToCachingNode.SetLinkNode(InputCache);InputRead->LinkToCachingNode.SetLinkNode(SplitCache);
        UpperRead->LinkToCachingNode.SetLinkNode(LocCache);BaseRead->LinkToCachingNode.SetLinkNode(LocCache);
        TStrongObjectPtr<UBlendProfile> Mask(NewObject<UBlendProfile>(GetTransientPackage()));Mask->OwningSkeleton=Skeleton;Mask->Mode=EBlendProfileMode::BlendMask;
        for(int32 B=0;B<81;++B){float V=0;for(const auto& E:Split->BlendMasks[0]->ProfileEntries)if(E.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(B)){V=E.BlendScale;break;}Mask->SetBoneBlendScale(B,V,false,true);}
        Split->BlendMasks[0]=Mask.Get();Split->InvalidatePerBoneBlendWeights();
        TArray<TSharedPtr<FJsonValue>> Events,Skipped;int32 LastCache=-1;FTap Outer[5],Source[5],Cache[3],Loc,Ref,Additives;FAiming Aiming;
        auto Setup=[&](FTap& N,const TCHAR* Kind,int32 Id){N.Kind=Kind;N.Id=Id;N.Events=&Events;N.LastCache=&LastCache;};
        FAnimNode_Slot* Slots[5];
        for(int32 S=0;S<5;++S)
        {
            Slots[S]=Node<FAnimNode_Slot>(Class,Main,Nodes[S]);if(!Slots[S]||Slots[S]->SlotName!=Names[S]||Slots[S]->bAlwaysUpdateSourcePose)return Fail(__LINE__);
            Setup(Outer[S],TEXT("slot"),S);Setup(Source[S],TEXT("source"),S);Outer[S].Child.SetLinkNode(Slots[S]);Slots[S]->Source.SetLinkNode(&Source[S]);
        }
        Setup(Cache[0],TEXT("cache"),181);Setup(Cache[1],TEXT("cache"),78);Setup(Cache[2],TEXT("cache"),83);
        Setup(Loc,TEXT("leaf"),83);Setup(Ref,TEXT("leaf"),79);Setup(Additives,TEXT("leaf"),5);Setup(Aiming,TEXT("aiming"),6);
        Cache[0].Child.SetLinkNode(InputRead);Cache[1].Child.SetLinkNode(&Outer[2]);Cache[2].Child.SetLinkNode(&Loc);
        InputCache->Pose.SetLinkNode(&Cache[0]);SplitCache->Pose.SetLinkNode(&Cache[1]);LocCache->Pose.SetLinkNode(&Cache[2]);
        Source[4].Child.SetLinkNode(Recovery);Recovery->Base.SetLinkNode(&Outer[3]);Recovery->Additive.SetLinkNode(&Additives);
        Source[3].Child.SetLinkNode(&Aiming);Aiming.First.SetLinkNode(P0);Aiming.Second.SetLinkNode(P1);
        Source[2].Child.SetLinkNode(Split);Split->BlendPoses[0].SetLinkNode(&Outer[0]);Split->BasePose.SetLinkNode(Dynamic);
        Source[0].Child.SetLinkNode(UpperRead);Dynamic->Base.SetLinkNode(BaseRead);Dynamic->Additive.SetLinkNode(&Outer[1]);Source[1].Child.SetLinkNode(&Ref);
        FPoseLink Root;Root.SetLinkNode(&Outer[4]);
        Root.Initialize(FAnimationInitializeContext(&Proxy));Root.CacheBones(FAnimationCacheBonesContext(&Proxy));
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
            Events.Reset();Skipped.Reset();LastCache=-1;Aiming.Blend=F->GetNumberField(TEXT("aimBlend"));
            DynamicWeight->SetPropertyValue_InContainer(Main,F->GetNumberField(TEXT("dynamicWeight")));
            if(F->GetBoolField(TEXT("initialize")))Root.Initialize(FAnimationInitializeContext(&Proxy));
            if(F->GetBoolField(TEXT("visited")))
            {
                FAnimationUpdateSharedContext Shared;FAnimationUpdateContext U(&Proxy,D,&Shared);
                U=U.FractionalWeightAndRootMotion(static_cast<float>(F->GetNumberField(TEXT("weight"))),static_cast<float>(F->GetNumberField(TEXT("rootModifier"))));
                if(!F->GetBoolField(TEXT("active")))U=U.AsInactive();
                UE::Anim::TOptionalScopedGraphMessage<UE::Anim::FCachedPoseSkippedUpdateHandler> Handler(true,U,
                    [&Skipped,&LastCache](TArrayView<const UE::Anim::FMessageStack> Messages){auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("cache"),LastCache);R->SetNumberField(TEXT("count"),Messages.Num());Skipped.Add(MakeShared<FJsonValueObject>(R));});
                Root.Update(U);
            }
            InputCache->PostGraphUpdate();SplitCache->PostGraphUpdate();LocCache->PostGraphUpdate();
            TArray<TSharedPtr<FJsonValue>> Rows;
            for(int32 S=0;S<5;++S){const auto W=FSlotAccess::Weights(*Slots[S]);float SW=W.SourceWeight,BW=W.SlotNodeWeight,TW=W.TotalNodeWeight;auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("slot"),S);R->SetNumberField(TEXT("sourceWeight"),SW);R->SetNumberField(TEXT("slotWeight"),BW);R->SetNumberField(TEXT("totalWeight"),TW);Rows.Add(MakeShared<FJsonValueObject>(R));}
            auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("frozen"),Frozen);R->SetArrayField(TEXT("slots"),Rows);R->SetArrayField(TEXT("events"),Events);R->SetArrayField(TEXT("skipped"),Skipped);R->SetNumberField(TEXT("dynamicAlpha"),Dynamic->ActualAlpha);Frames.Add(MakeShared<FJsonValueObject>(R));
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
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);R->SetNumberField(TEXT("rootMotionMode"),(int32)ERootMotionMode::RootMotionFromMontagesOnly);
    FString Result;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result));return Result;
}
