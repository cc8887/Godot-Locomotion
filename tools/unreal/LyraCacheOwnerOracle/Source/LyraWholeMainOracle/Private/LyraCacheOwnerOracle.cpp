#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraCacheOwner
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_CACHE_OWNER_FAILED line=%d"),Line);return {};}
template<class Tag,typename Tag::Type Member>struct TMember{friend typename Tag::Type Access(Tag){return Member;}};
struct FFrameMember{using Type=uint64 FGraphTraversalCounter::*;friend Type Access(FFrameMember);};
template struct TMember<FFrameMember,&FGraphTraversalCounter::LastSyncronizedFrame>;
struct FCacheAccess:FAnimNode_SaveCachedPose
{
    static const FGraphTraversalCounter& Init(const FAnimNode_SaveCachedPose& N){return N.*&FCacheAccess::InitializationCounter;}
    static const FGraphTraversalCounter& Bones(const FAnimNode_SaveCachedPose& N){return N.*&FCacheAccess::CachedBonesCounter;}
    static const FGraphTraversalCounter& Eval(const FAnimNode_SaveCachedPose& N){return N.*&FCacheAccess::EvaluationCounter;}
    static const FGraphTraversalCounter& Update(const FAnimNode_SaveCachedPose& N){return N.*&FCacheAccess::UpdateCounter;}
};
struct FRootMember{using Type=FAnimNode_Base* FAnimInstanceProxy::*;friend Type Access(FRootMember);};
template struct TMember<FRootMember,&FAnimInstanceProxy::RootNode>;
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FCacheReadDriver:FAnimNode_Base
{TFunction<void()> Read;void Evaluate_AnyThread(FPoseContext& Output) override{Read();Output.ResetToRefPose();}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);TArray<FBoneIndexType> Required;
        for(int32 I=0;I<S->GetReferenceSkeleton().GetNum();++I)Required.Add(static_cast<FBoneIndexType>(I));
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
    }
    static void Root(FAnimInstanceProxy& P,FAnimNode_Base* N){P.*Access(FRootMember{})=N;}
    static void Set(FAnimInstanceProxy& P,const FString& Op,const FGraphTraversalCounter& C)
    {
        if(Op==TEXT("init"))P.*&FProxyAccess::InitializationCounter=C;
        else if(Op==TEXT("bones"))P.*&FProxyAccess::CachedBonesCounter=C;
        else if(Op==TEXT("update"))P.*&FProxyAccess::UpdateCounter=C;
        else P.*&FProxyAccess::EvaluationCounter=C;
    }
};
struct FCacheLeafProbe:FAnimNode_Base
{
    int32 Initializations=0,Bones=0,Updates=0,Evaluations=0;float Marker=0,Weight=0;
    void Initialize_AnyThread(const FAnimationInitializeContext&) override{++Initializations;}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext&) override{++Bones;}
    void Update_AnyThread(const FAnimationUpdateContext& C) override{++Updates;Weight=C.GetFinalBlendWeight();}
    void Evaluate_AnyThread(FPoseContext& O) override
    {++Evaluations;O.ResetToRefPose();auto T=O.Pose[FCompactPoseBoneIndex(0)];T.SetTranslation(FVector(Marker,0,0));O.Pose[FCompactPoseBoneIndex(0)]=T;}
};
FGraphTraversalCounter Counter(int32 Value)
{
    FGraphTraversalCounter C;int32 Limit=0;
    do{C.Increment();check(++Limit<=65536);}while(C.Get()!=Value);return C;
}
TSharedPtr<FJsonObject> Snapshot(const FAnimNode_SaveCachedPose& N,const FCacheLeafProbe& L)
{
    auto R=MakeShared<FJsonObject>();
    auto C=[&](const TCHAR* Name,const FGraphTraversalCounter& V)
    {auto J=MakeShared<FJsonObject>();J->SetNumberField(TEXT("counter"),V.Get());J->SetNumberField(TEXT("frame"),V.HasEverBeenUpdated()?static_cast<double>(V.*Access(FFrameMember{})):-1);R->SetObjectField(Name,J);};
    C(TEXT("initialization"),FCacheAccess::Init(N));C(TEXT("bones"),FCacheAccess::Bones(N));C(TEXT("evaluation"),FCacheAccess::Eval(N));C(TEXT("update"),FCacheAccess::Update(N));
    R->SetNumberField(TEXT("weight"),N.GlobalWeight);R->SetNumberField(TEXT("initializations"),L.Initializations);R->SetNumberField(TEXT("boneCalls"),L.Bones);
    R->SetNumberField(TEXT("updates"),L.Updates);R->SetNumberField(TEXT("evaluations"),L.Evaluations);R->SetNumberField(TEXT("sourceWeight"),L.Weight);return R;
}
}

FString ULyraWholeMainOracleLibrary::ReadCacheLifecycle(const FString& RequestsJson)
{
    using namespace LyraCacheOwner;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MainClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* ProviderClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    if(!MainClass||!ProviderClass||!Mesh)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(GetTransientPackage(),NAME_None,RF_Transient));Component->SetSkeletalMeshAsset(Mesh);
    TStrongObjectPtr<UAnimInstance> Main(NewObject<UAnimInstance>(Component.Get(),MainClass,NAME_None,RF_Transient));
    TStrongObjectPtr<UAnimInstance> Provider(NewObject<UAnimInstance>(Component.Get(),ProviderClass,NAME_None,RF_Transient));
    auto& MainProxy=FInstanceAccess::Proxy(Main.Get());auto& ProviderProxy=FInstanceAccess::Proxy(Provider.Get());FProxyAccess::Setup(MainProxy,Main.Get(),Mesh->GetSkeleton());FProxyAccess::Setup(ProviderProxy,Provider.Get(),Mesh->GetSkeleton());
    UAnimInstance* Owners[]={Main.Get(),Main.Get(),Provider.Get()};FAnimInstanceProxy* Proxies[]={&MainProxy,&MainProxy,&ProviderProxy};int32 Ids[]={78,83,78};
    FAnimNode_SaveCachedPose* Nodes[3];FAnimNode_Base* Original[3];FCacheLeafProbe Leaves[3];
    for(int32 I=0;I<3;++I)
    {
        const auto* Class=IAnimClassInterface::GetFromClass(Owners[I]->GetClass());const auto& P=Class->GetAnimNodeProperties();const int32 Index=P.Num()-1-Ids[I];
        if(!P.IsValidIndex(Index)||P[Index]->Struct!=FAnimNode_SaveCachedPose::StaticStruct())return Fail(__LINE__);
        Nodes[I]=P[Index]->ContainerPtrToValuePtr<FAnimNode_SaveCachedPose>(Owners[I]);Original[I]=Nodes[I]->Pose.GetLinkNode();Nodes[I]->Pose.SetLinkNode(&Leaves[I]);
    }
    struct FRestore{FAnimNode_SaveCachedPose** N;FAnimNode_Base** O;~FRestore(){for(int32 I=0;I<3;++I)N[I]->Pose.SetLinkNode(O[I]);}} Restore{Nodes,Original};
    const uint64 PriorFrame=GFrameCounter;struct FFrameRestore{uint64 V;~FFrameRestore(){GFrameCounter=V;}} RestoreFrame{PriorFrame};
    auto States=[&](){TArray<TSharedPtr<FJsonValue>> S;for(int32 I=0;I<3;++I)S.Add(MakeShared<FJsonValueObject>(Snapshot(*Nodes[I],Leaves[I])));return S;};
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("initial"),States());TArray<TSharedPtr<FJsonValue>> Rows;
    const FMemMark Mark(FMemStack::Get());
    for(const auto& V:Q->GetArrayField(TEXT("steps")))
    {
        const auto Step=V->AsObject();const FString Op=Step->GetStringField(TEXT("op"));const int32 Count=Step->GetIntegerField(TEXT("counter"));
        if(Count==-1||Count<-32768||Count>32767)return Fail(__LINE__);
        GFrameCounter=static_cast<uint64>(Step->GetIntegerField(TEXT("frame")));const auto C=Counter(Count);
        FProxyAccess::Set(MainProxy,Op,C);FProxyAccess::Set(ProviderProxy,Op,C);
        TArray<TSharedPtr<FJsonValue>> Outputs;
        if(Op==TEXT("init"))for(int32 I=0;I<3;++I){FAnimationInitializeContext Context(Proxies[I]);Nodes[I]->Initialize_AnyThread(Context);}
        else if(Op==TEXT("bones"))for(int32 I=0;I<3;++I){FAnimationCacheBonesContext Context(Proxies[I]);Nodes[I]->CacheBones_AnyThread(Context);}
        else if(Op==TEXT("update"))for(int32 I=0;I<3;++I)
        {
            for(const auto& W:Step->GetArrayField(TEXT("weights"))[I]->AsArray())
            {FAnimationUpdateContext Context(Proxies[I],1.f/60.f);Context=Context.FractionalWeight(static_cast<float>(W->AsNumber()));Nodes[I]->Update_AnyThread(Context);}
            Nodes[I]->PostGraphUpdate();
        }
        else if(Op==TEXT("evaluate")||Op==TEXT("nested"))
        {
            FCacheReadDriver Outer;
            auto Read=[&](int32 Pass)
            {
                TArray<TSharedPtr<FJsonValue>> Values;
                for(int32 I=0;I<3;++I){Leaves[I].Marker=static_cast<float>(Step->GetNumberField(TEXT("marker")))+Pass*10.f+I*.125f;FPoseContext Output(Proxies[I]);Nodes[I]->Evaluate_AnyThread(Output);Values.Add(MakeShared<FJsonValueNumber>(Output.Pose[FCompactPoseBoneIndex(0)].GetTranslation().X));}
                Outputs.Add(MakeShared<FJsonValueArray>(Values));
            };
            auto EvaluateRoot=[&](FCacheReadDriver& Driver)
            {
                auto* OldRoot=MainProxy.GetRootNode();FProxyAccess::Root(MainProxy,&Driver);
                FCompactPose Pose;FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;
                FParallelEvaluationData Data{Curve,Pose,Attributes};Main->ParallelEvaluateAnimation(false,Mesh,Data);
                FProxyAccess::Root(MainProxy,OldRoot);
            };
            Outer.Read=[&]()
            {
                // EvaluateAnimation increments Main's proxy before entering
                // its root. This probe controls the counter at the cache nodes.
                FProxyAccess::Set(MainProxy,Op,C);
                Read(0);
                if(Op==TEXT("nested"))
                {
                    GFrameCounter++;const auto Other=Counter(Count==32767?-32768:Count+1);
                    FCacheReadDriver Inner;Inner.Read=[&](){FProxyAccess::Set(MainProxy,Op,Other);Read(1);};
                    FProxyAccess::Set(MainProxy,Op,Other);FProxyAccess::Set(ProviderProxy,Op,Other);EvaluateRoot(Inner);
                    FProxyAccess::Set(MainProxy,Op,C);FProxyAccess::Set(ProviderProxy,Op,C);Read(2);Read(3);
                }
                else Read(1);
            };
            EvaluateRoot(Outer);
        }
        else return Fail(__LINE__);
        auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("states"),States());Row->SetArrayField(TEXT("outputs"),Outputs);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("rows"),Rows);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
