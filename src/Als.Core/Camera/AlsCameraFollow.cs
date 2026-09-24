using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Camera;

// All positions are native UE world centimeters. Scene ownership and socket
// resolution belong to the host; this boundary retains no engine objects.
public readonly record struct AlsCameraFollowSettings(float TeleportDistance, float FirstPersonFov,
    float ThirdPersonFov, float TraceRadius, AlsDoubleVector TraceOverrideOffset,
    bool SmoothTraceDistance, float TraceHalfLife);
public readonly record struct AlsCameraFollowCurves(AlsDoubleVector PivotOffset, AlsDoubleVector CameraOffset,
    float LagX, float LagY, float LagZ, float RotationLag, float TraceOverride, float FirstPersonOverride);
public readonly record struct AlsCameraFollowInput(float Delta, bool AllowLag, AlsAimingRotation View,
    AlsDoubleVector FirstPivotSocket, AlsDoubleVector SecondPivotSocket, AlsDoubleVector FirstPersonSocket,
    AlsDoubleVector ShoulderSocket, bool DetachedRootPivot, AlsDoubleVector CapsuleBottom,
    AlsQuaternion MeshRotation, float MeshScale, long BaseId, string BaseBone, bool RelativeBaseRotation,
    AlsDoubleVector BaseLocation, AlsQuaternion BaseRotation, bool OverrideFov, float FovOverride, float FovOffset);
public readonly record struct AlsCameraTraceRequest(AlsDoubleVector Start, AlsDoubleVector End, float Radius);
// Start may be adjusted by initial-penetration recovery. Result is the sphere
// center at the resolved hit (or End on a miss), never the contact surface point.
public readonly record struct AlsCameraTraceResponse(AlsDoubleVector Start, AlsDoubleVector Result);
public readonly record struct AlsCameraFollowState(bool Initialized, long BaseId, string BaseBone,
    AlsDoubleVector PivotTarget, AlsDoubleVector PivotLag, AlsDoubleVector Pivot,
    AlsDoubleVector Location, AlsAimingRotation Rotation, float Fov, float TraceRatio,
    AlsDoubleVector BaseLocalPivotLag, AlsQuaternion BaseLocalRotation)
{
    public static AlsCameraFollowState Initial => new(false, 0, "", default, default, default, default,
        default, 90, 1, default, AlsQuaternion.Identity);
}

public static class AlsCameraFollow
{
    // Returns a complete candidate. The owner publishes it only together with
    // its matching curve-graph candidate; a failing query leaves history intact.
    public static AlsCameraFollowState Step(in AlsCameraFollowState previous, in AlsCameraFollowInput input,
        in AlsCameraFollowSettings settings, in AlsCameraFollowCurves curves,
        Func<AlsCameraTraceRequest, AlsCameraTraceResponse> trace)
    {
        Validate(input, settings, curves); ArgumentNullException.ThrowIfNull(trace);
        var state = previous;
        if (input.BaseId != previous.BaseId || input.BaseBone != previous.BaseBone)
        {
            state = state with { BaseId = input.BaseId, BaseBone = input.BaseBone,
                BaseLocalPivotLag = input.RelativeBaseRotation
                    ? (previous.PivotLag - input.BaseLocation).Rotate(input.BaseRotation.Conjugate()) : default,
                BaseLocalRotation = input.RelativeBaseRotation
                    ? input.BaseRotation.Conjugate() * AlsCameraMath.Quaternion(previous.Rotation) : AlsQuaternion.Identity };
        }
        var firstPivot = input.DetachedRootPivot ? input.CapsuleBottom : input.FirstPivotSocket;
        var target = (firstPivot + input.SecondPivotSocket) * .5;
        var firstPerson = System.Math.Clamp(curves.FirstPersonOverride, 0, 1);
        state = state with { Initialized = true, PivotTarget = target };
        if (firstPerson >= 1 - AlsPoseBlender.WeightThreshold)
            return state with { PivotLag = target, Pivot = target, Location = input.FirstPersonSocket,
                Rotation = input.View, Fov = input.OverrideFov ? input.FovOverride : settings.FirstPersonFov };

        var lag = AlsCameraMath.AllowLag(input.AllowLag && previous.Initialized, previous.PivotTarget, target, settings.TeleportDistance);
        var rotation = input.RelativeBaseRotation
            ? AlsCameraMath.Rotator(input.BaseRotation * state.BaseLocalRotation) : previous.Rotation;
        rotation = AlsCameraMath.Rotation(rotation, input.View, input.Delta, curves.RotationLag, lag);
        var pivotLag = input.RelativeBaseRotation
            ? input.BaseLocation + state.BaseLocalPivotLag.Rotate(input.BaseRotation) : previous.PivotLag;
        pivotLag = AlsCameraMath.PivotLag(pivotLag, target, rotation.Yaw, input.Delta, curves.LagX, curves.LagY, curves.LagZ, lag);
        if (input.RelativeBaseRotation)
            state = state with { BaseLocalPivotLag = (pivotLag - input.BaseLocation).Rotate(input.BaseRotation.Conjugate()),
                BaseLocalRotation = input.BaseRotation.Conjugate() * AlsCameraMath.Quaternion(rotation) };
        var pivotOffset = (curves.PivotOffset * input.MeshScale).Rotate(input.MeshRotation);
        var pivot = pivotLag + pivotOffset;
        var end = pivot + (curves.CameraOffset * input.MeshScale).Rotate(AlsCameraMath.Quaternion(rotation));
        // Native trace override offset is world-space and is not mesh-scaled.
        var overrideStart = target + pivotOffset + settings.TraceOverrideOffset;
        var start = input.ShoulderSocket + (overrideStart - input.ShoulderSocket) * System.Math.Clamp(curves.TraceOverride, 0, 1);
        var hit = trace(new(start, end, settings.TraceRadius * input.MeshScale));
        var resolved = AlsCameraMath.TraceDistance(hit.Start, end, hit.Result, previous.TraceRatio, input.Delta,
            settings.TraceHalfLife, lag, settings.SmoothTraceDistance);
        var location = resolved.Location;
        var fov = settings.ThirdPersonFov;
        if (firstPerson > AlsPoseBlender.WeightThreshold)
        {
            location += (input.FirstPersonSocket - location) * firstPerson;
            fov += (settings.FirstPersonFov - fov) * firstPerson;
        }
        if (input.OverrideFov) fov = input.FovOverride;
        return state with { Rotation = rotation, PivotLag = pivotLag, Pivot = pivot, Location = location,
            TraceRatio = resolved.Ratio, Fov = System.Math.Clamp(fov + input.FovOffset, 5, 175) };
    }

    private static void Validate(in AlsCameraFollowInput input, in AlsCameraFollowSettings settings, in AlsCameraFollowCurves curves)
    {
        static void Vector(AlsDoubleVector value) { if (!value.IsFinite) throw new ArgumentException("Nonfinite camera location."); }
        static void Number(float value, bool nonnegative = false)
        { if (!float.IsFinite(value) || (nonnegative && value < 0)) throw new ArgumentException("Invalid camera scalar."); }
        Number(input.Delta, true); Number(input.MeshScale, true);
        if (input.MeshScale == 0 || input.BaseBone is null || input.BaseId < 0) throw new ArgumentException("Invalid camera owner/base.");
        _ = AlsCameraMath.Quaternion(input.View); _ = AlsCameraMath.Rotator(input.MeshRotation); _ = AlsCameraMath.Rotator(input.BaseRotation);
        Vector(input.FirstPivotSocket); Vector(input.SecondPivotSocket); Vector(input.FirstPersonSocket); Vector(input.ShoulderSocket);
        Vector(input.CapsuleBottom); Vector(input.BaseLocation); Vector(settings.TraceOverrideOffset); Vector(curves.PivotOffset); Vector(curves.CameraOffset);
        Number(settings.TeleportDistance, true); Number(settings.TraceRadius, true); Number(settings.TraceHalfLife, true);
        Number(settings.FirstPersonFov); Number(settings.ThirdPersonFov); Number(input.FovOverride); Number(input.FovOffset);
        Number(curves.LagX, true); Number(curves.LagY, true); Number(curves.LagZ, true); Number(curves.RotationLag, true);
        Number(curves.TraceOverride); Number(curves.FirstPersonOverride);
    }
}
