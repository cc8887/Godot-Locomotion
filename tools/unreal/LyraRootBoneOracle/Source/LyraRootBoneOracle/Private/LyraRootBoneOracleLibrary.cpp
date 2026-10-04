#include "LyraRootBoneOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_Root.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraRootBone
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_ROOT_BONES_FAILED line=%d"),L);return {};}
template<class Tag,typename Tag::Type Member>struct TMember{friend typename Tag::Type Access(Tag){return Member;}};
struct FFrameMember{using Type=uint64 FGraphTraversalCounter::*;friend Type Access(FFrameMember);};
template struct TMember<FFrameMember,&FGraphTraversalCounter::LastSyncronizedFrame>;
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Update(FAnimInstanceProxy& P)
    {FAnimationUpdateSharedContext Shared;FAnimationUpdateContext C(&P,0.f,&Shared);(P.*&FProxyAccess::UpdateAnimation_WithRoot)(C,P.GetRootNode(),FName(TEXT("AnimGraph")));}
    static void Bones(FAnimInstanceProxy& P){(P.*&FProxyAccess::CacheBones)();}
    static void Invalidate(FAnimInstanceProxy& P)
    {(P.*static_cast<void(FAnimInstanceProxy::*)(const UE::Anim::FCurveFilterSettings&)>(&FProxyAccess::RecalcRequiredCurves))(UE::Anim::FCurveFilterSettings());}
};
TSharedPtr<FJsonObject> Phases(const FAnimInstanceProxy& P,uint64 BaseFrame)
{
    auto J=MakeShared<FJsonObject>();
    auto Add=[&](const TCHAR* Name,const FGraphTraversalCounter& C)
    {auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("counter"),C.Get());R->SetNumberField(TEXT("frame"),C.HasEverBeenUpdated()?static_cast<double>(C.*Access(FFrameMember{}))-static_cast<double>(BaseFrame):-1);J->SetObjectField(Name,R);};
    Add(TEXT("initialization"),P.GetInitializationCounter());Add(TEXT("bones"),P.GetCachedBonesCounter());
    Add(TEXT("update"),P.GetUpdateCounter());Add(TEXT("evaluation"),P.GetEvaluationCounter());return J;
}
struct FTap:FAnimNode_Base
{
    FAnimNode_Base* Original=nullptr;FString Name;int32 Owner=-1;
    TArray<TSharedPtr<FJsonValue>>* Events=nullptr;uint64 BaseFrame=0;
    void Event(const TCHAR* Phase,const TCHAR* Boundary,FAnimInstanceProxy* P)
    {auto J=MakeShared<FJsonObject>();J->SetStringField(TEXT("hook"),Name);J->SetNumberField(TEXT("owner"),Owner);J->SetStringField(TEXT("phase"),Phase);J->SetStringField(TEXT("boundary"),Boundary);J->SetObjectField(TEXT("phases"),Phases(*P,BaseFrame));Events->Add(MakeShared<FJsonValueObject>(J));}
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override
    {Event(TEXT("initialize"),TEXT("enter"),C.AnimInstanceProxy);Original->Initialize_AnyThread(C);Event(TEXT("initialize"),TEXT("leave"),C.AnimInstanceProxy);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override
    {Event(TEXT("bones"),TEXT("enter"),C.AnimInstanceProxy);Original->CacheBones_AnyThread(C);Event(TEXT("bones"),TEXT("leave"),C.AnimInstanceProxy);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {Event(TEXT("update"),TEXT("enter"),C.AnimInstanceProxy);Original->Update_AnyThread(C);Event(TEXT("update"),TEXT("leave"),C.AnimInstanceProxy);}
    void Evaluate_AnyThread(FPoseContext& C) override{Original->Evaluate_AnyThread(C);}
};
}

FString ULyraRootBoneOracleLibrary::ReadRootBones(const FString& RequestsJson)
{
    using namespace LyraRootBone;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MainClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* ProviderClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    const auto* MI=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    const auto* ProviderInterface=ProviderClass?IAnimClassInterface::GetFromClass(ProviderClass):nullptr;
    if(!MI||!ProviderInterface||!Mesh)return Fail(__LINE__);
    auto& Functions=const_cast<TArray<FAnimBlueprintFunction>&>(ProviderInterface->GetAnimBlueprintFunctions());
    struct FGroupsRestore{TArray<FAnimBlueprintFunction>& Target;TArray<FAnimBlueprintFunction> Old;~FGroupsRestore(){Target=MoveTemp(Old);}} GroupsRestore{Functions,Functions};
    for(const auto& Pair:Q->GetObjectField(TEXT("groups"))->Values)
    {auto* F=Functions.FindByPredicate([&](const auto& V){return V.Name==FName(*Pair.Key);});if(!F||!F->bImplemented)return Fail(__LINE__);F->Group=FName(*Pair.Value->AsString());}
    const FMemMark Mark(FMemStack::Get());const uint64 BaseFrame=GFrameCounter;
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{W.Get()};
    auto* Actor=W->SpawnActor<ACharacter>();if(!Actor)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Actor,NAME_None,RF_Transient));
    C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    C->SetSkeletalMeshAsset(Mesh);C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Actor->GetRootComponent());Actor->AddInstanceComponent(C.Get());C->RegisterComponent();
    auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& MP=FInstanceAccess::Proxy(Main);
    TArray<TSharedPtr<FJsonValue>> Events,Rows;
    auto Snapshot=[&](const TCHAR* Stage)
    {
        auto J=MakeShared<FJsonObject>();J->SetStringField(TEXT("stage"),Stage);J->SetObjectField(TEXT("main"),Phases(MP,BaseFrame));
        const auto& Instances=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();TArray<TSharedPtr<FJsonValue>> Providers,Calls;
        for(int32 I=0;I<Instances.Num();++I){auto P=MakeShared<FJsonObject>();P->SetNumberField(TEXT("owner"),I);P->SetObjectField(TEXT("phases"),Phases(FInstanceAccess::Proxy(Instances[I]),BaseFrame));Providers.Add(MakeShared<FJsonValueObject>(P));}
        for(auto* P:MI->GetLinkedAnimLayerNodeProperties()){auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("function"),N->Layer.ToString());R->SetNumberField(TEXT("owner"),Instances.IndexOfByKey(N->GetTargetInstance<UAnimInstance>()));Calls.Add(MakeShared<FJsonValueObject>(R));}
        J->SetArrayField(TEXT("providers"),Providers);J->SetArrayField(TEXT("calls"),Calls);J->SetArrayField(TEXT("events"),Events);Events.Reset();Rows.Add(MakeShared<FJsonValueObject>(J));
    };
    TArray<TUniquePtr<FTap>> Taps;
    struct FRestoredLink{FAnimNode_LinkedAnimLayer* Node;FAnimNode_Base* Original;};TArray<FRestoredLink> LinkedRoots;
    auto* Root=static_cast<FAnimNode_Root*>(MP.GetRootNode());if(!Root)return Fail(__LINE__);const FPoseLink OriginalResult=Root->Result;
    struct FRestore{FAnimNode_Root* Root;FPoseLink Result;TArray<FRestoredLink>& Links;~FRestore(){Root->Result=Result;for(const auto& L:Links)L.Node->LinkedRoot=L.Original;}} Restore{Root,OriginalResult,LinkedRoots};
    Taps.Add(MakeUnique<FTap>());auto* MainTap=Taps.Last().Get();MainTap->Original=Root->Result.GetLinkNode();MainTap->Name=TEXT("AnimGraph");MainTap->Events=&Events;MainTap->BaseFrame=BaseFrame;
    if(!MainTap->Original){const int32 Index=MI->GetAnimNodeProperties().Num()-1-Root->Result.LinkID;MainTap->Original=MP.GetMutableNodeFromIndex<FAnimNode_Base>(Index);}
    if(!MainTap->Original)return Fail(__LINE__);Root->Result.SetLinkNode(MainTap);
    auto WrapLinked=[&]()
    {
        const auto& Instances=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();
        for(auto* P:MI->GetLinkedAnimLayerNodeProperties())
        {
            auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);const int32 Owner=Instances.IndexOfByKey(N->GetTargetInstance<UAnimInstance>());
            if(Owner<0||!N->LinkedRoot)return false;
            auto T=MakeUnique<FTap>();T->Original=N->LinkedRoot;T->Name=N->Layer.ToString();T->Owner=Owner;T->Events=&Events;T->BaseFrame=BaseFrame;
            LinkedRoots.Add({N,N->LinkedRoot});N->LinkedRoot=T.Get();Taps.Add(MoveTemp(T));
        }
        return true;
    };
    Snapshot(TEXT("registered"));const FString Mode=Q->GetStringField(TEXT("mode"));
    if(Mode==TEXT("before-root")){Main->LinkAnimClassLayers(ProviderClass);Snapshot(TEXT("linked"));if(!WrapLinked())return Fail(__LINE__);}
    FProxyAccess::Update(MP);Snapshot(TEXT("first-update"));FProxyAccess::Bones(MP);Snapshot(TEXT("repeated-bones"));
    if(Mode==TEXT("after-root")){Main->LinkAnimClassLayers(ProviderClass);Snapshot(TEXT("linked"));if(!WrapLinked())return Fail(__LINE__);}
    if(Mode!=TEXT("self"))
    {
        const auto Before=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();Main->LinkAnimClassLayers(ProviderClass);
        if(Before!=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances())return Fail(__LINE__);Snapshot(TEXT("same-class"));
    }
    if(Mode!=TEXT("self"))
    {
        FProxyAccess::Update(MP);Snapshot(TEXT("linked-update"));
        FProxyAccess::Update(MP);Snapshot(TEXT("repeated-linked-update"));
    }
    FProxyAccess::Invalidate(MP);FProxyAccess::Bones(MP);Snapshot(TEXT("invalidated-bones"));FProxyAccess::Bones(MP);Snapshot(TEXT("repeated-invalidated-bones"));
    if(Mode!=TEXT("self"))
    {
        auto InvalidateProviders=[&]()
        {for(auto* Instance:static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances())FProxyAccess::Invalidate(FInstanceAccess::Proxy(Instance));};
        InvalidateProviders();FProxyAccess::Update(MP);Snapshot(TEXT("providers-invalidated-update"));
        FProxyAccess::Update(MP);Snapshot(TEXT("repeated-providers-update"));
        // Call the original Linked node. It inherits Update, propagates actual
        // inputs and enters the original target Proxy and function root.
        auto DirectPass=[&](const TCHAR* Prefix)
        {
            for(auto* Property:MI->GetLinkedAnimLayerNodeProperties())
            {
                auto* Node=Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
                FAnimationUpdateSharedContext Shared;FAnimationUpdateContext Context(&MP,0.f,&Shared);
                Node->Update_AnyThread(Context);
                Snapshot(*(FString(Prefix)+Node->Layer.ToString()));
            }
        };
        DirectPass(TEXT("direct-"));DirectPass(TEXT("repeat-direct-"));
        InvalidateProviders();DirectPass(TEXT("invalidated-direct-"));DirectPass(TEXT("repeat-invalidated-direct-"));
    }
    if(GFrameCounter!=BaseFrame)return Fail(__LINE__);
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("rows"),Rows);R->SetNumberField(TEXT("bones"),Mesh->GetRefSkeleton().GetNum());R->SetNumberField(TEXT("counterWrites"),0);R->SetNumberField(TEXT("globalFrameWrites"),0);
    FString Text;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Text));return Text;
}
