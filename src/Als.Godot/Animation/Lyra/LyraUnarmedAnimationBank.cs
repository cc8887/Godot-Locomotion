using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Import;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraClip(string Slot, string SourceObjectPath, string TargetObjectPath,
    string Fbx, string Sha256, double PlayLength,
    bool EnableRootMotion, bool ForceRootLock, string RootMotionRootLock, string[] FloatCurveNames,
    bool Loop = false);

internal sealed class LyraUnarmedCatalog
{
    private const string CatalogPath = "res://assets/generated/lyra_als/unarmed_catalog.json";
    private const string ExpectedSkeleton = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";

    private LyraUnarmedCatalog(Dictionary<string, LyraClip> clips) => Clips = clips;

    public IReadOnlyDictionary<string, LyraClip> Clips { get; }

    public static LyraUnarmedCatalog Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(CatalogPath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("targetSkeleton").GetString() != ExpectedSkeleton)
            throw new InvalidOperationException("Lyra catalog does not target the ALS skeleton.");
        var clips = new Dictionary<string, LyraClip>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("clips").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!;
            var fbx = row.GetProperty("fbx").GetString()!;
            if (string.IsNullOrWhiteSpace(slot) || fbx.Contains("..", StringComparison.Ordinal) ||
                !fbx.StartsWith("animations/LY_MM_Unarmed_", StringComparison.Ordinal) ||
                !fbx.EndsWith(".fbx", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid Lyra catalog entry: {slot}.");
            var clip = new LyraClip(slot, row.GetProperty("source").GetString()!,
                row.GetProperty("target").GetString()!, fbx, row.GetProperty("fbxSha256").GetString()!,
                row.GetProperty("playLength").GetDouble(), row.GetProperty("enableRootMotion").GetBoolean(),
                row.GetProperty("forceRootLock").GetBoolean(), row.GetProperty("rootMotionRootLock").GetString()!,
                row.GetProperty("floatCurveNames").EnumerateArray().Select(value => value.GetString()!).ToArray());
            if (!clips.TryAdd(slot, clip)) throw new InvalidOperationException($"Duplicate Lyra slot: {slot}.");
        }
        if (clips.Count != 21) throw new InvalidOperationException($"Expected 21 Unarmed clips; got {clips.Count}.");
        return new LyraUnarmedCatalog(clips);
    }

    public static string ResourcePath(LyraClip clip) => "res://assets/generated/lyra_als/" + clip.Fbx;

    public static void VerifyExport(LyraClip clip)
    {
        var path = ResourcePath(clip);
        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0 || !Convert.ToHexString(SHA256.HashData(bytes))
                .Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Lyra FBX hash mismatch: {clip.Slot}.");
        using var sidecar = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path[..^4] + ".source.json"));
        if (!sidecar.RootElement.GetProperty("fbxSha256").GetString()!
                .Equals(clip.Sha256, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs(sidecar.RootElement.GetProperty("metadata").GetProperty("sequencePlayLength").GetDouble() -
                clip.PlayLength) > 1e-5)
            throw new InvalidOperationException($"Lyra sidecar differs from catalog: {clip.Slot}.");
    }
}

internal sealed class LyraBoundRig : IDisposable
{
    private const string MannequinPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin";

    private LyraBoundRig(Node root, Skeleton3D skeleton, AnimationPlayer player,
        LyraUnarmedCatalog catalog, LyraUnarmedAuxCatalog? auxiliary,
        LyraUnarmedRemainingCatalog? remaining, LyraPistolCatalog? pistol,
        LyraRifleCatalog? rifle)
    {
        Root = root;
        Skeleton = skeleton;
        Player = player;
        Catalog = catalog;
        Auxiliary = auxiliary;
        Remaining = remaining;
        Pistol = pistol;
        Rifle = rifle;
    }

    public Node Root { get; }
    public Skeleton3D Skeleton { get; }
    public AnimationPlayer Player { get; }
    public LyraUnarmedCatalog Catalog { get; }
    public LyraUnarmedAuxCatalog? Auxiliary { get; }
    public LyraUnarmedRemainingCatalog? Remaining { get; }
    public LyraPistolCatalog? Pistol { get; }
    public LyraRifleCatalog? Rifle { get; }

    public static LyraBoundRig Build(bool includeAuxiliary = false, bool includeRemaining = false,
        bool includePistol = false, bool includeRifle = false)
    {
        var catalog = LyraUnarmedCatalog.Load();
        var auxiliary = includeAuxiliary ? LyraUnarmedAuxCatalog.Load() : null;
        var remaining = includeRemaining ? LyraUnarmedRemainingCatalog.Load() : null;
        var pistol = includePistol ? LyraPistolCatalog.Load() : null;
        var rifle = includeRifle ? LyraRifleCatalog.Load() : null;
        var setResource = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS asset set is missing.");
        var set = setResource.LoadDefinition();
        var mesh = set.SkeletalMeshes.Single(asset => asset.ObjectPath == MannequinPath);
        var scene = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(mesh.ResourcePath))
            ?? throw new InvalidOperationException("ALS mannequin scene is missing.");
        var root = scene.Instantiate();
        try
        {
            var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(root)
                ?? throw new InvalidOperationException("ALS mannequin has no skeleton.");
            AlsAnimationBinder.ValidateTargetSkeleton(skeleton, set.Skeletons[mesh.SkeletonId], "Lyra Unarmed");
            if (skeleton.GetBoneCount() != 68) throw new InvalidOperationException("ALS mannequin is not 68-bone.");

            using var library = new AnimationLibrary();
            using var importedName = new StringName("Unreal Take");
            var clips = catalog.Clips.Values.AsEnumerable();
            if (auxiliary is not null) clips = clips.Concat(auxiliary.Clips.Values);
            if (remaining is not null) clips = clips.Concat(remaining.Clips.Values);
            if (pistol is not null) clips = clips.Concat(pistol.Clips.Values);
            if (rifle is not null) clips = clips.Concat(rifle.Clips.Values);
            foreach (var clip in clips)
            {
                LyraUnarmedCatalog.VerifyExport(clip);
                var sourceScene = ResourceLoader.Load<PackedScene>(LyraUnarmedCatalog.ResourcePath(clip))
                    ?? throw new InvalidOperationException($"Lyra clip was not imported: {clip.Slot}.");
                var sourceRoot = sourceScene.Instantiate();
                try
                {
                    var sourcePlayer = AlsImportedResourceAuditor.FindFirst<AnimationPlayer>(sourceRoot)
                        ?? throw new InvalidOperationException($"Lyra clip has no AnimationPlayer: {clip.Slot}.");
                    using var source = sourcePlayer.GetAnimation(importedName);
                    if (source is null || source.GetTrackCount() == 0 ||
                        Math.Abs(source.Length - clip.PlayLength) > 1.0 / 30.0 + 1e-6)
                        throw new InvalidOperationException($"Lyra clip duration/tracks invalid: {clip.Slot}.");
                    using var bound = (Godot.Animation)source.Duplicate(true);
                    AlsAnimationBinder.RewriteTrackPaths(root, skeleton, bound, clip.Slot);
                    bound.LoopMode = clip.Loop || clip.Slot is "idle" or "crouch_idle" or "crouch_entry" or "crouch_exit" or
                        "hipfire_crouch" or
                        "jump_start_loop" or "jump_fall_loop" ||
                        clip.Slot.EndsWith("_cycle", StringComparison.Ordinal) ||
                        clip.Slot.StartsWith("crouch_walk_", StringComparison.Ordinal)
                        ? Godot.Animation.LoopModeEnum.Linear : Godot.Animation.LoopModeEnum.None;
                    if (clip.ForceRootLock)
                    {
                        if (clip.RootMotionRootLock != "RefPose")
                            throw new InvalidOperationException($"Lyra root policy is unsupported: {clip.Slot}.");
                        LockRootToReferencePose(root, skeleton, bound);
                    }
                    using var name = new StringName(clip.Slot);
                    if (library.AddAnimation(name, bound) != Error.Ok)
                        throw new InvalidOperationException($"Could not register Lyra clip: {clip.Slot}.");
                }
                finally
                {
                    sourceRoot.Free();
                }
            }
            var player = new AnimationPlayer { Name = "LyraAnimationPlayer",
                CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
            using (var rootPath = new NodePath("..")) player.RootNode = rootPath;
            root.AddChild(player);
            using var libraryName = new StringName("lyra");
            if (player.AddAnimationLibrary(libraryName, library) != Error.Ok)
                throw new InvalidOperationException("Could not attach the Lyra animation library.");
            return new LyraBoundRig(root, skeleton, player, catalog, auxiliary, remaining, pistol, rifle);
        }
        catch
        {
            root.Free();
            throw;
        }
    }

    public string QualifiedName(string slot)
    {
        _ = SourceClip(slot);
        return "lyra/" + slot;
    }

    public LyraClip SourceClip(string slot) => Catalog.Clips.TryGetValue(slot, out var clip)
        ? clip : Auxiliary?.Clips.TryGetValue(slot, out clip) == true
            ? clip : Remaining?.Clips.TryGetValue(slot, out clip) == true
                ? clip : Pistol?.Clips.TryGetValue(slot, out clip) == true
                    ? clip : Rifle?.Clips.TryGetValue(slot, out clip) == true
                        ? clip : throw new InvalidOperationException($"Missing Lyra slot: {slot}.");

    public void Dispose() => Root.Free();

    private static void LockRootToReferencePose(Node root, Skeleton3D skeleton, Godot.Animation animation)
    {
        var rest = skeleton.GetBoneRest(0);
        var rootName = skeleton.GetBoneName(0).ToString();
        var present = new HashSet<Godot.Animation.TrackType>();
        for (var track = 0; track < animation.GetTrackCount(); track++)
        {
            using var path = animation.TrackGetPath(track);
            if (path.GetSubNameCount() != 1 || path.GetSubName(0) != rootName) continue;
            var type = animation.TrackGetType(track);
            var value = type switch
            {
                Godot.Animation.TrackType.Position3D => Variant.From(rest.Origin),
                Godot.Animation.TrackType.Rotation3D => Variant.From(rest.Basis.GetRotationQuaternion()),
                Godot.Animation.TrackType.Scale3D => Variant.From(rest.Basis.Scale),
                _ => throw new InvalidOperationException("Unsupported Lyra root track type."),
            };
            for (var key = 0; key < animation.TrackGetKeyCount(track); key++)
                animation.TrackSetKeyValue(track, key, value);
            if (animation.TrackGetKeyCount(track) == 0)
                animation.TrackInsertKey(track, 0, value);
            present.Add(type);
        }
        using var skeletonPath = root.GetPathTo(skeleton);
        using var rootPath = new NodePath($"{skeletonPath}:{rootName}");
        foreach (var type in new[] { Godot.Animation.TrackType.Position3D,
                     Godot.Animation.TrackType.Rotation3D, Godot.Animation.TrackType.Scale3D })
        {
            if (present.Contains(type)) continue;
            var track = animation.AddTrack(type);
            animation.TrackSetPath(track, rootPath);
            var value = type switch
            {
                Godot.Animation.TrackType.Position3D => Variant.From(rest.Origin),
                Godot.Animation.TrackType.Rotation3D => Variant.From(rest.Basis.GetRotationQuaternion()),
                _ => Variant.From(rest.Basis.Scale),
            };
            animation.TrackInsertKey(track, 0, value);
        }
    }
}
