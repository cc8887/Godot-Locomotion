using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using Json.Schema;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aNativeEvidenceTests
{
    [Fact]
    public void FrozenSyntheticBaselineSatisfiesNativeEvidenceContract()
    {
        var bundle = P5aFrozenPlanDocuments.Create();

        AlsP5aNativeEvidence.Validate(
            bundle.Plan,
            bundle.Raw,
            bundle.Raw["nativeReferenceAudit"]!.AsObject());
    }

    [Fact]
    public void NativeEvidenceValidatorRejectsVersionOneDirectly()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        bundle.Raw["schemaVersion"] = 1;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("nested")]
    public void ClosedEvidenceRejectsUnknownProperties(string location)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        if (location == "root")
            bundle.Raw["unexpected"] = true;
        else
            Actual(bundle.Raw, 0, 0)["stateAfter"]!["unexpected"] = true;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void PhysicalAuditIsValidatedAgainstIndependentExpectedInventory()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var expectedAudit = bundle.Raw["nativeReferenceAudit"]!.DeepClone().AsObject();
        bundle.Raw["nativeReferenceAudit"]!["assets"]![0]!["stableId"] = "mutated";

        Assert.Throws<InvalidDataException>(() =>
            AlsP5aNativeEvidence.Validate(bundle.Plan, bundle.Raw, expectedAudit));
    }

    [Fact]
    public void PhysicalAuditNumericMismatchReportsTheExactEvidencePath()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var expectedAudit = bundle.Raw["nativeReferenceAudit"]!.DeepClone().AsObject();
        bundle.Raw["nativeReferenceAudit"]!["events"]![0]!["triggerWeightThreshold"] = 2e-5f;

        var failure = Assert.Throws<InvalidDataException>(() =>
            AlsP5aNativeEvidence.Validate(bundle.Plan, bundle.Raw, expectedAudit));

        Assert.Contains("$raw.nativeReferenceAudit.events[0].triggerWeightThreshold", failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedPhysicalAuditMatchesEquivalentBuilderBackedInventory()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var expectedAudit = bundle.Raw["nativeReferenceAudit"]!.DeepClone().AsObject();
        bundle.Raw["nativeReferenceAudit"] = JsonNode.Parse(expectedAudit.ToJsonString())!.AsObject();

        AlsP5aNativeEvidence.Validate(bundle.Plan, bundle.Raw, expectedAudit);
    }

    [Fact]
    public void UnknownPhysicalTimelineAssetRejectsBeforeProjection()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var timeline = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray())
            .First(items => items.Count > 0);
        timeline[0]!["source"]!["observedAssetStableId"] = "unknown-physical-asset";

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void RegisteredMappedTransitionTriggerCannotAppearOutsideItsPlaybackCrossing()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var source = Actual(bundle.Raw, 2, 23)["nativeRuntimeTimeline"]!.AsArray()
            .Single(item => item!["phase"]!.GetValue<string>() == "Trigger")!;
        Actual(bundle.Raw, 2, 32)["nativeRuntimeTimeline"]!.AsArray().Add(source.DeepClone());

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void RegisteredMappedActionTriggerCannotMovePastItsAuthoredBoundaryFrame()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var source = Actual(bundle.Raw, 5, 28)["nativeRuntimeTimeline"]!.AsArray();
        var triggerIndex = source.Select((item, index) => (item, index))
            .Single(pair => pair.item!["phase"]!.GetValue<string>() == "Trigger").index;
        var trigger = source[triggerIndex]!.DeepClone();
        source.RemoveAt(triggerIndex);
        Actual(bundle.Raw, 5, 29)["nativeRuntimeTimeline"]!.AsArray().Insert(0, trigger);

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void DynamicTransitionRequiresPhysicalVariantForInputStance()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var actual = Actual(bundle.Raw, 4, 0);
        var standingSource = Actual(bundle.Raw, 2, 0)["dynamicTransition"]!["source"]!.DeepClone();
        actual["dynamicTransition"]!["source"] = standingSource;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void ClosingActionRequiresExactlyOneMatchingTerminalOutcome()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var closing = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!.AsObject())
            .First(actual => actual["actionPlayback"]!["status"]!.GetValue<string>() == "ClosingThisFrame");
        closing["actionOutcomes"] = new JsonArray();

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void ActionOutcomeOrdinalMustMatchClosingPlayback()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var closing = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!.AsObject())
            .First(actual => actual["actionPlayback"]!["status"]!.GetValue<string>() == "ClosingThisFrame");
        closing["actionOutcomes"]![0]!["nativeInstanceOrdinal"] = "999";

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void LegalEarlierNaturalTerminalPassesNativeValidationAndProjection()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var actionFrames = bundle.Raw["cases"]![5]!["frames"]!.AsArray();
        var closingIndex = actionFrames.Select((frame, index) => (frame, index))
            .Single(pair => pair.frame!["nativeActual"]!["actionPlayback"]!["status"]!.GetValue<string>() ==
                            "ClosingThisFrame").index;
        var earlierIndex = closingIndex - 1;
        var earlier = actionFrames[earlierIndex]!["nativeActual"]!.AsObject();
        var historicalTerminal = actionFrames[closingIndex]!["nativeActual"]!.AsObject();
        var inactive = actionFrames[closingIndex + 1]!["nativeActual"]!.AsObject();
        var earlierPlayback = earlier["actionPlayback"]!.AsObject();
        earlierPlayback["status"] = "ClosingThisFrame";
        earlierPlayback["finalSegmentDeltaSeconds"] =
            earlierPlayback["currentMontageTimeSeconds"]!.GetValue<float>() -
            earlierPlayback["previousMontageTimeSeconds"]!.GetValue<float>();
        earlier["actionOutcomes"] = historicalTerminal["actionOutcomes"]!.DeepClone();
        CopyActionState(inactive, earlier);
        historicalTerminal["actionPlayback"] = inactive["actionPlayback"]!.DeepClone();
        historicalTerminal["actionOutcomes"] = new JsonArray();
        CopyActionState(inactive, historicalTerminal);

        Validate(bundle);
        var native = AlsP5aTrace.ProjectNativeCanonical(bundle.Plan, bundle.Raw);

        Assert.Single(native["cases"]![5]!["frames"]![earlierIndex]!["comparableActual"]!["actionOutcomes"]!.AsArray());
        Assert.Empty(native["cases"]![5]!["frames"]![closingIndex]!["comparableActual"]!["actionOutcomes"]!.AsArray());
    }

    [Theory]
    [InlineData("callback")]
    [InlineData("ordinal")]
    [InlineData("source")]
    public void ActionIdentityAndCallbackDriftReject(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var closing = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!.AsObject())
            .First(actual => actual["actionPlayback"]!["status"]!.GetValue<string>() == "ClosingThisFrame");
        if (mutation == "callback") closing["actionOutcomes"]![0]!["callback"] = "MontageStarted";
        else if (mutation == "ordinal") closing["actionPlayback"]!["nativeInstanceOrdinal"] = "2";
        else closing["actionPlayback"]!["montageSource"]!["observedAssetStableId"] = "drift";

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("time-gap")]
    [InlineData("rate-change")]
    [InlineData("inactive-gap")]
    public void LiveActionPlaybackMustRemainContinuous(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var actual = Actual(bundle.Raw, 5, 2);
        if (mutation == "time-gap")
            actual["actionPlayback"]!["previousMontageTimeSeconds"] = .02f;
        else if (mutation == "rate-change")
            actual["actionPlayback"]!["playRate"] = 2f;
        else
        {
            var inactive = Actual(bundle.Raw, 0, 0);
            actual["actionPlayback"] = inactive["actionPlayback"]!.DeepClone();
            foreach (var name in new[]
                     {
                         "actionPlaying", "actionSource", "actionNativeInstanceOrdinal", "actionTimeSeconds",
                     })
                actual["stateAfter"]![name] = inactive["stateAfter"]![name]!.DeepClone();
        }

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void CanonicalCallbackOrdinalRejectsBeforeProjectionSorting()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var events = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray())
            .First(items => items.Count > 1);
        events[0]!["frameEventOrdinal"] = 1;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("nativeInstanceOrdinal", "garbage")]
    [InlineData("playbackCycle", "-1")]
    public void CanonicalCallbackIdentityUsesCanonicalNumericOrdinals(string member, string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var observed = Frames(bundle.Raw)
            .SelectMany(frame => frame["nativeActual"]!["canonicalAssetOracle"]!["events"]!.AsArray())
            .First()!.AsObject();
        observed[member] = mutation;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void DuplicateActiveNotifyOwnerRejects()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var owners = Frames(bundle.Raw)
            .Select(frame => frame["nativeActual"]!["canonicalAssetOracle"]!["activeNotifyStates"]!.AsArray())
            .First(items => items.Count > 0);
        owners.Add(owners[0]!.DeepClone());

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void QueueOnlyTickCannotManufacturePhysicalState()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var timeline = bundle.Raw["cases"]![5]!["frames"]![1]!["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray();
        timeline.RemoveAt(0);

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("weight")]
    public void InvalidObservedRangesReject(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        if (mutation == "offset")
        {
            var timeline = Frames(bundle.Raw)
                .Select(frame => frame["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray())
                .First(items => items.Count > 0);
            timeline[0]!["observedFrameOffsetSeconds"] = 2f;
        }
        else
        {
            var visual = Frames(bundle.Raw)
                .Select(frame => frame["nativeActual"]!["actionVisualContribution"]!.AsObject())
                .First(value => value["contributing"]!.GetValue<bool>());
            visual["observedEffectiveWeight"] = 2f;
        }

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(.33333334f)]
    public void SemanticActionGraphWeightMustFollowLifecycleRecurrence(float mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        Actual(bundle.Raw, 5, 1)["canonicalAssetOracle"]!["graphCurveWeights"]!["action"] = mutation;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void TransitionReceiptCannotMoveAwayFromItsPlanProbe()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var frames = bundle.Raw["cases"]![2]!["frames"]!.AsArray();
        var receipt = frames[0]!["nativeActual"]!["transitionStimulusReceipts"]![0]!.DeepClone();
        frames[0]!["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Clear();
        frames[1]!["nativeActual"]!["transitionStimulusReceipts"]!.AsArray().Add(receipt);

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    public void PhysicalAuditRejectsMissingOrExtraStaticInventory(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var expected = bundle.Raw["nativeReferenceAudit"]!.DeepClone().AsObject();
        var assets = bundle.Raw["nativeReferenceAudit"]!["assets"]!.AsArray();
        if (mutation == "missing") assets.RemoveAt(0);
        else assets.Add(assets[0]!.DeepClone());

        Assert.Throws<InvalidDataException>(() =>
            AlsP5aNativeEvidence.Validate(bundle.Plan, bundle.Raw, expected));
    }

    [Fact]
    public void ValidRawOnlyAuditChangesDoNotChangeCanonicalProjectionOrPort()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var before = AlsP5aTrace.ProjectNativeCanonical(bundle.Plan, bundle.Raw);
        var portBefore = bundle.PortSchemaSeed.DeepClone();
        var actual = Actual(bundle.Raw, 0, 0);
        actual["canonicalAssetOracle"]!["compressedCurves"]!["leftIk"] = .25f;
        actual["animGraphCurveAudit"]!["leftIk"]!["value"] = -.65f;

        Validate(bundle);
        var after = AlsP5aTrace.ProjectNativeCanonical(bundle.Plan, bundle.Raw);

        Assert.True(JsonNode.DeepEquals(before, after));
        Assert.True(JsonNode.DeepEquals(portBefore, bundle.PortSchemaSeed));
    }

    [Fact]
    public void AuthoredCurveObservationFlowsIntoNativeCanonicalWithoutChangingPort()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var portBefore = bundle.PortSchemaSeed.DeepClone();
        Actual(bundle.Raw, 0, 0)["canonicalAssetOracle"]!["curves"]!["leftIk"] = .25f;

        Validate(bundle);
        var native = AlsP5aTrace.ProjectNativeCanonical(bundle.Plan, bundle.Raw);

        Assert.Equal(.25f,
            native["cases"]![0]!["frames"]![0]!["comparableActual"]!["curves"]!["leftIk"]!.GetValue<float>());
        Assert.True(JsonNode.DeepEquals(portBefore, bundle.PortSchemaSeed));
    }

    [Fact]
    public void CoreNaturalClosingProjectionUsesLifecycleOutcomeWithoutFrameNumber()
    {
        var previous = new AlsActionPlayerState
        {
            ActionDefinitionId = 2, SectionId = 3, SegmentBindingIndex = 4,
            RequestId = 7, PlaybackEpoch = 11, PlaybackTime = 1.48f,
            Playing = 1, Interruptible = 1,
            Lifecycle = new AlsActionLifecycleState { BlendingOut = 1, CurrentWeight = .01f },
        };
        var playback = new AlsActionPlayback(
            5, 2, 6, 3, 8, 11, 1.48f, 1.4999993f, 1.48f, 1.4999993f,
            .0123f, 1f, .2f, 0f, 1);
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(outcomes.TryAdd(new AlsActionOutcome(7, 2, 11, AlsActionResultCode.Completed)));

        var delta = P5aPortReplay.ValidateActionClosingForProjection(
            AlsActionCommand.None, .016666668f, playback, previous,
            AlsActionPlayerState.CreateDefault(), outcomes);

        Assert.Equal(.0123f, delta);
    }

    [Fact]
    public void CoreClosingProjectionRejectsNonterminalOutcome()
    {
        var previous = new AlsActionPlayerState
        {
            ActionDefinitionId = 2, SectionId = 3, SegmentBindingIndex = 4,
            RequestId = 7, PlaybackEpoch = 11, PlaybackTime = 1f,
            Playing = 1, Lifecycle = new AlsActionLifecycleState { BlendingOut = 1 },
        };
        var playback = new AlsActionPlayback(
            5, 2, 6, 3, 8, 11, 1f, 1f, 1f, 1f,
            0f, 1f, .2f, 0f, 1);
        var outcomes = new AlsActionOutcomeBuffer();
        Assert.True(outcomes.TryAdd(new AlsActionOutcome(7, 2, 11, AlsActionResultCode.Accepted)));

        Assert.Throws<InvalidDataException>(() => P5aPortReplay.ValidateActionClosingForProjection(
            AlsActionCommand.None, .016666668f, playback, previous,
            AlsActionPlayerState.CreateDefault(), outcomes));
    }

    [Theory]
    [InlineData("playback")]
    [InlineData("visual")]
    [InlineData("transition")]
    [InlineData("sync")]
    public void InactiveDtosRequireExactSentinels(string member)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var actual = Actual(bundle.Raw, 0, 0);
        if (member == "playback") actual["actionPlayback"]!["currentMontageTimeSeconds"] = .1f;
        else if (member == "visual") actual["actionVisualContribution"]!["observedMontageTimeSeconds"] = .1f;
        else if (member == "transition") actual["dynamicTransition"]!["previousTimeSeconds"] = .1f;
        else Actual(bundle.Raw, 2, 0)["canonicalAssetOracle"]!["sync"]!["phase"] = .1f;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("delay")]
    public void TransitionReceiptRequiresApprovedHookContract(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var receipt = Actual(bundle.Raw, 2, 0)["transitionStimulusReceipts"]![0]!.AsObject();
        if (mutation == "hash") receipt["hookContractSha256"] = new string('0', 64);
        else receipt["postHookFrameDelay"] = 1;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void ActionVisualBlendConfigurationIsStaticEvidence()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var visual = Actual(bundle.Raw, 5, 1)["actionVisualContribution"]!.AsObject();
        visual["observedBlendOutOption"] = 1;

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Theory]
    [InlineData("instant-as-state")]
    [InlineData("state-as-instant")]
    public void NotifyPhaseMustMatchAuditedDurationKind(string mutation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        if (mutation == "instant-as-state")
        {
            var timeline = Frames(bundle.Raw)
                .Select(frame => frame["nativeActual"]!["nativeRuntimeTimeline"]!.AsArray())
                .First(items => items.Any(item => item!["phase"]!.GetValue<string>() == "Trigger"));
            timeline.First(item => item!["phase"]!.GetValue<string>() == "Trigger")!["phase"] = "Tick";
        }
        else
        {
            var events = Actual(bundle.Raw, 5, 1)["canonicalAssetOracle"]!["events"]!.AsArray();
            events[0]!["phase"] = "Trigger";
        }

        Assert.Throws<InvalidDataException>(() => Validate(bundle));
    }

    [Fact]
    public void TraceSchemaAcceptsDynamicRuntimeCardinalityWithClosedItems()
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var events = bundle.NativeCanonical["cases"]!.AsArray()
            .SelectMany(caseNode => caseNode!["frames"]!.AsArray())
            .Select(frame => frame!["comparableActual"]!["events"]!.AsArray()).ToArray();
        var source = events.First(array => array.Count > 0);
        var destination = events.First(array => array.Count == 0);
        destination.Add(source[0]!.DeepClone());
        source.RemoveAt(0);

        Assert.True(EvaluateTraceSchema(bundle.NativeCanonical));
        destination[0]!["unexpected"] = true;
        Assert.False(EvaluateTraceSchema(bundle.NativeCanonical));
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("native")]
    [InlineData("port")]
    public void TraceSchemaAcceptsEveryVersionTwoRepresentation(string representation)
    {
        var bundle = P5aFrozenPlanDocuments.Create();
        var value = representation switch
        {
            "raw" => bundle.Raw,
            "native" => bundle.NativeCanonical,
            _ => bundle.PortSchemaSeed,
        };

        Assert.True(EvaluateTraceSchema(value));
    }

    [Theory]
    [InlineData(0, "extra")]
    [InlineData(4, "missing")]
    public void TraceSchemaRequiresExactAuxiliaryEventCountPerAsset(int assetIndex, string mutation)
    {
        var raw = P5aFrozenPlanDocuments.Create().Raw;
        var assets = raw["nativeReferenceAudit"]!["auxiliaryAssets"]!.AsArray();
        var events = assets[assetIndex]!["events"]!.AsArray();
        if (mutation == "extra")
            events.Add(assets[4]!["events"]![0]!.DeepClone());
        else
            events.RemoveAt(events.Count - 1);

        Assert.False(EvaluateTraceSchema(raw));
    }

    [Theory]
    [InlineData("assets")]
    [InlineData("events")]
    public void TraceSchemaRejectsAuxiliaryInventoryReordering(string mutation)
    {
        var raw = P5aFrozenPlanDocuments.Create().Raw;
        var assets = raw["nativeReferenceAudit"]!["auxiliaryAssets"]!.AsArray();
        var rows = mutation == "assets" ? assets : assets[4]!["events"]!.AsArray();
        var first = rows[0]!.DeepClone();
        rows[0] = rows[1]!.DeepClone();
        rows[1] = first;

        Assert.False(EvaluateTraceSchema(raw));
    }

    private static void Validate(P5aFrozenDocumentSet bundle) =>
        AlsP5aNativeEvidence.Validate(
            bundle.Plan,
            bundle.Raw,
            bundle.Raw["nativeReferenceAudit"]!.AsObject());

    private static IEnumerable<JsonObject> Frames(JsonObject root) =>
        root["cases"]!.AsArray()
            .SelectMany(caseNode => caseNode!["frames"]!.AsArray())
            .Select(frameNode => frameNode!.AsObject());

    private static JsonObject Actual(JsonObject raw, int caseIndex, int frameIndex) =>
        raw["cases"]![caseIndex]!["frames"]![frameIndex]!["nativeActual"]!.AsObject();

    private static void CopyActionState(JsonObject sourceActual, JsonObject targetActual)
    {
        var source = sourceActual["stateAfter"]!.AsObject();
        var target = targetActual["stateAfter"]!.AsObject();
        foreach (var name in new[]
                 {
                     "actionPlaying", "actionSource", "actionNativeInstanceOrdinal", "actionTimeSeconds",
                 })
            target[name] = source[name]!.DeepClone();
    }

    private static bool EvaluateTraceSchema(JsonObject value)
    {
        var path = Path.Combine(P5aRedHarness.RepositoryRoot(), "tools", "schemas", "als_p5a_trace.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(path));
        using var document = JsonDocument.Parse(value.ToJsonString());
        return schema.Evaluate(document.RootElement).IsValid;
    }
}
