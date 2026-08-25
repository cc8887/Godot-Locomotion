#include "AlsAnimationMetadataReader.h"

#include "AlsStableAssetId.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimCurveTypes.h"

bool FAlsAnimationMetadataReader::Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata, FString& OutError)
{
    const UAnimSequence* Sequence = Cast<UAnimSequence>(Asset.AssetData.GetAsset());
    if (!Sequence)
    {
        OutError = FString::Printf(TEXT("Unable to load animation sequence: %s"), *Asset.AssetData.GetObjectPathString());
        return false;
    }

    const FFrameRate FrameRate = Sequence->GetSamplingFrameRate();
    OutMetadata->SetNumberField(TEXT("playLength"), Sequence->GetPlayLength());
    OutMetadata->SetNumberField(TEXT("frameRateNumerator"), FrameRate.Numerator);
    OutMetadata->SetNumberField(TEXT("frameRateDenominator"), FrameRate.Denominator);
    OutMetadata->SetNumberField(TEXT("sampledKeyCount"), Sequence->GetNumberOfSampledKeys());
    OutMetadata->SetBoolField(TEXT("rootMotionEnabled"), Sequence->bEnableRootMotion);
    OutMetadata->SetNumberField(TEXT("rootMotionRootLock"), static_cast<uint8>(Sequence->RootMotionRootLock.GetValue()));
    OutMetadata->SetNumberField(TEXT("additiveType"), static_cast<uint8>(Sequence->GetAdditiveAnimType()));
    if (const USkeleton* Skeleton = Sequence->GetSkeleton())
    {
        const FString SkeletonPath = Skeleton->GetPathName();
        OutMetadata->SetStringField(TEXT("skeletonId"), FAlsStableAssetId::Create(SkeletonPath));
        OutMetadata->SetStringField(TEXT("skeletonObjectPath"), SkeletonPath);
    }
    else
    {
        OutMetadata->SetStringField(TEXT("skeletonId"), FString());
        OutMetadata->SetStringField(TEXT("skeletonObjectPath"), FString());
    }

    TArray<FString> CurveNames;
    if (const IAnimationDataModel* DataModel = Sequence->GetDataModel())
    {
        for (const FFloatCurve& Curve : DataModel->GetFloatCurves())
        {
            CurveNames.Add(Curve.GetName().ToString());
        }
    }
    CurveNames.Sort();
    TArray<TSharedPtr<FJsonValue>> Curves;
    for (const FString& CurveName : CurveNames)
    {
        Curves.Add(MakeShared<FJsonValueString>(CurveName));
    }
    OutMetadata->SetArrayField(TEXT("curves"), Curves);

    TArray<TSharedPtr<FJsonValue>> Notifies;
    for (int32 NotifyIndex = 0; NotifyIndex < Sequence->Notifies.Num(); ++NotifyIndex)
    {
        const FAnimNotifyEvent& Notify = Sequence->Notifies[NotifyIndex];
        const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
        Value->SetStringField(TEXT("name"), Notify.GetNotifyEventName().ToString());
        Value->SetNumberField(TEXT("time"), Notify.GetTime());
        Value->SetNumberField(TEXT("duration"), Notify.GetDuration());
        Value->SetNumberField(TEXT("sourceIndex"), NotifyIndex);
        Notifies.Add(MakeShared<FJsonValueObject>(Value));
    }
    OutMetadata->SetArrayField(TEXT("notifies"), Notifies);

    TArray<FAnimSyncMarker> Markers = Sequence->AuthoredSyncMarkers;
    Markers.Sort([](const FAnimSyncMarker& Left, const FAnimSyncMarker& Right)
    {
        return Left.Time == Right.Time ? Left.MarkerName.LexicalLess(Right.MarkerName) : Left.Time < Right.Time;
    });
    TArray<TSharedPtr<FJsonValue>> SyncMarkers;
    for (const FAnimSyncMarker& Marker : Markers)
    {
        const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
        Value->SetStringField(TEXT("name"), Marker.MarkerName.ToString());
        Value->SetNumberField(TEXT("time"), Marker.Time);
        SyncMarkers.Add(MakeShared<FJsonValueObject>(Value));
    }
    OutMetadata->SetArrayField(TEXT("syncMarkers"), SyncMarkers);
    return true;
}
