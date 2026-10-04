#include "AlsLyraMainCachePoseLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimRootMotionProvider.h"
#include "Animation/Skeleton.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_UseCachedPose.h"
#include "AnimNodes/AnimNode_ApplyAdditive.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "AnimNodes/AnimNode_RotateRootBone.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraMainCachePoseProbe
{
FString Failure(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_CACHE_POSE_FAILED line=%d"),Line);return {};}
// Diagnostic-only access on this transient proxy, as in the Aiming probe.
// Restore the original root before the transient instance is destroyed.
template<class Tag,typename Tag::Type Member>struct TOracleRoot{friend typename Tag::Type Access(Tag){return Member;}};
struct FRootMember{using Type=FAnimNode_Base* FAnimInstanceProxy::*;friend Type Access(FRootMember);};
template struct TOracleRoot<FRootMember,&FAnimInstanceProxy::RootNode>;
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
struct FProxyAccess:FAnimInstanceProxy
{
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        (P.*&FProxyAccess::InitializeObjects)(A);
        TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
        (P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void AdvanceEvaluation(FAnimInstanceProxy& P) {(P.*&FProxyAccess::EvaluationCounter).Increment();}
    static void SetEvaluation(FAnimInstanceProxy& P,const FGraphTraversalCounter& Counter) {(P.*&FProxyAccess::EvaluationCounter)=Counter;}
    static void SetRoot(FAnimInstanceProxy& P,FAnimNode_Base* Root) {(P.*Access(FRootMember{}))=Root;}
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    {(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
};

struct FCachePoseLeaf:FAnimNode_Base
{
    UAnimSequence* Sequence=nullptr;TSharedPtr<FJsonObject> Input;int32 Evaluations=0;
    void Evaluate_AnyThread(FPoseContext& O) override
    {
        ++Evaluations;FDeltaTimeRecord D;D.Set(static_cast<float>(Input->GetNumberField(TEXT("previous"))),static_cast<float>(Input->GetNumberField(TEXT("sourceDelta"))));
        FAnimationPoseData Data(O);FAnimExtractContext X(Input->GetNumberField(TEXT("time")),false,D,true);X.bExtractWithRootMotionProvider=true;
        Sequence->GetAnimationPose(Data,X);O.Curve.Set(TEXT("Distance"),static_cast<float>(Input->GetNumberField(TEXT("distance"))));
        O.Curve.SetFlags(TEXT("Distance"),static_cast<UE::Anim::ECurveElementFlags>(static_cast<uint32>(Input->GetNumberField(TEXT("flags")))));
    }
};
struct FCachePosePair:FAnimNode_Base
{
    FPoseLink First,Second;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{First.Initialize(C);Second.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{First.CacheBones(C);Second.CacheBones(C);}
    void Evaluate_AnyThread(FPoseContext& O) override{First.Evaluate(O);FPoseContext Other(O);Second.Evaluate(Other);}
};
// This diagnostic root runs inside Engine's ParallelEvaluateAnimation, which
// owns the real FCachedPoseScope and stack lifetime. No local scope substitute.
struct FCachePoseDriver:FAnimNode_Base
{
    FPoseLink First,Second;FCachePoseLeaf* Leaf=nullptr;USkeleton* Skeleton=nullptr;
    TSharedPtr<FJsonObject> Input,OtherInput;TArray<TSharedPtr<FJsonValue>>* Outputs=nullptr;
    FGraphTraversalCounter Counter;bool Valid=true;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{First.Initialize(C);Second.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{First.CacheBones(C);Second.CacheBones(C);}
    void Evaluate_AnyThread(FPoseContext& O) override
    {
        // Hold the traversal counter at this controlled boundary so that two
        // Engine evaluate lifetimes prove invalidation independently of it.
        FProxyAccess::SetEvaluation(*O.AnimInstanceProxy,Counter);
        Leaf->Input=Input;First.Evaluate(O);
        auto Data=LyraCyclePoseProbe::PoseData(O.Pose,O.Curve,O.CustomAttributes,Skeleton->GetReferenceSkeleton());
        if(!Data){Valid=false;return;}Outputs->Add(MakeShared<FJsonValueObject>(Data));
        O.Pose[FCompactPoseBoneIndex(0)].AddToTranslation(FVector(100,-200,300));O.Curve.Set(TEXT("Distance"),-777);
        Leaf->Input=OtherInput;FPoseContext Other(O);Second.Evaluate(Other);
        Data=LyraCyclePoseProbe::PoseData(Other.Pose,Other.Curve,Other.CustomAttributes,Skeleton->GetReferenceSkeleton());
        if(!Data){Valid=false;return;}Outputs->Add(MakeShared<FJsonValueObject>(Data));
        O.Pose.CopyBonesFrom(Other.Pose);O.Curve.CopyFrom(Other.Curve);O.CustomAttributes.CopyFrom(Other.CustomAttributes);
    }
};
template<class T>T* Node(const IAnimClassInterface* I,UAnimInstance* A,int32 Index)
{const auto& Props=I->GetAnimNodeProperties();auto* P=Props[Props.Num()-1-Index];return P->Struct==T::StaticStruct()?P->ContainerPtrToValuePtr<T>(A):nullptr;}
}
FString UAlsLyraMainCachePoseLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraMainCachePoseProbe;TSharedPtr<FJsonObject> Requests;const auto* MI=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;
    if(!MI||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return Failure(__LINE__);
    for(auto* S:Sequences){if(!S||S->GetSkeleton()!=Skeleton)return Failure(__LINE__);S->WaitOnExistingCompression(true);}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Requests->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto Trace=TV->AsObject();
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Failure(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* Owner=W->SpawnActor<ACharacter>(Spawn);if(!Owner)return Failure(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MainClass);C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();
        auto* Main=C->GetAnimInstance();if(!Main)return Failure(__LINE__);auto& P=FInstanceAccess::Proxy(Main);
        Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(P,Main,Skeleton);

        auto* PC=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));const auto* Interface=PC?IAnimClassInterface::GetFromClass(PC):nullptr;if(!Interface)return Failure(__LINE__);
        TStrongObjectPtr<UAnimInstance> Provider(NewObject<UAnimInstance>(C.Get(),PC,NAME_None,RF_Transient));
        auto* Loc=Node<FAnimNode_SaveCachedPose>(MI,Main,83);auto* Split=Node<FAnimNode_SaveCachedPose>(MI,Main,78);
        auto* Input=Node<FAnimNode_SaveCachedPose>(Interface,Provider.Get(),78);
        auto* Upper=Node<FAnimNode_UseCachedPose>(MI,Main,82);auto* Base=Node<FAnimNode_UseCachedPose>(MI,Main,80);auto* Pre=Node<FAnimNode_UseCachedPose>(MI,Main,77);
        auto* A=Node<FAnimNode_UseCachedPose>(Interface,Provider.Get(),76);auto* B=Node<FAnimNode_UseCachedPose>(Interface,Provider.Get(),75);
        if(!Loc||!Split||!Input||!Upper||!Base||!Pre||!A||!B)return Failure(__LINE__);
        FCachePoseLeaf Leaf;FCachePosePair Pair;Pair.First.SetLinkNode(Upper);Pair.Second.SetLinkNode(Base);
        Loc->Pose.SetLinkNode(&Leaf);Upper->LinkToCachingNode.SetLinkNode(Loc);Base->LinkToCachingNode.SetLinkNode(Loc);
        Split->Pose.SetLinkNode(&Pair);Pre->LinkToCachingNode.SetLinkNode(Split);Input->Pose.SetLinkNode(Pre);A->LinkToCachingNode.SetLinkNode(Input);B->LinkToCachingNode.SetLinkNode(Input);
        FCachePoseDriver Driver;Driver.First.SetLinkNode(A);Driver.Second.SetLinkNode(B);Driver.Leaf=&Leaf;Driver.Skeleton=Skeleton;
        Driver.Initialize_AnyThread(FAnimationInitializeContext(&P));Driver.CacheBones_AnyThread(FAnimationCacheBonesContext(&P));
        struct FRestoreRoot{FAnimInstanceProxy& Proxy;FAnimNode_Base* Old;~FRestoreRoot(){FProxyAccess::SetRoot(Proxy,Old);}}Restore{P,P.GetRootNode()};
        FProxyAccess::SetRoot(P,&Driver);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:Trace->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();auto R=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Outputs;
            Leaf.Evaluations=0;int32 Asset=F->GetNumberField(TEXT("asset"));if(!Sequences.IsValidIndex(Asset))return Failure(__LINE__);Leaf.Sequence=Sequences[Asset];
            FProxyAccess::AdvanceEvaluation(P);Driver.Counter=P.GetEvaluationCounter();
            if(F->GetBoolField(TEXT("evaluate")))
            {
                for(int32 Pass=0;Pass<2;++Pass)
                {
                    // A new lifetime scope invalidates the saved data even
                    // when the graph evaluation counter stays unchanged.
                    Driver.Input=F->GetArrayField(TEXT("inputs"))[Pass]->AsObject();
                    Driver.OtherInput=F->GetArrayField(TEXT("inputs"))[1-Pass]->AsObject();Driver.Outputs=&Outputs;
                    FCompactPose Pose;FBlendedHeapCurve Curves;UE::Anim::FHeapAttributeContainer Attributes;
                    FParallelEvaluationData Evaluation{Curves,Pose,Attributes};
                    Main->ParallelEvaluateAnimation(false,Carrier.Get(),Evaluation);
                    if(!Driver.Valid||!P.GetEvaluationCounter().IsSynchronized_Counter(Driver.Counter))return Failure(__LINE__);
                }
            }
            R->SetNumberField(TEXT("evaluations"),Leaf.Evaluations);R->SetArrayField(TEXT("outputs"),Outputs);Frames.Add(MakeShared<FJsonValueObject>(R));
        }
        auto T=MakeShared<FJsonObject>();T->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));T->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));T->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(T));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
