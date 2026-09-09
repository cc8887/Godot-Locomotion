using System.Collections.ObjectModel;
using Godot;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Transitions;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public readonly record struct AlsP5aAnimationLibraryStamp(
    string AnimationSetDefinitionDigest,
    ulong LayoutDigest,
    ulong BindingDigest,
    ulong GraphDigest);

public readonly record struct AlsP5aAnimationResourceDescriptor(
    int AnimationId,
    StringName ClipName,
    float PlayLengthSeconds,
    byte NormalizedTrack);

public sealed class AlsAnimationLibraryBuildResult : IDisposable
{
    private readonly AnimationLibrary _ownedLibrary;
    private readonly StringName _ownedLibraryName;
    private readonly OwnedStringNameTable _ownedClipNames;
    private readonly OwnedP5aAnimationLibraryData? _p5a;
    private int _disposed;

    internal AlsAnimationLibraryBuildResult(
        Node root,
        Skeleton3D skeleton,
        AnimationPlayer player,
        AnimationLibrary library,
        StringName libraryName,
        OwnedStringNameTable clipNames,
        OwnedP5aAnimationLibraryData? p5a = null)
    {
        Root = root;
        Skeleton = skeleton;
        Player = player;
        Library = library;
        ClipNames = clipNames.View;
        _p5a = p5a;
        _ownedLibrary = library;
        _ownedLibraryName = libraryName;
        _ownedClipNames = clipNames;
    }

    public Node Root { get; }

    public Skeleton3D Skeleton { get; }

    public AnimationPlayer Player { get; }

    public AnimationLibrary Library { get; }

    public IReadOnlyDictionary<int, StringName> ClipNames { get; }

    public AlsP5aAnimationLibraryStamp Stamp => RequireP5a().Stamp;

    public IReadOnlyDictionary<int, AlsP5aAnimationResourceDescriptor> Resources =>
        RequireP5a().Resources;

    public int PhysicalRootBoneId => RequireP5a().PhysicalRootBoneId;

    public NodePath PhysicalRootPath => RequireP5a().PhysicalRootPath;

    public IReadOnlyList<NodePath> ActionFilterPaths => RequireP5a().ActionFilterPaths;

    internal bool IsP5a => _p5a is not null;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (GodotObject.IsInstanceValid(Root))
            {
                Root.Free();
            }
        }
        finally
        {
            try
            {
                _ownedLibrary.Dispose();
            }
            finally
            {
                try
                {
                    _ownedLibraryName.Dispose();
                }
                finally
                {
                    try
                    {
                        _p5a?.Dispose();
                    }
                    finally
                    {
                        _ownedClipNames.Dispose();
                    }
                }
            }
        }
    }

    internal void ValidateLiveP5aSkeleton()
    {
        var p5a = RequireP5a();
        if (!GodotObject.IsInstanceValid(Root) || !GodotObject.IsInstanceValid(Skeleton) ||
            !ReferenceEquals(AlsImportedResourceAuditor.FindFirst<Skeleton3D>(Root), Skeleton) ||
            Skeleton.GetBoneCount() != p5a.ExpectedPhysicalParents.Length)
        {
            throw new InvalidOperationException("The P5A library target skeleton identity changed.");
        }

        for (var index = 0; index < p5a.ExpectedPhysicalParents.Length; index++)
        {
            if (Skeleton.GetBoneParent(index) != p5a.ExpectedPhysicalParents[index] ||
                !string.Equals(Skeleton.GetBoneName(index).ToString(),
                    p5a.ExpectedPhysicalNames[index], StringComparison.OrdinalIgnoreCase) ||
                Skeleton.GetBoneRest(index) != p5a.ExpectedBoneRests[index])
            {
                throw new InvalidOperationException("The P5A library target skeleton hierarchy changed.");
            }
        }

        using var actualRootPath = CreateBonePath(Root, Skeleton, p5a.PhysicalRootBoneId);
        if (!string.Equals(actualRootPath.ToString(), p5a.PhysicalRootPath.ToString(),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The P5A library physical root path changed.");
        }

        var actualFilters = BuildActionFilterPaths(
            Root, Skeleton, p5a.PhysicalRootBoneId, p5a.ExpectedPhysicalParents);
        try
        {
            if (actualFilters.Count != p5a.ActionFilterPaths.Count ||
                !actualFilters.Select(value => value.ToString()).SequenceEqual(
                    p5a.ActionFilterPaths.Select(value => value.ToString())))
            {
                throw new InvalidOperationException("The P5A library Action filter closure changed.");
            }
        }
        finally
        {
            foreach (var path in actualFilters) path.Dispose();
        }
    }

    private OwnedP5aAnimationLibraryData RequireP5a() => _p5a ??
        throw new InvalidOperationException("The animation library has no P5A build stamp.");

    private static NodePath CreateBonePath(Node root, Skeleton3D skeleton, int boneId)
    {
        using var skeletonPath = root.GetPathTo(skeleton);
        return new NodePath($"{skeletonPath}:{skeleton.GetBoneName(boneId)}");
    }

    private static List<NodePath> BuildActionFilterPaths(
        Node root,
        Skeleton3D skeleton,
        int physicalRootBoneId,
        IReadOnlyList<int> parents)
    {
        using var skeletonPath = root.GetPathTo(skeleton);
        var paths = new List<NodePath>(parents.Count - 1);
        for (var boneId = 0; boneId < parents.Count; boneId++)
        {
            if (boneId == physicalRootBoneId ||
                !HasPhysicalAncestor(parents, boneId, physicalRootBoneId))
            {
                continue;
            }
            paths.Add(new NodePath($"{skeletonPath}:{skeleton.GetBoneName(boneId)}"));
        }
        return paths;
    }

    private static bool HasPhysicalAncestor(
        IReadOnlyList<int> parents,
        int boneId,
        int ancestor)
    {
        for (var current = parents[boneId]; current >= 0; current = parents[current])
        {
            if (current == ancestor) return true;
        }
        return false;
    }
}

internal sealed class OwnedP5aAnimationLibraryData : IDisposable
{
    private readonly NodePath _physicalRootPath;
    private readonly NodePath[] _actionFilterPaths;
    private readonly IReadOnlyList<NodePath> _actionFilterView;
    private int _disposed;

    public OwnedP5aAnimationLibraryData(
        AlsP5aAnimationLibraryStamp stamp,
        IReadOnlyDictionary<int, AlsP5aAnimationResourceDescriptor> resources,
        int physicalRootBoneId,
        NodePath physicalRootPath,
        NodePath[] actionFilterPaths,
        int[] expectedPhysicalParents,
        string[] expectedPhysicalNames,
        Transform3D[] expectedBoneRests)
    {
        Stamp = stamp;
        Resources = resources;
        PhysicalRootBoneId = physicalRootBoneId;
        _physicalRootPath = physicalRootPath;
        _actionFilterPaths = actionFilterPaths;
        _actionFilterView = Array.AsReadOnly(actionFilterPaths);
        ExpectedPhysicalParents = expectedPhysicalParents;
        ExpectedPhysicalNames = expectedPhysicalNames;
        ExpectedBoneRests = expectedBoneRests;
    }

    public AlsP5aAnimationLibraryStamp Stamp { get; }
    public IReadOnlyDictionary<int, AlsP5aAnimationResourceDescriptor> Resources { get; }
    public int PhysicalRootBoneId { get; }
    public NodePath PhysicalRootPath => _physicalRootPath;
    public IReadOnlyList<NodePath> ActionFilterPaths => _actionFilterView;
    public int[] ExpectedPhysicalParents { get; }
    public string[] ExpectedPhysicalNames { get; }
    public Transform3D[] ExpectedBoneRests { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _physicalRootPath.Dispose();
        foreach (var path in _actionFilterPaths) path.Dispose();
    }
}

public static class AlsAnimationLibraryBuilder
{
    private const double AnimationLengthTolerance = 1.0 / 30.0;
    private const string ImportedAnimationName = "Unreal Take";
    private const string LibraryName = "als";

    public static AlsAnimationLibraryBuildResult Build(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(profile);
        return BuildInternal(
            animationSet,
            profile.SkeletonId,
            profile.MannequinMeshId,
            profile.AllAnimationIds,
            normalizedTrackDomainAnimationIds: null,
            p5aStamp: null,
            rootMotionExtractionLogicalBoneId: -1,
            rootMotionExtractionPhysicalBoneId: -1);
    }

    public static AlsAnimationLibraryBuildResult Build(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile)
    {
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(poseProfile);
        var animationIds = BuildP4AnimationClosure(animationSet, profile, poseProfile);
        HashSet<int> normalizedTrackDomainAnimationIds =
        [
            poseProfile.Aim.AdditiveBasePoseAnimationId,
            poseProfile.Aim.DownAnimationId,
            poseProfile.Aim.ForwardAnimationId,
            poseProfile.Aim.UpAnimationId,
        ];
        return BuildInternal(
            animationSet,
            profile.SkeletonId,
            profile.MannequinMeshId,
            animationIds,
            normalizedTrackDomainAnimationIds,
            p5aStamp: null,
            rootMotionExtractionLogicalBoneId: -1,
            rootMotionExtractionPhysicalBoneId: -1);
    }

    public static AlsAnimationLibraryBuildResult BuildP5a(
        AlsAnimationSetDefinition animationSet,
        AlsP5CoreRuntimeBindingSnapshot coreBindings)
    {
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(coreBindings);
        if (!string.Equals(animationSet.DefinitionDigest,
                coreBindings.AnimationSetDefinitionDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The P5A animation-set definition digest does not match the binding snapshot.");
        }
        if (!string.Equals(
                AlsAnimationSetPayload.ComputeDefinitionDigest(animationSet),
                animationSet.DefinitionDigest,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The P5A animation-set definition digest is stale.");
        }

        var graph = coreBindings.CreateGraphBuildView();
        var core = coreBindings.CreateCoreView();
        var occurrence = coreBindings.CreateOccurrenceLayoutView();
        AlsP5OccurrenceLayoutContract.Validate(
            occurrence.Version, occurrence.Digest, occurrence.Entries);
        var animationIds = BuildP5aAnimationClosure(animationSet, in graph, in core);
        var normalized = ValidateP5aGraphInputs(animationSet, in graph, animationIds);
        var stamp = new AlsP5aAnimationLibraryStamp(
            coreBindings.AnimationSetDefinitionDigest,
            coreBindings.LayoutDigest,
            coreBindings.Digest,
            coreBindings.GraphDigest);
        return BuildInternal(
            animationSet,
            graph.SkeletonId,
            graph.MannequinMeshId,
            animationIds,
            normalized,
            stamp,
            graph.RootMotionExtractionLogicalBoneId,
            graph.RootMotionExtractionPhysicalBoneId);
    }

    private static AlsAnimationLibraryBuildResult BuildInternal(
        AlsAnimationSetDefinition animationSet,
        int skeletonId,
        int mannequinMeshId,
        IReadOnlyList<int> animationIds,
        IReadOnlySet<int>? normalizedTrackDomainAnimationIds,
        AlsP5aAnimationLibraryStamp? p5aStamp,
        int rootMotionExtractionLogicalBoneId,
        int rootMotionExtractionPhysicalBoneId)
    {
        using var importedAnimationName = new StringName(ImportedAnimationName);
        var mannequin = GetMannequin(animationSet, skeletonId, mannequinMeshId);
        using var targetScene = LoadScene(mannequin.ResourcePath, mannequin.Name, "target");
        var targetRoot = targetScene.Instantiate();
        AnimationLibrary? library = null;
        StringName? libraryName = null;
        OwnedStringNameTable? names = null;
        OwnedP5aAnimationLibraryData? p5a = null;
        try
        {
            var targetSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException(
                    $"Target scene has no Skeleton3D: {mannequin.Name}");
            var skeletonDefinition = animationSet.Skeletons[skeletonId];
            AlsAnimationBinder.ValidateTargetSkeleton(
                targetSkeleton, skeletonDefinition, mannequin.Name);
            library = new AnimationLibrary();
            libraryName = new StringName(LibraryName);
            names = new OwnedStringNameTable(animationIds.Count);
            foreach (var animationId in animationIds)
            {
                var clip = GetClip(animationSet, animationId, skeletonId);
                using var sourceScene = LoadScene(clip.ResourcePath, clip.Name, "animation");
                using var sourceRoot = new OwnedNode(sourceScene.Instantiate());
                var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot.Value)
                    ?? throw new InvalidOperationException(
                        $"Animation scene has no AnimationPlayer: {clip.Name}");
                if (!sourcePlayer.HasAnimation(importedAnimationName))
                {
                    throw new InvalidOperationException(
                        $"Animation scene does not contain '{ImportedAnimationName}': {clip.Name}");
                }

                using var sourceAnimation = sourcePlayer.GetAnimation(importedAnimationName);
                if (sourceAnimation is null || sourceAnimation.GetTrackCount() == 0)
                {
                    throw new InvalidOperationException($"Imported animation is empty: {clip.Name}");
                }
                if (Math.Abs(sourceAnimation.Length - clip.PlayLength) >
                    AnimationLengthTolerance + 1e-6)
                {
                    throw new InvalidOperationException(
                        $"Animation length mismatch for {clip.Name}: " +
                        $"expected={clip.PlayLength} actual={sourceAnimation.Length}");
                }

                using var boundAnimation = (Godot.Animation)sourceAnimation.Duplicate(true);
                if (normalizedTrackDomainAnimationIds?.Contains(animationId) == true)
                {
                    NormalizeTrackDomains(boundAnimation);
                }
                AlsAnimationBinder.RewriteTrackPaths(
                    targetRoot, targetSkeleton, boundAnimation, clip.Name);
                StringName? animationName = new($"clip_{animationId}");
                try
                {
                    ThrowIfError(
                        library.AddAnimation(animationName, boundAnimation),
                        "add animation library clip",
                        clip.Name);
                    names.Add(animationId, animationName);
                    animationName = null;
                }
                finally
                {
                    animationName?.Dispose();
                }
            }

            var player = new AnimationPlayer
            {
                Name = "AlsAnimationPlayer",
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
            };
            using (var rootPath = new NodePath(".."))
            {
                player.RootNode = rootPath;
            }
            targetRoot.AddChild(player);
            ThrowIfError(
                player.AddAnimationLibrary(libraryName, library),
                "add animation library",
                mannequin.Name);

            if (p5aStamp.HasValue)
            {
                var resources = new Dictionary<int, AlsP5aAnimationResourceDescriptor>(
                    animationIds.Count);
                foreach (var animationId in animationIds)
                {
                    resources.Add(animationId, new AlsP5aAnimationResourceDescriptor(
                        animationId,
                        names.View[animationId],
                        animationSet.Animations[animationId].PlayLength,
                        normalizedTrackDomainAnimationIds!.Contains(animationId) ? (byte)1 : (byte)0));
                }
                var parents = skeletonDefinition.PhysicalBones
                    .Select(value => value.ParentPhysicalId).ToArray();
                var physicalRootPath = CreateBonePath(
                    targetRoot, targetSkeleton, rootMotionExtractionPhysicalBoneId);
                var actionFilterPaths = BuildP5aActionFilterPaths(
                    targetRoot,
                    targetSkeleton,
                    skeletonDefinition,
                    rootMotionExtractionLogicalBoneId,
                    rootMotionExtractionPhysicalBoneId,
                    mannequin.Name);
                p5a = new OwnedP5aAnimationLibraryData(
                    p5aStamp.Value,
                    new ReadOnlyDictionary<int, AlsP5aAnimationResourceDescriptor>(resources),
                    rootMotionExtractionPhysicalBoneId,
                    physicalRootPath,
                    actionFilterPaths,
                    parents,
                    skeletonDefinition.PhysicalBones.Select(value => value.Name).ToArray(),
                    Enumerable.Range(0, targetSkeleton.GetBoneCount())
                        .Select(targetSkeleton.GetBoneRest).ToArray());
            }

            var result = new AlsAnimationLibraryBuildResult(
                targetRoot,
                targetSkeleton,
                player,
                library,
                libraryName,
                names,
                p5a);
            library = null;
            libraryName = null;
            names = null;
            p5a = null;
            return result;
        }
        catch
        {
            p5a?.Dispose();
            ReleasePartialBuild(targetRoot, library, libraryName, names);
            throw;
        }
    }

    private static void NormalizeTrackDomains(Godot.Animation animation)
    {
        for (var trackIndex = 0; trackIndex < animation.GetTrackCount(); trackIndex++)
        {
            var keyCount = animation.TrackGetKeyCount(trackIndex);
            if (keyCount == 0 ||
                animation.TrackGetKeyTime(trackIndex, keyCount - 1) <= animation.Length)
            {
                continue;
            }

            var trackType = animation.TrackGetType(trackIndex);
            var endpointPosition = trackType == Godot.Animation.TrackType.Position3D
                ? animation.PositionTrackInterpolate(trackIndex, animation.Length)
                : default;
            var endpointRotation = trackType == Godot.Animation.TrackType.Rotation3D
                ? animation.RotationTrackInterpolate(trackIndex, animation.Length)
                : default;
            var endpointScale = trackType == Godot.Animation.TrackType.Scale3D
                ? animation.ScaleTrackInterpolate(trackIndex, animation.Length)
                : default;
            while (animation.TrackGetKeyCount(trackIndex) > 0 &&
                animation.TrackGetKeyTime(
                    trackIndex,
                    animation.TrackGetKeyCount(trackIndex) - 1) > animation.Length)
            {
                animation.TrackRemoveKey(
                    trackIndex,
                    animation.TrackGetKeyCount(trackIndex) - 1);
            }
            var remainingKeyCount = animation.TrackGetKeyCount(trackIndex);
            if (remainingKeyCount > 0 &&
                animation.TrackGetKeyTime(trackIndex, remainingKeyCount - 1) == animation.Length)
            {
                continue;
            }
            switch (trackType)
            {
                case Godot.Animation.TrackType.Position3D:
                    animation.PositionTrackInsertKey(
                        trackIndex, animation.Length, endpointPosition);
                    break;
                case Godot.Animation.TrackType.Rotation3D:
                    animation.RotationTrackInsertKey(
                        trackIndex, animation.Length, endpointRotation);
                    break;
                case Godot.Animation.TrackType.Scale3D:
                    animation.ScaleTrackInsertKey(
                        trackIndex, animation.Length, endpointScale);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Cannot normalize unsupported animation track type: {trackType}");
            }
        }
    }

    private static int[] BuildP5aAnimationClosure(
        AlsAnimationSetDefinition animationSet,
        in AlsP5GraphBuildView graph,
        in AlsP5RuntimeBindings core)
    {
        var closure = new HashSet<int>();
        var skeletonId = graph.SkeletonId;
        foreach (var animationId in graph.AllAnimationIds)
        {
            ValidateClosureAnimation(animationSet, animationId, graph.SkeletonId, "P5A graph");
            if (!closure.Add(animationId))
            {
                throw new InvalidOperationException(
                    $"P5A graph contains duplicate animation ID: {animationId}");
            }
        }

        var requiredGraphIds = new List<int>
        {
            graph.StandingIdleAnimationId,
            graph.CrouchingIdleAnimationId,
            graph.JumpStartAnimationId,
            graph.FallLoopAnimationId,
            graph.LandAnimationId,
            graph.LeanAdditiveBaseAnimationId,
            graph.Aim.DownAnimationId,
            graph.Aim.ForwardAnimationId,
            graph.Aim.UpAnimationId,
            graph.Aim.AdditiveBasePoseAnimationId,
        };
        requiredGraphIds.AddRange(graph.StandingSamples.ToArray().Select(value => value.AnimationId));
        requiredGraphIds.AddRange(graph.CrouchingSamples.ToArray().Select(value => value.AnimationId));
        requiredGraphIds.AddRange(graph.LeanSamples.ToArray().Select(value => value.AnimationId));
        requiredGraphIds.AddRange(graph.Turns.ToArray().Select(value => value.AnimationId));
        requiredGraphIds.AddRange(graph.Rotates.ToArray().Select(value => value.AnimationId));
        foreach (var animationId in requiredGraphIds)
        {
            ValidateClosureAnimation(animationSet, animationId, graph.SkeletonId, "P5A graph");
            closure.Add(animationId);
        }

        AddTransition(core.DynamicTransition.StandingLeft);
        AddTransition(core.DynamicTransition.StandingRight);
        AddTransition(core.DynamicTransition.CrouchingLeft);
        AddTransition(core.DynamicTransition.CrouchingRight);
        foreach (ref readonly var member in core.SyncMembers)
        {
            AddRuntimeAnimation(member.AnimationId, "Sync");
        }
        foreach (ref readonly var segment in core.ActionSegments)
        {
            AddRuntimeAnimation(segment.AnimationId, "Action Sequence");
        }

        return closure.OrderBy(value => value).ToArray();

        void AddTransition(AlsDynamicTransitionClipBinding transition)
        {
            AddRuntimeAnimation(transition.AnimationId, "Transition");
            AddRuntimeAnimation(transition.AdditiveBaseAnimationId, "Transition additive base");
        }

        void AddRuntimeAnimation(int animationId, string label)
        {
            ValidateClosureAnimation(animationSet, animationId, skeletonId, label);
            closure.Add(animationId);
        }
    }

    private static HashSet<int> ValidateP5aGraphInputs(
        AlsAnimationSetDefinition animationSet,
        in AlsP5GraphBuildView graph,
        IReadOnlyCollection<int> animationIds)
    {
        if ((uint)graph.SkeletonId >= (uint)animationSet.Skeletons.Length ||
            (uint)graph.MannequinMeshId >= (uint)animationSet.SkeletalMeshes.Length)
        {
            throw new InvalidOperationException("The P5A graph skeleton identity is invalid.");
        }
        var skeleton = animationSet.Skeletons[graph.SkeletonId];
        if (skeleton.RequiredBones.Root != graph.RootMotionExtractionLogicalBoneId ||
            (uint)graph.RootMotionExtractionLogicalBoneId >= (uint)skeleton.LogicalToPhysical.Length ||
            skeleton.LogicalToPhysical[graph.RootMotionExtractionLogicalBoneId] !=
                graph.RootMotionExtractionPhysicalBoneId ||
            (uint)graph.RootMotionExtractionPhysicalBoneId >= (uint)skeleton.PhysicalBones.Length)
        {
            throw new InvalidOperationException("The P5A graph root mapping is invalid.");
        }

        ValidateLogicalMasks(skeleton, in graph);
        var normalized = graph.NormalizedAnimationIds.ToArray().ToHashSet();
        if (normalized.Count != graph.NormalizedAnimationIds.Length ||
            !normalized.SetEquals(new[]
            {
                graph.Aim.AdditiveBasePoseAnimationId,
                graph.Aim.DownAnimationId,
                graph.Aim.ForwardAnimationId,
                graph.Aim.UpAnimationId,
            }) ||
            normalized.Any(value => !animationIds.Contains(value)))
        {
            throw new InvalidOperationException("The P5A normalized-track subset is invalid.");
        }
        return normalized;
    }

    private static void ValidateLogicalMasks(
        AlsSkeletonDefinition skeleton,
        in AlsP5GraphBuildView graph)
    {
        var nextOffset = 0;
        foreach (ref readonly var header in graph.MaskHeaders)
        {
            if (!Enum.IsDefined(header.Kind) ||
                (uint)header.LogicalRootBoneId >= (uint)skeleton.LogicalBones.Length ||
                header.BoneOffset != nextOffset || header.BoneCount <= 0 ||
                header.BoneOffset > graph.LogicalBoneIds.Length - header.BoneCount)
            {
                throw new InvalidOperationException("A P5A logical mask header is invalid.");
            }
            var members = graph.LogicalBoneIds.Slice(header.BoneOffset, header.BoneCount);
            var seen = new HashSet<int>();
            foreach (var logicalBoneId in members)
            {
                if ((uint)logicalBoneId >= (uint)skeleton.LogicalBones.Length ||
                    !seen.Add(logicalBoneId) ||
                    !HasLogicalAncestor(skeleton.LogicalBones, logicalBoneId,
                        header.LogicalRootBoneId))
                {
                    throw new InvalidOperationException("A P5A logical mask member is invalid.");
                }
            }
            nextOffset += header.BoneCount;
        }
        if (nextOffset != graph.LogicalBoneIds.Length)
        {
            throw new InvalidOperationException("The P5A logical mask ranges are incomplete.");
        }
    }

    private static bool HasLogicalAncestor(
        IReadOnlyList<AlsBoneDefinition> bones,
        int boneId,
        int ancestor)
    {
        for (var current = boneId; current >= 0; current = bones[current].ParentLogicalId)
        {
            if (current == ancestor) return true;
        }
        return false;
    }

    private static void ValidateP5aTargetSkeleton(
        Skeleton3D target,
        AlsSkeletonDefinition skeleton,
        int logicalRootBoneId,
        int physicalRootBoneId,
        string assetName)
    {
        if ((uint)logicalRootBoneId >= (uint)skeleton.LogicalToPhysical.Length ||
            skeleton.RequiredBones.Root != logicalRootBoneId ||
            skeleton.LogicalToPhysical[logicalRootBoneId] != physicalRootBoneId ||
            (uint)logicalRootBoneId >= (uint)skeleton.LogicalBones.Length ||
            (uint)physicalRootBoneId >= (uint)skeleton.PhysicalBones.Length ||
            (uint)physicalRootBoneId >= (uint)skeleton.PhysicalToLogical.Length ||
            skeleton.LogicalBones[logicalRootBoneId].PhysicalId != physicalRootBoneId ||
            skeleton.PhysicalBones[physicalRootBoneId].LogicalId != logicalRootBoneId ||
            skeleton.PhysicalToLogical[physicalRootBoneId] != logicalRootBoneId)
        {
            throw new InvalidOperationException($"P5A root mapping is invalid for {assetName}.");
        }
        for (var boneId = 0; boneId < skeleton.PhysicalBones.Length; boneId++)
        {
            var expectedParent = skeleton.PhysicalBones[boneId].ParentPhysicalId;
            if (expectedParent >= boneId || expectedParent < -1 ||
                target.GetBoneParent(boneId) != expectedParent)
            {
                throw new InvalidOperationException(
                    $"P5A target skeleton hierarchy mismatch for {assetName}: bone={boneId}.");
            }
        }
        var actualRestHash = AlsImportedResourceAuditor.ComputeTargetRestPoseHash(target, skeleton);
        if (!string.Equals(actualRestHash, skeleton.TargetPhysicalRestPoseHash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"P5A target skeleton rest mapping mismatch for {assetName}: " +
                AlsImportedResourceAuditor.DescribeRestPoseDifferences(target, skeleton));
        }
    }

    internal static NodePath[] BuildP5aActionFilterPaths(
        Node root,
        Skeleton3D target,
        AlsSkeletonDefinition skeleton,
        int logicalRootBoneId,
        int physicalRootBoneId,
        string assetName)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(skeleton);
        ValidateP5aTargetSkeleton(
            target, skeleton, logicalRootBoneId, physicalRootBoneId, assetName);
        var parents = skeleton.PhysicalBones.Select(value => value.ParentPhysicalId).ToArray();
        return BuildActionFilterPaths(root, target, physicalRootBoneId, parents).ToArray();
    }

    private static NodePath CreateBonePath(Node root, Skeleton3D skeleton, int boneId)
    {
        using var skeletonPath = root.GetPathTo(skeleton);
        return new NodePath($"{skeletonPath}:{skeleton.GetBoneName(boneId)}");
    }

    private static IEnumerable<NodePath> BuildActionFilterPaths(
        Node root,
        Skeleton3D skeleton,
        int physicalRootBoneId,
        IReadOnlyList<int> parents)
    {
        using var skeletonPath = root.GetPathTo(skeleton);
        for (var boneId = 0; boneId < parents.Count; boneId++)
        {
            if (boneId == physicalRootBoneId ||
                !HasPhysicalAncestor(parents, boneId, physicalRootBoneId))
            {
                continue;
            }
            yield return new NodePath($"{skeletonPath}:{skeleton.GetBoneName(boneId)}");
        }
    }

    private static bool HasPhysicalAncestor(
        IReadOnlyList<int> parents,
        int boneId,
        int ancestor)
    {
        for (var current = parents[boneId]; current >= 0; current = parents[current])
        {
            if (current == ancestor) return true;
        }
        return false;
    }

    private static int[] BuildP4AnimationClosure(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile)
    {
        if ((uint)profile.SkeletonId >= (uint)animationSet.Skeletons.Length ||
            poseProfile.SkeletonId != profile.SkeletonId ||
            poseProfile.Turns.Length != 8 ||
            poseProfile.Rotates.Length != 4)
        {
            throw new InvalidOperationException(
                "P4 animation library profile skeleton or fixed slot count is invalid.");
        }

        var closure = new HashSet<int>();
        foreach (var animationId in profile.AllAnimationIds)
        {
            ValidateClosureAnimation(animationSet, animationId, profile.SkeletonId, "P3");
            if (!closure.Add(animationId))
            {
                throw new InvalidOperationException(
                    $"P3 animation library profile contains duplicate animation ID: {animationId}");
            }
        }

        var p4Ids = poseProfile.Turns.Select(value => value.AnimationId)
            .Concat(poseProfile.Rotates.Select(value => value.AnimationId))
            .Concat([
                poseProfile.Aim.DownAnimationId,
                poseProfile.Aim.ForwardAnimationId,
                poseProfile.Aim.UpAnimationId,
                poseProfile.Aim.AdditiveBasePoseAnimationId,
            ])
            .ToArray();
        var uniqueP4 = new HashSet<int>();
        foreach (var animationId in p4Ids)
        {
            ValidateClosureAnimation(animationSet, animationId, profile.SkeletonId, "P4");
            if (!uniqueP4.Add(animationId))
            {
                throw new InvalidOperationException(
                    $"P4 animation library profile contains duplicate semantic animation ID: {animationId}");
            }
            closure.Add(animationId);
        }

        return closure.OrderBy(value => value).ToArray();
    }

    private static void ValidateClosureAnimation(
        AlsAnimationSetDefinition animationSet,
        int animationId,
        int skeletonId,
        string label)
    {
        if ((uint)animationId >= (uint)animationSet.Animations.Length)
        {
            throw new InvalidOperationException(
                $"{label} animation library profile ID is out of range: {animationId}");
        }
        if (animationSet.Animations[animationId].SkeletonId != skeletonId)
        {
            throw new InvalidOperationException(
                $"{label} animation library profile targets another skeleton: {animationId}");
        }
    }

    private static AlsSkeletalMeshDefinition GetMannequin(
        AlsAnimationSetDefinition animationSet,
        int skeletonId,
        int mannequinMeshId)
    {
        if ((uint)mannequinMeshId >= (uint)animationSet.SkeletalMeshes.Length)
        {
            throw new InvalidOperationException(
                $"P3 profile Mannequin mesh ID is out of range: {mannequinMeshId}");
        }
        if ((uint)skeletonId >= (uint)animationSet.Skeletons.Length)
        {
            throw new InvalidOperationException(
                $"P3 profile skeleton ID is out of range: {skeletonId}");
        }

        var mannequin = animationSet.SkeletalMeshes[mannequinMeshId];
        if (mannequin.SkeletonId != skeletonId)
        {
            throw new InvalidOperationException(
                $"P3 profile Mannequin skeleton mismatch: " +
                $"expected={skeletonId} actual={mannequin.SkeletonId}");
        }
        return mannequin;
    }

    private static AlsAnimationDefinition GetClip(
        AlsAnimationSetDefinition animationSet,
        int animationId,
        int skeletonId)
    {
        if ((uint)animationId >= (uint)animationSet.Animations.Length)
        {
            throw new InvalidOperationException(
                $"P3 profile animation ID is out of range: {animationId}");
        }

        var clip = animationSet.Animations[animationId];
        if (clip.SkeletonId != skeletonId)
        {
            throw new InvalidOperationException(
                $"P3 profile animation skeleton mismatch for {clip.Name}: " +
                $"expected={skeletonId} actual={clip.SkeletonId}");
        }
        return clip;
    }

    private static PackedScene LoadScene(
        string resourcePath,
        string assetName,
        string assetKind) =>
        ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(resourcePath))
        ?? throw new InvalidOperationException(
            $"Unable to load P3 {assetKind} scene: {assetName}");

    private static void ThrowIfError(Error error, string operation, string assetName)
    {
        if (error != Error.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to {operation} for {assetName}: {error}");
        }
    }

    private static void ReleasePartialBuild(
        Node targetRoot,
        AnimationLibrary? library,
        StringName? libraryName,
        OwnedStringNameTable? names)
    {
        try
        {
            if (GodotObject.IsInstanceValid(targetRoot))
            {
                targetRoot.Free();
            }
        }
        finally
        {
            try
            {
                library?.Dispose();
            }
            finally
            {
                try
                {
                    libraryName?.Dispose();
                }
                finally
                {
                    names?.Dispose();
                }
            }
        }
    }

    private sealed class OwnedNode : IDisposable
    {
        public OwnedNode(Node value) => Value = value;

        public Node Value { get; }

        public void Dispose() => Value.Free();
    }
}

internal sealed class OwnedStringNameTable : IDisposable
{
    private readonly Dictionary<int, StringName> _values;
    private int _disposed;

    public OwnedStringNameTable(int capacity)
    {
        _values = new Dictionary<int, StringName>(capacity);
        View = new ReadOnlyDictionary<int, StringName>(_values);
    }

    public IReadOnlyDictionary<int, StringName> View { get; }

    public void Add(int animationId, StringName animationName)
    {
        if (!_values.TryAdd(animationId, animationName))
        {
            throw new InvalidOperationException(
                $"P3 profile contains duplicate animation ID: {animationId}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var value in _values.Values)
        {
            value.Dispose();
        }
        _values.Clear();
    }
}
