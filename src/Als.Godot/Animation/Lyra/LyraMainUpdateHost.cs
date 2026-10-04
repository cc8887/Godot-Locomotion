using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainUpdateInput(LyraMainObservationInput Observation,
    double AimPitch, double Gravity, bool MontagePlaying, bool Dashing, bool RootYawEnabled, int RootYawMode);

internal sealed record LyraMainTailState(double UpperbodyWeight, double AimYaw, double AimPitch,
    double TimeToApex, AlsFloatSpringState Spring, int Mode, bool Enabled, bool Dashing)
{
    private static double NativeDouble(JsonElement row, string name) => BitConverter.Int64BitsToDouble(
        unchecked((long)Convert.ToUInt64(row.GetProperty(name + "Bits").GetString(), 16)));
    public static LyraMainTailState Read(JsonElement row) => new(
        NativeDouble(row, "UpperbodyDynamicAdditiveWeight"), NativeDouble(row, "AimYaw"),
        NativeDouble(row, "AimPitch"), NativeDouble(row, "TimeToJumpApex"),
        new(BitConverter.Int32BitsToSingle(unchecked((int)row.GetProperty("springVelocityBits").GetUInt32())),
            BitConverter.Int32BitsToSingle(unchecked((int)row.GetProperty("springPreviousBits").GetUInt32())), row.GetProperty("springValid").GetBoolean()),
        row.GetProperty("mode").GetInt32(), row.GetProperty("enabled").GetBoolean(), row.GetProperty("dashing").GetBoolean());
}

internal sealed record LyraMainUpdateCandidate(LyraMainObservationCandidate Observation,
    LyraMainObservationState State, LyraMainTailState Tail)
{
    public LyraCycleInput CycleInput => Observation.CycleInput;
    public LyraMainRotationState LeanRotation => State.Rotation;
}

// Original complete thread-safe Main macro. Its history is committed only after
// the enclosing graph has validated all of its dependent candidates.
internal sealed class LyraMainUpdateHost
{
    public static readonly string[] Order = [..LyraMainObservationHost.Order, "UpdateBlendWeightData",
        "UpdateRootYawOffset", "UpdateAimingData", "UpdateJumpFallData", "ClearFirstUpdate"];
    private readonly LyraMainObservationHost _observation;
    private readonly double _standingMin, _standingMax, _crouchedMin, _crouchedMax;
    private LyraMainUpdateCandidate? _pending;
    internal bool HasPending=>_pending is not null;
    internal long NextFrame=>_observation.NextFrame;
    public LyraMainObservationState State => _observation.State;
    public LyraMainTailState Tail { get; private set; }

    public LyraMainUpdateHost()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "main_update_policy.json"));
        var policy = document.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 ||
            !policy.GetProperty("order").EnumerateArray().Select(v => v.GetString()).SequenceEqual(Order))
            throw new NotSupportedException("Changed complete Main update order.");
        foreach (var dep in policy.GetProperty("dependencies").EnumerateObject())
            if (dep.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dep.Name)))
                throw new InvalidOperationException("Changed complete Main update dependency.");
        _observation = new(LyraMainObservationState.Read(policy.GetProperty("initial")));
        var initial = policy.GetProperty("tailInitial"); Tail = LyraMainTailState.Read(initial);
        _standingMin = initial.GetProperty("standing")[0].GetDouble(); _standingMax = initial.GetProperty("standing")[1].GetDouble();
        _crouchedMin = initial.GetProperty("crouched")[0].GetDouble(); _crouchedMax = initial.GetProperty("crouched")[1].GetDouble();
    }
    public LyraMainUpdateCandidate Prepare(in LyraMainUpdateInput input, float delta)
    {
        if (_pending is not null) throw new InvalidOperationException("Complete Main frame is pending.");
        if (!double.IsFinite(input.AimPitch) || !float.IsFinite((float)input.AimPitch) ||
            !double.IsFinite(input.Gravity) || input.RootYawMode is < 0 or > 2)
            throw new ArgumentException("Invalid complete Main input.");
        try
        {
            var observationInput = input.Observation with { RootYaw = State.RootYaw,
                Rotation = input.Observation.Rotation with { IsFirstUpdate = State.First } };
            var observation = _observation.Prepare(observationInput, delta); var state = observation.State;
            var tail = Tail with { Enabled = input.RootYawEnabled, Dashing = input.Dashing, Mode = input.RootYawMode };
            var distance = 0d - tail.UpperbodyWeight;
            var upperbody = input.MontagePlaying && state.Ground ? 1d : distance * distance < (double)1e-8f ? 0d :
                tail.UpperbodyWeight + distance * Math.Clamp((double)delta * 6d, 0d, 1d);
            var rootYaw = state.RootYaw; var aimYaw = tail.AimYaw;
            var enabled = input.RootYawEnabled;
            void SetRootYaw(double value)
            {
                if (!enabled) { rootYaw = 0; aimYaw = 0; return; }
                var normalized = (double)NormalizeFloatAxis((float)value);
                var min = state.Crouching ? _crouchedMin : _standingMin;
                var max = state.Crouching ? _crouchedMax : _standingMax;
                rootYaw = min == max ? normalized : AlsCharacterRotationMath.ClampAngle(normalized, min, max);
                aimYaw = rootYaw * -1d;
            }
            if (tail.Mode == 2) SetRootYaw(rootYaw - state.Rotation.YawDelta);
            var spring = tail.Spring;
            if (tail.Dashing || tail.Mode == 0)
            {
                var result = AlsKismetFloatSpring.Evaluate((float)rootYaw, 0, spring, 80, 1, delta, 1, .5f);
                spring = result.State; SetRootYaw(result.Value);
            }
            if (state.Jumping && input.Gravity == 0) throw new ArgumentException("Nonfinite original jump apex division.");
            var apex = state.Jumping ? (0d - state.Velocity.Z) / input.Gravity : 0d;
            tail = tail with { UpperbodyWeight = upperbody, AimYaw = aimYaw,
                AimPitch = NormalizeFloatAxis((float)input.AimPitch), TimeToApex = apex, Spring = spring, Mode = 0 };
            if (!double.IsFinite(upperbody) || !double.IsFinite(rootYaw) || !double.IsFinite(apex))
                throw new ArgumentException("Nonfinite complete Main candidate.");
            return _pending = new(observation, state with { First = false, RootYaw = rootYaw }, tail);
        }
        catch { _observation.Cancel(); throw; }
    }
    public void ValidateCommit(LyraMainUpdateCandidate candidate, int? graphRootYawMode = null)
    {
        if (!ReferenceEquals(candidate, _pending)) throw new InvalidOperationException("Stale/repeated complete Main candidate.");
        if (graphRootYawMode is < 0 or > 2) throw new ArgumentException("Invalid graph RootYaw mode.");
        _observation.ValidateCommit(candidate.Observation);
    }
    internal LyraMainUpdateCandidate ApplyGraphRootYaw(LyraMainUpdateCandidate candidate,double value)
    {
        ValidateCommit(candidate);
        if(!double.IsFinite(value))throw new ArgumentException("Nonfinite graph root yaw.");
        var root=0d;var aim=0d;
        if(candidate.Tail.Enabled)
        {
            var normalized=(double)NormalizeFloatAxis((float)value);
            var min=candidate.State.Crouching?_crouchedMin:_standingMin;var max=candidate.State.Crouching?_crouchedMax:_standingMax;
            root=min==max?normalized:AlsCharacterRotationMath.ClampAngle(normalized,min,max);aim=root*-1d;
        }
        return _pending=candidate with{State=candidate.State with{RootYaw=root},Tail=candidate.Tail with{AimYaw=aim}};
    }
    public void Commit(LyraMainUpdateCandidate candidate, int? graphRootYawMode = null)
    {
        ValidateCommit(candidate, graphRootYawMode); _observation.CommitFinal(candidate.Observation, candidate.State.RootYaw);
        Tail = graphRootYawMode.HasValue ? candidate.Tail with { Mode = graphRootYawMode.Value } : candidate.Tail;
        _pending = null;
    }
    public void Cancel() { _observation.Cancel(); _pending = null; }
    private static float NormalizeFloatAxis(float angle) => (float)AlsCharacterRotationMath.Normalize((double)angle);
}
