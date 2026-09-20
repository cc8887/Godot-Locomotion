using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;
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

    private static readonly DirectionCase[] DirectionCases =
    [
        new("forward", System.Numerics.Vector2.UnitY, new Vector2(0f, 1f)),
        new("left", -System.Numerics.Vector2.UnitX, new Vector2(-0.707107f, 0.707107f)),
        new("back", -System.Numerics.Vector2.UnitY, new Vector2(0f, -1f)),
        new("right", System.Numerics.Vector2.UnitX, new Vector2(0.707107f, 0.707107f)),
    ];

    public override void _Ready()
    {
        try
        {
            var (directionDigest, digest) = RunSmoke();
            GD.Print(
                $"GODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 " +
                $"direction_digest={directionDigest:X16} digest={digest:X16}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private (ulong DirectionDigest, ulong Digest) RunSmoke()
    {
        VerifyFiniteBlendGuard();
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var profile = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(ProfilePath)), definition);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));
        VerifyNeutralLean(definition, profile, settings);
        VerifyHipDirection(definition, profile, settings);

        VerifyWarmupFailureRetries(definition, profile, settings);
        VerifyProfileValidation(definition, profile);
        VerifyGraphTargetSkeletonBinding(definition, profile, settings);
        VerifyApplyAtomicValidation(definition, profile, settings);
        var directionDigest = VerifyDirectionMatrix(definition, profile, settings);

        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings);
        controller.Warmup();
        VerifyQuaternionCanonicalization(controller, graph.TargetSkeleton);
        VerifyGaitBlendMapping(controller, graph, settings);
        VerifyActionNaturalAdvance(controller, graph, graph.TargetSkeleton, settings);

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
            var initial = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
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

            var current = AlsPoseDigest.CapturePoses(graph.TargetSkeleton, PoseBoneNames);
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
        return (directionDigest, digest);
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
            graph, settings);
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

        var collinearLean = profile.LeanAdditiveSamples
            .Select((sample, index) => sample with { X = index, Y = index })
            .ToArray();
        ExpectInvalid("lean collinear grid", profile with { LeanAdditiveSamples = collinearLean });

        var nearlyCollinearLean = profile.LeanAdditiveSamples
            .Select((sample, index) => sample with
            {
                X = index,
                Y = index + ((index & 1) == 0 ? 1e-6f : -1e-6f),
            })
            .ToArray();
        ExpectInvalid(
            "lean nearly-collinear grid",
            profile with { LeanAdditiveSamples = nearlyCollinearLean });

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
                graph, settings);
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

    private void VerifyGraphTargetSkeletonBinding(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        using var libraryA = AlsAnimationLibraryBuilder.Build(definition, profile);
        using var libraryB = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(libraryA.Root);
        AddChild(libraryB.Root);
        using var graphA = AlsLocomotionGraphBuilder.Build(libraryA, profile, definition);
        using var graphB = AlsLocomotionGraphBuilder.Build(libraryB, profile, definition);

        if (!ReferenceEquals(graphA.TargetSkeleton, libraryA.Skeleton) ||
            ReferenceEquals(graphA.TargetSkeleton, libraryB.Skeleton) ||
            !ReferenceEquals(graphB.TargetSkeleton, libraryB.Skeleton) ||
            typeof(AlsLocomotionAnimationController)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == typeof(Skeleton3D)))
        {
            throw new InvalidOperationException(
                "P3 graph/controller API does not enforce its unique target skeleton.");
        }

        using var controller = new AlsLocomotionAnimationController(graphA, settings);
        controller.Warmup();
        var pelvisA = graphA.TargetSkeleton.FindBone("pelvis");
        var pelvisB = graphB.TargetSkeleton.FindBone("pelvis");
        if (pelvisA < 0 || pelvisB < 0)
        {
            throw new InvalidOperationException("P3 target skeleton fixture is missing pelvis.");
        }

        var originalA = graphA.TargetSkeleton.GetBonePoseRotation(pelvisA);
        var originalB = graphB.TargetSkeleton.GetBonePoseRotation(pelvisB);
        try
        {
            var changedRotation = new Quaternion(0f, 0f, 1f, 0f);
            if (MathF.Abs(originalA.Normalized().Dot(changedRotation)) > 0.99f)
            {
                changedRotation = new Quaternion(0.70710677f, 0f, 0f, 0.70710677f);
            }

            var baselineDigest = controller.ComputePoseDigest(901);
            graphB.TargetSkeleton.SetBonePoseRotation(pelvisB, changedRotation);
            var foreignDigest = controller.ComputePoseDigest(901);
            graphA.TargetSkeleton.SetBonePoseRotation(pelvisA, changedRotation);
            var targetDigest = controller.ComputePoseDigest(901);
            if (foreignDigest != baselineDigest || targetDigest == baselineDigest)
            {
                throw new InvalidOperationException(
                    $"P3 controller digest did not bind exclusively to graph target: " +
                    $"baseline={baselineDigest:X16} foreign={foreignDigest:X16} " +
                    $"target={targetDigest:X16}");
            }
        }
        finally
        {
            graphA.TargetSkeleton.SetBonePoseRotation(pelvisA, originalA);
            graphB.TargetSkeleton.SetBonePoseRotation(pelvisB, originalB);
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

    private void VerifyNeutralLean(AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile, AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var input = CreateModelInput(1, new System.Numerics.Vector3(1.75f, 0f, 0f),
            AlsStance.Standing, true, settings.FixedDeltaSeconds);
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();
        AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
        result.AnimationPhase = 0.25f;
        result.Lean = System.Numerics.Vector2.Zero;
        controller.Apply(in result, 0.0);
        var ids = new[] { "pelvis", "spine_03", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r" }
            .Select(graph.TargetSkeleton.FindBone).ToArray();
        var rotations = ids.Select(graph.TargetSkeleton.GetBonePoseRotation).ToArray();
        result.Identity = new AlsFrameIdentity(2, 0, 1);
        result.Lean = new System.Numerics.Vector2(0.0001f, 0f);
        controller.Apply(in result, 0.0);
        for (var index = 0; index < ids.Length; index++)
        {
            var after = graph.TargetSkeleton.GetBonePoseRotation(ids[index]);
            if (1f - MathF.Abs(rotations[index].Normalized().Dot(after.Normalized())) > 1e-5f)
                throw new InvalidOperationException($"Near-zero Lean changes the base pose: {graph.TargetSkeleton.GetBoneName(ids[index])} before={rotations[index]} after={after}");
        }
        GD.Print("P3_LEAN_NEUTRAL_OK bones=6");
    }

    private void VerifyHipDirection(AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile, AlsLocomotionSettings settings)
    {
        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var result = new AlsFrameResult
        {
            AnimationState = AlsAnimationState.Grounded,
            ResolvedLocomotionState = AlsLocomotionState.Grounded,
            ActualStance = AlsStance.Standing, ActualGait = AlsGait.Walking,
            Stride = 1f, PlayRate = 1f, AnimationPhase = 0.25f,
        };
        Apply(-1f, 0f, 0.0);
        var leftHips = Yaw("thigh_l", "thigh_r");
        var leftShoulders = Yaw("upperarm_l", "upperarm_r");
        Apply(1f, 0f, 0.0);
        var hipSeparation = Mathf.Abs(Mathf.Wrap(Yaw("thigh_l", "thigh_r") - leftHips, -Mathf.Pi, Mathf.Pi));
        var shoulderSeparation = Mathf.Abs(Mathf.Wrap(Yaw("upperarm_l", "upperarm_r") - leftShoulders, -Mathf.Pi, Mathf.Pi));
        if (hipSeparation < Mathf.DegToRad(40f) || shoulderSeparation < Mathf.DegToRad(20f))
            throw new InvalidOperationException($"Lateral cycles lost the authored hip/shoulder direction change: hips={Mathf.RadToDeg(hipSeparation)} shoulders={Mathf.RadToDeg(shoulderSeparation)}.");

        var original = ReadStandingBlend(graph);
        result.BlendCoordinates = new System.Numerics.Vector2(0f, -1f);
        var p4 = AlsP4AnimationInput.Disabled;
        var discarded = controller.PrepareFrame(in result, in p4, 0.25);
        controller.DiscardPrepared(in discarded);
        Apply(1f, 0f, 1.0 / 60.0);
        if (!ReadStandingBlend(graph).IsEqualApprox(original))
            throw new InvalidOperationException("Discarded frame changed hip selection.");
        result.BlendCoordinates = new System.Numerics.Vector2(0f, -1f);
        var rolledBack = controller.PrepareFrame(in result, in p4, 0.25);
        controller.ApplyPrepared(in rolledBack);
        controller.RollbackPrepared(in rolledBack);
        Apply(1f, 0f, 1.0 / 60.0);
        if (!ReadStandingBlend(graph).IsEqualApprox(original))
            throw new InvalidOperationException("Rolled-back frame changed hip selection.");

        Apply(0f, -1f, 0.25);
        Apply(1f, 0f, 0.25);
        if (ReadStandingBlend(graph).Y > -0.35f)
            throw new InvalidOperationException("Backward movement did not retain the rear hip variant when strafing.");
        for (var frame = 0; frame < 60; frame++) Apply(1f, (frame % 2 == 0 ? 0.1f : -0.1f), 1.0 / 60.0);
        if (ReadStandingBlend(graph).Y >= 0f)
            throw new InvalidOperationException("Lateral input noise toggled the hip hemisphere.");
        Apply(0f, 1f, 0.25);
        Apply(1f, 0f, 0.25);
        if (!ReadStandingBlend(graph).IsEqualApprox(original))
            throw new InvalidOperationException("Forward movement did not restore the front hip variant.");
        for (var frame = 0; frame < 30; frame++)
        {
            var previousAngle = ReadStandingBlend(graph).Angle();
            Apply(-1f, 0f, 1.0 / 60.0);
            var step = Mathf.Abs(Mathf.Wrap(ReadStandingBlend(graph).Angle() - previousAngle, -Mathf.Pi, Mathf.Pi));
            if (step > 10f / 60f + 1e-4f)
                throw new InvalidOperationException("Lateral reversal exceeded the pose direction step limit.");
        }
        if (!ReadStandingBlend(graph).IsEqualApprox(new Vector2(-original.X, original.Y)))
            throw new InvalidOperationException("Lateral reversal did not converge to the opposite hip pose.");
        GD.Print($"P3_HIP_DIRECTION_OK hips_degrees={Mathf.RadToDeg(hipSeparation):F2} shoulders_degrees={Mathf.RadToDeg(shoulderSeparation):F2} hysteresis=60 discard=1 rollback=1");

        void Apply(float right, float forward, double delta)
        {
            result.BlendCoordinates = new System.Numerics.Vector2(right, forward);
            controller.Apply(in result, delta);
        }
        float Yaw(string left, string right)
        {
            var skeleton = graph.TargetSkeleton;
            var across = skeleton.GlobalTransform.Basis *
                (skeleton.GetBoneGlobalPose(skeleton.FindBone(right)).Origin -
                skeleton.GetBoneGlobalPose(skeleton.FindBone(left)).Origin);
            return Mathf.Atan2(across.Z, across.X);
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
            new GaitBlendCase(88, 0f, 0.875f, AlsGait.Walking, new Vector2(0f, 0.25f)),
            new GaitBlendCase(88, 0.875f, 0f, AlsGait.Walking, new Vector2(0.1767765f, 0.1767765f)),
            new GaitBlendCase(88, -0.875f, 0f, AlsGait.Walking, new Vector2(-0.1767765f, 0.1767765f)),
            new GaitBlendCase(1, 0.0175f, 0f, AlsGait.Walking, new Vector2(0.00353553f, 0.00353553f)),
            new GaitBlendCase(1, -0.0175f, 0f, AlsGait.Walking, new Vector2(-0.00353553f, 0.00353553f)),
            new GaitBlendCase(175, 0f, 1.75f, AlsGait.Walking, new Vector2(0f, 0.5f)),
            new GaitBlendCase(375, 0f, 3.75f, AlsGait.Running, new Vector2(0f, 1f)),
            new GaitBlendCase(650, 0f, 6.5f, AlsGait.Sprinting, new Vector2(0f, 1.5f)),
            new GaitBlendCase(175, 1.75f, 0f, AlsGait.Walking, new Vector2(0.353553f, 0.353553f)),
            new GaitBlendCase(175, -1.75f, 0f, AlsGait.Walking, new Vector2(-0.353553f, 0.353553f)),
            new GaitBlendCase(375, 3.75f, 0f, AlsGait.Running, new Vector2(0.707107f, 0.707107f)),
            new GaitBlendCase(375, -3.75f, 0f, AlsGait.Running, new Vector2(-0.707107f, 0.707107f)),
            new GaitBlendCase(
                175,
                1.2374369f,
                1.2374369f,
                AlsGait.Walking,
                new Vector2(0.2071067f, 0.4142134f)),
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

            controller.Apply(in result, 0.5);
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
        var expectedCrouchingBlend = new Vector2(0f, 2f / 3f);
        if (!actualCrouchingBlend.IsEqualApprox(expectedCrouchingBlend))
        {
            throw new InvalidOperationException(
                    $"P3 crouching stride mapping mismatch: " +
                $"expected={expectedCrouchingBlend} actual={actualCrouchingBlend}");
        }
    }

    private ulong VerifyDirectionMatrix(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        long frameId = 0;
        using (var library = AlsAnimationLibraryBuilder.Build(definition, profile))
        {
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
            using var controller = new AlsLocomotionAnimationController(graph, settings);
            controller.Warmup();
            VerifyLookingDirectionMatrix(controller, graph, settings, ref frameId);
            VerifyAimingDirectionMatrix(controller, graph, settings, ref frameId);
            VerifyVelocityDirectionMatrix(controller, graph, settings, ref frameId);
        }

        return VerifyDirectionalPoseEvidence(definition, profile, settings);
    }

    private static void VerifyLookingDirectionMatrix(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings,
        ref long frameId)
    {
        const float viewYaw = MathF.PI / 3f;
        foreach (var item in DirectionCases)
        {
            var command = CreateDirectionalCommand(
                item.MovementAxes,
                viewYaw,
                viewYaw,
                AlsRotationMode.LookingDirection);
            var worldDirection = RequireCameraRelativeDirection(command, item.Name);
            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            var input = CreateDirectionalInput(
                ++frameId,
                command,
                worldDirection * 3.75f,
                viewYaw,
                settings.FixedDeltaSeconds);
            AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
            RequireDirectionalResult(result, AlsRotationMode.LookingDirection, item.Name);
            controller.Apply(in result, settings.FixedDeltaSeconds);
            controller.Apply(in result, 0.25);
            RequireStandingBlend(graph, item.ExpectedBlendPosition, 1e-4f,
                $"LookingDirection {item.Name}");
        }
    }

    private static void VerifyAimingDirectionMatrix(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings,
        ref long frameId)
    {
        const float aimYaw = MathF.PI / 2f;
        foreach (var item in DirectionCases)
        {
            var command = CreateDirectionalCommand(
                item.MovementAxes,
                aimYaw,
                aimYaw,
                AlsRotationMode.Aiming);
            var worldDirection = RequireCameraRelativeDirection(command, item.Name);
            var characterYaw = -MathF.PI / 2f;
            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            var previousFrameConverged = false;
            var converged = false;
            for (var frame = 0; frame < 240; frame++)
            {
                var input = CreateDirectionalInput(
                    ++frameId,
                    command,
                    worldDirection * 3.75f,
                    characterYaw,
                    settings.FixedDeltaSeconds);
                AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
                RequireDirectionalResult(result, AlsRotationMode.Aiming, item.Name);
                controller.Apply(in result, settings.FixedDeltaSeconds);
                characterYaw = result.TargetYaw;
                var currentFrameConverged = NormalizedYawError(characterYaw, aimYaw) < 1e-4f;
                if (currentFrameConverged && previousFrameConverged)
                {
                    converged = true;
                    break;
                }
                previousFrameConverged = currentFrameConverged;
            }

            if (!converged || NormalizedYawError(result.TargetYaw, aimYaw) >= 1e-3f)
            {
                throw new InvalidOperationException(
                    $"P3 Aiming {item.Name} did not converge to aim yaw: " +
                    $"expected={aimYaw:R} actual={result.TargetYaw:R}");
            }
            controller.Apply(in result, 0.25);
            RequireStandingBlend(graph, item.ExpectedBlendPosition, 1e-3f,
                $"Aiming {item.Name}");
        }
    }

    private static void VerifyVelocityDirectionMatrix(
        AlsLocomotionAnimationController controller,
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings,
        ref long frameId)
    {
        foreach (var item in DirectionCases)
        {
            var command = CreateDirectionalCommand(
                item.MovementAxes,
                0f,
                0f,
                AlsRotationMode.VelocityDirection);
            var worldDirection = RequireCameraRelativeDirection(command, item.Name);
            var expectedYaw = MathF.Atan2(-worldDirection.X, -worldDirection.Z);
            var characterYaw = MathF.PI * 0.75f;
            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            var previousFrameConverged = false;
            var converged = false;
            for (var frame = 0; frame < 240; frame++)
            {
                var input = CreateDirectionalInput(
                    ++frameId,
                    command,
                    worldDirection * 3.75f,
                    characterYaw,
                    settings.FixedDeltaSeconds);
                AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
                RequireDirectionalResult(result, AlsRotationMode.VelocityDirection, item.Name);
                controller.Apply(in result, settings.FixedDeltaSeconds);
                characterYaw = result.TargetYaw;
                var currentFrameConverged = NormalizedYawError(characterYaw, expectedYaw) < 1e-3f;
                if (currentFrameConverged && previousFrameConverged)
                {
                    converged = true;
                    break;
                }
                previousFrameConverged = currentFrameConverged;
            }

            if (!converged || NormalizedYawError(result.TargetYaw, expectedYaw) >= 1e-3f)
            {
                throw new InvalidOperationException(
                    $"P3 VelocityDirection {item.Name} did not converge to velocity yaw: " +
                    $"expected={expectedYaw:R} actual={result.TargetYaw:R}");
            }
            var actualBlend = ReadStandingBlend(graph);
            RequireFiniteBlend(actualBlend, $"VelocityDirection {item.Name}");
            if (MathF.Abs(actualBlend.X) >= 1e-3f || actualBlend.Y <= 0f)
            {
                throw new InvalidOperationException(
                    $"P3 VelocityDirection {item.Name} did not face local forward after convergence: " +
                    $"actual={actualBlend}");
            }
        }
    }

    private static AlsLocomotionCommand CreateDirectionalCommand(
        System.Numerics.Vector2 movementAxes,
        float viewYaw,
        float aimYaw,
        AlsRotationMode rotationMode) => new(
        movementAxes,
        viewYaw,
        0f,
        aimYaw,
        0f,
        AlsGait.Running,
        AlsStance.Standing,
        rotationMode,
        0);

    private static System.Numerics.Vector3 RequireCameraRelativeDirection(
        in AlsLocomotionCommand command,
        string label)
    {
        var resolved = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing);
        var localDirection = new System.Numerics.Vector3(
            command.MovementAxes.X,
            0f,
            -command.MovementAxes.Y);
        var expected = System.Numerics.Vector3.Transform(
            localDirection,
            System.Numerics.Matrix4x4.CreateRotationY(command.ViewYaw));
        if (System.Numerics.Vector3.Distance(resolved.WorldDirection, expected) >= 1e-5f)
        {
            throw new InvalidOperationException(
                $"P3 camera-relative {label} world direction mismatch: " +
                $"expected={expected} actual={resolved.WorldDirection}");
        }
        return resolved.WorldDirection;
    }

    private static AlsFrameInput CreateDirectionalInput(
        long frameId,
        in AlsLocomotionCommand command,
        System.Numerics.Vector3 actualVelocity,
        float characterYaw,
        float deltaTime)
    {
        var input = AlsFrameInput.CreateDefault(
            new AlsFrameIdentity(frameId, 0, 1),
            deltaTime);
        return input with
        {
            CharacterTransform = System.Numerics.Matrix4x4.CreateRotationY(characterYaw),
            ActualVelocity = actualVelocity,
            InputDirection = System.Numerics.Vector3.Normalize(actualVelocity),
            DesiredSpeed = actualVelocity.Length(),
            ViewRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                System.Numerics.Vector3.UnitY,
                command.ViewYaw),
            AimRotation = System.Numerics.Quaternion.CreateFromAxisAngle(
                System.Numerics.Vector3.UnitY,
                command.AimYaw),
            Floor = input.Floor with { IsGrounded = 1 },
            RequestedGait = command.RequestedGait,
            Stance = command.RequestedStance,
            RotationMode = command.RequestedRotationMode,
            Command = command,
            CharacterYaw = characterYaw,
            MaxAcceleration = 20f,
            MaxBrakingDeceleration = 15f,
        };
    }

    private static void RequireDirectionalResult(
        in AlsFrameResult result,
        AlsRotationMode expectedRotationMode,
        string label)
    {
        if (result.ResolvedLocomotionState != AlsLocomotionState.Grounded ||
            result.ActualGait != AlsGait.Running ||
            result.ActualStance != AlsStance.Standing ||
            result.ActualRotationMode != expectedRotationMode ||
            result.AnimationState != AlsAnimationState.Grounded)
        {
            throw new InvalidOperationException(
                $"P3 {expectedRotationMode} {label} result contract mismatch: " +
                $"state={result.ResolvedLocomotionState}/{result.AnimationState} " +
                $"gait={result.ActualGait} stance={result.ActualStance} " +
                $"rotation={result.ActualRotationMode}");
        }
    }

    private static Vector2 ReadStandingBlend(AlsLocomotionGraphBuildResult graph) =>
        graph.Tree.Get(graph.Handles.GroundedStanding.BlendPositionPath!).AsVector2();

    private static void RequireStandingBlend(
        AlsLocomotionGraphBuildResult graph,
        Vector2 expected,
        float tolerance,
        string label)
    {
        var actual = ReadStandingBlend(graph);
        RequireFiniteBlend(actual, label);
        // Lateral movement retains either front or rear hips according to arrival direction.
        if (expected.X != 0f && expected.Y > 0f) actual.Y = MathF.Abs(actual.Y);
        if (actual.DistanceTo(expected) >= tolerance)
        {
            throw new InvalidOperationException(
                $"P3 {label} standing-run blend mismatch: expected={expected} actual={actual}");
        }
    }

    private static void RequireFiniteBlend(Vector2 blend, string label)
    {
        if (!float.IsFinite(blend.X) || !float.IsFinite(blend.Y))
        {
            throw new InvalidOperationException(
                $"P3 {label} blend was not finite: {blend}");
        }
    }

    private static void VerifyFiniteBlendGuard()
    {
        var invalidBlends = new[]
        {
            new Vector2(float.NaN, 0f),
            new Vector2(0f, float.PositiveInfinity),
            new Vector2(float.NegativeInfinity, 0f),
        };
        foreach (var invalidBlend in invalidBlends)
        {
            try
            {
                RequireFiniteBlend(invalidBlend, "finite guard probe");
                throw new InvalidOperationException(
                    $"P3 finite blend guard accepted {invalidBlend}.");
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("not finite", StringComparison.Ordinal))
            {
            }
        }
    }

    private static float NormalizedYawError(float actual, float expected) =>
        MathF.Abs(AlsMath.NormalizeAngleRadians(actual - expected));

    private ulong VerifyDirectionalPoseEvidence(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile,
        AlsLocomotionSettings settings)
    {
        var poseDigests = new ulong[4];
        var poseOrder = new[] { 0, 2, 1, 3 };
        for (var poseIndex = 0; poseIndex < poseOrder.Length; poseIndex++)
        {
            var item = DirectionCases[poseOrder[poseIndex]];
            using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
            AddChild(library.Root);
            using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
            using var controller = new AlsLocomotionAnimationController(graph, settings);
            controller.Warmup();

            var command = CreateDirectionalCommand(
                item.MovementAxes,
                0f,
                0f,
                AlsRotationMode.LookingDirection);
            var worldDirection = RequireCameraRelativeDirection(command, item.Name);
            var state = new AlsRuntimeState();
            var result = new AlsFrameResult();
            for (var frame = 1; frame <= 8; frame++)
            {
                var input = CreateDirectionalInput(
                    frame,
                    command,
                    worldDirection * 3.75f,
                    0f,
                    settings.FixedDeltaSeconds);
                AlsLocomotionModel.Evaluate(input, ref state, ref result, settings);
                RequireDirectionalResult(result, AlsRotationMode.LookingDirection, item.Name);
                result.AnimationPhase = 0.375f;
                controller.Apply(in result, settings.FixedDeltaSeconds);
            }
            poseDigests[poseIndex] = controller.ComputePoseDigest(777);
        }

        if (poseDigests.Distinct().Count() != poseDigests.Length)
        {
            throw new InvalidOperationException(
                "P3 F/B/left/right pose digests were not pairwise distinct: " +
                string.Join(",", poseDigests.Select(value => $"{value:X16}")));
        }

        const ulong offsetBasis = 14695981039346656037UL;
        var directionDigest = offsetBasis;
        foreach (var poseDigest in poseDigests)
        {
            AppendDirectionDigest(ref directionDigest, poseDigest);
        }
        return directionDigest;
    }

    private static void AppendDirectionDigest(ref ulong digest, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= prime;
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
            !GodotObject.IsInstanceValid(independentGraph.TargetSkeleton) ||
            !ReferenceEquals(independentGraph.TargetSkeleton, independentLibrary.Skeleton) ||
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
                   rebuiltGraph, settings))
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

    private readonly record struct DirectionCase(
        string Name,
        System.Numerics.Vector2 MovementAxes,
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
