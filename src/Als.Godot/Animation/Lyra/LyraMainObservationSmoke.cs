using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainObservationSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main observation failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        var bytes = Godot.FileAccess.GetFileAsBytes(root + "main_observation_requests.json");
        using var requests = JsonDocument.Parse(bytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "main_observation_native.json"));
        var data = native.RootElement;
        Require(data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(bytes), "Changed observation requests.");
        foreach (var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dep.Name)), "Changed observation dependencies.");
        double maxVector = 0;
        var frames = 0; var stages = 0; var rejected = 0; var wall = 0; var jump = 0; var fall = 0; var crouch = 0; var ads = 0;
        var directions = new HashSet<int>();
        string identity = "";
        void Exact(string name, double a, double b)
            => Require(BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b), $"{identity} {name} actual={a:R} expected={b:R}");
        void Vector(string name, AlsDoubleVector a, AlsDoubleVector b)
        {
            for (var i = 0; i < 3; i++)
            {
                var error = Math.Abs(a[i] - b[i]); maxVector = Math.Max(maxVector, error);
                Require(error <= 1e-10, $"{identity} {name}[{i}] actual={a[i]:R} expected={b[i]:R} error={error:R}");
            }
        }
        void Compare(LyraMainObservationState a, LyraMainObservationState b)
        {
            Vector("Location", a.Location, b.Location); Vector("Velocity", a.Velocity, b.Velocity);
            Vector("LocalVelocity", a.LocalVelocity, b.LocalVelocity); Vector("Acceleration", a.LocalAcceleration, b.LocalAcceleration); Vector("Pivot", a.Pivot, b.Pivot);
            Exact("Displacement", a.Displacement, b.Displacement); Exact("DisplacementSpeed", a.DisplacementSpeed, b.DisplacementSpeed);
            Exact("pitch", a.Rotation.Pitch, b.Rotation.Pitch); Exact("yaw", a.Rotation.Yaw, b.Rotation.Yaw); Exact("roll", a.Rotation.Roll, b.Rotation.Roll);
            Exact("YawDelta", a.Rotation.YawDelta, b.Rotation.YawDelta); Exact("YawSpeed", a.Rotation.YawSpeed, b.Rotation.YawSpeed); Exact("LeanAngle", a.Rotation.LeanAngle, b.Rotation.LeanAngle);
            Exact("DirectionAngle", a.DirectionAngle, b.DirectionAngle); Exact("DirectionAngleWithOffset", a.DirectionAngleWithOffset, b.DirectionAngleWithOffset);
            Exact("TimeSinceFired", a.TimeSinceFired, b.TimeSinceFired); Exact("RootYaw", a.RootYaw, b.RootYaw); Exact("DeadZone", a.DeadZone, b.DeadZone);
            // Strip already compared floating fields to compare every remaining boolean/enum.
            Require(a with { Location=b.Location, Velocity=b.Velocity, LocalVelocity=b.LocalVelocity,
                LocalAcceleration=b.LocalAcceleration, Pivot=b.Pivot, Rotation=b.Rotation,
                Displacement=b.Displacement, DisplacementSpeed=b.DisplacementSpeed, DirectionAngle=b.DirectionAngle,
                DirectionAngleWithOffset=b.DirectionAngleWithOffset, TimeSinceFired=b.TimeSinceFired, RootYaw=b.RootYaw, DeadZone=b.DeadZone } == b,
                $"{identity} boolean/cardinal history mismatch actual={a} expected={b}");
        }
        void Reject(Action action)
        {
            try { action(); } catch (InvalidOperationException) { rejected++; return; } catch (ArgumentException) { rejected++; return; }
            throw new InvalidOperationException("Invalid observation operation accepted.");
        }
        for (var trace = 0; trace < 3; trace++)
        {
            var host = new LyraMainObservationHost(); var clean = new LyraMainObservationHost();
            var inputTrace = requests.RootElement.GetProperty("traces")[trace]; var outputTrace = data.GetProperty("traces")[trace];
            Compare(host.State, LyraMainObservationState.Read(outputTrace.GetProperty("initial")));
            LyraMainObservationCandidate? old = null;
            for (var index = 0; index < inputTrace.GetProperty("frames").GetArrayLength(); index++)
            {
                identity = $"hz={inputTrace.GetProperty("hz")} frame={index}";
                var frame = inputTrace.GetProperty("frames")[index]; var row = outputTrace.GetProperty("frames")[index];
                var snapshot = frame.GetProperty("snapshot"); var rotation = snapshot.GetProperty("rotation"); var delta = frame.GetProperty("delta").GetSingle();
                var input = new LyraMainObservationInput(LyraMainObservationState.Vector(snapshot.GetProperty("location")),
                    new(rotation.GetProperty("pitch").GetDouble(), rotation.GetProperty("yaw").GetDouble(), rotation.GetProperty("roll").GetDouble(), frame.GetProperty("first").GetBoolean(), false, false),
                    LyraMainObservationState.Vector(snapshot.GetProperty("velocity")), LyraMainObservationState.Vector(snapshot.GetProperty("acceleration")),
                    snapshot.GetProperty("ground").GetBoolean(), snapshot.GetProperty("crouching").GetBoolean(), snapshot.GetProperty("movementMode").GetInt32(),
                    frame.GetProperty("ads").GetBoolean(), frame.GetProperty("firing").GetBoolean(), frame.GetProperty("rootYaw").GetDouble());
                var before = host.State; var candidate = host.Prepare(input, delta);
                if (old is not null) Reject(() => host.Commit(old));
                Reject(() => host.Prepare(input, delta));
                for (var stage = 0; stage < 6; stage++)
                {
                    identity = $"hz={inputTrace.GetProperty("hz")} frame={index} stage={LyraMainObservationHost.Order[stage]}";
                    Compare(candidate.Stages[stage], LyraMainObservationState.Read(row.GetProperty("stages")[stage])); stages++;
                }
                host.Cancel(); Require(host.State == before, "Cancellation published observation history."); Reject(() => host.Commit(candidate));
                var retry = host.Prepare(input, delta); var single = clean.Prepare(input, delta);
                Require(retry.Stages.SequenceEqual(candidate.Stages) && retry.Stages.SequenceEqual(single.Stages), "Retry/clean update diverged.");
                // Public typed binding uses newly refreshed Crouching/ADS and raw (no RootYaw) direction.
                var cycle = retry.CycleInput; Require(cycle.Direction == (retry.State.DirectionNoOffset switch
                    { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward, 2 => LyraCardinalDirection.Left,
                      3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Invalid direction.") }) && cycle.Crouching == retry.State.Crouching &&
                    cycle.Ads == retry.State.Ads && cycle.DisplacementSpeed == (float)retry.State.DisplacementSpeed && cycle.RunningIntoWall == retry.State.Wall,
                    "Main to Cycle callback binding mismatch.");
                host.ValidateCommit(retry); host.Commit(retry); clean.Commit(single); Reject(() => host.Commit(retry));
                old = retry; frames++; directions.Add(host.State.DirectionNoOffset);
                if(host.State.Wall) wall++; if(host.State.Jumping) jump++; if(host.State.Falling) fall++;
                if(host.State.CrouchChanged) crouch++; if(host.State.AdsChanged) ads++;
            }
            Reject(() => host.Prepare(new(default, default, new(double.NaN,0,0), default, false, false, 0, false, false, 0), .01f));
        }
        Require(frames == 2520 && stages == 15120 && wall > 0 && jump > 0 && fall > 0 && crouch > 0 && ads > 0 && directions.Count == 4, "Missing observation coverage.");
        GD.Print($"LYRA_MAIN_OBSERVATION_GODOT_OK frames={frames} stages={stages} vectorCm={maxVector:R} wall={wall} jumping={jump} falling={fall} crouchChanged={crouch} adsChanged={ads} directions={directions.Count} rejected={rejected} scalarAndFlags=exactBits retry=true updateOnly=true production=false wholeMain=false");
    }
}
