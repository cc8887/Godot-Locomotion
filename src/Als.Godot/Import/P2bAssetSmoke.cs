using Godot;
using GodotAls.Animation;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import;

public partial class P2bAssetSmoke : Node
{
    private const string MannequinAssetId = "86d98d8177feb473c8a5f406c5b42f8c2a2f7b07";
    private const string M4a1AssetId = "2516ba17950769f5845f00f6c17c6d6ac913f475";

    private static readonly string[] ClipIds =
    [
        "6124eafdcbeaaf04bca366add34c821faa0e4963",
        "572c3c83c9007964c233db4c7288ae38e20c3dec",
        "a73b6e3c8aac55396058a7cb7c65b1afe6a539fa",
        "5c0f718da651311d539b76fcd178dc6467839df6",
        "bf827e8773890767df2c42df9208d461dc9e3ece",
        "74fae9958ca8fc1e28329c581038e55a4840c2c1",
    ];

    private static readonly string[] PoseBoneNames =
    [
        "root", "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

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
        var manifest = AlsManifestSerializer.Load(ProjectSettings.GlobalizePath(AlsGodotImportCoordinator.ManifestPath));
        var definition = AlsAnimationSetCompiler.Compile(manifest);
        var mannequinAsset = manifest.SkeletalMeshes.Single(asset => asset.Id == MannequinAssetId);
        var m4a1Asset = manifest.SkeletalMeshes.Single(asset => asset.Id == M4a1AssetId);
        var mannequin = LoadScene(mannequinAsset.OutputPath!, mannequinAsset.AssetName);
        var materialBuilder = new AlsMaterialBuilder(definition);
        var finalDigests = new List<string>();
        var overlayCount = 0;

        for (var clipIndex = 0; clipIndex < ClipIds.Length; clipIndex++)
        {
            var animationId = definition.AssetIndex.GetAnimationId(ClipIds[clipIndex]);
            var clip = definition.Animations[animationId];
            var source = LoadScene(clip.ResourcePath, clip.Name);
            using var bound = AlsAnimationBinder.Bind(
                mannequin,
                source,
                clip,
                definition.Skeletons[clip.SkeletonId]);
            AddChild(bound.Root);
            if (materialBuilder.ApplyToRoot(bound.Root, mannequinAsset) == 0)
            {
                throw new InvalidOperationException($"Mannequin received no reconstructed material for {clip.Name}.");
            }

            var initialPose = AlsPoseDigest.CapturePoses(bound.Skeleton, PoseBoneNames);
            var poseChanged = false;
            var frameCount = Math.Min(60, Math.Max(1, (int)Math.Ceiling(clip.PlayLength * 30.0)));
            for (var frame = 1; frame <= frameCount; frame++)
            {
                bound.Player.Advance(1.0 / 30.0);
                var currentPose = AlsPoseDigest.CapturePoses(bound.Skeleton, PoseBoneNames);
                poseChanged |= AlsPoseDigest.HasChanged(initialPose, currentPose);
                if (frame == frameCount)
                {
                    finalDigests.Add(AlsPoseDigest.Compute(bound.Skeleton, frame, PoseBoneNames));
                }
            }

            if (clipIndex < 4 && !poseChanged)
            {
                throw new InvalidOperationException($"Representative animation did not change the selected pose: {clip.Name}");
            }
            overlayCount += clip.Overlay ? 1 : 0;
        }

        var propRoot = LoadScene(m4a1Asset.OutputPath!, m4a1Asset.AssetName).Instantiate();
        AddChild(propRoot);
        try
        {
            if (materialBuilder.ApplyToRoot(propRoot, m4a1Asset) == 0)
            {
                throw new InvalidOperationException("M4A1 received no reconstructed material.");
            }
        }
        finally
        {
            propRoot.Free();
        }

        var mannequinRoot = mannequin.Instantiate();
        try
        {
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(mannequinRoot)
                ?? throw new InvalidOperationException("Mannequin scene has no Skeleton3D.");
            if (finalDigests.Distinct(StringComparer.Ordinal).Count() < 2)
            {
                throw new InvalidOperationException("Representative animations produced no distinct final pose digests.");
            }

            GD.Print(
                $"P2B_ASSET_SMOKE_OK mannequinBones={skeleton.GetBoneCount()} clips={ClipIds.Length} " +
                $"overlay={overlayCount} props={(m4a1Asset.Metadata.GetProperty("prop").GetBoolean() ? 1 : 0)}");
        }
        finally
        {
            mannequinRoot.Free();
        }
    }

    private static PackedScene LoadScene(string resourcePath, string assetName) =>
        ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(resourcePath))
        ?? throw new InvalidOperationException($"Unable to load representative scene: {assetName}");
}
