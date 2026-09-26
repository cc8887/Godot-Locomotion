using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsBasedFootLockFrameState(AlsBasedFootLockState Left, AlsBasedFootLockState Right,
    AlsPrecisePose LeftTarget, AlsPrecisePose RightTarget, AlsQuaternion PelvisRotation,
    ulong TeleportSequence, double SecondsSinceTeleport);

// Optional immutable oracle capture, allocated only with the diagnostic flag.
public sealed record AlsBasedFootLockTrace(AlsBasedFootLockState Previous, AlsBasedFootLockInput Input, AlsBasedFootLockState Result);
public sealed record AlsBasedFootLockFrameTrace(AlsFrameIdentity Identity, AlsBasedFootLockTrace Left, AlsBasedFootLockTrace Right);

public readonly record struct AlsBasedFootLockDiagnostics(byte Valid, ulong BaseIdentity,
    Vector3 LeftWorld, Vector3 RightWorld, Vector3 LeftBase, Vector3 RightBase, byte ThighConstrained, byte FootConstrained)
{
    public static AlsBasedFootLockDiagnostics From(in AlsBasedFootLockFrameState state) =>
        new(state.Left.HasSample ? (byte)1 : (byte)0, state.Left.BaseIdentity,
            AlsFootIkCoordinates.FromNative(state.Left.WorldLock.Position), AlsFootIkCoordinates.FromNative(state.Right.WorldLock.Position),
            AlsFootIkCoordinates.FromNative(state.Left.BaseLock.Position), AlsFootIkCoordinates.FromNative(state.Right.BaseLock.Position),
            state.Left.ThighConstrained || state.Right.ThighConstrained ? (byte)1 : (byte)0,
            state.Left.FootConstrained || state.Right.FootConstrained ? (byte)1 : (byte)0);
}

// Adapter from V4's rig and pre-controller animation to Refactored's independent
// target/lock/Final histories. The enclosing FootIkFrameRuntime owns the transaction.
internal sealed class AlsBasedFootLockFrame
{
    private readonly AlsBasedFootLockModel _model;
    private readonly AlsComponentPose _components;
    private readonly AlsLocalPose[] _blendedTargets;
    private readonly AlsFootIkPoseSpace _space;
    private readonly int _left, _right, _pelvis;
    private readonly AlsDoubleVector _leftAxis, _rightAxis;
    private readonly bool _captureTrace;
    private AlsBasedFootLockFrameTrace? _candidateTrace;
    public AlsBasedFootLockFrameTrace? CommittedTrace { get; private set; }
    public AlsBasedFootLockFrameState Committed { get; private set; }
    public AlsBasedFootLockFrameState Candidate { get; private set; }
    internal AlsPrecisePose CapturedPelvisComponent => Native(_components.Component(_pelvis));
    public AlsBasedFootLockFrame(ReadOnlySpan<string> bones, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsLocalPose> reference, AlsFootIkPoseSpace space, bool captureTrace, AlsBasedFootLockSettings? settings = null)
    {
        if (bones.Length != parents.Length || bones.Length != reference.Length) throw new ArgumentException("Based foot rig layout differs.");
        for (var i = 0; i < parents.Length; i++) if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Based foot rig must be parent-first.");
        _space = space; _components = new(parents); _captureTrace = captureTrace; _model = new(settings);
        _blendedTargets = new AlsLocalPose[parents.Length];
        var names = bones.ToArray();
        _left = Find("ik_foot_l"); _right = Find("ik_foot_r"); _pelvis = Find("pelvis");
        _leftAxis = Axis(Find("foot_l"), parents, reference);
        _rightAxis = Axis(Find("foot_r"), parents, reference);
        Candidate = new(AlsBasedFootLockState.Empty, AlsBasedFootLockState.Empty,
            AlsPrecisePose.Identity, AlsPrecisePose.Identity, AlsQuaternion.Identity, 0, 1);
        CaptureTargets(reference); Committed = Candidate;
        int Find(string name)
        {
            var index = Array.FindIndex(names, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : throw new ArgumentException("Missing based foot rig bone: " + name);
        }
    }
    private AlsDoubleVector Axis(int bone, ReadOnlySpan<int> parents, ReadOnlySpan<AlsLocalPose> reference)
    {
        while (parents[bone] != _pelvis)
        {
            bone = parents[bone];
            if (bone < 0) throw new ArgumentException("Foot must descend from pelvis.");
        }
        var axis = AlsFootIkCoordinates.ComponentToNative(reference[bone].Position, _space);
        if (axis.LengthSquared < 1e-8) throw new ArgumentException("Reference thigh axis is degenerate.");
        return axis * (1 / System.Math.Sqrt(axis.LengthSquared));
    }
    public void Prepare(in AlsFrameInput frame, AlsMovementStateInput movementState, ReadOnlySpan<float> curves, bool valid = true,
        AlsRefactoredMotionObservation? locomotion = null)
    {
        var scene = frame.FootIk;
        var worldRotation = scene.ComponentToWorld.Rotation;
        if (_space == AlsFootIkPoseSpace.Fbx) worldRotation *= Quaternion.Conjugate(AlsFootIkCoordinates.FbxToGodotRotation);
        var component = new AlsPrecisePose(AlsFootIkCoordinates.ToNative(scene.ComponentToWorld.Position),
            NativeRotation(new(worldRotation), AlsFootIkPoseSpace.Godot).Normalized(), new(scene.ComponentToWorld.Scale));
        var floor = frame.Floor;
        var baseId = floor.IsGrounded == 1 && floor.PlatformId >= 0 && floor.ColliderId > 0 ? (ulong)floor.ColliderId : 0;
        var basis = AlsPrecisePose.Identity;
        if (baseId != 0)
            basis = new(AlsFootIkCoordinates.ToNative(floor.PlatformTransform.Translation),
                NativeRotation(new(Quaternion.CreateFromRotationMatrix(floor.PlatformTransform)), AlsFootIkPoseSpace.Godot).Normalized(), AlsDoubleVector.One);
        var elapsed = scene.TeleportSequence != Committed.TeleportSequence ? 0 : System.Math.Min(1, Committed.SecondsSinceTeleport + frame.DeltaTime);
        var speed = new Vector2(scene.MovementVelocity.X, scene.MovementVelocity.Z).Length() * 100;
        // Refactored HasVelocity >= 1 cm/s; MovingSmooth's authored default is
        // 150 cm/s, independently of V4's ShouldMove graph rule.
        var hasInput = frame.Command.MovementAxes.LengthSquared() > 1e-4f;
        if (locomotion is { } observation && observation.Identity != frame.Identity)
            throw new ArgumentException("Foreign foot locomotion observation.");
        var moving = locomotion?.MovingSmooth ?? (hasInput && speed >= 1 || speed > 150);
        var input = new AlsBasedFootLockInput(frame.DeltaTime, curves[0], curves[2],
            movementState == AlsMovementStateInput.Grounded, moving, valid, false, elapsed,
            baseId, basis, component, AlsPrecisePose.Compose(Committed.LeftTarget, component),
            Committed.PelvisRotation, _leftAxis);
        var left = _model.Evaluate(Committed.Left, input);
        var leftInput = input;
        input = input with { IkAmount = curves[1], LockAmount = curves[3],
            TargetWorld = AlsPrecisePose.Compose(Committed.RightTarget, component), ThighAxisPelvisSpace = _rightAxis };
        Candidate = Committed with { Left = left, Right = _model.Evaluate(Committed.Right, input),
            TeleportSequence = scene.TeleportSequence, SecondsSinceTeleport = elapsed };
        if (_captureTrace) _candidateTrace = new(frame.Identity, new(Committed.Left, leftInput, Candidate.Left),
            new(Committed.Right, input, Candidate.Right));
    }
    public void CaptureTargets(ReadOnlySpan<AlsLocalPose> source)
    {
        _components.Begin(source);
        Candidate = Candidate with { LeftTarget = Native(_components.Component(_left)),
            RightTarget = Native(_components.Component(_right)), PelvisRotation = NativeRotation(_components.Component(_pelvis).Rotation, _space) };
    }
    public void CaptureBlendedTargets(ReadOnlySpan<AlsLocalPose> ordinary, ReadOnlySpan<AlsLocalPose> alternate, float ordinaryWeight)
    {
        if (ordinary.Length != _blendedTargets.Length || alternate.Length != _blendedTargets.Length ||
            !float.IsFinite(ordinaryWeight) || ordinaryWeight is <= 0 or >= 1)
            throw new ArgumentException("Mixed root foot targets require both complete source poses and a fractional weight.");
        // Match the root's native local-atom blend before reconstructing socket
        // components. Blending only world/socket endpoints changes the result
        // when their parents rotate. Ordinary IK target bones must come from
        // before the V4 lock controller, which overwrites those bones.
        for (var i = 0; i < _blendedTargets.Length; i++)
        {
            new AlsPrecisePose(ordinary[i]).Validate(); new AlsPrecisePose(alternate[i]).Validate();
            _blendedTargets[i] = AlsPrecisePoseBlender.Accumulate(
                AlsPrecisePoseBlender.Scale(new(ordinary[i]), ordinaryWeight),
                new(alternate[i]), 1 - ordinaryWeight).Normalized().ToSingle();
        }
        CaptureTargets(_blendedTargets);
    }
    private AlsPrecisePose Native(AlsPrecisePose pose) => new(
        _space == AlsFootIkPoseSpace.Fbx ? new(pose.Position.X * 100, -pose.Position.Y * 100, pose.Position.Z * 100) :
            new(-pose.Position.Z * 100, pose.Position.X * 100, pose.Position.Y * 100),
        NativeRotation(pose.Rotation, _space), AlsDoubleVector.One);
    private static AlsQuaternion NativeRotation(AlsQuaternion q, AlsFootIkPoseSpace space) =>
        space == AlsFootIkPoseSpace.Fbx ? new(-q.X, q.Y, -q.Z, q.W) : new(q.Z, -q.X, -q.Y, q.W);
    public void Commit() { Committed = Candidate; CommittedTrace = _candidateTrace; }
    public void Cancel() { Candidate = Committed; _candidateTrace = CommittedTrace; }
}
