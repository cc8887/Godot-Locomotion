#include "AlsAnimationMetadataReader.h"

#include "AlsNotifyClassRegistry.h"
#include "AlsStableAssetId.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimNotifies/AnimNotify_ResetDynamics.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimCurveTypes.h"
#include "Animation/Skeleton.h"
#include "Misc/SecureHash.h"
#include "ReferenceSkeleton.h"

#include <limits>

namespace
{
    constexpr TCHAR CanonicalRotationYawCurveName[] = TEXT("RotationYawSpeedRadiansPerSecond");
    constexpr TCHAR CanonicalRotationYawKind[] = TEXT("RotationYawSpeedRadiansPerSecond");
    constexpr double CanonicalYawDurationToleranceSeconds = 1e-4;

    FString CreateTimelineSha1(const FString& Value)
    {
        const FTCHARToUTF8 Utf8(*Value);
        uint8 Hash[FSHA1::DigestSize];
        FSHA1::HashBuffer(Utf8.Get(), Utf8.Length(), Hash);
        FString Result = BytesToHex(Hash, UE_ARRAY_COUNT(Hash));
        Result.ToLowerInline();
        return Result;
    }

    bool TimelineEntryLess(const FAlsExportedTimelineEntry& Left, const FAlsExportedTimelineEntry& Right)
    {
        if (Left.TimeSeconds != Right.TimeSeconds)
        {
            return Left.TimeSeconds < Right.TimeSeconds;
        }
        if (Left.SourceIndex != Right.SourceIndex)
        {
            return Left.SourceIndex < Right.SourceIndex;
        }
        if (Left.TrackIndex != Right.TrackIndex)
        {
            return Left.TrackIndex < Right.TrackIndex;
        }
        return Left.StableEventId.Compare(Right.StableEventId, ESearchCase::CaseSensitive) < 0;
    }

    bool SyncMarkerLess(const FAlsExportedSyncMarker& Left, const FAlsExportedSyncMarker& Right)
    {
        if (Left.TimeSeconds != Right.TimeSeconds)
        {
            return Left.TimeSeconds < Right.TimeSeconds;
        }
        if (Left.SourceIndex != Right.SourceIndex)
        {
            return Left.SourceIndex < Right.SourceIndex;
        }
        if (Left.TrackIndex != Right.TrackIndex)
        {
            return Left.TrackIndex < Right.TrackIndex;
        }
        return Left.StableMarkerId.Compare(Right.StableMarkerId, ESearchCase::CaseSensitive) < 0;
    }

    TSharedRef<FJsonObject> TimelineEntryToJson(const FAlsExportedTimelineEntry& Entry)
    {
        const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
        Value->SetStringField(TEXT("stableEventId"), Entry.StableEventId);
        Value->SetStringField(TEXT("kind"), Entry.Kind);
        Value->SetStringField(TEXT("sourceClassPath"), Entry.SourceClassPath);
        Value->SetStringField(TEXT("displayName"), Entry.DisplayName);
        Value->SetNumberField(TEXT("timeSeconds"), Entry.TimeSeconds);
        Value->SetNumberField(TEXT("durationSeconds"), Entry.DurationSeconds);
        Value->SetNumberField(TEXT("triggerWeightThreshold"), Entry.TriggerWeightThreshold);
        Value->SetStringField(TEXT("tickMode"), Entry.TickMode);
        Value->SetNumberField(TEXT("sourceIndex"), Entry.SourceIndex);
        Value->SetNumberField(TEXT("trackIndex"), Entry.TrackIndex);
        Value->SetObjectField(TEXT("payload"), Entry.Payload);
        return Value;
    }

    TSharedRef<FJsonObject> SyncMarkerToJson(const FAlsExportedSyncMarker& Marker)
    {
        const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
        Value->SetStringField(TEXT("stableMarkerId"), Marker.StableMarkerId);
        Value->SetStringField(TEXT("name"), Marker.Name);
        Value->SetNumberField(TEXT("timeSeconds"), Marker.TimeSeconds);
        Value->SetNumberField(TEXT("sourceIndex"), Marker.SourceIndex);
        Value->SetNumberField(TEXT("trackIndex"), Marker.TrackIndex);
        return Value;
    }

    bool FailCanonicalYawDuration(const FString& SequencePath, const FFrameRate& FrameRate, const int32 KeyCount,
        const double SequenceDuration, const double DataModelDuration, const double ComputedLastTime,
        const TCHAR* Reason, FString& OutError)
    {
        OutError = FString::Printf(TEXT("Invalid canonical root yaw duration: asset=%s keyCount=%d frameRateNumerator=%d frameRateDenominator=%d duration=%.17g sequenceDuration=%.17g computedLastTime=%.17g reason=%s."),
            *SequencePath, KeyCount, FrameRate.Numerator, FrameRate.Denominator, DataModelDuration,
            SequenceDuration, ComputedLastTime, Reason);
        return false;
    }

    bool ValidateCanonicalYawSampleDuration(const FString& SequencePath, const FFrameRate& FrameRate,
        const int32 KeyCount, const double SequenceDuration, const double DataModelDuration, FString& OutError)
    {
        const double FramesPerSecond = FrameRate.AsDecimal();
        const double ComputedLastTime = FMath::IsFinite(FramesPerSecond) && FramesPerSecond > 0.0 && KeyCount > 0
            ? static_cast<double>(KeyCount - 1) / FramesPerSecond
            : std::numeric_limits<double>::quiet_NaN();
        if (FrameRate.Numerator <= 0 || FrameRate.Denominator <= 0 || !FMath::IsFinite(FramesPerSecond) || FramesPerSecond <= 0.0)
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("invalid frame rate"), OutError);
        }
        if (KeyCount < 2)
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("insufficient key count"), OutError);
        }
        if (!FMath::IsFinite(SequenceDuration) || !FMath::IsFinite(DataModelDuration))
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("non-finite duration"), OutError);
        }
        if (SequenceDuration <= 0.0 || DataModelDuration <= 0.0)
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("non-positive duration"), OutError);
        }
        if (!FMath::IsNearlyEqual(SequenceDuration, DataModelDuration, CanonicalYawDurationToleranceSeconds))
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("sequence/data-model duration mismatch"), OutError);
        }
        if (!FMath::IsNearlyEqual(DataModelDuration, ComputedLastTime, CanonicalYawDurationToleranceSeconds))
        {
            return FailCanonicalYawDuration(SequencePath, FrameRate, KeyCount, SequenceDuration, DataModelDuration,
                ComputedLastTime, TEXT("duration/key-time mismatch"), OutError);
        }
        return true;
    }

    bool TryExtractCanonicalRootYawDegrees(const FString& SequencePath, const int32 FrameIndex, FQuat Rotation,
        double& OutYawDegrees, FString& OutError)
    {
        const double SizeSquared = Rotation.SizeSquared();
        const TCHAR* Reason = nullptr;
        if (Rotation.ContainsNaN() || !FMath::IsFinite(SizeSquared))
        {
            Reason = TEXT("non-finite quaternion");
        }
        else if (SizeSquared <= SMALL_NUMBER)
        {
            Reason = TEXT("zero-length quaternion");
        }
        if (Reason)
        {
            OutError = FString::Printf(TEXT("Invalid canonical root yaw quaternion: asset=%s frame=%d X=%.17g Y=%.17g Z=%.17g W=%.17g sizeSquared=%.17g reason=%s."),
                *SequencePath, FrameIndex, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W, SizeSquared, Reason);
            return false;
        }
        Rotation.Normalize();
        OutYawDegrees = Rotation.Rotator().Yaw;
        if (!FMath::IsFinite(OutYawDegrees))
        {
            OutError = FString::Printf(TEXT("Invalid canonical root yaw quaternion: asset=%s frame=%d X=%.17g Y=%.17g Z=%.17g W=%.17g sizeSquared=%.17g reason=non-finite yaw."),
                *SequencePath, FrameIndex, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W, SizeSquared);
            return false;
        }
        return true;
    }

    bool RequiresCanonicalRotationYawCurve(const FString& SequencePath)
    {
        static const TSet<FString> RequiredPaths = {
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_L90.ALS_CLF_Rotate_L90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_R90.ALS_CLF_Rotate_R90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L180.ALS_CLF_TurnIP_L180"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L90.ALS_CLF_TurnIP_L90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R180.ALS_CLF_TurnIP_R180"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R90.ALS_CLF_TurnIP_R90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_R90.ALS_N_Rotate_R90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L180.ALS_N_TurnIP_L180"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R180.ALS_N_TurnIP_R180"),
            TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R90.ALS_N_TurnIP_R90"),
        };
        return RequiredPaths.Contains(SequencePath);
    }

    bool HasCanonicalRotationYawCurve(const TArray<FAlsExportedFloatCurve>& Curves)
    {
        return Curves.ContainsByPredicate([](const FAlsExportedFloatCurve& Curve)
        {
            return Curve.CanonicalKind == CanonicalRotationYawKind;
        });
    }

    double ShortestSignedYawDeltaDegrees(double DeltaDegrees)
    {
        double Normalized = FMath::Fmod(DeltaDegrees, 360.0);
        if (Normalized <= -180.0)
        {
            Normalized += 360.0;
        }
        return Normalized > 180.0 ? Normalized - 360.0 : Normalized;
    }

    bool DeriveRotationYawCurve(const FString& SequencePath, const TArray<double>& TimeSeconds,
        const TArray<double>& WrappedYawDegrees, FAlsExportedFloatCurve& OutCurve, FString& OutError)
    {
        if (TimeSeconds.Num() != WrappedYawDegrees.Num() || TimeSeconds.Num() < 2)
        {
            OutError = FString::Printf(TEXT("Canonical root yaw requires at least two matching samples: asset=%s timeSamples=%d yawSamples=%d."),
                *SequencePath, TimeSeconds.Num(), WrappedYawDegrees.Num());
            return false;
        }

        TArray<double> UnwrappedYawDegrees;
        UnwrappedYawDegrees.SetNumUninitialized(WrappedYawDegrees.Num());
        for (int32 FrameIndex = 0; FrameIndex < WrappedYawDegrees.Num(); ++FrameIndex)
        {
            if (!FMath::IsFinite(TimeSeconds[FrameIndex]) || !FMath::IsFinite(WrappedYawDegrees[FrameIndex]))
            {
                OutError = FString::Printf(TEXT("Non-finite canonical root yaw sample: asset=%s frame=%d time=%.17g yawDegrees=%.17g."),
                    *SequencePath, FrameIndex, TimeSeconds[FrameIndex], WrappedYawDegrees[FrameIndex]);
                return false;
            }
            if (FrameIndex > 0 && TimeSeconds[FrameIndex] <= TimeSeconds[FrameIndex - 1])
            {
                OutError = FString::Printf(TEXT("Non-increasing canonical root yaw time: asset=%s frame=%d time=%.17g previousTime=%.17g."),
                    *SequencePath, FrameIndex, TimeSeconds[FrameIndex], TimeSeconds[FrameIndex - 1]);
                return false;
            }
            UnwrappedYawDegrees[FrameIndex] = FrameIndex == 0
                ? WrappedYawDegrees[FrameIndex]
                : UnwrappedYawDegrees[FrameIndex - 1] + ShortestSignedYawDeltaDegrees(
                    WrappedYawDegrees[FrameIndex] - WrappedYawDegrees[FrameIndex - 1]);
        }

        OutCurve = FAlsExportedFloatCurve();
        OutCurve.CanonicalKind = CanonicalRotationYawKind;
        OutCurve.SourceName = CanonicalRotationYawCurveName;
        OutCurve.SourceProvenance = TEXT("derived_root_track");
        OutCurve.PreInfinity = TEXT("Constant");
        OutCurve.PostInfinity = TEXT("Constant");
        for (int32 FrameIndex = 0; FrameIndex < UnwrappedYawDegrees.Num(); ++FrameIndex)
        {
            const int32 FirstIndex = FrameIndex == 0 ? 0 : FrameIndex - 1;
            const int32 LastIndex = FrameIndex == UnwrappedYawDegrees.Num() - 1
                ? UnwrappedYawDegrees.Num() - 1 : FrameIndex + 1;
            const double DerivativeRadiansPerSecond = (UnwrappedYawDegrees[LastIndex] - UnwrappedYawDegrees[FirstIndex]) /
                (TimeSeconds[LastIndex] - TimeSeconds[FirstIndex]) * (UE_DOUBLE_PI / 180.0);
            if (!FMath::IsFinite(DerivativeRadiansPerSecond))
            {
                OutError = FString::Printf(TEXT("Non-finite canonical root yaw derivative: asset=%s frame=%d time=%.17g value=%.17g."),
                    *SequencePath, FrameIndex, TimeSeconds[FrameIndex], DerivativeRadiansPerSecond);
                return false;
            }
            FAlsExportedFloatCurveKey& Key = OutCurve.Keys.AddDefaulted_GetRef();
            Key.TimeSeconds = TimeSeconds[FrameIndex];
            Key.Value = DerivativeRadiansPerSecond;
            Key.Interpolation = TEXT("Linear");
        }
        return true;
    }

    bool SortAndAssignCurveIds(const FString& SequencePath, TArray<FAlsExportedFloatCurve>& Curves, FString& OutError)
    {
        Curves.Sort([](const FAlsExportedFloatCurve& Left, const FAlsExportedFloatCurve& Right)
        {
            return Left.SourceName.Compare(Right.SourceName, ESearchCase::CaseSensitive) < 0;
        });
        for (int32 CurveIndex = 0; CurveIndex < Curves.Num(); ++CurveIndex)
        {
            if (CurveIndex > 0 && Curves[CurveIndex - 1].SourceName == Curves[CurveIndex].SourceName)
            {
                OutError = FString::Printf(TEXT("Duplicate exported float curve source name: asset=%s curve[%d]=%s."),
                    *SequencePath, CurveIndex, *Curves[CurveIndex].SourceName);
                return false;
            }
            Curves[CurveIndex].StableCurveId = CurveIndex;
        }
        return true;
    }

    bool TryAppendCanonicalRotationYawCurve(const UAnimSequence& Sequence, const FString& SequencePath,
        TArray<FAlsExportedFloatCurve>& OutCurves, FString& OutError)
    {
        if (!RequiresCanonicalRotationYawCurve(SequencePath))
        {
            return true;
        }
        const IAnimationDataModel* DataModel = Sequence.GetDataModel();
        const USkeleton* Skeleton = Sequence.GetSkeleton();
        if (!DataModel || !Skeleton)
        {
            OutError = FString::Printf(TEXT("Canonical root yaw source is unavailable: asset=%s dataModel=%d skeleton=%d."),
                *SequencePath, DataModel != nullptr, Skeleton != nullptr);
            return false;
        }
        const FReferenceSkeleton& ReferenceSkeleton = Skeleton->GetReferenceSkeleton();
        if (ReferenceSkeleton.GetNum() < 1)
        {
            OutError = FString::Printf(TEXT("Canonical root yaw root bone is unavailable: asset=%s."), *SequencePath);
            return false;
        }
        const FName RootBoneName = ReferenceSkeleton.GetBoneName(0);
        if (!DataModel->IsValidBoneTrackName(RootBoneName))
        {
            OutError = FString::Printf(TEXT("Canonical root yaw root track is missing: asset=%s rootBone=%s."),
                *SequencePath, *RootBoneName.ToString());
            return false;
        }
        const FFrameRate FrameRate = DataModel->GetFrameRate();
        const double FramesPerSecond = FrameRate.AsDecimal();
        const int32 FrameCount = DataModel->GetNumberOfKeys();
        const double SequenceDuration = Sequence.GetPlayLength();
        const double DataModelDuration = DataModel->GetPlayLength();
        if (!ValidateCanonicalYawSampleDuration(SequencePath, FrameRate, FrameCount,
            SequenceDuration, DataModelDuration, OutError))
        {
            return false;
        }
        TArray<FTransform> RootTransforms;
        DataModel->GetBoneTrackTransforms(RootBoneName, RootTransforms);
        if (RootTransforms.Num() != FrameCount)
        {
            OutError = FString::Printf(TEXT("Canonical root yaw root track sample count mismatch: asset=%s rootBone=%s frameCount=%d trackSamples=%d."),
                *SequencePath, *RootBoneName.ToString(), FrameCount, RootTransforms.Num());
            return false;
        }

        TArray<double> Times;
        TArray<double> WrappedYawDegrees;
        Times.Reserve(FrameCount);
        WrappedYawDegrees.Reserve(FrameCount);
        for (int32 FrameIndex = 0; FrameIndex < FrameCount; ++FrameIndex)
        {
            double YawDegrees = 0.0;
            if (!TryExtractCanonicalRootYawDegrees(SequencePath, FrameIndex, RootTransforms[FrameIndex].GetRotation(),
                YawDegrees, OutError))
            {
                return false;
            }
            Times.Add(static_cast<double>(FrameIndex) / FramesPerSecond);
            WrappedYawDegrees.Add(YawDegrees);
        }
        FAlsExportedFloatCurve DerivedCurve;
        if (!DeriveRotationYawCurve(SequencePath, Times, WrappedYawDegrees, DerivedCurve, OutError))
        {
            return false;
        }
        OutCurves.Add(MoveTemp(DerivedCurve));
        return true;
    }

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

    bool TryExportCurveKey(const FString& SequencePath, const int32 CurveIndex, const FString& CurveName,
        const int32 KeyIndex, const FRichCurveKey& Key, FAlsExportedFloatCurveKey& OutKey, FString& OutError)
    {
        const int32 TangentWeightMode = static_cast<int32>(Key.TangentWeightMode.GetValue());
        if (Key.TangentWeightMode != RCTWM_WeightedNone)
        {
            OutError = FString::Printf(TEXT("Unsupported weighted float curve key: asset=%s curve[%d]=%s key[%d] time=%.17g tangentWeightMode=%d arriveTangentWeight=%.17g leaveTangentWeight=%.17g."),
                *SequencePath, CurveIndex, *CurveName, KeyIndex, Key.Time, TangentWeightMode,
                Key.ArriveTangentWeight, Key.LeaveTangentWeight);
            return false;
        }

        OutKey.TimeSeconds = Key.Time;
        OutKey.Value = Key.Value;
        OutKey.ArriveTangent = Key.ArriveTangent;
        OutKey.LeaveTangent = Key.LeaveTangent;
        if (!TryMapInterpolation(Key.InterpMode, OutKey.Interpolation))
        {
            OutError = FString::Printf(TEXT("Unsupported float curve interpolation: asset=%s curve[%d]=%s key[%d] time=%.17g interpolation=%d."),
                *SequencePath, CurveIndex, *CurveName, KeyIndex, Key.Time,
                static_cast<int32>(Key.InterpMode.GetValue()));
            return false;
        }
        if (!IsFiniteCurveKey(OutKey))
        {
            OutError = FString::Printf(TEXT("Non-finite float curve key: asset=%s curve[%d]=%s key[%d] time=%.17g value=%.17g arriveTangent=%.17g leaveTangent=%.17g."),
                *SequencePath, CurveIndex, *CurveName, KeyIndex, Key.Time, Key.Value,
                Key.ArriveTangent, Key.LeaveTangent);
            return false;
        }
        return true;
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
    const FString SequencePath = Asset.AssetData.GetObjectPathString();

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
        const TArray<FFloatCurve>& FloatCurves = DataModel->GetFloatCurves();
        for (int32 CurveIndex = 0; CurveIndex < FloatCurves.Num(); ++CurveIndex)
        {
            const FFloatCurve& Curve = FloatCurves[CurveIndex];
            FAlsExportedFloatCurve ExportedCurve;
            ExportedCurve.SourceName = Curve.GetName().ToString();
            if (!TryMapInfinity(Curve.FloatCurve.PreInfinityExtrap, ExportedCurve.PreInfinity) ||
                !TryMapInfinity(Curve.FloatCurve.PostInfinityExtrap, ExportedCurve.PostInfinity))
            {
                OutError = FString::Printf(TEXT("Unsupported float curve infinity mode: asset=%s curve[%d]=%s preInfinity=%d postInfinity=%d."),
                    *SequencePath, CurveIndex, *ExportedCurve.SourceName,
                    static_cast<int32>(Curve.FloatCurve.PreInfinityExtrap),
                    static_cast<int32>(Curve.FloatCurve.PostInfinityExtrap));
                return false;
            }

            const TArray<FRichCurveKey>& CurveKeys = Curve.FloatCurve.GetConstRefOfKeys();
            for (int32 KeyIndex = 0; KeyIndex < CurveKeys.Num(); ++KeyIndex)
            {
                const FRichCurveKey& Key = CurveKeys[KeyIndex];
                FAlsExportedFloatCurveKey ExportedKey;
                if (!TryExportCurveKey(SequencePath, CurveIndex, ExportedCurve.SourceName, KeyIndex,
                    Key, ExportedKey, OutError))
                {
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
                    OutError = FString::Printf(TEXT("Duplicate float curve key time: asset=%s curve[%d]=%s key[%d] time=%.17g previousTime=%.17g."),
                        *SequencePath, CurveIndex, *ExportedCurve.SourceName, KeyIndex,
                        ExportedCurve.Keys[KeyIndex].TimeSeconds, ExportedCurve.Keys[KeyIndex - 1].TimeSeconds);
                    return false;
                }
            }
            OutCurves.Add(MoveTemp(ExportedCurve));
        }
    }
    if (!TryAppendCanonicalRotationYawCurve(*Sequence, SequencePath, OutCurves, OutError) ||
        !SortAndAssignCurveIds(SequencePath, OutCurves, OutError))
    {
        return false;
    }
    if (HasCanonicalRotationYawCurve(OutCurves))
    {
        // This is raw UE root-bone FRotator::Yaw (positive Z yaw); profile conversion signs are deferred to P4 runtime.
        OutMetadata->SetStringField(TEXT("canonicalRotationYawSourceConvention"), TEXT("ue_root_bone_rotator_yaw_degrees_z_up"));
        OutMetadata->SetStringField(TEXT("canonicalRotationYawProfileSignProvenance"), TEXT("runtime_profile_sign_pending"));
    }

    return ReadTimeline(*Sequence, Asset.Id, OutMetadata, OutError);
}

bool FAlsAnimationMetadataReader::ReadTimeline(const UAnimSequenceBase& Sequence, const FString& AssetStableId,
    TSharedRef<FJsonObject>& OutMetadata, FString& OutError)
{
    TArray<FAlsExportedTimelineEntry> Entries;
    TSet<int32> SourceIndices;
    TSet<FString> StableEventIds;
    for (int32 SourceIndex = 0; SourceIndex < Sequence.Notifies.Num(); ++SourceIndex)
    {
        if (SourceIndices.Contains(SourceIndex))
        {
            OutError = FString::Printf(TEXT("Duplicate notify source index: asset=%s sourceIndex=%d."),
                *AssetStableId, SourceIndex);
            return false;
        }
        SourceIndices.Add(SourceIndex);
        FAlsExportedTimelineEntry Entry;
        if (!FAlsNotifyClassRegistry::Export(Sequence.Notifies[SourceIndex], AssetStableId,
            SourceIndex, Entry, OutError))
        {
            return false;
        }
        if (StableEventIds.Contains(Entry.StableEventId))
        {
            OutError = FString::Printf(TEXT("Duplicate notify stable ID: asset=%s sourceIndex=%d id=%s."),
                *AssetStableId, SourceIndex, *Entry.StableEventId);
            return false;
        }
        StableEventIds.Add(Entry.StableEventId);
        Entries.Add(MoveTemp(Entry));
    }
    Entries.Sort(TimelineEntryLess);
    TArray<TSharedPtr<FJsonValue>> Timeline;
    Timeline.Reserve(Entries.Num());
    for (const FAlsExportedTimelineEntry& Entry : Entries)
    {
        Timeline.Add(MakeShared<FJsonValueObject>(TimelineEntryToJson(Entry)));
    }
    OutMetadata->SetArrayField(TEXT("timeline"), Timeline);

    TArray<FAnimSyncMarker> AuthoredMarkers;
    if (const UAnimSequence* AnimationSequence = Cast<UAnimSequence>(&Sequence))
    {
        AuthoredMarkers = AnimationSequence->AuthoredSyncMarkers;
    }
    else if (const UAnimMontage* Montage = Cast<UAnimMontage>(&Sequence))
    {
        AuthoredMarkers = Montage->MarkerData.AuthoredSyncMarkers;
    }

    TArray<FAlsExportedSyncMarker> Markers;
    TSet<int32> MarkerSourceIndices;
    TSet<FString> StableMarkerIds;
    for (int32 SourceIndex = 0; SourceIndex < AuthoredMarkers.Num(); ++SourceIndex)
    {
        const FAnimSyncMarker& AuthoredMarker = AuthoredMarkers[SourceIndex];
        FAlsExportedSyncMarker Marker;
        Marker.Name = AuthoredMarker.MarkerName.ToString();
        Marker.TimeSeconds = AuthoredMarker.Time;
        Marker.SourceIndex = SourceIndex;
#if WITH_EDITORONLY_DATA
        Marker.TrackIndex = AuthoredMarker.TrackIndex;
#else
        Marker.TrackIndex = 0;
#endif
        if (MarkerSourceIndices.Contains(SourceIndex) || Marker.Name.IsEmpty() || Marker.Name == TEXT("None") ||
            !FMath::IsFinite(Marker.TimeSeconds) || Marker.TimeSeconds < 0.0 || Marker.TrackIndex < 0)
        {
            OutError = FString::Printf(TEXT("Invalid sync marker: asset=%s sourceIndex=%d name=%s time=%.17g track=%d."),
                *AssetStableId, SourceIndex, *Marker.Name, Marker.TimeSeconds, Marker.TrackIndex);
            return false;
        }
        MarkerSourceIndices.Add(SourceIndex);
        Marker.StableMarkerId = CreateTimelineSha1(FString::Printf(TEXT("%s|marker|%d|%s"),
            *AssetStableId, SourceIndex, *Marker.Name));
        if (StableMarkerIds.Contains(Marker.StableMarkerId))
        {
            OutError = FString::Printf(TEXT("Duplicate sync marker stable ID: asset=%s sourceIndex=%d id=%s."),
                *AssetStableId, SourceIndex, *Marker.StableMarkerId);
            return false;
        }
        StableMarkerIds.Add(Marker.StableMarkerId);
        Markers.Add(MoveTemp(Marker));
    }
    Markers.Sort(SyncMarkerLess);
    TArray<TSharedPtr<FJsonValue>> SyncMarkers;
    SyncMarkers.Reserve(Markers.Num());
    for (const FAlsExportedSyncMarker& Marker : Markers)
    {
        SyncMarkers.Add(MakeShared<FJsonValueObject>(SyncMarkerToJson(Marker)));
    }
    OutMetadata->SetArrayField(TEXT("syncMarkers"), SyncMarkers);
    return true;
}

bool FAlsAnimationMetadataReader::RunTimelineSelfTest(int32& OutCaseCount, FString& OutError)
{
    OutCaseCount = 0;
    OutError.Reset();
    UAnimNotify_ResetDynamics* NotifyObject = NewObject<UAnimNotify_ResetDynamics>();
    if (!NotifyObject)
    {
        OutError = TEXT("Unable to construct timeline self-test notify.");
        return false;
    }

    FAnimNotifyEvent NotifyEvent;
    NotifyEvent.Notify = NotifyObject;
    NotifyEvent.NotifyName = TEXT("TimelineSelfTest");
    NotifyEvent.SetTime(0.25f);
    NotifyEvent.TriggerWeightThreshold = 0.5f;
    NotifyEvent.TrackIndex = 2;
    const FString AssetStableId = TEXT("timeline-self-test-asset");
    for (const EMontageNotifyTickType::Type TickMode :
        {EMontageNotifyTickType::Queued, EMontageNotifyTickType::BranchingPoint})
    {
        NotifyEvent.MontageTickType = TickMode;
        FAlsExportedTimelineEntry Entry;
        if (!FAlsNotifyClassRegistry::Export(NotifyEvent, AssetStableId, OutCaseCount, Entry, OutError))
        {
            return false;
        }
        const FString ExpectedTickMode = TickMode == EMontageNotifyTickType::Queued
            ? TEXT("Queued")
            : TEXT("BranchingPoint");
        if (Entry.TickMode != ExpectedTickMode || Entry.Kind != TEXT("Generic") ||
            Entry.StableEventId.Len() != FSHA1::DigestSize * 2 || !Entry.Payload.IsValid() || Entry.Payload->Values.Num() != 0)
        {
            OutError = FString::Printf(TEXT("Timeline self-test mismatch: case=%d tick=%s kind=%s id=%s payloadFields=%d."),
                OutCaseCount, *Entry.TickMode, *Entry.Kind, *Entry.StableEventId,
                Entry.Payload.IsValid() ? Entry.Payload->Values.Num() : INDEX_NONE);
            return false;
        }
        ++OutCaseCount;
    }
    return true;
}

bool FAlsAnimationMetadataReader::RunCurveKeySelfTest(int32& OutCaseCount, FString& OutError)
{
    constexpr int32 ExpectedCaseCount = 16;
    constexpr int32 CurveIndex = 7;
    constexpr int32 KeyIndex = 3;
    constexpr double ExpectedRadiansPerDegree = 0.017453292519943295769236907684886;
    const FString SequencePath = TEXT("/Game/AlsGodotExporterSelfTest/CurveSequence.CurveSequence");
    const FString CurveName = TEXT("CurveSelfTest");
    const double NaN = std::numeric_limits<double>::quiet_NaN();
    OutCaseCount = 0;
    OutError.Reset();

    {
        FRichCurveKey Key;
        Key.Time = 0.25f;
        Key.Value = 2.5f;
        Key.InterpMode = RCIM_Cubic;
        Key.ArriveTangent = -1.25f;
        Key.LeaveTangent = 3.5f;
        FAlsExportedFloatCurveKey ExportedKey;
        FString CaseError;
        if (!TryExportCurveKey(SequencePath, CurveIndex, CurveName, KeyIndex, Key, ExportedKey, CaseError) || !CaseError.IsEmpty() ||
            ExportedKey.TimeSeconds != Key.Time || ExportedKey.Value != Key.Value || ExportedKey.Interpolation != TEXT("Cubic") ||
            ExportedKey.ArriveTangent != Key.ArriveTangent || ExportedKey.LeaveTangent != Key.LeaveTangent)
        {
            OutError = FString::Printf(TEXT("Curve export self-test valid cubic case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FRichCurveKey Key;
        Key.Time = 0.25f;
        Key.Value = 2.5f;
        Key.InterpMode = RCIM_Cubic;
        Key.TangentWeightMode = RCTWM_WeightedBoth;
        Key.ArriveTangentWeight = 0.75f;
        Key.LeaveTangentWeight = 0.5f;
        FAlsExportedFloatCurveKey ExportedKey;
        FString CaseError;
        if (TryExportCurveKey(SequencePath, CurveIndex, CurveName, KeyIndex, Key, ExportedKey, CaseError) ||
            !CaseError.Contains(TEXT("asset=/Game/AlsGodotExporterSelfTest/CurveSequence.CurveSequence")) ||
            !CaseError.Contains(TEXT("curve[7]=CurveSelfTest")) || !CaseError.Contains(TEXT("key[3] time=0.25")) ||
            !CaseError.Contains(TEXT("tangentWeightMode=3")) || !CaseError.Contains(TEXT("arriveTangentWeight=0.75")) ||
            !CaseError.Contains(TEXT("leaveTangentWeight=0.5")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test weighted cubic case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FRichCurveKey Key;
        Key.InterpMode = static_cast<ERichCurveInterpMode>(255);
        FAlsExportedFloatCurveKey ExportedKey;
        FString CaseError;
        if (TryExportCurveKey(SequencePath, CurveIndex, CurveName, KeyIndex, Key, ExportedKey, CaseError) || CaseError.IsEmpty())
        {
            OutError = FString::Printf(TEXT("Curve export self-test invalid interpolation case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FRichCurveKey Key;
        Key.InterpMode = RCIM_Cubic;
        Key.Value = std::numeric_limits<float>::infinity();
        Key.ArriveTangent = std::numeric_limits<float>::infinity();
        FAlsExportedFloatCurveKey ExportedKey;
        FString CaseError;
        if (TryExportCurveKey(SequencePath, CurveIndex, CurveName, KeyIndex, Key, ExportedKey, CaseError) || CaseError.IsEmpty())
        {
            OutError = FString::Printf(TEXT("Curve export self-test non-finite value/tangent case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FAlsExportedFloatCurve CanonicalCurve;
        FString CaseError;
        if (!DeriveRotationYawCurve(SequencePath, { 0.0, 0.1, 0.2, 0.3 }, { 170.0, 179.0, -179.0, -170.0 }, CanonicalCurve, CaseError) ||
            !CaseError.IsEmpty() || CanonicalCurve.CanonicalKind != CanonicalRotationYawKind ||
            CanonicalCurve.SourceName != CanonicalRotationYawCurveName || CanonicalCurve.SourceProvenance != TEXT("derived_root_track") ||
            CanonicalCurve.PreInfinity != TEXT("Constant") || CanonicalCurve.PostInfinity != TEXT("Constant") || CanonicalCurve.Keys.Num() != 4 ||
            !FMath::IsNearlyEqual(CanonicalCurve.Keys[0].Value, 90.0 * ExpectedRadiansPerDegree, 1e-12) ||
            !FMath::IsNearlyEqual(CanonicalCurve.Keys[1].Value, 55.0 * ExpectedRadiansPerDegree, 1e-12) ||
            !FMath::IsNearlyEqual(CanonicalCurve.Keys[2].Value, 55.0 * ExpectedRadiansPerDegree, 1e-12) ||
            !FMath::IsNearlyEqual(CanonicalCurve.Keys[3].Value, 90.0 * ExpectedRadiansPerDegree, 1e-12))
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical unwrap/derivative case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FAlsExportedFloatCurve InvalidCurve;
        FString CaseError;
        if (DeriveRotationYawCurve(SequencePath, { 0.0, 0.0 }, { 0.0, 1.0 }, InvalidCurve, CaseError) ||
            !CaseError.Contains(TEXT("Non-increasing canonical root yaw time")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test invalid time case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FAlsExportedFloatCurve InvalidCurve;
        FString CaseError;
        if (DeriveRotationYawCurve(SequencePath, { 0.0, 0.1 }, { 0.0, NaN }, InvalidCurve, CaseError) ||
            !CaseError.Contains(TEXT("Non-finite canonical root yaw sample")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test non-finite root yaw case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FAlsExportedFloatCurve CanonicalCurve;
        FString CaseError;
        if (!DeriveRotationYawCurve(SequencePath, { 0.0, 0.1 }, { 0.0, 9.0 }, CanonicalCurve, CaseError) || !CaseError.IsEmpty())
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical append preparation failed: %s"), *CaseError);
            return false;
        }
        FAlsExportedFloatCurve RawCurve;
        RawCurve.SourceName = TEXT("RawCurve");
        RawCurve.Keys.AddDefaulted_GetRef().Value = 4.0;
        TArray<FAlsExportedFloatCurve> Curves = { RawCurve, CanonicalCurve };
        CaseError.Reset();
        if (!SortAndAssignCurveIds(SequencePath, Curves, CaseError) || !CaseError.IsEmpty() || Curves.Num() != 2 ||
            Curves[0].SourceName != TEXT("RawCurve") || Curves[0].StableCurveId != 0 || Curves[0].Keys[0].Value != 4.0 ||
            Curves[1].SourceName != CanonicalRotationYawCurveName || Curves[1].StableCurveId != 1)
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical append/raw preservation/sort case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FAlsExportedFloatCurve FirstCanonicalCurve;
        FirstCanonicalCurve.SourceName = CanonicalRotationYawCurveName;
        FirstCanonicalCurve.CanonicalKind = CanonicalRotationYawKind;
        FAlsExportedFloatCurve DuplicateCanonicalCurve;
        DuplicateCanonicalCurve.SourceName = CanonicalRotationYawCurveName;
        DuplicateCanonicalCurve.CanonicalKind = CanonicalRotationYawKind;
        TArray<FAlsExportedFloatCurve> Curves = { FirstCanonicalCurve, DuplicateCanonicalCurve };
        FString CaseError;
        if (SortAndAssignCurveIds(SequencePath, Curves, CaseError) || !CaseError.Contains(TEXT("Duplicate exported float curve source name")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical uniqueness case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FString CaseError;
        if (!ValidateCanonicalYawSampleDuration(SequencePath, FFrameRate(30, 1), 61, 2.0, 2.0, CaseError) || !CaseError.IsEmpty())
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical duration valid case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FString CaseError;
        if (ValidateCanonicalYawSampleDuration(SequencePath, FFrameRate(30, 1), 61, 0.0, 0.0, CaseError) ||
            !CaseError.Contains(TEXT("asset=/Game/AlsGodotExporterSelfTest/CurveSequence.CurveSequence")) || !CaseError.Contains(TEXT("keyCount=61")) ||
            !CaseError.Contains(TEXT("frameRateNumerator=30")) || !CaseError.Contains(TEXT("frameRateDenominator=1")) ||
            !CaseError.Contains(TEXT("reason=non-positive duration")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical duration zero case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FString CaseError;
        if (ValidateCanonicalYawSampleDuration(SequencePath, FFrameRate(30, 1), 61, NaN, NaN, CaseError) ||
            !CaseError.Contains(TEXT("duration=nan")) || !CaseError.Contains(TEXT("reason=non-finite duration")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical duration non-finite case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        FString CaseError;
        if (ValidateCanonicalYawSampleDuration(SequencePath, FFrameRate(30, 1), 61, 1.5, 1.5, CaseError) ||
            !CaseError.Contains(TEXT("computedLastTime=2")) || !CaseError.Contains(TEXT("reason=duration/key-time mismatch")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test canonical duration mismatch case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        double ExtractedYawDegrees = 0.0;
        FString CaseError;
        if (!TryExtractCanonicalRootYawDegrees(SequencePath, 3, FQuat(0.0, 0.0, 0.0, 2.0), ExtractedYawDegrees, CaseError) ||
            !CaseError.IsEmpty() || !FMath::IsNearlyEqual(ExtractedYawDegrees, 0.0, 1e-12))
        {
            OutError = FString::Printf(TEXT("Curve export self-test non-unit root quaternion case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        double ExtractedYawDegrees = 0.0;
        FString CaseError;
        if (TryExtractCanonicalRootYawDegrees(SequencePath, 4, FQuat(NaN, 0.0, 0.0, 1.0), ExtractedYawDegrees, CaseError) ||
            !CaseError.Contains(TEXT("asset=/Game/AlsGodotExporterSelfTest/CurveSequence.CurveSequence")) || !CaseError.Contains(TEXT("frame=4")) ||
            !CaseError.Contains(TEXT("X=nan")) || !CaseError.Contains(TEXT("Y=0")) || !CaseError.Contains(TEXT("Z=0")) ||
            !CaseError.Contains(TEXT("W=1")) || !CaseError.Contains(TEXT("sizeSquared=nan")) || !CaseError.Contains(TEXT("reason=non-finite quaternion")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test non-finite root quaternion case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }
    {
        double ExtractedYawDegrees = 0.0;
        FString CaseError;
        if (TryExtractCanonicalRootYawDegrees(SequencePath, 5, FQuat::Identity * 0.0, ExtractedYawDegrees, CaseError) ||
            !CaseError.Contains(TEXT("frame=5")) || !CaseError.Contains(TEXT("X=0")) || !CaseError.Contains(TEXT("Y=0")) ||
            !CaseError.Contains(TEXT("Z=0")) || !CaseError.Contains(TEXT("W=0")) || !CaseError.Contains(TEXT("sizeSquared=0")) ||
            !CaseError.Contains(TEXT("reason=zero-length quaternion")))
        {
            OutError = FString::Printf(TEXT("Curve export self-test zero-length root quaternion case failed: %s"), *CaseError);
            return false;
        }
        ++OutCaseCount;
    }

    if (OutCaseCount != ExpectedCaseCount)
    {
        OutError = FString::Printf(TEXT("Curve export self-test case count mismatch: expected=%d actual=%d."), ExpectedCaseCount, OutCaseCount);
        return false;
    }
    OutError.Reset();
    return true;
}
