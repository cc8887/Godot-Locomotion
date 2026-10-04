using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigDynamicsSmoke : Node
{
    private const string Root = "res://assets/generated/lyra_als/";
    private static JsonDocument Load(string n) => JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + n + ".json"));
    private static void Require(bool v, string message) { if (!v) throw new InvalidOperationException(message); }
    private static AlsDoubleVector V(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
    private static string State(LyraFootPlantRigDynamics d) => JsonSerializer.Serialize(new
    { vectors = new[] { d.Vector(0), d.Vector(1) }, scalars = new[] { d.Scalar(2), d.Scalar(3), d.Scalar(4) }, alphas = new[] { d.Alpha(5), d.Alpha(6) } });
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig dynamics failed: " + e); GetTree().Quit(1); }
    }
    private static void Run()
    {
        using var program = Load("footplant_rig_inputs_v1_program"); using var requests = Load("rig_dynamics_v1_requests");
        using var native = Load("rig_dynamics_v1_native");
        var n = native.RootElement; var q = requests.RootElement;
        Require(n.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "rig_dynamics_v1_requests.json")), "Stale dynamics requests.");
        Require(q.GetProperty("dependencies").GetProperty("footplant_rig_inputs_v1_program.json").GetString() ==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + "footplant_rig_inputs_v1_program.json")), "Stale dynamics program.");
        int frames = 0, calls = 0, retries = 0, resets = 0, rejects = 0, comparisons = 0;
        double maxVector = 0; float maxFloat = 0;
        void Float(float value, JsonElement expected, string label)
        {
            float b = expected.GetSingle(); maxFloat = Math.Max(maxFloat, Math.Abs(value - b));
            Require(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(b), $"{label}: float {value:R} != {b:R}"); comparisons++;
        }
        void Vector(AlsDoubleVector value, JsonElement expected, string label)
        {
            var b = V(expected); double error = Math.Max(Math.Max(Math.Abs(value.X-b.X), Math.Abs(value.Y-b.Y)), Math.Abs(value.Z-b.Z));
            maxVector = Math.Max(maxVector, error); Require(error <= 1e-10, $"{label}: vector error {error:R}"); comparisons += 3;
        }
        void Compare(LyraFootPlantRigDynamics d, int owner, JsonElement e, string label)
        {
            if (owner < 2)
            {
                var v = d.Vector(owner); Vector(v.Result, e.GetProperty("result"), label+"/result"); Vector(v.Result, e.GetProperty("simulated"), label+"/simulated");
                Vector(v.Velocity, e.GetProperty("velocity"), label+"/velocity"); Vector(v.PreviousTarget, e.GetProperty("target"), label+"/target");
                Vector(d.VectorOutputVelocity(owner), e.GetProperty("outputVelocity"), label+"/outputVelocity");
                Require(v.PreviousValid == e.GetProperty("valid").GetBoolean(), "Vector spring validity differs."); comparisons++;
            }
            else if (owner < 5)
            {
                var s = d.Scalar(owner); Float(s.Value, e.GetProperty("result"), label+"/result"); Float(s.Value, e.GetProperty("simulated"), label+"/simulated");
                Float(s.State.Velocity, e.GetProperty("velocity"), label+"/velocity"); Float(s.State.Velocity, e.GetProperty("outputVelocity"), label+"/outputVelocity");
                Float(s.State.PreviousTarget, e.GetProperty("target"), label+"/target"); Require(s.State.PreviousValid == e.GetProperty("valid").GetBoolean(), "Scalar spring validity differs."); comparisons++;
            }
            else
            {
                var a = d.Alpha(owner); Float(a.Result, e.GetProperty("result"), label+"/result"); Float(a.Interpolated, e.GetProperty("interpolated"), label+"/interpolated");
                Require(a.Initialized == e.GetProperty("initialized").GetBoolean(), "Alpha initialization differs."); comparisons++;
            }
        }
        for (int ti = 0; ti < n.GetProperty("traces").GetArrayLength(); ti++)
        {
            var t = n.GetProperty("traces")[ti]; var r = q.GetProperty("traces")[ti];
            Require(t.GetProperty("name").GetString() == r.GetProperty("name").GetString(), "Different dynamics trace.");
            var committed = new LyraFootPlantRigDynamics(program.RootElement);
            for (int owner = 0; owner < 7; owner++) Compare(committed, owner, t.GetProperty("initial")[owner], "initial/"+ti);
            for (int fi = 0; fi < t.GetProperty("frames").GetArrayLength(); fi++)
            {
                var expected = t.GetProperty("frames")[fi]; var frame = r.GetProperty("frames")[fi]; string before = State(committed); string? first = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var candidate = committed.Clone(); if (frame.GetProperty("reset").GetBoolean()) { candidate.Reset(); if (attempt == 1) resets++; }
                    int ci = 0;
                    foreach (var call in frame.GetProperty("calls").EnumerateArray())
                    {
                        int owner = call.GetProperty("owner").GetInt32(); double delta = frame.GetProperty("delta").GetDouble();
                        if (owner < 2) candidate.ExecuteVector(owner, delta, V(call.GetProperty("target")), V(call.GetProperty("current")));
                        else candidate.ExecuteScalar(owner, delta, call.GetProperty("target").GetSingle(), call.GetProperty("current").GetSingle());
                        Compare(candidate, owner, expected.GetProperty("calls")[ci++], $"{ti}/{fi}/{owner}/{ci}"); if (attempt == 1) calls++;
                    }
                    for (int owner = 0; owner < 7; owner++) Compare(candidate, owner, expected.GetProperty("after")[owner], $"{ti}/{fi}/after/{owner}");
                    first ??= State(candidate); Require(first == State(candidate), "Dynamics cancelled retry diverged.");
                    if (attempt == 0) { Require(before == State(committed), "Candidate changed committed dynamics."); retries++; }
                    else committed.CopyFrom(candidate);
                }
                frames++;
            }
            foreach (var bad in new Action[] { () => committed.ExecuteScalar(7, .01, 0), () => committed.ExecuteScalar(2, -1, 0),
                () => committed.ExecuteVector(0, .01, new(double.NaN, 0, 0), default) })
            {
                string before = State(committed);
                try { bad(); }
                catch (ArgumentException) { Require(before == State(committed), "Rejected dynamics changed history."); rejects++; continue; }
                throw new InvalidOperationException("Invalid dynamics call accepted.");
            }
        }
        Require(frames == 3360 && calls == 19217 && retries == frames && rejects == 27, "Incomplete real unit coverage.");
        GD.Print($"LYRA_RIG_DYNAMICS_GODOT_OK frames={frames} calls={calls} retries={retries} resets={resets} rejects={rejects} comparisons={comparisons} maxVector={maxVector:R} maxFloat={maxFloat:R} fullRig=false");
    }
}
