#include "LyraProxyPhaseOracleLibrary.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimGraph.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraProxyPhase
{
template<class Tag,typename Tag::Type Member>struct TMember{friend typename Tag::Type Access(Tag){return Member;}};
struct FFrameMember{using Type=uint64 FGraphTraversalCounter::*;friend Type Access(FFrameMember);};
template struct TMember<FFrameMember,&FGraphTraversalCounter::LastSyncronizedFrame>;
struct FRootMember{using Type=FAnimNode_Base* FAnimInstanceProxy::*;friend Type Access(FRootMember);};
template struct TMember<FRootMember,&FAnimInstanceProxy::RootNode>;
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S,FAnimNode_Base* Root)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> Required;
        for(int32 I=0;I<S->GetReferenceSkeleton().GetNum();++I)Required.Add(static_cast<FBoneIndexType>(I));
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
        P.*Access(FRootMember{})=Root;
    }
    static uint64 WorkerFrame(const FAnimInstanceProxy& P){return P.*&FProxyAccess::FrameCounterForUpdate;}
    static void RestoreRoot(FAnimInstanceProxy& P,FAnimNode_Base* Root){P.*Access(FRootMember{})=Root;}
    static void InitializeProxy(FAnimInstanceProxy& P,UAnimInstance* A){(P.*&FProxyAccess::Initialize)(A);}
    static void EvaluateNode(FAnimInstanceProxy& P,FPoseContext& O,FAnimNode_Base* N){(P.*&FProxyAccess::EvaluateAnimationNode_WithRoot)(O,N);}
    static void InitializeRoot(FAnimInstanceProxy& P,FAnimNode_Base* N){(P.*&FProxyAccess::InitializeRootNode_WithRoot)(N);}
    static void Bones(FAnimInstanceProxy& P){(P.*&FProxyAccess::CacheBones)();}
    static void Invalidate(FAnimInstanceProxy& P)
    {(P.*static_cast<void(FAnimInstanceProxy::*)(const UE::Anim::FCurveFilterSettings&)>(&FProxyAccess::RecalcRequiredCurves))(UE::Anim::FCurveFilterSettings());}
    static void UpdateRoot(FAnimInstanceProxy& P,const FAnimationUpdateContext& C,FAnimNode_Base* N){(P.*&FProxyAccess::UpdateAnimation_WithRoot)(C,N,FName(TEXT("AnimGraph")));}
};
struct FLeaf:FAnimNode_Base
{
    int32 Initializations=0,Bones=0,Updates=0,Evaluations=0;
    void Initialize_AnyThread(const FAnimationInitializeContext&) override{++Initializations;}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext&) override{++Bones;}
    void Update_AnyThread(const FAnimationUpdateContext&) override{++Updates;}
    void Evaluate_AnyThread(FPoseContext& O) override{++Evaluations;O.ResetToRefPose();}
};
struct FDriver:FLeaf
{
    FAnimNode_LinkedAnimGraph* Calls=nullptr;int32 Mask=7;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override
    {++Initializations;for(int32 I=0;I<3;++I)if(Mask&(1<<I))Calls[I].Initialize_AnyThread(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override
    {++Bones;for(int32 I=0;I<3;++I)if(Mask&(1<<I))Calls[I].CacheBones_AnyThread(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {++Updates;for(int32 I=0;I<3;++I)if(Mask&(1<<I))Calls[I].Update_AnyThread(C);}
    void Evaluate_AnyThread(FPoseContext& O) override
    {++Evaluations;for(int32 I=0;I<3;++I)if(Mask&(1<<I))Calls[I].Evaluate_AnyThread(O);O.ResetToRefPose();}
};
TSharedPtr<FJsonObject> ProxySnapshot(const FAnimInstanceProxy& P,const ULyraProxyProbeInstance& A)
{
    auto J=MakeShared<FJsonObject>();
    auto Add=[&](const TCHAR* Name,const FGraphTraversalCounter& C)
    {auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("counter"),C.Get());V->SetNumberField(TEXT("frame"),C.HasEverBeenUpdated()?static_cast<double>(C.*Access(FFrameMember{})):-1);J->SetObjectField(Name,V);};
    Add(TEXT("initialization"),P.GetInitializationCounter());Add(TEXT("bones"),P.GetCachedBonesCounter());
    Add(TEXT("update"),P.GetUpdateCounter());Add(TEXT("evaluation"),P.GetEvaluationCounter());
    J->SetNumberField(TEXT("workerFrame"),static_cast<double>(FProxyAccess::WorkerFrame(P)));
    J->SetNumberField(TEXT("workerCalls"),A.WorkerCalls);return J;
}
TSharedPtr<FJsonObject> LeafSnapshot(const FLeaf& L)
{
    auto J=MakeShared<FJsonObject>();J->SetNumberField(TEXT("initializations"),L.Initializations);J->SetNumberField(TEXT("boneCalls"),L.Bones);
    J->SetNumberField(TEXT("updates"),L.Updates);J->SetNumberField(TEXT("evaluations"),L.Evaluations);return J;
}
}

FString ULyraProxyPhaseOracleLibrary::ReadProxyPhases(const FString& RequestsJson)
{
    using namespace LyraProxyPhase;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return {};
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Mesh)return {};
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(GetTransientPackage(),NAME_None,RF_Transient));Component->SetSkeletalMeshAsset(Mesh);
    TStrongObjectPtr<ULyraProxyProbeInstance> Main(NewObject<ULyraProxyProbeInstance>(Component.Get()));
    TStrongObjectPtr<ULyraProxyProbeInstance> P0(NewObject<ULyraProxyProbeInstance>(Component.Get()));
    TStrongObjectPtr<ULyraProxyProbeInstance> P1(NewObject<ULyraProxyProbeInstance>(Component.Get()));
    ULyraProxyProbeInstance* Instances[]={Main.Get(),P0.Get(),P1.Get()};
    FAnimInstanceProxy* Proxies[]={&FInstanceAccess::Proxy(Main.Get()),&FInstanceAccess::Proxy(P0.Get()),&FInstanceAccess::Proxy(P1.Get())};
    FAnimNode_LinkedAnimGraph Calls[3];LyraProxyPhase::FLeaf Leaves[3],OwnedProviderRoots[2];FDriver Driver;Driver.Calls=Calls;
    FProxyAccess::Setup(*Proxies[0],Main.Get(),Mesh->GetSkeleton(),&Driver);
    FProxyAccess::Setup(*Proxies[1],P0.Get(),Mesh->GetSkeleton(),&OwnedProviderRoots[0]);
    FProxyAccess::Setup(*Proxies[2],P1.Get(),Mesh->GetSkeleton(),&OwnedProviderRoots[1]);
    for(int32 I=0;I<3;++I){Calls[I].SetTargetInstance(I<2?P0.Get():P1.Get());Calls[I].LinkedRoot=&Leaves[I];}
    struct FRestore{FAnimInstanceProxy** P;uint64 Frame;~FRestore(){for(int32 I=0;I<3;++I)FProxyAccess::RestoreRoot(*P[I],nullptr);GFrameCounter=Frame;}} Restore{Proxies,GFrameCounter};
    auto Snapshot=[&]()
    {
        auto J=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> ProxyRows,LeafRows;
        for(int32 I=0;I<3;++I){ProxyRows.Add(MakeShared<FJsonValueObject>(ProxySnapshot(*Proxies[I],*Instances[I])));LeafRows.Add(MakeShared<FJsonValueObject>(LeafSnapshot(Leaves[I])));}
        J->SetArrayField(TEXT("proxies"),ProxyRows);J->SetArrayField(TEXT("leaves"),LeafRows);J->SetObjectField(TEXT("mainRoot"),LeafSnapshot(Driver));return J;
    };
    auto Result=MakeShared<FJsonObject>();Result->SetObjectField(TEXT("initial"),Snapshot());TArray<TSharedPtr<FJsonValue>> Rows;
    const FMemMark Mark(FMemStack::Get());
    for(const auto& V:Q->GetArrayField(TEXT("steps")))
    {
        auto S=V->AsObject();const FString Op=S->GetStringField(TEXT("op"));GFrameCounter=static_cast<uint64>(S->GetIntegerField(TEXT("frame")));
        Driver.Mask=S->GetIntegerField(TEXT("mask"));auto& P=*Proxies[0];
        if(Op==TEXT("initialize"))FProxyAccess::InitializeRoot(P,&Driver);
        else if(Op==TEXT("initialize-function")){FAnimationInitializeContext C(&P);Calls[0].InitializeSubGraph_AnyThread(C);}
        else if(Op==TEXT("invalidate"))FProxyAccess::Invalidate(P);
        else if(Op==TEXT("bones"))FProxyAccess::Bones(P);
        else if(Op==TEXT("bones-function")){FAnimationCacheBonesContext C(&P);Calls[0].CacheBonesSubGraph_AnyThread(C);}
        else if(Op==TEXT("update"))
        {FAnimationUpdateSharedContext Shared;FAnimationUpdateContext C(&P,1.f/60.f,&Shared);FProxyAccess::UpdateRoot(P,C,&Driver);}
        else if(Op==TEXT("update-function"))
        {FAnimationUpdateSharedContext Shared;FAnimationUpdateContext C(&P,1.f/60.f,&Shared);Calls[0].Update_AnyThread(C);}
        else if(Op==TEXT("evaluate"))
        {FCompactPose Pose;FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;FParallelEvaluationData Data{Curve,Pose,Attributes};Main->ParallelEvaluateAnimation(false,Mesh,Data);}
        else if(Op==TEXT("evaluate-function")){FPoseContext O(&P);Calls[0].Evaluate_AnyThread(O);}
        else if(Op==TEXT("null-initialize"))FProxyAccess::InitializeRoot(P,nullptr);
        else if(Op==TEXT("null-update"))
        {FAnimationUpdateSharedContext Shared;FAnimationUpdateContext C(&P,1.f/60.f,&Shared);FProxyAccess::UpdateRoot(P,C,nullptr);}
        else if(Op==TEXT("null-evaluate")){FPoseContext O(&P);FProxyAccess::EvaluateNode(P,O,nullptr);}
        else if(Op==TEXT("wrap"))
        {FPoseContext O(&P);for(int32 I=0;I<S->GetIntegerField(TEXT("iterations"));++I)FProxyAccess::EvaluateNode(P,O,&Driver);}
        else if(Op==TEXT("proxy-initialize")){FProxyAccess::InitializeProxy(P,Main.Get());FProxyAccess::RestoreRoot(P,&Driver);}
        else{UE_LOG(LogTemp,Error,TEXT("LYRA_PROXY_PHASE_FAILED unknown operation %s"),*Op);return {};}
        Rows.Add(MakeShared<FJsonValueObject>(Snapshot()));
    }
    Result->SetArrayField(TEXT("rows"),Rows);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
