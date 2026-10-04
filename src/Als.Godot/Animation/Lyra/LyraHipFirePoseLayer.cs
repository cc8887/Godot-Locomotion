using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraHipFirePoseLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly LyraLinkedLayerProfile _profile;
    private readonly LyraUnarmedLayerMasks _masks;
    private readonly int[] _parents;
    private readonly float[] _weights;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _output;
    private readonly AlsLocalPose[] _standing;
    private readonly AlsLocalPose[] _crouching;
    private readonly NQuaternion[] _rotationScratch;
    private bool _hasApplied;
    private double _hipFireWeight;
    private double _aimOffsetBlendWeight;
    private LyraLogicalSourceBank? _logicalBank;
    private AlsPrecisePose[]? _logicalStanding, _logicalCrouching;
    private float[]? _logicalMask, _logicalWeights;
    private readonly AlsQuaternion[] _logicalScratch = new AlsQuaternion[81 * 3];
    private readonly string _standingSlot, _crouchingSlot;

    public LyraHipFirePoseLayer(LyraBoundRig rig)
        : this(rig, LyraLinkedLayerInventory.Load().Get("unarmed"), "idle", "hipfire_crouch") { }

    public LyraHipFirePoseLayer(LyraBoundRig rig, LyraLinkedLayerProfile profile,
        string standingSlot, string crouchingSlot)
    {
        _skeleton = rig.Skeleton;
        _profile = profile;
        _standingSlot = standingSlot; _crouchingSlot = crouchingSlot;
        _masks = LyraUnarmedLayerMasks.Load(_skeleton);
        if (_profile.Asset("Aim_HipFirePose") != rig.SourceClip(standingSlot).SourceObjectPath ||
            _profile.Asset("Aim_HipFirePose_Crouch") != rig.SourceClip(crouchingSlot).SourceObjectPath)
            throw new InvalidOperationException("Lyra HipFire evaluators require the CDO-bound source assets.");
        var count = _skeleton.GetBoneCount();
        _parents = new int[count];
        _weights = new float[count];
        _basis = new AlsLocalPose[count];
        _output = new AlsLocalPose[count];
        _standing = new AlsLocalPose[count];
        _crouching = new AlsLocalPose[count];
        _rotationScratch = new NQuaternion[count * 3];
        var rest = new AlsLocalPose[count];
        for (var bone = 0; bone < count; bone++)
        {
            _parents[bone] = _skeleton.GetBoneParent(bone);
            var transform = _skeleton.GetBoneRest(bone);
            var rotation = transform.Basis.GetRotationQuaternion();
            var scale = transform.Basis.Scale;
            rest[bone] = new AlsLocalPose(new NVector3(transform.Origin.X, transform.Origin.Y,
                transform.Origin.Z), new NQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W),
                new NVector3(scale.X, scale.Y, scale.Z));
        }
        SampleEvaluator(rig, standingSlot, rest, _standing);
        SampleEvaluator(rig, crouchingSlot, rest, _crouching);
    }

    public float Weight => (float)_hipFireWeight;
    public float AimOffsetBlendWeight => (float)_aimOffsetBlendWeight;
    public int AppliedFrames { get; private set; }

    public void ConfigureLogical(LyraLogicalSourceBank bank)
    {
        _logicalBank = bank;
        _logicalStanding = new AlsPrecisePose[81]; _logicalCrouching = new AlsPrecisePose[81];
        if (bank.Get(_standingSlot).Source != _profile.Asset("Aim_HipFirePose") ||
            bank.Get(_crouchingSlot).Source != _profile.Asset("Aim_HipFirePose_Crouch"))
            throw new InvalidOperationException("Logical HipFire binding differs from the provider.");
        bank.CreateSampler(_standingSlot).Sample(0, _logicalStanding);
        bank.CreateSampler(_crouchingSlot).Sample(0, _logicalCrouching);
        _logicalMask = _masks.LogicalWeights("UpperBodyMask", bank);
        _logicalWeights = new float[81];
    }

    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, bool isCrouching, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        var bank = _logicalBank ?? throw new InvalidOperationException("Logical HipFire is not configured.");
        if (Weight <= AlsPoseBlender.WeightThreshold) { input.CopyTo(output); return; }
        for (var bone = 0; bone < 81; bone++) _logicalWeights![bone] = _logicalMask![bone] * Weight;
        AlsMeshSpacePoseBlend.Blend(input, isCrouching ? _logicalCrouching! : _logicalStanding!,
            bank.Parents, _logicalWeights!, _logicalScratch, output);
        AppliedFrames++;
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        WritePose(_basis);
        _hasApplied = false;
    }

    public void UpdateWeight(bool isCrouching, bool isOnGround, bool isAiming,
        double timeSinceFiredWeapon, float applyHipfireOverridePose, float rootYawOffset,
        bool hasAcceleration, double delta)
    {
        if (!double.IsFinite(timeSinceFiredWeapon) || timeSinceFiredWeapon < 0 ||
            !float.IsFinite(applyHipfireOverridePose) || !float.IsFinite(rootYawOffset) ||
            !double.IsFinite(delta) || delta < 0)
            throw new ArgumentOutOfRangeException(nameof(timeSinceFiredWeapon));
        if (!_profile.RaiseWeaponAfterFiringWhenCrouched && isCrouching ||
            !isCrouching && isAiming && isOnGround)
        {
            _hipFireWeight = 0;
            _aimOffsetBlendWeight = 1;
        }
        else if (timeSinceFiredWeapon < _profile.RaiseWeaponAfterFiringDuration ||
                 isAiming && (isCrouching || !isOnGround) || applyHipfireOverridePose > 0)
        {
            _hipFireWeight = 1;
            _aimOffsetBlendWeight = 1;
        }
        else
        {
            _hipFireWeight = GodotAls.Core.Math.AlsMath.InterpolateTo(_hipFireWeight, 0, delta, 1, 1e-8);
            var aimTarget = Math.Abs(rootYawOffset) < 10 && hasAcceleration ? _hipFireWeight : 1;
            _aimOffsetBlendWeight = GodotAls.Core.Math.AlsMath.InterpolateTo(_aimOffsetBlendWeight, aimTarget, delta, 10, 1e-8);
        }
    }

    public void Apply(bool isCrouching)
    {
        if (Weight <= AlsPoseBlender.WeightThreshold) return;
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluatePose(_basis, isCrouching, _output);
        WritePose(_output);
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, bool isCrouching, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _basis.Length);
        if (Weight <= AlsPoseBlender.WeightThreshold)
        {
            input.CopyTo(output);
            return;
        }
        _masks.ScaleWeights("UpperBodyMask", Weight, _weights);
        AlsMeshSpacePoseBlend.Blend(input, isCrouching ? _crouching : _standing,
            _parents, _weights, _rotationScratch, output);
        AppliedFrames++;
    }

    private void WritePose(ReadOnlySpan<AlsLocalPose> pose)
    {
        for (var bone = 0; bone < pose.Length; bone++)
        {
            var atom = pose[bone];
            _skeleton.SetBonePosePosition(bone, new Vector3(atom.Position.X, atom.Position.Y,
                atom.Position.Z));
            _skeleton.SetBonePoseRotation(bone, new Quaternion(atom.Rotation.X, atom.Rotation.Y,
                atom.Rotation.Z, atom.Rotation.W));
            _skeleton.SetBonePoseScale(bone, new Vector3(atom.Scale.X, atom.Scale.Y, atom.Scale.Z));
        }
    }

    private static void SampleEvaluator(LyraBoundRig rig, string slot,
        ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> destination)
    {
        using var animation = rig.Player.GetAnimation(rig.QualifiedName(slot))
            ?? throw new InvalidOperationException($"Missing HipFire evaluator: {slot}.");
        using var clip = new AlsLocalPoseClip(animation, rig.Skeleton, ownsAnimation: false);
        clip.Sample(rest, 0, destination);
    }
}
