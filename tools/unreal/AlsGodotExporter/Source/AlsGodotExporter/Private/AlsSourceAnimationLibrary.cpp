#include "AlsSourceAnimationLibrary.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/Skeleton.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace
{
TArray<TSharedPtr<FJsonValue>> SourceNumbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}

TSharedRef<FJsonObject> SourceTransform(const FTransform& Transform)
{
    const FVector Position = Transform.GetTranslation();
    const FQuat Rotation = Transform.GetRotation();
    const FVector Scale = Transform.GetScale3D();
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetArrayField(TEXT("position"), SourceNumbers({Position.X, Position.Y, Position.Z}));
    Result->SetArrayField(TEXT("rotation"), SourceNumbers({Rotation.X, Rotation.Y, Rotation.Z, Rotation.W}));
    Result->SetArrayField(TEXT("scale"), SourceNumbers({Scale.X, Scale.Y, Scale.Z}));
    return Result;
}

TArray<TSharedPtr<FJsonValue>> SourceTransforms(const TArray<FTransform>& Transforms)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const FTransform& Transform : Transforms)
        Result.Add(MakeShared<FJsonValueObject>(SourceTransform(Transform)));
    return Result;
}

template <typename EnumType>
FString SourceEnumName(const EnumType Value)
{
    return StaticEnum<EnumType>()->GetNameStringByValue(static_cast<int64>(Value));
}

FString SourceJson(const TSharedRef<FJsonObject>& Object)
{
    FString Json;
    if (!FJsonSerializer::Serialize(Object, TJsonWriterFactory<>::Create(&Json))) return {};
    return Json;
}

void SourceOptionalPath(const TSharedRef<FJsonObject>& Object, const TCHAR* Field, const FString& Path)
{
    if (Path.IsEmpty()) Object->SetField(Field, MakeShared<FJsonValueNull>());
    else Object->SetStringField(Field, Path);
}
}

FString UAlsSourceAnimationLibrary::ReadSourceFloatCurves(UAnimSequence* Animation)
{
    if (!Animation || !Animation->GetDataModelInterface()) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    TArray<TSharedPtr<FJsonValue>> Curves;
    for (const FFloatCurve& Curve : Animation->GetDataModelInterface()->GetFloatCurves())
    {
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("name"), Curve.GetName().ToString());
        Row->SetStringField(TEXT("preInfinity"), SourceEnumName(Curve.FloatCurve.PreInfinityExtrap.GetValue()));
        Row->SetStringField(TEXT("postInfinity"), SourceEnumName(Curve.FloatCurve.PostInfinityExtrap.GetValue()));
        Row->SetNumberField(TEXT("defaultValue"), Curve.FloatCurve.GetDefaultValue());
        TArray<TSharedPtr<FJsonValue>> Keys;
        for (const FRichCurveKey& Key : Curve.FloatCurve.GetConstRefOfKeys())
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetNumberField(TEXT("time"), Key.Time);
            Value->SetNumberField(TEXT("value"), Key.Value);
            Value->SetNumberField(TEXT("arriveTangent"), Key.ArriveTangent);
            Value->SetNumberField(TEXT("leaveTangent"), Key.LeaveTangent);
            Value->SetNumberField(TEXT("arriveWeight"), Key.ArriveTangentWeight);
            Value->SetNumberField(TEXT("leaveWeight"), Key.LeaveTangentWeight);
            Value->SetStringField(TEXT("interpolation"), SourceEnumName(Key.InterpMode.GetValue()));
            Value->SetStringField(TEXT("tangentMode"), SourceEnumName(Key.TangentMode.GetValue()));
            Value->SetStringField(TEXT("weightMode"), SourceEnumName(Key.TangentWeightMode.GetValue()));
            Keys.Add(MakeShared<FJsonValueObject>(Value));
        }
        Row->SetArrayField(TEXT("keys"), Keys);
        Curves.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("curves"), Curves);
    return SourceJson(Result);
}

FString UAlsSourceAnimationLibrary::ReadSourceAnimationMetadata(UAnimSequence* Animation)
{
    if (!Animation || !Animation->GetSkeleton()) return {};
    const TScriptInterface<IAnimationDataModel> Model = Animation->GetDataModelInterface();
    if (!Model) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    PRAGMA_DISABLE_DEPRECATION_WARNINGS
    Result->SetNumberField(TEXT("sequencePlayLength"), Animation->GetPlayLength());
    PRAGMA_ENABLE_DEPRECATION_WARNINGS
    Result->SetStringField(TEXT("interpolation"), SourceEnumName(Animation->Interpolation));
    Result->SetStringField(TEXT("retargetSource"), Animation->RetargetSource.ToString());
    SourceOptionalPath(Result, TEXT("retargetSourceAsset"), Animation->GetRetargetSourceAsset().ToSoftObjectPath().ToString());
    Result->SetArrayField(TEXT("retargetSourceAssetReferencePose"), SourceTransforms(Animation->RetargetSourceAssetReferencePose));
    Result->SetStringField(TEXT("retargetTransformsSourceName"), Animation->GetRetargetTransformsSourceName().ToString());
    Result->SetArrayField(TEXT("retargetTransforms"), SourceTransforms(Animation->GetRetargetTransforms()));
    Result->SetBoolField(TEXT("enableRootMotion"), Animation->bEnableRootMotion);
    Result->SetBoolField(TEXT("forceRootLock"), Animation->bForceRootLock);
    Result->SetStringField(TEXT("rootMotionRootLock"), SourceEnumName(Animation->RootMotionRootLock.GetValue()));
    Result->SetBoolField(TEXT("useNormalizedRootMotionScale"), Animation->bUseNormalizedRootMotionScale);
    Result->SetStringField(TEXT("additiveType"), SourceEnumName(Animation->AdditiveAnimType.GetValue()));
    Result->SetStringField(TEXT("basePoseType"), SourceEnumName(Animation->RefPoseType.GetValue()));
    SourceOptionalPath(Result, TEXT("baseAsset"), Animation->RefPoseSeq ? Animation->RefPoseSeq->GetPathName() : FString());
    Result->SetNumberField(TEXT("baseFrame"), Animation->RefFrameIndex);
    Result->SetNumberField(TEXT("transformCurveCount"), Model->GetNumberOfTransformCurves());
    Result->SetNumberField(TEXT("animatedBoneAttributeCount"), Model->GetNumberOfAttributes());
    Result->SetNumberField(TEXT("floatCurveCount"), Model->GetNumberOfFloatCurves());
    TArray<TSharedPtr<FJsonValue>> CurveNames;
    for (const FFloatCurve& Curve : Model->GetFloatCurves())
        CurveNames.Add(MakeShared<FJsonValueString>(Curve.GetName().ToString()));
    Result->SetArrayField(TEXT("floatCurveNames"), CurveNames);
    const FReferenceSkeleton& Reference = Animation->GetSkeleton()->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    const FMemMark Mark(FMemStack::Get());
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Animation->GetSkeleton());
    Container.SetUseRAWData(true);
    Container.SetUseSourceData(false);
    Container.SetDisableRetargeting(false);
    FAnimExtractContext RootContext(0.0, false);
    RootContext.bExtractWithRootMotionProvider = false;
    Result->SetObjectField(TEXT("rootLockFirstFrame"),
        SourceTransform(Animation->ExtractRootTrackTransform(RootContext, &Container)));
    return SourceJson(Result);
}

FString UAlsSourceAnimationLibrary::ReadSkeletonPoseMetadata(USkeleton* Skeleton)
{
    if (!Skeleton) return {};
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Skeleton->GetPathName());
    TArray<TSharedPtr<FJsonValue>> RawNames, RawParents, LogicalNames, LogicalParents, Mapping, Modes, VirtualBones;
    for (int32 Bone = 0; Bone < Reference.GetRawBoneNum(); ++Bone)
    {
        RawNames.Add(MakeShared<FJsonValueString>(Reference.GetRawRefBoneInfo()[Bone].Name.ToString()));
        RawParents.Add(MakeShared<FJsonValueNumber>(Reference.GetRawParentIndex(Bone)));
        Modes.Add(MakeShared<FJsonValueString>(SourceEnumName(Skeleton->GetBoneTranslationRetargetingMode(Bone))));
    }
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
    {
        const FName Name = Reference.GetBoneName(Bone);
        LogicalNames.Add(MakeShared<FJsonValueString>(Name.ToString()));
        LogicalParents.Add(MakeShared<FJsonValueNumber>(Reference.GetParentIndex(Bone)));
        Mapping.Add(MakeShared<FJsonValueNumber>(Reference.FindRawBoneIndex(Name)));
    }
    for (const FVirtualBone& Virtual : Skeleton->GetVirtualBones())
    {
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("bone"), Reference.FindBoneIndex(Virtual.VirtualBoneName));
        Row->SetNumberField(TEXT("source"), Reference.FindBoneIndex(Virtual.SourceBoneName));
        Row->SetNumberField(TEXT("target"), Reference.FindBoneIndex(Virtual.TargetBoneName));
        VirtualBones.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("rawBoneNames"), RawNames);
    Result->SetArrayField(TEXT("rawParents"), RawParents);
    Result->SetArrayField(TEXT("logicalBoneNames"), LogicalNames);
    Result->SetArrayField(TEXT("logicalParents"), LogicalParents);
    Result->SetArrayField(TEXT("logicalToPhysical"), Mapping);
    Result->SetArrayField(TEXT("translationRetargetModes"), Modes);
    Result->SetArrayField(TEXT("virtualBones"), VirtualBones);
    Result->SetArrayField(TEXT("referencePose"), SourceTransforms(Reference.GetRefBonePose()));
    return SourceJson(Result);
}

namespace
{
FString ReadSourcePose(UAnimSequence* Animation, const double TimeSeconds,
                      const bool ShouldRetarget, const bool ExtractRootMotion,
                      const bool IgnoreRootLock, const bool EvaluateAdditive)
{
    if (!Animation || !Animation->GetSkeleton() || !FMath::IsFinite(TimeSeconds) ||
        !Animation->GetDataModelInterface()) return {};
    USkeleton* Skeleton = Animation->GetSkeleton();
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    const FMemMark Mark(FMemStack::Get());
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    Container.SetUseRAWData(true);
    Container.SetUseSourceData(false);
    Container.SetDisableRetargeting(!ShouldRetarget);
    FCompactPose Pose;
    Pose.SetBoneContainer(&Container);
    FBlendedCurve Curve;
    Curve.InitFrom(Container);
    UE::Anim::FStackAttributeContainer Attributes;
    FAnimationPoseData PoseData(Pose, Curve, Attributes);
    FAnimExtractContext Context(TimeSeconds, ExtractRootMotion);
    Context.bIgnoreRootLock = IgnoreRootLock;
    Context.bExtractWithRootMotionProvider = false;

    // Both entry points use the same RAW container. The animation entry additionally
    // performs the real asset's base-pose selection and local/mesh additive conversion.
    if (EvaluateAdditive) Animation->GetAnimationPose(PoseData, Context);
    else Animation->GetBonePose(PoseData, Context, true);
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    Result->SetNumberField(TEXT("timeSeconds"), TimeSeconds);
    Result->SetBoolField(TEXT("shouldRetarget"), ShouldRetarget);
    Result->SetBoolField(TEXT("extractRootMotion"), ExtractRootMotion);
    Result->SetBoolField(TEXT("ignoreRootLock"), IgnoreRootLock);
    TArray<TSharedPtr<FJsonValue>> Names, Poses;
    for (FCompactPoseBoneIndex Bone : Pose.ForEachBoneIndex())
    {
        const int32 SkeletonBone = Container.GetSkeletonPoseIndexFromCompactPoseIndex(Bone).GetInt();
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(SkeletonBone).ToString()));
        Poses.Add(MakeShared<FJsonValueObject>(SourceTransform(Pose[Bone])));
    }
    const TSharedRef<FJsonObject> Curves = MakeShared<FJsonObject>();
    Curve.ForEachElement([&](const auto& Element) { Curves->SetNumberField(Element.Name.ToString(), Element.Value); });
    Result->SetArrayField(TEXT("names"), Names);
    Result->SetArrayField(TEXT("pose"), Poses);
    Result->SetObjectField(TEXT("curves"), Curves);
    if (EvaluateAdditive) Result->SetBoolField(TEXT("evaluatedAdditive"), Animation->IsValidAdditive());
    return SourceJson(Result);
}
}

FString UAlsSourceAnimationLibrary::ReadRawBonePose(UAnimSequence* Animation, const double TimeSeconds,
                                                  const bool ShouldRetarget, const bool ExtractRootMotion,
                                                  const bool IgnoreRootLock)
{
    return ReadSourcePose(Animation, TimeSeconds, ShouldRetarget, ExtractRootMotion, IgnoreRootLock, false);
}

FString UAlsSourceAnimationLibrary::ReadRawAnimationPose(UAnimSequence* Animation, const double TimeSeconds,
                                                       const bool ShouldRetarget, const bool ExtractRootMotion,
                                                       const bool IgnoreRootLock)
{
    return ReadSourcePose(Animation, TimeSeconds, ShouldRetarget, ExtractRootMotion, IgnoreRootLock, true);
}

FString UAlsSourceAnimationLibrary::ReadRawSamplingCases()
{
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("source"), TEXT("FEvaluationContext + FAnimationRuntime.GetKeyIndicesFromTime + UAnimDataModel.Evaluate gates"));
    TArray<TSharedPtr<FJsonValue>> Cases;
    const TArray<FTransform> EmptyReference;
    const TArray<FFrameRate> Rates = {FFrameRate(30, 1), FFrameRate(30000, 1001)};
    const TArray<double> Frames = {-0.25, 0.0, 0.25, 0.0000999, 0.0001, 0.0001001,
        0.9998999, 0.9999, 0.9999001, 0.9999999, 0.99999998, 1.0, 1.0000001,
        2.0, 3.375, 7.75, 8.0, 8.25, 9.0};
    for (const FFrameRate& Rate : Rates)
    {
        for (const int32 KeyCount : {1, 9})
        {
            for (const EAnimInterpolationType Interpolation : {EAnimInterpolationType::Linear, EAnimInterpolationType::Step})
            {
                for (const double Frame : Frames)
                {
                    const double Time = (Frame * Rate.Denominator) / Rate.Numerator;
                    const UE::Anim::DataModel::FEvaluationContext Context(Time, Rate, NAME_None, EmptyReference, Interpolation);
                    const double SampleTime = Context.SampleFrameRate.AsSeconds(Context.SampleTime);
                    int32 First, Second;
                    float Alpha;
                    FAnimationRuntime::GetKeyIndicesFromTime(First, Second, Alpha, SampleTime, Rate, KeyCount);
                    const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
                    Row->SetNumberField(TEXT("frameRateNumerator"), Rate.Numerator);
                    Row->SetNumberField(TEXT("frameRateDenominator"), Rate.Denominator);
                    Row->SetNumberField(TEXT("sampledKeyCount"), KeyCount);
                    Row->SetNumberField(TEXT("interpolation"), static_cast<int32>(Interpolation));
                    Row->SetNumberField(TEXT("timeSeconds"), Time);
                    Row->SetNumberField(TEXT("sampleTimeSeconds"), SampleTime);
                    Row->SetNumberField(TEXT("selectorFirstKey"), First);
                    Row->SetNumberField(TEXT("selectorSecondKey"), Second);
                    Row->SetNumberField(TEXT("selectorAlpha"), Alpha);
                    // Exact gates from UAnimDataModel::Evaluate (AnimDataModel.cpp).
                    if (Interpolation == EAnimInterpolationType::Step) Alpha = 0.f;
                    bool Interpolate = true;
                    if (Alpha < UE_KINDA_SMALL_NUMBER)
                    {
                        Alpha = 0.f;
                        Interpolate = false;
                    }
                    else if (Alpha > 1.f - UE_KINDA_SMALL_NUMBER)
                    {
                        First = Second;
                        Interpolate = false;
                    }
                    Row->SetNumberField(TEXT("firstKey"), First);
                    Row->SetNumberField(TEXT("secondKey"), Second);
                    Row->SetNumberField(TEXT("alpha"), Alpha);
                    Row->SetBoolField(TEXT("interpolate"), Interpolate);
                    Cases.Add(MakeShared<FJsonValueObject>(Row));
                }
            }
        }
    }
    Result->SetArrayField(TEXT("samplingCases"), Cases);
    return SourceJson(Result);
}
