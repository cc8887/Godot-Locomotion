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
    private const string ImportedAnimationName = "Unreal Take";
    private const string LibraryName = "als";
    private const string AnimationName = "bound";

    public static AlsBoundAnimation Bind(
        PackedScene targetScene,
        PackedScene sourceScene,
        AlsAnimationDefinition clip,
        AlsSkeletonDefinition skeletonDefinition)
    {
        using var importedAnimationName = new StringName(ImportedAnimationName);
        using var boundAnimationName = new StringName(AnimationName);
        using var libraryName = new StringName(LibraryName);
        var targetRoot = targetScene.Instantiate();
        try
        {
            var targetSkeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException($"Target scene has no Skeleton3D: {clip.Name}");
            ValidateTargetSkeleton(targetSkeleton, skeletonDefinition, clip.Name);

            using var sourceRoot = new OwnedNode(sourceScene.Instantiate());
            var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot.Value)
                ?? throw new InvalidOperationException($"Animation scene has no AnimationPlayer: {clip.Name}");
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
            if (Math.Abs(sourceAnimation.Length - clip.PlayLength) > AnimationLengthTolerance + 1e-6)
            {
                throw new InvalidOperationException(
                    $"Animation length mismatch for {clip.Name}: expected={clip.PlayLength} actual={sourceAnimation.Length}");
            }

            using var boundAnimation = (Godot.Animation)sourceAnimation.Duplicate(true);
            RewriteTrackPaths(targetRoot, targetSkeleton, boundAnimation, clip.Name);
            ApplyReferencePoseRootLock(targetSkeleton, boundAnimation, clip, skeletonDefinition);

            using var library = new AnimationLibrary();
            ThrowIfError(library.AddAnimation(boundAnimationName, boundAnimation), "add bound animation", clip.Name);
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
            ThrowIfError(player.AddAnimationLibrary(libraryName, library), "add animation library", clip.Name);
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

    internal static void RewriteTrackPaths(
        Node targetRoot,
        Skeleton3D targetSkeleton,
        Godot.Animation animation,
        string clipName)
    {
        using var targetSkeletonPath = targetRoot.GetPathTo(targetSkeleton);
        var skeletonPath = targetSkeletonPath.ToString();
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

            using var sourcePath = animation.TrackGetPath(trackIndex);
            string boneName;
            try
            {
                boneName = AlsAnimationTrackPathContract.GetBoneName(sourcePath.ToString());
            }
            catch (AlsCompilationException exception)
            {
                throw new InvalidOperationException(
                    $"Animation track does not target exactly one bone for {clipName}: index={trackIndex} path={sourcePath}",
                    exception);
            }

            if (targetSkeleton.FindBone(boneName) < 0)
            {
                throw new InvalidOperationException(
                    $"Animation track targets a missing physical bone for {clipName}: index={trackIndex} bone={boneName}");
            }

            using var targetPath = new NodePath($"{skeletonPath}:{boneName}");
            animation.TrackSetPath(trackIndex, targetPath);
        }
    }

    internal static void ApplyReferencePoseRootLock(
        Skeleton3D skeleton, Godot.Animation animation,
        AlsAnimationDefinition clip, AlsSkeletonDefinition definition)
    {
        if (!clip.ForceRootLock || clip.RootMotionEnabled || clip.RootMotionRootLock != 0) return;
        var rootId = definition.LogicalToPhysical[definition.RequiredBones.Root];
        var rest = skeleton.GetBoneRest(rootId);
        var rootName = skeleton.GetBoneName(rootId).ToString();
        // UE RefPose ForceRootLock removes the exported root transform before pose blending.
        // Keep the tracks explicit so a preceding clip cannot leave its root pose behind.
        for (var track = 0; track < animation.GetTrackCount(); track++)
        {
            using var path = animation.TrackGetPath(track);
            if (path.GetSubNameCount() != 1 || path.GetSubName(0) != rootName) continue;
            var value = animation.TrackGetType(track) switch
            {
                Godot.Animation.TrackType.Position3D => Variant.From(rest.Origin),
                Godot.Animation.TrackType.Rotation3D => Variant.From(rest.Basis.GetRotationQuaternion()),
                Godot.Animation.TrackType.Scale3D => Variant.From(rest.Basis.Scale),
                _ => throw new InvalidOperationException($"Unsupported root track in {clip.Name}."),
            };
            for (var key = 0; key < animation.TrackGetKeyCount(track); key++)
                animation.TrackSetKeyValue(track, key, value);
        }
    }

    internal static void ValidateTargetSkeleton(
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
