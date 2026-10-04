using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraLeftHandPoseLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly bool _enabled;
    private readonly LyraUnarmedLayerMasks _masks;
    private readonly AlsLocalPose[] _reference;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _output;
    private readonly int[] _sources;
    private readonly float[] _weights;
    private bool _hasApplied;
    private AlsPrecisePose[]? _logicalReference;
    private float[]? _logicalMask;
    private readonly string? _source;

    public LyraLeftHandPoseLayer(LyraBoundRig rig, LyraLinkedLayerProfile profile)
    {
        LyraPoseLayerContracts.Validate();
        _skeleton = rig.Skeleton;
        _enabled = profile.EnableLeftHandPoseOverride;
        _masks = LyraUnarmedLayerMasks.Load(_skeleton);
        var count = _skeleton.GetBoneCount();
        _basis = new AlsLocalPose[count];
        _output = new AlsLocalPose[count];
        _reference = new AlsLocalPose[count];
        _sources = new int[count];
        _weights = new float[count];
        for (var bone = 0; bone < count; bone++)
        {
            var rest = _skeleton.GetBoneRest(bone);
            var q = rest.Basis.GetRotationQuaternion();
            var s = rest.Basis.Scale;
            _reference[bone] = new(new NVector3(rest.Origin.X, rest.Origin.Y, rest.Origin.Z),
                new NQuaternion(q.X, q.Y, q.Z, q.W), new NVector3(s.X, s.Y, s.Z));
        }
        var source = profile.Asset("LeftHandPose_Override");
        _source = source;
        if (source is null) return; // A null SequenceEvaluator produces the skeleton reference pose.
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/left_hand_pose_catalog.json"));
        var root = document.RootElement;
        var hash = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/linked_layer_inventory.json"))).ToLowerInvariant();
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("inventorySha256").GetString() != hash ||
            root.GetProperty("targetSkeleton").GetString() !=
                "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton" ||
            root.GetProperty("logicalBoneNames").GetArrayLength() != 79 ||
            root.GetProperty("logicalToPhysical").GetArrayLength() != 79)
            throw new InvalidOperationException("Lyra left-hand pose catalog differs.");
        var row = root.GetProperty("clips").EnumerateArray().Single(clip =>
            clip.GetProperty("source").GetString() == source);
        if (row.GetProperty("timeSeconds").GetDouble() != 0 || row.GetProperty("pose").GetArrayLength() != 79)
            throw new InvalidOperationException("Lyra left-hand evaluator requires the first frame.");
        var seen = new HashSet<int>();
        for (var logical = 0; logical < 79; logical++)
        {
            var bone = root.GetProperty("logicalToPhysical")[logical].GetInt32();
            if (bone < 0) continue;
            if (bone >= count || !seen.Add(bone) ||
                !string.Equals(root.GetProperty("logicalBoneNames")[logical].GetString(),
                    _skeleton.GetBoneName(bone).ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Lyra left-hand physical bone mapping differs.");
            var atom = row.GetProperty("pose")[logical];
            var p = atom.GetProperty("position");
            var q = atom.GetProperty("rotation");
            var s = atom.GetProperty("scale");
            if (p.GetArrayLength() != 3 || q.GetArrayLength() != 4 || s.GetArrayLength() != 3 ||
                new[] { p, q, s }.Any(values => values.EnumerateArray().Any(value => !float.IsFinite(value.GetSingle()))))
                throw new InvalidOperationException("Invalid left-hand reference transform.");
            var rotation = new NQuaternion(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle());
            if (Math.Abs(rotation.LengthSquared() - 1) > 1e-3f)
                throw new InvalidOperationException("Left-hand reference rotation is not normalized.");
            _reference[bone] = new(new NVector3(p[0].GetSingle(), -p[1].GetSingle(), p[2].GetSingle()) * 0.01f,
                NQuaternion.Normalize(rotation),
                new NVector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
        }
        if (seen.Count != count) throw new InvalidOperationException("Incomplete left-hand pose.");
    }

    public float Weight { get; private set; }
    public int EvaluatedFrames { get; private set; }
    public int AppliedFrames { get; private set; }

    public void ConfigureLogical(LyraLogicalSourceBank bank)
    {
        _logicalReference = bank.Reference.ToArray();
        _logicalMask = _masks.LogicalWeights("LeftFingersMask", bank);
        if (_source is not null)
        {
            // Only fingers have a nonzero mask; keep unmapped control channels.
            for (var bone = 0; bone < 68; bone++) _logicalReference[bone] = LyraHandRetargetPoseLayer.ToNative(_reference[bone]);
            if (_logicalMask.Skip(68).Any(v => v != 0))
                throw new NotSupportedException("The left reference evaluator needs extended control tracks.");
        }
    }

    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float disableCurve, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        if (_logicalReference is null) throw new InvalidOperationException("Logical LeftHandPose is not configured.");
        Weight = Math.Clamp((_enabled ? 1f : 0f) - disableCurve, 0, 1);
        EvaluatedFrames++;
        for (var bone = 0; bone < 81; bone++)
        {
            var weight = _logicalMask![bone] * Weight;
            output[bone] = weight <= AlsPoseBlender.WeightThreshold ? input[bone] :
                AlsPrecisePoseBlender.Blend(input[bone], _logicalReference[bone], weight);
        }
        if (Weight > AlsPoseBlender.WeightThreshold) AppliedFrames++;
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void Apply(float disableCurve)
    {
        if (_hasApplied) throw new InvalidOperationException("Restore the previous left-hand pose before evaluating.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, disableCurve, _output);
        if (Weight <= AlsPoseBlender.WeightThreshold) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, float disableCurve, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        Weight = Math.Clamp((_enabled ? 1f : 0f) - disableCurve, 0, 1);
        EvaluatedFrames++;
        if (Weight <= AlsPoseBlender.WeightThreshold)
        {
            input.CopyTo(output);
            return;
        }
        _masks.ScaleWeights("LeftFingersMask", Weight, _weights);
        AlsLayeredBonePoseBlend.BlendLocal(input, _reference, _sources, _weights, output);
        AppliedFrames++;
    }
}
