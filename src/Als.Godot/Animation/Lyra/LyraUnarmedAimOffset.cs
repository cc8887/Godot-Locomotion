using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraAimWeight(int Sample, float Weight);

internal sealed class LyraUnarmedAimOffset : ILyraAimingLayer
{
    private const string Root = "res://assets/generated/lyra_als/";
    private const string TargetSkeleton =
        "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton";
    private readonly Skeleton3D _skeleton;
    private readonly AlsLocalPose[][] _samples;
    private readonly (int[] Indices, Vector2[] Vertices)[] _triangles;
    private readonly Vector2[] _points;
    private readonly int[] _parents;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _additive;
    private readonly AlsLocalPose[] _output;
    private readonly NQuaternion[] _rotationScratch;
    private bool _hasApplied;
    private readonly string _profileName;
    private LyraLogicalSourceBank? _logicalBank;
    private AlsPrecisePose[][]? _logicalSamples;
    private readonly AlsPrecisePose[] _logicalAdditive = new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _logicalScratch = new AlsQuaternion[81 * 2];

    private LyraUnarmedAimOffset(Skeleton3D skeleton, AlsLocalPose[][] samples,
        Vector2[] points, (int[] Indices, Vector2[] Vertices)[] triangles, string profileName)
    {
        _skeleton = skeleton;
        _profileName = profileName;
        _samples = samples;
        _points = points;
        _triangles = triangles;
        var bones = skeleton.GetBoneCount();
        _parents = Enumerable.Range(0, bones).Select(skeleton.GetBoneParent).ToArray();
        _basis = new AlsLocalPose[bones];
        _additive = new AlsLocalPose[bones];
        _output = new AlsLocalPose[bones];
        _rotationScratch = new NQuaternion[bones * 2];
    }

    public int AppliedFrames { get; private set; }
    internal Skeleton3D Skeleton => _skeleton;

    public static LyraUnarmedAimOffset Load(Skeleton3D skeleton) => LoadProfile(skeleton, "unarmed");

    public static LyraUnarmedAimOffset LoadPistol(Skeleton3D skeleton) => LoadProfile(skeleton, "pistol");

    public static LyraUnarmedAimOffset LoadRifle(Skeleton3D skeleton) => LoadProfile(skeleton, "rifle");

    private static LyraUnarmedAimOffset LoadProfile(Skeleton3D skeleton, string profileName)
    {
        if (skeleton.GetBoneCount() != 68)
            throw new InvalidOperationException("Lyra AimOffset requires the ALS 68-bone mannequin.");
        var inventoryBytes = Godot.FileAccess.GetFileAsBytes(
            Root + profileName + "_special_inventory.json");
        using var inventory = JsonDocument.Parse(inventoryBytes);
        var inventoryRoot = inventory.RootElement;
        var inventorySamples = inventoryRoot.GetProperty("aimOffset").GetProperty("samples");
        var source = inventoryRoot.GetProperty("aimOffset").GetProperty("source").GetString()!;
        var profile = LyraLinkedLayerInventory.Load().Get(profileName);
        if (profile.Asset("IdleAimOffset") != source ||
            profileName == "unarmed" && profile.Asset("RelaxedAimOffset") != source)
            throw new InvalidOperationException(profileName + " Aiming no longer binds the exported AimOffset.");

        using var catalog = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            Root + profileName + "_aim_samples_catalog.json"));
        var root = catalog.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("mode").GetString() != "aim" ||
            root.GetProperty("targetSkeleton").GetString() != TargetSkeleton ||
            root.GetProperty("inventorySha256").GetString() !=
            Convert.ToHexString(SHA256.HashData(inventoryBytes)).ToLowerInvariant())
            throw new InvalidOperationException("Lyra AimOffset catalog has the wrong inventory or skeleton.");
        var names = root.GetProperty("logicalBoneNames");
        var mapping = root.GetProperty("logicalToPhysical");
        if (names.GetArrayLength() != 79 || mapping.GetArrayLength() != 79 ||
            inventorySamples.GetArrayLength() != 15 || root.GetProperty("clips").GetArrayLength() != 15)
            throw new InvalidOperationException("Lyra AimOffset bone or sample count changed.");
        var physical = new int[79];
        var seen = new bool[68];
        for (var logical = 0; logical < physical.Length; logical++)
        {
            var index = mapping[logical].GetInt32();
            physical[logical] = index;
            if (index < 0) continue;
            if (index >= 68 || seen[index] ||
                !string.Equals(names[logical].GetString(), skeleton.GetBoneName(index).ToString(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Lyra AimOffset ALS bone map changed.");
            seen[index] = true;
        }
        if (seen.Any(value => !value)) throw new InvalidOperationException("Lyra AimOffset omits an ALS bone.");

        var samples = new AlsLocalPose[15][];
        var points = new Vector2[15];
        foreach (var row in root.GetProperty("clips").EnumerateArray())
        {
            var index = row.GetProperty("index").GetInt32();
            if (index is < 0 or >= 15 || samples[index] is not null ||
                row.GetProperty("source").GetString() != inventorySamples[index].GetProperty("source").GetString() ||
                row.GetProperty("sourceUassetSha256").GetString() !=
                inventorySamples[index].GetProperty("sourceUassetSha256").GetString() ||
                row.GetProperty("metadata").GetProperty("additiveType").GetString() !=
                "AAT_RotationOffsetMeshSpace" ||
                row.GetProperty("metadata").GetProperty("basePoseType").GetString() != "ABPT_AnimFrame")
                throw new InvalidOperationException("Lyra AimOffset sample binding changed.");
            var coordinates = row.GetProperty("point");
            points[index] = new Vector2(coordinates[0].GetSingle(), coordinates[1].GetSingle());
            var pose = row.GetProperty("evaluatedPoses")[0].GetProperty("pose");
            if (pose.GetArrayLength() != 79)
                throw new InvalidOperationException("Lyra AimOffset sample has the wrong pose layout.");
            samples[index] = new AlsLocalPose[68];
            for (var logical = 0; logical < 79; logical++)
            {
                var bone = physical[logical];
                if (bone < 0) continue;
                var atom = pose[logical];
                var p = atom.GetProperty("position");
                var q = atom.GetProperty("rotation");
                var s = atom.GetProperty("scale");
                // Mesh-space rotations use the UE component axes; additive translations remain bone local.
                samples[index][bone] = new AlsLocalPose(
                    new NVector3(p[0].GetSingle(), -p[1].GetSingle(), p[2].GetSingle()) * 0.01f,
                    NQuaternion.Normalize(new NQuaternion(-q[1].GetSingle(), -q[2].GetSingle(),
                        q[0].GetSingle(), q[3].GetSingle())),
                    new NVector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
            }
        }
        if (samples.Any(sample => sample is null))
            throw new InvalidOperationException("Lyra AimOffset sample grid is incomplete.");

        using var grid = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            Root + profileName + "_aim_native_grid.json"));
        var gridRoot = grid.RootElement;
        if (gridRoot.GetProperty("inventorySha256").GetString() !=
                root.GetProperty("inventorySha256").GetString() ||
            gridRoot.GetProperty("source").GetString() != source)
            throw new InvalidOperationException("Lyra AimOffset triangulation has the wrong source.");
        var triangles = gridRoot.GetProperty("data").GetProperty("data").GetProperty("triangles")
            .EnumerateArray().Select(row =>
            {
                var indices = row.GetProperty("sampleIndices").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray();
                var vertices = row.GetProperty("vertices").EnumerateArray()
                    .Select(value => new Vector2(value.GetProperty("x").GetSingle(),
                        value.GetProperty("y").GetSingle())).ToArray();
                if (indices.Length != 3 || vertices.Length != 3 || indices.Any(index => index is < 0 or >= 15))
                    throw new InvalidOperationException("Lyra AimOffset triangulation is incomplete.");
                for (var corner = 0; corner < 3; corner++)
                {
                    var normalized = new Vector2((points[indices[corner]].X + 180) / 360,
                        (points[indices[corner]].Y + 90) / 180);
                    if (normalized.DistanceTo(vertices[corner]) > 1e-5f)
                        throw new InvalidOperationException("Lyra AimOffset triangle differs from its samples.");
                }
                return (indices, vertices);
            }).ToArray();
        if (triangles.Length != 16)
            throw new InvalidOperationException("Lyra AimOffset expected 16 native triangles.");
        return new LyraUnarmedAimOffset(skeleton, samples, points, triangles, profileName);
    }

    public int SampleWeights(float yaw, float pitch, Span<LyraAimWeight> output)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || output.Length < 3)
            throw new ArgumentOutOfRangeException(nameof(yaw));
        var point = new Vector2(Mathf.Clamp(yaw, -180, 180), Mathf.Clamp(pitch, -90, 90));
        for (var sample = 0; sample < _points.Length; sample++)
        {
            if (point.DistanceSquaredTo(_points[sample]) > 1e-8f) continue;
            output[0] = new LyraAimWeight(sample, 1);
            return 1;
        }
        var normalized = new Vector2((point.X + 180) / 360, (point.Y + 90) / 180);
        foreach (var (indices, vertices) in _triangles)
        {
            var a = vertices[0]; var b = vertices[1]; var c = vertices[2];
            var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (MathF.Abs(denominator) < 1e-8f) continue;
            var wa = ((b.Y - c.Y) * (normalized.X - c.X) +
                      (c.X - b.X) * (normalized.Y - c.Y)) / denominator;
            var wb = ((c.Y - a.Y) * (normalized.X - c.X) +
                      (a.X - c.X) * (normalized.Y - c.Y)) / denominator;
            var wc = 1 - wa - wb;
            if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
            Span<float> weights = stackalloc float[3] { MathF.Max(0, wa), MathF.Max(0, wb), MathF.Max(0, wc) };
            var total = weights[0] + weights[1] + weights[2];
            var count = 0;
            for (var corner = 0; corner < 3; corner++)
                if (weights[corner] / total > AlsPoseBlender.WeightThreshold)
                    output[count++] = new LyraAimWeight(indices[corner], weights[corner] / total);
            return count;
        }
        throw new InvalidOperationException("Lyra AimOffset input is outside its authored grid.");
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void ConfigureLogical(LyraLogicalSourceBank bank)
    {
        _logicalBank = bank;
        _logicalSamples = new AlsPrecisePose[15][];
        using var catalog = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "catalog.json"));
        for (var index = 0; index < 15; index++)
        {
            var slot = $"aim_{_profileName}_{index}";
            var row = catalog.RootElement.GetProperty("entries").EnumerateArray().Single(v => v.GetProperty("slot").GetString() == slot);
            var point = row.GetProperty("point");
            if (!bank.Get(slot).IsAdditive || point[0].GetSingle() != _points[index].X || point[1].GetSingle() != _points[index].Y)
                throw new InvalidOperationException("Logical AimOffset sample grid differs.");
            _logicalSamples[index] = new AlsPrecisePose[81];
            bank.CreateSampler(slot).Sample(0, _logicalSamples[index]);
        }
    }

    internal void SampleAdditive(float yaw, float pitch, Span<AlsPrecisePose> destination)
    {
        if (destination.Length != 81 || _logicalSamples is null) throw new ArgumentException("Incomplete logical AimOffset.");
        Span<LyraAimWeight> weights = stackalloc LyraAimWeight[3];
        var count = SampleWeights(yaw, pitch, weights);
        for (var bone = 0; bone < 81; bone++)
        {
            var mixed = AlsPrecisePoseBlender.Scale(_logicalSamples[weights[0].Sample][bone], weights[0].Weight);
            for (var source = 1; source < count; source++)
                mixed = AlsPrecisePoseBlender.Accumulate(mixed, _logicalSamples[weights[source].Sample][bone], weights[source].Weight);
            destination[bone] = mixed.Normalized();
        }
    }

    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float yaw, float pitch,
        float aimOffsetBlendWeight, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        var bank = _logicalBank ?? throw new InvalidOperationException("Logical Aiming is not configured.");
        SampleAdditive(yaw, pitch, _logicalAdditive);
        AlsPrecisePoseBlender.MeshApply(input, _logicalAdditive, bank.Parents, _logicalScratch, output, 1);
        AppliedFrames++;
    }

    internal void SampleAdditive(float yaw, float pitch, Span<AlsLocalPose> destination)
    {
        if (destination.Length < _samples[0].Length)
            throw new ArgumentException("Lyra AimOffset output pose is too short.", nameof(destination));
        Span<LyraAimWeight> weights = stackalloc LyraAimWeight[3];
        var count = SampleWeights(yaw, pitch, weights);
        for (var bone = 0; bone < _samples[0].Length; bone++)
        {
            var first = _samples[weights[0].Sample][bone];
            var mixed = AlsPoseBlender.Scale(first, weights[0].Weight);
            for (var source = 1; source < count; source++)
                mixed = AlsPoseBlender.Accumulate(mixed,
                    _samples[weights[source].Sample][bone], weights[source].Weight);
            destination[bone] = AlsPoseBlender.Normalize(mixed);
        }
    }

    public void Apply(float yaw, float pitch, float aimOffsetBlendWeight)
    {
        CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, yaw, pitch, aimOffsetBlendWeight, _output);
        WritePose(_skeleton, _output);
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, float yaw, float pitch,
        float aimOffsetBlendWeight, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        Evaluate(input, yaw, pitch, output);
        AppliedFrames++;
    }

    internal void Evaluate(ReadOnlySpan<AlsLocalPose> basis, float yaw, float pitch,
        Span<AlsLocalPose> output)
    {
        SampleAdditive(yaw, pitch, _additive);
        AlsMeshSpaceAdditivePose.Apply(basis, _additive, _parents, _rotationScratch, output);
    }

    internal static void CapturePose(Skeleton3D skeleton, Span<AlsLocalPose> destination)
    {
        for (var bone = 0; bone < destination.Length; bone++)
        {
            var position = skeleton.GetBonePosePosition(bone);
            var rotation = skeleton.GetBonePoseRotation(bone);
            var scale = skeleton.GetBonePoseScale(bone);
            destination[bone] = new AlsLocalPose(new NVector3(position.X, position.Y, position.Z),
                new NQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W),
                new NVector3(scale.X, scale.Y, scale.Z));
        }
    }

    internal static void WritePose(Skeleton3D skeleton, ReadOnlySpan<AlsLocalPose> pose)
    {
        for (var bone = 0; bone < pose.Length; bone++)
        {
            var atom = pose[bone];
            skeleton.SetBonePosePosition(bone, new Godot.Vector3(atom.Position.X, atom.Position.Y,
                atom.Position.Z));
            skeleton.SetBonePoseRotation(bone, new Godot.Quaternion(atom.Rotation.X, atom.Rotation.Y,
                atom.Rotation.Z, atom.Rotation.W));
            skeleton.SetBonePoseScale(bone, new Godot.Vector3(atom.Scale.X, atom.Scale.Y,
                atom.Scale.Z));
        }
    }
}
