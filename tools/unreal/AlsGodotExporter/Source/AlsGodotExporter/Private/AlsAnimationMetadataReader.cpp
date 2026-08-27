#include "AlsAnimationMetadataReader.h"

#include "AlsStableAssetId.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimCurveTypes.h"

namespace
{
    bool TryMapInterpolation(const ERichCurveInterpMode Mode, FString& OutInterpolation)
    {
        switch (Mode)
        {
        case RCIM_Constant: OutInterpolation = TEXT("Constant"); return true;
        case RCIM_Linear: OutInterpolation = TEXT("Linear"); return true;
        case RCIM_Cubic: OutInterpolation = TEXT("Cubic"); return true;
        default: return false;
        }
    }

    bool TryMapInfinity(const ERichCurveExtrapolation Mode, FString& OutInfinity)
    {
        switch (Mode)
        {
        case RCCE_None:
        case RCCE_Constant: OutInfinity = TEXT("Constant"); return true;
        case RCCE_Linear: OutInfinity = TEXT("Linear"); return true;
        case RCCE_Cycle: OutInfinity = TEXT("Cycle"); return true;
        case RCCE_CycleWithOffset: OutInfinity = TEXT("CycleWithOffset"); return true;
        case RCCE_Oscillate: OutInfinity = TEXT("Oscillate"); return true;
        default: return false;
        }
    }

    bool IsFiniteCurveKey(const FAlsExportedFloatCurveKey& Key)
    {
        return FMath::IsFinite(Key.TimeSeconds) && FMath::IsFinite(Key.Value) &&
            FMath::IsFinite(Key.ArriveTangent) && FMath::IsFinite(Key.LeaveTangent);
    }
}

bool FAlsAnimationMetadataReader::Read(const FAlsExportAsset& Asset, TSharedRef<FJsonObject>& OutMetadata,
    TArray<FAlsExportedFloatCurve>& OutCurves, FString& OutError)
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
    OutMetadata->SetBoolField(TEXT("loop"), Sequence->bLoop);
    OutMetadata->SetNumberField(TEXT("interpolation"), static_cast<uint8>(Sequence->Interpolation));
    OutMetadata->SetBoolField(TEXT("rootMotionEnabled"), Sequence->bEnableRootMotion);
    OutMetadata->SetNumberField(TEXT("rootMotionRootLock"), static_cast<uint8>(Sequence->RootMotionRootLock.GetValue()));
    OutMetadata->SetBoolField(TEXT("forceRootLock"), Sequence->bForceRootLock);
    OutMetadata->SetBoolField(TEXT("useNormalizedRootMotionScale"), Sequence->bUseNormalizedRootMotionScale);
    OutMetadata->SetNumberField(TEXT("additiveType"), static_cast<uint8>(Sequence->GetAdditiveAnimType()));
    OutMetadata->SetNumberField(TEXT("additiveBasePoseType"), static_cast<uint8>(Sequence->RefPoseType.GetValue()));
    OutMetadata->SetNumberField(TEXT("additiveBasePoseFrame"), Sequence->RefFrameIndex);
    const FString BasePosePath = Sequence->RefPoseSeq ? Sequence->RefPoseSeq->GetPathName() : FString();
    OutMetadata->SetStringField(TEXT("additiveBasePoseObjectPath"), BasePosePath);
    OutMetadata->SetStringField(TEXT("additiveBasePoseId"),
        BasePosePath.StartsWith(TEXT("/Game/AdvancedLocomotionV4/")) ? FAlsStableAssetId::Create(BasePosePath) : FString());
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

    OutCurves.Reset();
    if (const IAnimationDataModel* DataModel = Sequence->GetDataModel())
    {
        for (const FFloatCurve& Curve : DataModel->GetFloatCurves())
        {
            FAlsExportedFloatCurve ExportedCurve;
            ExportedCurve.SourceName = Curve.GetName().ToString();
            if (!TryMapInfinity(Curve.FloatCurve.PreInfinityExtrap, ExportedCurve.PreInfinity) ||
                !TryMapInfinity(Curve.FloatCurve.PostInfinityExtrap, ExportedCurve.PostInfinity))
            {
                OutError = FString::Printf(TEXT("Unsupported float curve infinity mode on %s."), *ExportedCurve.SourceName);
                return false;
            }

            for (const FRichCurveKey& Key : Curve.FloatCurve.GetConstRefOfKeys())
            {
                FAlsExportedFloatCurveKey ExportedKey;
                ExportedKey.TimeSeconds = Key.Time;
                ExportedKey.Value = Key.Value;
                ExportedKey.ArriveTangent = Key.ArriveTangent;
                ExportedKey.LeaveTangent = Key.LeaveTangent;
                if (!TryMapInterpolation(Key.InterpMode, ExportedKey.Interpolation))
                {
                    OutError = FString::Printf(TEXT("Unsupported float curve interpolation on %s."), *ExportedCurve.SourceName);
                    return false;
                }
                if (!IsFiniteCurveKey(ExportedKey))
                {
                    OutError = FString::Printf(TEXT("Non-finite float curve key on %s."), *ExportedCurve.SourceName);
                    return false;
                }
                ExportedCurve.Keys.Add(MoveTemp(ExportedKey));
            }

            ExportedCurve.Keys.Sort([](const FAlsExportedFloatCurveKey& Left, const FAlsExportedFloatCurveKey& Right)
            {
                return Left.TimeSeconds < Right.TimeSeconds;
            });
            for (int32 KeyIndex = 1; KeyIndex < ExportedCurve.Keys.Num(); ++KeyIndex)
            {
                if (ExportedCurve.Keys[KeyIndex - 1].TimeSeconds >= ExportedCurve.Keys[KeyIndex].TimeSeconds)
                {
                    OutError = FString::Printf(TEXT("Duplicate float curve key time on %s."), *ExportedCurve.SourceName);
                    return false;
                }
            }
            OutCurves.Add(MoveTemp(ExportedCurve));
        }
    }
    OutCurves.Sort([](const FAlsExportedFloatCurve& Left, const FAlsExportedFloatCurve& Right)
    {
        return Left.SourceName.Compare(Right.SourceName, ESearchCase::CaseSensitive) < 0;
    });
    for (int32 CurveIndex = 0; CurveIndex < OutCurves.Num(); ++CurveIndex)
    {
        OutCurves[CurveIndex].StableCurveId = CurveIndex;
    }

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
