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
            File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)), definition);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));

        VerifyLibraryContract(definition, locomotionProfile, poseProfile);
        VerifyAuthoritativeActionPhase(definition, locomotionProfile, poseProfile, settings);
        VerifyControllerAllocation(definition, locomotionProfile, poseProfile, settings);

        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
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
            var actionPlayback = graph.Tree.Get(
                slot < poseProfile.Turns.Length
                    ? p4Handles.TurnPlaybackPath
                    : p4Handles.RotatePlaybackPath).As<AnimationNodeStateMachinePlayback>()!;
            var hasBinding = slot < poseProfile.Turns.Length
                ? p4Handles.TryGetTurnBinding(p4.ActiveTurnAnimationId, out var actionBinding)
                : p4Handles.TryGetRotateBinding(p4.ActiveRotateAnimationId, out actionBinding);
            if (!hasBinding ||
                actionPlayback.GetCurrentNode() != actionBinding.StateName ||
                controller.ActiveTurnAnimationId != p4.ActiveTurnAnimationId ||
                controller.ActiveRotateAnimationId != p4.ActiveRotateAnimationId)
            {
                throw new InvalidOperationException(
                    $"P4 controller did not select the requested exact ID: frame={frame} slot={slot} " +
                    $"expectedState={actionBinding.StateName} actualState={actionPlayback.GetCurrentNode()} " +
                    $"expectedTurn={p4.ActiveTurnAnimationId} actualTurn={controller.ActiveTurnAnimationId} " +
                    $"expectedRotate={p4.ActiveRotateAnimationId} actualRotate={controller.ActiveRotateAnimationId} " +
                    $"baseState={basePlayback.GetCurrentNode()}");
            }
            var expectedBaseState = slot < poseProfile.Turns.Length ? "P4Turn" : "P4Rotate";
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
            var actionPlayback = graph.Tree.Get(
                graph.Handles.P4!.TurnPlaybackPath).As<AnimationNodeStateMachinePlayback>();
            throw new InvalidOperationException(
                $"P4 TimeSeek phase differs from direct animation sampling at delta=0: " +
                $"phase={phaseSeconds:R} duration={duration:R} rate=1.7 " +
                $"graphPosition={playback?.GetCurrentPlayPosition():R} " +
                $"directPosition={directLibrary.Player.CurrentAnimationPosition:R} " +
                $"inner={actionPlayback?.GetCurrentNode()} " +
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
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        var result = ValidResult();
        var turn = poseProfile.Turns[0];
        var phase = definition.Animations[turn.AnimationId].PlayLength * 0.5f;
        var p4 = AlsP4AnimationInput.Turn(
            turn.AnimationId, turn.BasePlayRate, phase, 0.5f, 0f, 1f, 0f);
        for (var index = 0; index < 64; index++)
        {
            controller.Apply(in result, in p4, 0.0);
        }

        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            controller.Apply(in result, in p4, 0.0);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        if (allocated != 0)
        {
            throw new InvalidOperationException(
                $"Steady P4 controller Apply allocated managed memory: {allocated} B");
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
        };
        var sampler = new AlsCurveSampler(curves);
        var values = new float[3];
        var ids = new[] { 3, 7, 11 };

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
        if (sampler.TrySample(ids, 1f, values.AsSpan(0, 2)))
        {
            throw new InvalidOperationException("Short curve destination was accepted.");
        }

        var direct = new AlsCurveSampler(23, curves[1].Keys);
        if (!direct.TrySample(23, 1f, out var directValue) || directValue != 0f)
        {
            throw new InvalidOperationException("Direct key-array curve binding failed.");
        }

        ExpectInvalid([
            new AlsFloatCurveKeyDefinition(1f, 0f, 0f, 0f, AlsCurveInterpolation.Linear),
            new AlsFloatCurveKeyDefinition(1f, 1f, 0f, 0f, AlsCurveInterpolation.Linear),
        ]);

        var extremeLinear = new AlsCurveSampler(31,
        [
            new AlsFloatCurveKeyDefinition(0f, -float.MaxValue, 0f, 0f, AlsCurveInterpolation.Linear),
            new AlsFloatCurveKeyDefinition(2f, float.MaxValue, 0f, 0f, AlsCurveInterpolation.Linear),
        ]);
        if (!extremeLinear.TrySample(31, 1f, out var midpoint) || midpoint != 0f)
        {
            throw new InvalidOperationException("Extreme linear midpoint must remain finite zero.");
        }

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

        void AssertSamples(float time, float constant, float linear, float cubic)
        {
            if (!sampler.TrySample(ids, time, values) ||
                !Mathf.IsEqualApprox(values[0], constant) ||
                !Mathf.IsEqualApprox(values[1], linear) ||
                !Mathf.IsEqualApprox(values[2], cubic))
            {
                throw new InvalidOperationException(
                    $"Curve sample mismatch at {time:R}: [{values[0]:R},{values[1]:R},{values[2]:R}]");
            }
        }


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
    }

    private static void VerifyNamedNodes(AlsLocomotionGraphBuildResult graph)
    {
        var root = graph.Tree.TreeRoot as AnimationNodeBlendTree
            ?? throw new InvalidOperationException("P4 graph root is not the required layered BlendTree.");
        foreach (var name in new[] { "Base", "P4AimDown", "P4AimForward", "P4AimUp" })
        {
            using var nodeName = new StringName(name);
            if (!root.HasNode(nodeName))
            {
                throw new InvalidOperationException($"P4 graph node is missing: {name}");
            }
        }
        using var baseName = new StringName("Base");
        var baseStateMachine = root.GetNode(baseName) as AnimationNodeStateMachine
            ?? throw new InvalidOperationException("P4 Base state machine is missing.");
        foreach (var name in new[] { "P4Turn", "P4Rotate" })
        {
            using var nodeName = new StringName(name);
            if (!baseStateMachine.HasNode(nodeName))
            {
                throw new InvalidOperationException($"P4 Base action node is missing: {name}");
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
        using var baseName = new StringName("Base");
        var baseStateMachine = (AnimationNodeStateMachine)root.GetNode(baseName);
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
            var branch = baseStateMachine.GetNode(stateName) as AnimationNodeStateMachine
                ?? throw new InvalidOperationException($"P4 action branch is invalid: {stateName}");
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
                    binding.DurationSeconds != animation.PlayLength ||
                    clip.UseCustomTimeline || clip.StretchTimeScale ||
                    !library.ClipNames.TryGetValue(animationIds[index], out var clipName) ||
                    !string.Equals(clip.Animation.ToString(), $"als/{clipName}", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P4 selector is not exactly bound: {stateName}/{animationIds[index]}");
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
                !baseClip.UseCustomTimeline || baseClip.TimelineLength != 1.0 || !baseClip.StretchTimeScale)
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
        var previousRotateState = graph.Tree.Get(
            p4Handles.RotatePlaybackPath).As<AnimationNodeStateMachinePlayback>()!.GetCurrentNode();
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
                graph.Tree.Get(p4Handles.RotatePlaybackPath)
                    .As<AnimationNodeStateMachinePlayback>()!.GetCurrentNode() != previousRotateState ||
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
