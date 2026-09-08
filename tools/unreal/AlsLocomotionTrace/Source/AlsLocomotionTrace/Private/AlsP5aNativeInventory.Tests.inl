bool RunP5aNativeAssetClosureSelfTest(const FP5aNativeAuxiliaryInventory& Auxiliary)
{
    FP5aNativeDiscovery Formal;
    Formal.FormalAuxiliary = &Auxiliary;
    const TArray<FP5aSourceDefinition> NoMappedSources;
    UAnimSequence* UnknownSequence{NewObject<UAnimSequence>()};
    UBlendSpace* UnknownBlendSpace{NewObject<UBlendSpace>()};
    UAnimMontage* EmptyMontage{NewObject<UAnimMontage>()};
    UAnimMontage* UnknownSegmentMontage{NewObject<UAnimMontage>()};
    UnknownSegmentMontage->SlotAnimTracks[0].SlotName = UAlsConstants::TransitionSlotName();
    FAnimSegment UnknownSegment;
    UnknownSegment.SetAnimReference(UnknownSequence);
    UnknownSegmentMontage->SlotAnimTracks[0].AnimTrack.AnimSegments.Add(UnknownSegment);
    UAnimMontage* NullSegmentMontage{NewObject<UAnimMontage>()};
    NullSegmentMontage->SlotAnimTracks[0].SlotName = UAlsConstants::TransitionSlotName();
    NullSegmentMontage->SlotAnimTracks[0].AnimTrack.AnimSegments.AddDefaulted();
    UAnimSequenceBase* KnownSequence{Cast<UAnimSequenceBase>(Auxiliary.Assets[6])};
    UAnimMontage* ValidMontage{UAnimMontage::CreateSlotAnimationAsDynamicMontage(
        KnownSequence, UAlsConstants::TransitionSlotName(), .2f, .2f, 1.5f, 1)};
    if (!IsValid(ValidMontage)) return false;
    UAnimMontage* MixedMontage{DuplicateObject<UAnimMontage>(ValidMontage, GetTransientPackage())};
    MixedMontage->SlotAnimTracks[0].AnimTrack.AnimSegments.Add(UnknownSegment);
    UAnimationAsset* Rejected[]{UnknownSequence, UnknownBlendSpace, EmptyMontage,
        UnknownSegmentMontage, NullSegmentMontage, MixedMontage};
    bool bSuccess{true};
    for (UAnimationAsset* Asset : Rejected)
    {
        if (Formal.ObserveAsset(Asset, NoMappedSources))
        {
            UE_LOG(LogTemp, Error, TEXT("P5A closure self-test accepted unknown transient asset=%s"),
                *GetPathNameSafe(Asset));
            bSuccess = false;
        }
    }
    if (!Formal.ObserveAsset(ValidMontage, NoMappedSources)) bSuccess = false;
    FP5aNativeDiscovery Diagnostic;
    if (!Diagnostic.ObserveAsset(UnknownSequence, NoMappedSources)) bSuccess = false;
    if (bSuccess)
        UE_LOG(LogTemp, Display, TEXT("P5A native asset closure self-test passed rejected=6 positive=1"));
    return bSuccess;
}
