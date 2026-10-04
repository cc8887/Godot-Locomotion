#include "LyraWeaponMontageOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimNode_Root.h"
#include "AnimNodes/AnimNode_RefPose.h"
#include "AnimNodes/AnimNode_Slot.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace WeaponMontageProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_WEAPON_MONTAGE_FAILED line=%d"),Line);return {};}
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
{
    static const TArray<FMontageEvaluationState>& Frozen(const FAnimInstanceProxy& P)
    {const auto Read=static_cast<const TArray<FMontageEvaluationState>&(FAnimInstanceProxy::*)()const>(&FProxyAccess::GetMontageEvaluationData);return (P.*Read)();}
    static void Bones(FAnimInstanceProxy& P,USkeletalMesh* Mesh)
    {
        TArray<FBoneIndexType> Required;for(int32 I=0;I<Mesh->GetRefSkeleton().GetNum();++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
        (P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D){(P.*&FProxyAccess::PreUpdate)(A,D);}
    static void UpdateRoot(FAnimInstanceProxy& P,float D)
    {(P.*&FProxyAccess::UpdateAnimationNode_WithRoot)(FAnimationUpdateContext(&P,D),P.GetRootNode(),TEXT("AnimGraph"));}
};
TSharedPtr<FJsonObject> Atom(const FTransform& T)
{
    auto R=MakeShared<FJsonObject>();const auto P=T.GetTranslation();const auto Q=T.GetRotation();const auto S=T.GetScale3D();
    auto Array=[](std::initializer_list<double> Values){TArray<TSharedPtr<FJsonValue>> A;for(double V:Values)A.Add(MakeShared<FJsonValueNumber>(V));return A;};
    R->SetArrayField(TEXT("position"),Array({P.X,P.Y,P.Z}));R->SetArrayField(TEXT("rotation"),Array({Q.X,Q.Y,Q.Z,Q.W}));R->SetArrayField(TEXT("scale"),Array({S.X,S.Y,S.Z}));return R;
}
TSharedPtr<FJsonObject> Blend(const FAlphaBlend& B,float Start)
{
    auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("alpha"),B.GetAlpha());R->SetNumberField(TEXT("begin"),B.GetBeginValue());
    R->SetNumberField(TEXT("desired"),B.GetDesiredValue());R->SetNumberField(TEXT("weight"),B.GetBlendedValue());
    R->SetNumberField(TEXT("option"),(int32)B.GetBlendOption());R->SetNumberField(TEXT("startAlpha"),Start);return R;
}
}

FString ULyraWeaponMontageOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    using namespace WeaponMontageProbe;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        const auto T=TV->AsObject();auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*T->GetStringField(TEXT("mesh")));
        auto* Class=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));auto* Interface=Class?IAnimClassInterface::GetFromClass(Class):nullptr;
        if(!Mesh||!Interface)return Fail(__LINE__);TArray<UAnimMontage*> Montages;
        for(const auto& MV:T->GetArrayField(TEXT("montages")))
        {
            auto* M=LoadObject<UAnimMontage>(nullptr,*MV->AsString());if(!M||M->GetSkeleton()!=Mesh->GetSkeleton())return Fail(__LINE__);
            for(const auto& Track:M->SlotAnimTracks)for(const auto& Segment:Track.AnimTrack.AnimSegments)
                if(auto* S=Cast<UAnimSequence>(Segment.GetAnimReference().Get()))S->WaitOnExistingCompression(true);
            Montages.Add(M);
        }
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Actor=World->SpawnActor<AActor>(Spawn);if(!Actor)return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Actor,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        C->SetSkeletalMesh(Mesh);C->SetAnimInstanceClass(Class);Actor->AddInstanceComponent(C.Get());Actor->SetRootComponent(C.Get());C->RegisterComponent();
        auto* A=C->GetAnimInstance();if(!A||A->RootMotionMode!=ERootMotionMode::RootMotionFromMontagesOnly)return Fail(__LINE__);
        auto& P=FInstanceAccess::Proxy(A);FProxyAccess::Bones(P,Mesh);auto* Root=P.GetRootNode();if(!Root)return Fail(__LINE__);
        const auto& Properties=Interface->GetAnimNodeProperties();if(Properties.Num()!=3)return Fail(__LINE__);
        TArray<TSharedPtr<FJsonValue>> Nodes;int32 Roots=0,Slots=0,Refs=0;
        for(int32 Index=0;Index<Properties.Num();++Index)
        {
            auto* Property=Properties[Properties.Num()-1-Index];auto N=MakeShared<FJsonObject>();N->SetNumberField(TEXT("index"),Index);N->SetStringField(TEXT("type"),Property->Struct->GetPathName());
            if(Property->Struct==FAnimNode_Root::StaticStruct())++Roots;
            else if(Property->Struct==FAnimNode_Slot::StaticStruct())
            {
                auto* Slot=Property->ContainerPtrToValuePtr<FAnimNode_Slot>(A);if(Slot->SlotName!=TEXT("DefaultSlot"))return Fail(__LINE__);
                ++Slots;N->SetStringField(TEXT("name"),Slot->SlotName.ToString());N->SetBoolField(TEXT("alwaysUpdate"),Slot->bAlwaysUpdateSourcePose);
            }
            else if(Property->Struct==FAnimNode_RefPose::StaticStruct())
            {auto* Ref=Property->ContainerPtrToValuePtr<FAnimNode_RefPose>(A);++Refs;N->SetNumberField(TEXT("refPoseType"),(int32)Ref->GetRefPoseType());}
            else return Fail(__LINE__);Nodes.Add(MakeShared<FJsonValueObject>(N));
        }
        if(Roots!=1||Slots!=1||Refs!=1)return Fail(__LINE__);
        Root->Initialize_AnyThread(FAnimationInitializeContext(&P));Root->CacheBones_AnyThread(FAnimationCacheBonesContext(&P));
        auto IndexOf=[&](FAnimNode_Base* Node)->int32{for(int32 I=0;I<Properties.Num();++I)if(Properties[Properties.Num()-1-I]->ContainerPtrToValuePtr<FAnimNode_Base>(A)==Node)return I;return INDEX_NONE;};
        for(int32 I=0;I<Properties.Num();++I)
        {
            auto* Property=Properties[Properties.Num()-1-I];auto N=Nodes[I]->AsObject();
            if(Property->Struct==FAnimNode_Root::StaticStruct())N->SetNumberField(TEXT("source"),IndexOf(Property->ContainerPtrToValuePtr<FAnimNode_Root>(A)->Result.GetLinkNode()));
            else if(Property->Struct==FAnimNode_Slot::StaticStruct())N->SetNumberField(TEXT("source"),IndexOf(Property->ContainerPtrToValuePtr<FAnimNode_Slot>(A)->Source.GetLinkNode()));
        }
        TArray<TSharedPtr<FJsonValue>> Names,Parents,Reference;
        const auto& R=Mesh->GetRefSkeleton();for(int32 B=0;B<R.GetNum();++B)
        {Names.Add(MakeShared<FJsonValueString>(R.GetBoneName(B).ToString()));Parents.Add(MakeShared<FJsonValueNumber>(R.GetParentIndex(B)));Reference.Add(MakeShared<FJsonValueObject>(Atom(R.GetRefBonePose()[B])));}
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());const auto F=FV->AsObject();const float D=F->GetNumberField(TEXT("delta"));
            FProxyAccess::Pre(P,A,D);FInstanceAccess::Tick(A,D);FProxyAccess::UpdateRoot(P,D);
            TArray<TSharedPtr<FJsonValue>> Frozen;
            for(const auto& E:FProxyAccess::Frozen(P))
            {
                auto V=Blend(E.BlendInfo,E.BlendStartAlpha);V->SetNumberField(TEXT("asset"),Montages.IndexOfByKey(E.Montage.Get()));
                V->SetNumberField(TEXT("position"),E.MontagePosition);V->SetNumberField(TEXT("previous"),E.DeltaTimeRecord.GetPrevious());V->SetNumberField(TEXT("delta"),E.DeltaTimeRecord.Delta);
                V->SetBoolField(TEXT("profile"),E.ActiveBlendProfile!=nullptr);Frozen.Add(MakeShared<FJsonValueObject>(V));
            }
            auto Row=MakeShared<FJsonObject>();float Slot=0,Source=0,Total=0;P.GetSlotWeight(TEXT("DefaultSlot"),Slot,Source,Total);
            Row->SetArrayField(TEXT("frozen"),Frozen);Row->SetNumberField(TEXT("slotWeight"),Slot);Row->SetNumberField(TEXT("sourceWeight"),Source);Row->SetNumberField(TEXT("totalWeight"),Total);
            if(F->GetBoolField(TEXT("sample")))
            {
                FPoseContext Output(&P);Root->Evaluate_AnyThread(Output);TArray<TSharedPtr<FJsonValue>> Pose;
                if(Output.Pose.GetNumBones()!=R.GetNum())return Fail(__LINE__);
                for(int32 B=0;B<Output.Pose.GetNumBones();++B)Pose.Add(MakeShared<FJsonValueObject>(Atom(Output.Pose[FCompactPoseBoneIndex(B)])));
                Row->SetArrayField(TEXT("pose"),Pose);TArray<TSharedPtr<FJsonValue>> Curves;
                Output.Curve.ForEachElement([&](const UE::Anim::FCurveElement& E){auto V=MakeShared<FJsonObject>();V->SetStringField(TEXT("name"),E.Name.ToString());V->SetNumberField(TEXT("value"),E.Value);Curves.Add(MakeShared<FJsonValueObject>(V));});
                Row->SetArrayField(TEXT("curves"),Curves);
                Row->SetNumberField(TEXT("attributes"),Output.CustomAttributes.Num());
            }
            Frames.Add(MakeShared<FJsonValueObject>(Row));
            // Commands occur after this proxy freeze/evaluation. Character-to-weapon
            // component tick ordering is a separate production integration boundary.
            for(const auto& CV:F->GetArrayField(TEXT("commands")))
            {
                const auto Cmd=CV->AsObject();const int32 Asset=Cmd->GetNumberField(TEXT("asset"));if(!Montages.IsValidIndex(Asset))return Fail(__LINE__);
                if(Cmd->GetBoolField(TEXT("stop")))A->Montage_Stop(Cmd->GetNumberField(TEXT("blend")),Montages[Asset]);
                else if(A->Montage_Play(Montages[Asset],Cmd->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,Cmd->GetNumberField(TEXT("start")),Cmd->GetBoolField(TEXT("stopGroup")))<=0)return Fail(__LINE__);
            }
        }
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("kind"),T->GetStringField(TEXT("kind")));Row->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));
        Row->SetArrayField(TEXT("nodes"),Nodes);Row->SetArrayField(TEXT("names"),Names);Row->SetArrayField(TEXT("parents"),Parents);Row->SetArrayField(TEXT("reference"),Reference);Row->SetArrayField(TEXT("frames"),Frames);
        Row->SetNumberField(TEXT("rootMotionMode"),(int32)A->RootMotionMode);Traces.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
