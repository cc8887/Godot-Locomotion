using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Import;

namespace GodotAls.Locomotion;

public partial class LyraRetargetSmoke : Node
{
    private const string SourcePath = "res://assets/generated/lyra_als/animations/LY_MM_Unarmed_Jog_Left.fbx";
    private const string SidecarPath = "res://assets/generated/lyra_als/animations/LY_MM_Unarmed_Jog_Left.source.json";
    private const string MannequinPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin";

    public override void _Ready()
    {
        try
        {
            var (bones, changed, tracks, length) = Run();
            GD.Print($"LYRA_ALS_GODOT_SMOKE_OK bones={bones} changed={changed} tracks={tracks} length={length:F4}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private (int Bones, int Changed, int Tracks, double Length) Run()
    {
        using var sidecar = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(SidecarPath));
        var expectedHash = sidecar.RootElement.GetProperty("fbxSha256").GetString();
        var actualHash = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(SourcePath)));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lyra FBX differs from its UE export sidecar.");

        var setResource = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS asset set is missing.");
        var set = setResource.LoadDefinition();
        var mesh = set.SkeletalMeshes.Single(asset => asset.ObjectPath == MannequinPath);
        var skeletonDefinition = set.Skeletons[mesh.SkeletonId];
        var targetScene = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(mesh.ResourcePath))
            ?? throw new InvalidOperationException("ALS mannequin scene is missing.");
        var sourceScene = ResourceLoader.Load<PackedScene>(SourcePath)
            ?? throw new InvalidOperationException("Retargeted Lyra FBX was not imported.");
        var targetRoot = targetScene.Instantiate();
        var sourceRoot = sourceScene.Instantiate();
        AddChild(targetRoot);
        try
        {
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(targetRoot)
                ?? throw new InvalidOperationException("ALS mannequin has no Skeleton3D.");
            AlsAnimationBinder.ValidateTargetSkeleton(skeleton, skeletonDefinition, "Lyra retarget");
            if (skeleton.GetBoneCount() != 68)
                throw new InvalidOperationException("Lyra target is not the ALS 68-bone rig.");

            var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot)
                ?? throw new InvalidOperationException("Imported Lyra FBX has no AnimationPlayer.");
            using var sourceName = new StringName("Unreal Take");
            if (!sourcePlayer.HasAnimation(sourceName))
                throw new InvalidOperationException("Imported Lyra FBX has no Unreal Take.");
            using var imported = sourcePlayer.GetAnimation(sourceName);
            if (imported is null || imported.GetTrackCount() == 0)
                throw new InvalidOperationException("Imported Lyra animation has no tracks.");

            var expectedLength = sidecar.RootElement.GetProperty("metadata")
                .GetProperty("sequencePlayLength").GetDouble();
            if (Math.Abs(imported.Length - expectedLength) > 1.0 / 30.0 + 1e-6)
                throw new InvalidOperationException("Godot Lyra animation duration differs from UE.");
            using var bound = (Godot.Animation)imported.Duplicate(true);
            AlsAnimationBinder.RewriteTrackPaths(targetRoot, skeleton, bound, "Lyra retarget");
            using var library = new AnimationLibrary();
            using var clipName = new StringName("jog_left");
            if (library.AddAnimation(clipName, bound) != Error.Ok)
                throw new InvalidOperationException("Could not bind Lyra clip to ALS skeleton.");
            var player = new AnimationPlayer { Name = "LyraAnimationPlayer",
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
            using (var rootPath = new NodePath("..")) player.RootNode = rootPath;
            targetRoot.AddChild(player);
            using var libraryName = new StringName("lyra");
            if (player.AddAnimationLibrary(libraryName, library) != Error.Ok)
                throw new InvalidOperationException("Could not install Lyra animation library.");

            player.Play("lyra/jog_left");
            player.Advance(0.0);
            var first = Enumerable.Range(0, skeleton.GetBoneCount())
                .Select(skeleton.GetBonePose).ToArray();
            player.Advance(0.35);
            var changed = 0;
            for (var index = 0; index < first.Length; index++)
            {
                var now = skeleton.GetBonePose(index);
                var before = first[index];
                var a = now.Basis.GetRotationQuaternion();
                var b = before.Basis.GetRotationQuaternion();
                var rotationChange = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) +
                                     Math.Abs(a.Z - b.Z) + Math.Abs(a.W - b.W);
                if (now.Origin.DistanceTo(before.Origin) > 1e-4f || rotationChange > 1e-4f)
                    changed++;
            }
            if (changed < 8)
                throw new InvalidOperationException($"Retargeted clip did not animate the ALS rig: {changed} bones.");
            return (skeleton.GetBoneCount(), changed, bound.GetTrackCount(), imported.Length);
        }
        finally
        {
            sourceRoot.Free();
            targetRoot.Free();
        }
    }
}
