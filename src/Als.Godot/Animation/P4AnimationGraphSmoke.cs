using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class P4AnimationGraphSmoke : Node
{
    private const string P3ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const string P4ProfilePath = "res://assets/config/p4_pose_profile.json";
    private const double DeltaTime = 1.0 / 60.0;
    private const int FrameCount = 240;
    private const ulong DigestOffset = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const ulong ExpectedCurveSamplerDigest = 0x82014E8E191BAE63UL;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    public override void _Ready()
    {
        try
        {
            var digest = RunSmoke();
            GD.Print($"P4_ANIMATION_GRAPH_OK frames={FrameCount} advances={FrameCount} digest={digest:X16}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private ulong RunSmoke()
    {
        VerifyCurveSampler();

        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var locomotionProfile = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P3ProfilePath)), definition);
        var poseProfile = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)), definition,
            locomotionProfile);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));

        VerifyLibraryContract(definition, locomotionProfile, poseProfile);
        VerifyAuthoritativeActionPhase(definition, locomotionProfile, poseProfile, settings);
        VerifyTurnBlend(definition, locomotionProfile, poseProfile, settings);
        VerifyInterruptedActionBlend(definition, locomotionProfile, poseProfile, settings);
        VerifyZeroDurationTurnBlend(definition, locomotionProfile, poseProfile, settings);
        VerifyAimEndpoints(definition, locomotionProfile, poseProfile, settings);
        VerifyControllerAllocation(definition, locomotionProfile, poseProfile, settings);
        VerifyPreparedFootCurves(definition, locomotionProfile, poseProfile, settings);
        VerifyPreparedAirborneTimeline(definition, locomotionProfile, poseProfile, settings);
        VerifyPreparedBaseCrossfades(definition, locomotionProfile, poseProfile, settings);
        VerifyPreparedRollback(definition, locomotionProfile, poseProfile, settings);
        VerifyAirborneTransitionRollback(definition, locomotionProfile, poseProfile, settings);

        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
        controller.Warmup();

        VerifyNamedNodes(graph);
        VerifyExactBindings(graph, poseProfile, library, definition);
        var p4Handles = graph.Handles.P4!;

        var turnDurations = poseProfile.Turns
            .Select(value => definition.Animations[value.AnimationId].PlayLength)
            .ToArray();
        if (turnDurations.Count(value => MathF.Abs(value - 2f) <= 0.02f) < 6 ||
            turnDurations.Count(value => MathF.Abs(value - 2.3333333f) <= 0.02f) < 2)
        {
            throw new InvalidOperationException(
                $"P4 formal Turn durations lost the six 2s/two 2.33s evidence: " +
                $"[{string.Join(',', turnDurations.Select(value => value.ToString("R")))}]");
        }

        var digest = DigestOffset;
        var result = ValidResult();
        var aimPoseDigests = new ulong[4];
        var phasesAboveOne = 0;
        var basePlayback = graph.Tree.Get(
            graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P4 Base playback is unavailable.");
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var slot = frame / 20;
            var profileDuration = slot < poseProfile.Turns.Length
                ? definition.Animations[poseProfile.Turns[slot].AnimationId].PlayLength
                : definition.Animations[poseProfile.Rotates[slot - poseProfile.Turns.Length].AnimationId].PlayLength;
            var phase = slot < poseProfile.Turns.Length
                ? ((frame % 20 + 1) / 20f) * profileDuration
                : ((frame % 20 + 1) / 21f) * profileDuration;
            var aimPhase = frame / (float)(FrameCount - 1);
            var p4 = slot < poseProfile.Turns.Length
                ? AlsP4AnimationInput.Turn(
                    poseProfile.Turns[slot].AnimationId,
                    poseProfile.Turns[slot].BasePlayRate,
                    phase,
                    aimPhase,
                    1f - aimPhase,
                    0.5f,
                    aimPhase)
                : AlsP4AnimationInput.Rotate(
                    poseProfile.Rotates[slot - poseProfile.Turns.Length].AnimationId,
                    1.15f + ((slot - poseProfile.Turns.Length) * 0.1f),
                    phase,
                    aimPhase,
                    1f - aimPhase,
                    0.5f,
                    aimPhase);

            if (frame < aimPoseDigests.Length)
            {
                p4 = p4 with
                {
                    TurnPhase = profileDuration * 0.5f,
                    AimDownPhase = 0.5f,
                    AimForwardPhase = 0.5f,
                    AimUpPhase = 0.5f,
                    AimDownWeight = frame == 1 ? 1f : 0f,
                    AimForwardWeight = frame == 2 ? 1f : 0f,
                    AimUpWeight = frame == 3 ? 1f : 0f,
                };
            }

            result.Identity = new AlsFrameIdentity(frame + 1, 0, 1);
            controller.Apply(in result, in p4, DeltaTime);
            var actionStateA = graph.Tree.Get(
                slot < poseProfile.Turns.Length
                    ? p4Handles.TurnBankACurrentStatePath
                    : p4Handles.RotateBankACurrentStatePath).AsString();
            var actionStateB = graph.Tree.Get(
                slot < poseProfile.Turns.Length
                    ? p4Handles.TurnBankBCurrentStatePath
                    : p4Handles.RotateBankBCurrentStatePath).AsString();
            var hasBinding = slot < poseProfile.Turns.Length
                ? p4Handles.TryGetTurnBinding(p4.ActiveTurnAnimationId, out var actionBinding)
                : p4Handles.TryGetRotateBinding(p4.ActiveRotateAnimationId, out actionBinding);
            if (!hasBinding ||
                (!string.Equals(actionStateA, actionBinding.StateName.ToString(), StringComparison.Ordinal) &&
                 !string.Equals(actionStateB, actionBinding.StateName.ToString(), StringComparison.Ordinal)) ||
                controller.ActiveTurnAnimationId != p4.ActiveTurnAnimationId ||
                controller.ActiveRotateAnimationId != p4.ActiveRotateAnimationId)
            {
                throw new InvalidOperationException(
                    $"P4 controller did not select the requested exact ID: frame={frame} slot={slot} " +
                    $"expectedState={actionBinding.StateName} actualStates={actionStateA}/{actionStateB} " +
                    $"expectedTurn={p4.ActiveTurnAnimationId} actualTurn={controller.ActiveTurnAnimationId} " +
                    $"expectedRotate={p4.ActiveRotateAnimationId} actualRotate={controller.ActiveRotateAnimationId} " +
                    $"baseState={basePlayback.GetCurrentNode()}");
            }
            const string expectedBaseState = "Grounded";
            if (!string.Equals(
                    basePlayback.GetCurrentNode().ToString(), expectedBaseState, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"P4 Base playback state mismatch: frame={frame} expected={expectedBaseState} " +
                    $"actual={basePlayback.GetCurrentNode()}");
            }
            if (phase > 1f)
            {
                phasesAboveOne++;
            }
            var poseDigest = controller.ComputePoseDigest(0);
            if (frame < aimPoseDigests.Length)
            {
                aimPoseDigests[frame] = poseDigest;
            }
            Append(ref digest, unchecked((ulong)(uint)p4.ActiveTurnAnimationId));
            Append(ref digest, unchecked((ulong)(uint)p4.ActiveRotateAnimationId));
            Append(ref digest, unchecked((ulong)(uint)BitConverter.SingleToInt32Bits(phase)));
            Append(ref digest, poseDigest);
        }

        if (aimPoseDigests.Distinct().Count() != aimPoseDigests.Length)
        {
            throw new InvalidOperationException(
                $"P4 Aim output did not produce distinct base/down/forward/up poses: " +
                $"{string.Join(',', aimPoseDigests.Select(value => value.ToString("X16")))}");
        }
        if (phasesAboveOne == 0)
        {
            throw new InvalidOperationException("P4 real-duration actions never exercised a phase above one second.");
        }

        if (controller.ManualAdvanceCount != FrameCount)
        {
            throw new InvalidOperationException(
                $"P4 graph must advance once per frame: frames={FrameCount} advances={controller.ManualAdvanceCount}");
        }

        VerifyControllerTransaction(controller, graph, poseProfile, in result);
        return digest;
    }

    private void VerifyPreparedFootCurves(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        if (System.Runtime.CompilerServices.RuntimeHelpers
                .IsReferenceOrContainsReferences<AlsPreparedAnimationFrame>() ||
            System.Runtime.CompilerServices.RuntimeHelpers
                .IsReferenceOrContainsReferences<AlsFootCurveSample>())
        {
            throw new InvalidOperationException(
                "Prepared animation and foot-curve contracts must remain value-only.");
        }
        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
        controller.Warmup();

        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        result.AnimationState = AlsAnimationState.JumpStart;
        var prepared = controller.PrepareFrame(in result, in disabled, DeltaTime);
        var sample = controller.SampleFootCurves(in prepared);
        if (sample.LeftIkWeight != 0f || sample.RightIkWeight != 0f)
        {
            throw new InvalidOperationException("Prepared JumpStart foot IK default was not zero.");
        }
        controller.ApplyPrepared(in prepared);
        controller.CommitPrepared(in prepared);

        result.AnimationState = AlsAnimationState.LandRecovery;
        result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
        prepared = controller.PrepareFrame(in result, in disabled, DeltaTime);
        sample = controller.SampleFootCurves(in prepared);
        if (sample.LeftIkWeight != 1f || sample.RightIkWeight != 1f)
        {
            throw new InvalidOperationException("Prepared LandRecovery foot IK default was not one.");
        }
        controller.ApplyPrepared(in prepared);
        controller.CommitPrepared(in prepared);

        result.AnimationState = AlsAnimationState.Grounded;
        var partialProfile = poseProfile with
        {
            FootCurves = poseProfile.FootCurves with { GroundedIkWeight = 0.5f },
        };
        using var partialController = new AlsLocomotionAnimationController(
            graph, settings, partialProfile, definition);
        partialController.Warmup();
        prepared = partialController.PrepareFrame(in result, in disabled, 0.0);
        sample = partialController.SampleFootCurves(in prepared);
        if (sample.LeftIkWeight != 0.5f || sample.RightIkWeight != 0.5f)
        {
            throw new InvalidOperationException("Prepared custom grounded foot IK default was not partial.");
        }

        var turn = poseProfile.Turns.First(value =>
        {
            var binding = poseProfile.FootCurves.Bindings.Single(item =>
                item.AnimationId == value.AnimationId);
            return binding.LeftLockCurveId >= 0 && binding.RightLockCurveId >= 0;
        });
        var curveBinding = poseProfile.FootCurves.Bindings.Single(value =>
            value.AnimationId == turn.AnimationId);
        var animation = definition.Animations[turn.AnimationId];
        var sampler = new AlsCurveSampler(animation.Curves);
        var leftCurve = animation.Curves.Single(value =>
            value.CurveId == curveBinding.LeftLockCurveId);
        var zeroKey = leftCurve.Keys.First(value => value.Value == 0f);
        var oneKey = leftCurve.Keys.First(value => value.Value >= 1f);
        var before = partialController.ManualAdvanceCount;
        var action = AlsP4AnimationInput.Turn(
            turn.AnimationId, turn.BasePlayRate, oneKey.TimeSeconds,
            0f, 0f, 0f, 0f);
        prepared = partialController.PrepareFrame(in result, in action, turn.BlendSeconds * 0.5);
        sample = partialController.SampleFootCurves(in prepared);
        if (!sampler.TrySample(curveBinding.LeftLockCurveId, oneKey.TimeSeconds, out var rawOne) ||
            MathF.Abs(sample.LeftLockCurve - (Math.Clamp(rawOne, 0f, 1f) * 0.5f)) > 1e-5f)
        {
            throw new InvalidOperationException("Prepared action lock did not reuse the half-blended graph decision.");
        }
        partialController.ApplyPrepared(in prepared);
        partialController.CommitPrepared(in prepared);
        prepared = partialController.PrepareFrame(in result, in action, turn.BlendSeconds * 0.5);
        partialController.ApplyPrepared(in prepared);
        partialController.CommitPrepared(in prepared);

        action = action with { TurnPhase = zeroKey.TimeSeconds };
        prepared = partialController.PrepareFrame(in result, in action, 0.0);
        sample = partialController.SampleFootCurves(in prepared);
        if (sample.LeftLockCurve != 0f)
        {
            throw new InvalidOperationException("Prepared lock curve did not sample the zero key at the selected phase.");
        }
        partialController.ApplyPrepared(in prepared);
        partialController.CommitPrepared(in prepared);

        action = action with { TurnPhase = oneKey.TimeSeconds };
        prepared = partialController.PrepareFrame(in result, in action, 0.0);
        sample = partialController.SampleFootCurves(in prepared);
        if (sample.LeftLockCurve != Math.Clamp(rawOne, 0f, 1f))
        {
            throw new InvalidOperationException("Prepared lock curve did not sample the full key at the selected phase.");
        }
        partialController.ApplyPrepared(in prepared);
        partialController.CommitPrepared(in prepared);
        if (partialController.ManualAdvanceCount - before != 4)
        {
            throw new InvalidOperationException("Prepared frames did not advance exactly once each.");
        }

        VerifyPreparedOwnership(partialController, controller, in result, in disabled);
        VerifyPreparedBaseCurves(definition, locomotionProfile, poseProfile, settings);
    }

    private static void VerifyPreparedOwnership(
        AlsLocomotionAnimationController owner,
        AlsLocomotionAnimationController foreign,
        in AlsFrameResult result,
        in AlsP4AnimationInput input)
    {
        var first = owner.PrepareFrame(in result, in input, 0.0);
        ExpectRejected(() => foreign.SampleFootCurves(in first), "foreign");
        var second = owner.PrepareFrame(in result, in input, 0.0);
        ExpectRejected(() => owner.SampleFootCurves(in first), "stale");
        owner.ApplyPrepared(in second);
        ExpectRejected(() => owner.ApplyPrepared(in second), "already applied");
        var commit = owner.PrepareCommit(in second);
        if (owner.TryFinalizePreparedCommit(default) ||
            owner.TryFinalizePreparedCommit(commit with { OwnerId = commit.OwnerId + 1L }))
        {
            throw new InvalidOperationException("Prepared default or foreign commit token was accepted.");
        }
        if (!owner.TryFinalizePreparedCommit(in commit) ||
            owner.TryFinalizePreparedCommit(in commit))
        {
            throw new InvalidOperationException("Prepared valid or stale commit token state was incorrect.");
        }
        ExpectRejected(() => owner.ApplyPrepared(in second), "reused");
        var discarded = owner.PrepareFrame(in result, in input, 0.0);
        owner.DiscardPrepared(in discarded);
        ExpectRejected(() => owner.SampleFootCurves(in discarded), "discarded");
        var afterDiscard = owner.PrepareFrame(in result, in input, 0.0);
        owner.ApplyPrepared(in afterDiscard);
        owner.CommitPrepared(in afterDiscard);

        static void ExpectRejected(Action action, string label)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains(
                    "stale, foreign or already applied", StringComparison.Ordinal))
            {
                rejected = true;
            }
            if (!rejected)
            {
                throw new InvalidOperationException($"Prepared {label} token was accepted.");
            }
        }
    }

    private void VerifyPreparedBaseCurves(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        var baseSample = locomotionProfile.StandingSamples[0];
        var animations = definition.Animations.ToArray();
        var source = animations[baseSample.AnimationId];
        var curveId = source.Curves.Length == 0
            ? 0
            : source.Curves.Max(value => value.CurveId) + 1;
        var curve = new AlsFloatCurveDefinition(
            curveId,
            AlsCanonicalCurveKind.None,
            "FootLock_L",
            AlsCurveProvenance.SourceCurve,
            [
                new(0f, 0f, 0f, 0f, AlsCurveInterpolation.Linear),
                new(source.PlayLength * 0.5f, 0.5f, 0f, 0f, AlsCurveInterpolation.Linear),
                new(source.PlayLength, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
            ]);
        animations[baseSample.AnimationId] = source with
        {
            Curves = [.. source.Curves, curve],
        };
        var bindings = poseProfile.FootCurves.Bindings;
        var bindingIndex = Array.FindIndex(
            bindings, value => value.AnimationId == baseSample.AnimationId);
        bindings[bindingIndex] = bindings[bindingIndex] with { LeftLockCurveId = curveId };
        var fixtureProfile = poseProfile with
        {
            FootCurves = poseProfile.FootCurves with { Bindings = bindings },
        };
        var fixtureDefinition = definition with { Animations = animations };
        using var library = AlsAnimationLibraryBuilder.Build(
            fixtureDefinition, locomotionProfile, fixtureProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, fixtureProfile, fixtureDefinition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, fixtureProfile, fixtureDefinition);
        controller.Warmup();

        var result = ValidResult();
        result.AnimationState = AlsAnimationState.Grounded;
        result.ActualStance = AlsStance.Standing;
        result.ActualGait = AlsGait.Walking;
        result.BlendCoordinates = new System.Numerics.Vector2(baseSample.X, baseSample.Y);
        var disabled = AlsP4AnimationInput.Disabled;
        var expected = new[] { 0f, 0.5f, 1f };
        for (var index = 0; index < expected.Length; index++)
        {
            result.AnimationPhase = expected[index];
            var prepared = controller.PrepareFrame(in result, in disabled, 0.0);
            if (prepared.BaseAnimationIdA != baseSample.AnimationId ||
                prepared.BaseWeightA != 1f)
            {
                throw new InvalidOperationException(
                    "Prepared base curve decision did not reuse the active blend-space sample.");
            }
            var sample = controller.SampleFootCurves(in prepared);
            if (MathF.Abs(sample.LeftLockCurve - expected[index]) > 1e-5f)
            {
                throw new InvalidOperationException(
                    "Prepared base curve did not sample 0/partial/1 across normalized phase keys.");
            }
            controller.ApplyPrepared(in prepared);
            controller.CommitPrepared(in prepared);
        }

        var neighbor = locomotionProfile.StandingSamples[1];
        result.AnimationPhase = 1f;
        result.BlendCoordinates = new System.Numerics.Vector2(
            baseSample.X + neighbor.X,
            baseSample.Y + neighbor.Y);
        var blended = controller.PrepareFrame(in result, in disabled, 0.0);
        var totalWeight = blended.BaseWeightA + blended.BaseWeightB + blended.BaseWeightC;
        var baseSampleWeight = 0f;
        if (blended.BaseAnimationIdA == baseSample.AnimationId)
        {
            baseSampleWeight += blended.BaseWeightA;
        }
        if (blended.BaseAnimationIdB == baseSample.AnimationId)
        {
            baseSampleWeight += blended.BaseWeightB;
        }
        if (blended.BaseAnimationIdC == baseSample.AnimationId)
        {
            baseSampleWeight += blended.BaseWeightC;
        }
        var blendedSample = controller.SampleFootCurves(in blended);
        if (MathF.Abs(totalWeight - 1f) > 1e-5f ||
            baseSampleWeight <= 0f ||
            baseSampleWeight >= 1f ||
            MathF.Abs(blendedSample.LeftLockCurve - baseSampleWeight) > 1e-5f)
        {
            throw new InvalidOperationException(
                "Prepared base curve did not use normalized barycentric curve weights.");
        }
        controller.ApplyPrepared(in blended);
        controller.CommitPrepared(in blended);
    }

    private void VerifyPreparedBaseCrossfades(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        const double halfTransition = 0.04;
        var (stateDefinition, stateProfile) = WithLeftFootCurves(
            definition,
            poseProfile,
            (locomotionProfile.StandingIdleAnimationId, 0f),
            (locomotionProfile.JumpStartAnimationId, 1f));
        using (var library = AlsAnimationLibraryBuilder.Build(
                   stateDefinition, locomotionProfile, stateProfile))
        {
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(
                library, locomotionProfile, stateProfile, stateDefinition);
            using var controller = new AlsLocomotionAnimationController(
                graph, settings, stateProfile, stateDefinition);
            controller.Warmup();
            var result = ValidResult();
            result.BlendCoordinates = System.Numerics.Vector2.Zero;
            var disabled = AlsP4AnimationInput.Disabled;
            controller.Apply(in result, in disabled, 0.0);
            result.AnimationState = AlsAnimationState.JumpStart;
            result.ResolvedLocomotionState = AlsLocomotionState.InAir;
            controller.Apply(in result, in disabled, 0.0);
            var prepared = controller.PrepareFrame(
                in result, in disabled, halfTransition);
            var curves = controller.SampleFootCurves(in prepared);
            if (MathF.Abs(curves.LeftLockCurve - 0.5f) > 1e-5f)
            {
                throw new InvalidOperationException(
                    "Prepared base curve did not match the top-state half crossfade.");
            }
            controller.ApplyPrepared(in prepared);
            controller.CommitPrepared(in prepared);
        }

        var (movingSourceDefinition, movingSourceProfile) = WithLeftFootCurves(
            definition,
            poseProfile,
            (locomotionProfile.JumpStartAnimationId, 0f));
        movingSourceDefinition = WithLeftFootRamp(
            movingSourceDefinition,
            ref movingSourceProfile,
            locomotionProfile.StandingIdleAnimationId);
        using (var library = AlsAnimationLibraryBuilder.Build(
                   movingSourceDefinition, locomotionProfile, movingSourceProfile))
        {
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(
                library, locomotionProfile, movingSourceProfile, movingSourceDefinition);
            using var controller = new AlsLocomotionAnimationController(
                graph, settings, movingSourceProfile, movingSourceDefinition);
            controller.Warmup();
            var result = ValidResult();
            result.BlendCoordinates = System.Numerics.Vector2.Zero;
            result.AnimationPhase = 0.25f;
            var disabled = AlsP4AnimationInput.Disabled;
            controller.Apply(in result, in disabled, 0.0);
            result.AnimationState = AlsAnimationState.JumpStart;
            result.ResolvedLocomotionState = AlsLocomotionState.InAir;
            controller.Apply(in result, in disabled, 0.0);
            var prepared = controller.PrepareFrame(
                in result, in disabled, halfTransition);
            var curves = controller.SampleFootCurves(in prepared);
            const float expectedPreviousPhase = 0.29f;
            const float expectedCurve = expectedPreviousPhase * 0.5f;
            if (MathF.Abs(prepared.PreviousBasePhaseNormalized - expectedPreviousPhase) > 1e-5f ||
                MathF.Abs(curves.LeftLockCurve - expectedCurve) > 1e-5f)
            {
                throw new InvalidOperationException(
                    "Prepared base curve froze the fading-from branch phase.");
            }
            controller.ApplyPrepared(in prepared);
            var playback = graph.Tree.Get(graph.Handles.TopPlaybackPath)
                .As<AnimationNodeStateMachinePlayback>();
            var fadingFromPosition = playback?.GetFadingFromPlayPosition() ?? -1.0;
            if (Math.Abs(fadingFromPosition - expectedPreviousPhase) > 1e-5)
            {
                throw new InvalidOperationException(
                    "Godot fading-from playback phase diverged from the prepared source curve phase: " +
                    $"expected={expectedPreviousPhase:R} actual={fadingFromPosition:R}");
            }
            controller.CommitPrepared(in prepared);
        }

        var (stanceDefinition, stanceProfile) = WithLeftFootCurves(
            definition,
            poseProfile,
            (locomotionProfile.StandingIdleAnimationId, 0f),
            (locomotionProfile.CrouchingIdleAnimationId, 1f));
        using (var library = AlsAnimationLibraryBuilder.Build(
                   stanceDefinition, locomotionProfile, stanceProfile))
        {
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(
                library, locomotionProfile, stanceProfile, stanceDefinition);
            using var controller = new AlsLocomotionAnimationController(
                graph, settings, stanceProfile, stanceDefinition);
            controller.Warmup();
            var result = ValidResult();
            result.BlendCoordinates = System.Numerics.Vector2.Zero;
            var disabled = AlsP4AnimationInput.Disabled;
            controller.Apply(in result, in disabled, 0.0);
            result.ActualStance = AlsStance.Crouching;
            controller.Apply(in result, in disabled, 0.0);
            var prepared = controller.PrepareFrame(
                in result, in disabled, halfTransition);
            var curves = controller.SampleFootCurves(in prepared);
            if (MathF.Abs(curves.LeftLockCurve - 0.5f) > 1e-5f)
            {
                throw new InvalidOperationException(
                    "Prepared base curve did not match the grounded-stance half crossfade.");
            }
            controller.ApplyPrepared(in prepared);
            controller.CommitPrepared(in prepared);
        }
    }

    private void VerifyPreparedAirborneTimeline(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        const float fixturePlayLength = 2f;
        var animationIds = new[]
        {
            locomotionProfile.JumpStartAnimationId,
            locomotionProfile.FallLoopAnimationId,
            locomotionProfile.LandAnimationId,
        };
        var animations = definition.Animations.ToArray();
        var bindings = poseProfile.FootCurves.Bindings.ToArray();
        foreach (var animationId in animationIds)
        {
            var source = animations[animationId];
            var curveId = source.Curves.Length == 0
                ? 0
                : source.Curves.Max(value => value.CurveId) + 1;
            animations[animationId] = source with
            {
                PlayLength = fixturePlayLength,
                Curves =
                [
                    .. source.Curves,
                    new AlsFloatCurveDefinition(
                        curveId,
                        AlsCanonicalCurveKind.None,
                        "FootLock_L",
                        AlsCurveProvenance.SourceCurve,
                        [
                            new(0f, 0f, 0f, 0f, AlsCurveInterpolation.Linear),
                            new(fixturePlayLength, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
                        ]),
                ],
            };
            var bindingIndex = Array.FindIndex(
                bindings, value => value.AnimationId == animationId);
            bindings[bindingIndex] = bindings[bindingIndex] with
            {
                LeftLockCurveId = curveId,
            };
        }
        var fixtureDefinition = definition with { Animations = animations };
        var fixtureProfile = poseProfile with
        {
            FootCurves = poseProfile.FootCurves with { Bindings = bindings },
        };
        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        foreach (var animationId in animationIds)
        {
            var clip = library.Library.GetAnimation(library.ClipNames[animationId]);
            clip.Length = fixturePlayLength;
        }
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, fixtureProfile, fixtureDefinition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, fixtureProfile, fixtureDefinition);
        controller.Warmup();
        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;

        AssertState(AlsAnimationState.JumpStart, 0.5, 0.5f);
        AssertState(AlsAnimationState.FallLoop, 1.25, 0.25f);
        AssertState(AlsAnimationState.LandRecovery, 1.5, 1f);

        void AssertState(AlsAnimationState state, double deltaTime, float expected)
        {
            result.AnimationState = state;
            result.ResolvedLocomotionState = state == AlsAnimationState.LandRecovery
                ? AlsLocomotionState.Grounded
                : AlsLocomotionState.InAir;
            controller.Apply(in result, in disabled, 0.0);
            var prepared = controller.PrepareFrame(in result, in disabled, deltaTime);
            var curves = controller.SampleFootCurves(in prepared);
            if (MathF.Abs(prepared.BasePhaseNormalized - expected) > 1e-5f ||
                MathF.Abs(curves.LeftLockCurve - expected) > 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Prepared {state} curve did not follow the graph one-second timeline.");
            }
            controller.ApplyPrepared(in prepared);
            var playback = graph.Tree.Get(graph.Handles.TopPlaybackPath)
                .As<AnimationNodeStateMachinePlayback>();
            if (playback is null ||
                Math.Abs(playback.GetCurrentPlayPosition() - expected) > 1e-5)
            {
                throw new InvalidOperationException(
                    $"Applied {state} graph phase diverged from the prepared one-second timeline.");
            }
            controller.CommitPrepared(in prepared);
        }
    }

    private static (AlsAnimationSetDefinition Definition, AlsPoseAnimationProfile Profile)
        WithLeftFootCurves(
            AlsAnimationSetDefinition definition,
            AlsPoseAnimationProfile profile,
            params (int AnimationId, float Value)[] values)
    {
        var animations = definition.Animations.ToArray();
        var bindings = profile.FootCurves.Bindings.ToArray();
        foreach (var (animationId, value) in values)
        {
            var source = animations[animationId];
            var curveId = source.Curves.Length == 0
                ? 0
                : source.Curves.Max(curve => curve.CurveId) + 1;
            animations[animationId] = source with
            {
                Curves =
                [
                    .. source.Curves,
                    new AlsFloatCurveDefinition(
                        curveId,
                        AlsCanonicalCurveKind.None,
                        "FootLock_L",
                        AlsCurveProvenance.SourceCurve,
                        [new(0f, value, 0f, 0f, AlsCurveInterpolation.Constant)]),
                ],
            };
            var bindingIndex = Array.FindIndex(
                bindings, binding => binding.AnimationId == animationId);
            bindings[bindingIndex] = bindings[bindingIndex] with
            {
                LeftLockCurveId = curveId,
            };
        }
        return (
            definition with { Animations = animations },
            profile with
            {
                FootCurves = profile.FootCurves with { Bindings = bindings },
            });
    }

    private static AlsAnimationSetDefinition WithLeftFootRamp(
        AlsAnimationSetDefinition definition,
        ref AlsPoseAnimationProfile profile,
        int animationId)
    {
        var animations = definition.Animations.ToArray();
        var bindings = profile.FootCurves.Bindings.ToArray();
        var source = animations[animationId];
        var curveId = source.Curves.Length == 0
            ? 0
            : source.Curves.Max(value => value.CurveId) + 1;
        animations[animationId] = source with
        {
            Curves =
            [
                .. source.Curves,
                new AlsFloatCurveDefinition(
                    curveId,
                    AlsCanonicalCurveKind.None,
                    "FootLock_L",
                    AlsCurveProvenance.SourceCurve,
                    [
                        new(0f, 0f, 0f, 0f, AlsCurveInterpolation.Linear),
                        new(source.PlayLength, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
                    ]),
            ],
        };
        var bindingIndex = Array.FindIndex(
            bindings, value => value.AnimationId == animationId);
        bindings[bindingIndex] = bindings[bindingIndex] with { LeftLockCurveId = curveId };
        profile = profile with
        {
            FootCurves = profile.FootCurves with { Bindings = bindings },
        };
        return definition with { Animations = animations };
    }

    private void VerifyPreparedRollback(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
        controller.Warmup();
        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        controller.Apply(in result, in disabled, 0.0);
        var beforePose = controller.ComputePoseDigest(901);
        var beforeAdvances = controller.ManualAdvanceCount;
        var beforeState = controller.ActiveAnimationState;

        result.AnimationState = AlsAnimationState.JumpStart;
        result.ResolvedLocomotionState = AlsLocomotionState.InAir;
        var failed = controller.PrepareFrame(in result, in disabled, 0.04);
        controller.ApplyPrepared(in failed);
        controller.RollbackPrepared(in failed);
        if (controller.ComputePoseDigest(901) != beforePose ||
            controller.ManualAdvanceCount != beforeAdvances ||
            controller.ActiveAnimationState != beforeState)
        {
            throw new InvalidOperationException(
                "Prepared rollback did not restore the committed controller and graph checkpoint.");
        }

        var retry = controller.PrepareFrame(in result, in disabled, 0.04);
        controller.ApplyPrepared(in retry);
        controller.CommitPrepared(in retry);
        if (controller.ManualAdvanceCount != beforeAdvances + 1 ||
            controller.ActiveAnimationState != AlsAnimationState.JumpStart)
        {
            throw new InvalidOperationException(
                "Prepared retry did not commit exactly once after rollback.");
        }
        var fadingPose = controller.ComputePoseDigest(904);
        var fadingAdvances = controller.ManualAdvanceCount;
        failed = controller.PrepareFrame(in result, in disabled, 0.02);
        controller.ApplyPrepared(in failed);
        controller.RollbackPrepared(in failed);
        if (controller.ComputePoseDigest(904) != fadingPose ||
            controller.ManualAdvanceCount != fadingAdvances)
        {
            throw new InvalidOperationException(
                "Prepared rollback did not reconstruct the committed in-progress base crossfade.");
        }
        using (var referenceLibrary = AlsAnimationLibraryBuilder.Build(
                   definition, locomotionProfile, poseProfile))
        {
            AddChild(referenceLibrary.Root);
            using var referenceGraph = AlsLocomotionGraphBuilder.Build(
                referenceLibrary, locomotionProfile, poseProfile, definition);
            using var referenceController = new AlsLocomotionAnimationController(
                referenceGraph, settings, poseProfile, definition);
            referenceController.Warmup();
            var referenceResult = ValidResult();
            referenceController.Apply(in referenceResult, in disabled, 0.0);
            referenceResult.AnimationState = AlsAnimationState.JumpStart;
            referenceResult.ResolvedLocomotionState = AlsLocomotionState.InAir;
            referenceController.Apply(in referenceResult, in disabled, 0.04);
            if (referenceController.ComputePoseDigest(903) !=
                controller.ComputePoseDigest(903))
            {
                throw new InvalidOperationException(
                    "Prepared retry pose differed from a controller that never executed the failed frame.");
            }
        }

        result.AnimationState = AlsAnimationState.Grounded;
        result.ResolvedLocomotionState = AlsLocomotionState.Grounded;
        var firstTurn = poseProfile.Turns[0];
        var secondTurn = poseProfile.Turns[1];
        var firstInput = AlsP4AnimationInput.Turn(
            firstTurn.AnimationId, firstTurn.BasePlayRate, 0f, 0f, 0f, 0f, 0f);
        controller.Apply(in result, in firstInput, firstTurn.BlendSeconds);
        var p4Pose = controller.ComputePoseDigest(902);
        var p4Advances = controller.ManualAdvanceCount;
        var p4Handles = graph.Handles.P4!;
        var turnStateA = graph.Tree.Get(p4Handles.TurnBankACurrentStatePath).AsString();
        var turnStateB = graph.Tree.Get(p4Handles.TurnBankBCurrentStatePath).AsString();
        var secondInput = AlsP4AnimationInput.Turn(
            secondTurn.AnimationId, secondTurn.BasePlayRate, 0f, 0f, 0f, 0f, 0f);
        failed = controller.PrepareFrame(
            in result, in secondInput, secondTurn.BlendSeconds * 0.5);
        controller.ApplyPrepared(in failed);
        controller.RollbackPrepared(in failed);
        if (controller.ComputePoseDigest(902) != p4Pose ||
            controller.ManualAdvanceCount != p4Advances ||
            controller.ActiveTurnAnimationId != firstTurn.AnimationId ||
            !string.Equals(
                graph.Tree.Get(p4Handles.TurnBankACurrentStatePath).AsString(),
                turnStateA,
                StringComparison.Ordinal) ||
            !string.Equals(
                graph.Tree.Get(p4Handles.TurnBankBCurrentStatePath).AsString(),
                turnStateB,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Prepared rollback did not restore the committed P4 action bank.");
        }
    }

    private void VerifyAirborneTransitionRollback(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
        controller.Warmup();
        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        ApplySequence(controller, ref result, in disabled);
        var expectedPose = controller.ComputePoseDigest(905);
        var expectedPlayback = CapturePlayback(graph);
        var expectedAdvances = controller.ManualAdvanceCount;

        var failed = controller.PrepareFrame(in result, in disabled, 0.02);
        controller.ApplyPrepared(in failed);
        controller.RollbackPrepared(in failed);
        var actualPlayback = CapturePlayback(graph);
        if (controller.ComputePoseDigest(905) != expectedPose ||
            controller.ManualAdvanceCount != expectedAdvances ||
            Math.Abs(actualPlayback.Current - expectedPlayback.Current) > 1e-5 ||
            Math.Abs(actualPlayback.FadingFrom - expectedPlayback.FadingFrom) > 1e-5 ||
            Math.Abs(actualPlayback.FadingPosition - expectedPlayback.FadingPosition) > 1e-5 ||
            actualPlayback.CurrentNode != expectedPlayback.CurrentNode ||
            actualPlayback.FadingFromNode != expectedPlayback.FadingFromNode)
        {
            throw new InvalidOperationException(
                "Jump-to-Fall late rollback did not restore the committed airborne crossfade: " +
                $"poseExpected={expectedPose:X16} poseActual={controller.ComputePoseDigest(905):X16} " +
                $"currentExpected={expectedPlayback.Current:R} currentActual={actualPlayback.Current:R} " +
                $"fadingExpected={expectedPlayback.FadingFrom:R} " +
                $"fadingActual={actualPlayback.FadingFrom:R} " +
                $"fadePosExpected={expectedPlayback.FadingPosition:R} " +
                $"fadePosActual={actualPlayback.FadingPosition:R} " +
                $"nodesExpected={expectedPlayback.FadingFromNode}->{expectedPlayback.CurrentNode} " +
                $"nodesActual={actualPlayback.FadingFromNode}->{actualPlayback.CurrentNode}.");
        }

        using var referenceLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(referenceLibrary.Root);
        using var referenceGraph = AlsLocomotionGraphBuilder.Build(
            referenceLibrary, locomotionProfile, poseProfile, definition);
        using var referenceController = new AlsLocomotionAnimationController(
            referenceGraph, settings, poseProfile, definition);
        referenceController.Warmup();
        var referenceResult = ValidResult();
        ApplySequence(referenceController, ref referenceResult, in disabled);
        var referencePlayback = CapturePlayback(referenceGraph);
        if (referenceController.ComputePoseDigest(906) != controller.ComputePoseDigest(906) ||
            Math.Abs(referencePlayback.Current - actualPlayback.Current) > 1e-5 ||
            Math.Abs(referencePlayback.FadingFrom - actualPlayback.FadingFrom) > 1e-5 ||
            Math.Abs(referencePlayback.FadingPosition - actualPlayback.FadingPosition) > 1e-5 ||
            referencePlayback.CurrentNode != actualPlayback.CurrentNode ||
            referencePlayback.FadingFromNode != actualPlayback.FadingFromNode)
        {
            throw new InvalidOperationException(
                "Jump-to-Fall rollback differed from a never-failed reference controller.");
        }

        static void ApplySequence(
            AlsLocomotionAnimationController target,
            ref AlsFrameResult frame,
            in AlsP4AnimationInput input)
        {
            target.Apply(in frame, in input, 0.0);
            frame.AnimationState = AlsAnimationState.JumpStart;
            frame.ResolvedLocomotionState = AlsLocomotionState.InAir;
            target.Apply(in frame, in input, 0.0);
            target.Apply(in frame, in input, 0.1);
            frame.AnimationState = AlsAnimationState.FallLoop;
            target.Apply(in frame, in input, 0.0);
            target.Apply(in frame, in input, 0.04);
        }

        static (double Current, double FadingFrom, double FadingPosition,
            StringName CurrentNode, StringName FadingFromNode) CapturePlayback(
            AlsLocomotionGraphBuildResult target)
        {
            var playback = target.Tree.Get(target.Handles.TopPlaybackPath)
                .As<AnimationNodeStateMachinePlayback>()
                ?? throw new InvalidOperationException("Top playback is missing.");
            return (
                playback.GetCurrentPlayPosition(),
                playback.GetFadingFromPlayPosition(),
                playback.GetFadingPosition(),
                playback.GetCurrentNode(),
                playback.GetFadingFromNode());
        }
    }

    private void VerifyLibraryContract(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile)
    {
        var duplicateTurns = poseProfile.Turns;
        duplicateTurns[1] = duplicateTurns[0];
        ExpectInvalid(poseProfile with { Turns = duplicateTurns }, "duplicate");

        var missingTurns = poseProfile.Turns;
        missingTurns[0] = missingTurns[0] with { AnimationId = definition.Animations.Length };
        ExpectInvalid(poseProfile with { Turns = missingTurns }, "missing");
        ExpectInvalid(
            poseProfile with { SkeletonId = definition.Skeletons.Length },
            "foreign skeleton");

        void ExpectInvalid(AlsPoseAnimationProfile malformed, string label)
        {
            var childrenBefore = GetChildCount();
            try
            {
                using var unexpected = AlsAnimationLibraryBuilder.Build(
                    definition, locomotionProfile, malformed);
                throw new InvalidOperationException(
                    $"P4 library accepted a {label} profile.");
            }
            catch (InvalidOperationException exception) when (
                !exception.Message.StartsWith("P4 library accepted", StringComparison.Ordinal))
            {
            }
            if (GetChildCount() != childrenBefore)
            {
                throw new InvalidOperationException(
                    $"P4 library {label} validation leaked a scene child.");
            }
        }
    }

    private void VerifyAuthoritativeActionPhase(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        var turn = poseProfile.Turns.First(value =>
            definition.Animations[value.AnimationId].PlayLength > 2.2f);
        var duration = definition.Animations[turn.AnimationId].PlayLength;
        var phaseSeconds = MathF.Min(1.75f, duration * 0.75f);

        using var graphLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(graphLibrary.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            graphLibrary, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var result = ValidResult();
        var p4 = AlsP4AnimationInput.Turn(
            turn.AnimationId, 1.7f, phaseSeconds, 0.5f, 0f, 0f, 0f);

        controller.Apply(in result, in p4, 0.0);
        controller.Apply(in result, in p4, turn.BlendSeconds * 0.5);
        controller.Apply(in result, in p4, turn.BlendSeconds * 0.5);
        controller.Apply(in result, in p4, 0.0);
        var zeroDeltaPose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);

        using var directLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(directLibrary.Root);
        if (!directLibrary.ClipNames.TryGetValue(turn.AnimationId, out var clipName))
        {
            throw new InvalidOperationException("Direct P4 phase fixture has no exact clip name.");
        }
        using var qualifiedName = new StringName($"als/{clipName}");
        directLibrary.Player.Play(qualifiedName);
        directLibrary.Player.Seek(phaseSeconds, true);
        directLibrary.Player.Advance(0.0);
        var directPose = AlsPoseDigest.CapturePoses(directLibrary.Skeleton, PoseBoneNames);
        if (AlsPoseDigest.HasChanged(directPose, zeroDeltaPose))
        {
            var playback = graph.Tree.Get(
                graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>();
            var actionState = $"{graph.Tree.Get(graph.Handles.P4!.TurnBankACurrentStatePath).AsString()}/" +
                graph.Tree.Get(graph.Handles.P4.TurnBankBCurrentStatePath).AsString();
            throw new InvalidOperationException(
                $"P4 TimeSeek phase differs from direct animation sampling at delta=0: " +
                $"phase={phaseSeconds:R} duration={duration:R} rate=1.7 " +
                $"graphPosition={playback?.GetCurrentPlayPosition():R} " +
                $"directPosition={directLibrary.Player.CurrentAnimationPosition:R} " +
                $"inner={actionState} " +
                $"delta={DescribeFirstPoseDelta(directPose, zeroDeltaPose)}");
        }

        controller.Apply(in result, in p4, DeltaTime);
        var frameDeltaPose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
        if (AlsPoseDigest.HasChanged(directPose, frameDeltaPose))
        {
            throw new InvalidOperationException(
                $"P4 TimeSeek phase advanced beyond canonical input at delta={DeltaTime:R}: " +
                $"phase={phaseSeconds:R} duration={duration:R} rate=1.7");
        }

        static string DescribeFirstPoseDelta(AlsBonePose[] expected, AlsBonePose[] actual)
        {
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    return $"bone={PoseBoneNames[index]} expected={expected[index]} actual={actual[index]}";
                }
            }
            return "raw-equal";
        }
    }

    private void VerifyControllerAllocation(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
        controller.Warmup();
        var result = ValidResult();
        var firstTurn = poseProfile.Turns[0];
        var secondTurn = poseProfile.Turns[1];
        var firstInput = AlsP4AnimationInput.Turn(
            firstTurn.AnimationId,
            firstTurn.BasePlayRate,
            definition.Animations[firstTurn.AnimationId].PlayLength * 0.5f,
            0.5f,
            0f,
            1f,
            0f);
        var secondInput = AlsP4AnimationInput.Turn(
            secondTurn.AnimationId,
            secondTurn.BasePlayRate,
            definition.Animations[secondTurn.AnimationId].PlayLength * 0.5f,
            0.5f,
            0f,
            1f,
            0f);
        for (var index = 0; index < 64; index++)
        {
            var prepared = controller.PrepareFrame(in result, in firstInput, 0.0);
            controller.SampleFootCurves(in prepared);
            controller.ApplyPrepared(in prepared);
            controller.CommitPrepared(in prepared);
        }
        var switchPrepared = controller.PrepareFrame(in result, in secondInput, 0.0);
        controller.SampleFootCurves(in switchPrepared);
        controller.ApplyPrepared(in switchPrepared);
        controller.CommitPrepared(in switchPrepared);

        var curveAccumulator = 0f;
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            var prepared = controller.PrepareFrame(in result, in secondInput, 0.0);
            var curves = controller.SampleFootCurves(in prepared);
            curveAccumulator += curves.LeftLockCurve + curves.RightLockCurve;
            controller.ApplyPrepared(in prepared);
            controller.CommitPrepared(in prepared);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        if (allocated != 0 || !float.IsFinite(curveAccumulator))
        {
            throw new InvalidOperationException(
                $"Steady P4 controller Apply allocated managed memory: {allocated} B");
        }
    }

    private void VerifyTurnBlend(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var graphLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(graphLibrary.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            graphLibrary, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        controller.Apply(in result, in disabled, 0.0);
        var basePose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
        var first = poseProfile.Turns[3];
        var second = poseProfile.Turns[6];
        var firstPhase = definition.Animations[first.AnimationId].PlayLength * 0.75f;
        var secondPhase = definition.Animations[second.AnimationId].PlayLength * 0.6f;
        var firstInput = AlsP4AnimationInput.Turn(
            first.AnimationId, 1.7f, firstPhase, 0.5f, 0f, 0f, 0f);
        var secondInput = AlsP4AnimationInput.Turn(
            second.AnimationId, 0.8f, secondPhase, 0.5f, 0f, 0f, 0f);

        using var directLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(directLibrary.Root);
        var firstDirect = SampleDirect(first.AnimationId, firstPhase);
        var secondDirect = SampleDirect(second.AnimationId, secondPhase);

        controller.Apply(in result, in firstInput, 0.0);
        AssertSame(basePose, Capture(), "Turn entry t=0 did not preserve the base pose");
        controller.Apply(in result, in firstInput, 0.1);
        AssertIntermediate(basePose, firstDirect, Capture(), "Turn entry t=0.1");
        controller.Apply(in result, in firstInput, 0.1);
        AssertSame(firstDirect, Capture(), "Turn entry t=0.2 did not reach the direct target");

        controller.Apply(in result, in secondInput, 0.0);
        AssertSame(firstDirect, Capture(), "Turn switch t=0 did not preserve the source clip");
        controller.Apply(in result, in secondInput, 0.1);
        AssertIntermediate(firstDirect, secondDirect, Capture(), "Turn switch t=0.1");
        controller.Apply(in result, in secondInput, 0.1);
        AssertSame(secondDirect, Capture(), "Turn switch t=0.2 did not reach the direct target");

        var rotate = poseProfile.Rotates[0];
        var rotatePhase = definition.Animations[rotate.AnimationId].PlayLength * 0.4f;
        var rotateInput = AlsP4AnimationInput.Rotate(
            rotate.AnimationId, 1.1f, rotatePhase, 0.5f, 0f, 0f, 0f);
        var rotateDirect = SampleDirect(rotate.AnimationId, rotatePhase);
        controller.Apply(in result, in rotateInput, 0.0);
        AssertSame(secondDirect, Capture(), "Turn to Rotate t=0 passed through base or popped");
        controller.Apply(in result, in rotateInput, 0.04);
        AssertIntermediate(secondDirect, rotateDirect, Capture(), "Turn to Rotate t=0.04");
        controller.Apply(in result, in rotateInput, 0.04);
        AssertSame(rotateDirect, Capture(), "Turn to Rotate t=0.08 did not reach Rotate");
        controller.Apply(in result, in secondInput, 0.0);
        AssertSame(rotateDirect, Capture(), "Rotate to Turn t=0 passed through base or popped");
        controller.Apply(in result, in secondInput, 0.04);
        AssertIntermediate(rotateDirect, secondDirect, Capture(), "Rotate to Turn t=0.04");
        controller.Apply(in result, in secondInput, 0.04);
        AssertSame(secondDirect, Capture(), "Rotate to Turn t=0.08 did not reach Turn");

        controller.Apply(in result, in disabled, 0.0);
        AssertSame(
            secondDirect,
            Capture(),
            $"Turn exit t=0 did not preserve the source clip " +
            $"actionBlend={graph.Tree.Get(graph.Handles.P4!.ActionBlendPath).AsSingle():R} " +
            $"turnBlend={graph.Tree.Get(graph.Handles.P4.TurnBlendPath).AsSingle():R}");
        controller.Apply(in result, in disabled, 0.1);
        AssertIntermediate(secondDirect, basePose, Capture(), "Turn exit t=0.1");
        controller.Apply(in result, in disabled, 0.1);
        AssertSame(basePose, Capture(), "Turn exit t=0.2 did not reach the base pose");
        if (controller.ManualAdvanceCount != 16)
        {
            throw new InvalidOperationException(
                $"Turn blend gate advance mismatch: {controller.ManualAdvanceCount}");
        }

        AlsBonePose[] SampleDirect(int animationId, float phase)
        {
            if (!directLibrary.ClipNames.TryGetValue(animationId, out var clipName))
            {
                throw new InvalidOperationException(
                    $"Turn blend direct fixture has no exact clip: {animationId}");
            }
            using var qualifiedName = new StringName($"als/{clipName}");
            directLibrary.Player.Play(qualifiedName);
            directLibrary.Player.Seek(phase, true);
            directLibrary.Player.Advance(0.0);
            return AlsPoseDigest.CapturePoses(directLibrary.Skeleton, PoseBoneNames);
        }

        AlsBonePose[] Capture() => AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
        static void AssertSame(AlsBonePose[] expected, AlsBonePose[] actual, string message)
        {
            if (AlsPoseDigest.HasChanged(expected, actual))
            {
                for (var index = 0; index < expected.Length; index++)
                {
                    if (expected[index] != actual[index])
                    {
                        throw new InvalidOperationException(
                            $"{message}: bone={PoseBoneNames[index]} " +
                            $"expected={expected[index]} actual={actual[index]}");
                    }
                }
                throw new InvalidOperationException(message);
            }
        }
        static void AssertIntermediate(
            AlsBonePose[] source,
            AlsBonePose[] target,
            AlsBonePose[] actual,
            string label)
        {
            if (!AlsPoseDigest.HasChanged(source, actual) ||
                !AlsPoseDigest.HasChanged(target, actual))
            {
                throw new InvalidOperationException($"{label} did not produce an intermediate pose.");
            }
        }
    }

    private void VerifyInterruptedActionBlend(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var graphLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(graphLibrary.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            graphLibrary, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        using var directLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(directLibrary.Root);
        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        controller.Apply(in result, in disabled, 0.0);
        var basePose = Capture();

        var a = TurnInput(0, 0.35f);
        var b = TurnInput(1, 0.65f);
        var c = TurnInput(2, 0.45f);
        var d = TurnInput(4, 0.55f);
        var aDirect = SampleDirect(poseProfile.Turns[0].AnimationId, a.TurnPhase);
        var bDirect = SampleDirect(poseProfile.Turns[1].AnimationId, b.TurnPhase);
        var cDirect = SampleDirect(poseProfile.Turns[2].AnimationId, c.TurnPhase);
        var dDirect = SampleDirect(poseProfile.Turns[4].AnimationId, d.TurnPhase);

        EnterTurn(in a, aDirect);
        controller.Apply(in result, in b, 0.0);
        controller.Apply(in result, in b, 0.05);
        var quarterPose = Capture();
        AssertIntermediate(aDirect, bDirect, quarterPose, "Turn A->B 25%");
        controller.Apply(in result, in c, 0.0);
        AssertSame(quarterPose, Capture(), "Turn A->B 25% retarget C changed the delta=0 pose");
        controller.Apply(in result, in d, 0.0);
        AssertSame(quarterPose, Capture(), "Turn A->B 25% latest retarget D changed the delta=0 pose");
        controller.Apply(in result, in d, 0.15);
        AssertSame(bDirect, Capture(), "Turn delayed retarget did not first complete B");
        controller.Apply(in result, in d, 0.1);
        AssertIntermediate(bDirect, dDirect, Capture(), "Turn B->latest D 50%");
        controller.Apply(in result, in d, 0.1);
        AssertSame(dDirect, Capture(), "Turn delayed retarget did not select the latest D");

        ExitTurn(in disabled, dDirect, basePose);
        EnterTurn(in a, aDirect);
        controller.Apply(in result, in b, 0.0);
        controller.Apply(in result, in b, 0.1);
        var halfPose = Capture();
        AssertIntermediate(aDirect, bDirect, halfPose, "Turn A->B 50%");
        controller.Apply(in result, in disabled, 0.0);
        AssertSame(halfPose, Capture(), "Turn A->B 50% exit changed the delta=0 pose");
        controller.Apply(in result, in disabled, 0.1);
        AssertIntermediate(halfPose, basePose, Capture(), "Interrupted Turn exit 50%");
        controller.Apply(in result, in disabled, 0.1);
        AssertSame(basePose, Capture(), "Interrupted Turn exit did not reach Base");
        controller.Apply(in result, in c, 0.0);
        AssertSame(basePose, Capture(), "Turn re-entry after exit changed the delta=0 base pose");
        controller.Apply(in result, in c, poseProfile.Turns[2].BlendSeconds);
        AssertSame(cDirect, Capture(), "Turn re-entry retained a stale interrupted blend");

        controller.Apply(in result, in b, 0.0);
        controller.Apply(in result, in b, 0.05);
        quarterPose = Capture();
        AssertIntermediate(cDirect, bDirect, quarterPose, "Turn C->B 25%");
        var rotateA = RotateInput(0, 0.4f);
        var rotateB = RotateInput(1, 0.6f);
        var rotateADirect = SampleDirect(
            poseProfile.Rotates[0].AnimationId, rotateA.RotatePhase);
        var rotateBDirect = SampleDirect(
            poseProfile.Rotates[1].AnimationId, rotateB.RotatePhase);
        controller.Apply(in result, in rotateA, 0.0);
        AssertSame(quarterPose, Capture(), "Turn->Rotate interrupted blend changed the delta=0 pose");
        controller.Apply(in result, in rotateA, 0.04);
        AssertIntermediate(quarterPose, rotateADirect, Capture(), "Turn composite->Rotate 50%");
        controller.Apply(in result, in rotateA, 0.04);
        AssertSame(rotateADirect, Capture(), "Turn composite->Rotate did not reach Rotate");

        controller.Apply(in result, in rotateB, 0.0);
        controller.Apply(in result, in rotateB, 0.04);
        var rotateHalfPose = Capture();
        AssertIntermediate(rotateADirect, rotateBDirect, rotateHalfPose, "Rotate A->B 50%");
        controller.Apply(in result, in c, 0.0);
        AssertSame(rotateHalfPose, Capture(), "Rotate->Turn interrupted blend changed the delta=0 pose");
        controller.Apply(in result, in c, 0.04);
        AssertIntermediate(rotateHalfPose, cDirect, Capture(), "Rotate composite->Turn 50%");
        controller.Apply(in result, in c, 0.04);
        AssertSame(cDirect, Capture(), "Rotate composite->Turn did not reach authoritative Turn phase");

        AlsP4AnimationInput TurnInput(int index, float normalizedPhase)
        {
            var turn = poseProfile.Turns[index];
            return AlsP4AnimationInput.Turn(
                turn.AnimationId,
                turn.BasePlayRate,
                definition.Animations[turn.AnimationId].PlayLength * normalizedPhase,
                0f,
                0f,
                0f,
                0f);
        }

        AlsP4AnimationInput RotateInput(int index, float normalizedPhase)
        {
            var rotate = poseProfile.Rotates[index];
            return AlsP4AnimationInput.Rotate(
                rotate.AnimationId,
                1f,
                definition.Animations[rotate.AnimationId].PlayLength * normalizedPhase,
                0f,
                0f,
                0f,
                0f);
        }

        void EnterTurn(in AlsP4AnimationInput input, AlsBonePose[] direct)
        {
            controller.Apply(in result, in input, 0.0);
            AssertSame(basePose, Capture(), "Turn entry changed the delta=0 base pose");
            controller.Apply(in result, in input, 0.2);
            AssertSame(direct, Capture(), "Turn entry did not reach the direct pose");
        }

        void ExitTurn(
            in AlsP4AnimationInput input,
            AlsBonePose[] direct,
            AlsBonePose[] targetBase)
        {
            controller.Apply(in result, in input, 0.0);
            AssertSame(direct, Capture(), "Turn exit changed the delta=0 action pose");
            controller.Apply(in result, in input, 0.2);
            AssertSame(targetBase, Capture(), "Turn exit did not reach Base");
        }

        AlsBonePose[] SampleDirect(int animationId, float phase)
        {
            if (!directLibrary.ClipNames.TryGetValue(animationId, out var clipName))
            {
                throw new InvalidOperationException(
                    $"Interrupted blend direct fixture has no exact clip: {animationId}");
            }
            using var qualifiedName = new StringName($"als/{clipName}");
            directLibrary.Player.Play(qualifiedName);
            directLibrary.Player.Seek(phase, true);
            directLibrary.Player.Advance(0.0);
            return AlsPoseDigest.CapturePoses(directLibrary.Skeleton, PoseBoneNames);
        }

        AlsBonePose[] Capture() => AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
        static void AssertSame(AlsBonePose[] expected, AlsBonePose[] actual, string message)
        {
            if (AlsPoseDigest.HasChanged(expected, actual))
            {
                throw new InvalidOperationException(message);
            }
        }
        static void AssertIntermediate(
            AlsBonePose[] source,
            AlsBonePose[] target,
            AlsBonePose[] actual,
            string label)
        {
            if (!AlsPoseDigest.HasChanged(source, actual) ||
                !AlsPoseDigest.HasChanged(target, actual))
            {
                throw new InvalidOperationException($"{label} did not produce an intermediate pose.");
            }
        }
    }

    private void VerifyZeroDurationTurnBlend(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        var zeroTurns = poseProfile.Turns;
        zeroTurns[1] = zeroTurns[1] with { BlendSeconds = 0f };
        var zeroProfile = poseProfile with { Turns = zeroTurns };
        using var graphLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, zeroProfile);
        AddChild(graphLibrary.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            graphLibrary, locomotionProfile, zeroProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        using var directLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, zeroProfile);
        AddChild(directLibrary.Root);
        var result = ValidResult();
        var first = zeroProfile.Turns[0];
        var second = zeroProfile.Turns[1];
        var firstPhase = definition.Animations[first.AnimationId].PlayLength * 0.4f;
        var secondPhase = definition.Animations[second.AnimationId].PlayLength * 0.7f;
        var firstInput = AlsP4AnimationInput.Turn(
            first.AnimationId, first.BasePlayRate, firstPhase, 0f, 0f, 0f, 0f);
        var secondInput = AlsP4AnimationInput.Turn(
            second.AnimationId, second.BasePlayRate, secondPhase, 0f, 0f, 0f, 0f);
        controller.Apply(in result, in firstInput, 0.0);
        controller.Apply(in result, in firstInput, first.BlendSeconds);
        controller.Apply(in result, in secondInput, 0.0);

        if (!directLibrary.ClipNames.TryGetValue(second.AnimationId, out var clipName))
        {
            throw new InvalidOperationException(
                $"Zero-duration Turn direct fixture has no exact clip: {second.AnimationId}");
        }
        using var qualifiedName = new StringName($"als/{clipName}");
        directLibrary.Player.Play(qualifiedName);
        directLibrary.Player.Seek(secondPhase, true);
        directLibrary.Player.Advance(0.0);
        var directPose = AlsPoseDigest.CapturePoses(directLibrary.Skeleton, PoseBoneNames);
        var graphPose = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
        if (AlsPoseDigest.HasChanged(directPose, graphPose))
        {
            throw new InvalidOperationException(
                "Turn BlendSeconds=0 did not display and commit the target bank at delta=0.");
        }
    }

    private void VerifyAimEndpoints(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile locomotionProfile,
        AlsPoseAnimationProfile poseProfile,
        AlsLocomotionSettings settings)
    {
        using var graphLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(graphLibrary.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            graphLibrary, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var result = ValidResult();
        var turn = poseProfile.Turns[2];
        var turnPhase = definition.Animations[turn.AnimationId].PlayLength * 0.5f;
        var baseInput = AlsP4AnimationInput.Turn(
            turn.AnimationId, 1f, turnPhase, 0f, 0f, 0f, 0f);
        controller.Apply(in result, in baseInput, 0.0);
        controller.Apply(in result, in baseInput, turn.BlendSeconds * 0.5);
        controller.Apply(in result, in baseInput, turn.BlendSeconds * 0.5);

        using var directLibrary = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(directLibrary.Root);
        var directions = new[]
        {
            ("Down", poseProfile.Aim.DownAnimationId, 1f, 0f, 0f),
            ("Forward", poseProfile.Aim.ForwardAnimationId, 0f, 1f, 0f),
            ("Up", poseProfile.Aim.UpAnimationId, 0f, 0f, 1f),
        };
        foreach (var direction in directions)
        {
            var graphZero = SampleGraph(0f);
            var graphHalf = SampleGraph(0.5f);
            var graphNearOne = SampleGraph(MathF.BitDecrement(1f));
            var graphOne = SampleGraph(1f);
            var directZero = SampleDirect(0f);
            var directHalf = SampleDirect(0.5f);
            var directOne = SampleDirect(1f);
            if (!AlsPoseDigest.HasChanged(graphZero, graphHalf) ||
                !AlsPoseDigest.HasChanged(graphHalf, graphOne) ||
                !AlsPoseDigest.HasChanged(graphZero, graphOne) ||
                !AlsPoseDigest.HasChanged(directZero, directHalf) ||
                !AlsPoseDigest.HasChanged(directHalf, directOne) ||
                !AlsPoseDigest.HasChanged(directZero, directOne))
            {
                throw new InvalidOperationException(
                    $"P4 Aim {direction.Item1} normalized 0/0.5/1 samples were not distinct.");
            }
            AssertDirectMotion(
                direction.Item1, "0->0.5", graphZero, graphHalf, directZero, directHalf);
            AssertDirectMotion(
                direction.Item1, "0.5->1", graphHalf, graphOne, directHalf, directOne);
            var endpointDistance = PoseDistance(graphNearOne, graphOne);
            var halfDistance = PoseDistance(graphHalf, graphOne);
            if (!double.IsFinite(endpointDistance) ||
                endpointDistance > Math.Max(1e-5, halfDistance * 0.001))
            {
                throw new InvalidOperationException(
                    $"P4 Aim {direction.Item1} was discontinuous at 1-epsilon/1: " +
                    $"endpoint={endpointDistance:R} half={halfDistance:R}");
            }

            AlsBonePose[] SampleGraph(float phase)
            {
                var input = AlsP4AnimationInput.Turn(
                    turn.AnimationId,
                    1f,
                    turnPhase,
                    phase,
                    direction.Item3,
                    direction.Item4,
                    direction.Item5);
                controller.Apply(in result, in input, 0.0);
                return AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
            }

            AlsBonePose[] SampleDirect(float phase)
            {
                if (!directLibrary.ClipNames.TryGetValue(direction.Item2, out var clipName))
                {
                    throw new InvalidOperationException(
                        $"P4 Aim direct fixture is missing: {direction.Item2}");
                }
                using var qualifiedName = new StringName($"als/{clipName}");
                directLibrary.Player.Play(qualifiedName);
                directLibrary.Player.Seek(
                    definition.Animations[direction.Item2].PlayLength * phase,
                    true);
                directLibrary.Player.Advance(0.0);
                return AlsPoseDigest.CapturePoses(directLibrary.Skeleton, PoseBoneNames);
            }
        }

        static double PoseDistance(AlsBonePose[] left, AlsBonePose[] right)
        {
            var distance = 0.0;
            for (var index = 0; index < left.Length; index++)
            {
                distance += (left[index].Position - right[index].Position).Length();
                distance += (left[index].Scale - right[index].Scale).Length();
                distance += Math.Abs(left[index].Rotation.X - right[index].Rotation.X);
                distance += Math.Abs(left[index].Rotation.Y - right[index].Rotation.Y);
                distance += Math.Abs(left[index].Rotation.Z - right[index].Rotation.Z);
                distance += Math.Abs(left[index].Rotation.W - right[index].Rotation.W);
            }
            return distance;
        }

        static void AssertDirectMotion(
            string direction,
            string interval,
            AlsBonePose[] graphFrom,
            AlsBonePose[] graphTo,
            AlsBonePose[] directFrom,
            AlsBonePose[] directTo)
        {
            for (var index = 0; index < graphFrom.Length; index++)
            {
                var graphPosition = (graphTo[index].Position - graphFrom[index].Position).Length();
                var directPosition = (directTo[index].Position - directFrom[index].Position).Length();
                var graphScale = (graphTo[index].Scale - graphFrom[index].Scale).Length();
                var directScale = (directTo[index].Scale - directFrom[index].Scale).Length();
                var graphRotation = RotationAngle(graphFrom[index].Rotation, graphTo[index].Rotation);
                var directRotation = RotationAngle(directFrom[index].Rotation, directTo[index].Rotation);
                if (!Near(graphPosition, directPosition) ||
                    !Near(graphScale, directScale) ||
                    !NearRotation(graphRotation, directRotation))
                {
                    throw new InvalidOperationException(
                        $"P4 Aim {direction} {interval} did not match direct clip motion: " +
                        $"bone={PoseBoneNames[index]} " +
                        $"position={graphPosition:R}/{directPosition:R} " +
                        $"scale={graphScale:R}/{directScale:R} " +
                        $"rotation={graphRotation:R}/{directRotation:R}");
                }
            }

            static bool Near(double left, double right) =>
                Math.Abs(left - right) <= Math.Max(1e-5, Math.Max(left, right) * 1e-4);

            static bool NearRotation(double left, double right) =>
                Math.Abs(left - right) <= Math.Max(1e-3, Math.Max(left, right) * 1e-4);

            static double RotationAngle(Quaternion left, Quaternion right)
            {
                var dot = Math.Clamp(Math.Abs(left.Dot(right)), 0f, 1f);
                return 2.0 * Math.Acos(dot);
            }
        }
    }

    private static void VerifyCurveSampler()
    {
        var curves = new[]
        {
            new AlsFloatCurveDefinition(3, AlsCanonicalCurveKind.None, "Constant",
                AlsCurveProvenance.SourceCurve,
                [
                    new AlsFloatCurveKeyDefinition(0f, 2f, 0f, 0f, AlsCurveInterpolation.Constant),
                    new AlsFloatCurveKeyDefinition(2f, 8f, 0f, 0f, AlsCurveInterpolation.Linear),
                ]),
            new AlsFloatCurveDefinition(7, AlsCanonicalCurveKind.None, "Linear",
                AlsCurveProvenance.SourceCurve,
                [
                    new AlsFloatCurveKeyDefinition(0f, -2f, 0f, 0f, AlsCurveInterpolation.Linear),
                    new AlsFloatCurveKeyDefinition(2f, 2f, 0f, 0f, AlsCurveInterpolation.Linear),
                ]),
            new AlsFloatCurveDefinition(11, AlsCanonicalCurveKind.None, "Cubic",
                AlsCurveProvenance.SourceCurve,
                [
                    new AlsFloatCurveKeyDefinition(1f, 1f, 0f, 4f, AlsCurveInterpolation.Cubic),
                    new AlsFloatCurveKeyDefinition(3f, 5f, -2f, 0f, AlsCurveInterpolation.Linear),
                ]),
            new AlsFloatCurveDefinition(13, AlsCanonicalCurveKind.None, "CubicOvershoot",
                AlsCurveProvenance.SourceCurve,
                [
                    new AlsFloatCurveKeyDefinition(0f, 0f, 0f, 4f, AlsCurveInterpolation.Cubic),
                    new AlsFloatCurveKeyDefinition(2f, 1f, -2f, 0f, AlsCurveInterpolation.Linear),
                ]),
            new AlsFloatCurveDefinition(
                19,
                AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond,
                "RotationYawSpeedRadiansPerSecond",
                AlsCurveProvenance.DerivedRootTrack,
                [
                    new AlsFloatCurveKeyDefinition(0f, -2f, 0f, 0f, AlsCurveInterpolation.Linear),
                    new AlsFloatCurveKeyDefinition(2f, 2f, 0f, 0f, AlsCurveInterpolation.Linear),
                ]),
        };
        var sampler = new AlsCurveSampler([curves[4], curves[0], curves[3], curves[1], curves[2]]);
        var values = new float[3];
        var ids = new[] { 3, 7, 11 };
        var samplerDigest = DigestOffset;

        AssertSamples(-1f, 2f, -2f, 1f);
        AssertSamples(0f, 2f, -2f, 1f);
        AssertSamples(1f, 2f, 0f, 1f);
        AssertSamples(2f, 8f, 2f, 4.5f);
        AssertSamples(3f, 8f, 2f, 5f);
        AssertSamples(5f, 8f, 2f, 5f);

        Array.Fill(values, 91f);
        if (sampler.TrySample(new[] { 3, 999, 11 }, 1f, values) ||
            values[0] != 91f || values[1] != 91f || values[2] != 91f)
        {
            throw new InvalidOperationException("Invalid curve ID polluted the caller buffer.");
        }
        if (sampler.TrySample(ids, float.NaN, values) || values[0] != 91f)
        {
            throw new InvalidOperationException("Non-finite curve time polluted the caller buffer.");
        }
        AppendFloatBits(ref samplerDigest, values[0]);
        AppendFloatBits(ref samplerDigest, values[1]);
        AppendFloatBits(ref samplerDigest, values[2]);
        if (sampler.TrySample(ids, 1f, values.AsSpan(0, 2)))
        {
            throw new InvalidOperationException("Short curve destination was accepted.");
        }

        if (!sampler.TrySample(13, 1f, out var finiteOvershoot))
        {
            throw new InvalidOperationException("Finite Cubic overshoot sample failed.");
        }
        AssertFloatBits(finiteOvershoot, 2f, "Cubic tangent-unit overshoot");
        AppendFloatBits(ref samplerDigest, finiteOvershoot);

        if (curves[4].CanonicalKind is not AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond ||
            curves[4].Provenance is not AlsCurveProvenance.DerivedRootTrack ||
            !sampler.TrySample(19, 0.5f, out var negativeYaw) ||
            !sampler.TrySample(19, 1.5f, out var positiveYaw))
        {
            throw new InvalidOperationException("Canonical derived yaw curve was not sampleable.");
        }
        AssertFloatBits(negativeYaw, -1f, "Canonical negative yaw");
        AssertFloatBits(positiveYaw, 1f, "Canonical positive yaw");
        AppendFloatBits(ref samplerDigest, negativeYaw);
        AppendFloatBits(ref samplerDigest, positiveYaw);

        var callerOrderIds = new[] { 19, 3, 13, 7, 11 };
        var callerOrderValues = new float[callerOrderIds.Length];
        if (!sampler.TrySample(callerOrderIds, 0.5f, callerOrderValues))
        {
            throw new InvalidOperationException("Caller-ordered multi-curve sample failed.");
        }
        var callerOrderExpected = new[] { -1f, 2f, 1.46875f, -1f, 1f };
        for (var index = 0; index < callerOrderValues.Length; index++)
        {
            AssertFloatBits(
                callerOrderValues[index],
                callerOrderExpected[index],
                $"Caller-ordered curve {callerOrderIds[index]}");
            AppendFloatBits(ref samplerDigest, callerOrderValues[index]);
        }

        var duplicateIds = new[] { 7, 7, 3 };
        var longDestination = new[] { 83f, 83f, 83f, 89f, 97f };
        if (!sampler.TrySample(duplicateIds, 0.5f, longDestination))
        {
            throw new InvalidOperationException("Duplicate caller curve IDs were rejected.");
        }
        AssertFloatBits(longDestination[0], -1f, "First duplicate curve");
        AssertFloatBits(longDestination[1], -1f, "Second duplicate curve");
        AssertFloatBits(longDestination[2], 2f, "Duplicate batch trailing sample");
        AssertFloatBits(longDestination[3], 89f, "Oversized destination first tail");
        AssertFloatBits(longDestination[4], 97f, "Oversized destination second tail");
        for (var index = 0; index < longDestination.Length; index++)
        {
            AppendFloatBits(ref samplerDigest, longDestination[index]);
        }

        var direct = new AlsCurveSampler(23, curves[1].Keys);
        if (!direct.TrySample(23, 1f, out var directValue) || directValue != 0f)
        {
            throw new InvalidOperationException("Direct key-array curve binding failed.");
        }

        var negativeTime = new AlsCurveSampler(29,
        [
            new AlsFloatCurveKeyDefinition(-2f, -1f, 0f, 0f, AlsCurveInterpolation.Linear),
            new AlsFloatCurveKeyDefinition(-1f, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
        ]);
        if (!negativeTime.TrySample(29, -1.5f, out var negativeTimeValue))
        {
            throw new InvalidOperationException("Negative direct key times were rejected.");
        }
        AssertFloatBits(negativeTimeValue, 0f, "Negative direct key-time midpoint");
        AppendFloatBits(ref samplerDigest, negativeTimeValue);

        ExpectInvalid([
            new AlsFloatCurveKeyDefinition(1f, 0f, 0f, 0f, AlsCurveInterpolation.Linear),
            new AlsFloatCurveKeyDefinition(1f, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
        ]);
        ExpectInvalid([
            new AlsFloatCurveKeyDefinition(0f, 0f, 0f, 0f, (AlsCurveInterpolation)byte.MaxValue),
        ]);
        ExpectArgumentNull(() => _ = new AlsCurveSampler((AlsFloatCurveDefinition[])null!));
        ExpectArgumentNull(() => _ = new AlsCurveSampler((AlsFloatCurveKeyDefinition[])null!));
        ExpectArgumentNull(() => _ = new AlsCurveSampler(41, (AlsFloatCurveKeyDefinition[])null!));
        ExpectNullCurveEntry(() => _ = new AlsCurveSampler([curves[0], null!]));
        ExpectInvalidCurves([curves[0], curves[0]]);
        ExpectInvalidCurves([
            new AlsFloatCurveDefinition(
                -1, AlsCanonicalCurveKind.None, "NegativeId", AlsCurveProvenance.SourceCurve,
                [new AlsFloatCurveKeyDefinition(0f, 0f, 0f, 0f, AlsCurveInterpolation.Linear)]),
        ]);
        ExpectInvalidCurves([
            new AlsFloatCurveDefinition(
                43, AlsCanonicalCurveKind.None, "Empty", AlsCurveProvenance.SourceCurve, []),
        ]);

        var snapshotKeys = new[]
        {
            new AlsFloatCurveKeyDefinition(0f, 3f, 0f, 0f, AlsCurveInterpolation.Linear),
        };
        var snapshotSampler = new AlsCurveSampler(47, snapshotKeys);
        snapshotKeys[0] = snapshotKeys[0] with { Value = 99f };
        if (!snapshotSampler.TrySample(47, 0f, out var snapshotValue))
        {
            throw new InvalidOperationException("Snapshot sampler was not sampleable.");
        }
        AssertFloatBits(snapshotValue, 3f, "Constructor key snapshot");

        var extremeLinear = new AlsCurveSampler(31,
        [
            new AlsFloatCurveKeyDefinition(0f, -float.MaxValue, 0f, 0f, AlsCurveInterpolation.Linear),
            new AlsFloatCurveKeyDefinition(2f, float.MaxValue, 0f, 0f, AlsCurveInterpolation.Linear),
        ]);
        if (!extremeLinear.TrySample(31, 1f, out var midpoint) || midpoint != 0f)
        {
            throw new InvalidOperationException("Extreme linear midpoint must remain finite zero.");
        }
        AssertFloatBits(midpoint, 0f, "Extreme linear midpoint");
        AppendFloatBits(ref samplerDigest, midpoint);

        var overshoot = new AlsCurveSampler(37,
        [
            new AlsFloatCurveKeyDefinition(0f, 0f, 0f, float.MaxValue, AlsCurveInterpolation.Cubic),
            new AlsFloatCurveKeyDefinition(float.MaxValue, 0f, -float.MaxValue, 0f, AlsCurveInterpolation.Linear),
        ]);
        var mixedSampler = new AlsCurveSampler([
            curves[1],
            new AlsFloatCurveDefinition(37, AlsCanonicalCurveKind.None, "Overflow",
                AlsCurveProvenance.SourceCurve,
                [
                    new AlsFloatCurveKeyDefinition(0f, 0f, 0f, float.MaxValue, AlsCurveInterpolation.Cubic),
                    new AlsFloatCurveKeyDefinition(float.MaxValue, 0f, -float.MaxValue, 0f, AlsCurveInterpolation.Linear),
                ]),
        ]);
        var overflowDestination = new[] { 71f, 73f };
        if (overshoot.TrySample(37, float.MaxValue * 0.5f, out var overflowValue) ||
            overflowValue != 0f ||
            mixedSampler.TrySample(new[] { 7, 37 }, float.MaxValue * 0.5f, overflowDestination) ||
            overflowDestination[0] != 71f || overflowDestination[1] != 73f)
        {
            throw new InvalidOperationException("Non-finite derived curve output was published transactionally.");
        }
        ExpectInvalid([
            new AlsFloatCurveKeyDefinition(0f, 0f, 0f, float.NaN, AlsCurveInterpolation.Cubic),
        ]);

        sampler.TrySample(ids, 1f, values);
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            if (!sampler.TrySample(ids, 1.25f, values))
            {
                throw new InvalidOperationException("Validated steady curve sample failed.");
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        if (allocated != 0)
        {
            throw new InvalidOperationException(
                $"Steady curve sampling allocated managed memory: {allocated} B");
        }
        Append(ref samplerDigest, unchecked((ulong)allocated));
        if (samplerDigest != ExpectedCurveSamplerDigest)
        {
            throw new InvalidOperationException(
                $"Curve sampler digest changed: expected={ExpectedCurveSamplerDigest:X16} actual={samplerDigest:X16}");
        }
        GD.Print(
            $"P4_CURVE_SAMPLER_OK digest={samplerDigest:X16} warm_curve_batch_allocation={allocated}B");

        void AssertSamples(float time, float constant, float linear, float cubic)
        {
            if (!sampler.TrySample(ids, time, values))
            {
                throw new InvalidOperationException(
                    $"Curve sample mismatch at {time:R}: [{values[0]:R},{values[1]:R},{values[2]:R}]");
            }
            AssertFloatBits(values[0], constant, $"Constant at {time:R}");
            AssertFloatBits(values[1], linear, $"Linear at {time:R}");
            AssertFloatBits(values[2], cubic, $"Cubic at {time:R}");
            AppendFloatBits(ref samplerDigest, values[0]);
            AppendFloatBits(ref samplerDigest, values[1]);
            AppendFloatBits(ref samplerDigest, values[2]);
        }

        static void AssertFloatBits(float actual, float expected, string label)
        {
            var actualBits = BitConverter.SingleToInt32Bits(actual);
            var expectedBits = BitConverter.SingleToInt32Bits(expected);
            if (actualBits != expectedBits)
            {
                throw new InvalidOperationException(
                    $"{label} bits changed: expected=0x{expectedBits:X8} actual=0x{actualBits:X8}");
            }
        }

        static void AppendFloatBits(ref ulong digest, float value) =>
            Append(ref digest, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

        static void ExpectInvalid(AlsFloatCurveKeyDefinition[] keys)
        {
            try
            {
                _ = new AlsCurveSampler(keys);
                throw new InvalidOperationException("Malformed curve keys were accepted.");
            }
            catch (ArgumentException)
            {
            }
        }

        static void ExpectArgumentNull(Action action)
        {
            try
            {
                action();
                throw new InvalidOperationException("Null curve constructor input was accepted.");
            }
            catch (ArgumentNullException)
            {
            }
        }

        static void ExpectNullCurveEntry(Action action)
        {
            try
            {
                action();
                throw new InvalidOperationException("Null curve entry was accepted.");
            }
            catch (ArgumentException exception) when (exception is not ArgumentNullException)
            {
            }
        }

        static void ExpectInvalidCurves(AlsFloatCurveDefinition[] invalidCurves)
        {
            try
            {
                _ = new AlsCurveSampler(invalidCurves);
                throw new InvalidOperationException("Malformed curve definitions were accepted.");
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static void VerifyNamedNodes(AlsLocomotionGraphBuildResult graph)
    {
        var root = graph.Tree.TreeRoot as AnimationNodeBlendTree
            ?? throw new InvalidOperationException("P4 graph root is not the required layered BlendTree.");
        foreach (var name in new[] { "Base", "P4Turn", "P4Rotate", "P4AimDown", "P4AimForward", "P4AimUp" })
        {
            using var nodeName = new StringName(name);
            if (!root.HasNode(nodeName))
            {
                throw new InvalidOperationException($"P4 graph node is missing: {name}");
            }
        }
    }

    private static void VerifyExactBindings(
        AlsLocomotionGraphBuildResult graph,
        AlsPoseAnimationProfile profile,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition)
    {
        var root = (AnimationNodeBlendTree)graph.Tree.TreeRoot;
        VerifyBank(graph.Handles.P4!.TurnStateName, profile.Turns.Select(value => value.AnimationId).ToArray());
        VerifyBank(graph.Handles.P4.RotateStateName, profile.Rotates.Select(value => value.AnimationId).ToArray());
        VerifyAim(graph.Handles.P4.AimDownStateName, profile.Aim.DownAnimationId);
        VerifyAim(graph.Handles.P4.AimForwardStateName, profile.Aim.ForwardAnimationId);
        VerifyAim(graph.Handles.P4.AimUpStateName, profile.Aim.UpAnimationId);

        for (var index = 0; index < profile.Turns.Length; index++)
        {
            var turn = profile.Turns[index];
            if (!graph.Handles.TryGetP4TurnSelection(turn.AnimationId, out var selection) ||
                selection != index)
            {
                throw new InvalidOperationException($"P4 Turn ID was not bound exactly: {turn.AnimationId}");
            }
            VerifyCurve(turn.AnimationId, turn.CurveId);
        }
        for (var index = 0; index < profile.Rotates.Length; index++)
        {
            var rotate = profile.Rotates[index];
            if (!graph.Handles.TryGetP4RotateSelection(rotate.AnimationId, out var selection) ||
                selection != index)
            {
                throw new InvalidOperationException($"P4 Rotate ID was not bound exactly: {rotate.AnimationId}");
            }
            VerifyCurve(rotate.AnimationId, rotate.CurveId);
        }

        void VerifyBank(StringName stateName, int[] animationIds)
        {
            var actionRoot = root.GetNode(stateName) as AnimationNodeBlendTree
                ?? throw new InvalidOperationException($"P4 action branch is invalid: {stateName}");
            foreach (var bankLabel in new[] { "BankA", "BankB" })
            {
                using var bankName = new StringName(bankLabel);
                var branch = actionRoot.GetNode(bankName) as AnimationNodeBlendTree
                    ?? throw new InvalidOperationException($"P4 action bank is missing: {stateName}/{bankLabel}");
                using var selectorName = new StringName("Select");
                var selector = branch.GetNode(selectorName) as AnimationNodeTransition
                    ?? throw new InvalidOperationException($"P4 action selector is missing: {stateName}/{bankLabel}");
                if (selector.InputCount != animationIds.Length || selector.XfadeTime != 0.0)
                {
                    throw new InvalidOperationException(
                        $"P4 action selector contract mismatch: {stateName}/{bankLabel}");
                }
                for (var index = 0; index < animationIds.Length; index++)
                {
                    var isTurn = stateName == graph.Handles.P4!.TurnStateName;
                    var found = isTurn
                        ? graph.Handles.P4.TryGetTurnBinding(animationIds[index], out var binding)
                        : graph.Handles.P4.TryGetRotateBinding(animationIds[index], out binding);
                    var clipBranch = found
                        ? branch.GetNode(binding.StateName) as AnimationNodeBlendTree
                        : null;
                    using var clipNodeName = new StringName("Clip");
                    var clip = clipBranch?.GetNode(clipNodeName) as AnimationNodeAnimation;
                    var animation = definition.Animations[animationIds[index]];
                    if (!found || clip is null || binding.Selection != index ||
                        !string.Equals(selector.GetInputName(index), binding.StateName.ToString(), StringComparison.Ordinal) ||
                        binding.DurationSeconds != animation.PlayLength ||
                        (isTurn && binding.BlendSeconds != profile.Turns[index].BlendSeconds) ||
                        clip.UseCustomTimeline || clip.StretchTimeScale ||
                        !library.ClipNames.TryGetValue(animationIds[index], out var clipName) ||
                        !string.Equals(clip.Animation.ToString(), $"als/{clipName}", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"P4 selector is not exactly bound: {stateName}/{bankLabel}/{animationIds[index]}");
                    }
                }
            }
        }

        void VerifyAim(StringName stateName, int animationId)
        {
            var branch = root.GetNode(stateName) as AnimationNodeBlendTree
                ?? throw new InvalidOperationException($"P4 Aim branch is invalid: {stateName}");
            using var aimName = new StringName("Aim");
            using var baseName = new StringName("AdditiveBase");
            using var deltaName = new StringName("Delta");
            var clip = branch.GetNode(aimName) as AnimationNodeAnimation
                ?? throw new InvalidOperationException($"P4 Aim clip is missing: {stateName}");
            var baseClip = branch.GetNode(baseName) as AnimationNodeAnimation
                ?? throw new InvalidOperationException($"P4 Aim additive base is missing: {stateName}");
            if (branch.GetNode(deltaName) is not AnimationNodeSub2)
            {
                throw new InvalidOperationException($"P4 Aim delta Sub2 is missing: {stateName}");
            }
            if (!library.ClipNames.TryGetValue(animationId, out var clipName) ||
                !library.ClipNames.TryGetValue(profile.Aim.AdditiveBasePoseAnimationId, out var baseClipName) ||
                !string.Equals(clip.Animation.ToString(), $"als/{clipName}", StringComparison.Ordinal) ||
                !string.Equals(baseClip.Animation.ToString(), $"als/{baseClipName}", StringComparison.Ordinal) ||
                !clip.UseCustomTimeline || clip.TimelineLength != 1.0 || !clip.StretchTimeScale ||
                !baseClip.UseCustomTimeline || baseClip.TimelineLength != 1.0 || !baseClip.StretchTimeScale ||
                clip.LoopMode != Godot.Animation.LoopModeEnum.None ||
                baseClip.LoopMode != Godot.Animation.LoopModeEnum.None)
            {
                throw new InvalidOperationException($"P4 Aim clip is not exactly bound: {stateName}");
            }
        }

        void VerifyCurve(int animationId, int curveId)
        {
            var animation = definition.Animations[animationId];
            var sampler = new AlsCurveSampler(animation.Curves);
            if (!sampler.TrySample(curveId, animation.PlayLength * 0.5f, out var value) ||
                !float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"P4 profile curve ID is not exactly sampleable: animation={animationId} curve={curveId}");
            }
        }
    }

    private static void VerifyControllerTransaction(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsPoseAnimationProfile poseProfile,
        in AlsFrameResult result)
    {
        var before = controller.ManualAdvanceCount;
        var invalid = AlsP4AnimationInput.Turn(999999, 1f, 0.5f, 0f, 0f, 1f, 0f);
        try
        {
            controller.Apply(in result, in invalid, DeltaTime);
            throw new InvalidOperationException("Unknown P4 animation ID was accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        if (controller.ManualAdvanceCount != before)
        {
            throw new InvalidOperationException("Invalid P4 input advanced the tree.");
        }

        var mutuallyActive = invalid with
        {
            ActiveTurnAnimationId = 0,
            ActiveRotateAnimationId = 0,
        };
        try
        {
            controller.Apply(in result, in mutuallyActive, DeltaTime);
            throw new InvalidOperationException("Mutually active Turn/Rotate was accepted.");
        }
        catch (ArgumentException)
        {
        }
        if (controller.ManualAdvanceCount != before)
        {
            throw new InvalidOperationException("Mutually active P4 input advanced the tree.");
        }

        var previousTurnId = controller.ActiveTurnAnimationId;
        var previousRotateId = controller.ActiveRotateAnimationId;
        var p4Handles = graph.Handles.P4!;
        var previousRotateStateA = graph.Tree.Get(
            p4Handles.RotateBankACurrentStatePath).AsString();
        var previousRotateStateB = graph.Tree.Get(
            p4Handles.RotateBankBCurrentStatePath).AsString();
        var previousAimWeight = GetTreeValue(p4Handles.AimForwardWeightPath);
        p4Handles.TryGetTurnBinding(poseProfile.Turns[0].AnimationId, out var turnBinding);
        p4Handles.TryGetRotateBinding(poseProfile.Rotates[0].AnimationId, out var rotateBinding);
        var malformedInputs = new[]
        {
            AlsP4AnimationInput.Disabled with { TurnPlayRate = float.NaN },
            AlsP4AnimationInput.Disabled with { RotatePhase = -0.01f },
            AlsP4AnimationInput.Disabled with { AimDownPhase = float.PositiveInfinity },
            AlsP4AnimationInput.Disabled with { AimForwardWeight = 1.01f },
            AlsP4AnimationInput.Disabled with { ActiveTurnAnimationId = -2 },
            AlsP4AnimationInput.Turn(
                poseProfile.Turns[0].AnimationId,
                1f,
                MathF.BitIncrement(turnBinding.DurationSeconds),
                0f,
                0f,
                0f,
                0f),
            AlsP4AnimationInput.Rotate(
                poseProfile.Rotates[0].AnimationId,
                1f,
                rotateBinding.DurationSeconds,
                0f,
                0f,
                0f,
                0f),
        };
        foreach (var malformed in malformedInputs)
        {
            try
            {
                controller.Apply(in result, in malformed, DeltaTime);
                throw new InvalidOperationException("Malformed P4 numeric input was accepted.");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            if (controller.ManualAdvanceCount != before ||
                controller.ActiveTurnAnimationId != previousTurnId ||
                controller.ActiveRotateAnimationId != previousRotateId ||
                !string.Equals(
                    graph.Tree.Get(p4Handles.RotateBankACurrentStatePath).AsString(),
                    previousRotateStateA,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    graph.Tree.Get(p4Handles.RotateBankBCurrentStatePath).AsString(),
                    previousRotateStateB,
                    StringComparison.Ordinal) ||
                GetTreeValue(p4Handles.AimForwardWeightPath) != previousAimWeight)
            {
                throw new InvalidOperationException("Malformed P4 input partially changed controller state.");
            }
        }

        float GetTreeValue(StringName path) => graph.Tree.Get(path).AsSingle();
    }

    private static AlsFrameResult ValidResult() => new()
    {
        ResolvedLocomotionState = AlsLocomotionState.Grounded,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ActualGait = AlsGait.Running,
        ActualStance = AlsStance.Standing,
        ActualRotationMode = AlsRotationMode.LookingDirection,
        AnimationState = AlsAnimationState.Grounded,
        BlendCoordinates = System.Numerics.Vector2.UnitY,
        Stride = 1f,
        PlayRate = 1f,
        Lean = System.Numerics.Vector2.Zero,
    };

    private static void Append(ref ulong digest, ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= DigestPrime;
        }
    }
}
