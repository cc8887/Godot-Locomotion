bool RunP5aNativeCurveSemanticsSelfTest(const TSharedPtr<FJsonObject>& Plan)
{
    TArray<FP5aSourceDefinition> Sources;
    TArray<FP5aCaseDefinition> Cases;
    if (!ParseP5aCaseDefinitions(Plan, Sources, Cases)) return false;
    const FP5aSourceDefinition* OffsetSource{FindP5aSource(Sources,
        TEXT("a71ce1294ab3dbd4ce6f2f47bde5b4ce29b4b26b"))};
    const FP5aSourceDefinition* MissingSource{FindP5aSource(Sources,
        TEXT("5ec5cb2f6cb21621b6166e0bc5294c154f3b61b6"))};
    if (OffsetSource == nullptr || MissingSource == nullptr) return false;
    const FName CurveName{TEXT("Enable_Transition")};
    const FAnimExtractContext Context{0.0f, false, {}, false};
    constexpr float Weight{.083333336f};
    bool bSuccess{true};
    int32 CheckCount{0};
    for (const bool bAuthored : {true, false})
    {
        for (const bool bMissing : {false, true})
        {
            const FP5aSourceDefinition& FixtureSource{bMissing ? *MissingSource : *OffsetSource};
            UAnimSequenceBase* Asset{FixtureSource.CanonicalAsset.Get()};
            if (!IsValid(Asset)) return false;
            const bool bPresent{Asset->HasCurveData(CurveName, bAuthored)};
            if ((bMissing && bPresent) || (!bMissing && bAuthored && !bPresent)) return false;
            const float Offset{bPresent ? Asset->EvaluateCurveData(CurveName, Context, bAuthored) : 0.0f};
            if (bAuthored && !bMissing && Offset != -1.0f) return false;
            const float Expected{bAuthored && !bMissing ? .9166667f
                : static_cast<float>(1.0 + static_cast<double>(Offset) * Weight)};
            for (int32 Lane{0}; Lane < 3; ++Lane)
            {
                // Route a real loaded curve through each evaluator entry without editing asset data.
                TArray<FP5aSourceDefinition> FixtureSources{FixtureSource};
                FixtureSources[0].SourceKind = Lane == 2 ? TEXT("ActionSequence") : TEXT("Base");
                FP5aFrameDefinition Frame;
                if (Lane == 0)
                {
                    FP5aPlaybackDefinition Playback;
                    Playback.TraceSourceId = FixtureSource.TraceSourceId;
                    Playback.Lane = TEXT("base");
                    Playback.Weight = Weight;
                    Frame.Playbacks.Add(Playback);
                }
                const TSharedRef<FJsonObject> Result{EvaluateP5aCanonicalCurves(Frame, FixtureSources,
                    Lane == 1 ? &FixtureSources[0] : nullptr, 0.0f, 0.0f,
                    Lane == 2 ? Weight : 0.0f, Lane == 1 ? Weight : 0.0f, bAuthored)};
                const float Actual{static_cast<float>(Result->GetNumberField(TEXT("allowTransitions")))};
                ++CheckCount;
                if (Actual != Expected)
                {
                    UE_LOG(LogTemp, Error, TEXT("P5A curve semantics self-test lane=%d authored=%d missing=%d expected=%.9g actual=%.9g"),
                        Lane, bAuthored, bMissing, Expected, Actual);
                    bSuccess = false;
                }
            }
        }
    }
    if (bSuccess)
        UE_LOG(LogTemp, Display, TEXT("P5A native curve semantics self-test passed checks=%d"), CheckCount);
    return bSuccess;
}
