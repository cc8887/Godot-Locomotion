#include "AlsLyraGraphLibrary.h"
#include "AlsSourceAnimationLibrary.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimData/IAnimationDataController.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/Skeleton.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "JsonObjectConverter.h"

namespace
{
TSharedPtr<FJsonObject> AttributeValue(const UScriptStruct* Type, const void* Address)
{
    const auto Result = MakeShared<FJsonObject>();
    if (!Type || !Address || !FJsonObjectConverter::UStructToJsonObject(Type, Address, Result)) return nullptr;
    return Result;
}
}

FString UAlsLyraGraphLibrary::ReadSourceCurveTrace(UAnimSequence* Animation, const FString& TimesJson)
{
    TArray<TSharedPtr<FJsonValue>> Times;
    if (!Animation || !Animation->GetSkeleton() || !Animation->GetDataModelInterface() ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(TimesJson), Times)) return {};
    TSharedPtr<FJsonObject> Metadata;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(
        UAlsSourceAnimationLibrary::ReadSourceFloatCurves(Animation)), Metadata)) return {};
    const auto& SourceCurves = Animation->GetDataModelInterface()->GetFloatCurves();
    for (int32 Index = 0; Index < SourceCurves.Num(); ++Index)
        Metadata->GetArrayField(TEXT("curves"))[Index]->AsObject()->SetNumberField(
            TEXT("typeFlags"), SourceCurves[Index].GetCurveTypeFlags());
    TArray<TSharedPtr<FJsonValue>> AttributeMetadata;
    for (const auto& Attribute : Animation->GetDataModelInterface()->GetAttributes())
    {
        const auto Row = MakeShared<FJsonObject>(); const auto& Id = Attribute.Identifier;
        Row->SetStringField(TEXT("name"), Id.GetName().ToString());
        Row->SetStringField(TEXT("bone"), Id.GetBoneName().ToString());
        Row->SetNumberField(TEXT("boneIndex"), Id.GetBoneIndex());
        Row->SetStringField(TEXT("type"), Id.GetType()->GetPathName());
        TArray<TSharedPtr<FJsonValue>> Keys;
        for (const auto& Key : Attribute.Curve.GetConstRefOfKeys())
        {
            const auto Value = MakeShared<FJsonObject>(); Value->SetNumberField(TEXT("time"), Key.Time);
            const auto Data = AttributeValue(Id.GetType(), Key.GetValuePtr<void>()); if (!Data) return {};
            Value->SetObjectField(TEXT("value"), Data); Keys.Add(MakeShared<FJsonValueObject>(Value));
        }
        Row->SetArrayField(TEXT("keys"), Keys); AttributeMetadata.Add(MakeShared<FJsonValueObject>(Row));
    }
    Metadata->SetArrayField(TEXT("attributes"), AttributeMetadata);
    const FMemMark Mark(FMemStack::Get());
    TArray<FBoneIndexType> Required;
    const auto& Reference = Animation->GetSkeleton()->GetReferenceSkeleton();
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Animation->GetSkeleton());
    Container.SetUseRAWData(true); Container.SetUseSourceData(false); Container.SetDisableRetargeting(false);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for (const auto& Time : Times)
    {
        const double Seconds = Time->AsNumber(); if (!FMath::IsFinite(Seconds)) return {};
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("seconds"), Seconds);
        for (int32 Mode = 0; Mode < 2; ++Mode)
        {
            const FMemMark FrameMark(FMemStack::Get());
            FCompactPose Pose; Pose.SetBoneContainer(&Container);
            FBlendedCurve Curve; Curve.InitFrom(Container);
            UE::Anim::FStackAttributeContainer Attributes;
            FAnimationPoseData Data(Pose, Curve, Attributes);
            FAnimExtractContext Context(Seconds, false); Context.bExtractWithRootMotionProvider = false;
            if (Mode == 0) Animation->GetBonePose(Data, Context, true);
            else Animation->GetAnimationPose(Data, Context);
            const auto Values = MakeShared<FJsonObject>();
            Curve.ForEachElement([&](const auto& Element)
            {
                const auto Value = MakeShared<FJsonObject>();
                Value->SetNumberField(TEXT("value"), Element.Value);
                Value->SetNumberField(TEXT("flags"), static_cast<uint32>(Element.Flags));
                Values->SetObjectField(Element.Name.ToString(), Value);
            });
            Row->SetObjectField(Mode == 0 ? TEXT("raw") : TEXT("output"), Values);
            TArray<TSharedPtr<FJsonValue>> AttributeValues;
            const auto& Types = Attributes.GetUniqueTypes();
            for (int32 TypeIndex = 0; TypeIndex < Types.Num(); ++TypeIndex)
            {
                const auto& Keys = Attributes.GetKeys(TypeIndex); const auto& AttributeData = Attributes.GetValues(TypeIndex);
                for (int32 Index = 0; Index < Keys.Num(); ++Index)
                {
                    const auto Value = MakeShared<FJsonObject>(); const auto& Key = Keys[Index];
                    Value->SetStringField(TEXT("name"), Key.GetName().ToString());
                    Value->SetStringField(TEXT("namespace"), Key.GetNamespace().ToString());
                    Value->SetStringField(TEXT("type"), Types[TypeIndex]->GetPathName());
                    const int32 Bone = Container.GetSkeletonPoseIndexFromCompactPoseIndex(FCompactPoseBoneIndex(Key.GetIndex())).GetInt();
                    Value->SetStringField(TEXT("bone"), Reference.GetBoneName(Bone).ToString());
                    const auto DataValue = AttributeValue(Types[TypeIndex].Get(), AttributeData[Index].template GetPtr<void>());
                    if (!DataValue) return {};
                    Value->SetObjectField(TEXT("value"), DataValue); AttributeValues.Add(MakeShared<FJsonValueObject>(Value));
                }
            }
            Row->SetArrayField(Mode == 0 ? TEXT("rawAttributes") : TEXT("outputAttributes"), AttributeValues);
        }
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetObjectField(TEXT("metadata"), Metadata);
    Result->SetArrayField(TEXT("rows"), Rows);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

UAnimSequence* UAlsLyraGraphLibrary::CreateSourceCurveFixture(UAnimSequence* Animation)
{
    if (!Animation || !Animation->IsValidAdditive() || !Animation->GetDataModelInterface() ||
        !Animation->GetDataModelInterface()->GetFloatCurves().IsEmpty()) return nullptr;
    UAnimSequence* Base = DuplicateObject<UAnimSequence>(Animation, GetTransientPackage());
    UAnimSequence* Source = DuplicateObject<UAnimSequence>(Animation, GetTransientPackage());
    for (auto* Sequence : { Base, Source })
    { Sequence->ClearFlags(RF_Public | RF_Standalone); Sequence->SetFlags(RF_Transient); }
    Base->AdditiveAnimType = AAT_None; Base->RefPoseSeq = nullptr;
    Source->RefPoseSeq = Base; Source->RefPoseType = ABPT_AnimFrame; Source->RefFrameIndex = 0;
    const float Length = static_cast<float>(Animation->GetPlayLength());
    auto Add = [Length](UAnimSequence* Sequence, FName Name, float First, float Middle, float Last)
    {
        auto& Controller = Sequence->GetController();
        const FAnimationCurveIdentifier Id(Name, ERawCurveTrackTypes::RCT_Float);
        TArray<FRichCurveKey> Keys{FRichCurveKey(0.f, First), FRichCurveKey(Length * .41f, Middle), FRichCurveKey(Length, Last)};
        for (auto& Key : Keys) { Key.InterpMode = RCIM_Cubic; Key.TangentMode = RCTM_User; Key.ArriveTangent = -7.f; Key.LeaveTangent = 12.f; }
        return Controller.AddCurve(Id, AACF_DefaultCurve, false) && Controller.SetCurveKeys(Id, Keys, false);
    };
    if (!Add(Base, TEXT("NativeBoth"), 1.f, 3.f, 2.f) || !Add(Base, TEXT("NativeBaseOnly"), 8.f, 10.f, 9.f) ||
        !Add(Source, TEXT("NativeBoth"), 4.f, 13.f, -6.f) || !Add(Source, TEXT("NativeSourceOnly"), -2.f, 5.f, 1.f)) return nullptr;
    return Source;
}
