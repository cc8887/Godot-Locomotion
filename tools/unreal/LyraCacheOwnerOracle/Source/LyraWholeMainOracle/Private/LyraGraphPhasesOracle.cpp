#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraGraphPhases
{
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_GRAPH_PHASE_FAILED line=%d"),L);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{static void InitializeRoot(FAnimInstanceProxy& P){(P.*static_cast<void(FAnimInstanceProxy::*)(FAnimNode_Base*)>(&FProxyAccess::InitializeRootNode_WithRoot))(P.GetRootNode());}};
struct FMachineAccess:FAnimNode_StateMachine
{static int32 Count(const FAnimNode_StateMachine& M){return (M.*static_cast<const FBakedAnimationStateMachine*(FAnimNode_StateMachine::*)()const>(&FMachineAccess::GetMachineDescription))()->States.Num();}};
struct FTap:FAnimNode_Base
{
    FAnimNode_Base* Original;FString Name;TArray<TSharedPtr<FJsonValue>>* Trace;
    FTap(FAnimNode_Base* N,FString Id,TArray<TSharedPtr<FJsonValue>>* T):Original(N),Name(MoveTemp(Id)),Trace(T){}
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{Trace->Add(MakeShared<FJsonValueString>(Name));Original->Initialize_AnyThread(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{Trace->Add(MakeShared<FJsonValueString>(Name));Original->CacheBones_AnyThread(C);}
};
struct FLinkRestore{FPoseLinkBase* Link;FAnimNode_Base* Original;};
struct FRootRestore{FAnimNode_LinkedAnimLayer* Layer;FAnimNode_Base* Original;};
struct FOwner
{
    UAnimInstance* Instance;FString Name;const IAnimClassInterface* Class;
    TMap<int32,TUniquePtr<FTap>> Nodes;
    explicit FOwner(UAnimInstance* A,FString N):Instance(A),Name(MoveTemp(N)),Class(IAnimClassInterface::GetFromClass(A->GetClass())){}
};
}

FString ULyraWholeMainOracleLibrary::ReadGraphPhases(const FString& RequestsJson)
{
    using namespace LyraGraphPhases;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Class=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* Provider=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    if(!Class||!Provider||!Mesh)return Fail(__LINE__);
    const FMemMark Mark(FMemStack::Get());
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};
    auto* Actor=World->SpawnActor<AActor>();if(!Actor)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Actor,NAME_None,RF_Transient));
    Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMeshAsset(Mesh);Component->SetAnimInstanceClass(Class);Actor->SetRootComponent(Component.Get());Actor->AddInstanceComponent(Component.Get());Component->RegisterComponent();
    auto* Main=Component->GetAnimInstance();if(!Main)return Fail(__LINE__);
    Main->LinkAnimClassLayers(Provider);
    const auto& Linked=static_cast<const USkeletalMeshComponent*>(Component.Get())->GetLinkedAnimInstances();
    if(Linked.Num()!=1)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Trace;TArray<TUniquePtr<FOwner>> Owners;
    Owners.Add(MakeUnique<FOwner>(Main,TEXT("main")));Owners.Add(MakeUnique<FOwner>(Linked[0],TEXT("provider")));
    for(auto& Owner:Owners)
    {
        if(!Owner->Class)return Fail(__LINE__);
        const auto& Properties=Owner->Class->GetAnimNodeProperties();auto& Proxy=FInstanceAccess::Proxy(Owner->Instance);
        const auto& Nodes=Q->GetArrayField(Owner->Name+TEXT("Nodes"));
        for(const auto& V:Nodes)
        {
            const auto N=V->AsObject();const int32 Id=N->GetIntegerField(TEXT("index"));const int32 Property=Properties.Num()-1-Id;
            if(!Properties.IsValidIndex(Property)||Properties[Property]->Struct->GetPathName()!=N->GetStringField(TEXT("type")))return Fail(__LINE__);
            Owner->Nodes.Add(Id,MakeUnique<FTap>(Proxy.GetMutableNodeFromIndex<FAnimNode_Base>(Id),Owner->Name+TEXT(":")+FString::FromInt(Id),&Trace));
        }
    }
    TArray<FLinkRestore> Links;TArray<FRootRestore> Roots;
    struct FRestore{TArray<FLinkRestore>& L;TArray<FRootRestore>& R;~FRestore(){for(const auto& V:L)V.Link->SetLinkNode(V.Original);for(const auto& V:R)V.Layer->LinkedRoot=V.Original;}} Restore{Links,Roots};
    for(auto& Owner:Owners)
    {
        const auto& Properties=Owner->Class->GetAnimNodeProperties();
        for(auto& Pair:Owner->Nodes)
        {
            auto* Property=Properties[Properties.Num()-1-Pair.Key];auto* Original=Pair.Value->Original;
            auto Link=[&](FPoseLinkBase* P){if(P->LinkID>=0)if(auto* Tap=Owner->Nodes.Find(Properties.Num()-1-P->LinkID)){Links.Add({P,P->GetLinkNode()});P->SetLinkNode(Tap->Get());}};
            for(TFieldIterator<FProperty> Field(Property->Struct);Field;++Field)
            {
                if(auto* S=CastField<FStructProperty>(*Field);S&&S->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))Link(S->ContainerPtrToValuePtr<FPoseLinkBase>(Original));
                else if(auto* A=CastField<FArrayProperty>(*Field))if(auto* Inner=CastField<FStructProperty>(A->Inner);Inner&&Inner->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))
                {FScriptArrayHelper H(A,A->ContainerPtrToValuePtr<void>(Original));for(int32 I=0;I<H.Num();I++)Link(reinterpret_cast<FPoseLinkBase*>(H.GetRawPtr(I)));}
            }
            if(Property->Struct==FAnimNode_LinkedAnimLayer::StaticStruct()&&Owner->Instance==Main)
            {
                auto* Layer=static_cast<FAnimNode_LinkedAnimLayer*>(Original);
                if(Layer->GetTargetInstance<UAnimInstance>()!=Linked[0]||!Layer->LinkedRoot)return Fail(__LINE__);
                const auto Function=Q->GetObjectField(TEXT("roots"))->GetIntegerField(Layer->Layer.ToString());
                auto* Root=Owners[1]->Nodes.Find(Function);if(!Root||Root->Get()->Original!=Layer->LinkedRoot)return Fail(__LINE__);
                Roots.Add({Layer,Layer->LinkedRoot});Layer->LinkedRoot=Root->Get();
            }
        }
    }
    auto& Proxy=FInstanceAccess::Proxy(Main);auto Result=MakeShared<FJsonObject>();
    auto State=[&]()
    {
        TArray<TSharedPtr<FJsonValue>> Machines;
        for(auto& Owner:Owners)for(auto& Pair:Owner->Nodes)
        {
            const auto* P=Owner->Class->GetAnimNodeProperties()[Owner->Class->GetAnimNodeProperties().Num()-1-Pair.Key];
            if(P->Struct!=FAnimNode_StateMachine::StaticStruct())continue;
            auto* M=static_cast<FAnimNode_StateMachine*>(Pair.Value->Original);auto Row=MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("owner"),Owner->Name);Row->SetNumberField(TEXT("node"),Pair.Key);
            Row->SetNumberField(TEXT("state"),M->GetCurrentState());Row->SetNumberField(TEXT("elapsed"),M->GetCurrentStateElapsedTime());
            TArray<TSharedPtr<FJsonValue>> Weights;for(int32 I=0;I<FMachineAccess::Count(*M);I++)Weights.Add(MakeShared<FJsonValueNumber>(M->GetStateWeight(I)));
            Row->SetArrayField(TEXT("weights"),Weights);Machines.Add(MakeShared<FJsonValueObject>(Row));
        }
        return Machines;
    };
    Trace.Add(MakeShared<FJsonValueString>(TEXT("main:85")));FProxyAccess::InitializeRoot(Proxy);Result->SetArrayField(TEXT("initialize"),Trace);Trace.Reset();
    Result->SetArrayField(TEXT("machines"),State());
    TArray<TSharedPtr<FJsonValue>> CacheRows;
    const uint64 PriorFrame=GFrameCounter;
    struct FFrameRestore{uint64 Frame;~FFrameRestore(){GFrameCounter=Frame;}} RestoreFrame{PriorFrame};
    for(const auto& Step:Q->GetArrayField(TEXT("cacheSteps")))
    {
        const auto S=Step->AsObject();const int32 Counter=S->GetIntegerField(TEXT("counter"));const int32 Frame=S->GetIntegerField(TEXT("frame"));
        if(Counter==-1||Counter<-32768||Counter>32767||Frame<0)return Fail(__LINE__);
        GFrameCounter=PriorFrame+Frame;auto& C=const_cast<FGraphTraversalCounter&>(Proxy.GetCachedBonesCounter());C.Reset();
        int32 Limit=0;do{C.Increment();if(++Limit>65536)return Fail(__LINE__);}while(C.Get()!=Counter);
        FAnimationCacheBonesContext Context(&Proxy);Trace.Add(MakeShared<FJsonValueString>(TEXT("main:85")));Proxy.GetRootNode()->CacheBones_AnyThread(Context);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("counter"),Counter);Row->SetNumberField(TEXT("frame"),Frame);Row->SetArrayField(TEXT("order"),Trace);Trace.Reset();
        CacheRows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("cacheSteps"),CacheRows);Result->SetNumberField(TEXT("linkedInstances"),Linked.Num());
    TArray<TSharedPtr<FJsonValue>> Times;
    for(auto& Owner:Owners)for(auto& Pair:Owner->Nodes)
    {
        auto* P=Owner->Class->GetAnimNodeProperties()[Owner->Class->GetAnimNodeProperties().Num()-1-Pair.Key];
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("owner"),Owner->Name);Row->SetNumberField(TEXT("node"),Pair.Key);
        if(P->Struct==FAnimNode_SequencePlayer::StaticStruct())Row->SetNumberField(TEXT("time"),static_cast<FAnimNode_SequencePlayer*>(Pair.Value->Original)->GetAccumulatedTime());
        else if(P->Struct==FAnimNode_SequenceEvaluator::StaticStruct())Row->SetNumberField(TEXT("time"),static_cast<FAnimNode_SequenceEvaluator*>(Pair.Value->Original)->GetExplicitTime());
        else continue;
        Times.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("sourceTimes"),Times);FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
