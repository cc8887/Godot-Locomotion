using System.Reflection;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainUpdateSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Complete Main update failed: " + error); GetTree().Quit(1); }
    }
    internal static LyraMainUpdateInput ReadInput(JsonElement frame)
    {
        var snapshot = frame.GetProperty("snapshot"); var rotation = snapshot.GetProperty("rotation");
        return new(new(LyraMainObservationState.Vector(snapshot.GetProperty("location")),
            new(rotation.GetProperty("pitch").GetDouble(), rotation.GetProperty("yaw").GetDouble(), rotation.GetProperty("roll").GetDouble(), false, false, false),
            LyraMainObservationState.Vector(snapshot.GetProperty("velocity")), LyraMainObservationState.Vector(snapshot.GetProperty("acceleration")),
            snapshot.GetProperty("ground").GetBoolean(), snapshot.GetProperty("crouching").GetBoolean(), snapshot.GetProperty("movementMode").GetInt32(),
            frame.GetProperty("ads").GetBoolean(), frame.GetProperty("firing").GetBoolean(), 0),
            snapshot.GetProperty("aimPitch").GetDouble(), snapshot.GetProperty("gravity").GetDouble(), snapshot.GetProperty("montage").GetBoolean(),
            frame.GetProperty("dashing").GetBoolean(), frame.GetProperty("enabled").GetBoolean(), frame.GetProperty("mode").GetInt32());
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        var bytes = Godot.FileAccess.GetFileAsBytes(root + "main_update_requests.json");
        using var requests = JsonDocument.Parse(bytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "main_update_native.json"));
        var data = native.RootElement;
        Require(data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(bytes), "Changed whole Main request dependency.");
        foreach (var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dep.Name)), "Changed whole Main provenance.");
        var frames = 0; var rejected = 0; var first = 0; var hold = 0; var dashAccumulate = 0;
        var disabledHold = 0; var groundMontage = 0; var airMontage = 0; var spring = 0; var rootChanges = 0;
        string identity = "";
        var comparison = new LyraMainNativeComparison();
        void Compare(string name, object a, object b) { comparison.Identity = identity; comparison.Compare(name, a, b); }
        void Reject(Action action)
        {
            try { action(); } catch (InvalidOperationException) { rejected++; return; } catch (ArgumentException) { rejected++; return; }
            throw new InvalidOperationException("Invalid full Main transaction accepted.");
        }
        for (var trace = 0; trace < 3; trace++)
        {
            var host = new LyraMainUpdateHost(); var clean = new LyraMainUpdateHost();
            var inputs = requests.RootElement.GetProperty("traces")[trace]; var outputs = data.GetProperty("traces")[trace];
            Compare("initial", host.State, LyraMainObservationState.Read(outputs.GetProperty("initial")));
            Compare("tailInitial", host.Tail, LyraMainTailState.Read(outputs.GetProperty("tailInitial")));
            LyraMainUpdateCandidate? old = null;
            for (var index = 0; index < inputs.GetProperty("frames").GetArrayLength(); index++)
            {
                identity = $"hz={inputs.GetProperty("hz")} frame={index}";
                var frame = inputs.GetProperty("frames")[index]; var row = outputs.GetProperty("frames")[index];
                var input = ReadInput(frame); var delta = frame.GetProperty("delta").GetSingle();
                var before = host.State; var tailBefore = host.Tail;
                Compare("before", before with { Ads = input.Observation.Ads, Firing = input.Observation.Firing }, LyraMainObservationState.Read(row.GetProperty("before")));
                Compare("tailBefore", tailBefore with { Enabled = input.RootYawEnabled, Dashing = input.Dashing, Mode = input.RootYawMode }, LyraMainTailState.Read(row.GetProperty("tailBefore")));
                var candidate = host.Prepare(input, delta);
                Compare("after", candidate.State, LyraMainObservationState.Read(row.GetProperty("after")));
                Compare("tailAfter", candidate.Tail, LyraMainTailState.Read(row.GetProperty("tailAfter")));
                Reject(() => host.Prepare(input, delta)); if (old is not null) Reject(() => host.Commit(old));
                if (before.First) first++;
                if (input.RootYawMode == 1 && !input.Dashing)
                {
                    hold++; Require(candidate.Tail.Spring == tailBefore.Spring && candidate.State.RootYaw == before.RootYaw, "Hold advanced root/spring history.");
                    if (!input.RootYawEnabled && before.RootYaw != 0) disabledHold++;
                }
                if (input.Dashing && input.RootYawMode == 2) dashAccumulate++;
                if (input.MontagePlaying && candidate.State.Ground) groundMontage++;
                if (input.MontagePlaying && !candidate.State.Ground) airMontage++;
                if (candidate.Tail.Spring.PreviousValid) spring++;
                if (candidate.State.RootYaw != before.RootYaw) rootChanges++;
                host.Cancel(); Require(host.State == before && host.Tail == tailBefore, "Cancellation published full Main history.");
                Reject(() => host.Commit(candidate));
                var retry = host.Prepare(input, delta); var single = clean.Prepare(input, delta);
                Require(retry.State == candidate.State && retry.Tail == candidate.Tail && retry.State == single.State && retry.Tail == single.Tail &&
                    retry.Observation.Stages.SequenceEqual(candidate.Observation.Stages), "Full Main retry/clean candidate diverged.");
                host.ValidateCommit(retry); host.Commit(retry); clean.Commit(single); Reject(() => host.Commit(retry));
                Require(host.State == retry.State && host.Tail == retry.Tail && !host.State.First && host.Tail.Mode == 0, "Incomplete whole Main commit.");
                old = retry; frames++;
            }
            var validFrame = ReadInput(inputs.GetProperty("frames")[0]);
            Reject(() => host.Prepare(validFrame with { RootYawMode = 3 }, .01f));
            Reject(() => host.Prepare(validFrame with { AimPitch = double.NaN }, .01f));
            var jumping = validFrame with { Gravity = 0, Observation = validFrame.Observation with { MovementMode = 3, Velocity = new(0,0,10) } };
            Reject(() => host.Prepare(jumping, .01f));
            var recovery = host.Prepare(validFrame, .01f); host.Commit(recovery);
        }
        Require(frames == 2520 && first == 3 && hold > 0 && disabledHold > 0 && dashAccumulate > 0 && groundMontage > 0 && airMontage > 0 && spring > 0 && rootChanges > 0,
            "Missing whole Main branch/history coverage.");
        GD.Print($"LYRA_MAIN_UPDATE_GODOT_OK frames={frames} first={first} hold={hold} disabledHold={disabledHold} dashAccumulate={dashAccumulate} groundMontage={groundMontage} airMontage={airMontage} spring={spring} rootChanges={rootChanges} vectorCm={comparison.MaxVector:R} rejected={rejected} scalarsAndSpring=exactBits retry=true updateOnly=true production=false mainGraph=false");
    }
}

internal sealed class LyraMainNativeComparison
{
    public double MaxVector { get; private set; }
    public string Identity { get; set; } = "";
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    public void Compare(string name, object a, object b)
        {
            if (a is AlsDoubleVector va && b is AlsDoubleVector vb)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    var difference = Math.Abs(va[axis] - vb[axis]); MaxVector = Math.Max(MaxVector, difference);
                    Require(difference <= 1e-10, $"{Identity} {name}[{axis}] {va[axis]:R} != {vb[axis]:R}");
                }
            }
            else if (a is double da && b is double db)
                Require(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db), $"{Identity} {name} {da:R} != {db:R}");
            else if (a is float fa && b is float fb)
                Require(BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb), $"{Identity} {name} {fa:R} != {fb:R}");
            else if (a.GetType().IsPrimitive || a.GetType().IsEnum)
                Require(a.Equals(b), $"{Identity} {name} {a} != {b}");
            else
                foreach (var p in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Compare(name + "." + p.Name, p.GetValue(a)!, p.GetValue(b)!);
        }
}
