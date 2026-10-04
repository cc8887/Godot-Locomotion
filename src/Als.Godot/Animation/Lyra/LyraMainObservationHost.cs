using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// UE centimetres and double world vectors/rotator. The gather boundary owns
// conversions from Godot physics and the external gameplay tags/root history.
internal readonly record struct LyraMainObservationInput(AlsDoubleVector Location, LyraMainRotationInput Rotation,
    AlsDoubleVector Velocity, AlsDoubleVector Acceleration, bool Ground, bool Crouching,
    int MovementMode, bool Ads, bool Firing, double RootYaw);

internal sealed record LyraMainObservationState
{
    public AlsDoubleVector Location { get; init; }
    public double Displacement { get; init; }
    public double DisplacementSpeed { get; init; }
    public LyraMainRotationState Rotation { get; init; }
    public AlsDoubleVector Velocity { get; init; }
    public AlsDoubleVector LocalVelocity { get; init; }
    public double DirectionAngle { get; init; }
    public double DirectionAngleWithOffset { get; init; }
    public int Direction { get; init; }
    public int DirectionNoOffset { get; init; }
    public bool HasVelocity { get; init; }
    public AlsDoubleVector LocalAcceleration { get; init; }
    public bool HasAcceleration { get; init; }
    public AlsDoubleVector Pivot { get; init; }
    public int AccelerationDirection { get; init; }
    public bool Wall { get; init; }
    public bool Ground { get; init; }
    public bool Crouching { get; init; }
    public bool CrouchChanged { get; init; }
    public bool AdsChanged { get; init; }
    public bool WasAds { get; init; }
    public double TimeSinceFired { get; init; }
    public bool Jumping { get; init; }
    public bool Falling { get; init; }
    public bool First { get; init; }
    public double RootYaw { get; init; }
    public double DeadZone { get; init; }
    public bool Ads { get; init; }
    public bool Firing { get; init; }

    internal static AlsDoubleVector Vector(JsonElement row) => new(row.GetProperty("x").GetDouble(), row.GetProperty("y").GetDouble(), row.GetProperty("z").GetDouble());
    public static LyraMainObservationState Read(JsonElement row)
    {
        var rot = row.GetProperty("WorldRotation");
        return new()
        {
            Location = Vector(row.GetProperty("WorldLocation")), Displacement = row.GetProperty("DisplacementSinceLastUpdate").GetDouble(),
            DisplacementSpeed = row.GetProperty("DisplacementSpeed").GetDouble(),
            Rotation = new(rot.GetProperty("pitch").GetDouble(), rot.GetProperty("yaw").GetDouble(), rot.GetProperty("roll").GetDouble(),
                row.GetProperty("YawDeltaSinceLastUpdate").GetDouble(), row.GetProperty("YawDeltaSpeed").GetDouble(), row.GetProperty("AdditiveLeanAngle").GetDouble()),
            Velocity = Vector(row.GetProperty("WorldVelocity")), LocalVelocity = Vector(row.GetProperty("LocalVelocity2D")),
            DirectionAngle = row.GetProperty("LocalVelocityDirectionAngle").GetDouble(), DirectionAngleWithOffset = row.GetProperty("LocalVelocityDirectionAngleWithOffset").GetDouble(),
            Direction = row.GetProperty("LocalVelocityDirection").GetInt32(), DirectionNoOffset = row.GetProperty("LocalVelocityDirectionNoOffset").GetInt32(),
            HasVelocity = row.GetProperty("HasVelocity").GetBoolean(), LocalAcceleration = Vector(row.GetProperty("LocalAcceleration2D")),
            HasAcceleration = row.GetProperty("HasAcceleration").GetBoolean(), Pivot = Vector(row.GetProperty("PivotDirection2D")),
            AccelerationDirection = row.GetProperty("CardinalDirectionFromAcceleration").GetInt32(), Wall = row.GetProperty("IsRunningIntoWall").GetBoolean(),
            Ground = row.GetProperty("IsOnGround").GetBoolean(), Crouching = row.GetProperty("IsCrouching").GetBoolean(), CrouchChanged = row.GetProperty("CrouchStateChange").GetBoolean(),
            AdsChanged = row.GetProperty("ADSStateChanged").GetBoolean(), WasAds = row.GetProperty("WasADSLastUpdate").GetBoolean(),
            TimeSinceFired = row.GetProperty("TimeSinceFiredWeapon").GetDouble(), Jumping = row.GetProperty("IsJumping").GetBoolean(), Falling = row.GetProperty("IsFalling").GetBoolean(),
            First = row.GetProperty("IsFirstUpdate").GetBoolean(), RootYaw = row.GetProperty("RootYawOffset").GetDouble(), DeadZone = row.GetProperty("CardinalDirectionDeadZone").GetDouble(),
            Ads = row.GetProperty("GameplayTag_IsADS").GetBoolean(), Firing = row.GetProperty("GameplayTag_IsFiring").GetBoolean()
        };
    }
}

internal sealed record LyraMainObservationCandidate(long Frame, ImmutableArray<LyraMainObservationState> Stages)
{
    public LyraMainObservationState State => Stages[^1];
    public LyraCycleInput CycleInput => new(State.Crouching, State.Ads, State.DirectionNoOffset switch
        { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward,
          2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Invalid native cardinal direction.") },
        (float)State.DisplacementSpeed, State.Wall);
    public LyraMainRotationState LeanRotation => State.Rotation;
}

// First six original Main functions. Remaining blend/root/aim/jump updates
// and clearing FirstUpdate belong to the enclosing complete Main transaction.
internal sealed class LyraMainObservationHost
{
    public static readonly string[] Order = ["UpdateLocationData", "UpdateRotationData", "UpdateVelocityData",
        "UpdateAccelerationData", "UpdateWallDetectionHeuristic", "UpdateCharacterStateData"];
    private LyraMainObservationCandidate? _pending;
    private long _frame;
    internal long NextFrame=>_frame;
    public LyraMainObservationState State { get; private set; }
    public LyraMainObservationHost(LyraMainObservationState? initial = null)
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "main_observation_policy.json"));
        var policy = document.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 ||
            !policy.GetProperty("order").EnumerateArray().Select(v => v.GetString()).SequenceEqual(Order))
            throw new NotSupportedException("Changed Main observation update order.");
        foreach (var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if (dependency.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)))
                throw new InvalidOperationException("Changed Main observation dependency.");
        State = initial ?? LyraMainObservationState.Read(policy.GetProperty("initial"));
        if (State.DeadZone != policy.GetProperty("deadZone").GetDouble()) throw new InvalidOperationException("Changed Main direction dead zone.");
    }
    public LyraMainObservationCandidate Prepare(in LyraMainObservationInput input, float delta)
    {
        if (_pending is not null) throw new InvalidOperationException("Main observation frame is pending.");
        if (!input.Location.IsFinite || !input.Velocity.IsFinite || !input.Acceleration.IsFinite || !double.IsFinite(input.RootYaw) ||
            !float.IsFinite(delta) || delta < 0 || input.MovementMode is < 0 or > 6)
            throw new ArgumentException("Invalid Main observation snapshot.");
        var state = State with { First = input.Rotation.IsFirstUpdate, Ads = input.Ads, Firing = input.Firing, RootYaw = input.RootYaw };
        var stages = ImmutableArray.CreateBuilder<LyraMainObservationState>(6);
        var displacement = input.Location - state.Location;
        var distance = Math.Sqrt(displacement.X * displacement.X + displacement.Y * displacement.Y);
        state = state with { Location = input.Location, Displacement = state.First ? 0 : distance,
            DisplacementSpeed = state.First || delta == 0 ? 0 : distance / (double)delta };
        stages.Add(state);
        state = state with { Rotation = state.Rotation.Prepare(input.Rotation with { CrouchingAtRotation = state.Crouching, AdsAtRotation = state.Ads }, delta) };
        stages.Add(state);
        var movingBefore = state.LocalVelocity != AlsDoubleVector.Zero;
        var worldVelocity = input.Velocity with { Z = 0 };
        var velocity = Unrotate(worldVelocity, state.Rotation);
        var angle = (double)CalculateDirection(worldVelocity, state.Rotation);
        var angleWithOffset = angle - state.RootYaw;
        state = state with { Velocity = input.Velocity, LocalVelocity = velocity, DirectionAngle = angle, DirectionAngleWithOffset = angleWithOffset,
            Direction = Cardinal(angleWithOffset, state.DeadZone, state.Direction, movingBefore),
            DirectionNoOffset = Cardinal(angle, state.DeadZone, state.DirectionNoOffset, movingBefore),
            HasVelocity = Math.Abs(velocity.X * velocity.X + velocity.Y * velocity.Y) > 1e-6 };
        stages.Add(state);
        var accelerationWorld = input.Acceleration with { Z = 0 };
        var acceleration = Unrotate(accelerationWorld, state.Rotation);
        var pivot = Normal(state.Pivot + (Normal(accelerationWorld) - state.Pivot) * .5);
        state = state with { LocalAcceleration = acceleration, HasAcceleration = Math.Abs(acceleration.X * acceleration.X + acceleration.Y * acceleration.Y) > 1e-6,
            Pivot = pivot, AccelerationDirection = Cardinal(CalculateDirection(pivot, state.Rotation), state.DeadZone, 0, false) ^ 1 };
        stages.Add(state);
        var dot = AlsDoubleVector.Dot(Normal(acceleration), Normal(velocity));
        state = state with { Wall = Math.Sqrt(acceleration.X * acceleration.X + acceleration.Y * acceleration.Y) > .1 &&
            Math.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y) < 200 && dot >= -.6 && dot <= .6 };
        stages.Add(state);
        state = state with { Ground = input.Ground, Crouching = input.Crouching, CrouchChanged = input.Crouching != state.Crouching,
            AdsChanged = state.Ads != state.WasAds, WasAds = state.Ads, TimeSinceFired = state.Firing ? 0 : state.TimeSinceFired + (double)delta,
            Jumping = input.MovementMode == 3 && state.Velocity.Z > 0, Falling = input.MovementMode == 3 && state.Velocity.Z <= 0 };
        stages.Add(state);
        if (!double.IsFinite(state.DisplacementSpeed) || !velocity.IsFinite || !acceleration.IsFinite || !pivot.IsFinite)
            throw new ArgumentException("Nonfinite Main observation result.");
        return _pending = new(_frame, stages.MoveToImmutable());
    }
    public void ValidateCommit(LyraMainObservationCandidate candidate)
    { if (!ReferenceEquals(candidate, _pending) || candidate.Frame != _frame) throw new InvalidOperationException("Stale/repeated Main observation candidate."); }
    public void Commit(LyraMainObservationCandidate candidate)
    { ValidateCommit(candidate); State = candidate.State; _frame++; _pending = null; }
    public void CommitFinal(LyraMainObservationCandidate candidate, double rootYaw)
    {
        ValidateCommit(candidate);
        if (!double.IsFinite(rootYaw)) throw new ArgumentException("Nonfinite final Main root yaw.");
        State = candidate.State with { First = false, RootYaw = rootYaw }; _frame++; _pending = null;
    }
    public void Cancel() { _pending = null; }

    private static int Cardinal(double angle, double deadZone, int current, bool useCurrent)
    {
        var fwd = useCurrent && current == 0 ? deadZone * 2 : deadZone;
        var bwd = useCurrent && current == 1 ? deadZone * 2 : deadZone;
        return Math.Abs(angle) <= 45 + fwd ? 0 : Math.Abs(angle) >= 135 - bwd ? 1 : angle > 0 ? 3 : 2;
    }
    private static AlsDoubleVector Normal(AlsDoubleVector value) => value.SafeNormal(1e-4f);
    private static AlsAimingRotation Rotation(LyraMainRotationState rotation) => new(rotation.Pitch, rotation.Yaw, rotation.Roll);
    private static AlsDoubleVector Unrotate(AlsDoubleVector value, LyraMainRotationState rotation) =>
        AlsCharacterRotationMath.UnrotateVector(value, Rotation(rotation));
    private static float CalculateDirection(AlsDoubleVector value, LyraMainRotationState rotation) =>
        AlsCharacterRotationMath.CalculateDirection(value, Rotation(rotation));
}
