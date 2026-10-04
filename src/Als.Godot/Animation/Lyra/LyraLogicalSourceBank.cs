using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Immutable source resources. A character/player creates its own samplers and
// owns all scratch; this bank has no animation clocks or mutable pose cache.
internal sealed class LyraLogicalSourceBank
{
    internal const string Root = "res://assets/generated/lyra_als/logical_controls/";
    internal const string MainLeanRoot = "res://assets/generated/lyra_als/main_lean/";
    internal const string LocomotionExtrasRoot = "res://assets/generated/lyra_als/locomotion_extras/";
    internal const string MontageActionsRoot = "res://assets/generated/lyra_als/montage_actions/";
    private readonly int[] _parents, _mapping, _skin;
    private readonly AlsPrecisePose[] _reference;
    private readonly AlsLogicalVirtualBone[] _virtual;
    private readonly Dictionary<string, LyraLogicalSourceDefinition> _sources;

    private LyraLogicalSourceBank(int[] parents, int[] mapping, int[] skin, AlsPrecisePose[] reference,
        AlsLogicalVirtualBone[] virtualBones, Dictionary<string, LyraLogicalSourceDefinition> sources)
    { _parents = parents; _mapping = mapping; _skin = skin; _reference = reference; _virtual = virtualBones; _sources = sources; }

    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<int> RawMapping => _mapping;
    public ReadOnlySpan<int> SkinLogicalIndices => _skin;
    public ReadOnlySpan<AlsPrecisePose> Reference => _reference;
    public ReadOnlySpan<AlsLogicalVirtualBone> VirtualBones => _virtual;
    public IEnumerable<string> Slots => _sources.Keys;
    public int Count => _sources.Count;
    public LyraSourceCurveBank Curves { get; private set; } = null!;
    public string CalibrationSha256 { get; private init; } = "";
    public string CatalogSha256 { get; private init; } = "";
    public string? MainLeanCatalogSha256 { get; private init; }
    public string? LocomotionExtrasCatalogSha256 { get; private init; }
    public string? MontageActionsCatalogSha256 { get; private init; }
    public LyraLogicalSourceDefinition Get(string slot) => _sources.TryGetValue(slot, out var source)
        ? source : throw new InvalidOperationException("Missing Lyra logical source: " + slot);
    public LyraLogicalSourceSampler CreateSampler(string slot) => new(this, Get(slot));
    public string SlotForSource(string source) => _sources.Values.Single(v => v.Source == source).Slot;
    public int Bone(string name)
    {
        // Names are validated against this calibration when the bank is loaded.
        return _boneNames.TryGetValue(name, out var bone) ? bone : throw new InvalidOperationException("Missing logical bone: " + name);
    }
    private Dictionary<string, int> _boneNames = new(StringComparer.OrdinalIgnoreCase);

    public static LyraLogicalSourceBank Load(bool includeMainLean = false, bool includeLocomotionExtras = false,
        bool includeMontageActions = false)
    {
        var calibrationBytes = Godot.FileAccess.GetFileAsBytes(Root + "calibration.json");
        var calibrationSha = Sha(calibrationBytes);
        using var sampling = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + "sampling.json"));
        if (sampling.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
            sampling.RootElement.GetProperty("calibrationSha256").GetString() != calibrationSha ||
            sampling.RootElement.GetProperty("frameTimeRounding").GetString() != "RoundSubframe")
            throw new InvalidOperationException("Unsupported Lyra native sampling profile.");
        using var calibration = JsonDocument.Parse(calibrationBytes);
        var contract = calibration.RootElement;
        if (contract.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported Lyra logical source calibration.");
        foreach (var dependency in contract.GetProperty("dependencySha256").EnumerateObject())
            if (Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + dependency.Name)) != dependency.Value.GetString())
                throw new InvalidOperationException("Stale Lyra logical calibration: " + dependency.Name);
        var layout = contract.GetProperty("layout");
        var names = layout.GetProperty("logicalBoneNames").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var ids = names.Select((name, id) => (name, id)).ToDictionary(v => v.name, v => v.id, StringComparer.OrdinalIgnoreCase);
        var parents = layout.GetProperty("logicalParents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var mapping = layout.GetProperty("logicalToPhysical").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var skin = contract.GetProperty("skinLogicalIndices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var reference = layout.GetProperty("referencePose").EnumerateArray().Select(ParsePose).ToArray();
        var modes = layout.GetProperty("translationRetargetModes").EnumerateArray().Select(v => v.GetString() switch
        { "Animation" => 0, "Skeleton" => 1, _ => throw new NotSupportedException("Unexpected ALS control retarget mode.") }).ToArray();
        var virtualBones = layout.GetProperty("virtualBones").EnumerateArray().Select(v => new AlsLogicalVirtualBone(
            v.GetProperty("bone").GetInt32(), v.GetProperty("source").GetInt32(), v.GetProperty("target").GetInt32())).ToArray();
        if (names.Length != 81 || parents.Length != 81 || mapping.Length != 81 || reference.Length != 81 ||
            modes.Length != 69 || virtualBones.Length != 12 || skin.Length != 68 ||
            !skin.SequenceEqual(Enumerable.Range(0, 68)) || names[68] != "weapon_r" ||
            names[80] != "VB IK_Hand_L_weaponSpace" || mapping[68] != 68 || mapping[80] != -1 ||
            parents[68] != ids["hand_r"] || parents[80] != 68 ||
            !virtualBones.Contains(new(80, 68, ids["hand_l"])))
            throw new InvalidOperationException("Lyra logical source does not preserve ALS skin/control identities.");
        var catalogBytes = Godot.FileAccess.GetFileAsBytes(Root + "catalog.json");
        using var catalog = JsonDocument.Parse(catalogBytes);
        var catalogRoot = catalog.RootElement;
        if (catalogRoot.GetProperty("schemaVersion").GetInt32() != 1 ||
            catalogRoot.GetProperty("calibrationSha256").GetString() != calibrationSha)
            throw new InvalidOperationException("Stale Lyra logical source catalog.");
        var rows = catalogRoot.GetProperty("entries").EnumerateArray().ToArray();
        if (rows.Length != 234 || rows.Count(r => r.GetProperty("additive").GetBoolean()) != 45)
            throw new InvalidOperationException("Incomplete original Lyra source catalog.");
        var leanBytes = includeMainLean ? Godot.FileAccess.GetFileAsBytes(MainLeanRoot + "catalog.json") : null;
        using var lean = leanBytes is null ? null : JsonDocument.Parse(leanBytes);
        var leanSlots = new HashSet<string>(StringComparer.Ordinal);
        if (lean is not null)
        {
            var extension = lean.RootElement;
            var inventoryBytes = Godot.FileAccess.GetFileAsBytes(MainLeanRoot + "inventory.json");
            using var inventory = JsonDocument.Parse(inventoryBytes);
            if (extension.GetProperty("schemaVersion").GetInt32() != 1 ||
                extension.GetProperty("baseCatalogSha256").GetString() != Sha(catalogBytes) ||
                extension.GetProperty("calibrationSha256").GetString() != calibrationSha ||
                extension.GetProperty("inventorySha256").GetString() != Sha(inventoryBytes) ||
                extension.GetProperty("skinPreservation").GetDouble() != 0 ||
                inventory.RootElement.GetProperty("sourceNodesSha256").GetString() !=
                    Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/source_nodes.json")))
                throw new InvalidOperationException("Stale Main Lean extension.");
            var additions = extension.GetProperty("entries").EnumerateArray().ToArray();
            var samples = inventory.RootElement.GetProperty("samples");
            var slots = new[] { "main_lean_center", "main_lean_left", "main_lean_right" };
            if (additions.Length != 3 || samples.GetArrayLength() != 3)
                throw new InvalidOperationException("Incomplete Main Lean samples.");
            for (var i = 0; i < additions.Length; i++)
            {
                var row = additions[i]; var sample = samples[i];
                if (row.GetProperty("slot").GetString() != slots[i] || row.GetProperty("category").GetString() != "main_lean" ||
                    row.GetProperty("sampleIndex").GetInt32() != i || !row.GetProperty("additive").GetBoolean() ||
                    row.GetProperty("source").GetString() != sample.GetProperty("source").GetString() ||
                    row.GetProperty("rateScale").GetSingle() != sample.GetProperty("rateScale").GetSingle() ||
                    !row.GetProperty("position").EnumerateArray().Select(v => v.GetDouble())
                        .SequenceEqual(sample.GetProperty("position").EnumerateArray().Select(v => v.GetDouble())))
                    throw new InvalidOperationException("Changed Main Lean sample identity.");
                leanSlots.Add(slots[i]);
            }
            rows = rows.Concat(additions).ToArray();
        }
        var extrasBytes = includeLocomotionExtras ? Godot.FileAccess.GetFileAsBytes(LocomotionExtrasRoot + "catalog.json") : null;
        using var extras = extrasBytes is null ? null : JsonDocument.Parse(extrasBytes);
        var extraSlots = new HashSet<string>(StringComparer.Ordinal);
        if (extras is not null)
        {
            var extension = extras.RootElement;
            if (extension.GetProperty("schemaVersion").GetInt32() != 1 || extension.GetProperty("skinPreservation").GetDouble() != 0)
                throw new InvalidOperationException("Changed locomotion extension profile.");
            foreach (var dependency in extension.GetProperty("dependencies").EnumerateObject())
                if (Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + dependency.Name)) != dependency.Value.GetString())
                    throw new InvalidOperationException("Stale locomotion resource dependency: " + dependency.Name);
            var additions = extension.GetProperty("entries").EnumerateArray().ToArray();
            if (additions.Length != 8 || additions.Count(r => r.GetProperty("additive").GetBoolean()) != 3)
                throw new InvalidOperationException("Incomplete locomotion extras.");
            foreach (var row in additions)
                if (row.GetProperty("category").GetString() != "locomotion_extras" || !extraSlots.Add(row.GetProperty("slot").GetString()!))
                    throw new InvalidOperationException("Changed or repeated locomotion extra.");
            rows = rows.Concat(additions).ToArray();
        }
        var montageBytes = includeMontageActions ? Godot.FileAccess.GetFileAsBytes(MontageActionsRoot + "catalog.json") : null;
        using var montage = montageBytes is null ? null : JsonDocument.Parse(montageBytes);
        var montageSlots = new HashSet<string>(StringComparer.Ordinal);
        if (montage is not null)
        {
            var extension = montage.RootElement;
            if (extension.GetProperty("schemaVersion").GetInt32() != 1 || extension.GetProperty("skinPreservation").GetDouble() != 0)
                throw new InvalidOperationException("Changed Montage action profile.");
            foreach (var dependency in extension.GetProperty("dependencies").EnumerateObject())
                if (Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + dependency.Name)) != dependency.Value.GetString())
                    throw new InvalidOperationException("Stale Montage action dependency: " + dependency.Name);
            var additions = extension.GetProperty("entries").EnumerateArray().ToArray();
            if (additions.Length != 55 || additions.Count(r => r.GetProperty("additive").GetBoolean()) != 27)
                throw new InvalidOperationException("Incomplete Montage action resources.");
            foreach (var row in additions)
                if (row.GetProperty("category").GetString() != "montage_actions" || !montageSlots.Add(row.GetProperty("slot").GetString()!))
                    throw new InvalidOperationException("Changed or repeated Montage action.");
            rows = rows.Concat(additions).ToArray();
        }
        var slotsByTarget = rows.ToDictionary(r => r.GetProperty("target").GetString()!, r => r.GetProperty("slot").GetString()!);
        var sources = new Dictionary<string, LyraLogicalSourceDefinition>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var file = row.GetProperty("file").GetString()!;
            if (!file.StartsWith("clips/", StringComparison.Ordinal) || file.Contains("..", StringComparison.Ordinal) ||
                !file.EndsWith(".json", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid logical source path.");
            var isMainLean = leanSlots.Contains(row.GetProperty("slot").GetString()!);
            var isExtra = extraSlots.Contains(row.GetProperty("slot").GetString()!);
            var isMontage = montageSlots.Contains(row.GetProperty("slot").GetString()!);
            var bytes = Godot.FileAccess.GetFileAsBytes((isMainLean ? MainLeanRoot : isExtra ? LocomotionExtrasRoot : isMontage ? MontageActionsRoot : Root) + file);
            if (Sha(bytes) != row.GetProperty("sha256").GetString()) throw new InvalidOperationException("Changed logical source: " + file);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement; var raw = root.GetProperty("raw"); var metadata = root.GetProperty("metadata");
            var slot = row.GetProperty("slot").GetString()!;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("calibrationSha256").GetString() != calibrationSha ||
                root.GetProperty("source").GetString() != row.GetProperty("source").GetString() ||
                root.GetProperty("target").GetString() != row.GetProperty("target").GetString() ||
                raw.GetProperty("source").GetString() != row.GetProperty("target").GetString() ||
                raw.GetProperty("skeletonSource").GetString() != layout.GetProperty("source").GetString())
                throw new InvalidOperationException("Logical source identity changed: " + slot);
            var keyCount = raw.GetProperty("sampledKeyCount").GetInt32();
            var length = raw.GetProperty("playLength").GetDouble();
            if (keyCount != row.GetProperty("keyCount").GetInt32() || length != row.GetProperty("playLength").GetDouble())
                throw new InvalidOperationException("Logical source time contract changed: " + slot);
            var presence = new bool[81];
            var physical = new AlsLocalPose[checked(keyCount * 69)];
            var virtualKeys = new AlsLocalPose[checked(keyCount * 12)];
            foreach (var track in raw.GetProperty("tracks").EnumerateArray())
            {
                if (!ids.TryGetValue(track.GetProperty("bone").GetString()!, out var logical) || presence[logical])
                    throw new InvalidOperationException("Foreign or duplicate logical track: " + slot);
                presence[logical] = true;
                var p = track.GetProperty("positions"); var q = track.GetProperty("rotations"); var s = track.GetProperty("scales");
                if (p.GetArrayLength() != 1 && p.GetArrayLength() != keyCount ||
                    q.GetArrayLength() != 1 && q.GetArrayLength() != keyCount ||
                    s.GetArrayLength() != 0 && s.GetArrayLength() != 1 && s.GetArrayLength() != keyCount)
                    throw new InvalidOperationException("Unsupported raw channel length: " + slot);
                var rawIndex = mapping[logical];
                var virtualIndex = Array.FindIndex(virtualBones, v => v.Bone == logical);
                for (var key = 0; key < keyCount; key++)
                {
                    var position = p[p.GetArrayLength() == 1 ? 0 : key];
                    var rotation = q[q.GetArrayLength() == 1 ? 0 : key];
                    var scale = s.GetArrayLength() == 0 ? Vector3.One : Vector(s[s.GetArrayLength() == 1 ? 0 : key]);
                    var atom = new AlsLocalPose(Vector(position), new Quaternion(rotation[0].GetSingle(),
                        rotation[1].GetSingle(), rotation[2].GetSingle(), rotation[3].GetSingle()), scale);
                    if (rawIndex >= 0) physical[key * 69 + rawIndex] = atom;
                    else virtualKeys[key * 12 + virtualIndex] = atom;
                }
            }
            if (presence[80] || (isMontage ? presence[68] != row.GetProperty("weaponTrackPresent").GetBoolean() ||
                    presence[68] != root.GetProperty("weaponTrackPresent").GetBoolean() : !presence[68]))
                throw new InvalidOperationException("Weapon track presence changed or its VB was not generated at source keys.");
            var interpolation = metadata.GetProperty("interpolation").GetString() switch
            { "Linear" => AlsRawAnimationInterpolation.Linear, "Step" => AlsRawAnimationInterpolation.Step,
                _ => throw new NotSupportedException("Unknown raw interpolation.") };
            var data = new AlsRawAnimationPoseData(new(sources.Count, slot, row.GetProperty("target").GetString()!, 0),
                raw.GetProperty("frameRateNumerator").GetInt32(), raw.GetProperty("frameRateDenominator").GetInt32(),
                keyCount, length, interpolation, mapping, virtualBones.Select(v => v.Bone).ToArray(), presence, physical, virtualKeys);
            var authored = metadata.GetProperty("retargetSourceAssetReferencePose").EnumerateArray().Select(ParsePose).ToArray();
            var retarget = new AlsPrecisePoseRetargetModel(mapping, modes, presence, reference, authored);
            var additive = row.GetProperty("additive").GetBoolean();
            var additiveType = metadata.GetProperty("additiveType").GetString();
            string? baseSlot = null;
            double baseTime = 0;
            if (additive)
            {
                var localFrame = metadata.GetProperty("basePoseType").GetString() == "ABPT_LocalAnimFrame";
                var frame = metadata.GetProperty("baseFrame").GetInt32();
                if ((isMontage ? additiveType is not ("AAT_LocalSpaceBase" or "AAT_RotationOffsetMeshSpace") :
                        additiveType != (isMainLean || isExtra ? "AAT_LocalSpaceBase" : "AAT_RotationOffsetMeshSpace")) ||
                    (!localFrame && metadata.GetProperty("basePoseType").GetString() != "ABPT_AnimFrame") ||
                    frame < 0 || (!isExtra && !isMontage && (localFrame || frame != 0)))
                    throw new NotSupportedException("Changed original additive base policy.");
                if (localFrame)
                {
                    if (metadata.GetProperty("baseAsset").ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Local frame must use its own sequence.");
                    baseSlot = slot;
                }
                else if (!slotsByTarget.TryGetValue(metadata.GetProperty("baseAsset").GetString()!, out baseSlot))
                    throw new InvalidOperationException("Missing original animation-frame base.");
                var baseRow = rows.Single(r => r.GetProperty("slot").GetString() == baseSlot);
                // UE GetSequencePose divides by sampled keys, then multiplies
                // the base sequence play length. This differs from frame/fps.
                var baseLength = (isExtra || isMontage) && baseRow.TryGetProperty("sequencePlayLength", out var sequenceLength)
                    ? sequenceLength.GetDouble() : baseRow.GetProperty("playLength").GetDouble();
                baseTime = baseLength * Math.Clamp((double)frame / baseRow.GetProperty("keyCount").GetInt32(), 0, 1);
                if ((isExtra || isMontage) && (row.GetProperty("baseSlot").GetString() != baseSlot || row.GetProperty("baseSampleTime").GetDouble() != baseTime))
                    throw new InvalidOperationException("Changed locomotion additive base time.");
                if (isMainLean && baseSlot != "main_lean_center")
                    throw new InvalidOperationException("Changed Main Lean additive base.");
            }
            else if (metadata.GetProperty("additiveType").GetString() != "AAT_None")
                throw new InvalidOperationException("Ordinary source unexpectedly additive.");
            var lockMode = metadata.GetProperty("rootMotionRootLock").GetString() switch
            { "RefPose" => 0, "AnimFirstFrame" => 1, "Zero" => 2, _ => throw new NotSupportedException("Unsupported raw root lock.") };
            sources.Add(slot, new(slot, row.GetProperty("source").GetString()!, data, retarget, additive, baseSlot, lockMode,
                metadata.GetProperty("forceRootLock").GetBoolean(), ParsePose(metadata.GetProperty("rootLockFirstFrame")),
                Array.AsReadOnly(metadata.GetProperty("floatCurveNames").EnumerateArray().Select(c => c.GetString()!).ToArray()),
                metadata.GetProperty("animatedBoneAttributeCount").GetInt32(), metadata.GetProperty("transformCurveCount").GetInt32(),
                metadata.GetProperty("enableRootMotion").GetBoolean(), metadata.GetProperty("useNormalizedRootMotionScale").GetBoolean(),
                isMontage ? additiveType == "AAT_RotationOffsetMeshSpace" : !isMainLean && !isExtra, baseTime));
        }
        if (sources.Count != 234 + (includeMainLean ? 3 : 0) + (includeLocomotionExtras ? 8 : 0) + (includeMontageActions ? 55 : 0) ||
            sources.Values.Count(v => v.IsAdditive) != 45 + (includeMainLean ? 3 : 0) + (includeLocomotionExtras ? 3 : 0) + (includeMontageActions ? 27 : 0))
            throw new InvalidOperationException("Incomplete logical source closure.");
        var result = new LyraLogicalSourceBank(parents, mapping, skin, reference, virtualBones, sources)
        { CalibrationSha256 = calibrationSha, CatalogSha256 = Sha(catalogBytes),
            MainLeanCatalogSha256 = leanBytes is null ? null : Sha(leanBytes),
            LocomotionExtrasCatalogSha256 = extrasBytes is null ? null : Sha(extrasBytes),
            MontageActionsCatalogSha256 = montageBytes is null ? null : Sha(montageBytes), _boneNames = ids };
        var additionalCurves = (lean?.RootElement.GetProperty("curves").EnumerateArray().ToArray() ?? [])
            .Concat(extras?.RootElement.GetProperty("curves").EnumerateArray().ToArray() ?? [])
            .Concat(montage?.RootElement.GetProperty("curves").EnumerateArray().ToArray() ?? []).ToArray();
        // Montage-only curves belong to the same complete pose layout. They
        // remain absent in sequence samples and enter at native Curve.Combine.
        using var montageMetadata = includeMontageActions ? JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/montage_catalog_v2.json")) : null;
        var montageCurveNames = montageMetadata?.RootElement.GetProperty("assets").EnumerateArray()
            .SelectMany(a => a.GetProperty("curves").GetProperty("curves").EnumerateArray())
            .Select(c => c.GetProperty("name").GetString()!).ToArray();
        result.Curves = LyraSourceCurveBank.Load(result, additionalCurves, montageCurveNames);
        return result;
    }

    internal static AlsPrecisePose ParsePose(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        var pose = new AlsPrecisePose(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()),
            new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
        pose.Validate(1e-5); return pose;
    }
    private static Vector3 Vector(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    internal static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record LyraLogicalSourceDefinition(string Slot, string Source, AlsRawAnimationPoseData Data,
    AlsPrecisePoseRetargetModel Retarget, bool IsAdditive, string? BaseSlot, int RootLock,
    bool ForceRootLock, AlsPrecisePose AnimationFirstFrame, IReadOnlyList<string> FloatCurveNames,
    int AttributeCount, int TransformCurveCount, bool EnableRootMotion, bool NormalizedRootMotionScale,
    bool MeshSpaceAdditive = true, double BaseSampleTime = 0);

// One occurrence owns this scratch. Sampling generates/interpolates missing VBs
// first, applies tracked-bone translation retargeting/root lock, then additive.
internal sealed class LyraLogicalSourceSampler
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraLogicalSourceDefinition _definition;
    private readonly AlsPreciseRawSequenceSampler _raw;
    private readonly LyraLogicalSourceSampler? _base;
    private readonly AlsPrecisePose[] _absolute = new AlsPrecisePose[81], _reference = new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _rotations = new AlsQuaternion[162];
    private int _sampling;
    public LyraLogicalSourceSampler(LyraLogicalSourceBank bank, LyraLogicalSourceDefinition definition)
    {
        _bank = bank; _definition = definition;
        _raw = new(definition.Data, bank.Parents, bank.Reference, bank.VirtualBones, AlsRawFrameTimeRounding.RoundSubframe);
        if (definition.BaseSlot is not null && definition.BaseSlot != definition.Slot)
            _base = bank.CreateSampler(definition.BaseSlot);
    }
    public void SampleRaw(double seconds, Span<AlsPrecisePose> output) => _raw.Sample(seconds, output);
    public void Sample(double seconds, Span<AlsPrecisePose> output, Span<LyraCurveSample> curves)
    {
        // Validate curve output before evaluating the pose or touching scratch.
        if (curves.Length != _bank.Curves.Names.Length || !float.IsFinite((float)seconds)) throw new ArgumentException("Incomplete source curve buffer/time.");
        Sample(seconds, output);
        _bank.Curves.Sample(_definition.Slot, seconds, curves);
    }
    public void Sample(double seconds, Span<AlsPrecisePose> output, Span<LyraCurveSample> curves,
        Span<LyraAttributeSample> attributes)
        => Sample(seconds, output, curves, attributes, false);
    public void Sample(double seconds, Span<AlsPrecisePose> output, Span<LyraCurveSample> curves,
        Span<LyraAttributeSample> attributes, bool extractRootMotion)
    {
        if (attributes.Length != _bank.Curves.Attributes.Layout.Length) throw new ArgumentException("Incomplete source attribute buffer.");
        if (curves.Length != _bank.Curves.Names.Length || !float.IsFinite((float)seconds)) throw new ArgumentException("Incomplete source curve buffer/time.");
        Sample(seconds, output, extractRootMotion);
        _bank.Curves.Sample(_definition.Slot, seconds, curves);
        _bank.Curves.Attributes.Sample(_definition.Slot, seconds, attributes);
    }
    public void Sample(double seconds, Span<AlsPrecisePose> output)
        => Sample(seconds, output, false);
    public void Sample(double seconds, Span<AlsPrecisePose> output, bool extractRootMotion)
    {
        if (output.Length != 81 || !double.IsFinite(seconds)) throw new ArgumentException("Logical sample requires all 81 bones and finite time.");
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0) throw new InvalidOperationException("Logical sampler occurrence is already in use.");
        try
        {
            SampleAbsolute(seconds, _absolute, extractRootMotion);
            if (!_definition.IsAdditive) { _absolute.CopyTo(output); return; }
            if (_base is null) SampleAbsolute(_definition.BaseSampleTime, _reference, extractRootMotion); else _base.SampleAbsolute(_definition.BaseSampleTime, _reference, extractRootMotion);
            if (_definition.MeshSpaceAdditive)
                AlsPrecisePoseBlender.MeshDifference(_absolute, _reference, _bank.Parents, _rotations, output,isPc:true);
            else
                for (var bone = 0; bone < output.Length; bone++)
                    output[bone] = AlsPrecisePoseBlender.LocalDifference(_absolute[bone], _reference[bone], isPc:true);
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
    private void SampleAbsolute(double seconds, Span<AlsPrecisePose> output, bool extractRootMotion = false)
    {
        _raw.Sample(seconds, output);
        _definition.Retarget.Apply(output, true);
        if (_definition.ForceRootLock || extractRootMotion && _definition.EnableRootMotion) output[0] = _definition.RootLock switch
        { 0 => _bank.Reference[0], 1 => _definition.AnimationFirstFrame, _ => AlsPrecisePose.Identity };
    }
}
