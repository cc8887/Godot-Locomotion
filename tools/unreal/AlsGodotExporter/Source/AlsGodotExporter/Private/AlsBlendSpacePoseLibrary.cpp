#include "AlsSourceAnimationLibrary.h"

#include "Animation/BlendSpace.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

static FString ReadBlendPose(UBlendSpace* BlendSpace, const float Pitch, const float Y, const float NormalizedTime, const bool Legacy)
{
    if (!BlendSpace || !BlendSpace->GetSkeleton() || !FMath::IsFinite(Pitch) || !FMath::IsFinite(Y) || !FMath::IsFinite(NormalizedTime)) return {};
    USkeleton* Skeleton = BlendSpace->GetSkeleton();
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    const FMemMark Mark(FMemStack::Get());
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    Container.SetUseRAWData(true);
    Container.SetUseSourceData(false);
    Container.SetDisableRetargeting(false);
    FCompactPose Pose; Pose.SetBoneContainer(&Container);
    FBlendedCurve Curve; Curve.InitFrom(Container);
    UE::Anim::FStackAttributeContainer Attributes;
    FAnimationPoseData PoseData(Pose, Curve, Attributes);

    TArray<FBlendSampleData> Samples;
    int32 CachedIndex = INDEX_NONE;
    if (!BlendSpace->GetSamplesFromBlendInput(FVector(Pitch, Y, 0), Samples, CachedIndex, true)) return {};
    const float Time = FMath::Clamp(NormalizedTime, 0.0f, 1.0f);
    TArray<TSharedPtr<FJsonValue>> SampleRows;
    for (FBlendSampleData& Sample : Samples)
    {
        const FBlendSample& Asset = BlendSpace->GetBlendSample(Sample.SampleDataIndex);
        if (!Asset.Animation) return {};
        PRAGMA_DISABLE_DEPRECATION_WARNINGS
        Sample.Time = Time * Asset.Animation->GetPlayLength();
        PRAGMA_ENABLE_DEPRECATION_WARNINGS
        Sample.PreviousTime = Sample.Time;
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("index"), Sample.SampleDataIndex);
        Row->SetNumberField(TEXT("weight"), Sample.GetClampedWeight());
        Row->SetNumberField(TEXT("seconds"), Sample.Time);
        SampleRows.Add(MakeShared<FJsonValueObject>(Row));
    }
    FAnimExtractContext Context(0.0, false);
    Context.bIgnoreRootLock = false;
    Context.bExtractWithRootMotionProvider = false;
    BlendSpace->GetAnimationPose(Samples, Context, PoseData);
    auto Numbers = [](std::initializer_list<double> Values)
    {
        TArray<TSharedPtr<FJsonValue>> Result;
        for (const double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
        return Result;
    };
    TArray<TSharedPtr<FJsonValue>> Names, Poses;
    for (const FCompactPoseBoneIndex Bone : Pose.ForEachBoneIndex())
    {
        const FTransform& Transform = Pose[Bone];
        const FVector P = Transform.GetTranslation(), S = Transform.GetScale3D();
        const FQuat Q = Transform.GetRotation();
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
        Row->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
        Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z}));
        Poses.Add(MakeShared<FJsonValueObject>(Row));
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Container.GetSkeletonPoseIndexFromCompactPoseIndex(Bone).GetInt()).ToString()));
    }
    const TSharedRef<FJsonObject> Curves = MakeShared<FJsonObject>();
    Curve.ForEachElement([&](const auto& Element) { Curves->SetNumberField(Element.Name.ToString(), Element.Value); });
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), BlendSpace->GetPathName());
    Result->SetNumberField(TEXT("pitch"), Pitch);
    if (!Legacy) Result->SetNumberField(TEXT("y"), Y);
    Result->SetNumberField(TEXT("normalizedTime"), NormalizedTime);
    Result->SetArrayField(TEXT("samples"), SampleRows);
    Result->SetArrayField(TEXT("names"), Names);
    Result->SetArrayField(TEXT("pose"), Poses);
    Result->SetObjectField(TEXT("curves"), Curves);
    FString Json;
    if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json))) return {};
    return Json;
}

FString UAlsSourceAnimationLibrary::ReadRawBlendSpacePose(UBlendSpace* BlendSpace, const float Pitch, const float NormalizedTime)
{
    return ReadBlendPose(BlendSpace, Pitch, 0, NormalizedTime, true);
}

FString UAlsSourceAnimationLibrary::ReadRawBlendSpacePose2D(UBlendSpace* BlendSpace, const float X, const float Y, const float NormalizedTime)
{
    return ReadBlendPose(BlendSpace, X, Y, NormalizedTime, false);
}
