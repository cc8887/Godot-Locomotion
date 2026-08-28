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
        Skeleton3D targetSkeleton,
        AlsLocomotionGraphHandles handles,
        List<IDisposable> ownedResources)
    {
        Tree = tree;
        TargetSkeleton = targetSkeleton;
        Handles = handles;
        _ownedResources = ownedResources;
    }

    public AnimationTree Tree { get; }

    public Skeleton3D TargetSkeleton { get; }

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
        float[] standingGaitRadii,
        float crouchingRadius,
        AlsLocomotionGraphParameterSet groundedStanding,
        AlsLocomotionGraphParameterSet groundedCrouching,
        AlsLocomotionGraphParameterSet jumpStart,
        AlsLocomotionGraphParameterSet fallLoop,
        AlsLocomotionGraphParameterSet landRecovery,
        AlsP4GraphHandles? p4)
    {
        AnimationPlayerPath = animationPlayerPath;
        TopPlaybackPath = topPlaybackPath;
        GroundedPlaybackPath = groundedPlaybackPath;
        StateNames = stateNames;
        StanceNames = stanceNames;
        StandingGaitRadii = standingGaitRadii;
        CrouchingRadius = crouchingRadius;
        GroundedStanding = groundedStanding;
        GroundedCrouching = groundedCrouching;
        JumpStart = jumpStart;
        FallLoop = fallLoop;
        LandRecovery = landRecovery;
        P4 = p4;

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
            .. (p4?.OwnedHandles ?? []),
        ];
    }

    public NodePath AnimationPlayerPath { get; }

    public StringName TopPlaybackPath { get; }

    public StringName GroundedPlaybackPath { get; }

    public IReadOnlyList<StringName> StateNames { get; }

    public IReadOnlyList<StringName> StanceNames { get; }

    public IReadOnlyList<float> StandingGaitRadii { get; }

    public float CrouchingRadius { get; }

    public AlsLocomotionGraphParameterSet GroundedStanding { get; }

    public AlsLocomotionGraphParameterSet GroundedCrouching { get; }

    public AlsLocomotionGraphParameterSet JumpStart { get; }

    public AlsLocomotionGraphParameterSet FallLoop { get; }

    public AlsLocomotionGraphParameterSet LandRecovery { get; }

    public AlsP4GraphHandles? P4 { get; }

    public bool TryGetP4TurnSelection(int animationId, out float selection)
    {
        selection = 0f;
        return P4 is not null && P4.TryGetTurnSelection(animationId, out selection);
    }

    public bool TryGetP4RotateSelection(int animationId, out float selection)
    {
        selection = 0f;
        return P4 is not null && P4.TryGetRotateSelection(animationId, out selection);
    }

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

public sealed class AlsP4GraphHandles
{
    private readonly Dictionary<int, float> _turnSelections;
    private readonly Dictionary<int, float> _rotateSelections;

    internal AlsP4GraphHandles(
        StringName turnStateName,
        StringName rotateStateName,
        StringName aimDownStateName,
        StringName aimForwardStateName,
        StringName aimUpStateName,
        StringName turnSelectionPath,
        StringName turnPlayRatePath,
        StringName turnPhasePath,
        StringName rotateSelectionPath,
        StringName rotatePlayRatePath,
        StringName rotatePhasePath,
        StringName aimDownPhasePath,
        StringName aimDownWeightPath,
        StringName aimForwardPhasePath,
        StringName aimForwardWeightPath,
        StringName aimUpPhasePath,
        StringName aimUpWeightPath,
        Dictionary<int, float> turnSelections,
        Dictionary<int, float> rotateSelections)
    {
        TurnStateName = turnStateName;
        RotateStateName = rotateStateName;
        AimDownStateName = aimDownStateName;
        AimForwardStateName = aimForwardStateName;
        AimUpStateName = aimUpStateName;
        TurnSelectionPath = turnSelectionPath;
        TurnPlayRatePath = turnPlayRatePath;
        TurnPhasePath = turnPhasePath;
        RotateSelectionPath = rotateSelectionPath;
        RotatePlayRatePath = rotatePlayRatePath;
        RotatePhasePath = rotatePhasePath;
        AimDownPhasePath = aimDownPhasePath;
        AimDownWeightPath = aimDownWeightPath;
        AimForwardPhasePath = aimForwardPhasePath;
        AimForwardWeightPath = aimForwardWeightPath;
        AimUpPhasePath = aimUpPhasePath;
        AimUpWeightPath = aimUpWeightPath;
        _turnSelections = turnSelections;
        _rotateSelections = rotateSelections;
        OwnedHandles =
        [
            turnStateName, rotateStateName, aimDownStateName, aimForwardStateName, aimUpStateName,
            turnSelectionPath, turnPlayRatePath, turnPhasePath,
            rotateSelectionPath, rotatePlayRatePath, rotatePhasePath,
            aimDownPhasePath, aimDownWeightPath,
            aimForwardPhasePath, aimForwardWeightPath,
            aimUpPhasePath, aimUpWeightPath,
        ];
    }

    public StringName TurnStateName { get; }
    public StringName RotateStateName { get; }
    public StringName AimDownStateName { get; }
    public StringName AimForwardStateName { get; }
    public StringName AimUpStateName { get; }
    public StringName TurnSelectionPath { get; }
    public StringName TurnPlayRatePath { get; }
    public StringName TurnPhasePath { get; }
    public StringName RotateSelectionPath { get; }
    public StringName RotatePlayRatePath { get; }
    public StringName RotatePhasePath { get; }
    public StringName AimDownPhasePath { get; }
    public StringName AimDownWeightPath { get; }
    public StringName AimForwardPhasePath { get; }
    public StringName AimForwardWeightPath { get; }
    public StringName AimUpPhasePath { get; }
    public StringName AimUpWeightPath { get; }
    internal IDisposable[] OwnedHandles { get; }

    public bool TryGetTurnSelection(int animationId, out float selection) =>
        _turnSelections.TryGetValue(animationId, out selection);

    public bool TryGetRotateSelection(int animationId, out float selection) =>
        _rotateSelections.TryGetValue(animationId, out selection);
}

public sealed class AlsLocomotionGraphParameterSet
{
    internal AlsLocomotionGraphParameterSet(
        StringName? blendPositionPath,
        StringName leanPositionPath,
        StringName leanAmountPath,
        StringName playRatePath,
        StringName? phasePath,
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
        var ownedHandles = new List<IDisposable>(5);
        if (blendPositionPath is not null)
        {
            ownedHandles.Add(blendPositionPath);
        }
        ownedHandles.Add(leanPositionPath);
        ownedHandles.Add(leanAmountPath);
        ownedHandles.Add(playRatePath);
        if (phasePath is not null)
        {
            ownedHandles.Add(phasePath);
        }
        OwnedHandles = ownedHandles.ToArray();
    }

    public StringName? BlendPositionPath { get; }

    public StringName LeanPositionPath { get; }

    public StringName LeanAmountPath { get; }

    public StringName PlayRatePath { get; }

    public StringName? PhasePath { get; }

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
    private const float CoordinateTolerance = 1e-5f;
    private const float RingTolerance = 1e-4f;

    public static AlsLocomotionGraphBuildResult Build(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsAnimationSetDefinition animationSet) =>
        BuildInternal(library, profile, null, animationSet);

    public static AlsLocomotionGraphBuildResult Build(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile,
        AlsAnimationSetDefinition animationSet) =>
        BuildInternal(library, profile, poseProfile, animationSet);

    private static AlsLocomotionGraphBuildResult BuildInternal(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile? poseProfile,
        AlsAnimationSetDefinition animationSet)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(animationSet);
        ValidateInputs(library, profile, animationSet);
        if (poseProfile is not null)
        {
            ValidateP4Inputs(library, profile, poseProfile, animationSet);
        }
        var layout = ValidateProfileLayout(profile);

        var ownedResources = new List<IDisposable>();
        AlsLocomotionGraphHandles? handles = null;
        AnimationTree? tree = null;
        try
        {
            handles = CreateHandles(layout, poseProfile);
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
            if (poseProfile is not null)
            {
                var p4 = handles.P4!;
                top.AddNode(
                    p4.TurnStateName,
                    BuildP4ActionBranch(
                        library,
                        poseProfile.Turns.Select(value => value.AnimationId).ToArray(),
                        ownedResources));
                top.AddNode(
                    p4.RotateStateName,
                    BuildP4ActionBranch(
                        library,
                        poseProfile.Rotates.Select(value => value.AnimationId).ToArray(),
                        ownedResources));
                top.AddNode(
                    p4.AimDownStateName,
                    BuildP4AimBranch(
                        library,
                        poseProfile.Aim.AdditiveBasePoseAnimationId,
                        poseProfile.Aim.DownAnimationId,
                        ownedResources));
                top.AddNode(
                    p4.AimForwardStateName,
                    BuildP4AimBranch(
                        library,
                        poseProfile.Aim.AdditiveBasePoseAnimationId,
                        poseProfile.Aim.ForwardAnimationId,
                        ownedResources));
                top.AddNode(
                    p4.AimUpStateName,
                    BuildP4AimBranch(
                        library,
                        poseProfile.Aim.AdditiveBasePoseAnimationId,
                        poseProfile.Aim.UpAnimationId,
                        ownedResources));
                AddAllStateTransitions(
                    top,
                    [
                        .. handles.StateNames,
                        p4.TurnStateName,
                        p4.RotateStateName,
                        p4.AimDownStateName,
                        p4.AimForwardStateName,
                        p4.AimUpStateName,
                    ],
                    ownedResources);
            }
            else
            {
                AddAllStateTransitions(top, handles.StateNames, ownedResources);
            }

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

            var result = new AlsLocomotionGraphBuildResult(
                tree, library.Skeleton, handles, ownedResources);
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

    private static void ValidateP4Inputs(
        AlsAnimationLibraryBuildResult library,
        AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile,
        AlsAnimationSetDefinition animationSet)
    {
        if (poseProfile.SkeletonId != profile.SkeletonId)
        {
            throw new InvalidOperationException(
                $"P4 graph skeleton mismatch: expected={profile.SkeletonId} actual={poseProfile.SkeletonId}");
        }
        if (poseProfile.Turns.Length != 8 || poseProfile.Rotates.Length != 4)
        {
            throw new InvalidOperationException(
                $"P4 graph requires exactly eight Turn and four Rotate clips: " +
                $"turns={poseProfile.Turns.Length} rotates={poseProfile.Rotates.Length}");
        }

        var animationIds = poseProfile.Turns.Select(value => value.AnimationId)
            .Concat(poseProfile.Rotates.Select(value => value.AnimationId))
            .Concat([
                poseProfile.Aim.DownAnimationId,
                poseProfile.Aim.ForwardAnimationId,
                poseProfile.Aim.UpAnimationId,
                poseProfile.Aim.AdditiveBasePoseAnimationId,
            ]);
        foreach (var animationId in animationIds)
        {
            if ((uint)animationId >= (uint)animationSet.Animations.Length ||
                animationSet.Animations[animationId].SkeletonId != poseProfile.SkeletonId ||
                !library.ClipNames.TryGetValue(animationId, out var clipName) ||
                !library.Library.HasAnimation(clipName))
            {
                throw new InvalidOperationException(
                    $"P4 graph library is missing exact animation ID: {animationId}");
            }
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
        GraphProfileLayout layout,
        AlsPoseAnimationProfile? poseProfile)
    {
        var allocated = new List<IDisposable>();
        try
        {
            var stateNames = new[]
            {
                AllocateHandle(allocated, new StringName("Grounded")),
                AllocateHandle(allocated, new StringName("JumpStart")),
                AllocateHandle(allocated, new StringName("FallLoop")),
                AllocateHandle(allocated, new StringName("LandRecovery")),
            };
            var stanceNames = new[]
            {
                AllocateHandle(allocated, new StringName("Standing")),
                AllocateHandle(allocated, new StringName("Crouching")),
            };
            var animationPlayerPath = AllocateHandle(
                allocated, new NodePath("../AlsAnimationPlayer"));
            var topPlaybackPath = AllocateHandle(
                allocated, new StringName("parameters/playback"));
            var groundedPlaybackPath = AllocateHandle(
                allocated, new StringName("parameters/Grounded/playback"));
            var groundedStanding = CreateParameterSet(
                "Grounded/Standing",
                true,
                true,
                layout.StandingBounds,
                layout.LeanBounds,
                allocated);
            var groundedCrouching = CreateParameterSet(
                "Grounded/Crouching",
                true,
                true,
                layout.CrouchingBounds,
                layout.LeanBounds,
                allocated);
            var jumpStart = CreateParameterSet(
                "JumpStart", false, false, default, layout.LeanBounds, allocated);
            var fallLoop = CreateParameterSet(
                "FallLoop", false, false, default, layout.LeanBounds, allocated);
            var landRecovery = CreateParameterSet(
                "LandRecovery", false, false, default, layout.LeanBounds, allocated);
            var p4 = poseProfile is null ? null : CreateP4Handles(poseProfile, allocated);
            return new AlsLocomotionGraphHandles(
                animationPlayerPath,
                topPlaybackPath,
                groundedPlaybackPath,
                stateNames,
                stanceNames,
                layout.StandingGaitRadii,
                layout.CrouchingRadius,
                groundedStanding,
                groundedCrouching,
                jumpStart,
                fallLoop,
                landRecovery,
                p4);
        }
        catch
        {
            for (var index = allocated.Count - 1; index >= 0; index--)
            {
                allocated[index].Dispose();
            }
            throw;
        }
    }

    private static AlsLocomotionGraphParameterSet CreateParameterSet(
        string prefix,
        bool hasBlendPosition,
        bool hasPhasePath,
        (Vector2 Minimum, Vector2 Maximum) blendBounds,
        (Vector2 Minimum, Vector2 Maximum) leanBounds,
        List<IDisposable> allocated) => new(
            hasBlendPosition
                ? AllocateHandle(
                    allocated,
                    new StringName($"parameters/{prefix}/Locomotion/blend_position"))
                : null,
            AllocateHandle(allocated, new StringName($"parameters/{prefix}/Lean/blend_position")),
            AllocateHandle(allocated, new StringName($"parameters/{prefix}/LeanAdd/add_amount")),
            AllocateHandle(allocated, new StringName($"parameters/{prefix}/Scale/scale")),
            hasPhasePath
                ? AllocateHandle(
                    allocated,
                    new StringName($"parameters/{prefix}/Seek/seek_request"))
                : null,
            blendBounds.Minimum,
            blendBounds.Maximum,
            leanBounds.Minimum,
            leanBounds.Maximum);

    private static AlsP4GraphHandles CreateP4Handles(
        AlsPoseAnimationProfile profile,
        List<IDisposable> allocated)
    {
        var turnSelections = new Dictionary<int, float>(profile.Turns.Length);
        for (var index = 0; index < profile.Turns.Length; index++)
        {
            if (!turnSelections.TryAdd(profile.Turns[index].AnimationId, index))
            {
                throw new InvalidOperationException(
                    $"P4 graph contains duplicate Turn animation ID: {profile.Turns[index].AnimationId}");
            }
        }
        var rotateSelections = new Dictionary<int, float>(profile.Rotates.Length);
        for (var index = 0; index < profile.Rotates.Length; index++)
        {
            if (!rotateSelections.TryAdd(profile.Rotates[index].AnimationId, index))
            {
                throw new InvalidOperationException(
                    $"P4 graph contains duplicate Rotate animation ID: {profile.Rotates[index].AnimationId}");
            }
        }

        return new AlsP4GraphHandles(
            Handle("P4Turn"),
            Handle("P4Rotate"),
            Handle("P4AimDown"),
            Handle("P4AimForward"),
            Handle("P4AimUp"),
            Handle("parameters/P4Turn/Select/blend_position"),
            Handle("parameters/P4Turn/Scale/scale"),
            Handle("parameters/P4Turn/Seek/seek_request"),
            Handle("parameters/P4Rotate/Select/blend_position"),
            Handle("parameters/P4Rotate/Scale/scale"),
            Handle("parameters/P4Rotate/Seek/seek_request"),
            Handle("parameters/P4AimDown/Seek/seek_request"),
            Handle("parameters/P4AimDown/Blend/blend_amount"),
            Handle("parameters/P4AimForward/Seek/seek_request"),
            Handle("parameters/P4AimForward/Blend/blend_amount"),
            Handle("parameters/P4AimUp/Seek/seek_request"),
            Handle("parameters/P4AimUp/Blend/blend_amount"),
            turnSelections,
            rotateSelections);

        StringName Handle(string value) =>
            AllocateHandle(allocated, new StringName(value));
    }

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
        var outerRadius = samples
            .Select(sample => MathF.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y)))
            .Max();
        return BuildLayeredBranch(
            library,
            samples,
            leanSamples,
            true,
            RadialBounds(outerRadius),
            ownedResources);
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

    private static AnimationNodeBlendTree BuildP4ActionBranch(
        AlsAnimationLibraryBuildResult library,
        IReadOnlyList<int> animationIds,
        List<IDisposable> ownedResources)
    {
        if (animationIds.Count < 2)
        {
            throw new InvalidOperationException("P4 action bank requires at least two clips.");
        }

        var tree = Own(ownedResources, new AnimationNodeBlendTree());
        var select = Own(ownedResources, new AnimationNodeBlendSpace1D
        {
            MinSpace = 0f,
            MaxSpace = animationIds.Count - 1,
            Snap = 1f,
            BlendMode = AnimationNodeBlendSpace1D.BlendModeEnum.Discrete,
            SyncMode = AnimationNodeBlendSpace1D.SyncModeEnum.None,
            ValueLabel = "AnimationId",
        });
        for (var index = 0; index < animationIds.Count; index++)
        {
            var animationId = animationIds[index];
            var clip = CreateAnimationNode(library, animationId, false, ownedResources);
            using var pointName = new StringName($"id_{animationId}");
            select.AddBlendPoint(clip, index, -1, pointName);
        }

        var scale = Own(ownedResources, new AnimationNodeTimeScale());
        var seek = Own(ownedResources, new AnimationNodeTimeSeek());
        using var selectName = new StringName("Select");
        using var scaleName = new StringName("Scale");
        using var seekName = new StringName("Seek");
        using var outputName = new StringName("output");
        tree.AddNode(selectName, select);
        tree.AddNode(scaleName, scale);
        tree.AddNode(seekName, seek);
        tree.ConnectNode(scaleName, 0, selectName);
        tree.ConnectNode(seekName, 0, scaleName);
        tree.ConnectNode(outputName, 0, seekName);
        return tree;
    }

    private static AnimationNodeBlendTree BuildP4AimBranch(
        AlsAnimationLibraryBuildResult library,
        int baseAnimationId,
        int aimAnimationId,
        List<IDisposable> ownedResources)
    {
        var tree = Own(ownedResources, new AnimationNodeBlendTree());
        var basePose = CreateAnimationNode(library, baseAnimationId, true, ownedResources);
        var aim = CreateAnimationNode(library, aimAnimationId, true, ownedResources);
        var blend = Own(ownedResources, new AnimationNodeBlend2());
        var seek = Own(ownedResources, new AnimationNodeTimeSeek());
        using var baseName = new StringName("Base");
        using var aimName = new StringName("Aim");
        using var blendName = new StringName("Blend");
        using var seekName = new StringName("Seek");
        using var outputName = new StringName("output");
        tree.AddNode(baseName, basePose);
        tree.AddNode(aimName, aim);
        tree.AddNode(blendName, blend);
        tree.AddNode(seekName, seek);
        tree.ConnectNode(blendName, 0, baseName);
        tree.ConnectNode(blendName, 1, aimName);
        tree.ConnectNode(seekName, 0, blendName);
        tree.ConnectNode(outputName, 0, seekName);
        return tree;
    }

    private static AnimationNodeBlendTree BuildLayeredBranch(
        AlsAnimationLibraryBuildResult library,
        IReadOnlyList<AlsLocomotionAnimationSample> locomotionSamples,
        IReadOnlyList<AlsLocomotionAnimationSample> leanSamples,
        bool loop,
        (Vector2 Minimum, Vector2 Maximum)? locomotionBounds,
        List<IDisposable> ownedResources)
    {
        var tree = Own(ownedResources, new AnimationNodeBlendTree());
        var locomotion = BuildBlendSpace(
            library, locomotionSamples, loop, locomotionBounds, ownedResources);
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
        var lean = BuildBlendSpace(library, leanSamples, true, null, ownedResources);
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
        (Vector2 Minimum, Vector2 Maximum)? explicitBounds,
        List<IDisposable> ownedResources)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("P3 graph blend space cannot be empty.");
        }

        var bounds = explicitBounds ?? Bounds(samples, false);
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

    private static GraphProfileLayout ValidateProfileLayout(
        AlsLocomotionAnimationProfile profile)
    {
        ValidateSampleGrid("standing", profile.StandingSamples);
        ValidateSampleGrid("crouching", profile.CrouchingSamples);
        ValidateSampleGrid("lean", profile.LeanAdditiveSamples);

        var standingGaitRadii = CalculateStandingGaitRadii(profile.StandingSamples);
        var crouchingRadius = CalculateCrouchingRadius(profile.CrouchingSamples);
        return new GraphProfileLayout(
            standingGaitRadii,
            crouchingRadius,
            RadialBounds(standingGaitRadii[(int)AlsGait.Sprinting]),
            RadialBounds(crouchingRadius),
            Bounds(profile.LeanAdditiveSamples, false));
    }

    private static void ValidateSampleGrid(
        string label,
        IReadOnlyList<AlsLocomotionAnimationSample> samples)
    {
        if (samples.Count < 3)
        {
            throw new InvalidOperationException(
                $"P3 {label} blend grid must contain at least three samples.");
        }

        var minimumX = float.PositiveInfinity;
        var minimumY = float.PositiveInfinity;
        var maximumX = float.NegativeInfinity;
        var maximumY = float.NegativeInfinity;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (!float.IsFinite(sample.X) ||
                !float.IsFinite(sample.Y) ||
                !float.IsFinite(sample.RateScale) ||
                sample.RateScale <= 0f)
            {
                throw new InvalidOperationException(
                    $"P3 {label} blend grid contains an invalid sample at index {index}.");
            }

            for (var previous = 0; previous < index; previous++)
            {
                if (MathF.Abs(sample.X - samples[previous].X) <= CoordinateTolerance &&
                    MathF.Abs(sample.Y - samples[previous].Y) <= CoordinateTolerance)
                {
                    throw new InvalidOperationException(
                        $"P3 {label} blend grid contains duplicate coordinates at " +
                        $"indices {previous} and {index}.");
                }
            }

            minimumX = MathF.Min(minimumX, sample.X);
            minimumY = MathF.Min(minimumY, sample.Y);
            maximumX = MathF.Max(maximumX, sample.X);
            maximumY = MathF.Max(maximumY, sample.Y);
        }

        if (maximumX - minimumX <= CoordinateTolerance ||
            maximumY - minimumY <= CoordinateTolerance)
        {
            throw new InvalidOperationException(
                $"P3 {label} blend grid must span both coordinate axes.");
        }

        var coordinateScale = (double)MathF.Max(
            maximumX - minimumX,
            maximumY - minimumY);
        var areaTolerance = coordinateScale * coordinateScale * CoordinateTolerance;
        for (var first = 0; first < samples.Count - 2; first++)
        {
            for (var second = first + 1; second < samples.Count - 1; second++)
            {
                var firstX = (double)samples[second].X - samples[first].X;
                var firstY = (double)samples[second].Y - samples[first].Y;
                for (var third = second + 1; third < samples.Count; third++)
                {
                    var secondX = (double)samples[third].X - samples[first].X;
                    var secondY = (double)samples[third].Y - samples[first].Y;
                    var area = Math.Abs((firstX * secondY) - (firstY * secondX));
                    if (area > areaTolerance)
                    {
                        return;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"P3 {label} blend grid must contain a non-collinear sample triangle.");
    }

    private static float[] CalculateStandingGaitRadii(
        IReadOnlyList<AlsLocomotionAnimationSample> samples)
    {
        var sortedRadii = samples
            .Select(sample => MathF.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y)))
            .OrderBy(radius => radius)
            .ToArray();
        if (sortedRadii[0] <= RingTolerance)
        {
            throw new InvalidOperationException(
                "P3 standing locomotion sample cannot occupy the idle origin.");
        }

        var clusters = new List<List<float>>(3);
        foreach (var radius in sortedRadii)
        {
            if (clusters.Count == 0 ||
                radius - clusters[^1][0] > RingTolerance)
            {
                clusters.Add(new List<float> { radius });
            }
            else
            {
                clusters[^1].Add(radius);
            }
        }
        if (clusters.Count != 3)
        {
            throw new InvalidOperationException(
                $"P3 standing locomotion profile must contain exactly three gait rings: " +
                $"actual={clusters.Count}");
        }

        return clusters
            .Select(cluster => (float)cluster.Average(value => (double)value))
            .ToArray();
    }

    private static float CalculateCrouchingRadius(
        IReadOnlyList<AlsLocomotionAnimationSample> samples)
    {
        var sortedRadii = samples
            .Select(sample => MathF.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y)))
            .OrderBy(radius => radius)
            .ToArray();
        if (sortedRadii[0] <= RingTolerance ||
            sortedRadii[^1] - sortedRadii[0] > RingTolerance)
        {
            throw new InvalidOperationException(
                "P3 crouching locomotion profile must contain one nonzero outer ring.");
        }
        return (float)sortedRadii.Average(value => (double)value);
    }

    private static (Vector2 Minimum, Vector2 Maximum) RadialBounds(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            throw new InvalidOperationException("P3 locomotion ring radius must be positive and finite.");
        }
        return (new Vector2(-radius, -radius), new Vector2(radius, radius));
    }

    private static T Own<T>(List<IDisposable> resources, T resource)
        where T : GodotObject, IDisposable
    {
        resources.Add(resource);
        return resource;
    }

    private static T AllocateHandle<T>(List<IDisposable> allocated, T handle)
        where T : IDisposable
    {
        allocated.Add(handle);
        return handle;
    }

    private readonly record struct GraphProfileLayout(
        float[] StandingGaitRadii,
        float CrouchingRadius,
        (Vector2 Minimum, Vector2 Maximum) StandingBounds,
        (Vector2 Minimum, Vector2 Maximum) CrouchingBounds,
        (Vector2 Minimum, Vector2 Maximum) LeanBounds);
}
