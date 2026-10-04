#include "AlsLyraLeftHandLayerLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_LinkedInputPose.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimRootMotionProvider.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendProfile.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

namespace LyraLeftHandProbe
{
FString Failure(int32 Line) { UE_LOG(LogTemp, Error, TEXT("LYRA_LEFT_HAND_LAYER_FAILED line=%d"), Line); return {}; }
struct FInstanceAccess : UAnimInstance
{ static FAnimInstanceProxy& Proxy(UAnimInstance* A) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A); } };
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* A,float D)
    { (P.*&FProxyAccess::UpdateCounter).Increment(); (P.*&FProxyAccess::PreUpdate)(A,D); }
    static void Setup(FAnimInstanceProxy& P,UAnimInstance* A,USkeleton* S)
    {
        // Refresh the cached skeleton before CacheBones, not just before the
        // first Update. RequiredBones alone does not refresh GetSkeleton().
        (P.*&FProxyAccess::InitializeObjects)(A);
        TArray<FBoneIndexType> Required; for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
        P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*S);
        P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);
        (P.*&FProxyAccess::CachedBonesCounter).Increment();
    }
    static void Publish(FAnimInstanceProxy& P,const TSharedPtr<FJsonObject>& Values)
    {
        using FRead=TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType);
        auto& C=(P.*static_cast<FRead>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);C.Reset();
        for(const auto& V:Values->Values)C.Add(FName(*V.Key),static_cast<float>(V.Value->AsNumber()));
    }
};
struct FBlendAccess : FAnimNode_LayeredBoneBlend
{
    static TSharedPtr<FJsonObject> CurveBindings(FAnimNode_LayeredBoneBlend& Node)
    {
        const auto Result=MakeShared<FJsonObject>();
        (Node.*&FBlendAccess::CurvePoseSourceIndices).ForEachElement([&](const auto& E){Result->SetNumberField(E.Name.ToString(),E.Index);});
        return Result;
    }
};
struct FInputLeaf : FAnimNode_Base
{
    UAnimSequence* Sequence=nullptr;float Time=0,Previous=0,Delta=0;uint32 Flags=0;int32 Updates=0;float UpdateWeight=0;
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    { ++Updates;UpdateWeight=Context.GetFinalBlendWeight(); }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        FDeltaTimeRecord Interval;Interval.Set(Previous,Delta);
        FAnimationPoseData Data(Output);FAnimExtractContext Extract(static_cast<double>(Time),false,Interval,true);
        Extract.bExtractWithRootMotionProvider=true;Sequence->GetAnimationPose(Data,Extract);
        Output.Curve.Set(TEXT("Distance"),17.f+Time*.25f);
        Output.Curve.SetFlags(TEXT("Distance"),static_cast<UE::Anim::ECurveElementFlags>(Flags));
    }
};
}

FString UAlsLyraLeftHandLayerLibrary::ReadTrace(UClass* MainClass,USkeletalMesh* Mesh,USkeleton* Skeleton,
    const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraLeftHandProbe;
    if(!MainClass || !Mesh || !Skeleton || Skeleton->GetReferenceSkeleton().GetNum()!=81 || Sequences.IsEmpty())return Failure(__LINE__);
    TSharedPtr<FJsonObject> Input;if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Failure(__LINE__);
    for(auto* S:Sequences)if(!S || S->GetSkeleton()!=Skeleton || S->IsValidAdditive())return Failure(__LINE__);
    if(!UE::Anim::IAnimRootMotionProvider::Get())return Failure(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TraceValue:Input->GetArrayField(TEXT("traces")))
    {
        const FMemMark Mark(FMemStack::Get());const auto Trace=TraceValue->AsObject();
        auto* LC=LoadObject<UClass>(nullptr,*Trace->GetStringField(TEXT("class")));
        auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Failure(__LINE__);
        const auto* Function=IAnimClassInterface::FindAnimBlueprintFunction(LI,TEXT("LeftHandPose_OverrideState"));if(!Function)return Failure(__LINE__);
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Failure(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}}Cleanup{World.Get()};
        FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;auto* Owner=World->SpawnActor<ACharacter>(Spawn);if(!Owner)return Failure(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        Component->SetSkeletalMesh(Carrier.Get());Component->SetAnimInstanceClass(MainClass);Component->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(Component.Get());Component->RegisterComponent();
        auto* Main=Component->GetAnimInstance();if(!Main)return Failure(__LINE__);Main->LinkAnimClassLayers(LC);
        auto* Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));if(!Layer || Layer->GetClass()!=LC)return Failure(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto& LP=FInstanceAccess::Proxy(Layer);
        Carrier->SetSkeleton(Skeleton);FProxyAccess::Setup(MP,Main,Skeleton);FProxyAccess::Setup(LP,Layer,Skeleton);
        if(MP.GetSkeleton()!=Skeleton || LP.GetSkeleton()!=Skeleton)return Failure(__LINE__);
        const auto& Properties=LI->GetAnimNodeProperties();
        // These are original compiled indices, independently checked by the
        // Python graph manifest. Property order runs in the opposite direction.
        if(Properties.Num()!=118 || Properties.IndexOfByKey(Function->OutputPoseNodeProperty)!=0)return Failure(__LINE__);
        auto* Root=Properties[0]->ContainerPtrToValuePtr<FAnimNode_Base>(Layer);
        auto* Blend=Properties[2]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(Layer);
        auto* InputPose=Properties[1]->ContainerPtrToValuePtr<FAnimNode_LinkedInputPose>(Layer);
        auto* Child=Properties[3]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluator>(Layer);
        auto* Enabled=FindFProperty<FBoolProperty>(LC,TEXT("EnableLeftHandPoseOverride"));
        auto* Weight=FindFProperty<FDoubleProperty>(LC,TEXT("LeftHandPoseOverrideWeight"));
        auto* Asset=FindFProperty<FObjectPropertyBase>(LC,TEXT("LeftHandPose_Override"));
        if(!Enabled || !Weight || !Asset || Enabled->GetPropertyValue_InContainer(Layer) || Asset->GetObjectPropertyValue_InContainer(Layer))return Failure(__LINE__);
        if(Blend->bMeshSpaceRotationBlend || Blend->bRootSpaceRotationBlend || Blend->bMeshSpaceScaleBlend || Blend->bUpdateBasePoseFirst ||
            !Blend->bBlendRootMotionBasedOnRootBone || Blend->CurveBlendOption!=ECurveBlendOption::Override || Blend->BlendMasks.Num()!=1)return Failure(__LINE__);
        TStrongObjectPtr<UBlendProfile> Mask(NewObject<UBlendProfile>(GetTransientPackage()));Mask->OwningSkeleton=Skeleton;Mask->Mode=EBlendProfileMode::BlendMask;
        TArray<TSharedPtr<FJsonValue>> Weights;
        for(int32 B=0;B<81;++B)
        {float W=0;for(const auto& E:Blend->BlendMasks[0]->ProfileEntries)if(E.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(B)){W=E.BlendScale;break;}
         Mask->SetBoneBlendScale(B,W,false,true);Weights.Add(MakeShared<FJsonValueNumber>(W));}
        Blend->BlendMasks[0]=Mask.Get();Blend->InvalidatePerBoneBlendWeights();
        FInputLeaf Source;FPoseLink SourceLink;SourceLink.SetLinkNode(&Source);SourceLink.Initialize(FAnimationInitializeContext(&MP));SourceLink.CacheBones(FAnimationCacheBonesContext(&MP));
        InputPose->DynamicUnlink();InputPose->DynamicLink(&MP,&SourceLink,1);
        FPoseLink Link;Link.SetLinkNode(Root);Link.Initialize(FAnimationInitializeContext(&LP));Link.CacheBones(FAnimationCacheBonesContext(&LP));
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FrameValue:Trace->GetArrayField(TEXT("frames")))
        {
            const auto Frame=FrameValue->AsObject();const float D=Frame->GetNumberField(TEXT("delta"));
            FProxyAccess::Pre(MP,Main,D);FProxyAccess::Pre(LP,Layer,D);
            if(Frame->GetBoolField(TEXT("initialize")))Link.Initialize(FAnimationInitializeContext(&LP));
            const auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("feedbackBefore"),Layer->GetCurveValue(TEXT("DisableLeftHandPoseOverride")));
            Row->SetNumberField(TEXT("weightBefore"),Weight->GetPropertyValue_InContainer(Layer));
            Source.Updates=0;Source.UpdateWeight=0;Source.Sequence=Sequences[static_cast<int32>(Frame->GetNumberField(TEXT("asset")))];
            Source.Time=Frame->GetNumberField(TEXT("time"));Source.Previous=Frame->GetNumberField(TEXT("previous"));Source.Delta=Frame->GetNumberField(TEXT("sourceDelta"));Source.Flags=static_cast<uint32>(Frame->GetNumberField(TEXT("flags")));
            const bool Active=Frame->GetBoolField(TEXT("active"));
            if(Active)
            {
                Link.Update(FAnimationUpdateContext(&LP,D).FractionalWeight(static_cast<float>(Frame->GetNumberField(TEXT("weight")))));
                if(Frame->GetBoolField(TEXT("evaluate")))
                {
                    FPoseContext Base(&MP);SourceLink.Evaluate(Base);
                    FPoseContext Output(&LP);Link.Evaluate(Output);
                    const auto BaseData=LyraCyclePoseProbe::PoseData(Base.Pose,Base.Curve,Base.CustomAttributes,Skeleton->GetReferenceSkeleton());
                    const auto OutputData=LyraCyclePoseProbe::PoseData(Output.Pose,Output.Curve,Output.CustomAttributes,Skeleton->GetReferenceSkeleton());
                    if(!BaseData || !OutputData)return Failure(__LINE__);Row->SetObjectField(TEXT("input"),BaseData);Row->SetObjectField(TEXT("output"),OutputData);
                    FProxyAccess::Publish(MP,Frame->GetObjectField(TEXT("finalFeedback")));Layer->CopyCurveValues(*Main);
                }
            }
            Row->SetNumberField(TEXT("weight"),Weight->GetPropertyValue_InContainer(Layer));Row->SetNumberField(TEXT("blendWeight"),Blend->BlendWeights[0]);
            Row->SetNumberField(TEXT("feedbackAfter"),Layer->GetCurveValue(TEXT("DisableLeftHandPoseOverride")));
            Row->SetNumberField(TEXT("inputUpdates"),Source.Updates);Row->SetNumberField(TEXT("inputWeight"),Source.UpdateWeight);
            Row->SetBoolField(TEXT("childAssetNull"),Child->GetSequence()==nullptr);Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Out=MakeShared<FJsonObject>();Out->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));Out->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));
        Out->SetArrayField(TEXT("mask"),Weights);Out->SetObjectField(TEXT("curveBindings"),FBlendAccess::CurveBindings(*Blend));Out->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(Out));
        InputPose->DynamicUnlink();
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);FString Text;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text));return Text;
}
