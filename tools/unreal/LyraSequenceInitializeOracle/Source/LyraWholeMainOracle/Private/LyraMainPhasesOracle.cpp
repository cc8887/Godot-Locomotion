#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraMainPhases
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_PHASE_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void InitializeRoot(FAnimInstanceProxy& P)
    {(P.*static_cast<void(FAnimInstanceProxy::*)(FAnimNode_Base*)>(&FProxyAccess::InitializeRootNode_WithRoot))(P.GetRootNode());}
};
struct FTap:FAnimNode_Base
{
    FAnimNode_Base* Original;FString Name;TArray<TSharedPtr<FJsonValue>>* Trace;
    FTap(FAnimNode_Base* N,FString Id,TArray<TSharedPtr<FJsonValue>>* T):Original(N),Name(MoveTemp(Id)),Trace(T){}
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{Trace->Add(MakeShared<FJsonValueString>(Name));Original->Initialize_AnyThread(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{Trace->Add(MakeShared<FJsonValueString>(Name));Original->CacheBones_AnyThread(C);}
};
}

FString ULyraWholeMainOracleLibrary::ReadMainPhases(const FString& RequestsJson)
{
    using namespace LyraMainPhases;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Class=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    const auto* Interface=Class?IAnimClassInterface::GetFromClass(Class):nullptr;if(!Interface||!Mesh)return Fail(__LINE__);
    const FMemMark Mark(FMemStack::Get());
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};
    auto* Owner=World->SpawnActor<AActor>();if(!Owner)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
    Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMeshAsset(Mesh);Component->SetAnimInstanceClass(Class);Owner->SetRootComponent(Component.Get());Owner->AddInstanceComponent(Component.Get());Component->RegisterComponent();
    auto* Main=Component->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& Proxy=FInstanceAccess::Proxy(Main);
    TArray<TSharedPtr<FJsonValue>> Trace;TMap<int32,TUniquePtr<FTap>> Nodes;TArray<TUniquePtr<FTap>> Roots;
    const auto& Properties=Interface->GetAnimNodeProperties();
    for(const auto& V:Q->GetArrayField(TEXT("nodes")))
    {
        const auto N=V->AsObject();const int32 Id=N->GetIntegerField(TEXT("index"));
        const int32 Property=Properties.Num()-1-Id;
        if(!Properties.IsValidIndex(Property)||Properties[Property]->Struct->GetPathName()!=N->GetStringField(TEXT("type")))return Fail(__LINE__);
        auto* Original=Proxy.GetMutableNodeFromIndex<FAnimNode_Base>(Id);
        Nodes.Add(Id,MakeUnique<FTap>(Original,FString::Printf(TEXT("node:%d"),Id),&Trace));
    }
    struct FLinkRestore{FPoseLinkBase* Link;FAnimNode_Base* Original;};TArray<FLinkRestore> Links;
    struct FRootRestore{FAnimNode_LinkedAnimLayer* Layer;FAnimNode_Base* Original;};TArray<FRootRestore> LayerRoots;
    struct FRestore{TArray<FLinkRestore>& L;TArray<FRootRestore>& R;~FRestore(){for(const auto& V:L)V.Link->SetLinkNode(V.Original);for(const auto& V:R)V.Layer->LinkedRoot=V.Original;}} Restore{Links,LayerRoots};
    for(auto& Pair:Nodes)
    {
        auto* Property=Properties[Properties.Num()-1-Pair.Key];auto* Original=Pair.Value->Original;
        auto Link=[&](FPoseLinkBase* P)
        {if(P->LinkID>=0)if(auto* Tap=Nodes.Find(Properties.Num()-1-P->LinkID)){Links.Add({P,P->GetLinkNode()});P->SetLinkNode(Tap->Get());}};
        for(TFieldIterator<FProperty> Field(Property->Struct);Field;++Field)
        {
            if(auto* S=CastField<FStructProperty>(*Field);S&&S->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))Link(S->ContainerPtrToValuePtr<FPoseLinkBase>(Original));
            else if(auto* A=CastField<FArrayProperty>(*Field))if(auto* Inner=CastField<FStructProperty>(A->Inner);Inner&&Inner->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))
            {FScriptArrayHelper H(A,A->ContainerPtrToValuePtr<void>(Original));for(int32 I=0;I<H.Num();I++)Link(reinterpret_cast<FPoseLinkBase*>(H.GetRawPtr(I)));}
        }
        if(Property->Struct==FAnimNode_LinkedAnimLayer::StaticStruct())
        {
            auto* Layer=static_cast<FAnimNode_LinkedAnimLayer*>(Original);
            if(Layer->GetTargetInstance<UAnimInstance>()!=Main||!Layer->LinkedRoot)return Fail(__LINE__);
            LayerRoots.Add({Layer,Layer->LinkedRoot});Roots.Add(MakeUnique<FTap>(Layer->LinkedRoot,TEXT("self:")+Layer->Layer.ToString(),&Trace));Layer->LinkedRoot=Roots.Last().Get();
        }
    }
    auto* Machine=Proxy.GetMutableNodeFromIndex<FAnimNode_StateMachine>(7);
    auto Result=MakeShared<FJsonObject>();
    Trace.Add(MakeShared<FJsonValueString>(TEXT("node:85")));FProxyAccess::InitializeRoot(Proxy);Result->SetArrayField(TEXT("initialize"),Trace);Trace.Reset();
    Result->SetNumberField(TEXT("state"),Machine->GetCurrentState());Result->SetNumberField(TEXT("elapsed"),Machine->GetCurrentStateElapsedTime());
    TArray<TSharedPtr<FJsonValue>> Weights;for(int32 I=0;I<12;I++)Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetStateWeight(I)));Result->SetArrayField(TEXT("weights"),Weights);
    // Match an explicit full-layout bone-cache invalidation. Repeat the same
    // counter next, so native SaveCachedPose and state caches suppress children.
    const_cast<FGraphTraversalCounter&>(Proxy.GetCachedBonesCounter()).Increment();
    FAnimationCacheBonesContext Cache(&Proxy);Trace.Add(MakeShared<FJsonValueString>(TEXT("node:85")));Proxy.GetRootNode()->CacheBones_AnyThread(Cache);
    Result->SetArrayField(TEXT("cacheBones"),Trace);Trace.Reset();
    Trace.Add(MakeShared<FJsonValueString>(TEXT("node:85")));Proxy.GetRootNode()->CacheBones_AnyThread(Cache);Result->SetArrayField(TEXT("repeatedCacheBones"),Trace);
    Result->SetNumberField(TEXT("linkedInstances"),static_cast<const USkeletalMeshComponent*>(Component.Get())->GetLinkedAnimInstances().Num());
    FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
