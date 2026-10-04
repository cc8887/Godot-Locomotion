#include "AlsLyraMontageLibrary.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendProfile.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "AnimNodes/AnimNode_Slot.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Curves/CurveFloat.h"
#include "HAL/IConsoleManager.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraMontageProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_MONTAGE_FAILED line=%d"),Line);return {};}
FString Path(const UObject* Object){return Object?Object->GetPathName():FString();}
FString Text(const TSharedPtr<FJsonObject>& Data){FString Result;FJsonSerializer::Serialize(Data.ToSharedRef(),TJsonWriterFactory<>::Create(&Result));return Result;}
struct FInstanceAccess:UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}
    static void Tick(UAnimInstance* A,float D)
    {
        A->NotifyQueue.AnimNotifies.Reset();A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (A->*&FInstanceAccess::Montage_UpdateWeight)(D);(A->*&FInstanceAccess::Montage_Advance)(D);
        (A->*&FInstanceAccess::UpdateMontageEvaluationData)();
    }
};
struct FProxyAccess:FAnimInstanceProxy
{static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}};
struct FSlotAccess:FAnimNode_Slot
{static FSlotNodeWeightInfo Weights(FAnimNode_Slot& N){return N.*&FSlotAccess::WeightData;}};
struct FUpdateLeaf:FAnimNode_Base
{
    TSharedPtr<FJsonObject> Row;
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {
        Row->SetBoolField(TEXT("updated"),true);Row->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());
        Row->SetNumberField(TEXT("rootModifier"),C.GetRootMotionWeightModifier());Row->SetBoolField(TEXT("active"),C.IsActive());
    }
};
constexpr int32 NodeIds[]={81,71,2,74,84};
const TCHAR* SlotNames[]={TEXT("UpperBody"),TEXT("UpperBodyAdditive"),TEXT("FullBodyAdditivePreAim"),TEXT("AdditiveHitReact"),TEXT("FullBody")};
}

FString UAlsLyraMontageLibrary::ReadCatalog(const TArray<UAnimMontage*>& Montages)
{
    using namespace LyraMontageProbe;TArray<TSharedPtr<FJsonValue>> Assets;
    for(auto* M:Montages)
    {
        if(!M||!M->GetSkeleton())return Fail(__LINE__);auto R=MakeShared<FJsonObject>();
        R->SetStringField(TEXT("path"),Path(M));R->SetStringField(TEXT("skeleton"),Path(M->GetSkeleton()));
        R->SetStringField(TEXT("group"),M->GetGroupName().ToString());R->SetNumberField(TEXT("duration"),M->GetPlayLength());R->SetNumberField(TEXT("rateScale"),M->RateScale);
        R->SetNumberField(TEXT("blendInTime"),M->BlendIn.GetBlendTime());R->SetNumberField(TEXT("blendOutTime"),M->BlendOut.GetBlendTime());
        R->SetNumberField(TEXT("blendInOption"),static_cast<uint8>(M->BlendIn.GetBlendOption()));R->SetNumberField(TEXT("blendOutOption"),static_cast<uint8>(M->BlendOut.GetBlendOption()));
        R->SetNumberField(TEXT("blendModeIn"),static_cast<uint8>(M->BlendModeIn));R->SetNumberField(TEXT("blendModeOut"),static_cast<uint8>(M->BlendModeOut));
        R->SetStringField(TEXT("blendInCurve"),Path(M->BlendIn.GetCustomCurve()));R->SetStringField(TEXT("blendOutCurve"),Path(M->BlendOut.GetCustomCurve()));
        R->SetStringField(TEXT("blendInProfile"),Path(M->BlendProfileIn));R->SetStringField(TEXT("blendOutProfile"),Path(M->BlendProfileOut));
        R->SetNumberField(TEXT("blendOutTriggerTime"),M->BlendOutTriggerTime);R->SetBoolField(TEXT("autoBlendOut"),M->bEnableAutoBlendOut);
        R->SetBoolField(TEXT("legacyRootTranslation"),M->bEnableRootMotionTranslation);R->SetBoolField(TEXT("legacyRootRotation"),M->bEnableRootMotionRotation);
        R->SetBoolField(TEXT("rootMotion"),M->HasRootMotion());R->SetStringField(TEXT("syncGroup"),M->SyncGroup.ToString());R->SetNumberField(TEXT("syncSlotIndex"),M->SyncSlotIndex);
        TArray<TSharedPtr<FJsonValue>> Sections,Slots,Markers;
        for(const auto& S:M->CompositeSections)
        {auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),S.SectionName.ToString());V->SetStringField(TEXT("next"),S.NextSectionName.ToString());V->SetNumberField(TEXT("time"),S.GetTime());Sections.Add(MakeShared<FJsonValueObject>(V));}
        for(const auto& S:M->SlotAnimTracks)
        {
            auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),S.SlotName.ToString());V->SetStringField(TEXT("group"),M->GetSkeleton()->GetSlotGroupName(S.SlotName).ToString());TArray<TSharedPtr<FJsonValue>> Segments;
            for(const auto& A:S.AnimTrack.AnimSegments)
            {
                auto P=MakeShared<FJsonObject>();auto* Sequence=Cast<UAnimSequence>(A.GetAnimReference());
                P->SetStringField(TEXT("animation"),Path(A.GetAnimReference()));P->SetNumberField(TEXT("start"),A.StartPos);P->SetNumberField(TEXT("clipStart"),A.AnimStartTime);
                P->SetNumberField(TEXT("clipEnd"),A.AnimEndTime);P->SetNumberField(TEXT("clipRate"),A.AnimPlayRate);P->SetNumberField(TEXT("loops"),A.LoopingCount);
                P->SetNumberField(TEXT("additiveType"),Sequence?static_cast<uint8>(Sequence->AdditiveAnimType):-1);Segments.Add(MakeShared<FJsonValueObject>(P));
            }
            V->SetArrayField(TEXT("segments"),Segments);Slots.Add(MakeShared<FJsonValueObject>(V));
        }
        for(const auto& Mkr:M->MarkerData.AuthoredSyncMarkers)
        {auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),Mkr.MarkerName.ToString());V->SetNumberField(TEXT("time"),Mkr.Time);V->SetNumberField(TEXT("track"),Mkr.TrackIndex);Markers.Add(MakeShared<FJsonValueObject>(V));}
        R->SetArrayField(TEXT("sections"),Sections);R->SetArrayField(TEXT("slots"),Slots);R->SetArrayField(TEXT("markers"),Markers);Assets.Add(MakeShared<FJsonValueObject>(R));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("assets"),Assets);return Text(Result);
}

FString UAlsLyraMontageLibrary::ReadSlotUpdateTrace(UClass* MainClass,USkeletalMesh* Mesh,const TArray<UAnimMontage*>& Montages,const FString& RequestsJson)
{
    using namespace LyraMontageProbe;TSharedPtr<FJsonObject> Requests;const auto* Interface=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!Mesh||!Interface||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Requests->GetArrayField(TEXT("traces")))
    {
        auto T=TV->AsObject();const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* World;~FCleanup(){World->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=W->SpawnActor<ACharacter>(Spawn);if(!Owner)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(MainClass);
        C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& P=FInstanceAccess::Proxy(Main);
        FAnimNode_Slot* Slots[5];FUpdateLeaf Leaves[5];
        for(int32 I=0;I<5;++I)
        {
            const auto& Props=Interface->GetAnimNodeProperties();auto* Prop=Props[Props.Num()-1-NodeIds[I]];
            if(Prop->Struct!=FAnimNode_Slot::StaticStruct())return Fail(__LINE__);Slots[I]=Prop->ContainerPtrToValuePtr<FAnimNode_Slot>(Main);
            if(Slots[I]->SlotName!=SlotNames[I]||Slots[I]->bAlwaysUpdateSourcePose)return Fail(__LINE__);
            Slots[I]->Source.SetLinkNode(&Leaves[I]);Slots[I]->Initialize_AnyThread(FAnimationInitializeContext(&P));
        }
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());auto F=FV->AsObject();float D=F->GetNumberField(TEXT("delta"));
            FInstanceAccess::Tick(Main,D);FProxyAccess::Pre(P,Main,D);
            TArray<TSharedPtr<FJsonValue>> Rows,Instances;
            for(int32 I=0;I<5;++I)
            {
                if(F->GetBoolField(TEXT("initialize")))Slots[I]->Initialize_AnyThread(FAnimationInitializeContext(&P));
                auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("node"),NodeIds[I]);R->SetStringField(TEXT("slot"),SlotNames[I]);R->SetBoolField(TEXT("updated"),false);Leaves[I].Row=R;
                auto Context=FAnimationUpdateContext(&P,D).FractionalWeightAndRootMotion(F->GetNumberField(TEXT("weight")),F->GetNumberField(TEXT("rootModifier")));
                if(!F->GetBoolField(TEXT("active")))Context=Context.AsInactive();
                if(F->GetBoolField(TEXT("visited")))Slots[I]->Update_AnyThread(Context);
                auto V=FSlotAccess::Weights(*Slots[I]);R->SetNumberField(TEXT("sourceWeight"),V.SourceWeight);R->SetNumberField(TEXT("slotWeight"),V.SlotNodeWeight);R->SetNumberField(TEXT("totalWeight"),V.TotalNodeWeight);Rows.Add(MakeShared<FJsonValueObject>(R));
            }
            for(auto* I:Main->MontageInstances)if(I&&I->IsValid())
            {auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("asset"),Montages.IndexOfByKey(I->Montage));R->SetNumberField(TEXT("position"),I->GetPosition());R->SetNumberField(TEXT("weight"),I->GetWeight());R->SetBoolField(TEXT("playing"),I->IsPlaying());Instances.Add(MakeShared<FJsonValueObject>(R));}
            auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("slots"),Rows);Row->SetArrayField(TEXT("instances"),Instances);Frames.Add(MakeShared<FJsonValueObject>(Row));
            // Commands occur after the frozen graph snapshot, as in native pre-
            // Blueprint Montage tick. Newly played instances advance next frame.
            for(const auto& CV:F->GetArrayField(TEXT("commands")))
            {
                auto Command=CV->AsObject();int32 Asset=Command->GetNumberField(TEXT("asset"));if(!Montages.IsValidIndex(Asset))return Fail(__LINE__);
                if(Command->GetBoolField(TEXT("stop")))Main->Montage_Stop(Command->GetNumberField(TEXT("blend")),Montages[Asset]);
                else
                {
                    bool StopGroup=false;Command->TryGetBoolField(TEXT("stopGroup"),StopGroup);
                    if(Main->Montage_Play(Montages[Asset],Command->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,Command->GetNumberField(TEXT("start")),StopGroup)<=0)return Fail(__LINE__);
                }
            }
        }
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);R->SetBoolField(TEXT("markSourceInactive"),IConsoleManager::Get().FindConsoleVariable(TEXT("a.Montage.MarkMontageSourceAsBlendingOut"))->GetBool());return Text(R);
}
