using Godot;
using GodotAls.Assets;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class P3bAnimationLibrarySmoke : Node
{
    private const double AnimationLengthTolerance = 1.0 / 30.0;
    private const string ProfilePath = "res://assets/config/p3_locomotion_profile.json";

    public override void _Ready()
    {
        try
        {
            RunSmoke();
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void RunSmoke()
    {
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var profile = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(ProfilePath)), definition);

        var result = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(result.Root);

        var skeletonCount = CountSkeletons(result.Root);
        if (skeletonCount != 1)
        {
            throw new InvalidOperationException(
                $"P3 animation library must own exactly one Skeleton3D: actual={skeletonCount}");
        }
        if (!ReferenceEquals(
                AlsImportedResourceAuditor.FindFirst<Skeleton3D>(result.Root),
                result.Skeleton))
        {
            throw new InvalidOperationException("P3 animation library returned the wrong target skeleton.");
        }
        if (result.Skeleton.GetBoneCount() != 68)
        {
            throw new InvalidOperationException(
                $"P3 Mannequin skeleton bone count mismatch: actual={result.Skeleton.GetBoneCount()}");
        }

        var expectedIds = profile.AllAnimationIds;
        if (expectedIds.Distinct().Count() != expectedIds.Length ||
            result.ClipNames.Count != expectedIds.Length ||
            result.Player.GetAnimationList().Length != expectedIds.Length)
        {
            throw new InvalidOperationException("P3 profile animations were not inserted exactly once.");
        }

        using var targetSkeletonNodePath = result.Root.GetPathTo(result.Skeleton);
        var targetSkeletonPath = targetSkeletonNodePath.ToString();
        foreach (var animationId in expectedIds)
        {
            if (!result.ClipNames.TryGetValue(animationId, out var animationName))
            {
                throw new InvalidOperationException(
                    $"P3 animation library has no prebuilt name for animation ID {animationId}.");
            }
            if (animationName.ToString() != $"clip_{animationId}" ||
                !result.Library.HasAnimation(animationName))
            {
                throw new InvalidOperationException(
                    $"P3 animation ID {animationId} has an invalid deterministic clip name.");
            }

            using var animation = result.Library.GetAnimation(animationName)
                ?? throw new InvalidOperationException(
                    $"P3 animation library returned a null clip for animation ID {animationId}.");
            var definitionClip = definition.Animations[animationId];
            if (Math.Abs(animation.Length - definitionClip.PlayLength) >
                AnimationLengthTolerance + 1e-6)
            {
                throw new InvalidOperationException(
                    $"P3 duplicated Unreal Take length mismatch for {definitionClip.Name}: " +
                    $"expected={definitionClip.PlayLength} actual={animation.Length}");
            }

            for (var trackIndex = 0; trackIndex < animation.GetTrackCount(); trackIndex++)
            {
                var trackType = animation.TrackGetType(trackIndex);
                if (trackType is not (Godot.Animation.TrackType.Position3D or
                    Godot.Animation.TrackType.Rotation3D or
                    Godot.Animation.TrackType.Scale3D))
                {
                    throw new InvalidOperationException(
                        $"P3 animation library contains unsupported track type: " +
                        $"animation={animationId} index={trackIndex} type={trackType}");
                }

                using var trackPath = animation.TrackGetPath(trackIndex);
                var path = trackPath.ToString();
                var separator = path.LastIndexOf(':');
                if (separator <= 0 ||
                    !string.Equals(path[..separator], targetSkeletonPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P3 animation track does not target the owned skeleton: " +
                        $"animation={animationId} index={trackIndex} path={path}");
                }
            }
        }

        GD.Print(
            $"GODOT_ALS_P3B_LIBRARY_OK bones={result.Skeleton.GetBoneCount()} " +
            $"clips={expectedIds.Length} skeletons={skeletonCount}");

        result.Dispose();
        result.Dispose();

        var parent = new Node { Name = "P3bParentFreedOwner" };
        AddChild(parent);
        var parentFreedResult = AlsAnimationLibraryBuilder.Build(definition, profile);
        parent.AddChild(parentFreedResult.Root);
        parent.Free();
        parentFreedResult.Dispose();
        parentFreedResult.Dispose();

        var partialIds = profile.AllAnimationIds
            .Take(3)
            .Append(definition.Animations.Length)
            .ToArray();
        var partialProfile = profile with { AllAnimationIds = partialIds };
        try
        {
            using var unexpected = AlsAnimationLibraryBuilder.Build(definition, partialProfile);
            throw new InvalidOperationException("P3 partial-build fixture unexpectedly succeeded.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("animation ID is out of range", StringComparison.Ordinal))
        {
        }

        var rebuilt = AlsAnimationLibraryBuilder.Build(definition, profile);
        AddChild(rebuilt.Root);
        if (rebuilt.Player.GetAnimationList().Length != expectedIds.Length)
        {
            throw new InvalidOperationException("P3 animation library could not rebuild after partial failure.");
        }
        rebuilt.Dispose();
        rebuilt.Dispose();

        GD.Print("GODOT_ALS_P3B_LIBRARY_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1");
    }

    private static int CountSkeletons(Node root)
    {
        var count = root is Skeleton3D ? 1 : 0;
        foreach (var child in root.GetChildren())
        {
            count += CountSkeletons(child);
        }
        return count;
    }
}
