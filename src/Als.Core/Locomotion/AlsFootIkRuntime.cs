using GodotAls.Core.Contracts;
using M = System.Math;

namespace GodotAls.Core.Locomotion;

// Authored ALS V4 contract; the importer validates the complete ordered graph.
public sealed record AlsFootIkDefinition(AlsDoubleVector LeftKneeOffset, AlsDoubleVector RightKneeOffset,
    double StartStretchRatio, double MaxStretchScale);

public enum AlsFootIkPoseSpace : byte { Godot, Fbx }

// Properties use native UE centimeters/rotators. ComponentToWorld and poses use
// world uses Godot axes/units; component/bone poses use the constructor's basis.
// Curve alphas are UPDATE-time values, separate from pose curves.
public readonly record struct AlsFootIkControlInput(AlsFrameIdentity Identity, AlsFootIkPropertyState Properties,
    float UpdateLeftEnableCurve, float UpdateRightEnableCurve, AlsPrecisePose ComponentToWorld)
{
    public bool UseBasedFinal { get; init; }
    public AlsPrecisePose BasedLeftFinal { get; init; }
    public AlsPrecisePose BasedRightFinal { get; init; }
}

public sealed class AlsFootIkRuntime
{
    private readonly AlsFootIkDefinition _definition;
    private readonly AlsComponentPose _component;
    private readonly int[] _locks, _targets, _knees;
    private readonly int[][] _chains;
    private readonly int _pelvis;
    private readonly double _units;
    private readonly AlsFootIkPoseSpace _poseSpace;
    private readonly float[] _alphas = new float[5];
    private readonly AlsLocalPose[] _output;
    private readonly AlsInertialCurve[] _curves;
    private AlsFootIkControlInput _input;
    private bool _prepared, _evaluated;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public int EvaluatedControls { get; private set; }
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _output : throw new InvalidOperationException("Foot IK has not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Foot IK has not evaluated.");

    public AlsFootIkRuntime(AlsFootIkDefinition definition, ReadOnlySpan<string> bones, ReadOnlySpan<int> parents,
        int curveCount, double unitsPerCentimeter = .01, AlsFootIkPoseSpace poseSpace = AlsFootIkPoseSpace.Godot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (bones.IsEmpty || bones.Length != parents.Length || curveCount < 0 || (uint)poseSpace > 1 ||
            !double.IsFinite(unitsPerCentimeter) || unitsPerCentimeter <= 0 ||
            !definition.LeftKneeOffset.IsFinite || !definition.RightKneeOffset.IsFinite ||
            !double.IsFinite(definition.StartStretchRatio) || !double.IsFinite(definition.MaxStretchScale))
            throw new ArgumentException("Invalid foot IK definition/layout.");
        var names = bones.ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Duplicate foot IK bones.");
        for (var i = 0; i < parents.Length; i++)
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Foot IK requires parent-first bones.");
        _definition = definition; _units = unitsPerCentimeter; _poseSpace=poseSpace; _pelvis = Find("pelvis");
        _locks = [Find("ik_foot_l"), Find("ik_foot_r")];
        _targets = [Find("VB ik_foot_l_Offset"), Find("VB ik_foot_r_Offset")];
        _knees = [Find("VB ik_knee_target_l"), Find("VB ik_knee_target_r")];
        _chains = new int[2][];
        for (var side = 0; side < 2; side++)
        {
            var end = Find(side == 0 ? "foot_l" : "foot_r"); var lower = parents[end];
            var upper = lower < 0 ? -1 : parents[lower];
            if (upper < 0) throw new ArgumentException("Incomplete foot IK chain.");
            _chains[side] = [upper, lower, end];
        }
        _component = new(parents); _output = new AlsLocalPose[bones.Length]; _curves = new AlsInertialCurve[curveCount];
        int Find(string name)
        {
            var index = Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : throw new ArgumentException("Missing foot IK bone: " + name);
        }
    }

    public void Prepare(in AlsFootIkControlInput input)
    {
        if (_prepared || input.Identity.SlotGeneration == 0 || CommittedIdentity != default &&
            (input.Identity.CharacterId != CommittedIdentity.CharacterId || input.Identity.SlotGeneration != CommittedIdentity.SlotGeneration ||
             input.Identity.FrameId <= CommittedIdentity.FrameId)) throw new InvalidOperationException("Invalid foot IK candidate.");
        input.ComponentToWorld.Validate();
        if (input.UseBasedFinal) { input.BasedLeftFinal.Validate(); input.BasedRightFinal.Validate(); }
        var p = input.Properties;
        if (!p.LeftLock.Location.IsFinite || !p.RightLock.Location.IsFinite || !p.LeftOffset.Location.IsFinite ||
            !p.RightOffset.Location.IsFinite || !p.Pelvis.Offset.IsFinite || !p.LeftLock.Rotation.Finite ||
            !p.RightLock.Rotation.Finite || !p.LeftOffset.Rotation.Finite || !p.RightOffset.Rotation.Finite)
            throw new ArgumentException("Nonfinite foot IK properties.");
        _alphas[0] = Alpha(p.LeftLock.Alpha); _alphas[1] = Alpha(p.RightLock.Alpha);
        _alphas[2] = Alpha(input.UpdateLeftEnableCurve); _alphas[3] = Alpha(input.UpdateRightEnableCurve);
        _alphas[4] = Alpha(p.Pelvis.Alpha);
        _input = input; _prepared = true; _evaluated = false; EvaluatedControls = 0;
    }

    public void Evaluate(ReadOnlySpan<AlsLocalPose> source, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (!_prepared || _evaluated) throw new InvalidOperationException("Foot IK requires one prepared evaluation.");
        try
        {
            if (curves.Length != _curves.Length) throw new ArgumentException("Foot IK curve layout differs.");
            foreach (var c in curves) if (!float.IsFinite(c.Value)) throw new ArgumentException("Nonfinite foot IK curve.");
            _component.Begin(source);
            var p = _input.Properties;
            if (_input.UseBasedFinal)
            {
                BasedFinal(0, _input.BasedLeftFinal); BasedFinal(1, _input.BasedRightFinal);
            }
            else { Lock(0, p.LeftLock); Lock(1, p.RightLock); }
            Offset(0, p.LeftOffset); Offset(1, p.RightOffset);
            if (Active(_alphas[4]))
            {
                var world = AlsPrecisePose.Compose(_component.Component(_pelvis), _input.ComponentToWorld);
                world = world with { Position = world.Position + FromNative(p.Pelvis.Offset) };
                Apply(_pelvis, AlsPrecisePose.Relative(world, _input.ComponentToWorld), _alphas[4]);
            }
            Knee(0, _definition.LeftKneeOffset); Knee(1, _definition.RightKneeOffset);
            Solve(0); Solve(1);
            _component.Export(_output); curves.CopyTo(_curves); _evaluated = true;
        }
        catch { Cancel(); throw; }
    }
    private void Lock(int side, AlsFootLockInputState state)
    {
        if (!Active(_alphas[side])) return;
        var bone = _locks[side]; var pose = _component.Component(bone);
        Apply(bone, pose with { Position = FromNativeComponent(state.Location), Rotation = ComponentRotation(state.Rotation) }, _alphas[side]);
    }
    private void BasedFinal(int side, AlsPrecisePose final)
    {
        // Refactored Final already contains the target/anchor blend. Applying
        // lock alpha again would double-blend and change its release behavior.
        var bone = _locks[side]; var pose = _component.Component(bone);
        Apply(bone, pose with { Position = FromNativeComponent(final.Position), Rotation = FromNativeComponent(final.Rotation) }, 1);
    }
    private void Offset(int side, AlsFootOffsetInputState state)
    {
        var alpha = _alphas[side + 2]; if (!Active(alpha)) return;
        var bone = _targets[side]; var pose = _component.Component(bone);
        // Native ModifyBone separately converts rotation and translation through
        // world space. Reusing one world transform skips native TRS conversions.
        var world = AlsPrecisePose.Compose(pose, _input.ComponentToWorld);
        world = world with { Rotation = Rotation(state.Rotation) * world.Rotation };
        pose = AlsPrecisePose.Relative(world, _input.ComponentToWorld);
        world = AlsPrecisePose.Compose(pose, _input.ComponentToWorld);
        world = world with { Position = world.Position + FromNative(state.Location) };
        Apply(bone, AlsPrecisePose.Relative(world, _input.ComponentToWorld), alpha);
    }
    private void Knee(int side, AlsDoubleVector offset)
    {
        var alpha = _alphas[side + 2]; if (!Active(alpha)) return;
        var bone = _knees[side]; var pose = _component.Component(bone);
        var local = AlsPrecisePose.Relative(pose, pose);
        local = local with { Position = local.Position + FromNativeComponent(offset) };
        Apply(bone, AlsPrecisePose.Compose(local, pose), alpha);
    }
    private void Solve(int side)
    {
        var alpha = _alphas[side + 2]; if (!Active(alpha)) return;
        var chain = _chains[side];
        _ = _component.Local(chain[2]); _ = _component.Local(chain[1]); _ = _component.Local(chain[0]);
        var lower = _component.Component(chain[1]); var upper = _component.Component(chain[0]);
        var end = _component.Component(chain[2]); var target = _component.Component(_targets[side]);
        var pole = _component.Component(_knees[side]).Position;
        var r = ToNative(upper); var j = ToNative(lower); var e = ToNative(end);
        AlsTwoBoneIk.Solve(ref r, ref j, ref e, ToNative(pole), ToNative(target.Position), true,
            _definition.StartStretchRatio, _definition.MaxStretchScale);
        Span<AlsPrecisePose> solved = stackalloc AlsPrecisePose[3];
        solved[0] = upper with { Position = FromNativeComponent(r.Position), Rotation = FromNativeComponent(r.Rotation) };
        solved[1] = lower with { Position = FromNativeComponent(j.Position), Rotation = FromNativeComponent(j.Rotation) };
        solved[2] = end with { Position = FromNativeComponent(e.Position), Rotation = target.Rotation };
        _component.Apply(chain, solved, alpha); EvaluatedControls++;
    }
    private void Apply(int bone, AlsPrecisePose pose, float alpha)
    {
        Span<int> bones = stackalloc int[1] { bone };
        Span<AlsPrecisePose> transforms = stackalloc AlsPrecisePose[1] { pose };
        _component.Apply(bones, transforms, alpha); EvaluatedControls++;
    }
    private static bool Active(float alpha) => alpha > AlsPoseBlender.WeightThreshold;
    private static float Alpha(double value)
    {
        if (!double.IsFinite(value) || !float.IsFinite((float)value)) throw new ArgumentException("Nonfinite foot IK alpha.");
        return M.Clamp((float)value, 0, 1);
    }
    private AlsDoubleVector ToNative(AlsDoubleVector v) => _poseSpace == AlsFootIkPoseSpace.Fbx ?
        new(v.X / _units,-v.Y / _units,v.Z / _units) : new(-v.Z / _units, v.X / _units, v.Y / _units);
    private AlsDoubleVector FromNative(AlsDoubleVector v) => new(v.Y * _units, v.Z * _units, -v.X * _units);
    private AlsDoubleVector FromNativeComponent(AlsDoubleVector v) => _poseSpace == AlsFootIkPoseSpace.Fbx ?
        new(v.X*_units,-v.Y*_units,v.Z*_units) : FromNative(v);
    private AlsQuaternion FromNativeComponent(AlsQuaternion q) => _poseSpace == AlsFootIkPoseSpace.Fbx ? new(-q.X,q.Y,-q.Z,q.W) : FromNative(q);
    private AlsPrecisePose ToNative(AlsPrecisePose pose) => new(ToNative(pose.Position),
        _poseSpace == AlsFootIkPoseSpace.Fbx ? new(-pose.Rotation.X,pose.Rotation.Y,-pose.Rotation.Z,pose.Rotation.W) :
        new(pose.Rotation.Z, -pose.Rotation.X, -pose.Rotation.Y, pose.Rotation.W), AlsDoubleVector.One);
    private static AlsQuaternion FromNative(AlsQuaternion q) => new(-q.Y, -q.Z, q.X, q.W);
    private static AlsQuaternion Rotation(AlsAimingRotation value) => FromNative(NativeRotation(value));
    private AlsQuaternion ComponentRotation(AlsAimingRotation value) => FromNativeComponent(NativeRotation(value));
    private static AlsQuaternion NativeRotation(AlsAimingRotation value)
    {
        var p = value.Pitch % 360 * (M.PI / 360); var y = value.Yaw % 360 * (M.PI / 360); var r = value.Roll % 360 * (M.PI / 360);
        var sp = M.Sin(p); var cp = M.Cos(p); var sy = M.Sin(y); var cy = M.Cos(y); var sr = M.Sin(r); var cr = M.Cos(r);
        return new(cr * (sp * sy) - sr * (cp * cy), -cr * (sp * cy) - sr * (cp * sy),
            cr * (cp * sy) - sr * (sp * cy), cr * (cp * cy) + sr * (sp * sy));
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    { if (!_prepared || !_evaluated || identity != _input.Identity) throw new InvalidOperationException("Foot IK has no complete candidate."); }
    public void Commit(AlsFrameIdentity identity) { ValidateCommit(identity); CommittedIdentity = identity; Cancel(); }
    public void Cancel() { _prepared = _evaluated = false; }
}
