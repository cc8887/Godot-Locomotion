#include "AlsMeshSpaceBlendCommandlet.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/AttributeTypes.h"
#include "Animation/Skeleton.h"
#include "AnimationRuntime.h"
#include "BoneContainer.h"
#include "BonePose.h"
#include "Dom/JsonObject.h"
#include "HAL/IConsoleManager.h"
#include "Misc/FileHelper.h"
#include "Misc/MemStack.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}

TArray<TSharedPtr<FJsonValue>> PoseJson(const FCompactPose& Pose)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const FCompactPoseBoneIndex Bone : Pose.ForEachBoneIndex())
    {
        const FTransform& Transform = Pose[Bone];
        const FVector P = Transform.GetTranslation();
        const FQuat R = Transform.GetRotation();
        const FVector S = Transform.GetScale3D();
        const auto Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
        Row->SetArrayField(TEXT("rotation"), Numbers({R.X, R.Y, R.Z, R.W}));
        Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z}));
        Result.Add(MakeShared<FJsonValueObject>(Row));
    }
    return Result;
}

int32 ExportComponentScale(const FString& OutputPath, const USkeleton* Skeleton, const FBoneContainer& Container)
{
    FCompactPose Base, Output;
    Base.SetBoneContainer(&Container); Output.SetBoneContainer(&Container);
    Base.ResetToRefPose(); Output.ResetToRefPose();
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("ALS component scale body + FCSPose::LocalBlendCSBoneTransforms; synthetic ALS poses, not an AnimBP trace"));
    TArray<TSharedPtr<FJsonValue>> Parents, Names, Cases;
    const auto& Reference = Skeleton->GetReferenceSkeleton();
    for (const auto Bone : Base.ForEachBoneIndex())
    {
        Parents.Add(MakeShared<FJsonValueNumber>(Container.GetParentBoneIndex(Bone).GetInt()));
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Container.GetSkeletonIndex(Bone)).ToString()));
    }
    Root->SetArrayField(TEXT("parents"), Parents); Root->SetArrayField(TEXT("names"), Names);
    const FVector Scale(1.4f, 1.4f, 1.f);
    Root->SetArrayField(TEXT("scale"), Numbers({Scale.X, Scale.Y, Scale.Z}));
    for (int32 Pattern = 0; Pattern < 3; ++Pattern)
    {
        Base.ResetToRefPose();
        if (Pattern > 0)
        {
            for (const auto Bone : Base.ForEachBoneIndex())
            {
                const int32 Index = Bone.GetInt();
                FQuat Rotation = FRotator(Index * 3 + 17, Index * 7 - 23, Index * 2 + 11).Quaternion();
                if (Index % 2) Rotation *= -1.0;
                const FVector BoneScale(Pattern == 2 && Index % 5 == 0 ? 0 : 1.05, .9, 1.1);
                Base[Bone] = FTransform(Rotation, FVector(Index * .7 + 1, Index * -.4, Index * .6), BoneScale);
            }
        }
        for (const TCHAR* Name : {TEXT("ik_foot_root"), TEXT("spine_02"), TEXT("root")})
        {
            const FCompactPoseBoneIndex Bone = Container.GetCompactPoseIndexFromSkeletonPoseIndex(
                FSkeletonPoseBoneIndex(Reference.FindBoneIndex(Name)));
            if (Bone.GetInt() < 0) return 10;
            for (float InputAlpha : {-1.f, 0.f, .000005f, .00001f, .000011f, .25f, .5f, .75f, .999989f, .99999f, 1.f, 2.f})
            {
                FCSPose<FCompactPose> Component;
                Component.InitPose(Base);
                const float Alpha = FMath::Clamp(InputAlpha, 0.f, 1.f);
                if (FAnimWeight::IsRelevant(Alpha))
                {
                    FTransform Transform = Component.GetComponentSpaceTransform(Bone);
                    Transform.SetScale3D(Transform.GetScale3D() * Scale);
                    const FBoneTransform Change(Bone, Transform);
                    Component.LocalBlendCSBoneTransforms(MakeArrayView(&Change, 1), Alpha);
                }
                FCSPose<FCompactPose>::ConvertComponentPosesToLocalPoses(Component, Output);
                const auto Row = MakeShared<FJsonObject>();
                Row->SetNumberField(TEXT("pattern"), Pattern); Row->SetNumberField(TEXT("bone"), Bone.GetInt());
                Row->SetNumberField(TEXT("alpha"), InputAlpha); Row->SetArrayField(TEXT("input"), PoseJson(Base));
                Row->SetArrayField(TEXT("output"), PoseJson(Output)); Cases.Add(MakeShared<FJsonValueObject>(Row));
            }
        }
    }
    Root->SetArrayField(TEXT("cases"), Cases);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 11;
    UE_LOG(LogTemp, Display, TEXT("ALS_COMPONENT_SCALE_OK cases=%d bones=%d assets_saved=0"), Cases.Num(), Base.GetNumBones());
    return 0;
}
}

UAlsMeshSpaceBlendCommandlet::UAlsMeshSpaceBlendCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

int32 UAlsMeshSpaceBlendCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const auto Sequence = LoadObject<UAnimSequence>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F"));
    if (!Sequence || !Sequence->GetSkeleton()) return 2;
    const auto Skeleton = Sequence->GetSkeleton();
    const auto& Reference = Skeleton->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(Bone);
    const FMemMark Mark(FMemStack::Get());
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    if (FParse::Param(*Params, TEXT("ComponentScale"))) return ExportComponentScale(OutputPath, Skeleton, Container);
    FCompactPose Base;
    FCompactPose Target;
    FCompactPose Output;
    Base.SetBoneContainer(&Container);
    Target.SetBoneContainer(&Container);
    Output.SetBoneContainer(&Container);
    Base.ResetToRefPose();
    Target.ResetToRefPose();
    Output.ResetToRefPose();
    FBlendedCurve BaseCurve;
    FBlendedCurve TargetCurve;
    FBlendedCurve OutputCurve;
    UE::Anim::FStackAttributeContainer BaseAttributes;
    UE::Anim::FStackAttributeContainer TargetAttributes;
    UE::Anim::FStackAttributeContainer OutputAttributes;
    FAnimationPoseData OutputData(Output, OutputCurve, OutputAttributes);
    TArray<FPerBoneBlendWeight> Weights;
    Weights.SetNumZeroed(Base.GetNumBones());

    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("FAnimationRuntime::BlendPosesPerBoneFilter; synthetic local poses on the ALS skeleton, not an AnimBP trace"));
    TArray<TSharedPtr<FJsonValue>> Parents;
    TArray<TSharedPtr<FJsonValue>> Names;
    for (const FCompactPoseBoneIndex Bone : Base.ForEachBoneIndex())
    {
        Parents.Add(MakeShared<FJsonValueNumber>(Container.GetParentBoneIndex(Bone).GetInt()));
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Container.GetSkeletonIndex(Bone)).ToString()));
        for (int32 Layer = 0; Layer < 2; ++Layer)
        {
            const int32 Index = Bone.GetInt();
            FQuat R = FRotator(Index * 13 + Layer * 57, Index * 29 - Layer * 71, Index * 7 + Layer * 31).Quaternion();
            if ((Index + Layer) % 2) R *= -1.0;
            const FTransform Pose(R, FVector(Index * .013 + Layer * .05, Index * -.017, Layer * .03),
                FVector(1 + Index * .001, 1 + Layer * .07, 1 - Index * .001));
            (Layer == 0 ? Base : Target)[Bone] = Pose;
        }
    }
    Root->SetArrayField(TEXT("parents"), Parents);
    Root->SetArrayField(TEXT("names"), Names);
    Root->SetArrayField(TEXT("base"), PoseJson(Base));
    Root->SetArrayField(TEXT("layer"), PoseJson(Target));
    TArray<TSharedPtr<FJsonValue>> Cases;
    const float Alphas[] = {0, .000005f, .00001f, .000011f, .25f, .5f, .75f, .999989f, .99999f, 1};
    IConsoleVariable* Ispc = IConsoleManager::Get().FindConsoleVariable(TEXT("a.BlendPosesPerBoneFilter.ISPC"));
    const int32 OriginalIspc = Ispc ? Ispc->GetInt() : 0;
    for (int32 Backend = 0; Backend < (Ispc ? 2 : 1); ++Backend)
    {
        if (Ispc) Ispc->Set(Backend, ECVF_SetByCode);
        for (int32 Pattern = 0; Pattern < 4; ++Pattern)
        for (const float Alpha : Alphas)
        {
            TArray<TSharedPtr<FJsonValue>> WeightJson;
            for (const FCompactPoseBoneIndex Bone : Base.ForEachBoneIndex())
            {
                bool Affected = Pattern == 2 || (Pattern == 3 && Bone.GetInt() % 3 == 1);
                for (auto Ancestor = Bone; Ancestor.GetInt() >= 0 && Pattern < 2; Ancestor = Container.GetParentBoneIndex(Ancestor))
                {
                    const FName Name = Reference.GetBoneName(Container.GetSkeletonIndex(Ancestor));
                    Affected |= Name == (Pattern == 0 ? TEXT("thigh_l") : TEXT("thigh_r")) ||
                        Name == (Pattern == 0 ? TEXT("ik_foot_l") : TEXT("ik_foot_r"));
                }
                Weights[Bone.GetInt()].SourceIndex = 0;
                Weights[Bone.GetInt()].BlendWeight = Affected ? Alpha : 0;
                WeightJson.Add(MakeShared<FJsonValueNumber>(Weights[Bone.GetInt()].BlendWeight));
            }
            FAnimationRuntime::BlendPosesPerBoneFilter(Base, MakeArrayView(&Target, 1), BaseCurve,
                MakeArrayView(&TargetCurve, 1), BaseAttributes, MakeArrayView(&TargetAttributes, 1),
                OutputData, Weights, FAnimationRuntime::EBlendPosesPerBoneFilterFlags::MeshSpaceRotation, ECurveBlendOption::Override);
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("ispcRequested"), Backend);
            Row->SetNumberField(TEXT("pattern"), Pattern);
            Row->SetNumberField(TEXT("alpha"), Alpha);
            Row->SetArrayField(TEXT("weights"), WeightJson);
            Row->SetArrayField(TEXT("output"), PoseJson(Output));
            Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    if (Ispc) Ispc->Set(OriginalIspc, ECVF_SetByCode);
    Root->SetArrayField(TEXT("cases"), Cases);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 3;
    UE_LOG(LogTemp, Display, TEXT("ALS_MESH_SPACE_BLEND_OK cases=%d bones=%d assets_saved=0"), Cases.Num(), Base.GetNumBones());
    return 0;
}
