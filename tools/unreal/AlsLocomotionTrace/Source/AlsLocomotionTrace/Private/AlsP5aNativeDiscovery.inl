// Included inside AlsLocomotionTrace after the existing identity and JSON helpers.
// This document is diagnostic evidence, never a formal trace or canonical input.
void SetP5aDiscoveryFloat(const TSharedRef<FJsonObject>& Object, const TCHAR* Name, const float Value)
{
    if (!FMath::IsFinite(Value))
    {
        Object->SetField(Name, MakeShared<FJsonValueNull>());
        return;
    }
    char Buffer[64]{};
    const auto Conversion{std::to_chars(Buffer, Buffer + UE_ARRAY_COUNT(Buffer) - 1, static_cast<double>(Value))};
    check(Conversion.ec == std::errc{});
    *Conversion.ptr = '\0';
    Object->SetField(Name, MakeShared<FJsonValueNumberString>(FString{ANSI_TO_TCHAR(Buffer)}));
    uint32 Bits{0};
    FMemory::Memcpy(&Bits, &Value, sizeof Bits);
    Object->SetStringField(FString{Name} + TEXT("Bits"), FString::Printf(TEXT("%08x"), Bits));
}

TSharedRef<FJsonObject> P5aDiscoveryMontageSnapshot(
    const FAnimMontageInstance* Instance, const UAnimMontage* OriginalMontage)
{
    const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
    Value->SetBoolField(TEXT("instancePresent"), Instance != nullptr);
    Value->SetStringField(TEXT("montageObjectPath"), GetPathNameSafe(OriginalMontage));
    if (Instance == nullptr) return Value;
    Value->SetNumberField(TEXT("montageInstanceId"), Instance->GetInstanceID());
    Value->SetBoolField(TEXT("instanceValid"), Instance->IsValid());
    Value->SetBoolField(TEXT("playing"), Instance->IsPlaying());
    Value->SetBoolField(TEXT("stopped"), Instance->IsStopped());
    Value->SetBoolField(TEXT("blendComplete"), Instance->GetBlend().IsComplete());
    Value->SetBoolField(TEXT("autoBlendOut"), Instance->bEnableAutoBlendOut);
    SetP5aDiscoveryFloat(Value, TEXT("positionSeconds"), Instance->GetPosition());
    SetP5aDiscoveryFloat(Value, TEXT("previousPositionSeconds"), Instance->GetPreviousPosition());
    SetP5aDiscoveryFloat(Value, TEXT("playRate"), Instance->GetPlayRate());
    SetP5aDiscoveryFloat(Value, TEXT("blendTimeSeconds"), Instance->GetBlend().GetBlendTime());
    SetP5aDiscoveryFloat(Value, TEXT("blendTimeRemainingSeconds"), Instance->GetBlend().GetBlendTimeRemaining());
    SetP5aDiscoveryFloat(Value, TEXT("alpha"), Instance->GetBlend().GetAlpha());
    SetP5aDiscoveryFloat(Value, TEXT("weight"), Instance->GetWeight());
    SetP5aDiscoveryFloat(Value, TEXT("desiredWeight"), Instance->GetDesiredWeight());
    SetP5aDiscoveryFloat(Value, TEXT("defaultBlendTimeMultiplier"), Instance->DefaultBlendTimeMultiplier);
    Value->SetNumberField(TEXT("blendOption"), static_cast<int32>(Instance->GetBlend().GetBlendOption()));
    Value->SetField(TEXT("remainingLastSectionPlaybackTimeSeconds"), MakeShared<FJsonValueNull>());
    Value->SetStringField(TEXT("remainingTimeMethod"), TEXT("unavailable: montage substepper is private"));
    if (IsValid(OriginalMontage))
    {
        const int32 SectionIndex{OriginalMontage->GetSectionIndexFromPosition(Instance->GetPosition())};
        Value->SetNumberField(TEXT("sectionIndex"), SectionIndex);
        Value->SetStringField(TEXT("sectionName"), OriginalMontage->GetSectionName(SectionIndex).ToString());
        if (SectionIndex != INDEX_NONE && Instance->GetNextSectionID(SectionIndex) == INDEX_NONE &&
            Instance->GetPlayRate() == 1.0f && OriginalMontage->RateScale == 1.0f)
        {
            float SectionStart{0.0f};
            float SectionEnd{0.0f};
            OriginalMontage->GetSectionStartAndEndTime(SectionIndex, SectionStart, SectionEnd);
            SetP5aDiscoveryFloat(Value, TEXT("sectionEndSeconds"), SectionEnd);
            SetP5aDiscoveryFloat(Value, TEXT("remainingLastSectionPlaybackTimeSeconds"),
                FMath::Abs(SectionEnd - Instance->GetPosition()));
            Value->SetStringField(TEXT("remainingTimeMethod"),
                TEXT("public last-section distance at unit rate; no time-stretch at default rate"));
        }
    }
    return Value;
}

struct FP5aNativeDiscovery
{
    TMap<FString, TSharedPtr<FJsonObject>> Assets;
    TArray<TSharedPtr<FJsonValue>> Cases;
    int32 MeasuredFrameCount{0};
    const FP5aNativeAuxiliaryInventory* FormalAuxiliary{nullptr};

    bool ObserveAsset(UAnimationAsset* Asset, const TArray<FP5aSourceDefinition>& Sources)
    {
        if (!IsValid(Asset)) return false;
        if (Asset->GetOutermost() == GetTransientPackage())
        {
            if (FormalAuxiliary == nullptr) return true;
            // Only the native Transition slot's dynamic single-sequence wrapper is supported.
            const UAnimMontage* Montage{Cast<UAnimMontage>(Asset)};
            if (Montage == nullptr || Montage->GetClass() != UAnimMontage::StaticClass() ||
                Montage->Notifies.Num() != 0 || Montage->SlotAnimTracks.Num() != 1 ||
                Montage->SlotAnimTracks[0].SlotName != UAlsConstants::TransitionSlotName() ||
                Montage->SlotAnimTracks[0].AnimTrack.AnimSegments.Num() != 1) return false;
            const FAnimSegment& Segment{Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0]};
            UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(Segment.GetAnimReference())};
            if (!IsValid(Sequence) || Sequence->IsA<UAnimMontage>() ||
                Sequence->GetOutermost() == GetTransientPackage() ||
                Segment.StartPos != 0.0f || Segment.AnimStartTime != 0.0f ||
                Segment.AnimEndTime != Sequence->GetPlayLength() ||
                Segment.AnimEndTime <= 0.0f || Segment.AnimPlayRate != 1.0f ||
                Segment.LoopingCount != 1 || Montage->GetSkeleton() != Sequence->GetSkeleton()) return false;
            return ObserveAsset(Sequence, Sources);
        }
        const FString Path{Asset->GetPathName()};
        const bool bMapped{Sources.ContainsByPredicate([Asset](const FP5aSourceDefinition& Source)
        {
            return Source.NativeAsset == Asset || Source.NativeAssets.Contains(Asset);
        })};
        if (FormalAuxiliary != nullptr && !bMapped && !FormalAuxiliary->Contains(Asset))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A runtime asset closure rejected unlisted asset=%s"), *Path);
            return false;
        }
        if (Assets.Contains(Path)) return true;
        const FString Hash{CreateP5aPackageSha256(*Asset)};
        if (Hash.IsEmpty()) return false;
        const FString AssetId{CreateP5aAssetStableId(Path)};
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("assetObjectPath"), Path);
        Value->SetStringField(TEXT("assetClassPath"), Asset->GetClass()->GetPathName());
        Value->SetStringField(TEXT("assetStableId"), AssetId);
        Value->SetStringField(TEXT("assetPackageSha256"), Hash);
        Value->SetBoolField(TEXT("inFrozenNativeInventory"), bMapped);
        TArray<TSharedPtr<FJsonValue>> Notifies;
        if (const UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(Asset)})
        {
            SetP5aDiscoveryFloat(Value, TEXT("durationSeconds"), Sequence->GetPlayLength());
            SetP5aDiscoveryFloat(Value, TEXT("rateScale"), Sequence->RateScale);
            for (int32 Index{0}; Index < Sequence->Notifies.Num(); ++Index)
                Notifies.Add(MakeShared<FJsonValueObject>(NotifyIdentity(*Sequence, Sequence->Notifies[Index], Index)));
        }
        Value->SetArrayField(TEXT("notifies"), Notifies);
        if (const UAnimMontage* Montage{Cast<UAnimMontage>(Asset)})
        {
            const TSharedRef<FJsonObject> Settings{MakeShared<FJsonObject>()};
            Settings->SetBoolField(TEXT("autoBlendOut"), Montage->bEnableAutoBlendOut);
            SetP5aDiscoveryFloat(Settings, TEXT("blendOutTriggerTimeSeconds"), Montage->BlendOutTriggerTime);
            SetP5aDiscoveryFloat(Settings, TEXT("blendInTimeSeconds"), Montage->BlendIn.GetBlendTime());
            SetP5aDiscoveryFloat(Settings, TEXT("blendOutTimeSeconds"), Montage->BlendOut.GetBlendTime());
            Settings->SetNumberField(TEXT("blendOutOption"), static_cast<int32>(Montage->BlendOut.GetBlendOption()));
            Settings->SetNumberField(TEXT("blendOutMode"), static_cast<int32>(Montage->BlendModeOut));
            Settings->SetStringField(TEXT("blendOutCustomCurvePath"), GetPathNameSafe(Montage->BlendOut.GetCustomCurve()));
            Value->SetObjectField(TEXT("montageSettings"), Settings);
        }
        Assets.Add(Path, Value);
        return true;
    }

    static TSharedRef<FJsonObject> NotifyIdentity(const UAnimSequenceBase& Asset, const FAnimNotifyEvent& Notify, int32 Index)
    {
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        const UObject* NotifyObject{IsValid(Notify.NotifyStateClass) ? static_cast<const UObject*>(Notify.NotifyStateClass.Get())
            : static_cast<const UObject*>(Notify.Notify.Get())};
        const FString ClassPath{IsValid(NotifyObject) ? NotifyObject->GetClass()->GetPathName() : FString{}};
        Value->SetStringField(TEXT("assetObjectPath"), Asset.GetPathName());
        Value->SetStringField(TEXT("notifyClassPath"), ClassPath);
        Value->SetStringField(TEXT("notifyObjectPath"), GetPathNameSafe(NotifyObject));
        Value->SetStringField(TEXT("notifyName"), Notify.NotifyName.ToString());
        Value->SetStringField(TEXT("eventGuid"), Notify.Guid.ToString(EGuidFormats::Digits).ToLower());
        Value->SetStringField(TEXT("stableEventId"), Index != INDEX_NONE
            ? CreateP5aEventStableId(CreateP5aAssetStableId(Asset.GetPathName()), Index, ClassPath) : FString{});
        Value->SetNumberField(TEXT("sourceIndex"), Index);
        Value->SetNumberField(TEXT("trackIndex"), Notify.TrackIndex);
        Value->SetBoolField(TEXT("isState"), IsValid(Notify.NotifyStateClass));
        Value->SetBoolField(TEXT("isBranchingPoint"), Notify.IsBranchingPoint());
        Value->SetStringField(TEXT("authoredTickMode"), Notify.MontageTickType == EMontageNotifyTickType::Queued ? TEXT("Queued") : TEXT("BranchingPoint"));
        SetP5aDiscoveryFloat(Value, TEXT("authoredTimeSeconds"), Notify.GetTime());
        SetP5aDiscoveryFloat(Value, TEXT("durationSeconds"), Notify.GetDuration());
        SetP5aDiscoveryFloat(Value, TEXT("triggerTimeSeconds"), Notify.GetTriggerTime());
        SetP5aDiscoveryFloat(Value, TEXT("endTriggerTimeSeconds"), Notify.GetEndTriggerTime());
        SetP5aDiscoveryFloat(Value, TEXT("triggerWeightThreshold"), Notify.TriggerWeightThreshold);
        return Value;
    }

    bool ObserveNotify(const FAnimNotifyEventReference& Reference, const TArray<FP5aSourceDefinition>& Sources,
                       TArray<TSharedPtr<FJsonValue>>& Values, const TCHAR* Method,
                       const FAnimMontageInstance* BranchInstance = nullptr)
    {
        UAnimSequenceBase* Source{const_cast<UAnimSequenceBase*>(Cast<UAnimSequenceBase>(Reference.GetSourceObject()))};
        const FAnimNotifyEvent* Notify{Reference.GetNotify()};
        if (!IsValid(Source) || Notify == nullptr || !ObserveAsset(Source, Sources)) return false;
        int32 SourceIndex{INDEX_NONE};
        for (int32 Index{0}; Index < Source->Notifies.Num(); ++Index)
        {
            if (Source->Notifies[Index] == *Notify)
            {
                if (SourceIndex != INDEX_NONE) return false;
                SourceIndex = Index;
            }
        }
        if (SourceIndex == INDEX_NONE) return false;
        const TSharedRef<FJsonObject> Value{NotifyIdentity(*Source, *Notify, SourceIndex)};
        Value->SetStringField(TEXT("observationMethod"), Method);
        Value->SetNumberField(TEXT("notifyInstanceId"), Reference.GetNotifyInstanceID());
        SetP5aDiscoveryFloat(Value, TEXT("referenceTimeSeconds"), Reference.GetCurrentAnimationTime());
        if (const UE::Anim::FAnimNotifyMontageInstanceContext* Context{
            Reference.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>()})
            Value->SetNumberField(TEXT("montageContextId"), Context->MontageInstanceID);
        else Value->SetField(TEXT("montageContextId"), MakeShared<FJsonValueNull>());
        if (BranchInstance != nullptr)
        {
            Value->SetNumberField(TEXT("branchingMontageInstanceId"), BranchInstance->GetInstanceID());
            SetP5aDiscoveryFloat(Value, TEXT("branchingMontagePositionSeconds"), BranchInstance->GetPosition());
        }
        Values.Add(MakeShared<FJsonValueObject>(Value));
        return true;
    }

    bool ObserveRuntime(AAlsTraceCharacter& Character, const TArray<FP5aSourceDefinition>& Sources,
                        const TSharedRef<FJsonObject>& Frame)
    {
        UAlsAnimationInstance* Animation{Character.GetTraceAnimationInstanceMutable()};
        TArray<TSharedPtr<FJsonValue>> Montages;
        TArray<TSharedPtr<FJsonValue>> Branching;
        const FArrayProperty* BranchingProperty{FindFProperty<FArrayProperty>(FAnimMontageInstance::StaticStruct(), TEXT("ActiveStateBranchingPoints"))};
        if (BranchingProperty == nullptr) return false;
        for (const FAnimMontageInstance* Instance : Animation->MontageInstances)
        {
            if (Instance == nullptr || !IsValid(Instance->Montage)) continue;
            UAnimMontage* Montage{Instance->Montage};
            if (!ObserveAsset(Montage, Sources)) return false;
            const TSharedRef<FJsonObject> Value{P5aDiscoveryMontageSnapshot(Instance, Montage)};
            Value->SetBoolField(TEXT("transientMontage"), Montage->GetOutermost() == GetTransientPackage());
            TArray<TSharedPtr<FJsonValue>> Segments;
            for (int32 TrackIndex{0}; TrackIndex < Montage->SlotAnimTracks.Num(); ++TrackIndex)
            {
                const FSlotAnimationTrack& Track{Montage->SlotAnimTracks[TrackIndex]};
                for (int32 Index{0}; Index < Track.AnimTrack.AnimSegments.Num(); ++Index)
                {
                    const FAnimSegment& Segment{Track.AnimTrack.AnimSegments[Index]};
                    if (!ObserveAsset(Segment.GetAnimReference(), Sources)) return false;
                    const TSharedRef<FJsonObject> Item{MakeShared<FJsonObject>()};
                    Item->SetStringField(TEXT("assetObjectPath"), Segment.GetAnimReference()->GetPathName());
                    Item->SetStringField(TEXT("slotName"), Track.SlotName.ToString());
                    Item->SetNumberField(TEXT("trackIndex"), TrackIndex);
                    Item->SetNumberField(TEXT("segmentIndex"), Index);
                    SetP5aDiscoveryFloat(Item, TEXT("animationTimeSeconds"), Segment.ConvertTrackPosToAnimPos(Instance->GetPosition()));
                    SetP5aDiscoveryFloat(Item, TEXT("segmentPlayRate"), Segment.GetValidPlayRate());
                    SetP5aDiscoveryFloat(Item, TEXT("montageStartSeconds"), Segment.StartPos);
                    SetP5aDiscoveryFloat(Item, TEXT("animationStartSeconds"), Segment.AnimStartTime);
                    SetP5aDiscoveryFloat(Item, TEXT("animationEndSeconds"), Segment.AnimEndTime);
                    Segments.Add(MakeShared<FJsonValueObject>(Item));
                }
            }
            Value->SetArrayField(TEXT("segments"), Segments);
            Montages.Add(MakeShared<FJsonValueObject>(Value));
            FScriptArrayHelper Active{BranchingProperty, BranchingProperty->ContainerPtrToValuePtr<void>(Instance)};
            for (int32 Index{0}; Index < Active.Num(); ++Index)
            {
                const FAnimNotifyEvent* Notify{reinterpret_cast<const FAnimNotifyEvent*>(Active.GetRawPtr(Index))};
                if (!ObserveNotify(FAnimNotifyEventReference{Notify, Montage}, Sources, Branching,
                    TEXT("reflected active branching state snapshot"), Instance)) return false;
            }
        }
        TArray<TSharedPtr<FJsonValue>> Queue;
        TArray<TSharedPtr<FJsonValue>> Active;
        for (const FAnimNotifyEventReference& Notify : Animation->NotifyQueue.AnimNotifies)
            if (!ObserveNotify(Notify, Sources, Queue, TEXT("post-update queue snapshot; not a callback"))) return false;
        for (const FAnimNotifyEventReference& Notify : Animation->ActiveAnimNotifyEventReference)
            if (!ObserveNotify(Notify, Sources, Active, TEXT("post-update native active state snapshot"))) return false;
        Frame->SetArrayField(TEXT("montages"), Montages);
        Frame->SetArrayField(TEXT("branchingStates"), Branching);
        Frame->SetArrayField(TEXT("notifyQueue"), Queue);
        Frame->SetArrayField(TEXT("activeNotifyStates"), Active);
        Frame->SetStringField(TEXT("locomotionAction"), Character.GetLocomotionAction().ToString());
        SetP5aDiscoveryFloat(Frame, TEXT("rootMotionTranslationScale"), Character.GetAnimRootMotionTranslationScale());
        TArray<TSharedPtr<FJsonValue>> Players;
        TArray<UAnimInstance*> Instances;
        Instances.Add(Animation);
        const USkeletalMeshComponent* Mesh{Character.GetMesh()};
        for (UAnimInstance* Linked : Mesh->GetLinkedAnimInstances()) if (IsValid(Linked)) Instances.AddUnique(Linked);
        for (UAnimInstance* Instance : Instances)
        {
            const IAnimClassInterface* Interface{IAnimClassInterface::GetFromClass(Instance->GetClass())};
            if (Interface == nullptr) continue;
            for (const FStructProperty* Property : Interface->GetAnimNodeProperties())
            {
                if (!Property->Struct->IsChildOf(FAnimNode_AssetPlayerRelevancyBase::StaticStruct())) continue;
                const FAnimNode_AssetPlayerRelevancyBase* Player{Property->ContainerPtrToValuePtr<FAnimNode_AssetPlayerRelevancyBase>(Instance)};
                if (Player == nullptr || !IsValid(Player->GetAnimAsset()) || Player->GetCachedBlendWeight() <= 0.0f) continue;
                if (!ObserveAsset(Player->GetAnimAsset(), Sources)) return false;
                const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
                Value->SetStringField(TEXT("instanceClassPath"), Instance->GetClass()->GetPathName());
                Value->SetStringField(TEXT("nodeProperty"), Property->GetName());
                Value->SetStringField(TEXT("assetObjectPath"), Player->GetAnimAsset()->GetPathName());
                Value->SetStringField(TEXT("observationMethod"), TEXT("asset player cached blend-weight snapshot"));
                SetP5aDiscoveryFloat(Value, TEXT("cachedBlendWeight"), Player->GetCachedBlendWeight());
                SetP5aDiscoveryFloat(Value, TEXT("accumulatedTimeSeconds"), Player->GetAccumulatedTime());
                Players.Add(MakeShared<FJsonValueObject>(Value));
            }
        }
        Frame->SetArrayField(TEXT("assetPlayers"), Players);
        return true;
    }

    bool Save(const FString& OutputPath, const FString& PlanHash) const
    {
        if (Cases.Num() != 8 || MeasuredFrameCount != 374) return false;
        const TSharedRef<FJsonObject> Value{MakeShared<FJsonObject>()};
        Value->SetStringField(TEXT("kind"), TEXT("p5a_native_discovery_v1"));
        Value->SetBoolField(TEXT("diagnosticOnly"), true);
        Value->SetStringField(TEXT("tracePlanSha256"), PlanHash);
        Value->SetStringField(TEXT("referenceCommit"), LockedReferenceCommit);
        Value->SetStringField(TEXT("patchSha256"), LockedPatchHash);
        Value->SetNumberField(TEXT("measuredFrameCount"), MeasuredFrameCount);
        TArray<FString> Paths;
        Assets.GenerateKeyArray(Paths);
        Paths.Sort();
        TArray<TSharedPtr<FJsonValue>> Inventory;
        for (const FString& Path : Paths) Inventory.Add(MakeShared<FJsonValueObject>(Assets[Path].ToSharedRef()));
        Value->SetArrayField(TEXT("assets"), Inventory);
        Value->SetArrayField(TEXT("cases"), Cases);
        return SaveJson(OutputPath, Value);
    }
};
