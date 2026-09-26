using Godot;
using GodotAls.Animation;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

internal readonly record struct AlsMotorMantlingState(AlsMantlingFrame Frame, CollisionObject3D? Target,
    bool Relative, AlsMantlingWarpSettings Settings, AlsMantlingWarpAnchors Anchors, AlsMantlingMotionDefinition? Motion)
{
    public Transform3D Destination { get; init; }
    public bool PreventAutoRestart { get; init; }
    public Vector3 ActorScale { get; init; }
}

public partial class AlsCharacterMotor
{
    private AlsMantlingProbe? _mantleProbe;
    private AlsMantlingDemoResources? _mantleResources;
    private AlsMotorMantlingState _mantling;
    internal bool MantlingEndedByAnimation { get; set; }
    internal bool MantleTargetDestroyed { get; private set; }
    internal AlsMantlingFrame Mantling => _mantling.Frame;
    internal string MantleProbeRejection => _mantleProbe?.Rejection ?? "Disabled";
    private Transform3D MantleDestination => _mantling.Relative && GodotObject.IsInstanceValid(_mantling.Target)
        ? _mantling.Target!.GlobalTransform * _mantling.Destination : _mantling.Destination;
    internal void CancelMantling() { _mantling = default; MantleTargetDestroyed = false; }
    // Native mannequin base rotation offset (mesh faces +Y, actor faces +X).
    private static readonly AlsQuaternion MantleMeshRotation = new(0, 0, -Math.Sqrt(.5), Math.Sqrt(.5));

    private bool StepMantling(bool grounded, in AlsResolvedLocomotionCommand command, AlsOverlayKind overlayKind, bool blocked, bool cancel,
        in AlsActionRequest request, float delta)
    {
        if (_mantleProbe is null) return false;
        MantleTargetDestroyed = false;
        var state = _mantling;
        if (state.Frame.Active)
        {
            var exists = GodotObject.IsInstanceValid(state.Target) && state.Target!.IsInsideTree() && !state.Target.IsQueuedForDeletion();
            var interrupted = !exists || blocked || cancel || request.Command != AlsActionCommand.None;
            if (interrupted || MantlingEndedByAnimation || state.Frame.Time >= state.Settings.Duration)
            {
                MantleTargetDestroyed = !exists;
                _mantling = state with { Frame = state.Frame with { Active = false, Started = false, Interrupted = interrupted }, PreventAutoRestart = cancel };
                return false;
            }
            var step = AlsMantlingRootMotion.Prepare(state.Frame.Time, delta, delta, state.Settings, state.Anchors,
                NativeActor(GlobalTransform), MantleMeshRotation, new(0, 0, 1), true, state.Relative,
                NativeActor(state.Target!.GlobalTransform), state.Motion!.Roots);
            // ALS PhysCustom explicitly uses MoveUpdatedComponent(..., false).
            // All clearance tests happened before starting the action.
            var actor = FromNativeActor(step.TargetActor);
            actor.Basis = actor.Basis.ScaledLocal(state.ActorScale); GlobalTransform = actor;
            Velocity = new((float)(step.Velocity.Y * .01), (float)(step.Velocity.Z * .01), (float)(-step.Velocity.X * .01));
            _mantling = state with { Frame = state.Frame with { Time = step.Time, Started = false } };
            return true;
        }
        _mantling = state with { Frame = state.Frame with { Started = false, Interrupted = false },
            PreventAutoRestart = state.PreventAutoRestart && !grounded && command.JumpPressed == 0 };
        if (blocked || cancel || _mantling.PreventAutoRestart || request.Command != AlsActionCommand.None || grounded && command.JumpPressed == 0) return false;
        var scale = GlobalBasis.Scale;
        if (MathF.Abs(scale.X - scale.Y) > 1e-4f || MathF.Abs(scale.X - scale.Z) > 1e-4f || scale.X <= 0)
            throw new InvalidOperationException("Mantle requires an upright uniformly scaled character.");
        if (!_mantleProbe.Find(grounded, ToGodot(command.WorldDirection), Velocity, GetCharacterYaw(),
            _capsuleShape!.Radius * scale.X, _capsuleShape.Height * scale.Y * .5f, scale.Y, out var target)) return false;
        var overlay = "Als.OverlayMode." + overlayKind;
        var motion = _mantleResources!.Motion.Select(target.Type, overlay);
        var montage = _mantleResources.Montages.Definitions[motion.Settings.Montage];
        var pose = _mantleResources.Montages.Poses[montage.SequencePath];
        var settings = motion.CreateWarpSettings(target.Height, (double)pose.Data.FrameRateNumerator / pose.Data.FrameRateDenominator);
        var targetBase = NativeActor(target.Body.GlobalTransform);
        var desired = NativeActor(target.World);
        if (target.Relative) desired = AlsPrecisePose.Relative(desired, targetBase);
        var anchors = AlsMantlingRootMotion.CreateAnchors(NativeActor(GlobalTransform), desired, MantleMeshRotation,
            motion.Roots.Sample(settings.MontageStartTime), motion.Roots.Sample(motion.Settings.MontageLength), target.Relative, targetBase);
        _mantling = new(new(true, true, false, montage.Asset.ActionDefinitionId, 0, settings.MontageStartTime,
            settings.MontageRate, target.Height, target.Type), target.Body, target.Relative, settings, anchors, motion)
        { Destination = target.Relative ? target.Body.GlobalTransform.AffineInverse() * target.World : target.World, ActorScale = scale };
        Velocity = Vector3.Zero; return true;
    }

    private static AlsPrecisePose NativeActor(Transform3D transform)
    {
        // Change world axes using a proper rotation, preserving base pitch/roll.
        var p = transform.Origin; var q = transform.Basis.Orthonormalized().GetRotationQuaternion();
        return new(new(-p.Z * 100d, p.X * 100d, p.Y * 100d), new(q.Z, -q.X, -q.Y, q.W),
            new(transform.Basis.Scale.Z, transform.Basis.Scale.X, transform.Basis.Scale.Y));
    }
    private static Transform3D FromNativeActor(in AlsPrecisePose pose)
    {
        var p = pose.Position; var q = pose.Rotation;
        return new(new Basis(new Quaternion((float)-q.Y, (float)-q.Z, (float)q.X, (float)q.W).Normalized()),
            new((float)(p.Y * .01), (float)(p.Z * .01), (float)(-p.X * .01)));
    }
}
