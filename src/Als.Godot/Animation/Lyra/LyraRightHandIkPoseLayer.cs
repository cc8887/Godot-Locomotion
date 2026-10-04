using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// The right TwoBoneIK node only. Left-hand weapon-space CopyBone and solver
// are separate source nodes; an existing IK left hand is not their substitute.
internal sealed class LyraRightHandIkPoseLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly bool _disabled;
    private readonly int[] _chain;
    private readonly AlsTwoBoneIkController _controller;
    private readonly AlsPrecisePose[] _nativeInput, _nativeOutput;
    private readonly AlsLocalPose[] _basis, _output;
    private bool _hasApplied;
    private AlsTwoBoneIkController? _logicalController;

    public LyraRightHandIkPoseLayer(LyraBoundRig rig, LyraLinkedLayerProfile profile)
    {
        LyraPoseLayerContracts.Validate();
        _skeleton = rig.Skeleton;
        _disabled = profile.DisableHandIk;
        using var defaults = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/skeletal_control_defaults.json"));
        if (defaults.RootElement.GetProperty("mainDefaults").GetProperty("EnableControlRig").GetBoolean())
            throw new NotSupportedException("The preceding enabled root-offset controller is not implemented.");
        var count = _skeleton.GetBoneCount();
        var parents = new int[count];
        for (var bone = 0; bone < count; bone++) parents[bone] = _skeleton.GetBoneParent(bone);
        var hand = _skeleton.FindBone("hand_r");
        var effector = _skeleton.FindBone("ik_hand_r");
        var lower = _skeleton.FindBone("lowerarm_r");
        var upper = _skeleton.FindBone("upperarm_r");
        if (count != 68 || hand < 0 || lower < 0 || upper < 0 || effector < 0 ||
            parents[hand] != lower || parents[lower] != upper)
            throw new InvalidOperationException("Lyra right-hand IK requires the ALS arm hierarchy.");
        _chain = [upper, lower, hand];
        _controller = new(parents, hand, effector, lower, new AlsDoubleVector(0, 50, 0), false);
        _nativeInput = new AlsPrecisePose[count]; _nativeOutput = new AlsPrecisePose[count];
        _basis = new AlsLocalPose[count]; _output = new AlsLocalPose[count];
    }

    public float Alpha { get; private set; }
    public int EvaluatedFrames { get; private set; }
    public int AppliedFrames { get; private set; }
    public double LastUpperRotationRadians { get; private set; }
    public void ConfigureLogical(LyraLogicalSourceBank bank) => _logicalController = new(bank.Parents,
        bank.Bone("hand_r"), bank.Bone("ik_hand_r"), bank.Bone("lowerarm_r"), new(0, 50, 0), false);

    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, float disableCurve, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        var controller = _logicalController ?? throw new InvalidOperationException("Logical RightHandIK is not configured.");
        Alpha = Math.Clamp((_disabled ? 0f : 1f) - disableCurve, 0, 1);
        EvaluatedFrames++; LastUpperRotationRadians = 0;
        controller.Evaluate(input, default, Alpha, output);
        if (Alpha <= AlsPoseBlender.WeightThreshold) return;
        LastUpperRotationRadians = 2 * Math.Acos(Math.Clamp(Math.Abs(AlsQuaternion.Dot(
            input[_chain[0]].Rotation.Normalized(), output[_chain[0]].Rotation.Normalized())), 0, 1));
        AppliedFrames++;
    }

    public void Apply(float disableCurve)
    {
        if (_hasApplied) throw new InvalidOperationException("Restore the previous right-hand IK input.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, disableCurve, _output);
        if (Alpha <= AlsPoseBlender.WeightThreshold) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true;
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, float disableCurve, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (!float.IsFinite(disableCurve)) throw new ArgumentOutOfRangeException(nameof(disableCurve));
        Alpha = Math.Clamp((_disabled ? 0f : 1f) - disableCurve, 0, 1);
        EvaluatedFrames++;
        LastUpperRotationRadians = 0;
        input.CopyTo(output);
        if (Alpha <= AlsPoseBlender.WeightThreshold) return;
        for (var bone = 0; bone < input.Length; bone++)
            _nativeInput[bone] = LyraHandRetargetPoseLayer.ToNative(input[bone]);
        _controller.Evaluate(_nativeInput, default, Alpha, _nativeOutput);
        foreach (var bone in _chain)
        {
            var pose = _nativeOutput[bone];
            output[bone] = new(new System.Numerics.Vector3((float)(pose.Position.X * .01),
                (float)(-pose.Position.Y * .01), (float)(pose.Position.Z * .01)),
                new System.Numerics.Quaternion((float)-pose.Rotation.X, (float)pose.Rotation.Y,
                    (float)-pose.Rotation.Z, (float)pose.Rotation.W), pose.Scale.ToSingle());
        }
        LastUpperRotationRadians = 2 * Math.Acos(Math.Clamp(Math.Abs(AlsQuaternion.Dot(
            _nativeInput[_chain[0]].Rotation.Normalized(), _nativeOutput[_chain[0]].Rotation.Normalized())), 0, 1));
        AppliedFrames++;
    }
}
