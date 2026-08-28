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

        var allIds = locomotionProfile.AllAnimationIds
            .Concat(poseProfile.Turns.Select(value => value.AnimationId))
            .Concat(poseProfile.Rotates.Select(value => value.AnimationId))
            .Concat([
                poseProfile.Aim.DownAnimationId,
                poseProfile.Aim.ForwardAnimationId,
                poseProfile.Aim.UpAnimationId,
                poseProfile.Aim.AdditiveBasePoseAnimationId,
            ])
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var libraryProfile = locomotionProfile with { AllAnimationIds = allIds };

        using var library = AlsAnimationLibraryBuilder.Build(definition, libraryProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();

        VerifyNamedNodes(graph);
        VerifyExactBindings(graph, poseProfile, library, definition);

        var digest = DigestOffset;
        var result = ValidResult();
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var slot = frame / 20;
            var phase = (frame % 20 + 1) / 20f;
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

            result.Identity = new AlsFrameIdentity(frame + 1, 0, 1);
            controller.Apply(in result, in p4, DeltaTime);
            var p4Handles = graph.Handles.P4!;
            var actualSelection = graph.Tree.Get(
                slot < poseProfile.Turns.Length
                    ? p4Handles.TurnSelectionPath
                    : p4Handles.RotateSelectionPath).AsSingle();
            var expectedSelection = slot < poseProfile.Turns.Length
                ? slot
                : slot - poseProfile.Turns.Length;
            if (actualSelection != expectedSelection ||
                controller.ActiveTurnAnimationId != p4.ActiveTurnAnimationId ||
                controller.ActiveRotateAnimationId != p4.ActiveRotateAnimationId)
            {
                throw new InvalidOperationException(
                    $"P4 controller did not select the requested exact ID: frame={frame} slot={slot}");
            }
            Append(ref digest, unchecked((ulong)(uint)p4.ActiveTurnAnimationId));
            Append(ref digest, unchecked((ulong)(uint)p4.ActiveRotateAnimationId));
            Append(ref digest, unchecked((ulong)(uint)BitConverter.SingleToInt32Bits(phase)));
        }

        if (controller.ManualAdvanceCount != FrameCount)
        {
            throw new InvalidOperationException(
                $"P4 graph must advance once per frame: frames={FrameCount} advances={controller.ManualAdvanceCount}");
        }

        VerifyControllerTransaction(controller, graph, in result);
        return digest;
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
        var root = graph.Tree.TreeRoot as AnimationNodeStateMachine
            ?? throw new InvalidOperationException("P4 graph root is not a state machine.");
        foreach (var name in new[] { "P4Turn", "P4Rotate", "P4AimDown", "P4AimForward", "P4AimUp" })
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
        var root = (AnimationNodeStateMachine)graph.Tree.TreeRoot;
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
            var branch = root.GetNode(stateName) as AnimationNodeBlendTree
                ?? throw new InvalidOperationException($"P4 action branch is invalid: {stateName}");
            using var selectName = new StringName("Select");
            var select = branch.GetNode(selectName) as AnimationNodeBlendSpace1D
                ?? throw new InvalidOperationException($"P4 action selector is missing: {stateName}");
            if (select.GetBlendPointCount() != animationIds.Length)
            {
                throw new InvalidOperationException($"P4 action selector count mismatch: {stateName}");
            }
            for (var index = 0; index < animationIds.Length; index++)
            {
                var clip = select.GetBlendPointNode(index) as AnimationNodeAnimation
                    ?? throw new InvalidOperationException($"P4 selector point is not an animation: {stateName}/{index}");
                if (select.GetBlendPointPosition(index) != index ||
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
            var clip = branch.GetNode(aimName) as AnimationNodeAnimation
                ?? throw new InvalidOperationException($"P4 Aim clip is missing: {stateName}");
            if (!library.ClipNames.TryGetValue(animationId, out var clipName) ||
                !string.Equals(clip.Animation.ToString(), $"als/{clipName}", StringComparison.Ordinal))
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
        var previousRotateSelection = GetTreeValue(p4Handles.RotateSelectionPath);
        var previousAimWeight = GetTreeValue(p4Handles.AimForwardWeightPath);
        var malformedInputs = new[]
        {
            AlsP4AnimationInput.Disabled with { TurnPlayRate = float.NaN },
            AlsP4AnimationInput.Disabled with { RotatePhase = -0.01f },
            AlsP4AnimationInput.Disabled with { AimDownPhase = float.PositiveInfinity },
            AlsP4AnimationInput.Disabled with { AimForwardWeight = 1.01f },
            AlsP4AnimationInput.Disabled with { ActiveTurnAnimationId = -2 },
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
                GetTreeValue(p4Handles.RotateSelectionPath) != previousRotateSelection ||
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
