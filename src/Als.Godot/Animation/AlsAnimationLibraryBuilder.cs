using System.Collections.ObjectModel;
using Godot;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsAnimationLibraryBuildResult : IDisposable
{
    internal AlsAnimationLibraryBuildResult(
        Node root,
        Skeleton3D skeleton,
        AnimationPlayer player,
        AnimationLibrary library,
        IReadOnlyDictionary<int, StringName> clipNames)
    {
        Root = root;
        Skeleton = skeleton;
        Player = player;
        Library = library;
        ClipNames = clipNames;
    }

    public Node Root { get; }

    public Skeleton3D Skeleton { get; }

    public AnimationPlayer Player { get; }

    public AnimationLibrary Library { get; }

    public IReadOnlyDictionary<int, StringName> ClipNames { get; }

    public void Dispose() => Root.Free();
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

        var mannequin = GetMannequin(animationSet, profile);
        var targetScene = LoadScene(mannequin.ResourcePath, mannequin.Name, "target");
        var targetRoot = targetScene.Instantiate();
        try
        {
            var targetSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException(
                    $"Target scene has no Skeleton3D: {mannequin.Name}");
            var skeletonDefinition = animationSet.Skeletons[profile.SkeletonId];
            AlsAnimationBinder.ValidateTargetSkeleton(
                targetSkeleton, skeletonDefinition, mannequin.Name);

            var library = new AnimationLibrary();
            var names = new Dictionary<int, StringName>(profile.AllAnimationIds.Length);
            foreach (var animationId in profile.AllAnimationIds)
            {
                var clip = GetClip(animationSet, animationId, profile.SkeletonId);
                var sourceScene = LoadScene(clip.ResourcePath, clip.Name, "animation");
                using var sourceRoot = new OwnedNode(sourceScene.Instantiate());
                var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot.Value)
                    ?? throw new InvalidOperationException(
                        $"Animation scene has no AnimationPlayer: {clip.Name}");
                if (!sourcePlayer.HasAnimation(ImportedAnimationName))
                {
                    throw new InvalidOperationException(
                        $"Animation scene does not contain '{ImportedAnimationName}': {clip.Name}");
                }

                var sourceAnimation = sourcePlayer.GetAnimation(ImportedAnimationName);
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

                var boundAnimation = (Godot.Animation)sourceAnimation.Duplicate(true);
                AlsAnimationBinder.RewriteTrackPaths(
                    targetRoot, targetSkeleton, boundAnimation, clip.Name);
                var animationName = new StringName($"clip_{animationId}");
                ThrowIfError(
                    library.AddAnimation(animationName, boundAnimation),
                    "add animation library clip",
                    clip.Name);
                if (!names.TryAdd(animationId, animationName))
                {
                    throw new InvalidOperationException(
                        $"P3 profile contains duplicate animation ID: {animationId}");
                }
            }

            var player = new AnimationPlayer
            {
                Name = "AlsAnimationPlayer",
                RootNode = new NodePath(".."),
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
            };
            targetRoot.AddChild(player);
            ThrowIfError(
                player.AddAnimationLibrary(LibraryName, library),
                "add animation library",
                mannequin.Name);

            return new AlsAnimationLibraryBuildResult(
                targetRoot,
                targetSkeleton,
                player,
                library,
                new ReadOnlyDictionary<int, StringName>(names));
        }
        catch
        {
            targetRoot.Free();
            throw;
        }
    }

    private static AlsSkeletalMeshDefinition GetMannequin(
        AlsAnimationSetDefinition animationSet,
        AlsLocomotionAnimationProfile profile)
    {
        if ((uint)profile.MannequinMeshId >= (uint)animationSet.SkeletalMeshes.Length)
        {
            throw new InvalidOperationException(
                $"P3 profile Mannequin mesh ID is out of range: {profile.MannequinMeshId}");
        }
        if ((uint)profile.SkeletonId >= (uint)animationSet.Skeletons.Length)
        {
            throw new InvalidOperationException(
                $"P3 profile skeleton ID is out of range: {profile.SkeletonId}");
        }

        var mannequin = animationSet.SkeletalMeshes[profile.MannequinMeshId];
        if (mannequin.SkeletonId != profile.SkeletonId)
        {
            throw new InvalidOperationException(
                $"P3 profile Mannequin skeleton mismatch: " +
                $"expected={profile.SkeletonId} actual={mannequin.SkeletonId}");
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

    private sealed class OwnedNode : IDisposable
    {
        public OwnedNode(Node value) => Value = value;

        public Node Value { get; }

        public void Dispose() => Value.Free();
    }
}
