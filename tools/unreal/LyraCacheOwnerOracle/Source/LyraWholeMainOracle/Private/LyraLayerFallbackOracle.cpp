#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AttributesRuntime.h"
#include "Animation/BuiltInAttributeTypes.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraLayerFallback
{
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FInput:FAnimNode_Base
{
    int32 Marker,Initializations=0,Caches=0,Updates=0,Evaluations=0;
    TArray<FString>* Order=nullptr;
    explicit FInput(int32 Value):Marker(Value){}
    void Initialize_AnyThread(const FAnimationInitializeContext&) override{++Initializations;Order->Add(FString::Printf(TEXT("input:%d"),Marker));}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext&) override{++Caches;Order->Add(FString::Printf(TEXT("input:%d"),Marker));}
    void Update_AnyThread(const FAnimationUpdateContext&) override{++Updates;}
    void Evaluate_AnyThread(FPoseContext& O) override
    {
        ++Evaluations;O.ResetToRefPose();
        O.Pose[FCompactPoseBoneIndex(0)].SetTranslation(FVector(Marker,-Marker,Marker*.5));
        O.Curve.Set(TEXT("FallbackProbe"),Marker+.25f);
        O.Curve.SetFlags(TEXT("FallbackProbe"),static_cast<UE::Anim::ECurveElementFlags>(2));
        const UE::Anim::FAttributeId Id(TEXT("FallbackProbe"),FCompactPoseBoneIndex(0));
        O.CustomAttributes.FindOrAdd<FIntegerAnimationAttribute>(Id)->Value=Marker*3;
    }
};
struct FRootTap:FAnimNode_Base
{
    FAnimNode_Base* Original;TArray<FString>* Order;int32 Initializations=0,Caches=0;
    FRootTap(FAnimNode_Base* Root,TArray<FString>* Trace):Original(Root),Order(Trace){}
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override
    {++Initializations;Order->Add(TEXT("root"));Original->Initialize_AnyThread(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override
    {++Caches;Order->Add(TEXT("root"));Original->CacheBones_AnyThread(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override{Original->Update_AnyThread(C);}
    void Evaluate_AnyThread(FPoseContext& C) override{Original->Evaluate_AnyThread(C);}
};
FString Fail(int32 L){UE_LOG(LogTemp,Error,TEXT("LYRA_LAYER_FALLBACK_FAILED line=%d"),L);return {};}
TSharedPtr<FJsonObject> Evaluate(FAnimNode_LinkedAnimLayer& Node,FAnimInstanceProxy& Proxy,
    const FString& Name,bool Additive,bool Prefilled)
{
    const FMemMark Mark(FMemStack::Get());FInput First(11),Second(22);
    // Swap only this live instance's incoming pose links for controlled inputs.
    // No CDO, compiled function, asset or package is modified.
    const auto Saved=Node.InputPoses;const auto SavedRoot=Node.LinkedRoot;
    TArray<FString> Order;First.Order=Second.Order=&Order;FRootTap Tap(SavedRoot,&Order);
    struct FRestore{FAnimNode_LinkedAnimLayer& Node;TArray<FPoseLink> Inputs;FAnimNode_Base* Root;~FRestore(){Node.InputPoses=Inputs;Node.LinkedRoot=Root;}} Restore{Node,Saved,SavedRoot};
    if(SavedRoot)Node.LinkedRoot=&Tap;
    for(int32 I=0;I<Node.InputPoses.Num();++I)Node.InputPoses[I].SetLinkNode(I?&Second:&First);
    FAnimationInitializeContext Init(&Proxy);FAnimationCacheBonesContext Cache(&Proxy);
    Node.Initialize_AnyThread(Init);
    TArray<TSharedPtr<FJsonValue>> InitOrder;for(const auto& V:Order)InitOrder.Add(MakeShared<FJsonValueString>(V));Order.Reset();
    Node.CacheBones_AnyThread(Cache);
    TArray<TSharedPtr<FJsonValue>> CacheOrder;for(const auto& V:Order)CacheOrder.Add(MakeShared<FJsonValueString>(V));
    // UpdateAnimation may cache again; freeze the phase measurement before
    // exercising the separate self/unbound Update/Evaluate paths below.
    const int32 RootInit=Tap.Initializations,RootCache=Tap.Caches;
    const int32 FirstInit=First.Initializations,SecondInit=Second.Initializations,FirstCache=First.Caches,SecondCache=Second.Caches;
    // This oracle measures initialization/cache traversal of external roots.
    // Complete external pose evaluation requires the enclosing Main instance
    // entrypoint, worker update and cached-pose lifetime (covered separately).
    const bool External=Node.GetTargetInstance<UAnimInstance>()&&Node.GetTargetInstance<UAnimInstance>()!=Proxy.GetAnimInstanceObject();
    FAnimationUpdateSharedContext Shared;
    FAnimationUpdateContext Update(&Proxy,.02f,&Shared);if(!External)Node.Update_AnyThread(Update);
    FPoseContext Out(&Proxy,Additive);Out.ResetToRefPose();
    const UE::Anim::FAttributeId Id(TEXT("FallbackProbe"),FCompactPoseBoneIndex(0));
    if(Prefilled){Out.Curve.Set(TEXT("FallbackProbe"),-7);Out.Curve.SetFlags(TEXT("FallbackProbe"),static_cast<UE::Anim::ECurveElementFlags>(1));Out.CustomAttributes.FindOrAdd<FIntegerAnimationAttribute>(Id)->Value=-21;}
    if(!External)Node.Evaluate_AnyThread(Out);
    auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("name"),Name);
    Row->SetBoolField(TEXT("poseEvaluated"),!External);
    Row->SetArrayField(TEXT("initializeOrder"),InitOrder);Row->SetArrayField(TEXT("cacheOrder"),CacheOrder);
    Row->SetNumberField(TEXT("rootInitializations"),RootInit);Row->SetNumberField(TEXT("rootCaches"),RootCache);
    Row->SetNumberField(TEXT("firstInitializations"),FirstInit);Row->SetNumberField(TEXT("secondInitializations"),SecondInit);
    Row->SetNumberField(TEXT("firstCaches"),FirstCache);Row->SetNumberField(TEXT("secondCaches"),SecondCache);
    Row->SetNumberField(TEXT("inputs"),Saved.Num());Row->SetBoolField(TEXT("additive"),Additive);Row->SetBoolField(TEXT("prefilled"),Prefilled);
    Row->SetNumberField(TEXT("firstUpdates"),First.Updates);Row->SetNumberField(TEXT("secondUpdates"),Second.Updates);
    Row->SetNumberField(TEXT("firstEvaluations"),First.Evaluations);Row->SetNumberField(TEXT("secondEvaluations"),Second.Evaluations);
    bool HasCurve=false;Row->SetNumberField(TEXT("curve"),Out.Curve.Get(TEXT("FallbackProbe"),HasCurve));Row->SetBoolField(TEXT("hasCurve"),HasCurve);
    Row->SetNumberField(TEXT("curveFlags"),static_cast<uint32>(Out.Curve.GetFlags(TEXT("FallbackProbe"))));
    const auto* Attribute=Out.CustomAttributes.Find<FIntegerAnimationAttribute>(Id);
    Row->SetBoolField(TEXT("hasAttribute"),Attribute!=nullptr);Row->SetNumberField(TEXT("attribute"),Attribute?Attribute->Value:0);
    TArray<TSharedPtr<FJsonValue>> Pose;
    for(FCompactPoseBoneIndex Bone:Out.Pose.ForEachBoneIndex())
    {
        const auto& T=Out.Pose[Bone];auto P=MakeShared<FJsonObject>();
        auto Values=[](std::initializer_list<double> X){TArray<TSharedPtr<FJsonValue>> A;for(double V:X)A.Add(MakeShared<FJsonValueNumber>(V));return A;};
        const auto L=T.GetTranslation(),S=T.GetScale3D();const auto R=T.GetRotation();
        P->SetArrayField(TEXT("position"),Values({L.X,L.Y,L.Z}));P->SetArrayField(TEXT("rotation"),Values({R.X,R.Y,R.Z,R.W}));P->SetArrayField(TEXT("scale"),Values({S.X,S.Y,S.Z}));Pose.Add(MakeShared<FJsonValueObject>(P));
    }
    Row->SetArrayField(TEXT("pose"),Pose);return Row;
}
}

FString ULyraWholeMainOracleLibrary::ReadLayerFallback(const FString& RequestsJson)
{
    using namespace LyraLayerFallback;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* MainClass=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* Provider=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));
    const auto* Interface=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!Interface||!Provider||!Mesh)return Fail(__LINE__);
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};
    auto* Owner=World->SpawnActor<AActor>();if(!Owner)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
    Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMeshAsset(Mesh);Component->SetAnimInstanceClass(MainClass);Owner->SetRootComponent(Component.Get());Owner->AddInstanceComponent(Component.Get());Component->RegisterComponent();
    auto* Main=Component->GetAnimInstance();if(!Main)return Fail(__LINE__);auto& Proxy=FInstanceAccess::Proxy(Main);
    TArray<FBoneIndexType> Bones;for(int32 I=0;I<Mesh->GetRefSkeleton().GetNum();++I)Bones.Add(I);
    Proxy.GetRequiredBones().InitializeTo(Bones,UE::Anim::FCurveFilterSettings(),*Mesh);Proxy.GetRequiredBones().SetUseRAWData(true);
    TArray<TSharedPtr<FJsonValue>> Cases,Functions;
    for(const auto& F:Interface->GetAnimBlueprintFunctions())
    {
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("name"),F.Name.ToString());Row->SetBoolField(TEXT("implemented"),F.bImplemented);
        Row->SetBoolField(TEXT("hasRootProperty"),F.OutputPoseNodeProperty!=nullptr);Row->SetNumberField(TEXT("rootIndex"),F.OutputPoseNodeIndex);
        // bImplemented controls Link selection. It does not say whether the
        // compiler emitted an executable default root for self calls.
        const auto& Properties=Interface->GetAnimNodeProperties();TArray<TSharedPtr<FJsonValue>> Nodes;TSet<int32> Visited;
        TFunction<bool(int32)> Visit=[&](int32 Index)
        {
            if(Visited.Contains(Index))return true;if(!Properties.IsValidIndex(Index))return false;Visited.Add(Index);
            const auto* Property=Properties[Index];const void* Node=Property->ContainerPtrToValuePtr<void>(MainClass->GetDefaultObject());
            auto N=MakeShared<FJsonObject>();N->SetNumberField(TEXT("propertyIndex"),Index);N->SetStringField(TEXT("type"),Property->Struct->GetPathName());
            if(Property->Struct==FAnimNode_LinkedInputPose::StaticStruct())N->SetStringField(TEXT("inputName"),static_cast<const FAnimNode_LinkedInputPose*>(Node)->Name.ToString());
            TArray<TSharedPtr<FJsonValue>> Links;TArray<int32> Children;
            auto Link=[&](const FString& Pin,const FPoseLinkBase* P)
            {if(P->LinkID>=0){auto L=MakeShared<FJsonObject>();L->SetStringField(TEXT("pin"),Pin);L->SetNumberField(TEXT("propertyIndex"),P->LinkID);Links.Add(MakeShared<FJsonValueObject>(L));Children.Add(P->LinkID);}};
            for(TFieldIterator<FProperty> Field(Property->Struct);Field;++Field)
            {
                if(const auto* S=CastField<FStructProperty>(*Field);S&&S->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))Link(Field->GetName(),S->ContainerPtrToValuePtr<FPoseLinkBase>(Node));
                else if(const auto* A=CastField<FArrayProperty>(*Field))if(const auto* Inner=CastField<FStructProperty>(A->Inner);Inner&&Inner->Struct->IsChildOf(FPoseLinkBase::StaticStruct()))
                {FScriptArrayHelper H(A,A->ContainerPtrToValuePtr<void>(Node));for(int32 I=0;I<H.Num();I++)Link(FString::Printf(TEXT("%s[%d]"),*Field->GetName(),I),reinterpret_cast<const FPoseLinkBase*>(H.GetRawPtr(I)));}
            }
            N->SetArrayField(TEXT("links"),Links);Nodes.Add(MakeShared<FJsonValueObject>(N));for(int32 Child:Children)if(!Visit(Child))return false;return true;
        };
        // Main's full AnimGraph is already exported separately. Capture only
        // the fourteen default closures, using native property indices.
        if(F.Name!=TEXT("AnimGraph"))
        {
            const int32 Index=Properties.IndexOfByKey(F.OutputPoseNodeProperty);if(Index<0||!Visit(Index))return Fail(__LINE__);
            Row->SetNumberField(TEXT("rootPropertyIndex"),Index);Row->SetArrayField(TEXT("nodes"),Nodes);
        }
        Functions.Add(MakeShared<FJsonValueObject>(Row));
    }
    for(int32 Stage=0;Stage<4;Stage++)
    {
        if(Stage==1||Stage==3)Main->LinkAnimClassLayers(Provider);else if(Stage==2)Main->UnlinkAnimClassLayers(Provider);
        for(const auto* Property:Interface->GetLinkedAnimLayerNodeProperties())
        {
            auto* Node=Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);
            if((Node->GetTargetInstance<UAnimInstance>()==Main)!=(Stage==0||Stage==2))return Fail(__LINE__);
            auto Row=Evaluate(*Node,Proxy,FString(Stage==0?TEXT("initial:"):Stage==1?TEXT("linked:"):Stage==2?TEXT("unlinked:"):TEXT("relinked:"))+Node->Layer.ToString(),false,true);
            Row->SetBoolField(TEXT("self"),Stage==0||Stage==2);Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    // Bare, invalid-target calls cover zero, one and multiple pose pins, and
    // both normal/additive output contexts with cold/prefilled output data.
    for(int32 Inputs=0;Inputs<3;Inputs++)for(bool Additive:{false,true})for(bool Prefilled:{false,true})
    {
        FAnimNode_LinkedAnimLayer Node;Node.InputPoses.SetNum(Inputs);
        Cases.Add(MakeShared<FJsonValueObject>(Evaluate(Node,Proxy,FString::Printf(TEXT("unbound:%d:%d:%d"),Inputs,Additive,Prefilled),Additive,Prefilled)));
    }
    Component->UnregisterComponent();auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("functions"),Functions);Result->SetArrayField(TEXT("cases"),Cases);
    FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
