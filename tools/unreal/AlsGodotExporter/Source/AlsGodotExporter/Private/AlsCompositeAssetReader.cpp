#include "AlsCompositeAssetReader.h"

#include "AlsAnimationMetadataReader.h"
#include "AlsStableAssetId.h"
#include "Animation/AnimMontage.h"
#include "Animation/BlendSpace.h"

namespace
{
    void SetAnimationReference(const UAnimationAsset* Animation, const TSharedRef<FJsonObject>& Value)
    {
        if (Animation)
        {
            const FString ObjectPath = Animation->GetPathName();
            Value->SetStringField(TEXT("animationId"), FAlsStableAssetId::Create(ObjectPath));
            Value->SetStringField(TEXT("animationObjectPath"), ObjectPath);
        }
        else
        {
            Value->SetStringField(TEXT("animationId"), FString());
            Value->SetStringField(TEXT("animationObjectPath"), FString());
        }
    }
}

bool FAlsCompositeAssetReader::Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError)
{
    UObject* Object = Asset.AssetData.GetAsset();
    if (!Object)
    {
        OutError = FString::Printf(TEXT("Unable to load composite animation asset: %s"), *Asset.AssetData.GetObjectPathString());
        return false;
    }

    if (const UAnimMontage* Montage = Cast<UAnimMontage>(Object))
    {
        TArray<TSharedPtr<FJsonValue>> Sections;
        for (const FCompositeSection& Section : Montage->CompositeSections)
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("name"), Section.SectionName.ToString());
            Value->SetStringField(TEXT("nextSection"),
                Section.NextSectionName.IsNone() ? FString() : Section.NextSectionName.ToString());
            Value->SetNumberField(TEXT("startTime"), Section.GetTime());
            Sections.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("sections"), Sections);

        TArray<TSharedPtr<FJsonValue>> Slots;
        for (const FSlotAnimationTrack& Slot : Montage->SlotAnimTracks)
        {
            const TSharedRef<FJsonObject> SlotValue = MakeShared<FJsonObject>();
            SlotValue->SetStringField(TEXT("slotName"), Slot.SlotName.ToString());
            TArray<TSharedPtr<FJsonValue>> Segments;
            for (const FAnimSegment& Segment : Slot.AnimTrack.AnimSegments)
            {
                const TSharedRef<FJsonObject> SegmentValue = MakeShared<FJsonObject>();
                SetAnimationReference(Segment.GetAnimReference(), SegmentValue);
                SegmentValue->SetNumberField(TEXT("startPosition"), Segment.StartPos);
                SegmentValue->SetNumberField(TEXT("animationStartTime"), Segment.AnimStartTime);
                SegmentValue->SetNumberField(TEXT("animationEndTime"), Segment.AnimEndTime);
                SegmentValue->SetNumberField(TEXT("playRate"), Segment.AnimPlayRate);
                SegmentValue->SetNumberField(TEXT("loopCount"), Segment.LoopingCount);
                Segments.Add(MakeShared<FJsonValueObject>(SegmentValue));
            }
            SlotValue->SetArrayField(TEXT("segments"), Segments);
            Slots.Add(MakeShared<FJsonValueObject>(SlotValue));
        }
        OutMetadata->SetArrayField(TEXT("slots"), Slots);
        OutMetadata->SetNumberField(TEXT("playLength"), Montage->GetPlayLength());
        OutMetadata->SetNumberField(TEXT("blendInTime"), Montage->BlendIn.GetBlendTime());
        OutMetadata->SetNumberField(TEXT("blendInOption"), static_cast<uint8>(Montage->BlendIn.GetBlendOption()));
        OutMetadata->SetNumberField(TEXT("blendOutTime"), Montage->BlendOut.GetBlendTime());
        OutMetadata->SetNumberField(TEXT("blendOutOption"), static_cast<uint8>(Montage->BlendOut.GetBlendOption()));
        OutMetadata->SetNumberField(TEXT("blendOutTriggerTime"), Montage->BlendOutTriggerTime);
        OutMetadata->SetBoolField(TEXT("enableAutoBlendOut"), Montage->bEnableAutoBlendOut);
        if (!FAlsAnimationMetadataReader::ReadTimeline(*Montage, Asset.Id, OutMetadata, OutError))
        {
            return false;
        }

        OutMetadata->RemoveField(TEXT("syncMarkers"));
        return true;
    }

    if (const UBlendSpace* BlendSpace = Cast<UBlendSpace>(Object))
    {
        TArray<TSharedPtr<FJsonValue>> Parameters;
        for (int32 ParameterIndex = 0; ParameterIndex < 3; ++ParameterIndex)
        {
            const FBlendParameter& Parameter = BlendSpace->GetBlendParameter(ParameterIndex);
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("name"), Parameter.DisplayName);
            Value->SetNumberField(TEXT("minimum"), Parameter.Min);
            Value->SetNumberField(TEXT("maximum"), Parameter.Max);
            Value->SetNumberField(TEXT("gridDivisions"), Parameter.GridNum);
            Parameters.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("parameters"), Parameters);

        TArray<TSharedPtr<FJsonValue>> Samples;
        for (const FBlendSample& Sample : BlendSpace->GetBlendSamples())
        {
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            SetAnimationReference(Sample.Animation, Value);
            Value->SetArrayField(TEXT("sampleValue"), {
                MakeShared<FJsonValueNumber>(Sample.SampleValue.X),
                MakeShared<FJsonValueNumber>(Sample.SampleValue.Y),
                MakeShared<FJsonValueNumber>(Sample.SampleValue.Z),
            });
            Value->SetNumberField(TEXT("rateScale"), Sample.RateScale);
            Samples.Add(MakeShared<FJsonValueObject>(Value));
        }
        OutMetadata->SetArrayField(TEXT("samples"), Samples);
        return true;
    }

    OutError = FString::Printf(TEXT("Unsupported composite animation class: %s"), *Object->GetClass()->GetPathName());
    return false;
}

bool FAlsCompositeAssetReader::RunSelfTest(int32& OutCaseCount, FString& OutError)
{
    OutCaseCount = 0;
    OutError.Reset();
    UAnimMontage* MontageSelfTest = NewObject<UAnimMontage>();
    if (!MontageSelfTest)
    {
        OutError = TEXT("Composite self-test could not create a transient Montage.");
        return false;
    }
    FAnimSyncMarker& Marker = MontageSelfTest->MarkerData.AuthoredSyncMarkers.AddDefaulted_GetRef();
    Marker.MarkerName = TEXT("CompositeMarker");
    Marker.Time = 0.0f;

    FAlsExportAsset Asset;
    Asset.AssetData = FAssetData(MontageSelfTest);
    Asset.Kind = EAlsAssetKind::AnimMontage;
    Asset.Id = TEXT("transient-composite-montage-self-test");
    TSharedRef<FJsonObject> Metadata = MakeShared<FJsonObject>();
    FString ReadError;
    const TArray<TSharedPtr<FJsonValue>>* Timeline = nullptr;
    if (!Read(Asset, Metadata, ReadError) || !ReadError.IsEmpty() ||
        !Metadata->TryGetArrayField(TEXT("timeline"), Timeline) || !Timeline || Timeline->Num() != 0 ||
        Metadata->HasField(TEXT("syncMarkers")))
    {
        OutError = FString::Printf(TEXT("Composite self-test production Montage metadata mismatch: %s"), *ReadError);
        return false;
    }
    ++OutCaseCount;
    return true;
}
