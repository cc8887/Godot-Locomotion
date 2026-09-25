using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponStatePoseTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases() => AlsRefactoredWeaponNativeTests.Cases();
    [Theory]
    [MemberData(nameof(Cases))]
    public void FullWeightStatesMatchNativePoseAndCurves(AlsRefactoredWeaponKind kind, int hz)
        => Compare(kind, hz, false);
    [Theory]
    [MemberData(nameof(Cases))]
    public void OrderedMachineTransitionsMatchNativePoseAndCurves(AlsRefactoredWeaponKind kind, int hz)
        => Compare(kind, hz, true);
    private void Compare(AlsRefactoredWeaponKind kind, int hz, bool wholeMachine)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var source = new AlsRefactoredWeaponSourceProfile(catalog, new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind)));
        var profile = new AlsRefactoredWeaponStatePose(catalog, source); var sampler = profile.CreateSampler();
        var machinePose = new AlsRefactoredWeaponMachinePose(profile);
        var machine = source.Machine.CreateRuntime(); var update = source.CreateRuntime(0);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, new(MantlingHostFixture.Read("refactored_sync_inputs"), catalog),
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), source.Players.Bind(0, new Dictionary<string, int> { ["Secondary Motion"] = 0, ["Movement"] = 1 }));
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_weapon_source_trace_" + kind));
        var trace = doc.RootElement.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz + "hz");
        Assert.Equal(profile.BoneNames.ToArray(), doc.RootElement.GetProperty("names").EnumerateArray().Select(n => n.GetString()!));
        var result = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[profile.CurveNames.Length];
        var frame = 0; var compared = 0; var multi = 0; var quick = 0; var states = new HashSet<int>(); double maxP = 0, maxQ = 0, maxS = 0, maxC = 0, maxAlpha = 0;
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var delta = input.GetProperty("delta").GetSingle(); var ps = input.GetProperty("poseState");
            float V(string key) => ps.GetProperty(key).GetSingle();
            var vel = input.GetProperty("groundedState").GetProperty("VelocityBlend");
            var value = new AlsRefactoredWeaponPoseInput(V("GaitWalkingAmount"), V("GaitRunningAmount"), V("GaitSprintingAmount"), V("StandingAmount"), V("CrouchingAmount"), V("InAirAmount"),
                input.GetProperty("inAirState").GetProperty("GroundPredictionAmount").GetSingle(), input.GetProperty("standingState").GetProperty("SprintAccelerationAmount").GetSingle(),
                new Vector4(vel.GetProperty("ForwardAmount").GetSingle(), vel.GetProperty("BackwardAmount").GetSingle(), vel.GetProperty("LeftAmount").GetSingle(), vel.GetProperty("RightAmount").GetSingle()));
            var rules = new AlsRefactoredWeaponRuleInput(input.GetProperty("rotationMode").GetString()!, input.GetProperty("gait").GetString()!, input.GetProperty("locomotionMode").GetString()!, input.GetProperty("moving").GetBoolean(), input.GetProperty("allowed").GetBoolean());
            machine.Prepare(frame, rules, delta, reinitialize: input.GetProperty("reset").GetBoolean()); update.Prepare(frame, machine, value, delta); players.Prepare(frame, update.SourceInputs, delta);
            var nativeTransitions = row.GetProperty("transitions"); var transitions = machine.Candidate.State.Transitions;
            Assert.Equal(nativeTransitions.GetArrayLength(), transitions.Count);
            if (transitions.Count > 1) multi++;
            for (var i = 0; i < transitions.Count; i++)
            {
                maxAlpha = Math.Max(maxAlpha, Math.Abs(transitions.GetTransition(i).Alpha - nativeTransitions[i].GetProperty("alpha").GetSingle()));
                if (source.Machine.Resources.Edges[machine.Candidate.State.GetActivePath(i).Edge].QuickFeet) quick++;
            }
            Assert.True(maxAlpha <= 2e-6);
            var weights = row.GetProperty("weights").EnumerateArray().Select(w => w.GetSingle()).ToArray();
            var state = wholeMachine ? row.GetProperty("current").GetInt32() : Array.IndexOf(weights, 1f);
            // This oracle is the complete linked graph. It isolates a state only
            // when no transition or outer action contributes to the output.
            if ((wholeMachine || state >= 0 && weights.Count(w => w != 0) == 1 && row.GetProperty("transitions").GetArrayLength() == 0) && input.GetProperty("action").GetString() == "")
            {
                var pitch = input.GetProperty("viewState").GetProperty("PitchAmount").GetSingle();
                void Sample(long serial)
                {
                    if (wholeMachine) machinePose.Sample(serial, pitch, machine, update, players, result, curves);
                    else sampler.Sample(serial, state, pitch, update, players, result, curves);
                }
                Sample(frame); compared++; states.Add(state);
                var before = result.ToArray(); var beforeCurves = curves.ToArray();
                if (frame == 0)
                {
                    var foreign = source.Machine.CreateRuntime(); foreign.Prepare(frame, rules, delta);
                    Assert.Throws<ArgumentException>(() => machinePose.Sample(frame, pitch, foreign, update, players, result, curves));
                    Assert.Throws<ArgumentException>(() => sampler.Sample(frame, state, float.NaN, update, players, result, curves));
                }
                Assert.Throws<ArgumentException>(() => Sample(frame + 1));
                Assert.Equal(before, result); Assert.Equal(beforeCurves, curves);
                if (frame % 17 == 0)
                {
                    update.Cancel(); players.Cancel();
                    Assert.Throws<ArgumentException>(() => Sample(frame));
                    update.Prepare(frame, machine, value, delta); players.Prepare(frame, update.SourceInputs, delta);
                    Sample(frame); Assert.Equal(before, result); Assert.Equal(beforeCurves, curves);
                }
                for (var b = 0; b < result.Length; b++)
                {
                    var native = row.GetProperty("pose")[b]; var actual = result[b];
                    double[] Values(string n) => native.GetProperty(n).EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    var p = Values("position"); var q = Values("rotation"); var s = Values("scale");
                    var dp = Math.Sqrt(Math.Pow(actual.Position.X-p[0],2)+Math.Pow(actual.Position.Y-p[1],2)+Math.Pow(actual.Position.Z-p[2],2));
                    var sign = actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3]<0?-1:1;
                    var dq = new[]{Math.Abs(actual.Rotation.X-sign*q[0]),Math.Abs(actual.Rotation.Y-sign*q[1]),Math.Abs(actual.Rotation.Z-sign*q[2]),Math.Abs(actual.Rotation.W-sign*q[3])}.Max();
                    var ds = new[]{Math.Abs(actual.Scale.X-s[0]),Math.Abs(actual.Scale.Y-s[1]),Math.Abs(actual.Scale.Z-s[2])}.Max();
                    maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                    // Single-state sampling has double-pose precision. Composed
                    // transitions inherit the float curve/weight budget (2e-6),
                    // including UE's fp:fast rounding. 1e-4 cm is 1 micrometer.
                    Assert.True(dp <= (wholeMachine ? 1e-4 : 1e-10) && dq <= (wholeMachine ? 2e-6 : 1e-12) && ds <= (wholeMachine ? 2e-6 : 1e-12),$"{kind}/{hz}/{frame} state={state} bone={b} p={dp:R} q={dq:R} s={ds:R} alpha={(machine.Candidate.State.Transitions.Count > 0 ? machine.Candidate.State.Transitions.GetTransition(0).Alpha : -1):R} native={row.GetProperty("transitions").GetRawText()}");
                }
                var nativeCurves = row.GetProperty("curves"); var present = 0;
                for (var c = 0; c < curves.Length; c++)
                {
                    Assert.Equal(curves[c].Present, nativeCurves.TryGetProperty(profile.CurveNames[c], out var expected)); if (!curves[c].Present) continue; present++;
                    var dc = Math.Abs(curves[c].Value - expected.GetSingle()); maxC = Math.Max(maxC, dc); Assert.True(dc<=2e-6,$"{kind}/{hz}/{frame} curve={profile.CurveNames[c]} diff={dc:R}");
                }
                Assert.Equal(present, nativeCurves.EnumerateObject().Count());
            }
            machine.ValidateCommit(frame); update.ValidateCommit(frame); players.ValidateCommit(frame);
            machine.Commit(frame); update.Commit(frame); players.Commit(frame++);
        }
        Assert.True(compared > hz); Assert.Equal(3, states.Count);
        Assert.True(multi > 0);
        // Existing traces take/replace the ready exits without retaining edge 2
        // for Evaluate. Do not claim native QuickFeet pose coverage from them.
        Assert.Equal(0, quick);
        if (wholeMachine) Assert.Equal(frame, compared);
        output.WriteLine($"{kind}/{hz} machine={wholeMachine}: frames={frame} compared={compared} multi={multi} quick={quick} maxP={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxC={maxC:R} maxAlpha={maxAlpha:R}");
    }
}
