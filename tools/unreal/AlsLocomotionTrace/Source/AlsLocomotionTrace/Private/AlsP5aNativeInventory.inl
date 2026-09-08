// Physical auxiliary identities never enter the canonical source allocator.
struct FP5aNativeAuxiliaryInventory
{
    TArray<FP5aObservedSource> Sources;
    TArray<TObjectPtr<UAnimationAsset>> Assets;
    TArray<TSharedPtr<FJsonValue>> AuditRows;

    bool Contains(const UAnimationAsset* Asset) const
    {
        return Assets.Contains(Asset);
    }

    bool IsDisjointFrom(const TArray<FP5aSourceDefinition>& Mapped) const
    {
        for (const FP5aSourceDefinition& Source : Mapped)
        {
            if (Contains(Source.NativeAsset)) return false;
            for (const UAnimSequenceBase* Asset : Source.NativeAssets) if (Contains(Asset)) return false;
        }
        return true;
    }

    static bool MatchesString(const TSharedPtr<FJsonObject>& Expected, const TCHAR* Key, const FString& Actual)
    {
        FString Value;
        return Expected->TryGetStringField(Key, Value) && Value == Actual;
    }

    static bool MatchesFloat(const TSharedPtr<FJsonObject>& Expected, const TCHAR* Key, const float Actual)
    {
        double Value{0.0};
        return Expected->TryGetNumberField(Key, Value) && FMath::IsFinite(Value) &&
            FMath::IsFinite(static_cast<float>(Value)) && static_cast<float>(Value) == Actual;
    }

    bool ReadAndValidate(const TSharedPtr<FJsonObject>& Plan)
    {
        static constexpr const TCHAR* Paths[]{
            TEXT("/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle"),
            TEXT("/ALS/ALS/Animations/Grounded/Lean/BS_Als_Lean.BS_Als_Lean"),
            TEXT("/ALS/ALS/Animations/Grounded/WalkRun/BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward"),
            TEXT("/ALS/ALS/Animations/Overlays/Other/A_Als_Default_Poses.A_Als_Default_Poses"),
            TEXT("/ALS/ALS/Animations/Transitions/A_Als_CrouchToStand.A_Als_CrouchToStand"),
            TEXT("/ALS/ALS/Animations/Transitions/A_Als_StandToCrouch.A_Als_StandToCrouch"),
            TEXT("/ALS/ALS/Animations/Transitions/A_Als_Stand_Transition_Right.A_Als_Stand_Transition_Right"),
            TEXT("/ALS/ALS/Animations/Transitions/A_Als_Stop_Left.A_Als_Stop_Left"),
            TEXT("/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look")};
        const TArray<TSharedPtr<FJsonValue>>* Dependencies{nullptr};
        if (!Plan->TryGetArrayField(TEXT("nativeAuditDependencies"), Dependencies) ||
            Dependencies == nullptr || Dependencies->Num() != UE_ARRAY_COUNT(Paths)) return false;
        Sources.Reset();
        Assets.Reset();
        AuditRows.Reset();
        for (int32 AssetIndex{0}; AssetIndex < UE_ARRAY_COUNT(Paths); ++AssetIndex)
        {
            const TSharedPtr<FJsonObject>* Expected{nullptr};
            if (!(*Dependencies)[AssetIndex]->TryGetObject(Expected) || Expected == nullptr ||
                (*Expected)->Values.Num() != 5 ||
                !MatchesString(*Expected, TEXT("assetObjectPath"), Paths[AssetIndex])) return false;
            UAnimationAsset* Asset{LoadObject<UAnimationAsset>(nullptr, Paths[AssetIndex])};
            if (!IsValid(Asset)) return false;
            FP5aObservedSource Source;
            Source.AssetObjectPath = Asset->GetPathName();
            Source.AssetStableId = CreateP5aAssetStableId(Source.AssetObjectPath);
            Source.AssetPackageSha256 = CreateP5aPackageSha256(*Asset);
            Source.AssetClassPath = Asset->GetClass()->GetPathName();
            if (Source.AssetPackageSha256.IsEmpty() ||
                !MatchesString(*Expected, TEXT("assetStableId"), Source.AssetStableId) ||
                !MatchesString(*Expected, TEXT("assetPackageSha256"), Source.AssetPackageSha256) ||
                !MatchesString(*Expected, TEXT("assetClassPath"), Source.AssetClassPath)) return false;
            const TArray<TSharedPtr<FJsonValue>>* ExpectedEvents{nullptr};
            if (!(*Expected)->TryGetArrayField(TEXT("events"), ExpectedEvents) || ExpectedEvents == nullptr) return false;
            const UAnimSequenceBase* Sequence{Cast<UAnimSequenceBase>(Asset)};
            const int32 NotifyCount{Sequence != nullptr ? Sequence->Notifies.Num() : 0};
            if (ExpectedEvents->Num() != NotifyCount) return false;
            TArray<TSharedPtr<FJsonValue>> Events;
            for (int32 Index{0}; Index < NotifyCount; ++Index)
            {
                const FAnimNotifyEvent& Notify{Sequence->Notifies[Index]};
                const UObject* NotifyObject{IsValid(Notify.NotifyStateClass)
                    ? static_cast<const UObject*>(Notify.NotifyStateClass.Get()) : static_cast<const UObject*>(Notify.Notify.Get())};
                if (!IsValid(NotifyObject)) return false;
                const FString ClassPath{NotifyObject->GetClass()->GetPathName()};
                const FString EventId{CreateP5aEventStableId(Source.AssetStableId, Index, ClassPath)};
                const FString TickMode{Notify.MontageTickType == EMontageNotifyTickType::Queued ? TEXT("Queued") : TEXT("BranchingPoint")};
                const TSharedPtr<FJsonObject>* ExpectedEvent{nullptr};
                double SourceIndex{-1.0};
                double TrackIndex{-1.0};
                if (!(*ExpectedEvents)[Index]->TryGetObject(ExpectedEvent) || ExpectedEvent == nullptr ||
                    (*ExpectedEvent)->Values.Num() != 8 ||
                    !MatchesString(*ExpectedEvent, TEXT("stableEventId"), EventId) ||
                    !MatchesString(*ExpectedEvent, TEXT("sourceClassPath"), ClassPath) ||
                    !MatchesString(*ExpectedEvent, TEXT("tickMode"), TickMode) ||
                    !(*ExpectedEvent)->TryGetNumberField(TEXT("sourceIndex"), SourceIndex) || SourceIndex != Index ||
                    !(*ExpectedEvent)->TryGetNumberField(TEXT("trackIndex"), TrackIndex) || TrackIndex != Notify.TrackIndex ||
                    !MatchesFloat(*ExpectedEvent, TEXT("timeSeconds"), Notify.GetTime()) ||
                    !MatchesFloat(*ExpectedEvent, TEXT("durationSeconds"), Notify.GetDuration()) ||
                    !MatchesFloat(*ExpectedEvent, TEXT("triggerWeightThreshold"), Notify.TriggerWeightThreshold)) return false;
                const TSharedRef<FJsonObject> Row{MakeShared<FJsonObject>()};
                Row->SetStringField(TEXT("stableEventId"), EventId);
                Row->SetStringField(TEXT("sourceClassPath"), ClassPath);
                Row->SetNumberField(TEXT("sourceIndex"), Index);
                Row->SetNumberField(TEXT("trackIndex"), Notify.TrackIndex);
                SetP5aFloatField(Row, TEXT("timeSeconds"), Notify.GetTime());
                SetP5aFloatField(Row, TEXT("durationSeconds"), Notify.GetDuration());
                SetP5aFloatField(Row, TEXT("triggerWeightThreshold"), Notify.TriggerWeightThreshold);
                Row->SetStringField(TEXT("tickMode"), TickMode);
                Events.Add(MakeShared<FJsonValueObject>(Row));
            }
            const TSharedRef<FJsonObject> Row{MakeShared<FJsonObject>()};
            Row->SetStringField(TEXT("assetObjectPath"), Source.AssetObjectPath);
            Row->SetStringField(TEXT("assetStableId"), Source.AssetStableId);
            Row->SetStringField(TEXT("assetPackageSha256"), Source.AssetPackageSha256);
            Row->SetStringField(TEXT("assetClassPath"), Source.AssetClassPath);
            Row->SetArrayField(TEXT("events"), Events);
            Sources.Add(MoveTemp(Source));
            Assets.Add(Asset);
            AuditRows.Add(MakeShared<FJsonValueObject>(Row));
        }
        return true;
    }
};
