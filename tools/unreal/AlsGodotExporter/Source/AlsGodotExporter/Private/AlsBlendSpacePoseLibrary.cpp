#include "AlsSourceAnimationLibrary.h"

#include "Animation/BlendSpace.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UObjectGlobals.h"

static FString ReadBlendPose(UBlendSpace* BlendSpace, const float Pitch, const float Y,
    const float NormalizedTime, const bool Legacy, const FString& TimedJson = FString(),
    UAnimSequence* BaseAnimation = nullptr, const double BaseTime = 0)
{
    if (!BlendSpace || !BlendSpace->GetSkeleton() || !FMath::IsFinite(Pitch) || !FMath::IsFinite(Y) || !FMath::IsFinite(NormalizedTime)) return {};
    if (BaseAnimation && (BaseAnimation->GetSkeleton() != BlendSpace->GetSkeleton() ||
        BaseAnimation->IsValidAdditive() || !FMath::IsFinite(BaseTime))) return {};
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
    if (TimedJson.IsEmpty())
    {
        if (!BlendSpace->GetSamplesFromBlendInput(FVector(Pitch, Y, 0), Samples, CachedIndex, true)) return {};
    }
    else
    {
        TSharedPtr<FJsonObject> Input;
        if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(TimedJson), Input)) return {};
        for (const auto& Value : Input->GetArrayField(TEXT("samples")))
        {
            const auto Row = Value->AsObject(); FBlendSampleData Sample;
            Sample.SampleDataIndex = static_cast<int32>(Row->GetNumberField(TEXT("index")));
            if (!BlendSpace->GetBlendSamples().IsValidIndex(Sample.SampleDataIndex)) return {};
            Sample.TotalWeight = static_cast<float>(Row->GetNumberField(TEXT("weight")));
            PRAGMA_DISABLE_DEPRECATION_WARNINGS
            Sample.Time = Row->GetNumberField(TEXT("time"));
            PRAGMA_ENABLE_DEPRECATION_WARNINGS
            Samples.Add(Sample);
        }
        if (Samples.IsEmpty()) return {};
    }
    const float Time = FMath::Clamp(NormalizedTime, 0.0f, 1.0f);
    TArray<TSharedPtr<FJsonValue>> SampleRows;
    for (FBlendSampleData& Sample : Samples)
    {
        const FBlendSample& Asset = BlendSpace->GetBlendSample(Sample.SampleDataIndex);
        if (!Asset.Animation) return {};
        PRAGMA_DISABLE_DEPRECATION_WARNINGS
        if (TimedJson.IsEmpty()) Sample.Time = Time * Asset.Animation->GetPlayLength();
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
    if (BaseAnimation)
    {
        FCompactPose BasePose; BasePose.SetBoneContainer(&Container);
        FBlendedCurve BaseCurve; BaseCurve.InitFrom(Container);
        UE::Anim::FStackAttributeContainer BaseAttributes;
        FAnimationPoseData BaseData(BasePose, BaseCurve, BaseAttributes);
        FAnimExtractContext BaseContext(BaseTime, false);
        BaseContext.bIgnoreRootLock = false;
        BaseContext.bExtractWithRootMotionProvider = false;
        BaseAnimation->GetAnimationPose(BaseData, BaseContext);
        auto PoseRows = [&](const FCompactPose& Atoms)
        {
            TArray<TSharedPtr<FJsonValue>> Rows;
            for (const FCompactPoseBoneIndex Bone : Atoms.ForEachBoneIndex())
            {
                const FTransform& Transform = Atoms[Bone];
                const FVector P = Transform.GetTranslation(), S = Transform.GetScale3D();
                const FQuat Q = Transform.GetRotation();
                const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
                Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
                Row->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
                Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z}));
                Rows.Add(MakeShared<FJsonValueObject>(Row));
            }
            return Rows;
        };
        Result->SetStringField(TEXT("baseSource"), BaseAnimation->GetPathName());
        Result->SetNumberField(TEXT("baseTime"), BaseTime);
        Result->SetArrayField(TEXT("basePose"), PoseRows(BasePose));
        FAnimationRuntime::AccumulateMeshSpaceRotationAdditiveToLocalPose(BaseData, PoseData, 1.0f);
        BasePose.NormalizeRotations();
        Result->SetArrayField(TEXT("appliedPose"), PoseRows(BasePose));
    }
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

FString UAlsSourceAnimationLibrary::ReadRawAimingPose2D(UBlendSpace* BlendSpace, UAnimSequence* BaseAnimation,
    const double BaseTime, const float X, const float Y, const float NormalizedTime)
{
    if (!BaseAnimation) return {};
    return ReadBlendPose(BlendSpace, X, Y, NormalizedTime, false, FString(), BaseAnimation, BaseTime);
}

static UBlendSpace* RetargetTransientBlendSpace(UBlendSpace* Source, USkeleton* TargetSkeleton,
    const TArray<UAnimSequence*>& TargetSamples)
{
    if (!Source || !TargetSkeleton || Source->GetBlendSamples().Num() != TargetSamples.Num()) return nullptr;
    for (const UAnimSequence* Sample : TargetSamples)
    {
        if (!Sample || Sample->GetSkeleton() != TargetSkeleton) return nullptr;
    }

    UBlendSpace* Temporary = DuplicateObject<UBlendSpace>(Source, GetTransientPackage());
    if (!Temporary) return nullptr;
    Temporary->SetFlags(RF_Transient);
    for (int32 Index = 0; Index < TargetSamples.Num(); ++Index)
    {
        if (!Temporary->ReplaceSampleAnimation(Index, TargetSamples[Index])) return nullptr;
    }
    Temporary->SetSkeleton(TargetSkeleton);
    Temporary->ValidateSampleData();
    if (Temporary->GetBlendSamples().Num() != TargetSamples.Num()) return nullptr;
    for (int32 Index = 0; Index < TargetSamples.Num(); ++Index)
    {
        if (Temporary->GetBlendSample(Index).Animation != TargetSamples[Index] ||
            !Temporary->GetBlendSample(Index).bIsValid) return nullptr;
    }
    Temporary->ResampleData();
    return Temporary;
}

FString UAlsSourceAnimationLibrary::ReadRetargetedBlendSpacePose2D(UBlendSpace* Source, USkeleton* TargetSkeleton,
    const TArray<UAnimSequence*>& TargetSamples, const float X, const float Y, const float NormalizedTime)
{
    UBlendSpace* Temporary = RetargetTransientBlendSpace(Source, TargetSkeleton, TargetSamples);
    if (!Temporary) return {};
    return ReadRawBlendSpacePose2D(Temporary, X, Y, NormalizedTime);
}

FString UAlsSourceAnimationLibrary::ReadRetargetedAimingPose2D(UBlendSpace* Source, USkeleton* TargetSkeleton,
    const TArray<UAnimSequence*>& TargetSamples, UAnimSequence* BaseAnimation, const double BaseTime,
    const float X, const float Y, const float NormalizedTime)
{
    UBlendSpace* Temporary = RetargetTransientBlendSpace(Source, TargetSkeleton, TargetSamples);
    if (!Temporary || !BaseAnimation) return {};
    return ReadBlendPose(Temporary, X, Y, NormalizedTime, false, FString(), BaseAnimation, BaseTime);
}

FString UAlsSourceAnimationLibrary::ReadRawBlendSpaceTimedPose(UBlendSpace* BlendSpace, const FString& SamplesJson)
{
    if (SamplesJson.IsEmpty()) return {};
    return ReadBlendPose(BlendSpace, 0, 0, 0, false, SamplesJson);
}
