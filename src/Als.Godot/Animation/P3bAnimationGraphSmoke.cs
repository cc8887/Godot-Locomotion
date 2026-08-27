using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Import;
using GodotAls.Import.Compilation;

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

        using var library = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(library, profile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, library.Skeleton);
        controller.Warmup();

        var segments = CreateSegments();
        var previousState = segments[0].ExpectedState;
        var previousStance = segments[0].ExpectedStance;
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

            if (controller.ActiveAnimationState != segment.ExpectedState ||
                controller.ActiveStance != segment.ExpectedStance)
            {
                throw new InvalidOperationException(
                    $"P3 graph state mismatch for {segment.Name}: " +
                    $"expected={segment.ExpectedState}/{segment.ExpectedStance} " +
                    $"actual={controller.ActiveAnimationState}/{controller.ActiveStance}");
            }

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

            if (segment.ExpectedState != previousState ||
                (segment.ExpectedState == AlsAnimationState.Grounded &&
                 segment.ExpectedStance != previousStance))
            {
                transitions++;
            }
            previousState = segment.ExpectedState;
            previousStance = segment.ExpectedStance;
        }

        if (transitions != 5)
        {
            throw new InvalidOperationException(
                $"P3 graph transition count mismatch: expected=5 actual={transitions}");
        }
        if (controller.ManualAdvanceCount != frameId)
        {
            throw new InvalidOperationException(
                $"P3 graph must advance exactly once per Apply: " +
                $"applies={frameId} advances={controller.ManualAdvanceCount}");
        }

        var digest = controller.ComputePoseDigest(frameId);
        VerifyLifecycle(definition, profile);
        return digest;
    }

    private void VerifyLifecycle(
        AlsAnimationSetDefinition definition,
        AlsLocomotionAnimationProfile profile)
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
                   rebuiltGraph, partialLibrary.Skeleton))
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
}
