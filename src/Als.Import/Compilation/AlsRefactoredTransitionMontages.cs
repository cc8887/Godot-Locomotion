using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original Transition slot resources merged into a caller-owned Refactored
/// bank. The caller reserves animation/group IDs; native indices are provenance only.</summary>
public sealed class AlsRefactoredTransitionMontages
{
    private readonly Dictionary<string, AlsSequenceMontageAsset> _sources;
    private readonly HashSet<AlsRefactoredWeaponNotifyBinding> _bindings;
    private readonly AlsSequenceMontageAsset[] _assets;
    public ReadOnlySpan<AlsSequenceMontageAsset> Assets => _assets;
    public int NativeGroupIndex { get; }
    public int HostGroupId { get; }
    public string CatalogDigest { get; }
    public string SourcePath(int animationId) => _sources.Single(p => p.Value.AnimationId == animationId).Key;
    public AlsRefactoredTransitionMontages(AlsRefactoredAnimationCatalog catalog, string inventoryJson,
        IReadOnlyList<AlsRefactoredWeaponNotifyProfile> profiles, IReadOnlyDictionary<string, int> animationIds, int hostGroupId)
    {
        if (hostGroupId < 0 || !profiles.Select(p => p.Machine.Resources.Kind).Order().SequenceEqual(Enum.GetValues<AlsRefactoredWeaponKind>().Order()) ||
            profiles.Any(p => p.Machine.Resources.CatalogDigest != catalog.IndexDigest)) throw new ArgumentException("Incomplete original weapon binding closure.");
        CatalogDigest = catalog.IndexDigest;
        _bindings = profiles.SelectMany(p => p.Bindings.ToArray()).ToHashSet();
        var paths = _bindings.Select(b => b.Sequence).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (animationIds.Count != paths.Length || paths.Any(p => !animationIds.TryGetValue(p, out var id) || id < 0) ||
            animationIds.Values.Distinct().Count() != paths.Length) throw new ArgumentException("Invalid host transition resource IDs.");
        using var doc = JsonDocument.Parse(inventoryJson); var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("searchRoot").GetString() != "/ALS")
            throw new ArgumentException("Foreign native slot inventory.");
        const string skeleton = "/ALS/ALS/Character/SK_Als.SK_Als";
        var meta = root.GetProperty("skeletons").GetProperty(skeleton);
        var groups = meta.GetProperty("groups").EnumerateArray().ToArray();
        var grounded = groups.Single(g => g.GetProperty("name").GetString() == "Grounded");
        string[] names = ["Transition", "TurnInPlaceStanding", "TurnInPlaceCrouching"];
        if (!grounded.GetProperty("slots").EnumerateArray().Select(v => v.GetString()).SequenceEqual(names) ||
            names.Any(n => groups.Sum(g => g.GetProperty("slots").EnumerateArray().Count(s => s.GetString() == n)) != 1))
            throw new ArgumentException("Grounded slot group closure differs.");
        NativeGroupIndex = grounded.GetProperty("index").GetInt32(); HostGroupId = hostGroupId;
        var line = Regex.Match(meta.GetProperty("nativeText").GetString()!, @"(?m)^\s+SlotGroups\(" + NativeGroupIndex + "\\)=\\(GroupName=\"Grounded\",SlotNames=\\(([^\\r\\n]*)\\)\\)\\r?$");
        if (!line.Success || !Regex.Matches(line.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).SequenceEqual(names))
            throw new ArgumentException("Native Grounded group differs from exported slots.");
        _sources = new(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var source = catalog.Read(path); var evaluation = source.GetProperty("evaluation");
            if (source.GetProperty("raw").GetProperty("skeletonSource").GetString() != skeleton || evaluation.GetProperty("enableRootMotion").GetBoolean() ||
                evaluation.GetProperty("additiveType").GetString() != "AAT_RotationOffsetMeshSpace") throw new ArgumentException("Foreign transition sequence policy.");
            var scale = Regex.Match(source.GetProperty("nativeText").GetString()!, @"(?m)^   RateScale=([^\r\n]+)");
            if (scale.Success && float.Parse(scale.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new ArgumentException("Transition source rate requires explicit segment support.");
            _sources.Add(path, new(animationIds[path], AlsMontageSlot.Transition, hostGroupId, evaluation.GetProperty("sequencePlayLength").GetSingle(), 2));
        }
        _assets = paths.Select(p => _sources[p]).ToArray();
        _ = new AlsMontageRuntime([], sequences: _assets);
    }
    public AlsSequenceMontageCommand Command(in AlsRefactoredWeaponNotifyBinding binding)
    {
        if (!_bindings.Contains(binding)) throw new ArgumentException("Foreign transition binding.");
        var asset = _sources[binding.Sequence];
        return new(asset.AnimationId, asset.Slot, binding.PlayRate, binding.StartTime, binding.BlendIn, binding.BlendOut);
    }
    public void Play(AlsMontageRuntime owner, AlsFrameIdentity identity, ReadOnlySpan<AlsRefactoredWeaponTransitionRequest> requests)
    {
        owner.ValidateCommit(identity);
        var ordinal = -1;
        // Validate the whole batch before mutating the shared physical bank.
        foreach (var request in requests)
        {
            if (request.Identity != identity || request.QueueOrdinal <= ordinal || !_bindings.Contains(request.Binding) ||
                !_sources.TryGetValue(request.Binding.Sequence, out var expected) ||
                !owner.TryGetSequenceAsset(expected.AnimationId, expected.Slot, out var actual) || actual != expected)
                throw new ArgumentException("Foreign transition request or shared bank resource.");
            ordinal = request.QueueOrdinal;
        }
        foreach (var request in requests)
        {
            var binding = request.Binding; var asset = _sources[binding.Sequence];
            if (!owner.PlaySequence(new(asset.AnimationId, asset.Slot, binding.PlayRate, binding.StartTime, binding.BlendIn, binding.BlendOut)))
                throw new InvalidOperationException("Validated transition resource disappeared.");
        }
    }
    public void StopTransitionAndTurnInPlace(AlsMontageRuntime owner, AlsFrameIdentity identity, float blendOut = -1)
    {
        owner.ValidateCommit(identity);
        owner.StopSlots([AlsMontageSlot.Transition, AlsTurnSlot.Standing, AlsTurnSlot.Crouching], blendOut);
    }
}
