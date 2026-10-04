#include "AlsLyraLegIKLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Animation/AnimationPoseData.h"
#include "BoneControllers/AnimNode_LegIK.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"
namespace LyraLegIKProbe
{
struct FLegAccess:FAnimNode_SkeletalControlBase
{static void Cache(FAnimNode_LegIK& Node,const FBoneContainer& Bones){(Node.*&FLegAccess::InitializeBoneReferences)(Bones);}};
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_LEG_IK_FAILED line=%d"),Line);return {};}
TSharedPtr<FJsonValue> Vector(const FVector& V)
{return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{MakeShared<FJsonValueNumber>(V.X),MakeShared<FJsonValueNumber>(V.Y),MakeShared<FJsonValueNumber>(V.Z)});}
TSharedPtr<FJsonObject> History(const FAnimNode_LegIK& Node)
{
    auto R=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Legs;
    for(const auto& L:Node.LegsData)
    {
        auto V=MakeShared<FJsonObject>();const bool Initialized=L.IKChain.Links.Num()==3;
        V->SetField(TEXT("real"),Vector(Initialized?L.IKChain.Links[1].RealBendDir:FVector::ZeroVector));
        V->SetField(TEXT("base"),Vector(Initialized?L.IKChain.Links[1].BaseBendDir:FVector::ZeroVector));
        Legs.Add(MakeShared<FJsonValueObject>(V));
    }
    R->SetArrayField(TEXT("legs"),Legs);return R;
}
}
FString UAlsLyraLegIKLibrary::ReadTrace(USkeleton* Skeleton,const TArray<UClass*>& LayerClasses,
    const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraLegIKProbe;TSharedPtr<FJsonObject> Input;
    if(!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||LayerClasses.Num()!=3||Sequences.IsEmpty()||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Input))return Fail(__LINE__);
    for(auto* S:Sequences){if(!S||S->GetSkeleton()!=Skeleton||S->IsValidAdditive())return Fail(__LINE__);S->WaitOnExistingCompression(true);}
    const FMemMark Mark(FMemStack::Get());TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add(static_cast<FBoneIndexType>(I));
    FBoneContainer Container(Required,UE::Anim::FCurveFilterSettings(),*Skeleton);Container.SetUseRAWData(true);Container.SetDisableRetargeting(false);
    FAnimInstanceProxy Proxy;TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Input->GetArrayField(TEXT("traces")))
    {
        auto T=TV->AsObject();const int32 Profile=static_cast<int32>(T->GetNumberField(TEXT("provider")));if(Profile<0||Profile>=3)return Fail(__LINE__);
        auto* Class=LayerClasses[Profile];if(!Class)return Fail(__LINE__);FAnimNode_LegIK Node;int32 Count=0;
        for(TFieldIterator<FStructProperty> P(Class);P;++P)if(P->Struct==FAnimNode_LegIK::StaticStruct()){Node=*P->ContainerPtrToValuePtr<FAnimNode_LegIK>(Class->GetDefaultObject());++Count;}
        if(Count!=1||Node.LegsDefinition.Num()!=2||Node.SoftPercentLength!=1||Node.SoftAlpha!=1)return Fail(__LINE__);
        Node.MyAnimInstanceProxy=&Proxy;FLegAccess::Cache(Node,Container);
        TArray<TSharedPtr<FJsonValue>> Rows;
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();auto R=MakeShared<FJsonObject>();
            R->SetObjectField(TEXT("historyBefore"),History(Node));if(F->GetBoolField(TEXT("recache")))FLegAccess::Cache(Node,Container);
            FCompactPose Pose;Pose.SetBoneContainer(&Container);FBlendedCurve Curves;Curves.InitFrom(Container);UE::Anim::FStackAttributeContainer Attributes;
            FAnimationPoseData Data(Pose,Curves,Attributes);FDeltaTimeRecord Interval;Interval.Set(static_cast<float>(F->GetNumberField(TEXT("previous"))),static_cast<float>(F->GetNumberField(TEXT("sourceDelta"))));
            FAnimExtractContext Extract(F->GetNumberField(TEXT("time")),false,Interval,true);Extract.bExtractWithRootMotionProvider=true;
            Sequences[static_cast<int32>(F->GetNumberField(TEXT("asset")))]->GetAnimationPose(Data,Extract);
            FCSPose<FCompactPose> Adjust;Adjust.InitPose(Pose);
            for(int32 Limb=0;Limb<2;++Limb)
            {
                const auto& L=Node.LegsData[Limb];const auto Foot=L.FKLegBoneIndices[0],Knee=L.FKLegBoneIndices[1],Hip=L.FKLegBoneIndices[2];
                const FVector H=Adjust.GetComponentSpaceTransform(Hip).GetLocation(),K=Adjust.GetComponentSpaceTransform(Knee).GetLocation(),P=Adjust.GetComponentSpaceTransform(Foot).GetLocation();
                if(F->GetBoolField(TEXT("straight")))
                {
                    const FVector Direction=(P-H).GetSafeNormal();FTransform KT=Adjust.GetComponentSpaceTransform(Knee),FT=Adjust.GetComponentSpaceTransform(Foot);
                    KT.SetLocation(H+Direction*FVector::Dist(H,K));FT.SetLocation(H+Direction*(FVector::Dist(H,K)+FVector::Dist(K,P)));
                    Adjust.SetComponentSpaceTransform(Knee,KT);Adjust.SetComponentSpaceTransform(Foot,FT);
                }
                FTransform Target=Adjust.GetComponentSpaceTransform(L.IKFootBoneIndex);const int32 Mode=static_cast<int32>(F->GetNumberField(TEXT("mode")));
                if(Mode==1){const auto& Offset=F->GetArrayField(TEXT("offset"));Target.AddToTranslation(FVector(Offset[0]->AsNumber(),Offset[1]->AsNumber(),Offset[2]->AsNumber())*(Limb?-.7:1));}
                else if(Mode>=2)
                {
                    const FVector Direction=(P-H).GetSafeNormal();const double Length=FVector::Dist(H,K)+FVector::Dist(K,P);
                    Target.SetLocation(Mode==2?H+Direction*(Length*1.25):Mode==3?H+Direction*(Length*.999999):Mode==4?H:H-Direction*(Length*.65));
                }
                if(F->GetBoolField(TEXT("rotate")))Target.SetRotation(FQuat(FVector::UpVector,F->GetNumberField(TEXT("angle")))*Target.GetRotation());
                Adjust.SetComponentSpaceTransform(L.IKFootBoneIndex,Target);
            }
            FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Adjust,Pose);
            R->SetObjectField(TEXT("input"),LyraCyclePoseProbe::PoseData(Pose,Curves,Attributes,Skeleton->GetReferenceSkeleton()));
            FComponentSpacePoseContext Output(&Proxy);Output.Pose.InitPose(Pose);Output.Curve.CopyFrom(Curves);Output.CustomAttributes.CopyFrom(Attributes);
            const float Alpha=static_cast<float>(F->GetNumberField(TEXT("alpha")));if(!Node.IsValidToEvaluate(Skeleton,Container))return Fail(__LINE__);
            TArray<FBoneTransform> Changes;if(Alpha>ZERO_ANIMWEIGHT_THRESH){Node.EvaluateSkeletalControl_AnyThread(Output,Changes);if(!Changes.IsEmpty())Output.Pose.LocalBlendCSBoneTransforms(Changes,Alpha);}
            FCompactPose After=Output.Pose.GetPose();FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose,After);
            R->SetNumberField(TEXT("changedBones"),Changes.Num());R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(After,Output.Curve,Output.CustomAttributes,Skeleton->GetReferenceSkeleton()));
            R->SetObjectField(TEXT("history"),History(Node));Rows.Add(MakeShared<FJsonValueObject>(R));
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Rows);Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Json));return Json;
}
