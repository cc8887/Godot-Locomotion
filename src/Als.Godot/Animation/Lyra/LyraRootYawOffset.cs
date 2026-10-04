using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRootYawDefaults(bool Enabled, float StandingMinimum,
    float StandingMaximum, float CrouchedMinimum, float CrouchedMaximum)
{
    private const string ResourcePath = "res://assets/generated/lyra_als/root_yaw_defaults.json";
    private const string MainClass = "/Game/Characters/Heroes/Mannequin/Animations/" +
        "ABP_Mannequin_Base.ABP_Mannequin_Base_C";

    public static LyraRootYawDefaults Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("class").GetString() != MainClass ||
            root.GetProperty("initialMode").GetString() !=
            "<AnimEnum_RootYawOffsetMode.BLEND_OUT: 0>")
            throw new InvalidOperationException("Lyra root-yaw defaults do not match the main AnimBP.");
        var standing = root.GetProperty("angleClamp");
        var crouched = root.GetProperty("angleClampCrouched");
        if (standing.GetArrayLength() != 2 || crouched.GetArrayLength() != 2)
            throw new InvalidOperationException("Invalid Lyra root-yaw angle bounds.");
        var result = new LyraRootYawDefaults(root.GetProperty("enableRootYawOffset").GetBoolean(),
            standing[0].GetSingle(), standing[1].GetSingle(),
            crouched[0].GetSingle(), crouched[1].GetSingle());
        if (!float.IsFinite(result.StandingMinimum) || !float.IsFinite(result.StandingMaximum) ||
            !float.IsFinite(result.CrouchedMinimum) || !float.IsFinite(result.CrouchedMaximum) ||
            result.StandingMinimum > result.StandingMaximum ||
            result.CrouchedMinimum > result.CrouchedMaximum)
            throw new InvalidOperationException("Invalid Lyra root-yaw angle bounds.");
        return result;
    }
}

internal enum LyraRootYawMode { BlendOut, Hold, Accumulate }

internal sealed class LyraRootYawOffset
{
    private readonly Skeleton3D _skeleton;
    private readonly int _boneCount;
    private readonly LyraRootYawDefaults _defaults;
    private LyraRootYawMode _mode;
    private float _previousActorYaw;
    private float _springVelocity;
    private Quaternion _incomingRootRotation;
    private double _turnYawCurveValue;
    private bool _hasActorYaw;
    private bool _hasApplied;

    public LyraRootYawOffset(Skeleton3D skeleton, LyraRootYawDefaults defaults)
    {
        if (skeleton.GetBoneCount() == 0 ||
            !skeleton.GetBoneName(0).ToString().Equals("root", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Lyra RotateRootBone requires root at bone zero.");
        _skeleton = skeleton;
        _boneCount = skeleton.GetBoneCount();
        _defaults = defaults;
    }

    public float OffsetDegrees { get; private set; }
    public float AimYawDegrees => -OffsetDegrees;
    public LyraRootYawMode PendingMode => _mode;
    public int TurnCurveFeedbackCount { get; private set; }
    public void ConfigureLogical(LyraLogicalSourceBank bank)
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "hand_chain_native.json"));
        var root = document.RootElement;
        var settings = root.GetProperty("rootYawSettings");
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("calibrationSha256").GetString() != bank.CalibrationSha256 ||
            root.GetProperty("catalogSha256").GetString() != bank.CatalogSha256 || settings.GetProperty("nodes").GetArrayLength() != 1 ||
            settings.GetProperty("class").GetString() != "/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C" ||
            LyraLogicalSourceBank.ParsePose(settings.GetProperty("nodes")[0].GetProperty("meshToComponent")) != AlsPrecisePose.Identity ||
            settings.GetProperty("nodes")[0].GetProperty("rotateRootMotionAttribute").GetBoolean())
            throw new NotSupportedException("Logical RotateRootBone requires the captured identity mesh basis and no root-motion attribute rotation.");
    }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        _skeleton.SetBonePoseRotation(0, _incomingRootRotation);
        _hasApplied = false;
    }

    public void Update(float actorYawDegrees, double delta, bool isCrouching, bool isDashing = false)
    {
        if (!float.IsFinite(actorYawDegrees) || !double.IsFinite(delta) || delta < 0)
            throw new ArgumentOutOfRangeException(nameof(actorYawDegrees));
        var actorYaw = NormalizeAxis(actorYawDegrees);
        var yawDelta = _hasActorYaw ? actorYaw - _previousActorYaw : 0;
        _previousActorYaw = actorYaw;
        _hasActorYaw = true;

        if (_mode == LyraRootYawMode.Accumulate)
            SetOffset(OffsetDegrees - yawDelta, isCrouching);
        if (isDashing || _mode == LyraRootYawMode.BlendOut)
        {
            if (delta > 1e-8)
            {
                var step = (float)delta;
                var omega = MathF.Sqrt(80f);
                var error = OffsetDegrees;
                var coefficient = _springVelocity + error * omega;
                var x = omega * step;
                var attenuation = 1f /
                    (1f + 1.00746054f * x + 0.45053901f * x * x + 0.25724632f * x * x * x);
                _springVelocity = (coefficient - error * omega - coefficient * x) * attenuation;
                SetOffset((error + coefficient * step) * attenuation, isCrouching);
            }
        }
        _mode = LyraRootYawMode.BlendOut;
    }

    public void QueueMode(LyraMotionPhase phase)
    {
        _mode = phase switch
        {
            LyraMotionPhase.Idle or LyraMotionPhase.Stop => LyraRootYawMode.Accumulate,
            LyraMotionPhase.Start => LyraRootYawMode.Hold,
            _ => LyraRootYawMode.BlendOut,
        };
    }

    public void ProcessTurnYawCurve(float remainingYaw, float weight, bool isCrouching)
    {
        if (!float.IsFinite(remainingYaw) || !float.IsFinite(weight))
            throw new ArgumentOutOfRangeException(nameof(weight));
        var previous = _turnYawCurveValue;
        if (Math.Abs(weight) <= 1e-4f)
        {
            _turnYawCurveValue = 0;
            return;
        }
        _turnYawCurveValue = (double)remainingYaw / weight;
        if (previous == 0) return;
        SetOffset((float)(OffsetDegrees - (_turnYawCurveValue - previous)), isCrouching);
        TurnCurveFeedbackCount++;
    }

    public void ResetTurnYawCurve() => _turnYawCurveValue = 0;

    public void ApplyRootPose()
    {
        if (!_defaults.Enabled || Math.Abs(OffsetDegrees) <= 1e-6f) return;
        _incomingRootRotation = _skeleton.GetBonePoseRotation(0);
        // The ALS FBX basis maps a positive Unreal Z yaw to a negative Godot Y yaw.
        var rotation = new Quaternion(Vector3.Up, -Mathf.DegToRad(OffsetDegrees));
        _skeleton.SetBonePoseRotation(0, (_incomingRootRotation * rotation).Normalized());
        _hasApplied = true;
    }

    public void EvaluatePose(ReadOnlySpan<AlsLocalPose> input, Span<AlsLocalPose> output)
    {
        LyraPoseBuffers.Validate(input, output, _boneCount);
        input.CopyTo(output);
        if (!_defaults.Enabled || Math.Abs(OffsetDegrees) <= 1e-6f) return;
        var q = input[0].Rotation;
        var incoming = new Quaternion(q.X, q.Y, q.Z, q.W);
        var rotation = (incoming * new Quaternion(Vector3.Up, -Mathf.DegToRad(OffsetDegrees))).Normalized();
        output[0] = input[0] with { Rotation = new System.Numerics.Quaternion(rotation.X,
            rotation.Y, rotation.Z, rotation.W) };
    }
    public void EvaluatePose(ReadOnlySpan<AlsPrecisePose> input, Span<AlsPrecisePose> output)
    {
        LyraPoseBuffers.Validate(input, output);
        input.CopyTo(output);
        if (!_defaults.Enabled || Math.Abs(OffsetDegrees) <= 1e-6f) return;
        var yaw = AlsRootRotationMath.Quaternion(OffsetDegrees);
        output[0] = input[0] with { Rotation = (input[0].Rotation * yaw).Normalized() };
    }

    private void SetOffset(float value, bool isCrouching)
    {
        if (!_defaults.Enabled)
        {
            OffsetDegrees = 0;
            return;
        }
        value = NormalizeAxis(value);
        var minimum = isCrouching ? _defaults.CrouchedMinimum : _defaults.StandingMinimum;
        var maximum = isCrouching ? _defaults.CrouchedMaximum : _defaults.StandingMaximum;
        OffsetDegrees = minimum == maximum ? value : Math.Clamp(value, minimum, maximum);
    }

    private static float NormalizeAxis(float degrees)
        => AlsCharacterRotationMath.NormalizeFloat(degrees);
}
