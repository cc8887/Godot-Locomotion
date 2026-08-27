using System.Collections.ObjectModel;
using Godot;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsAnimationLibraryBuildResult : IDisposable
{
    private readonly AnimationLibrary _ownedLibrary;
    private readonly StringName _ownedLibraryName;
    private readonly OwnedStringNameTable _ownedClipNames;
    private int _disposed;

    internal AlsAnimationLibraryBuildResult(
        Node root,
        Skeleton3D skeleton,
        AnimationPlayer player,
        AnimationLibrary library,
        StringName libraryName,
        OwnedStringNameTable clipNames)
    {
        Root = root;
        Skeleton = skeleton;
        Player = player;
        Library = library;
        ClipNames = clipNames.View;
        _ownedLibrary = library;
        _ownedLibraryName = libraryName;
        _ownedClipNames = clipNames;
    }

    public Node Root { get; }

    public Skeleton3D Skeleton { get; }

    public AnimationPlayer Player { get; }

    public AnimationLibrary Library { get; }

    public IReadOnlyDictionary<int, StringName> ClipNames { get; }

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
                    _ownedClipNames.Dispose();
                }
            }
        }
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

        using var importedAnimationName = new StringName(ImportedAnimationName);
        var mannequin = GetMannequin(animationSet, profile);
        var targetScene = LoadScene(mannequin.ResourcePath, mannequin.Name, "target");
        var targetRoot = targetScene.Instantiate();
        AnimationLibrary? library = null;
        StringName? libraryName = null;
        OwnedStringNameTable? names = null;
        try
        {
            var targetSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException(
                    $"Target scene has no Skeleton3D: {mannequin.Name}");
            var skeletonDefinition = animationSet.Skeletons[profile.SkeletonId];
            AlsAnimationBinder.ValidateTargetSkeleton(
                targetSkeleton, skeletonDefinition, mannequin.Name);

            library = new AnimationLibrary();
            libraryName = new StringName(LibraryName);
            names = new OwnedStringNameTable(profile.AllAnimationIds.Length);
            foreach (var animationId in profile.AllAnimationIds)
            {
                var clip = GetClip(animationSet, animationId, profile.SkeletonId);
                var sourceScene = LoadScene(clip.ResourcePath, clip.Name, "animation");
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

            var result = new AlsAnimationLibraryBuildResult(
                targetRoot,
                targetSkeleton,
                player,
                library,
                libraryName,
                names);
            library = null;
            libraryName = null;
            names = null;
            return result;
        }
        catch
        {
            ReleasePartialBuild(targetRoot, library, libraryName, names);
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
