using System.Security.Cryptography;
using Godot;
using GodotAls.Assets;
using GodotAls.Import.Manifest;

namespace GodotAls.Import;

// Runtime provenance gate for Kimodo-regenerated ALS assets.
//
// Purpose: prove at import/run time that the animation set actually being exercised was
// regenerated through the Kimodo mocap pipeline (BVH -> UE IKRetargeter -> ALS_Final FBX),
// instead of silently falling back to the original ALS v4 exports.
//
// The gate lives on the manifest layer on purpose: raw sequence JSON is validated by
// AlsRawAnimationSourceCompiler.Fields() against an exact field set, so provenance markers
// cannot be embedded in raw JSON; the manifest is the only layer that both (a) survives
// deserialization into typed records (AlsManifestKimodoSource / AlsManifestKimodoSourceMarker)
// and (b) is consumed by both the P2B import flow and the demo runtime flow.
//
// Enable conditions (either one):
//   - ALS_REQUIRE_KIMODO_SOURCE=1 environment variable (explicit opt-in), or
//   - the resolved asset root contains "kimodo_als" (auto-enable for Kimodo trees).
// When enabled, the following must hold or the process throws before any animation runs:
//   1. manifest.kimodoSourceMarker exists and reports variant=="kimodo-als-regen",
//      replacedAnimations==123, keptOriginalAnimations==3.
//   2. Exactly 123 of the 126 manifest Animations entries carry a kimodoSource block;
//      every such block must have pipeline=="kimodo-als-final",
//      retargetedAssetPath starting with "/Game/KimodoMocara/", and a non-empty fbxSha256.
//   3. For every Kimodo-sourced animation, the actual FBX bytes on disk (asset root +
//      outputPath) hash to exactly kimodoSource.fbxSha256. This proves the FBX payload
//      shipped with the tree is the retargeted Kimodo output, not a stale original.
//   4. For the 3 kept-original animations (no kimodoSource), the file row sha256 in
//      manifest.Files for their outputPath must match the on-disk FBX hash, so a kept
//      original cannot mask an accidentally shipped stale Kimodo intermediate.
//   5. The compiled resource digest must equal the Kimodo definition digest
//      afcd3d54af3a42e982ec013c3b30cef8546d1ddd817d4f2e22a6355efdae3279, pinning the
//      whole tree (manifest + FBX + raw closure) to the Kimodo regeneration batch
//      (original ALS timing domain: Kimodo pose content resampled onto original
//      key counts via restore_original_timing.py).
public static class AlsKimodoSourceGate
{
    private const string ExpectedVariant = "kimodo-als-regen";
    private const string ExpectedPipeline = "kimodo-als-final";
    private const string ExpectedRetargetPrefix = "/Game/KimodoMocara/";
    private const int ExpectedReplacedAnimations = 123;
    private const int ExpectedKeptOriginalAnimations = 3;
    private const string ExpectedDefinitionDigest =
        "afcd3d54af3a42e982ec013c3b30cef8546d1ddd817d4f2e22a6355efdae3279";

    internal static bool IsEnabled()
    {
        var explicitFlag = System.Environment.GetEnvironmentVariable("ALS_REQUIRE_KIMODO_SOURCE");
        if (!string.IsNullOrWhiteSpace(explicitFlag))
        {
            return explicitFlag.Trim() == "1" || explicitFlag.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        return AlsGodotImportCoordinator.AssetRoot.Contains("kimodo_als", StringComparison.OrdinalIgnoreCase);
    }

    internal static void VerifyManifest(AlsManifest manifest)
    {
        var marker = manifest.KimodoSourceMarker;
        if (marker is null)
        {
            throw new InvalidOperationException(
                "ALS Kimodo source gate: manifest.kimodoSourceMarker is missing; " +
                "this animation tree was not produced by synthesize_kimodo_manifest.py.");
        }
        if (marker.Variant != ExpectedVariant)
        {
            throw new InvalidOperationException(
                $"ALS Kimodo source gate: unexpected marker variant '{marker.Variant}' (expected '{ExpectedVariant}').");
        }
        if (marker.ReplacedAnimations != ExpectedReplacedAnimations ||
            marker.KeptOriginalAnimations != ExpectedKeptOriginalAnimations)
        {
            throw new InvalidOperationException(
                "ALS Kimodo source gate: marker counts mismatch: " +
                $"replaced={marker.ReplacedAnimations} (expected {ExpectedReplacedAnimations}), " +
                $"keptOriginal={marker.KeptOriginalAnimations} (expected {ExpectedKeptOriginalAnimations}).");
        }

        var kimodoAnimations = manifest.Animations.Where(a => a.KimodoSource is not null).ToArray();
        if (kimodoAnimations.Length != ExpectedReplacedAnimations)
        {
            throw new InvalidOperationException(
                "ALS Kimodo source gate: animation provenance count mismatch: " +
                $"kimodoSourced={kimodoAnimations.Length} (expected {ExpectedReplacedAnimations}) " +
                $"of total {manifest.Animations.Length}.");
        }

        foreach (var animation in kimodoAnimations)
        {
            var source = animation.KimodoSource!;
            if (source.Pipeline != ExpectedPipeline)
            {
                throw new InvalidOperationException(
                    $"ALS Kimodo source gate: animation '{animation.Id}' has pipeline '{source.Pipeline}' " +
                    $"(expected '{ExpectedPipeline}').");
            }
            if (!source.RetargetedAssetPath.StartsWith(ExpectedRetargetPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ALS Kimodo source gate: animation '{animation.Id}' retargetedAssetPath " +
                    $"'{source.RetargetedAssetPath}' does not start with '{ExpectedRetargetPrefix}'.");
            }
            if (string.IsNullOrEmpty(source.FbxSha256))
            {
                throw new InvalidOperationException(
                    $"ALS Kimodo source gate: animation '{animation.Id}' has an empty fbxSha256.");
            }
        }

        GD.Print(
            $"ALS_KIMODO_GATE_OK stage=manifest replaced={ExpectedReplacedAnimations} " +
            $"keptOriginal={ExpectedKeptOriginalAnimations} variant={ExpectedVariant}");
    }

    internal static void VerifyFbxPayloads(AlsManifest manifest)
    {
        var assetRootAbsolute = ProjectSettings.GlobalizePath(AlsGodotImportCoordinator.AssetRoot);
        var fileHashes = manifest.Files.ToDictionary(f => f.RelativePath, f => f.Sha256, StringComparer.Ordinal);
        var verified = 0;
        var kept = 0;

        foreach (var animation in manifest.Animations)
        {
            if (string.IsNullOrEmpty(animation.OutputPath))
            {
                throw new InvalidOperationException(
                    $"ALS Kimodo source gate: animation '{animation.Id}' has no outputPath.");
            }

            var fbxAbsolute = Path.Combine(assetRootAbsolute, animation.OutputPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fbxAbsolute))
            {
                throw new InvalidOperationException(
                    $"ALS Kimodo source gate: FBX file is missing for animation '{animation.Id}': {animation.OutputPath}");
            }

            string actualHash;
            using (var stream = File.OpenRead(fbxAbsolute))
            {
                actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }

            if (animation.KimodoSource is not null)
            {
                if (!string.Equals(actualHash, animation.KimodoSource.FbxSha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"ALS Kimodo source gate: FBX sha256 mismatch for Kimodo animation '{animation.Id}': " +
                        $"manifest.kimodoSource.fbxSha256={animation.KimodoSource.FbxSha256} actual={actualHash}. " +
                        "The shipped FBX is not the Kimodo retargeted output.");
                }
                verified++;
            }
            else
            {
                if (!fileHashes.TryGetValue(animation.OutputPath, out var fileRowSha) ||
                    !string.Equals(actualHash, fileRowSha, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"ALS Kimodo source gate: FBX sha256 mismatch for kept-original animation '{animation.Id}': " +
                        "on-disk file does not match manifest.files row; the kept original may have been overwritten.");
                }
                kept++;
            }
        }

        if (verified != ExpectedReplacedAnimations || kept != ExpectedKeptOriginalAnimations)
        {
            throw new InvalidOperationException(
                $"ALS Kimodo source gate: FBX verification count mismatch: kimodo={verified} kept={kept}.");
        }

        GD.Print($"ALS_KIMODO_GATE_OK stage=fbx verified={verified} kept={kept}");
    }

    internal static void VerifyCompiledDigest(string definitionDigest)
    {
        if (!string.Equals(definitionDigest, ExpectedDefinitionDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ALS Kimodo source gate: compiled definition digest mismatch: " +
                $"actual={definitionDigest} expected={ExpectedDefinitionDigest}. " +
                "The imported animation set is not the Kimodo regeneration batch.");
        }
        GD.Print($"ALS_KIMODO_GATE_OK stage=digest digest={definitionDigest}");
    }

    internal static void VerifyRuntimeResource()
    {
        // Demo/smoke entry: verify the already-compiled .tres resource digest without a full re-import.
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath);
        if (resource is null)
        {
            throw new InvalidOperationException(
                "ALS Kimodo source gate: compiled animation set resource is missing: " +
                AlsGodotImportCoordinator.CompiledResourcePath);
        }
        VerifyCompiledDigest(resource.DefinitionDigest);
        GD.Print("ALS_KIMODO_GATE_OK stage=runtime_resource");
    }
}
