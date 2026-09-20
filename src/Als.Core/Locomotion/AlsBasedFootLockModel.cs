using M = System.Math;

namespace GodotAls.Core.Locomotion;

// ALS-Refactored RefreshFootLock / ProcessFootLock* / ConstrainFootLock.
// All observations use UE axes and centimeters. This is a separate policy from
// V4 SetFootLocking: callers must explicitly choose it and supply pre-IK targets.
public enum AlsFootLockBaseRotationMode { FullRotation, GravityTwist }

public readonly record struct AlsBasedFootLockSettings(bool AllowLock, float ThighAngleLimit, float FootAngleLimit)
{
    // Original ALS remains the baseline. GravityTwist is an explicit contact
    // policy: the downstream foot rig owns alignment to the ground normal.
    public AlsFootLockBaseRotationMode BaseRotationMode { get; init; }
    public bool PinFinalContact { get; init; }
    public bool CorrectUnplantedPenetration { get; init; }
    public bool PinContactToes { get; init; }
    public static AlsBasedFootLockSettings Default => new(true, 90, 40);
}

public readonly record struct AlsBasedFootLockState(bool HasSample, ulong BaseIdentity, float Amount,
    AlsPrecisePose WorldLock, AlsPrecisePose BaseLock, AlsPrecisePose ComponentLock, AlsPrecisePose FinalComponent)
{
    public bool ThighConstrained { get; init; }
    public bool FootConstrained { get; init; }
    public static AlsBasedFootLockState Empty => new(false, 0, 0, AlsPrecisePose.Identity,
        AlsPrecisePose.Identity, AlsPrecisePose.Identity, AlsPrecisePose.Identity);
}

// Identity zero means no relative movement base. The adapter owns validity,
// teleport timing (including the 0.2-second smoothing window), and MovingSmooth.
// No scene object, shared mutable history, or physics query enters this model.
public readonly record struct AlsBasedFootLockInput(float DeltaTime, float IkAmount, float LockAmount,
    bool Grounded, bool MovingSmooth, bool Valid, bool BecameValid, double SecondsSinceTeleport,
    ulong BaseIdentity, AlsPrecisePose BaseTransform, AlsPrecisePose ComponentTransform,
    AlsPrecisePose TargetWorld, AlsQuaternion PelvisRotation, AlsDoubleVector ThighAxisPelvisSpace);

public sealed class AlsBasedFootLockModel
{
    public AlsBasedFootLockSettings Settings { get; }
    public AlsBasedFootLockModel(AlsBasedFootLockSettings? settings = null)
    {
        Settings = settings ?? AlsBasedFootLockSettings.Default;
        if (!float.IsFinite(Settings.ThighAngleLimit) || Settings.ThighAngleLimit is < 0 or > 180 ||
            !float.IsFinite(Settings.FootAngleLimit) || Settings.FootAngleLimit is < 0 or > 180 ||
            !Enum.IsDefined(Settings.BaseRotationMode) ||
            Settings.PinFinalContact && Settings.BaseRotationMode != AlsFootLockBaseRotationMode.GravityTwist ||
            (Settings.CorrectUnplantedPenetration || Settings.PinContactToes) && !Settings.PinFinalContact)
            throw new ArgumentException("Invalid Refactored foot-lock limits.");
    }

    // Returns a candidate only. Retrying with the same committed state and
    // observations is safe; the enclosing animation transaction owns commit.
    public AlsBasedFootLockState Evaluate(in AlsBasedFootLockState previous, in AlsBasedFootLockInput input)
    {
        if (!input.Valid) return previous;
        Validate(input);
        var old = previous.HasSample ? previous : AlsBasedFootLockState.Empty;
        Validate(old);
        var becameValid = input.BecameValid || !old.HasSample;
        var component = input.ComponentTransform;
        var basis = Rigid(input.BaseTransform); // UE base anchors do not inherit scale.
        var target = Rigid(input.TargetWorld);
        var ik = M.Clamp(input.IkAmount, 0, 1);
        var amount = M.Clamp(input.LockAmount, 0, 1);
        var next = old with { HasSample = true, BaseIdentity = input.BaseIdentity, ThighConstrained = false, FootConstrained = false };

        // Preserve the component-space lock during an explicit teleport window.
        if (!becameValid && input.SecondsSinceTeleport <= .2 && Relevant(ik * old.Amount))
        {
            next = next with { WorldLock = ToWorld(next.ComponentLock, component) };
            next = Rebase(next, input.BaseIdentity, basis);
        }
        if ((becameValid || old.BaseIdentity != input.BaseIdentity) && Relevant(ik * old.Amount))
        {
            if (becameValid) next = next with { WorldLock = target };
            next = Rebase(next, input.BaseIdentity, basis);
            next = next with { ComponentLock = ToComponent(next.WorldLock, component) };
        }

        if (input.MovingSmooth || !input.Grounded)
            amount = becameValid ? 0 : MathF.Max(0, MathF.Min(amount,
                old.Amount - input.DeltaTime * (input.MovingSmooth ? 5f : .6f)));

        if (!Settings.AllowLock || !Relevant(ik * amount))
        {
            if (old.Amount > 0)
                next = next with { Amount = 0, WorldLock = AlsPrecisePose.Identity,
                    BaseLock = AlsPrecisePose.Identity, ComponentLock = AlsPrecisePose.Identity };
            return next with { FinalComponent = ToComponent(target, component) };
        }

        if (amount >= 1f - AlsPoseBlender.WeightThreshold)
        {
            if (amount > old.Amount)
            {
                // Previous final pose excludes IK. Repeated full-weight frames
                // must not recapture an animated target and walk the anchor.
                var capture = becameValid ? target : ToWorld(old.FinalComponent, component);
                next = next with { BaseLock = input.BaseIdentity != 0 ? ToBaseAnchor(capture, basis) : AlsPrecisePose.Identity };
                if (old.Amount <= .9f)
                    next = next with { WorldLock = capture, ComponentLock = ToComponent(capture, component) };
            }
            next = next with { Amount = 1 };
        }
        else if (amount <= old.Amount) next = next with { Amount = amount };

        if (input.BaseIdentity != 0) next = next with { WorldLock = FromBaseAnchor(next.BaseLock, basis) };
        next = next with { ComponentLock = ToComponent(next.WorldLock, component) };
        next = Constrain(next, input, component, basis, target);

        var alpha = next.Amount;
        var sign = AlsQuaternion.Dot(target.Rotation, next.WorldLock.Rotation) >= 0 ? 1 : -1;
        var final = new AlsPrecisePose(target.Position * (1.0 - alpha) + next.WorldLock.Position * alpha,
            (target.Rotation * (sign * (1.0 - alpha)) + next.WorldLock.Rotation * alpha).Normalized(), AlsDoubleVector.One);
        return next with { FinalComponent = ToComponent(final, component) };
    }

    private AlsBasedFootLockState Constrain(AlsBasedFootLockState next, in AlsBasedFootLockInput input,
        AlsPrecisePose component, AlsPrecisePose basis, AlsPrecisePose target)
    {
        var thigh = input.ThighAxisPelvisSpace.Rotate(input.PelvisRotation);
        var angle = SignedAngleXY(thigh, next.ComponentLock.Position);
        if (MathF.Abs(angle) > Settings.ThighAngleLimit + 1e-4f)
        {
            var offset = Yaw(MathF.Min(Settings.ThighAngleLimit, MathF.Max(-Settings.ThighAngleLimit, angle)) - angle);
            var locked = next.ComponentLock;
            locked = Single(locked with { Position = locked.Position.Rotate(offset), Rotation = (offset * locked.Rotation).Normalized() });
            next = next with { ComponentLock = locked, WorldLock = ToWorld(locked, component), ThighConstrained = true };
            next = Rebase(next, input.BaseIdentity, basis);
        }

        var delta = (next.WorldLock.Rotation * target.Rotation.Conjugate()).ToSingle();
        var twist = 2f * MathF.Atan2(delta.Z, delta.W);
        while (twist > MathF.PI) twist -= 2f * MathF.PI;
        while (twist < -MathF.PI) twist += 2f * MathF.PI;
        var degrees = twist * (180f / MathF.PI);
        if (MathF.Abs(degrees) > Settings.FootAngleLimit + 1e-4f)
        {
            var offset = Yaw(M.Clamp(degrees, -Settings.FootAngleLimit, Settings.FootAngleLimit) - degrees);
            // Source deliberately limits the world/final rotation here, without
            // replacing the component/base anchor. Do not feed the limit back.
            next = next with { WorldLock = next.WorldLock with { Rotation = offset * next.WorldLock.Rotation }, FootConstrained = true };
        }
        return next;
    }

    private static float SignedAngleXY(AlsDoubleVector from, AlsDoubleVector to)
    {
        var a = new System.Numerics.Vector2((float)from.X, (float)from.Y);
        var b = new System.Numerics.Vector2((float)to.X, (float)to.Y);
        var length = a.LengthSquared();
        a = length > 1e-8f ? a * (1f / MathF.Sqrt(length)) : System.Numerics.Vector2.Zero;
        length = b.LengthSquared();
        b = length > 1e-8f ? b * (1f / MathF.Sqrt(length)) : System.Numerics.Vector2.Zero;
        // Match UAlsVector::AngleBetweenSignedXY, including zero cross product;
        // replacing this with atan2 changes the antiparallel case.
        return MathF.Acos(M.Clamp(System.Numerics.Vector2.Dot(a, b), -1, 1)) * (180f / MathF.PI) *
            MathF.Sign(a.X * b.Y - a.Y * b.X);
    }
    private static AlsQuaternion Yaw(float degrees) => new(AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitZ,
        degrees * (MathF.PI / 180f)).ToSingle());
    private static bool Relevant(float weight) => weight > AlsPoseBlender.WeightThreshold;
    private static AlsPrecisePose Rigid(AlsPrecisePose pose) => pose with { Scale = AlsDoubleVector.One };
    private static AlsPrecisePose Single(AlsPrecisePose pose) => new(Rigid(pose).ToSingle());
    private static AlsPrecisePose ToComponent(AlsPrecisePose world, AlsPrecisePose component) =>
        Single(AlsPrecisePose.Relative(world, component));
    private static AlsPrecisePose ToWorld(AlsPrecisePose local, AlsPrecisePose component) =>
        Rigid(AlsPrecisePose.Compose(local, component));
    private AlsBasedFootLockState Rebase(AlsBasedFootLockState state, ulong identity, AlsPrecisePose basis) =>
        state with { BaseLock = identity != 0 ? ToBaseAnchor(state.WorldLock, basis) : AlsPrecisePose.Identity };
    private AlsPrecisePose ToBaseAnchor(AlsPrecisePose world, AlsPrecisePose basis)
    {
        var anchor = ToComponent(world, basis);
        return TwistOnly(basis) ? anchor with
        {
            Rotation = new((GravityTwist(basis.Rotation).Conjugate() * world.Rotation).ToSingle())
        } : anchor;
    }
    private AlsPrecisePose FromBaseAnchor(AlsPrecisePose anchor, AlsPrecisePose basis)
    {
        var world = ToWorld(anchor, basis);
        return TwistOnly(basis) ? world with { Rotation = GravityTwist(basis.Rotation) * anchor.Rotation } : world;
    }
    private bool TwistOnly(AlsPrecisePose basis) => Settings.BaseRotationMode == AlsFootLockBaseRotationMode.GravityTwist &&
        (basis.Rotation.X != 0 || basis.Rotation.Y != 0);
    private static AlsQuaternion GravityTwist(AlsQuaternion rotation)
    {
        // Swing-twist decomposition around native world +Z. Euler yaw of a
        // delta rotation is not equivalent on a compound pitch/roll surface.
        var twist = new AlsQuaternion(0, 0, rotation.Z, rotation.W);
        // At a 180-degree swing twist is underdetermined. Keep a finite,
        // deterministic heading while ordinary airborne/unlock rules release.
        return twist.LengthSquared > 1e-12 ? twist.Normalized() : AlsQuaternion.Identity;
    }
    private static void Validate(in AlsBasedFootLockState state)
    {
        if (!float.IsFinite(state.Amount) || state.Amount is < 0 or > 1) throw new ArgumentException("Invalid foot-lock history.");
        state.WorldLock.Validate(); state.BaseLock.Validate(); state.ComponentLock.Validate(); state.FinalComponent.Validate();
    }
    private static void Validate(in AlsBasedFootLockInput input)
    {
        if (!float.IsFinite(input.DeltaTime) || input.DeltaTime < 0 || !float.IsFinite(input.IkAmount) ||
            !float.IsFinite(input.LockAmount) || !double.IsFinite(input.SecondsSinceTeleport) || input.SecondsSinceTeleport < 0 ||
            !input.ThighAxisPelvisSpace.IsFinite || M.Abs(input.ThighAxisPelvisSpace.LengthSquared - 1) > .001 ||
            !double.IsFinite(input.PelvisRotation.LengthSquared) || M.Abs(input.PelvisRotation.LengthSquared - 1) > .001)
            throw new ArgumentException("Invalid based foot-lock observation.");
        input.BaseTransform.Validate(); input.ComponentTransform.Validate(); input.TargetWorld.Validate();
        if (input.ComponentTransform.Scale.X <= 0 || input.ComponentTransform.Scale.Y <= 0 || input.ComponentTransform.Scale.Z <= 0)
            throw new ArgumentException("Based foot locking requires a nondegenerate positive component scale.");
    }
}
