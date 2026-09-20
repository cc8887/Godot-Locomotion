using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal static class AlsFootIkGodot
{
    public static AlsLocalPose Pose(Transform3D value)
    {
        var p = value.Origin; var q = value.Basis.GetRotationQuaternion(); var s = value.Basis.Scale;
        return new(new(p.X, p.Y, p.Z), new(q.X, q.Y, q.Z, q.W), new(s.X, s.Y, s.Z));
    }
    public static Transform3D Transform(AlsLocalPose value) => new(
        new Basis(new Quaternion(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W)).ScaledLocal(new(value.Scale.X, value.Scale.Y, value.Scale.Z)),
        new(value.Position.X, value.Position.Y, value.Position.Z));
}

public partial class AlsCharacterMotor
{
    private bool _nativeFootGatherEnabled;
    private AlsFootIkPoseSample _initialNativeFeet;
    private AlsFootOffsetInputModel _nativeFootOffsets = null!;
    private ulong _footTeleportSequence;
    // Movement owners call this when applying an explicit teleport. The event
    // sequence lives outside candidate Step state; retrying a frame observes the
    // same event, while animation commits its own smoothing-window history.
    public void NotifyFootLockTeleport()
    {
        EnsureMainThread();
        _footTeleportSequence = checked(_footTeleportSequence + 1);
    }
    internal void ConfigureNativeFeet(AlsFootIkPoseSample initial, AlsFootOffsetInputModel offsets)
    {
        _initialNativeFeet = initial; _nativeFootOffsets = offsets; _nativeFootGatherEnabled = true;
    }
    internal AlsFrameInput RecaptureReplacementFeet(in AlsFrameInput frame)
    {
        if (!_nativeFootGatherEnabled) return frame;
        // The movement step is already complete. Only replace scene queries for
        // the new skeleton's cold reference pose; never relabel retired bones.
        var q = frame.FootIk.LastMovementRotation;
        var scene = GatherNativeFeet(frame.Identity, AlsP3Presentation.ToGodot(frame.CharacterTransform),
            new(q.X, q.Y, q.Z, q.W), frame.FootIk.WorldDelta, frame.Floor.IsGrounded == 1,
            out var left, out var right, out _, out _);
        scene = scene with { MovementVelocity = frame.FootIk.MovementVelocity };
        return frame with { FootIk = scene, LeftFootHit = left, RightFootHit = right };
    }
    private AlsFootIkSceneSample GatherNativeFeet(AlsFrameIdentity identity, Transform3D character,
        Quaternion lastMovementRotation, float delta, bool grounded, out AlsFootHit left, out AlsFootHit right,
        out CollisionObject3D? leftPlatform, out CollisionObject3D? rightPlatform)
    {
        // Unlike the legacy probe path, native foot state is not cleared by a
        // character yaw/translation threshold. The authored functions own it.
        RememberGatherTransform(character);
        var committed = _footProbeExchange.TryReadNative(identity, out var source);
        if (!committed) source = _initialNativeFeet;
        var component = character * AlsFootIkGodot.Transform(source.ComponentToCharacter);
        var root = component * ToGodot(source.RootComponent);
        var leftWorld = component * ToGodot(source.LeftComponent.Position);
        var rightWorld = component * ToGodot(source.RightComponent.Position);
        LastFootGatherConsumed = committed;
        LastFootGatherRequestIdentity = source.Identity;
        LastLeftFootQueryWorldOrigin = new(leftWorld.X, root.Y, leftWorld.Z);
        LastRightFootQueryWorldOrigin = new(rightWorld.X, root.Y, rightWorld.Z);
        left = AlsFootHit.Invalid; right = AlsFootHit.Invalid; leftPlatform = rightPlatform = null;
        if (grounded && source.LeftEnableCurve > 0) left = Trace(leftWorld, _footQueries[0], out leftPlatform);
        if (grounded && source.RightEnableCurve > 0) right = Trace(rightWorld, _footQueries[1], out rightPlatform);
        return new(1, source.Identity, AlsFootIkGodot.Pose(component), source.LeftComponent, source.RightComponent,
            ToNumerics(root), new(lastMovementRotation.X, lastMovementRotation.Y, lastMovementRotation.Z, lastMovementRotation.W),
            ToNumerics(Velocity), delta) { TeleportSequence = _footTeleportSequence };

        AlsFootHit Trace(Vector3 foot, PhysicsRayQueryParameters3D query, out CollisionObject3D? platform)
        {
            var segment = _nativeFootOffsets.Trace(AlsFootIkCoordinates.ToNative(ToNumerics(foot)), AlsFootIkCoordinates.ToNative(ToNumerics(root)));
            query.From = ToGodot(AlsFootIkCoordinates.FromNative(segment.Start));
            query.To = ToGodot(AlsFootIkCoordinates.FromNative(segment.End));
            if (_runtimeContext is not null) System.Threading.Interlocked.Increment(ref _runtimeContext.FootGatherQueries);
            return ReadFootHit(query, Vector3.Up, out platform);
        }
    }
}

public partial class AlsP3WorkerRoot
{
    private static System.Numerics.Quaternion ToNumericsRotation(Basis basis)
    {
        var q = basis.GetRotationQuaternion(); return new(q.X, q.Y, q.Z, q.W);
    }
    private int _nativeRootBone, _nativeLeftBone, _nativeRightBone;
    internal bool UsesNativeFootIk => _controller?.UsesNativeFootIk == true;
    internal AlsFootIkPoseSample InitialNativeFeet { get; private set; }
    private void ConfigureNativeFeet(Transform3D character)
    {
        if (!UsesNativeFootIk) return;
        _nativeRootBone = Find("root"); _nativeLeftBone = Find("ik_foot_l"); _nativeRightBone = Find("ik_foot_r");
        InitialNativeFeet = CaptureNativeFeet(default, character, 0, 0);
        int Find(string name)
        {
            var index = _skeleton!.FindBone(name);
            return index >= 0 ? index : throw new InvalidOperationException("Missing native foot scene bone: " + name);
        }
    }
    private AlsFootIkPoseSample CaptureNativeFeet(AlsFrameIdentity identity, Transform3D character, float leftCurve, float rightCurve) =>
        new(identity, AlsFootIkGodot.Pose(character.AffineInverse() * _skeleton!.GlobalTransform),
            AlsFootIkGodot.Pose(_skeleton.GetBoneGlobalPose(_nativeLeftBone)), AlsFootIkGodot.Pose(_skeleton.GetBoneGlobalPose(_nativeRightBone)),
            ToNumerics(_skeleton.GetBoneGlobalPose(_nativeRootBone).Origin), leftCurve, rightCurve);
}
