using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using System.Text;

namespace GodotAls.Animation;

public partial class P3bAnimationGraphSmoke : Node
{
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const double DeltaTime = 1.0 / 30.0;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    public override void _Ready()
    {
        try
        {
            var digest = RunSmoke();
            GD.Print($"GODOT_ALS_P3B_GRAPH_OK transitions=5 digest={digest:X16}");
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
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var profile = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(ProfilePath)), definition);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));

        VerifyWarmupFailureRetries(definition, profile, settings);
        VerifyProfileValidation(definition, profile);
        VerifyApplyAtomicValidation(definition, profile, settings);

        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, library.Skeleton, settings);
        controller.Warmup();
        VerifyQuaternionCanonicalization(controller, library.Skeleton);
        VerifyGaitBlendMapping(controller, graph, settings);
        VerifyActionNaturalAdvance(controller, graph, library.Skeleton, settings);

        var segments = CreateSegments();
        var advanceCountBeforeSegments = controller.ManualAdvanceCount;
        var topPlayback = graph.Tree.Get(
            graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph top playback is unavailable.");
        var groundedPlayback = graph.Tree.Get(
            graph.Handles.GroundedPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph Grounded playback is unavailable.");
        var previousState = AlsAnimationState.Grounded;
        var previousStance = AlsStance.Standing;
        var hasPreviousActualState = false;
        var transitions = 0;
        long frameId = 0;
        foreach (var segment in segments)
        {
            var initial = AlsPoseDigest.CapturePoses(library.Skeleton, PoseBoneNames);
            for (var frame = 0; frame < segment.FrameCount; frame++)
            {
                var result = segment.Result;
                result.Identity = new AlsFrameIdentity(++frameId, 0, 1);
                result.AnimationPhase = (float)((frame + 1.0) / segment.FrameCount);
                controller.Apply(in result, DeltaTime);
            }

            var actualState = AssertActualPlaybackState(
                graph,
                topPlayback,
                groundedPlayback,
                segment.ExpectedState,
                segment.ExpectedStance,
                segment.Name);

            var current = AlsPoseDigest.CapturePoses(library.Skeleton, PoseBoneNames);
            for (var boneIndex = 0; boneIndex < PoseBoneNames.Length; boneIndex++)
            {
                if (!AlsPoseDigest.HasChanged(
                        new[] { initial[boneIndex] },
                        new[] { current[boneIndex] }))
                {
                    throw new InvalidOperationException(
                        $"P3 graph segment did not change bone pose: " +
                        $"segment={segment.Name} bone={PoseBoneNames[boneIndex]}");
                }
            }

            if (hasPreviousActualState &&
                (actualState.State != previousState ||
                 (actualState.State == AlsAnimationState.Grounded &&
                  actualState.Stance != previousStance)))
            {
                transitions++;
            }
            previousState = actualState.State;
            previousStance = actualState.Stance;
            hasPreviousActualState = true;
        }

        if (transitions != 5)
        {
            throw new InvalidOperationException(
                $"P3 graph transition count mismatch: expected=5 actual={transitions}");
        }
        if (controller.ManualAdvanceCount - advanceCountBeforeSegments != frameId)
        {
            throw new InvalidOperationException(
                $"P3 graph must advance exactly once per Apply: " +
                $"applies={frameId} " +
                $"advances={controller.ManualAdvanceCount - advanceCountBeforeSegments}");
        }

        VerifyLandingRecoveryBoundary(controller, graph, settings);
        var digest = controller.ComputePoseDigest(frameId);
        VerifyLifecycle(definition, profile, settings);
        return digest;
    }

    private void VerifyWarmupFailureRetries(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, library.Skeleton, settings);
        graph.Dispose();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                controller.Warmup();
                throw new InvalidOperationException(
                    $"P3 Warmup attempt {attempt} treated an invalid graph as ready.");
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void VerifyProfileValidation(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile)
    {
        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        var failures = new List<string>();

        var malformedStandingRing = profile.StandingSamples.ToArray();
        malformedStandingRing[^1] = malformedStandingRing[^1] with { X = 0.6f, Y = 0.8f };
        ExpectInvalid("standing ring", profile with { StandingSamples = malformedStandingRing });

        var malformedCrouchingRing = profile.CrouchingSamples.ToArray();
        malformedCrouchingRing[0] = malformedCrouchingRing[0] with { X = 0f, Y = 0.5f };
        ExpectInvalid("crouching ring", profile with { CrouchingSamples = malformedCrouchingRing });

        var duplicateStanding = profile.StandingSamples.ToArray();
        duplicateStanding[1] = duplicateStanding[1] with
        {
            X = duplicateStanding[0].X,
            Y = duplicateStanding[0].Y,
        };
        ExpectInvalid("standing duplicate", profile with { StandingSamples = duplicateStanding });

        var duplicateCrouching = profile.CrouchingSamples.ToArray();
        duplicateCrouching[1] = duplicateCrouching[1] with
        {
            X = duplicateCrouching[0].X,
            Y = duplicateCrouching[0].Y,
        };
        ExpectInvalid("crouching duplicate", profile with { CrouchingSamples = duplicateCrouching });

        var duplicateLean = profile.LeanAdditiveSamples.ToArray();
        duplicateLean[1] = duplicateLean[1] with
        {
            X = duplicateLean[0].X,
            Y = duplicateLean[0].Y,
        };
        ExpectInvalid("lean duplicate", profile with { LeanAdditiveSamples = duplicateLean });

        var degenerateLean = profile.LeanAdditiveSamples
            .Select((sample, index) => sample with { X = 0f, Y = index })
            .ToArray();
        ExpectInvalid("lean degenerate grid", profile with { LeanAdditiveSamples = degenerateLean });

        var chainedRadii = profile.StandingSamples.ToArray();
        chainedRadii[0] = chainedRadii[0] with { X = 0f, Y = 0.50009f };
        chainedRadii[1] = chainedRadii[1] with { X = -0.5f, Y = 0f };
        chainedRadii[2] = chainedRadii[2] with { X = 0.50018f, Y = 0f };
        ExpectInvalid("standing chained radii", profile with { StandingSamples = chainedRadii });

        var shuffledProfile = profile with
        {
            StandingSamples = profile.StandingSamples.Reverse().ToArray(),
            CrouchingSamples = profile.CrouchingSamples.Reverse().ToArray(),
            LeanAdditiveSamples = profile.LeanAdditiveSamples.Reverse().ToArray(),
        };
        using (var baseline = AlsLocomotionGraphBuilder.Build(library, profile, definition))
        using (var shuffled = AlsLocomotionGraphBuilder.Build(library, shuffledProfile, definition))
        {
            AssertEquivalentLayout(baseline.Handles, shuffled.Handles, failures);
        }

        if (failures.Count != 0)
        {
            throw new InvalidOperationException(
                "P3 profile validation contract failed: " + string.Join("; ", failures));
        }

        void ExpectInvalid(string label, AlsLocomotionAnimationProfile invalidProfile)
        {
            try
            {
                using var unexpected = AlsLocomotionGraphBuilder.Build(
                    library, invalidProfile, definition);
                failures.Add($"{label} was accepted");
            }
            catch (InvalidOperationException)
            {
            }

            using var rebuilt = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        }
    }

    private static void AssertEquivalentLayout(
        AlsLocomotionGraphHandles baseline,
        AlsLocomotionGraphHandles shuffled,
        List<string> failures)
    {
        if (!baseline.StandingGaitRadii.SequenceEqual(shuffled.StandingGaitRadii) ||
            baseline.CrouchingRadius != shuffled.CrouchingRadius ||
            baseline.GroundedStanding.BlendMinimum != shuffled.GroundedStanding.BlendMinimum ||
            baseline.GroundedStanding.BlendMaximum != shuffled.GroundedStanding.BlendMaximum ||
            baseline.GroundedStanding.LeanMinimum != shuffled.GroundedStanding.LeanMinimum ||
            baseline.GroundedStanding.LeanMaximum != shuffled.GroundedStanding.LeanMaximum ||
            baseline.AnimationPlayerPath.ToString() != shuffled.AnimationPlayerPath.ToString() ||
            baseline.TopPlaybackPath.ToString() != shuffled.TopPlaybackPath.ToString() ||
            baseline.GroundedPlaybackPath.ToString() != shuffled.GroundedPlaybackPath.ToString())
        {
            failures.Add("shuffled samples changed radii, bounds, or handles");
        }
    }

    private void VerifyApplyAtomicValidation(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        var invalidGait = Result(
            AlsAnimationState.JumpStart,
            AlsStance.Standing,
            new System.Numerics.Vector2(0.2f, 0.8f));
        invalidGait.ActualGait = (AlsGait)byte.MaxValue;
        VerifyRejected("invalid gait", invalidGait);

        var excessivePlayRate = Result(
            AlsAnimationState.JumpStart,
            AlsStance.Standing,
            new System.Numerics.Vector2(0.2f, 0.8f));
        excessivePlayRate.PlayRate = settings.PlayRateMaximum + 0.25f;
        VerifyRejected("excessive play rate", excessivePlayRate);

        void VerifyRejected(string label, AlsFrameResult invalidResult)
        {
            using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
            using var controller = new AlsLocomotionAnimationController(
                graph, library.Skeleton, settings);
            controller.Warmup();
            var before = CaptureRuntimeSnapshot(controller, graph);
            Exception? rejection = null;
            try
            {
                controller.Apply(in invalidResult, settings.FixedDeltaSeconds);
            }
            catch (Exception exception)
            {
                rejection = exception;
            }

            if (rejection?.GetType() != typeof(ArgumentOutOfRangeException))
            {
                throw new InvalidOperationException(
                    $"P3 Apply {label} rejection type mismatch: " +
                    $"expected={nameof(ArgumentOutOfRangeException)} " +
                    $"actual={rejection?.GetType().Name ?? "none"}");
            }

            var after = CaptureRuntimeSnapshot(controller, graph);
            if (before != after)
            {
                throw new InvalidOperationException(
                    $"P3 Apply {label} mutated runtime before rejection: " +
                    $"before={before} after={after}");
            }
        }
    }

    private static void VerifyQuaternionCanonicalization(
        AlsLocomotionAnimationController controller,
        Skeleton3D skeleton)
    {
        var pelvis = skeleton.FindBone("pelvis");
        if (pelvis < 0)
        {
            throw new InvalidOperationException("P3 digest fixture is missing pelvis.");
        }

        var original = skeleton.GetBonePoseRotation(pelvis);
        try
        {
            var rotation = new Quaternion(0f, 0f, 1f, 0f);
            skeleton.SetBonePoseRotation(pelvis, rotation);
            var positiveDigest = controller.ComputePoseDigest(777);
            skeleton.SetBonePoseRotation(
                pelvis,
                new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W));
            var negativeDigest = controller.ComputePoseDigest(777);
            if (positiveDigest != negativeDigest)
            {
                throw new InvalidOperationException(
                    $"P3 180-degree quaternion sign changed pose digest: " +
                    $"q={positiveDigest:X16} negativeQ={negativeDigest:X16}");
            }
        }
        finally
        {
            skeleton.SetBonePoseRotation(pelvis, original);
        }
    }

    private static GraphRuntimeSnapshot CaptureRuntimeSnapshot(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph)
    {
        var topPlayback = graph.Tree.Get(
            graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph top playback is unavailable.");
        var groundedPlayback = graph.Tree.Get(
            graph.Handles.GroundedPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph Grounded playback is unavailable.");
        using var topNode = topPlayback.GetCurrentNode();
        using var groundedNode = groundedPlayback.GetCurrentNode();
        var builder = new StringBuilder();
        Append(graph.Handles.GroundedStanding);
        Append(graph.Handles.GroundedCrouching);
        Append(graph.Handles.JumpStart);
        Append(graph.Handles.FallLoop);
        Append(graph.Handles.LandRecovery);
        return new GraphRuntimeSnapshot(
            topNode.ToString(),
            groundedNode.ToString(),
            string.Join(",", topPlayback.GetTravelPath().Select(name => name.ToString())),
            string.Join(",", groundedPlayback.GetTravelPath().Select(name => name.ToString())),
            controller.ActiveAnimationState,
            controller.ActiveStance,
            controller.ManualAdvanceCount,
            builder.ToString());

        void Append(AlsLocomotionGraphParameterSet parameters)
        {
            if (parameters.BlendPositionPath is not null)
            {
                builder.Append(graph.Tree.Get(parameters.BlendPositionPath).AsVector2()).Append('|');
            }
            builder.Append(graph.Tree.Get(parameters.LeanPositionPath).AsVector2()).Append('|');
            builder.Append(graph.Tree.Get(parameters.LeanAmountPath).AsSingle()).Append('|');
            builder.Append(graph.Tree.Get(parameters.PlayRatePath).AsSingle()).Append('|');
            if (parameters.PhasePath is not null)
            {
                builder.Append(graph.Tree.Get(parameters.PhasePath).AsSingle()).Append('|');
            }
        }
    }

    private static ActualPlaybackState AssertActualPlaybackState(
        AlsLocomotionGraphBuildResult graph,
        AnimationNodeStateMachinePlayback topPlayback,
        AnimationNodeStateMachinePlayback groundedPlayback,
        AlsAnimationState expectedState,
        AlsStance expectedStance,
        string label)
    {
        using var actualTop = topPlayback.GetCurrentNode();
        var expectedTop = graph.Handles.StateNames[(int)expectedState];
        var actualState = FindActualState(graph, actualTop);
        if (actualState != expectedState)
        {
            throw new InvalidOperationException(
                $"P3 actual top playback mismatch for {label}: " +
                $"expected={expectedTop} actual={actualTop}");
        }
        if (actualState != AlsAnimationState.Grounded)
        {
            return new ActualPlaybackState(actualState, expectedStance);
        }

        using var actualGrounded = groundedPlayback.GetCurrentNode();
        var expectedGrounded = graph.Handles.StanceNames[(int)expectedStance];
        var actualStance = FindActualStance(graph, actualGrounded);
        if (actualStance != expectedStance)
        {
            throw new InvalidOperationException(
                $"P3 actual Grounded playback mismatch for {label}: " +
                $"expected={expectedGrounded} actual={actualGrounded}");
        }
        return new ActualPlaybackState(actualState, actualStance);
    }

    private static AlsAnimationState FindActualState(
        AlsLocomotionGraphBuildResult graph,
        StringName actual)
    {
        for (var index = 0; index < graph.Handles.StateNames.Count; index++)
        {
            if (actual.Equals(graph.Handles.StateNames[index]))
            {
                return (AlsAnimationState)index;
            }
        }
        throw new InvalidOperationException($"P3 graph entered an unknown top state: {actual}");
    }

    private static AlsStance FindActualStance(
        AlsLocomotionGraphBuildResult graph,
        StringName actual)
    {
        for (var index = 0; index < graph.Handles.StanceNames.Count; index++)
        {
            if (actual.Equals(graph.Handles.StanceNames[index]))
            {
                return (AlsStance)index;
            }
        }
        throw new InvalidOperationException($"P3 graph entered an unknown Grounded stance: {actual}");
    }

    private static void VerifyLandingRecoveryBoundary(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings)
    {
        var topPlayback = graph.Tree.Get(
            graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph top playback is unavailable.");
        var groundedPlayback = graph.Tree.Get(
            graph.Handles.GroundedPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph Grounded playback is unavailable.");
        var recoveryFrames = checked((int)Math.Ceiling(
            settings.LandingRecoveryDuration / settings.FixedDeltaSeconds));
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.InAir,
            PreviousLocomotionState = AlsLocomotionState.Grounded,
            ActualGait = AlsGait.Running,
            AnimationPhase = 0.4f,
        };
        var result = new AlsFrameResult();

        for (var frame = 1; frame <= recoveryFrames + 1; frame++)
        {
            var input = CreateModelInput(
                300 + frame,
                System.Numerics.Vector3.Zero,
                AlsStance.Standing,
                true,
                settings.FixedDeltaSeconds);
            AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
            var expected = frame <= recoveryFrames
                ? AlsAnimationState.LandRecovery
                : AlsAnimationState.Grounded;
            if (result.ResolvedLocomotionState != AlsLocomotionState.Grounded ||
                result.AnimationState != expected)
            {
                throw new InvalidOperationException(
                    $"P3 landing recovery model boundary mismatch: frame={frame} " +
                    $"expected=Grounded/{expected} " +
                    $"actual={result.ResolvedLocomotionState}/{result.AnimationState} " +
                    $"remaining={state.LandingRecoveryTime:R}");
            }

            controller.Apply(in result, settings.FixedDeltaSeconds);
            AssertActualPlaybackState(
                graph,
                topPlayback,
                groundedPlayback,
                expected,
                AlsStance.Standing,
                $"landing frame {frame}");
            if (frame == recoveryFrames && state.LandingRecoveryTime != 0f)
            {
                throw new InvalidOperationException(
                    $"P3 landing recovery timer did not expire at its boundary: " +
                    $"frame={frame} remaining={state.LandingRecoveryTime:R}");
            }
        }
    }

    private static void VerifyGaitBlendMapping(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings)
    {
        var cases = new[]
        {
            new GaitBlendCase(0, 0f, 0f, AlsGait.Walking, Vector2.Zero),
            new GaitBlendCase(88, 0f, 0.875f, AlsGait.Walking, new Vector2(0f, 0.5f)),
            new GaitBlendCase(175, 0f, 1.75f, AlsGait.Walking, new Vector2(0f, 0.5f)),
            new GaitBlendCase(375, 0f, 3.75f, AlsGait.Running, new Vector2(0f, 1f)),
            new GaitBlendCase(650, 0f, 6.5f, AlsGait.Sprinting, new Vector2(0f, 1.5f)),
            new GaitBlendCase(
                175,
                1.2374369f,
                1.2374369f,
                AlsGait.Walking,
                new Vector2(0.353553f, 0.353553f)),
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            var input = CreateModelInput(
                index + 1,
                new System.Numerics.Vector3(item.RightSpeed, 0f, -item.ForwardSpeed),
                AlsStance.Standing,
                true,
                settings.FixedDeltaSeconds);
            AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
            if (result.ActualGait != item.ExpectedGait)
            {
                throw new InvalidOperationException(
                    $"P3 model gait fixture mismatch at {item.SpeedCentimeters} cm/s: " +
                    $"expected={item.ExpectedGait} actual={result.ActualGait}");
            }

            controller.Apply(in result, settings.FixedDeltaSeconds);
            var actual = graph.Tree.Get(
                graph.Handles.GroundedStanding.BlendPositionPath!).AsVector2();
            if (!actual.IsEqualApprox(item.ExpectedBlendPosition))
            {
                throw new InvalidOperationException(
                    $"P3 gait ring mapping mismatch at {item.SpeedCentimeters} cm/s: " +
                    $"expected={item.ExpectedBlendPosition} actual={actual}");
            }
            var actualTimeScale = graph.Tree.Get(
                graph.Handles.GroundedStanding.PlayRatePath).AsSingle();
            var expectedTimeScale = result.PlayRate * result.Stride;
            if (!Mathf.IsEqualApprox(actualTimeScale, expectedTimeScale))
            {
                throw new InvalidOperationException(
                    $"P3 grounded stride time-scale mismatch at {item.SpeedCentimeters} cm/s: " +
                    $"expected={expectedTimeScale:R} actual={actualTimeScale:R}");
            }
        }

        var crouchingState = new AlsRuntimeState();
        var crouchingResult = new AlsFrameResult();
        var crouchingInput = CreateModelInput(
            cases.Length + 1,
            new System.Numerics.Vector3(0f, 0f, -1f),
            AlsStance.Crouching,
            true,
            settings.FixedDeltaSeconds);
        AlsLocomotionModel.Evaluate(
            crouchingInput,
            ref crouchingState,
            ref crouchingResult,
            settings);
        controller.Apply(in crouchingResult, settings.FixedDeltaSeconds);
        var actualCrouchingBlend = graph.Tree.Get(
            graph.Handles.GroundedCrouching.BlendPositionPath!).AsVector2();
        var expectedCrouchingBlend = new Vector2(0f, 1f);
        if (!actualCrouchingBlend.IsEqualApprox(expectedCrouchingBlend))
        {
            throw new InvalidOperationException(
                $"P3 crouching outer-ring mapping mismatch: " +
                $"expected={expectedCrouchingBlend} actual={actualCrouchingBlend}");
        }
    }

    private static void VerifyActionNaturalAdvance(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        Skeleton3D skeleton,
        AlsLocomotionSettings settings)
    {
        var topPlayback = graph.Tree.Get(
            graph.Handles.TopPlaybackPath).As<AnimationNodeStateMachinePlayback>()
            ?? throw new InvalidOperationException("P3 graph top playback is unavailable.");
        var state = new AlsRuntimeState
        {
            Initialized = 1,
            LocomotionState = AlsLocomotionState.Grounded,
            PreviousLocomotionState = AlsLocomotionState.Grounded,
            ActualGait = AlsGait.Running,
            AnimationPhase = 0.375f,
        };
        var result = new AlsFrameResult();
        long frameId = 100;

        VerifyAction(
            AlsAnimationState.JumpStart,
            iteration => CreateModelInput(
                ++frameId,
                new System.Numerics.Vector3(0f, iteration == 0 ? 4.2f : 3f, -2f),
                AlsStance.Standing,
                false,
                settings.FixedDeltaSeconds,
                iteration == 0 ? (byte)1 : (byte)0));
        VerifyAction(
            AlsAnimationState.FallLoop,
            _ => CreateModelInput(
                ++frameId,
                new System.Numerics.Vector3(0f, -1f, -2f),
                AlsStance.Standing,
                false,
                settings.FixedDeltaSeconds));
        VerifyAction(
            AlsAnimationState.LandRecovery,
            _ => CreateModelInput(
                ++frameId,
                System.Numerics.Vector3.Zero,
                AlsStance.Standing,
                true,
                settings.FixedDeltaSeconds));

        void VerifyAction(
            AlsAnimationState expectedState,
            Func<int, AlsFrameInput> inputFactory)
        {
            var firstPosition = 0f;
            var firstPose = Array.Empty<AlsBonePose>();
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var input = inputFactory(iteration);
                AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
                if (result.AnimationState != expectedState ||
                    !Mathf.IsEqualApprox(result.AnimationPhase, 0.375f))
                {
                    throw new InvalidOperationException(
                        $"P3 constant-phase action fixture mismatch: " +
                        $"expected={expectedState}/0.375 " +
                        $"actual={result.AnimationState}/{result.AnimationPhase:R}");
                }
                controller.Apply(in result, settings.FixedDeltaSeconds);

                if (iteration == 6)
                {
                    firstPosition = topPlayback.GetCurrentPlayPosition();
                    firstPose = AlsPoseDigest.CapturePoses(skeleton, PoseBoneNames);
                }
                else if (iteration == 7)
                {
                    var secondPosition = topPlayback.GetCurrentPlayPosition();
                    var secondPose = AlsPoseDigest.CapturePoses(skeleton, PoseBoneNames);
                    if (secondPosition <= firstPosition + (settings.FixedDeltaSeconds * 0.5f) ||
                        !AlsPoseDigest.HasChanged(firstPose, secondPose))
                    {
                        throw new InvalidOperationException(
                            $"P3 action playback froze on constant P3A phase: state={expectedState} " +
                            $"first={firstPosition:R} second={secondPosition:R}");
                    }
                }
            }
        }
    }

    private static AlsFrameInput CreateModelInput(
        long frameId,
        System.Numerics.Vector3 velocity,
        AlsStance stance,
        bool grounded,
        float deltaTime,
        byte jumpAccepted = 0)
    {
        var command = new AlsLocomotionCommand(
            System.Numerics.Vector2.UnitY,
            0f,
            0f,
            AlsGait.Sprinting,
            stance,
            AlsRotationMode.LookingDirection,
            jumpAccepted);
        return AlsFrameInput.CreateDefault(new AlsFrameIdentity(frameId, 0, 1), deltaTime) with
        {
            ActualVelocity = velocity,
            InputDirection = velocity.LengthSquared() > 0f
                ? System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(velocity.X, 0f, velocity.Z))
                : System.Numerics.Vector3.Zero,
            DesiredSpeed = MathF.Sqrt((velocity.X * velocity.X) + (velocity.Z * velocity.Z)),
            Floor = new AlsFloorSample(
                grounded ? (byte)1 : (byte)0,
                System.Numerics.Vector3.UnitY,
                -1,
                System.Numerics.Matrix4x4.Identity,
                System.Numerics.Vector3.Zero),
            RequestedGait = AlsGait.Sprinting,
            Stance = stance,
            Command = command,
            MaxAcceleration = 20f,
            MaxBrakingDeceleration = 15f,
            JumpAccepted = jumpAccepted,
        };
    }

    private void VerifyLifecycle(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        var independentLibrary = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(independentLibrary.Root);
        var independentGraph = AlsLocomotionGraphBuilder.Build(
            independentLibrary, profile, definition);
        independentGraph.Dispose();
        independentGraph.Dispose();
        if (!GodotObject.IsInstanceValid(independentLibrary.Root) ||
            independentLibrary.Player.GetAnimationList().Length != profile.AllAnimationIds.Length)
        {
            throw new InvalidOperationException(
                "P3 graph disposal incorrectly released the borrowed Task 2 animation library.");
        }
        independentLibrary.Dispose();

        var parent = new Node { Name = "P3bGraphParentFreedOwner" };
        AddChild(parent);
        var parentFreedLibrary = AlsAnimationLibraryBuilder.Build(definition, profile);
        parent.AddChild(parentFreedLibrary.Root);
        var parentFreedGraph = AlsLocomotionGraphBuilder.Build(
            parentFreedLibrary, profile, definition);
        parent.Free();
        parentFreedGraph.Dispose();
        parentFreedGraph.Dispose();
        parentFreedLibrary.Dispose();
        parentFreedLibrary.Dispose();

        var invalidSamples = profile.StandingSamples.ToArray();
        invalidSamples[0] = invalidSamples[0] with
        {
            AnimationId = definition.Animations.Length,
        };
        var invalidProfile = profile with { StandingSamples = invalidSamples };
        var partialLibrary = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(partialLibrary.Root);
        try
        {
            using var unexpected = AlsLocomotionGraphBuilder.Build(
                partialLibrary, invalidProfile, definition);
            throw new InvalidOperationException("P3 partial graph fixture unexpectedly succeeded.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("no clip name", StringComparison.Ordinal))
        {
        }

        using (var rebuiltGraph = AlsLocomotionGraphBuilder.Build(
                   partialLibrary, profile, definition))
        using (var rebuiltController = new AlsLocomotionAnimationController(
                   rebuiltGraph, partialLibrary.Skeleton, settings))
        {
            rebuiltController.Warmup();
        }
        partialLibrary.Dispose();
        partialLibrary.Dispose();

        GD.Print(
            "GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK double_dispose=1 parent_free=1 " +
            "partial=1 rebuild=1 borrowed=1");
    }

    private static Segment[] CreateSegments() =>
    [
        new(
            "Grounded/Standing",
            Result(AlsAnimationState.Grounded, AlsStance.Standing, new System.Numerics.Vector2(0.35f, 0.9f)),
            AlsAnimationState.Grounded,
            AlsStance.Standing,
            12),
        new(
            "Grounded/Crouching",
            Result(AlsAnimationState.Grounded, AlsStance.Crouching, new System.Numerics.Vector2(-0.85f, 0.1f)),
            AlsAnimationState.Grounded,
            AlsStance.Crouching,
            12),
        new(
            "JumpStart",
            Result(AlsAnimationState.JumpStart, AlsStance.Standing, System.Numerics.Vector2.Zero),
            AlsAnimationState.JumpStart,
            AlsStance.Standing,
            12),
        new(
            "FallLoop",
            Result(AlsAnimationState.FallLoop, AlsStance.Standing, System.Numerics.Vector2.Zero),
            AlsAnimationState.FallLoop,
            AlsStance.Standing,
            12),
        new(
            "LandRecovery",
            Result(AlsAnimationState.LandRecovery, AlsStance.Standing, System.Numerics.Vector2.Zero),
            AlsAnimationState.LandRecovery,
            AlsStance.Standing,
            12),
        new(
            "Grounded/Standing Return",
            Result(AlsAnimationState.Grounded, AlsStance.Standing, new System.Numerics.Vector2(-0.4f, -0.85f)),
            AlsAnimationState.Grounded,
            AlsStance.Standing,
            12),
    ];

    private static AlsFrameResult Result(
        AlsAnimationState animationState,
        AlsStance stance,
        System.Numerics.Vector2 blendCoordinates) => new()
    {
        ResolvedLocomotionState = animationState is AlsAnimationState.JumpStart or AlsAnimationState.FallLoop
            ? AlsLocomotionState.InAir
            : AlsLocomotionState.Grounded,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ActualGait = AlsGait.Running,
        ActualStance = stance,
        ActualRotationMode = AlsRotationMode.LookingDirection,
        AnimationState = animationState,
        BlendCoordinates = blendCoordinates,
        Stride = 1f,
        PlayRate = 1f,
        Lean = new System.Numerics.Vector2(0.15f, -0.1f),
    };

    private readonly record struct Segment(
        string Name,
        AlsFrameResult Result,
        AlsAnimationState ExpectedState,
        AlsStance ExpectedStance,
        int FrameCount);

    private readonly record struct GaitBlendCase(
        int SpeedCentimeters,
        float RightSpeed,
        float ForwardSpeed,
        AlsGait ExpectedGait,
        Vector2 ExpectedBlendPosition);

    private readonly record struct ActualPlaybackState(
        AlsAnimationState State,
        AlsStance Stance);

    private readonly record struct GraphRuntimeSnapshot(
        string TopNode,
        string GroundedNode,
        string TopTravelPath,
        string GroundedTravelPath,
        AlsAnimationState ControllerState,
        AlsStance ControllerStance,
        long AdvanceCount,
        string Parameters);
}
