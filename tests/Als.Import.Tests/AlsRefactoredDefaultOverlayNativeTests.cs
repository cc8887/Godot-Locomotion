using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDefaultOverlayNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void OriginalAnimGraphMatchesCandidateClocksPredictionPoseAndCurves(int hz)
    {
        var json = MantlingHostFixture.Read("refactored_animation_sources");
        var catalog = new AlsRefactoredAnimationCatalog(json,
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var sync = MantlingHostFixture.Read("refactored_sync_inputs");
        var bank = new AlsRefactoredSyncBank(sync, catalog);
        var profile = AlsRefactoredDefaultOverlayCompiler.Compile(catalog);
        var overlay = profile.CreateRuntime(0);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank,
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), [profile.PlayerDefinition(0, 0)]);
        using var document = JsonDocument.Parse(MantlingHostFixture.Read("refactored_default_overlay_trace"));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(AlsRefactoredDefaultOverlayProfile.Blueprint, root.GetProperty("source").GetString());
        Assert.Equal(profile.BoneNames.ToArray(), root.GetProperty("names").EnumerateArray().Select(n => n.GetString()!));
        foreach (var (name, text) in new[] { ("refactored_animation_sources", json), ("refactored_sync_inputs", sync) })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == $"{hz}hz");
        var frame = 0; double maxP = 0, maxQ = 0, maxS = 0, maxC = 0, maxA = 0, maxT = 0;
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request = row.GetProperty("input"); var poseState = request.GetProperty("poseState");
            float V(string name) => poseState.GetProperty(name).GetSingle();
            var input = new AlsRefactoredDefaultOverlayInput(V("GaitWalkingAmount"), V("StandingAmount"), V("CrouchingAmount"), V("InAirAmount"),
                request.GetProperty("inAirState").GetProperty("GroundPredictionAmount").GetSingle());
            var delta = request.GetProperty("delta").GetSingle(); var reset = request.GetProperty("reset").GetBoolean();
            overlay.Prepare(frame, input, delta, reinitialize: reset); players.Prepare(frame, [overlay.SourceInput], delta);
            overlay.Evaluate(frame, players);
            var first = overlay.Pose.ToArray(); var curves = overlay.Curves.ToArray();
            overlay.Cancel(); players.Cancel();
            overlay.Prepare(frame, input, delta, reinitialize: reset); players.Prepare(frame, [overlay.SourceInput], delta);
            overlay.Evaluate(frame, players);
            Assert.Equal(first, overlay.Pose.ToArray()); Assert.Equal(curves, overlay.Curves.ToArray());
            var da = Math.Abs(overlay.CandidateState.Prediction - row.GetProperty("predictionAlpha").GetSingle());
            var dt = Math.Abs(players.Players[0].Time - row.GetProperty("idleTime").GetSingle());
            maxA = Math.Max(maxA, da); maxT = Math.Max(maxT, dt);
            Assert.True(da <= 1e-6 && dt <= 2e-6, $"{hz}Hz frame={frame} prediction={da:R} time={dt:R}");
            Assert.Equal(players.Ticks[0].Weight, row.GetProperty("idleWeight").GetSingle());
            Assert.Equal(79, row.GetProperty("pose").GetArrayLength());
            for (var bone = 0; bone < 79; bone++)
            {
                var expected = row.GetProperty("pose")[bone]; var actual = overlay.Pose[bone];
                double[] Values(string name) => expected.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var p = Values("position"); var q = Values("rotation"); var s = Values("scale");
                var dp = Math.Sqrt(Math.Pow(actual.Position.X - p[0], 2) + Math.Pow(actual.Position.Y - p[1], 2) + Math.Pow(actual.Position.Z - p[2], 2));
                var sign = actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3] < 0 ? -1 : 1;
                var dq = new[] { Math.Abs(actual.Rotation.X-sign*q[0]), Math.Abs(actual.Rotation.Y-sign*q[1]), Math.Abs(actual.Rotation.Z-sign*q[2]), Math.Abs(actual.Rotation.W-sign*q[3]) }.Max();
                var ds = new[] { Math.Abs(actual.Scale.X-s[0]), Math.Abs(actual.Scale.Y-s[1]), Math.Abs(actual.Scale.Z-s[2]) }.Max();
                maxP = Math.Max(maxP, dp); maxQ = Math.Max(maxQ, dq); maxS = Math.Max(maxS, ds);
                // Tight enough to catch replacing UE's float 1-(1-alpha) with alpha.
                Assert.True(dp <= 1e-10 && dq <= 1e-12 && ds <= 1e-12, $"{hz}Hz frame={frame} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
            }
            var nativeCurves = row.GetProperty("curves"); var present = 0;
            for (var c = 0; c < profile.CurveNames.Length; c++)
            {
                var curve = overlay.Curves[c];
                Assert.Equal(curve.Present, nativeCurves.TryGetProperty(profile.CurveNames[c], out var expected));
                if (!curve.Present) continue;
                present++; var dc = Math.Abs(curve.Value - expected.GetSingle()); maxC = Math.Max(maxC, dc);
                Assert.True(dc <= 2e-6, $"{hz}Hz frame={frame} curve={profile.CurveNames[c]} difference={dc:R}");
            }
            Assert.Equal(present, nativeCurves.EnumerateObject().Count());
            players.ValidateCommit(frame); overlay.ValidateCommit(frame); players.Commit(frame); overlay.Commit(frame++);
        }
        Assert.Equal(hz * 3, frame);
        output.WriteLine($"{hz}Hz frames={frame} bones={frame * 79} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxCurve={maxC:R} maxPrediction={maxA:R} maxTime={maxT:R}");
    }
}
