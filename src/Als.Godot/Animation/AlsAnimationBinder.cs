using Godot;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public sealed class AlsBoundAnimation : IDisposable
{
    public AlsBoundAnimation(Node root, Skeleton3D skeleton, AnimationPlayer player, string animationName)
    {
        Root = root;
        Skeleton = skeleton;
        Player = player;
        AnimationName = animationName;
    }

    public Node Root { get; }

    public Skeleton3D Skeleton { get; }

    public AnimationPlayer Player { get; }

    public string AnimationName { get; }

    public void Dispose() => Root.Free();
}

public static class AlsAnimationBinder
{
    private const double AnimationLengthTolerance = 1.0 / 30.0;
    private const string LibraryName = "als";
    private const string AnimationName = "bound";

    public static AlsBoundAnimation Bind(
        PackedScene targetScene,
        PackedScene sourceScene,
        AlsAnimationDefinition clip,
        AlsSkeletonDefinition skeletonDefinition)
    {
        var targetRoot = targetScene.Instantiate();
        try
        {
            var targetSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException($"Target scene has no Skeleton3D: {clip.Name}");
            ValidateTargetSkeleton(targetSkeleton, skeletonDefinition, clip.Name);

            using var sourceRoot = new OwnedNode(sourceScene.Instantiate());
            var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot.Value)
                ?? throw new InvalidOperationException($"Animation scene has no AnimationPlayer: {clip.Name}");
            var sourceAnimation = sourcePlayer.GetAnimationList()
                .Select(name => sourcePlayer.GetAnimation(name))
                .FirstOrDefault(animation => animation is not null && animation.GetTrackCount() > 0)
                ?? throw new InvalidOperationException($"Animation scene has no non-empty clip: {clip.Name}");
            if (Math.Abs(sourceAnimation.Length - clip.PlayLength) > AnimationLengthTolerance + 1e-6)
            {
                throw new InvalidOperationException(
                    $"Animation length mismatch for {clip.Name}: expected={clip.PlayLength} actual={sourceAnimation.Length}");
            }

            var boundAnimation = (Godot.Animation)sourceAnimation.Duplicate(true);
            RewriteTrackPaths(targetRoot, targetSkeleton, boundAnimation, clip.Name);

            var library = new AnimationLibrary();
            ThrowIfError(library.AddAnimation(AnimationName, boundAnimation), "add bound animation", clip.Name);
            var player = new AnimationPlayer
            {
                Name = "AlsAnimationPlayer",
                RootNode = new NodePath(".."),
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
            };
            targetRoot.AddChild(player);
            ThrowIfError(player.AddAnimationLibrary(LibraryName, library), "add animation library", clip.Name);
            var qualifiedName = $"{LibraryName}/{AnimationName}";
            player.Play(qualifiedName);
            player.Advance(0.0);
            return new AlsBoundAnimation(targetRoot, targetSkeleton, player, qualifiedName);
        }
        catch
        {
            targetRoot.Free();
            throw;
        }
    }

    private static void RewriteTrackPaths(
        Node targetRoot,
        Skeleton3D targetSkeleton,
        Godot.Animation animation,
        string clipName)
    {
        var skeletonPath = targetRoot.GetPathTo(targetSkeleton).ToString();
        for (var trackIndex = 0; trackIndex < animation.GetTrackCount(); trackIndex++)
        {
            var trackType = animation.TrackGetType(trackIndex);
            if (trackType is not (Godot.Animation.TrackType.Position3D or
                Godot.Animation.TrackType.Rotation3D or
                Godot.Animation.TrackType.Scale3D))
            {
                throw new InvalidOperationException(
                    $"Unsupported animation track type for {clipName}: index={trackIndex} type={trackType}");
            }

            var sourcePath = animation.TrackGetPath(trackIndex);
            if (sourcePath.GetSubNameCount() != 1)
            {
                throw new InvalidOperationException(
                    $"Animation track does not target exactly one bone for {clipName}: index={trackIndex} path={sourcePath}");
            }

            var boneName = sourcePath.GetSubName(0);
            if (targetSkeleton.FindBone(boneName) < 0)
            {
                throw new InvalidOperationException(
                    $"Animation track targets a missing physical bone for {clipName}: index={trackIndex} bone={boneName}");
            }

            animation.TrackSetPath(trackIndex, new NodePath($"{skeletonPath}:{boneName}"));
        }
    }

    private static void ValidateTargetSkeleton(
        Skeleton3D skeleton,
        AlsSkeletonDefinition definition,
        string clipName)
    {
        if (skeleton.GetBoneCount() != definition.PhysicalBones.Length)
        {
            throw new InvalidOperationException(
                $"Target skeleton bone count mismatch for {clipName}: expected={definition.PhysicalBones.Length} actual={skeleton.GetBoneCount()}");
        }

        for (var index = 0; index < definition.PhysicalBones.Length; index++)
        {
            var expected = definition.PhysicalBones[index].Name;
            var actual = skeleton.GetBoneName(index).ToString();
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target skeleton bone mismatch for {clipName}: index={index} expected={expected} actual={actual}");
            }
        }
    }

    private static void ThrowIfError(Error error, string operation, string clipName)
    {
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Failed to {operation} for {clipName}: {error}");
        }
    }

    private sealed class OwnedNode : IDisposable
    {
        public OwnedNode(Node value) => Value = value;

        public Node Value { get; }

        public void Dispose() => Value.Free();
    }
}
