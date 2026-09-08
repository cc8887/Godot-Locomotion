using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Events;

namespace GodotAls.Core.Animation;

internal static class AlsP5aNativeEvidence
{
    private const float FloatTolerance = 1e-5f;
    private const string TransitionHookContractSha256 =
        "78cfb6aad01c29ac179f63515835aeeb6dd48b70dc42223cf81dea771e27f11d";

    private readonly record struct PhysicalEventDefinition(
        bool IsState,
        float TimeSeconds,
        string TraceSourceId,
        string SourceKind);

    internal static void Validate(
        JsonObject plan,
        JsonObject raw,
        JsonObject expectedPhysicalAudit)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(expectedPhysicalAudit);

        var template = P5aFrozenPlanDocuments.Create().Raw;
        var shapes = RuntimeShapes.Create(template);
        ValidateShape(template, raw, "$raw", shapes);
        ValidateRootIdentity(plan, raw);
        ValidatePhysicalAudit(raw["nativeReferenceAudit"]!.AsObject(), expectedPhysicalAudit);
        if (!AlsP5aTrace.JsonEvidenceEquals(
                plan["nativeAuditDependencies"],
                raw["nativeReferenceAudit"]!["auxiliaryAssets"]))
        {
            throw Invalid("$raw.nativeReferenceAudit.auxiliaryAssets",
                "auxiliary inventory differs from the frozen plan dependency audit");
        }

        var sourceIndex = SourceIndex.Create(plan, expectedPhysicalAudit);
        ValidateCases(plan, raw, sourceIndex);
    }

    private static void ValidateRootIdentity(JsonObject plan, JsonObject raw)
    {
        if (plan["schemaVersion"]!.GetValue<int>() != 2 || raw["schemaVersion"]!.GetValue<int>() != 2)
            throw Invalid("$raw.schemaVersion", "plan and raw evidence must both use schema version 2");
        RequireString(raw, "kind", "p5a_trace", "$raw");
        RequireString(raw, "representation", "native_raw", "$raw");
        RequireString(raw, "provenance", "als_runtime", "$raw");
        if (!AlsP5aTrace.JsonEvidenceEquals(raw["reference"], plan["reference"]) ||
            !AlsP5aTrace.JsonEvidenceEquals(raw["snapshot"], plan["snapshot"]))
        {
            throw Invalid("$raw", "reference or snapshot differs from the trace plan");
        }

        var expectedSha = Convert.ToHexString(
            SHA256.HashData(P5aFrozenPlanDocuments.CanonicalBytes(plan))).ToLowerInvariant();
        RequireString(raw, "tracePlanSha256", expectedSha, "$raw");
    }

    private static void ValidatePhysicalAudit(JsonObject actual, JsonObject expected)
    {
        ValidateShape(expected, actual, "$raw.nativeReferenceAudit", RuntimeShapes.None);
        if (!AlsP5aTrace.JsonEvidenceEquals(actual, expected))
        {
            throw Invalid(
                AlsP5aTrace.FindFirstDifferencePath(actual, expected, "$raw.nativeReferenceAudit"),
                "physical inventory differs from the approved audit");
        }
    }

    private static void ValidateCases(JsonObject plan, JsonObject raw, SourceIndex sources)
    {
        var planCases = plan["cases"]!.AsArray();
        var rawCases = raw["cases"]!.AsArray();
        if (planCases.Count != rawCases.Count)
            throw Invalid("$raw.cases", "case count differs from the plan");

        for (var caseIndex = 0; caseIndex < planCases.Count; caseIndex++)
        {
            var planCase = planCases[caseIndex]!.AsObject();
            var rawCase = rawCases[caseIndex]!.AsObject();
            if (rawCase["ordinal"]!.GetValue<int>() != planCase["ordinal"]!.GetValue<int>() ||
                rawCase["caseId"]!.GetValue<string>() != planCase["caseId"]!.GetValue<string>())
            {
                throw Invalid($"$raw.cases[{caseIndex}]", "case identity differs from the plan");
            }
            ValidateCase(planCase, rawCase, caseIndex, sources);
        }
    }

    private static void ValidateCase(
        JsonObject planCase,
        JsonObject rawCase,
        int caseIndex,
        SourceIndex sources)
    {
        var planFrames = planCase["frames"]!.AsArray();
        var rawFrames = rawCase["frames"]!.AsArray();
        if (planFrames.Count != rawFrames.Count)
            throw Invalid($"$raw.cases[{caseIndex}].frames", "frame count differs from the plan");

        var action = new ActionLifecycle();
        var graph = new SemanticGraphState();
        var canonicalStates = new HashSet<string>(StringComparer.Ordinal);
        var physicalStates = new HashSet<string>(StringComparer.Ordinal);
        JsonObject? previousTransition = null;
        JsonObject? previousSync = null;
        string? receiptContract = null;

        for (var frameIndex = 0; frameIndex < planFrames.Count; frameIndex++)
        {
            var planFrame = planFrames[frameIndex]!.AsObject();
            var rawFrame = rawFrames[frameIndex]!.AsObject();
            if (planFrame["frameIndex"]!.GetValue<int>() != frameIndex ||
                rawFrame["frameIndex"]!.GetValue<int>() != frameIndex)
            {
                throw Invalid($"$raw.cases[{caseIndex}].frames[{frameIndex}]", "frame index is not contiguous");
            }

            var input = planFrame["input"]!.AsObject();
            var actual = rawFrame["nativeActual"]!.AsObject();
            var path = $"$raw.cases[{caseIndex}].frames[{frameIndex}].nativeActual";
            ValidateFrameScalars(actual, path);
            ValidateCanonicalEvents(actual["canonicalAssetOracle"]!.AsObject(), input, path,
                sources, canonicalStates);
            ValidatePhysicalTimeline(actual, input, path,
                sources, physicalStates);
            ValidateAction(input, actual, path, sources, action);
            ValidateTransition(input, actual, path, sources, ref previousTransition, ref receiptContract);
            ValidateSemanticGraphWeights(input, actual, path, graph);
            ValidateSync(actual["canonicalAssetOracle"]!["sync"]!.AsObject(), path, sources,
                ref previousSync);
        }
    }

    private static void ValidateFrameScalars(JsonObject actual, string path)
    {
        var counters = actual["frameUpdateAudit"]!.AsObject();
        foreach (var name in new[] { "animationUpdates", "evaluations", "postUpdates", "meshTicks" })
        {
            if (counters[name]!.GetValue<int>() != 1)
                throw Invalid($"{path}.frameUpdateAudit.{name}", "must be exactly one");
        }

        var oracle = actual["canonicalAssetOracle"]!.AsObject();
        ValidateUnitScalars(oracle["curves"]!.AsObject(), $"{path}.canonicalAssetOracle.curves");
        ValidateUnitScalars(oracle["compressedCurves"]!.AsObject(), $"{path}.canonicalAssetOracle.compressedCurves");
        ValidateUnitScalars(oracle["graphCurveWeights"]!.AsObject(), $"{path}.canonicalAssetOracle.graphCurveWeights");
        foreach (var (name, node) in actual["animGraphCurveAudit"]!.AsObject())
        {
            var audit = node!.AsObject();
            var present = audit["present"]!.GetValue<bool>();
            var value = FiniteFloat(audit, "value", $"{path}.animGraphCurveAudit.{name}");
            if (!present && value != 0f)
                throw Invalid($"{path}.animGraphCurveAudit.{name}", "curve audit is outside its physical range");
        }
    }

    private static void ValidateCanonicalEvents(
        JsonObject oracle,
        JsonObject input,
        string path,
        SourceIndex sources,
        HashSet<string> active)
    {
        var delta = FiniteFloat(input["window"]!.AsObject(), "deltaSeconds", "$plan.input.window");
        var events = oracle["events"]!.AsArray();
        for (var index = 0; index < events.Count; index++)
        {
            var observed = events[index]!.AsObject();
            var eventPath = $"{path}.canonicalAssetOracle.events[{index}]";
            if (observed["frameEventOrdinal"]!.GetValue<int>() != index)
                throw Invalid(eventPath, "frameEventOrdinal does not preserve raw callback order");
            RequireOrdinal(observed, "nativeInstanceOrdinal", eventPath);
            RequireCycle(observed, "playbackCycle", eventPath);
            ValidateOffsetAndWeight(observed, delta, eventPath, hasWeight: true);
            var isState = sources.ResolveCanonicalEvent(observed, eventPath);

            var phase = observed["phase"]!.GetValue<string>();
            ValidatePhaseKind(isState, phase, eventPath);
            var key = CanonicalStateKey(observed);
            ApplyStatePhase(active, key, phase, eventPath);
            var termination = observed["nativeTerminationReason"]!.GetValue<string>();
            if (termination != "None" && !(phase == "End" && termination == "Cancelled"))
                throw Invalid(eventPath, "termination reason is inconsistent with the callback phase");
        }

        var owners = oracle["activeNotifyStates"]!.AsArray();
        var observedOwners = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index]!.AsObject();
            var ownerPath = $"{path}.canonicalAssetOracle.activeNotifyStates[{index}]";
            RequireOrdinal(owner, "nativeInstanceOrdinal", ownerPath);
            RequireCycle(owner, "playbackCycle", ownerPath);
            sources.ResolveCanonicalOwner(owner, ownerPath);
            var key = CanonicalOwnerKey(owner);
            if (!observedOwners.Add(key))
                throw Invalid(ownerPath, "duplicate active notify owner");
        }
        if (!active.SetEquals(observedOwners))
            throw Invalid($"{path}.canonicalAssetOracle.activeNotifyStates", "active owner snapshot disagrees with callbacks");
    }

    private static void ValidatePhysicalTimeline(
        JsonObject actual,
        JsonObject input,
        string path,
        SourceIndex sources,
        HashSet<string> active)
    {
        var timeline = actual["nativeRuntimeTimeline"]!.AsArray();
        var delta = FiniteFloat(input["window"]!.AsObject(), "deltaSeconds", "$plan.input.window");
        for (var index = 0; index < timeline.Count; index++)
        {
            var observed = timeline[index]!.AsObject();
            var eventPath = $"{path}.nativeRuntimeTimeline[{index}]";
            ValidateOffsetAndWeight(observed, delta, eventPath, hasWeight: false);
            var definition = sources.ResolvePhysicalEvent(observed, eventPath);
            var phase = observed["phase"]!.GetValue<string>();
            ValidatePhaseKind(definition.IsState, phase, eventPath);
            if (phase == "Trigger")
                ValidateMappedTriggerTiming(actual, observed, definition, delta, eventPath);
            ApplyStatePhase(active, PhysicalStateKey(observed), phase, eventPath);
        }
    }

    private static void ValidateMappedTriggerTiming(
        JsonObject actual,
        JsonObject observed,
        PhysicalEventDefinition definition,
        float delta,
        string path)
    {
        if (definition.TraceSourceId == "audit") return;
        JsonObject playback;
        string sourceMember;
        string previousMember;
        string currentMember;
        if (definition.SourceKind == "Transition")
        {
            playback = actual["dynamicTransition"]!.AsObject();
            sourceMember = "source";
            previousMember = "previousTimeSeconds";
            currentMember = "currentTimeSeconds";
        }
        else if (definition.SourceKind is "ActionMontage" or "ActionSequence")
        {
            playback = actual["actionPlayback"]!.AsObject();
            sourceMember = definition.SourceKind == "ActionMontage" ? "montageSource" : "segmentSource";
            previousMember = definition.SourceKind == "ActionMontage"
                ? "previousMontageTimeSeconds" : "previousClipTimeSeconds";
            currentMember = definition.SourceKind == "ActionMontage"
                ? "currentMontageTimeSeconds" : "currentClipTimeSeconds";
        }
        else
        {
            return;
        }

        var active = definition.SourceKind == "Transition"
            ? playback["active"]!.GetValue<bool>()
            : playback["status"]!.GetValue<string>() is "Playing" or "ClosingThisFrame";
        if (!active || NativeObservedSourceKey(playback[sourceMember]!.AsObject()) !=
            NativeObservedSourceKey(observed["source"]!.AsObject()))
            throw Invalid(path, "mapped physical trigger has no matching active playback");
        var previous = FiniteFloat(playback, previousMember, path);
        var current = FiniteFloat(playback, currentMember, path);
        if (definition.TimeSeconds < previous || definition.TimeSeconds > current || current <= previous)
            throw Invalid(path, "mapped physical trigger did not cross its authored boundary this frame");
        var elapsedPlayback = definition.TimeSeconds - previous;
        var observedOffset = FiniteFloat(observed, "observedFrameOffsetSeconds", path);
        // Authored boundaries within float tolerance of the frame start are ambiguous after UE float
        // accumulation. Their direction is still strict, but the raw in-frame offset remains authoritative.
        if (elapsedPlayback > FloatTolerance &&
            MathF.Abs(observedOffset - elapsedPlayback / (current - previous) * delta) > FloatTolerance)
            throw Invalid(path, "mapped physical trigger offset disagrees with playback crossing");
    }

    private static string NativeObservedSourceKey(JsonObject value) => string.Join('|',
        value["observedAssetObjectPath"], value["observedAssetStableId"], value["observedAssetPackageSha256"],
        value["observedAssetClassPath"], value["observedMontageObjectPath"],
        value["observedMontageStableId"], value["observedSectionName"], value["observedSlotName"],
        value["observedSegmentIndex"]);

    private static void ValidateAction(
        JsonObject input,
        JsonObject actual,
        string path,
        SourceIndex sources,
        ActionLifecycle lifecycle)
    {
        var command = input["actionRequest"]!["command"]!.GetValue<string>();
        var outcomes = actual["actionOutcomes"]!.AsArray();
        var playback = actual["actionPlayback"]!.AsObject();
        var status = playback["status"]!.GetValue<string>();
        var state = actual["stateAfter"]!.AsObject();
        var terminalCount = 0;

        foreach (var node in outcomes)
        {
            var outcome = node!.AsObject();
            var reason = outcome["nativeReason"]!.GetValue<string>();
            var callback = outcome["callback"]!.GetValue<string>();
            var interrupted = outcome["interrupted"]!.GetValue<bool>();
            var ordinal = RequireOrdinal(outcome, "nativeInstanceOrdinal", path);
            var sourceKey = sources.ResolveNativeSource(outcome["actionSource"]!.AsObject(), $"{path}.actionOutcomes");
            if (sources.NativeKind(sourceKey) != "ActionMontage")
                throw Invalid($"{path}.actionOutcomes", "action outcome source is not an action montage");

            if (reason == "Started")
            {
                if (callback != "MontageStarted" || interrupted || command != "Start" || lifecycle.Started)
                    throw Invalid($"{path}.actionOutcomes", "invalid or duplicate Started outcome");
                lifecycle.Started = true;
                lifecycle.Source = sourceKey;
                lifecycle.Ordinal = ordinal;
            }
            else if (reason is "Finished" or "Cancelled")
            {
                terminalCount++;
                var expectedCallback = reason == "Finished" ? "MontageEnded" : "MontageBlendingOutStarted";
                if (!lifecycle.Started || lifecycle.Terminal || callback != expectedCallback ||
                    interrupted != (reason == "Cancelled") || command == "Cancel" != (reason == "Cancelled") ||
                    sourceKey != lifecycle.Source || ordinal != lifecycle.Ordinal)
                {
                    throw Invalid($"{path}.actionOutcomes", "terminal outcome is inconsistent with action lifecycle");
                }
                lifecycle.Terminal = true;
            }
            else
            {
                throw Invalid($"{path}.actionOutcomes", $"unsupported native outcome '{reason}'");
            }
        }

        if (command == "Start" && outcomes.Count(node => node!["nativeReason"]!.GetValue<string>() == "Started") != 1)
            throw Invalid($"{path}.actionOutcomes", "Start command requires exactly one Started outcome");
        if (command == "Cancel" && outcomes.Count(node => node!["nativeReason"]!.GetValue<string>() == "Cancelled") != 1)
            throw Invalid($"{path}.actionOutcomes", "Cancel command requires exactly one Cancelled outcome");

        if (status is "Playing" or "ClosingThisFrame")
        {
            if (!lifecycle.Started || lifecycle.Terminal && status != "ClosingThisFrame")
                throw Invalid($"{path}.actionPlayback", "action playback exists outside a live lifecycle");
            var montage = sources.ResolveNativeSource(playback["montageSource"]!.AsObject(), $"{path}.actionPlayback.montageSource");
            var segment = sources.ResolveNativeSource(playback["segmentSource"]!.AsObject(), $"{path}.actionPlayback.segmentSource");
            var ordinal = RequireOrdinal(playback, "nativeInstanceOrdinal", path);
            if (montage != lifecycle.Source || sources.NativeKind(montage) != "ActionMontage" ||
                sources.NativeKind(segment) != "ActionSequence" || ordinal != lifecycle.Ordinal)
                throw Invalid($"{path}.actionPlayback", "action playback identity drifted");
            var previous = FiniteFloat(playback, "previousMontageTimeSeconds", path);
            var current = FiniteFloat(playback, "currentMontageTimeSeconds", path);
            var previousClip = FiniteFloat(playback, "previousClipTimeSeconds", path);
            var currentClip = FiniteFloat(playback, "currentClipTimeSeconds", path);
            var finalDelta = FiniteFloat(playback, "finalSegmentDeltaSeconds", path);
            var rate = FiniteFloat(playback, "playRate", path);
            if (previous < 0f || current < previous || previousClip < 0f || currentClip < previousClip ||
                finalDelta < 0f || rate <= 0f ||
                status == "Playing" && finalDelta != 0f ||
                lifecycle.HasPlayback &&
                    (MathF.Abs(previous - lifecycle.LastMontageTime) > FloatTolerance ||
                     MathF.Abs(previousClip - lifecycle.LastClipTime) > FloatTolerance ||
                     !SameFloat(rate, lifecycle.PlayRate)))
                throw Invalid($"{path}.actionPlayback", "action time or rate is invalid");
            if (!lifecycle.HasPlayback)
                lifecycle.PlayRate = rate;
            lifecycle.HasPlayback = true;
            lifecycle.LastMontageTime = current;
            lifecycle.LastClipTime = currentClip;
            if (playback["currentSectionName"]!.GetValue<string>() !=
                    playback["montageSource"]!["observedSectionName"]!.GetValue<string>() ||
                playback["segmentIndex"]!.GetValue<int>() !=
                    playback["segmentSource"]!["observedSegmentIndex"]!.GetValue<int>())
                throw Invalid($"{path}.actionPlayback", "section or segment evidence drifted from its source");
            if (status == "ClosingThisFrame" && terminalCount != 1 || status == "Playing" && terminalCount != 0)
                throw Invalid($"{path}.actionPlayback", "ClosingThisFrame and terminal outcome disagree");
        }
        else if (status != "Inactive")
        {
            throw Invalid($"{path}.actionPlayback.status", $"unsupported action status '{status}'");
        }
        else if (terminalCount != 0)
        {
            throw Invalid($"{path}.actionPlayback", "terminal outcome requires a closing playback snapshot");
        }
        else
        {
            if (lifecycle.Started && !lifecycle.Terminal)
                throw Invalid($"{path}.actionPlayback", "live action lifecycle disappeared without a terminal outcome");
            ValidateInactiveActionPlayback(playback, $"{path}.actionPlayback");
        }

        var playingAfter = state["actionPlaying"]!.GetValue<bool>();
        if (playingAfter != (status == "Playing"))
            throw Invalid($"{path}.stateAfter.actionPlaying", "state does not agree with playback closure");
        if (playingAfter)
        {
            var source = sources.ResolveNativeSource(state["actionSource"]!.AsObject(), $"{path}.stateAfter.actionSource");
            if (source != lifecycle.Source || RequireOrdinal(state, "actionNativeInstanceOrdinal", path) != lifecycle.Ordinal)
                throw Invalid($"{path}.stateAfter", "action state identity drifted");
            if (MathF.Abs(FiniteFloat(state, "actionTimeSeconds", path) -
                          FiniteFloat(playback, "currentMontageTimeSeconds", path)) > FloatTolerance)
                throw Invalid($"{path}.stateAfter.actionTimeSeconds", "action state time differs from playback");
        }
        else
        {
            ValidateInactiveActionState(state, path);
        }
        if (lifecycle.Terminal && status != "ClosingThisFrame" && playingAfter)
            throw Invalid($"{path}.stateAfter", "action resurrected after terminal callback");

        var visual = actual["actionVisualContribution"]!.AsObject();
        var contributing = visual["contributing"]!.GetValue<bool>();
        var weight = FiniteFloat(visual, "observedEffectiveWeight", path);
        if (weight < 0f || weight > 1f || !contributing && weight != 0f)
            throw Invalid($"{path}.actionVisualContribution", "visual contribution is outside its physical range");
        if (contributing)
        {
            var source = sources.ResolveNativeSource(visual["montageSource"]!.AsObject(), $"{path}.actionVisualContribution.montageSource");
            if (!lifecycle.Started || source != lifecycle.Source ||
                RequireOrdinal(visual, "nativeInstanceOrdinal", path) != lifecycle.Ordinal)
                throw Invalid($"{path}.actionVisualContribution", "visual tail identity drifted");
            var time = FiniteFloat(visual, "observedMontageTimeSeconds", path);
            if (time < 0f || time > sources.NativeDuration(source) + FloatTolerance ||
                lifecycle.HasVisual && time + FloatTolerance < lifecycle.LastVisualTime ||
                !SameFloat(FiniteFloat(visual, "observedBlendInSeconds", path), .1f) ||
                !SameFloat(FiniteFloat(visual, "observedBlendOutSeconds", path), .3f) ||
                visual["observedBlendInOption"]!.GetValue<int>() != 2 ||
                visual["observedBlendOutOption"]!.GetValue<int>() != 2)
                throw Invalid($"{path}.actionVisualContribution", "visual time or frozen blend configuration is invalid");
            lifecycle.HasVisual = true;
            lifecycle.LastVisualTime = time;
        }
        else
        {
            ValidateInactiveActionVisual(visual, $"{path}.actionVisualContribution");
        }
    }

    private static void ValidateTransition(
        JsonObject input,
        JsonObject actual,
        string path,
        SourceIndex sources,
        ref JsonObject? previous,
        ref string? receiptContract)
    {
        var probe = input["transitionProbe"]!.AsObject();
        var requested = probe["left"]!["relevant"]!.GetValue<bool>() ||
                        probe["right"]!["relevant"]!.GetValue<bool>();
        var receipts = actual["transitionStimulusReceipts"]!.AsArray();
        if (receipts.Count != (requested ? 1 : 0))
            throw Invalid($"{path}.transitionStimulusReceipts", "receipt position disagrees with transition probe input");
        if (receipts.Count == 1)
        {
            var receipt = receipts[0]!.AsObject();
            receiptContract ??= TransitionHookContractSha256;
            if (receipt["hookContractSha256"]!.GetValue<string>() != receiptContract ||
                !receipt["preHookUpdatedThisFrame"]!.GetValue<bool>() ||
                !receipt["postHookUpdatedThisFrame"]!.GetValue<bool>() ||
                !receipt["restoreVerified"]!.GetValue<bool>() ||
                receipt["preHookFrameDelay"]!.GetValue<int>() != 0 ||
                receipt["postHookFrameDelay"]!.GetValue<int>() != 2 ||
                receipt["preHookTransitionActive"]!.GetValue<bool>() ||
                !SameFloat(FiniteFloat(receipt, "observedAllowTransitions", path), 1f))
                throw Invalid($"{path}.transitionStimulusReceipts[0]", "transition hook receipt is invalid");
            var receiptPath = $"{path}.transitionStimulusReceipts[0]";
            ValidateReceiptFoot(probe["left"]!.AsObject(), receipt["left"]!.AsObject(), $"{receiptPath}.left");
            ValidateReceiptFoot(probe["right"]!.AsObject(), receipt["right"]!.AsObject(), $"{receiptPath}.right");
        }

        var transition = actual["dynamicTransition"]!.AsObject();
        var state = actual["stateAfter"]!.AsObject();
        var active = transition["active"]!.GetValue<bool>();
        if (transition["activatedAfterUpdate"]!.GetValue<bool>() != requested ||
            state["transitionPlaying"]!.GetValue<bool>() != active)
            throw Invalid($"{path}.dynamicTransition", "transition activation or state disagrees with input");
        if (active)
        {
            var source = sources.ResolveNativeSource(transition["source"]!.AsObject(), $"{path}.dynamicTransition.source");
            var stateSource = sources.ResolveNativeSource(state["transitionSource"]!.AsObject(), $"{path}.stateAfter.transitionSource");
            var nativeSourceKey = sources.ResolveNativeSourceKey(
                transition["source"]!.AsObject(), $"{path}.dynamicTransition.source");
            var stateNativeSourceKey = sources.ResolveNativeSourceKey(
                state["transitionSource"]!.AsObject(), $"{path}.stateAfter.transitionSource");
            var ordinal = RequireOrdinal(transition, "nativeInstanceOrdinal", path);
            var current = FiniteFloat(transition, "currentTimeSeconds", path);
            var previousTime = FiniteFloat(transition, "previousTimeSeconds", path);
            var rate = FiniteFloat(transition, "playRate", path);
            var weight = FiniteFloat(transition, "observedEffectiveWeight", path);
            var blendIn = FiniteFloat(transition, "observedBlendInSeconds", path);
            var blendOut = FiniteFloat(transition, "observedBlendOutSeconds", path);
            var foot = transition["foot"]!.GetValue<string>();
            var stance = input["modes"]!["stance"]!.GetValue<string>();
            sources.RequireTransitionVariant(source, nativeSourceKey, stance, foot,
                $"{path}.dynamicTransition.source");
            if (transition["activatedAfterUpdate"]!.GetValue<bool>() &&
                    !probe[foot.ToLowerInvariant()]!["relevant"]!.GetValue<bool>() ||
                sources.NativeKind(source) != "Transition" || source != stateSource ||
                nativeSourceKey != stateNativeSourceKey ||
                ordinal != RequireOrdinal(state, "transitionNativeInstanceOrdinal", path) ||
                foot != state["transitionFoot"]!.GetValue<string>() ||
                current < previousTime || current > sources.NativeDuration(source) + FloatTolerance ||
                rate <= 0f || weight < 0f || weight > 1f ||
                !SameFloat(blendIn, .2f) || !SameFloat(blendOut, .2f) ||
                MathF.Abs(FiniteFloat(state, "transitionTimeSeconds", path) - current) > FloatTolerance ||
                state["transitionCooldownFrames"]!.GetValue<int>() < 0 ||
                previous is not null && (nativeSourceKey != previous["source"]!.GetValue<string>() ||
                    ordinal != previous["ordinal"]!.GetValue<ulong>() ||
                    MathF.Abs(previousTime - previous["time"]!.GetValue<float>()) > FloatTolerance))
                throw Invalid($"{path}.dynamicTransition", "transition identity, continuity, or range is invalid");
            previous = new JsonObject { ["source"] = nativeSourceKey, ["ordinal"] = ordinal, ["time"] = current };
        }
        else
        {
            ValidateInactiveTransition(transition, $"{path}.dynamicTransition");
            if (state["transitionPlaying"]!.GetValue<bool>())
                throw Invalid($"{path}.stateAfter.transitionPlaying", "inactive transition retained active state");
            ValidateInactiveTransitionState(state, path);
            previous = null;
        }
    }

    private static void ValidateSemanticGraphWeights(
        JsonObject input,
        JsonObject actual,
        string path,
        SemanticGraphState state)
    {
        var delta = FiniteFloat(input["window"]!.AsObject(), "deltaSeconds", "$plan.input.window");
        var outcomes = actual["actionOutcomes"]!.AsArray();
        var started = outcomes.Any(node => node!["nativeReason"]!.GetValue<string>() == "Started");
        var finished = outcomes.Any(node => node!["nativeReason"]!.GetValue<string>() == "Finished");
        var cancelled = outcomes.Any(node => node!["nativeReason"]!.GetValue<string>() == "Cancelled");
        if (started)
        {
            state.ActionActive = true;
            state.ActionWeight = StepSemanticWeight(state.ActionWeight, 1f, delta);
        }
        else if (finished || cancelled)
        {
            var activeDelta = 0f;
            if (finished)
            {
                var playback = actual["actionPlayback"]!.AsObject();
                activeDelta = global::System.Math.Clamp(
                    FiniteFloat(playback, "currentMontageTimeSeconds", path) -
                    FiniteFloat(playback, "previousMontageTimeSeconds", path), 0f, delta);
            }
            state.ActionWeight = StepSemanticWeight(state.ActionWeight, 1f, activeDelta);
            state.ActionWeight = StepSemanticWeight(state.ActionWeight, 0f, delta - activeDelta);
            state.ActionActive = false;
        }
        else
        {
            state.ActionWeight = StepSemanticWeight(
                state.ActionWeight, state.ActionActive ? 1f : 0f, delta);
        }

        var transitionWasActive = state.TransitionActive;
        state.TransitionActive = actual["dynamicTransition"]!["active"]!.GetValue<bool>();
        if (transitionWasActive)
        {
            state.TransitionWeight = StepSemanticWeight(
                state.TransitionWeight, state.TransitionActive ? 1f : 0f, delta);
        }

        var weights = actual["canonicalAssetOracle"]!["graphCurveWeights"]!.AsObject();
        if (MathF.Abs(FiniteFloat(weights, "action", path) - state.ActionWeight) > FloatTolerance ||
            MathF.Abs(FiniteFloat(weights, "transition", path) - state.TransitionWeight) > FloatTolerance)
            throw Invalid($"{path}.canonicalAssetOracle.graphCurveWeights",
                "semantic weights disagree with lifecycle recurrence");
    }

    private static float StepSemanticWeight(float weight, float target, float delta)
    {
        var step = (float)((double)delta / (double).2f);
        return target > weight ? MathF.Min(target, weight + step) : MathF.Max(target, weight - step);
    }

    private static void ValidateReceiptFoot(JsonObject probe, JsonObject receipt, string path)
    {
        foreach (var (expectedName, observedName) in new[]
                 {
                     ("targetMeters", "observedTargetMeters"),
                     ("lockMeters", "observedLockMeters"),
                 })
        {
            var expected = probe[expectedName]!.AsObject();
            var observed = receipt[observedName]!.AsObject();
            foreach (var axis in new[] { "x", "y", "z" })
            {
                if (!AlsP5aTrace.JsonEvidenceEquals(expected[axis], observed[axis]))
                    throw Invalid($"{path}.{observedName}.{axis}", "receipt does not reproduce the plan probe");
            }
        }

        if (receipt["observedLockAmount"]!.GetValue<float>() !=
            (probe["relevant"]!.GetValue<bool>() ? 1f : 0f))
            throw Invalid($"{path}.observedLockAmount", "receipt does not reproduce the plan probe");
    }

    private static void ValidateSync(JsonObject sync, string path, SourceIndex sources, ref JsonObject? previous)
    {
        if (!sync["active"]!.GetValue<bool>())
        {
            ValidateInactiveSync(sync, $"{path}.canonicalAssetOracle.sync");
            previous = null;
            return;
        }
        var source = sources.ResolveCanonicalSource(sync["leader"]!.AsObject(), $"{path}.canonicalAssetOracle.sync.leader");
        if (sources.CanonicalKind(source) != "Base")
            throw Invalid($"{path}.canonicalAssetOracle.sync", "sync leader is not a base source");
        sources.ResolveMarker(source, sync["previousMarker"]!.AsObject(), path);
        sources.ResolveMarker(source, sync["nextMarker"]!.AsObject(), path);
        var ordinal = RequireOrdinal(sync, "nativeInstanceOrdinal", path);
        foreach (var name in new[] { "phase", "leftFootPhase", "rightFootPhase" })
        {
            var value = FiniteFloat(sync, name, path);
            if (value < 0f || value > 1f)
                throw Invalid($"{path}.canonicalAssetOracle.sync.{name}", "sync phase is outside [0,1]");
        }
        if (previous is not null &&
            (previous["source"]!.GetValue<string>() != source || previous["ordinal"]!.GetValue<ulong>() != ordinal))
            throw Invalid($"{path}.canonicalAssetOracle.sync", "sync identity drifted");
        previous = new JsonObject { ["source"] = source, ["ordinal"] = ordinal };
    }

    private static void ValidateOffsetAndWeight(JsonObject value, float delta, string path, bool hasWeight)
    {
        var offset = FiniteFloat(value, "observedFrameOffsetSeconds", path);
        if (offset < 0f || offset > delta + FloatTolerance)
            throw Invalid(path, "frame offset is outside the input window");
        if (hasWeight)
        {
            var weight = FiniteFloat(value, "observedWeight", path);
            if (weight < 0f || weight > 1f)
                throw Invalid(path, "observed weight is outside [0,1]");
        }
    }

    private static void ApplyStatePhase(HashSet<string> active, string key, string phase, string path)
    {
        if (phase == "Trigger") return;
        if (phase == "Begin")
        {
            if (!active.Add(key)) throw Invalid(path, "duplicate Begin for an active notify state");
            return;
        }
        if (phase == "Tick")
        {
            if (!active.Contains(key)) throw Invalid(path, "Tick has no active notify state");
            return;
        }
        if (phase == "End")
        {
            if (!active.Remove(key)) throw Invalid(path, "End has no active notify state");
            return;
        }
        throw Invalid(path, $"unsupported notify phase '{phase}'");
    }

    private static void ValidatePhaseKind(bool isState, string phase, string path)
    {
        if (isState ? phase is not ("Begin" or "Tick" or "End") : phase != "Trigger")
            throw Invalid(path, isState
                ? "state notify must use Begin, Tick, or End"
                : "instant notify must use Trigger");
    }

    private static void ValidateInactiveActionPlayback(JsonObject value, string path)
    {
        if (!EmptyNativeSource(value["montageSource"]!.AsObject()) ||
            !EmptyNativeSource(value["segmentSource"]!.AsObject()) ||
            value["nativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            value["currentSectionName"]!.GetValue<string>() != string.Empty ||
            value["segmentIndex"]!.GetValue<int>() != -1 ||
            !AllZero(value, "previousMontageTimeSeconds", "currentMontageTimeSeconds",
                "previousClipTimeSeconds", "currentClipTimeSeconds", "finalSegmentDeltaSeconds", "playRate"))
            throw Invalid(path, "inactive action playback is not the exact sentinel");
    }

    private static void ValidateInactiveActionVisual(JsonObject value, string path)
    {
        if (!EmptyNativeSource(value["montageSource"]!.AsObject()) ||
            value["nativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            value["observedBlendInOption"]!.GetValue<int>() != -1 ||
            value["observedBlendOutOption"]!.GetValue<int>() != -1 ||
            !AllZero(value, "observedMontageTimeSeconds", "observedBlendInSeconds",
                "observedBlendOutSeconds", "observedEffectiveWeight"))
            throw Invalid(path, "inactive action visual is not the exact sentinel");
    }

    private static void ValidateInactiveActionState(JsonObject value, string path)
    {
        if (!EmptyNativeSource(value["actionSource"]!.AsObject()) ||
            value["actionNativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            FiniteFloat(value, "actionTimeSeconds", path) != 0f)
            throw Invalid($"{path}.stateAfter", "inactive action state is not the exact sentinel");
    }

    private static void ValidateInactiveTransition(JsonObject value, string path)
    {
        if (!EmptyNativeSource(value["source"]!.AsObject()) ||
            value["nativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            value["foot"]!.GetValue<string>() != "Left" ||
            value["activatedAfterUpdate"]!.GetValue<bool>() ||
            !AllZero(value, "previousTimeSeconds", "currentTimeSeconds", "playRate",
                "observedBlendInSeconds", "observedBlendOutSeconds", "observedEffectiveWeight"))
            throw Invalid(path, "inactive transition is not the exact sentinel");
    }

    private static void ValidateInactiveTransitionState(JsonObject value, string path)
    {
        if (!EmptyNativeSource(value["transitionSource"]!.AsObject()) ||
            value["transitionNativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            value["transitionFoot"]!.GetValue<string>() != "Left" ||
            FiniteFloat(value, "transitionTimeSeconds", path) != 0f ||
            value["transitionCooldownFrames"]!.GetValue<int>() != 0)
            throw Invalid($"{path}.stateAfter", "inactive transition state is not the exact sentinel");
    }

    private static void ValidateInactiveSync(JsonObject value, string path)
    {
        if (!EmptyCanonicalSource(value["leader"]!.AsObject()) ||
            value["nativeInstanceOrdinal"]!.GetValue<string>() != "0" ||
            !EmptyMarker(value["previousMarker"]!.AsObject()) || !EmptyMarker(value["nextMarker"]!.AsObject()) ||
            value["cycle"]!.GetValue<string>() != "0" ||
            !AllZero(value, "phase", "leftFootPhase", "rightFootPhase"))
            throw Invalid(path, "inactive sync is not the exact sentinel");
    }

    private static bool EmptyNativeSource(JsonObject value) =>
        value.Where(pair => pair.Key != "observedSegmentIndex")
            .All(pair => pair.Value!.GetValue<string>() == string.Empty) &&
        value["observedSegmentIndex"]!.GetValue<int>() == -1;

    private static bool EmptyCanonicalSource(JsonObject value) =>
        value.Where(pair => pair.Key != "observedSegmentIndex")
            .All(pair => pair.Value!.GetValue<string>() == string.Empty) &&
        value["observedSegmentIndex"]!.GetValue<int>() == -1;

    private static bool EmptyMarker(JsonObject value) =>
        value["stableMarkerId"]!.GetValue<string>() == string.Empty &&
        value["name"]!.GetValue<string>() == string.Empty &&
        value["sourceIndex"]!.GetValue<int>() == -1 && value["trackIndex"]!.GetValue<int>() == -1 &&
        FiniteFloat(value, "timeSeconds", "$raw.marker") == 0f;

    private static bool AllZero(JsonObject value, params string[] names) =>
        names.All(name => FiniteFloat(value, name, "$raw") == 0f);

    private static bool SameFloat(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static string CanonicalStateKey(JsonObject value) =>
        $"{value["source"]!["observedAssetStableId"]!.GetValue<string>()}|" +
        $"{value["observedEventStableId"]!.GetValue<string>()}|" +
        $"{value["nativeInstanceOrdinal"]!.GetValue<string>()}|{value["playbackCycle"]!.GetValue<string>()}";

    private static string CanonicalOwnerKey(JsonObject value) =>
        $"{value["source"]!["observedAssetStableId"]!.GetValue<string>()}|" +
        $"{value["observedEventStableId"]!.GetValue<string>()}|" +
        $"{value["nativeInstanceOrdinal"]!.GetValue<string>()}|{value["playbackCycle"]!.GetValue<string>()}";

    private static string PhysicalStateKey(JsonObject value) =>
        $"{value["source"]!["observedAssetStableId"]!.GetValue<string>()}|" +
        $"{value["observedEventStableId"]!.GetValue<string>()}|" +
        $"{value["observedSourceIndex"]!.GetValue<int>()}|{value["observedTrackIndex"]!.GetValue<int>()}";

    private static void ValidateUnitScalars(JsonObject values, string path)
    {
        foreach (var (name, _) in values)
        {
            var value = FiniteFloat(values, name, path);
            if (value < 0f || value > 1f)
                throw Invalid($"{path}.{name}", "value is outside [0,1]");
        }
    }

    private static float FiniteFloat(JsonObject value, string name, string path)
    {
        if (value[name] is not JsonValue node || !node.TryGetValue<float>(out var result) || !float.IsFinite(result))
            throw Invalid($"{path}.{name}", "must be a finite number");
        return result;
    }

    private static ulong RequireOrdinal(JsonObject value, string name, string path)
    {
        var text = value[name]!.GetValue<string>();
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) ||
            ordinal == 0 || text != ordinal.ToString(CultureInfo.InvariantCulture))
            throw Invalid($"{path}.{name}", "must be a canonical positive ordinal");
        return ordinal;
    }

    private static ulong RequireCycle(JsonObject value, string name, string path)
    {
        var text = value[name]!.GetValue<string>();
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var cycle) ||
            text != cycle.ToString(CultureInfo.InvariantCulture))
            throw Invalid($"{path}.{name}", "must be a canonical nonnegative ordinal");
        return cycle;
    }

    private static void ValidateShape(JsonNode template, JsonNode? actual, string path, RuntimeShapes shapes)
    {
        if (template is JsonObject templateObject)
        {
            if (actual is not JsonObject actualObject || actualObject.Count != templateObject.Count)
                throw Invalid(path, "object keys differ from the closed evidence shape");
            foreach (var (name, childTemplate) in templateObject)
            {
                if (!actualObject.TryGetPropertyValue(name, out var child) || childTemplate is null)
                    throw Invalid($"{path}.{name}", "required property is missing");
                ValidateShape(childTemplate, child, $"{path}.{name}", shapes);
            }
            return;
        }
        if (template is JsonArray templateArray)
        {
            if (actual is not JsonArray actualArray)
                throw Invalid(path, "must be an array");
            if (shapes.TryGet(path, out var itemShape, out var capacity))
            {
                if (actualArray.Count > capacity) throw Invalid(path, $"exceeds capacity {capacity}");
                for (var index = 0; index < actualArray.Count; index++)
                    ValidateShape(itemShape!, actualArray[index], $"{path}[{index}]", RuntimeShapes.None);
                return;
            }
            if (actualArray.Count != templateArray.Count)
                throw Invalid(path, "array length differs from the closed evidence shape");
            for (var index = 0; index < templateArray.Count; index++)
                ValidateShape(templateArray[index]!, actualArray[index], $"{path}[{index}]", shapes);
            return;
        }
        if (actual is not JsonValue actualValue || template is not JsonValue templateValue ||
            !SameLeafKind(template.GetValueKind(), actual.GetValueKind()))
            throw Invalid(path, "JSON leaf type differs from the closed evidence shape");
        if (template.GetValueKind() == JsonValueKind.Number &&
            templateValue.TryGetValue<int>(out _) && !actualValue.TryGetValue<int>(out _))
            throw Invalid(path, "must be an integer");
    }

    private static bool SameLeafKind(JsonValueKind template, JsonValueKind actual) =>
        template == actual || template is JsonValueKind.True or JsonValueKind.False &&
            actual is JsonValueKind.True or JsonValueKind.False;

    private static void RequireString(JsonObject value, string name, string expected, string path)
    {
        if (value[name] is not JsonValue node || !node.TryGetValue<string>(out var actual) || actual != expected)
            throw Invalid($"{path}.{name}", $"must equal '{expected}'");
    }

    private static InvalidDataException Invalid(string path, string reason) =>
        new($"P5A native evidence is invalid at {path}: {reason}.");

    private sealed class ActionLifecycle
    {
        internal bool Started;
        internal bool Terminal;
        internal bool HasPlayback;
        internal bool HasVisual;
        internal string Source = string.Empty;
        internal ulong Ordinal;
        internal float LastMontageTime;
        internal float LastClipTime;
        internal float PlayRate;
        internal float LastVisualTime;
    }

    private sealed class SemanticGraphState
    {
        internal bool ActionActive;
        internal float ActionWeight;
        internal bool TransitionActive;
        internal float TransitionWeight;
    }

    private sealed record RuntimeShapes(
        JsonNode? CanonicalEvent,
        JsonNode? CanonicalOwner,
        JsonNode? PhysicalEvent,
        JsonNode? Outcome,
        JsonNode? Receipt)
    {
        internal static readonly RuntimeShapes None = new(null, null, null, null, null);

        internal static RuntimeShapes Create(JsonObject template)
        {
            var actuals = template["cases"]!.AsArray()
                .SelectMany(caseNode => caseNode!["frames"]!.AsArray())
                .Select(frame => frame!["nativeActual"]!).ToArray();
            return new RuntimeShapes(
                actuals.SelectMany(actual => actual["canonicalAssetOracle"]!["events"]!.AsArray()).First()!,
                actuals.SelectMany(actual => actual["canonicalAssetOracle"]!["activeNotifyStates"]!.AsArray()).First()!,
                actuals.SelectMany(actual => actual["nativeRuntimeTimeline"]!.AsArray()).First()!,
                actuals.SelectMany(actual => actual["actionOutcomes"]!.AsArray()).First()!,
                actuals.SelectMany(actual => actual["transitionStimulusReceipts"]!.AsArray()).First()!);
        }

        internal bool TryGet(string path, out JsonNode? shape, out int capacity)
        {
            if (path.EndsWith(".canonicalAssetOracle.events", StringComparison.Ordinal))
                (shape, capacity) = (CanonicalEvent, AlsEventBuffer.Capacity);
            else if (path.EndsWith(".canonicalAssetOracle.activeNotifyStates", StringComparison.Ordinal))
                (shape, capacity) = (CanonicalOwner, 16);
            else if (path.EndsWith(".nativeRuntimeTimeline", StringComparison.Ordinal))
                (shape, capacity) = (PhysicalEvent, 16);
            else if (path.EndsWith(".actionOutcomes", StringComparison.Ordinal))
                (shape, capacity) = (Outcome, AlsActionOutcomeBuffer.Capacity);
            else if (path.EndsWith(".transitionStimulusReceipts", StringComparison.Ordinal))
                (shape, capacity) = (Receipt, 1);
            else
            {
                shape = null;
                capacity = 0;
                return false;
            }
            return shape is not null;
        }
    }

    private sealed class SourceIndex
    {
        private readonly Dictionary<string, string> canonicalSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> nativeSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> transitionVariants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> canonicalEvents = new(StringComparer.Ordinal);
        private readonly HashSet<string> canonicalOwners = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (bool IsState, float TimeSeconds)> physicalEvents = new(StringComparer.Ordinal);
        private readonly HashSet<string> markers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> nativeDurations = new(StringComparer.Ordinal);

        internal static SourceIndex Create(JsonObject plan, JsonObject audit)
        {
            var result = new SourceIndex();
            foreach (var sourceNode in plan["sources"]!.AsArray())
            {
                var source = sourceNode!.AsObject();
                var traceId = source["traceSourceId"]!.GetValue<string>();
                var kind = source["layoutKey"]!["sourceKind"]!.GetValue<string>();
                result.canonicalSources.Add(CanonicalSourceKey(source["canonicalEvidence"]!.AsObject()), $"{traceId}|{kind}");
                foreach (var variantNode in source["nativeVariants"]!.AsArray())
                {
                    var variant = variantNode!.AsObject();
                    var nativeKey = NativeVariantKey(variant);
                    result.nativeSources.Add(nativeKey, $"{traceId}|{kind}");
                    if (kind == "Transition")
                    {
                        result.transitionVariants.Add(string.Join('|', traceId,
                            variant["stance"], variant["foot"]), nativeKey);
                    }
                    result.nativeDurations[traceId] = variant["durationSeconds"]!.GetValue<float>();
                }
            }
            foreach (var eventNode in plan["eventMap"]!.AsArray())
            {
                var eventMap = eventNode!.AsObject();
                result.canonicalEvents.Add(CanonicalEventKey(eventMap),
                    eventMap["canonicalEvidence"]!["durationSeconds"]!.GetValue<float>() > 0f);
                result.canonicalOwners.Add($"{eventMap["traceSourceId"]!.GetValue<string>()}|" +
                    eventMap["canonicalEvidence"]!["stableEventId"]!.GetValue<string>());
            }
            foreach (var markerNode in plan["markerMap"]!.AsArray())
            {
                var marker = markerNode!.AsObject();
                result.markers.Add($"{marker["traceSourceId"]!.GetValue<string>()}|" +
                    MarkerKey(marker["canonicalEvidence"]!.AsObject()));
            }
            foreach (var assetNode in audit["assets"]!.AsArray())
            {
                var asset = assetNode!.AsObject();
                result.nativeSources.TryAdd(NativeAuditAssetKey(asset), $"audit|{asset["assetClassPath"]!.GetValue<string>()}");
            }
            foreach (var eventNode in audit["events"]!.AsArray())
            {
                var physicalEvent = eventNode!.AsObject();
                result.physicalEvents.Add(PhysicalAuditEventKey(physicalEvent), (
                    physicalEvent["durationSeconds"]!.GetValue<float>() > 0f,
                    physicalEvent["timeSeconds"]!.GetValue<float>()));
            }
            if (audit["auxiliaryAssets"] is JsonArray auxiliaryAssets)
            {
                foreach (var assetNode in auxiliaryAssets)
                {
                    var asset = assetNode!.AsObject();
                    result.nativeSources.Add(AuxiliaryObservedSourceKey(asset),
                        $"audit|{asset["assetClassPath"]!.GetValue<string>()}");
                    foreach (var eventNode in asset["events"]!.AsArray())
                    {
                        var observedEvent = eventNode!.AsObject();
                        result.physicalEvents.Add(string.Join('|',
                            asset["assetStableId"], observedEvent["stableEventId"],
                            observedEvent["sourceIndex"], observedEvent["trackIndex"]), (
                            observedEvent["durationSeconds"]!.GetValue<float>() > 0f,
                            observedEvent["timeSeconds"]!.GetValue<float>()));
                    }
                }
            }
            return result;
        }

        internal string ResolveCanonicalSource(JsonObject observed, string path)
        {
            if (!canonicalSources.TryGetValue(CanonicalObservedSourceKey(observed), out var value))
                throw Invalid(path, "canonical source evidence does not resolve uniquely");
            return value.Split('|')[0];
        }

        internal string ResolveNativeSource(JsonObject observed, string path)
        {
            if (!nativeSources.TryGetValue(ResolveNativeSourceKey(observed, path), out var value))
                throw Invalid(path, "native source evidence is unknown or ambiguous");
            return value.Split('|')[0];
        }

        internal string ResolveNativeSourceKey(JsonObject observed, string path)
        {
            var key = NativeObservedSourceKey(observed);
            if (!nativeSources.ContainsKey(key))
                throw Invalid(path, "native source evidence is unknown or ambiguous");
            return key;
        }

        internal void RequireTransitionVariant(
            string traceSourceId,
            string nativeSourceKey,
            string stance,
            string foot,
            string path)
        {
            if (!transitionVariants.TryGetValue(string.Join('|', traceSourceId, stance, foot), out var expected) ||
                nativeSourceKey != expected)
                throw Invalid(path, "transition source is not the approved native variant for stance and foot");
        }

        internal string CanonicalKind(string traceId) => canonicalSources.Values
            .Where(value => value.StartsWith(traceId + "|", StringComparison.Ordinal))
            .Select(value => value.Split('|')[1]).Single();

        internal string NativeKind(string traceId) => nativeSources.Values
            .Where(value => value.StartsWith(traceId + "|", StringComparison.Ordinal))
            .Select(value => value.Split('|')[1]).FirstOrDefault() ?? string.Empty;

        internal float NativeDuration(string traceId) => nativeDurations.TryGetValue(traceId, out var duration)
            ? duration
            : throw Invalid("$raw", "native source duration is unavailable");

        internal bool ResolveCanonicalEvent(JsonObject observed, string path)
        {
            var observedSource = observed["source"]!.AsObject();
            var sourceId = ResolveCanonicalSource(observedSource, path);
            if (!canonicalEvents.TryGetValue(CanonicalObservedEventKey(sourceId, observed), out var isState))
                throw Invalid(path, "canonical event evidence does not resolve uniquely");
            var montageOwner = observed["observedOwnerKind"]!.GetValue<string>() == "MontageTimeline";
            if (observed["observedMontageStableId"]!.GetValue<string>() !=
                    observedSource["observedMontageStableId"]!.GetValue<string>() ||
                observed["observedOwnerSectionName"]!.GetValue<string>() !=
                    (montageOwner ? observedSource["observedSectionName"]!.GetValue<string>() : string.Empty) ||
                observed["observedSegmentIndex"]!.GetValue<int>() !=
                    observedSource["observedSegmentIndex"]!.GetValue<int>())
                throw Invalid(path, "canonical event owner source evidence drifted");
            return isState;
        }

        internal void ResolveCanonicalOwner(JsonObject observed, string path)
        {
            var sourceId = ResolveCanonicalSource(observed["source"]!.AsObject(), path);
            if (!canonicalOwners.Contains($"{sourceId}|{observed["observedEventStableId"]!.GetValue<string>()}"))
                throw Invalid(path, "active notify owner does not resolve to an approved event");
        }

        internal PhysicalEventDefinition ResolvePhysicalEvent(JsonObject observed, string path)
        {
            var source = observed["source"]!.AsObject();
            var traceSourceId = ResolveNativeSource(source, path);
            if (!physicalEvents.TryGetValue(PhysicalObservedEventKey(observed), out var definition))
                throw Invalid(path, "physical event does not resolve to the approved audit inventory");
            return new PhysicalEventDefinition(
                definition.IsState, definition.TimeSeconds, traceSourceId, NativeKind(traceSourceId));
        }

        internal void ResolveMarker(string traceSourceId, JsonObject marker, string path)
        {
            if (!markers.Contains($"{traceSourceId}|{MarkerKey(marker)}"))
                throw Invalid(path, "sync marker evidence does not resolve uniquely");
        }

        private static string CanonicalSourceKey(JsonObject value) => string.Join('|',
            value["assetObjectPath"], value["assetStableId"], value["assetClassPath"],
            value["montageStableId"], value["sectionName"], value["slotName"], value["segmentIndex"]);

        private static string CanonicalObservedSourceKey(JsonObject value) => string.Join('|',
            value["observedAssetObjectPath"], value["observedAssetStableId"], value["observedAssetClassPath"],
            value["observedMontageStableId"], value["observedSectionName"], value["observedSlotName"],
            value["observedSegmentIndex"]);

        private static string NativeVariantKey(JsonObject value) => string.Join('|',
            value["assetObjectPath"], value["assetStableId"], value["assetPackageSha256"], value["assetClassPath"],
            value["montageObjectPath"], value["montageStableId"], value["sectionName"], value["slotName"],
            value["segmentIndex"]);

        private static string NativeAuditAssetKey(JsonObject value) => string.Join('|',
            value["assetObjectPath"], value["assetStableId"], value["assetPackageSha256"], value["assetClassPath"],
            value["montageObjectPath"], value["montageStableId"], value["sectionName"], value["slotName"],
            value["segmentIndex"]);

        private static string NativeObservedSourceKey(JsonObject value) => string.Join('|',
            value["observedAssetObjectPath"], value["observedAssetStableId"], value["observedAssetPackageSha256"],
            value["observedAssetClassPath"], value["observedMontageObjectPath"],
            value["observedMontageStableId"], value["observedSectionName"], value["observedSlotName"],
            value["observedSegmentIndex"]);

        private static string AuxiliaryObservedSourceKey(JsonObject value) => string.Join('|',
            value["assetObjectPath"], value["assetStableId"], value["assetPackageSha256"],
            value["assetClassPath"], "", "", "", "", -1);

        private static string CanonicalEventKey(JsonObject map) =>
            CanonicalEventEvidenceKey(map["traceSourceId"]!.GetValue<string>(), map["canonicalEvidence"]!.AsObject());

        private static string CanonicalObservedEventKey(string sourceId, JsonObject value) => string.Join('|',
            sourceId, value["observedEventStableId"], value["observedEventAssetStableId"],
            value["observedOwnerKind"], value["observedSourceClassPath"], value["observedSourceIndex"],
            value["observedTrackIndex"], value["boundaryOrdinal"], value["kind"], value["tickMode"],
            PayloadKey(value["payload"]!.AsObject()));

        private static string CanonicalEventEvidenceKey(string sourceId, JsonObject value) => string.Join('|',
            sourceId, value["stableEventId"], value["assetStableId"], value["ownerKind"],
            value["sourceClassPath"], value["sourceIndex"], value["trackIndex"], value["boundaryOrdinal"],
            value["kind"], value["tickMode"], PayloadKey(value["payload"]!.AsObject()));

        private static string PayloadKey(JsonObject value) => string.Join('|',
            value["semanticId"], value["enumValue0"], value["enumValue1"], value["enumValue2"],
            value["scalarValue0"], value["flags"]);

        private static string PhysicalAuditEventKey(JsonObject value) => string.Join('|',
            value["assetStableId"], value["stableEventId"], value["sourceIndex"], value["trackIndex"]);

        private static string PhysicalObservedEventKey(JsonObject value) => string.Join('|',
            value["source"]!["observedAssetStableId"], value["observedEventStableId"],
            value["observedSourceIndex"], value["observedTrackIndex"]);

        private static string MarkerKey(JsonObject value) => string.Join('|',
            value["assetStableId"], value["stableMarkerId"], value["name"], value["sourceIndex"],
            value["trackIndex"], value["timeSeconds"]!.ToJsonString());
    }
}
