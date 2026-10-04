using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// The first SkeletalControls node only; hand solvers and foot controls follow
// this operator in the source graph and are tracked separately in ROADMAP.
internal sealed class LyraHandRetargetPoseLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly float _fkWeight;
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _component;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _output;
    private readonly int _leftFk, _rightFk, _leftIk, _rightIk, _gun;
    private bool _hasApplied;
    private LyraLogicalSourceBank? _logicalBank;
    private readonly AlsPrecisePose[] _logicalComponent = new AlsPrecisePose[81];

    public LyraHandRetargetPoseLayer(LyraBoundRig rig, LyraLinkedLayerProfile profile)
    {
        LyraPoseLayerContracts.Validate();
        _skeleton = rig.Skeleton;
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/skeletal_control_defaults.json"));
        var root = document.RootElement;
        var hash = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/linked_layer_inventory.json"))).ToLowerInvariant();
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("inventorySha256").GetString() != hash)
            throw new InvalidOperationException("Lyra skeletal-control defaults differ.");
        var inventory = LyraLinkedLayerInventory.Load();
        var profileName = root.GetProperty("profiles").EnumerateObject().Single(row =>
            inventory.Get(row.Name).ClassPath == profile.ClassPath).Name;
        _fkWeight = root.GetProperty("profiles").GetProperty(profileName).GetProperty("Hand FKWeight").GetSingle();
        if (!float.IsFinite(_fkWeight)) throw new InvalidOperationException("Invalid Lyra Hand FKWeight.");
        var layout = root.GetProperty("skeletons").GetProperty("target").GetProperty("layout");
        var count = _skeleton.GetBoneCount();
        if (count != 68 || layout.GetProperty("rawBoneNames").GetArrayLength() != count)
            throw new InvalidOperationException("Lyra hand retargeting requires the ALS skeleton.");
        _basis = new AlsLocalPose[count];
        _output = new AlsLocalPose[count];
        _component = new AlsPrecisePose[count];
        _parents = new int[count];
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var bone = 0; bone < count; bone++)
        {
            var name = _skeleton.GetBoneName(bone).ToString();
            _parents[bone] = _skeleton.GetBoneParent(bone);
            if (!string.Equals(layout.GetProperty("rawBoneNames")[bone].GetString(), name,
                    StringComparison.OrdinalIgnoreCase) ||
                layout.GetProperty("rawParents")[bone].GetInt32() != _parents[bone] ||
                _parents[bone] >= bone || !indices.TryAdd(name, bone))
                throw new InvalidOperationException("Lyra hand-retarget hierarchy differs.");
        }
        _leftFk = indices["hand_l"]; _rightFk = indices["hand_r"];
        _leftIk = indices["ik_hand_l"]; _rightIk = indices["ik_hand_r"];
        _gun = indices["ik_hand_gun"];
        if (_parents[_leftIk] != _gun || _parents[_rightIk] != _gun)
            throw new InvalidOperationException("Lyra hand targets must inherit the gun transform.");
    }

    public int EvaluatedFrames { get; private set; }
    public int AppliedFrames { get; private set; }
    public float Alpha { get; private set; }
    public double LastOffsetCm { get; private set; }
    public void ConfigureLogical(LyraLogicalSourceBank bank) => _logicalBank = bank;
    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float disableCurve, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        var bank = _logicalBank ?? throw new InvalidOperationException("Logical HandRetarget is not configured.");
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        Alpha = Math.Clamp(1 - disableCurve, 0, 1); EvaluatedFrames++; LastOffsetCm = 0;
        input.CopyTo(output);
        if (Alpha <= AlsPoseBlender.WeightThreshold) return;
        for (var bone = 0; bone < 81; bone++)
            _logicalComponent[bone] = (bank.Parents[bone] < 0 ? input[bone] :
                AlsPrecisePose.Compose(input[bone], _logicalComponent[bank.Parents[bone]])).Normalized();
        var from = _logicalComponent[_gun];
        var to = AlsHandIkRetargeting.Retarget(from, _logicalComponent[_leftFk], _logicalComponent[_rightFk],
            _logicalComponent[_leftIk], _logicalComponent[_rightIk], _fkWeight);
        LastOffsetCm = Math.Sqrt((to.Position - from.Position).LengthSquared);
        if (LastOffsetCm == 0) return;
        var relative = AlsPrecisePose.Relative(to, _logicalComponent[bank.Parents[_gun]]);
        output[_gun] = input[_gun] with { Position = AlsPrecisePose.BlendTransform(input[_gun], relative, Alpha).Position };
        AppliedFrames++;
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void Apply(float disableCurve)
    {
        if (_hasApplied) throw new InvalidOperationException("Restore the previous hand-retarget input.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, disableCurve, _output);
        if (Alpha <= AlsPoseBlender.WeightThreshold || LastOffsetCm == 0) return;
        var position = _output[_gun].Position;
        _skeleton.SetBonePosePosition(_gun, new Vector3(position.X, position.Y, position.Z));
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, float disableCurve, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        Alpha = Math.Clamp(1 - disableCurve, 0, 1);
        EvaluatedFrames++;
        LastOffsetCm = 0;
        input.CopyTo(output);
        if (Alpha <= AlsPoseBlender.WeightThreshold) return;
        for (var bone = 0; bone < input.Length; bone++)
        {
            var local = ToNative(input[bone]);
            _component[bone] = (_parents[bone] < 0 ? local :
                AlsPrecisePose.Compose(local, _component[_parents[bone]])).Normalized();
        }
        var from = _component[_gun];
        var to = AlsHandIkRetargeting.Retarget(from, _component[_leftFk], _component[_rightFk],
            _component[_leftIk], _component[_rightIk], _fkWeight);
        LastOffsetCm = Math.Sqrt((to.Position - from.Position).LengthSquared);
        if (LastOffsetCm == 0) return;
        var relative = AlsPrecisePose.Relative(to, _component[_parents[_gun]]);
        var blended = AlsPrecisePose.BlendTransform(ToNative(input[_gun]), relative, Alpha);
        var position = blended.Position;
        // This node changes only the gun's local translation. Its IK descendants
        // inherit the offset; skin/finger local transforms stay exactly unchanged.
        output[_gun] = input[_gun] with { Position = new System.Numerics.Vector3((float)(position.X * 0.01),
            (float)(-position.Y * 0.01), (float)(position.Z * 0.01)) };
        AppliedFrames++;
    }

    internal static AlsPrecisePose ToNative(in AlsLocalPose pose) => new(
        new(pose.Position.X * 100d, -pose.Position.Y * 100d, pose.Position.Z * 100d),
        new(-pose.Rotation.X, pose.Rotation.Y, -pose.Rotation.Z, pose.Rotation.W),
        new(pose.Scale.X, pose.Scale.Y, pose.Scale.Z));
}
