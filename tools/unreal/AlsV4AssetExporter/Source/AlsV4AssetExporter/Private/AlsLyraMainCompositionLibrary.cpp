#include "AlsLyraMainCompositionLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimRootMotionProvider.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendProfile.h"
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

namespace LyraMainCompositionProbe
{
FString Failure(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_MAIN_COMPOSITION_FAILED line=%d"),Line);return {};}
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
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    {(P.*&FProxyAccess::UpdateCounter).Increment();(P.*&FProxyAccess::PreUpdate)(A,D);}
};
struct FBlendAccess:FAnimNode_LayeredBoneBlend
{
    static TSharedPtr<FJsonObject> Bindings(FAnimNode_LayeredBoneBlend& N)
    {auto R=MakeShared<FJsonObject>();(N.*&FBlendAccess::CurvePoseSourceIndices).ForEachElement([&](const auto& E){R->SetNumberField(E.Name.ToString(),E.Index);});return R;}
};
struct FCompositionLeaf:FAnimNode_Base
{
    int32 Id=0;UAnimSequence* Sequence=nullptr;TSharedPtr<FJsonObject> Frame;TArray<TSharedPtr<FJsonValue>>* Updates=nullptr;
    void Update_AnyThread(const FAnimationUpdateContext& C) override
    {auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("leaf"),Id);R->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());Updates->Add(MakeShared<FJsonValueObject>(R));}
    void Evaluate_AnyThread(FPoseContext& O) override
    {
        FDeltaTimeRecord D;D.Set(static_cast<float>(Frame->GetNumberField(TEXT("previous"))),static_cast<float>(Frame->GetNumberField(TEXT("sourceDelta"))));
        FAnimationPoseData Data(O);FAnimExtractContext X(Frame->GetNumberField(TEXT("time")),false,D,true);X.bExtractWithRootMotionProvider=true;
        Sequence->GetAnimationPose(Data,X);
        O.Curve.Set(TEXT("Distance"),static_cast<float>(Frame->GetNumberField(TEXT("distance"))));
        O.Curve.SetFlags(TEXT("Distance"),static_cast<UE::Anim::ECurveElementFlags>(static_cast<uint32>(Frame->GetNumberField(TEXT("flags")))));
    }
};
template<class T>T* Node(const IAnimClassInterface* I,UAnimInstance* A,int32 Compiled)
{auto* P=I->GetAnimNodeProperties()[I->GetAnimNodeProperties().Num()-1-Compiled];return P->Struct==T::StaticStruct()?P->ContainerPtrToValuePtr<T>(A):nullptr;}
}

FString UAlsLyraMainCompositionLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraMainCompositionProbe;
    const auto* MI=MainClass?IAnimClassInterface::GetFromClass(MainClass):nullptr;TSharedPtr<FJsonObject> Input;
    if(!MI||!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Failure(__LINE__);
    for(auto* S:Sequences){if(!S||S->GetSkeleton()!=Skeleton)return Failure(__LINE__);S->WaitOnExistingCompression(true);}
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
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
        auto* Split=Node<FAnimNode_LayeredBoneBlend>(MI,Main,0);auto* Dynamic=Node<FAnimNode_ApplyAdditive>(MI,Main,3);
        auto* Recovery=Node<FAnimNode_ApplyAdditive>(MI,Main,76);auto* Root=Node<FAnimNode_RotateRootBone>(MI,Main,72);
        auto* Weight=FindFProperty<FDoubleProperty>(MainClass,TEXT("UpperbodyDynamicAdditiveWeight"));auto* Yaw=FindFProperty<FDoubleProperty>(MainClass,TEXT("RootYawOffset"));
        if(!Split||!Dynamic||!Recovery||!Root||!Weight||!Yaw||Split->BlendMasks.Num()!=1||!Split->BlendMasks[0]||!Split->bMeshSpaceRotationBlend||Split->bRootSpaceRotationBlend||Split->bMeshSpaceScaleBlend||Split->bUpdateBasePoseFirst||!Split->bBlendRootMotionBasedOnRootBone||Root->bRotateRootMotionAttribute)return Failure(__LINE__);
        TArray<TSharedPtr<FJsonValue>> Weights,SourceWeights;
        for(const auto& E:Split->BlendMasks[0]->ProfileEntries){auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("bone"),E.BoneReference.BoneName.ToString());R->SetNumberField(TEXT("scale"),E.BlendScale);SourceWeights.Add(MakeShared<FJsonValueObject>(R));}
        TStrongObjectPtr<UBlendProfile> Mask(NewObject<UBlendProfile>(GetTransientPackage()));Mask->OwningSkeleton=Skeleton;Mask->Mode=EBlendProfileMode::BlendMask;
        for(int32 B=0;B<81;++B){float V=0;for(const auto& E:Split->BlendMasks[0]->ProfileEntries)if(E.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(B)){V=E.BlendScale;break;}Mask->SetBoneBlendScale(B,V,false,true);Weights.Add(MakeShared<FJsonValueNumber>(V));}
        Split->BlendMasks[0]=Mask.Get();Split->InvalidatePerBoneBlendWeights();
        FCompositionLeaf Leaves[5];TArray<TSharedPtr<FJsonValue>> Updates;for(int32 I=0;I<5;++I){Leaves[I].Id=I;Leaves[I].Updates=&Updates;}
        // Explicit operator boundaries: source leaves stand in for evaluated
        // Slot/Aiming outputs. Original node links/handlers themselves execute.
        Dynamic->Base.SetLinkNode(&Leaves[0]);Dynamic->Additive.SetLinkNode(&Leaves[2]);
        Split->BasePose.SetLinkNode(Dynamic);Split->BlendPoses[0].SetLinkNode(&Leaves[1]);
        Recovery->Base.SetLinkNode(&Leaves[4]);Recovery->Additive.SetLinkNode(&Leaves[3]);Root->BasePose.SetLinkNode(Recovery);
        FPoseLink Upper,Final;Upper.SetLinkNode(Split);Final.SetLinkNode(Root);
        Upper.Initialize(FAnimationInitializeContext(&P));Final.Initialize(FAnimationInitializeContext(&P));Upper.CacheBones(FAnimationCacheBonesContext(&P));Final.CacheBones(FAnimationCacheBonesContext(&P));
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:Trace->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();float D=F->GetNumberField(TEXT("delta"));FProxyAccess::Pre(P,Main,D);
            Weight->SetPropertyValue_InContainer(Main,F->GetNumberField(TEXT("dynamicWeight")));Yaw->SetPropertyValue_InContainer(Main,F->GetNumberField(TEXT("rootYaw")));
            if(F->GetBoolField(TEXT("initialize"))){Upper.Initialize(FAnimationInitializeContext(&P));Final.Initialize(FAnimationInitializeContext(&P));}
            const auto& Inputs=F->GetArrayField(TEXT("leaves"));if(Inputs.Num()!=5)return Failure(__LINE__);
            for(int32 I=0;I<5;++I){Leaves[I].Frame=Inputs[I]->AsObject();int32 A=Leaves[I].Frame->GetNumberField(TEXT("asset"));if(!Sequences.IsValidIndex(A))return Failure(__LINE__);Leaves[I].Sequence=Sequences[A];if(Leaves[I].Sequence->IsValidAdditive()!=(I==2||I==3))return Failure(__LINE__);}
            Updates.Reset();auto R=MakeShared<FJsonObject>();
            if(F->GetBoolField(TEXT("visited")))
            {
                FAnimationUpdateContext U(&P,D);U=U.FractionalWeight(static_cast<float>(F->GetNumberField(TEXT("weight"))));Upper.Update(U);Final.Update(U);
                if(F->GetBoolField(TEXT("evaluate")))
                {
                    FPoseContext A(&P),B(&P);Upper.Evaluate(A);Final.Evaluate(B);
                    auto AO=LyraCyclePoseProbe::PoseData(A.Pose,A.Curve,A.CustomAttributes,Skeleton->GetReferenceSkeleton());auto BO=LyraCyclePoseProbe::PoseData(B.Pose,B.Curve,B.CustomAttributes,Skeleton->GetReferenceSkeleton());
                    if(!AO||!BO)return Failure(__LINE__);R->SetObjectField(TEXT("upper"),AO);R->SetObjectField(TEXT("final"),BO);
                }
            }
            R->SetNumberField(TEXT("dynamicAlpha"),Dynamic->ActualAlpha);R->SetNumberField(TEXT("recoveryAlpha"),Recovery->ActualAlpha);
            R->SetNumberField(TEXT("splitWeight"),Split->BlendWeights[0]);R->SetNumberField(TEXT("pitch"),Root->Pitch);R->SetNumberField(TEXT("yaw"),Root->Yaw);R->SetArrayField(TEXT("updates"),Updates);Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        auto T=MakeShared<FJsonObject>();T->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));T->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));T->SetArrayField(TEXT("mask"),Weights);T->SetArrayField(TEXT("sourceMask"),SourceWeights);T->SetObjectField(TEXT("curveBindings"),FBlendAccess::Bindings(*Split));T->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(T));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
