using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsLocomotionGraphBuildResult : IDisposable
{
    private readonly List<IDisposable> _ownedResources;
    private int _disposed;

    internal AlsLocomotionGraphBuildResult(
        AnimationTree tree,
        AlsLocomotionGraphHandles handles,
        List<IDisposable> ownedResources)
    {
        Tree = tree;
        Handles = handles;
        _ownedResources = ownedResources;
    }

    public AnimationTree Tree { get; }

    public AlsLocomotionGraphHandles Handles { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (GodotObject.IsInstanceValid(Tree))
            {
                var parent = Tree.GetParent();
                if (GodotObject.IsInstanceValid(parent))
                {
                    parent.RemoveChild(Tree);
                }
                Tree.Free();
            }
        }
        finally
        {
            try
            {
                Handles.Dispose();
            }
            finally
            {
                for (var index = _ownedResources.Count - 1; index >= 0; index--)
                {
                    _ownedResources[index].Dispose();
                }
                _ownedResources.Clear();
            }
        }
    }
}

public sealed class AlsLocomotionGraphHandles : IDisposable
{
    private readonly IDisposable[] _ownedHandles;
    private int _disposed;

    internal AlsLocomotionGraphHandles(
        NodePath animationPlayerPath,
        StringName topPlaybackPath,
        StringName groundedPlaybackPath,
        StringName[] stateNames,
        StringName[] stanceNames,
        AlsLocomotionGraphParameterSet groundedStanding,
        AlsLocomotionGraphParameterSet groundedCrouching,
        AlsLocomotionGraphParameterSet jumpStart,
        AlsLocomotionGraphParameterSet fallLoop,
        AlsLocomotionGraphParameterSet landRecovery)
    {
        AnimationPlayerPath = animationPlayerPath;
        TopPlaybackPath = topPlaybackPath;
        GroundedPlaybackPath = groundedPlaybackPath;
        StateNames = stateNames;
        StanceNames = stanceNames;
        GroundedStanding = groundedStanding;
        GroundedCrouching = groundedCrouching;
        JumpStart = jumpStart;
        FallLoop = fallLoop;
        LandRecovery = landRecovery;

        _ownedHandles =
        [
            animationPlayerPath,
            topPlaybackPath,
            groundedPlaybackPath,
            .. stateNames,
            .. stanceNames,
            .. groundedStanding.OwnedHandles,
            .. groundedCrouching.OwnedHandles,
            .. jumpStart.OwnedHandles,
            .. fallLoop.OwnedHandles,
            .. landRecovery.OwnedHandles,
        ];
    }

    public NodePath AnimationPlayerPath { get; }

    public StringName TopPlaybackPath { get; }

    public StringName GroundedPlaybackPath { get; }

    public IReadOnlyList<StringName> StateNames { get; }

    public IReadOnlyList<StringName> StanceNames { get; }

    public AlsLocomotionGraphParameterSet GroundedStanding { get; }

    public AlsLocomotionGraphParameterSet GroundedCrouching { get; }

    public AlsLocomotionGraphParameterSet JumpStart { get; }

    public AlsLocomotionGraphParameterSet FallLoop { get; }

    public AlsLocomotionGraphParameterSet LandRecovery { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var handle in _ownedHandles)
        {
            handle.Dispose();
        }
    }
}

public sealed class AlsLocomotionGraphParameterSet
{
    internal AlsLocomotionGraphParameterSet(
        StringName? blendPositionPath,
        StringName leanPositionPath,
        StringName leanAmountPath,
        StringName playRatePath,
        StringName phasePath,
        Vector2 blendMinimum,
        Vector2 blendMaximum,
        Vector2 leanMinimum,
        Vector2 leanMaximum)
    {
        BlendPositionPath = blendPositionPath;
        LeanPositionPath = leanPositionPath;
        LeanAmountPath = leanAmountPath;
        PlayRatePath = playRatePath;
        PhasePath = phasePath;
        BlendMinimum = blendMinimum;
        BlendMaximum = blendMaximum;
        LeanMinimum = leanMinimum;
        LeanMaximum = leanMaximum;
        OwnedHandles = blendPositionPath is null
            ? [leanPositionPath, leanAmountPath, playRatePath, phasePath]
            : [blendPositionPath, leanPositionPath, leanAmountPath, playRatePath, phasePath];
    }

    public StringName? BlendPositionPath { get; }

    public StringName LeanPositionPath { get; }

    public StringName LeanAmountPath { get; }

    public StringName PlayRatePath { get; }

    public StringName PhasePath { get; }

    public Vector2 BlendMinimum { get; }

    public Vector2 BlendMaximum { get; }

    public Vector2 LeanMinimum { get; }

    public Vector2 LeanMaximum { get; }

    internal IDisposable[] OwnedHandles { get; }
}

public static class AlsLocomotionGraphBuilder
{
    private const string LibraryName = "als";
    private const float TransitionTime = 0.08f;

    public static AlsLocomotionGraphBuildResult Build(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsAnimationSetDefinition animationSet)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(animationSet);
        ValidateInputs(library, profile, animationSet);

        var ownedResources = new List<IDisposable>();
        AlsLocomotionGraphHandles? handles = null;
        AnimationTree? tree = null;
        try
        {
            handles = CreateHandles(profile);
            var top = Own(ownedResources, new AnimationNodeStateMachine
            {
                StateMachineType = AnimationNodeStateMachine.StateMachineTypeEnum.Root,
            });
            var grounded = Own(ownedResources, new AnimationNodeStateMachine
            {
                StateMachineType = AnimationNodeStateMachine.StateMachineTypeEnum.Nested,
            });

            var standing = BuildGroundedBranch(
                library,
                profile.StandingIdleAnimationId,
                profile.StandingSamples,
                profile.LeanAdditiveSamples,
                ownedResources);
            var crouching = BuildGroundedBranch(
                library,
                profile.CrouchingIdleAnimationId,
                profile.CrouchingSamples,
                profile.LeanAdditiveSamples,
                ownedResources);
            grounded.AddNode(handles.StanceNames[(int)AlsStance.Standing], standing);
            grounded.AddNode(handles.StanceNames[(int)AlsStance.Crouching], crouching);
            AddTransition(
                grounded,
                handles.StanceNames[(int)AlsStance.Standing],
                handles.StanceNames[(int)AlsStance.Crouching],
                ownedResources);
            AddTransition(
                grounded,
                handles.StanceNames[(int)AlsStance.Crouching],
                handles.StanceNames[(int)AlsStance.Standing],
                ownedResources);

            var jumpStart = BuildActionBranch(
                library, profile.JumpStartAnimationId, profile.LeanAdditiveSamples, false, ownedResources);
            var fallLoop = BuildActionBranch(
                library, profile.FallLoopAnimationId, profile.LeanAdditiveSamples, true, ownedResources);
            var landRecovery = BuildActionBranch(
                library, profile.LandAnimationId, profile.LeanAdditiveSamples, false, ownedResources);

            top.AddNode(handles.StateNames[(int)AlsAnimationState.Grounded], grounded);
            top.AddNode(handles.StateNames[(int)AlsAnimationState.JumpStart], jumpStart);
            top.AddNode(handles.StateNames[(int)AlsAnimationState.FallLoop], fallLoop);
            top.AddNode(handles.StateNames[(int)AlsAnimationState.LandRecovery], landRecovery);
            AddAllStateTransitions(top, handles.StateNames, ownedResources);

            tree = new AnimationTree
            {
                Name = "AlsLocomotionAnimationTree",
                TreeRoot = top,
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
                Deterministic = true,
                Active = false,
            };
            library.Root.AddChild(tree);
            using (var rootPath = new NodePath(".."))
            {
                tree.RootNode = rootPath;
            }
            tree.AnimPlayer = handles.AnimationPlayerPath;

            var result = new AlsLocomotionGraphBuildResult(tree, handles, ownedResources);
            tree = null;
            handles = null;
            return result;
        }
        catch
        {
            if (tree is not null && GodotObject.IsInstanceValid(tree))
            {
                var parent = tree.GetParent();
                if (GodotObject.IsInstanceValid(parent))
                {
                    parent.RemoveChild(tree);
                }
                tree.Free();
            }
            handles?.Dispose();
            for (var index = ownedResources.Count - 1; index >= 0; index--)
            {
                ownedResources[index].Dispose();
            }
            throw;
        }
    }

    private static void ValidateInputs(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsAnimationSetDefinition animationSet)
    {
        if (!GodotObject.IsInstanceValid(library.Root) ||
            !GodotObject.IsInstanceValid(library.Skeleton) ||
            !GodotObject.IsInstanceValid(library.Player))
        {
            throw new ObjectDisposedException(nameof(library));
        }
        if ((uint)profile.SkeletonId >= (uint)animationSet.Skeletons.Length)
        {
            throw new InvalidOperationException(
                $"P3 graph profile skeleton ID is out of range: {profile.SkeletonId}");
        }
        foreach (var animationId in profile.AllAnimationIds)
        {
            if ((uint)animationId >= (uint)animationSet.Animations.Length ||
                !library.ClipNames.TryGetValue(animationId, out var clipName) ||
                !library.Library.HasAnimation(clipName))
            {
                throw new InvalidOperationException(
                    $"P3 graph library is missing profile animation ID: {animationId}");
            }
        }
    }

    private static AlsLocomotionGraphHandles CreateHandles(
        AlsLocomotionAnimationProfile profile)
    {
        var stateNames = new[]
        {
            new StringName("Grounded"),
            new StringName("JumpStart"),
            new StringName("FallLoop"),
            new StringName("LandRecovery"),
        };
        var stanceNames = new[]
        {
            new StringName("Standing"),
            new StringName("Crouching"),
        };
        var leanBounds = Bounds(profile.LeanAdditiveSamples, false);
        var standingBounds = Bounds(profile.StandingSamples, true);
        var crouchingBounds = Bounds(profile.CrouchingSamples, true);
        return new AlsLocomotionGraphHandles(
            new NodePath("../AlsAnimationPlayer"),
            new StringName("parameters/playback"),
            new StringName("parameters/Grounded/playback"),
            stateNames,
            stanceNames,
            CreateParameterSet("Grounded/Standing", true, standingBounds, leanBounds),
            CreateParameterSet("Grounded/Crouching", true, crouchingBounds, leanBounds),
            CreateParameterSet("JumpStart", false, default, leanBounds),
            CreateParameterSet("FallLoop", false, default, leanBounds),
            CreateParameterSet("LandRecovery", false, default, leanBounds));
    }

    private static AlsLocomotionGraphParameterSet CreateParameterSet(
        string prefix,
        bool hasBlendPosition,
        (Vector2 Minimum, Vector2 Maximum) blendBounds,
        (Vector2 Minimum, Vector2 Maximum) leanBounds) => new(
            hasBlendPosition
                ? new StringName($"parameters/{prefix}/Locomotion/blend_position")
                : null,
            new StringName($"parameters/{prefix}/Lean/blend_position"),
            new StringName($"parameters/{prefix}/LeanAdd/add_amount"),
            new StringName($"parameters/{prefix}/Scale/scale"),
            new StringName($"parameters/{prefix}/Seek/seek_request"),
            blendBounds.Minimum,
            blendBounds.Maximum,
            leanBounds.Minimum,
            leanBounds.Maximum);

    private static AnimationNodeBlendTree BuildGroundedBranch(
        AlsAnimationLibraryBuildResult library,
        int idleAnimationId,
        IReadOnlyList<AlsLocomotionAnimationSample> locomotionSamples,
        IReadOnlyList<AlsLocomotionAnimationSample> leanSamples,
        List<IDisposable> ownedResources)
    {
        var samples = new AlsLocomotionAnimationSample[locomotionSamples.Count + 1];
        samples[0] = new AlsLocomotionAnimationSample(idleAnimationId, 0f, 0f, 1f);
        for (var index = 0; index < locomotionSamples.Count; index++)
        {
            samples[index + 1] = locomotionSamples[index];
        }
        return BuildLayeredBranch(library, samples, leanSamples, true, ownedResources);
    }

    private static AnimationNodeBlendTree BuildActionBranch(
        AlsAnimationLibraryBuildResult library,
        int animationId,
        IReadOnlyList<AlsLocomotionAnimationSample> leanSamples,
        bool loop,
        List<IDisposable> ownedResources)
    {
        var tree = Own(ownedResources, new AnimationNodeBlendTree());
        var clip = CreateAnimationNode(library, animationId, loop, ownedResources);
        AddLayerAndTimingNodes(tree, clip, library, leanSamples, ownedResources);
        return tree;
    }

    private static AnimationNodeBlendTree BuildLayeredBranch(
        AlsAnimationLibraryBuildResult library,
        IReadOnlyList<AlsLocomotionAnimationSample> locomotionSamples,
        IReadOnlyList<AlsLocomotionAnimationSample> leanSamples,
        bool loop,
        List<IDisposable> ownedResources)
    {
        var tree = Own(ownedResources, new AnimationNodeBlendTree());
        var locomotion = BuildBlendSpace(
            library, locomotionSamples, loop, ownedResources);
        AddLayerAndTimingNodes(tree, locomotion, library, leanSamples, ownedResources);
        return tree;
    }

    private static void AddLayerAndTimingNodes(
        AnimationNodeBlendTree tree,
        AnimationRootNode baseNode,
        AlsAnimationLibraryBuildResult library,
        IReadOnlyList<AlsLocomotionAnimationSample> leanSamples,
        List<IDisposable> ownedResources)
    {
        var lean = BuildBlendSpace(library, leanSamples, true, ownedResources);
        var leanAdd = Own(ownedResources, new AnimationNodeAdd2());
        var scale = Own(ownedResources, new AnimationNodeTimeScale());
        var seek = Own(ownedResources, new AnimationNodeTimeSeek());

        using var locomotionName = new StringName("Locomotion");
        using var leanName = new StringName("Lean");
        using var leanAddName = new StringName("LeanAdd");
        using var scaleName = new StringName("Scale");
        using var seekName = new StringName("Seek");
        using var outputName = new StringName("output");
        tree.AddNode(locomotionName, baseNode);
        tree.AddNode(leanName, lean);
        tree.AddNode(leanAddName, leanAdd);
        tree.AddNode(scaleName, scale);
        tree.AddNode(seekName, seek);
        tree.ConnectNode(leanAddName, 0, locomotionName);
        tree.ConnectNode(leanAddName, 1, leanName);
        tree.ConnectNode(scaleName, 0, leanAddName);
        tree.ConnectNode(seekName, 0, scaleName);
        tree.ConnectNode(outputName, 0, seekName);
    }

    private static AnimationNodeBlendSpace2D BuildBlendSpace(
        AlsAnimationLibraryBuildResult library,
        IReadOnlyList<AlsLocomotionAnimationSample> samples,
        bool loop,
        List<IDisposable> ownedResources)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("P3 graph blend space cannot be empty.");
        }

        var bounds = Bounds(samples, false);
        var blend = Own(ownedResources, new AnimationNodeBlendSpace2D
        {
            AutoTriangles = true,
            MinSpace = bounds.Minimum,
            MaxSpace = bounds.Maximum,
            Snap = new Vector2(0.01f, 0.01f),
            XLabel = "Lateral",
            YLabel = "Forward",
            SyncMode = AnimationNodeBlendSpace2D.SyncModeEnum.CyclicMutable,
        });
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var animation = CreateAnimationNode(
                library, sample.AnimationId, loop, ownedResources);
            using var pointName = new StringName($"clip_{sample.AnimationId}_{index}");
            blend.AddBlendPoint(animation, new Vector2(sample.X, sample.Y), -1, pointName);
        }
        return blend;
    }

    private static AnimationNodeAnimation CreateAnimationNode(
        AlsAnimationLibraryBuildResult library,
        int animationId,
        bool loop,
        List<IDisposable> ownedResources)
    {
        if (!library.ClipNames.TryGetValue(animationId, out var clipName))
        {
            throw new InvalidOperationException(
                $"P3 graph library has no clip name for animation ID: {animationId}");
        }

        var node = Own(ownedResources, new AnimationNodeAnimation
        {
            UseCustomTimeline = true,
            TimelineLength = 1.0,
            StretchTimeScale = true,
            LoopMode = loop ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None,
        });
        using var qualifiedName = new StringName($"{LibraryName}/{clipName}");
        node.Animation = qualifiedName;
        return node;
    }

    private static void AddAllStateTransitions(
        AnimationNodeStateMachine stateMachine,
        IReadOnlyList<StringName> stateNames,
        List<IDisposable> ownedResources)
    {
        for (var from = 0; from < stateNames.Count; from++)
        {
            for (var to = 0; to < stateNames.Count; to++)
            {
                if (from != to)
                {
                    AddTransition(stateMachine, stateNames[from], stateNames[to], ownedResources);
                }
            }
        }
    }

    private static void AddTransition(
        AnimationNodeStateMachine stateMachine,
        StringName from,
        StringName to,
        List<IDisposable> ownedResources)
    {
        var transition = Own(ownedResources, new AnimationNodeStateMachineTransition
        {
            AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled,
            Reset = true,
            XfadeTime = TransitionTime,
        });
        stateMachine.AddTransition(from, to, transition);
    }

    private static (Vector2 Minimum, Vector2 Maximum) Bounds(
        IReadOnlyList<AlsLocomotionAnimationSample> samples,
        bool includeOrigin)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("P3 graph cannot calculate empty sample bounds.");
        }

        var minimum = includeOrigin
            ? Vector2.Zero
            : new Vector2(samples[0].X, samples[0].Y);
        var maximum = minimum;
        for (var index = includeOrigin ? 0 : 1; index < samples.Count; index++)
        {
            var point = new Vector2(samples[index].X, samples[index].Y);
            minimum = new Vector2(MathF.Min(minimum.X, point.X), MathF.Min(minimum.Y, point.Y));
            maximum = new Vector2(MathF.Max(maximum.X, point.X), MathF.Max(maximum.Y, point.Y));
        }

        // Godot requires non-zero blend-space extents even for a one-axis source grid.
        if (Mathf.IsEqualApprox(minimum.X, maximum.X))
        {
            minimum.X -= 0.001f;
            maximum.X += 0.001f;
        }
        if (Mathf.IsEqualApprox(minimum.Y, maximum.Y))
        {
            minimum.Y -= 0.001f;
            maximum.Y += 0.001f;
        }
        return (minimum, maximum);
    }

    private static T Own<T>(List<IDisposable> resources, T resource)
        where T : GodotObject, IDisposable
    {
        resources.Add(resource);
        return resource;
    }
}
