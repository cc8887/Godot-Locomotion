using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStandingHostNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OriginalStandingGraphAndParentMatchContinuousHost(int hz)
    {
        var profile = AlsRefactoredStandingHostTests.Data.Value; var host = profile.CreateRuntime(19, 1);
        using var document = JsonDocument.Parse(MantlingHostFixture.Read("refactored_standing_host_trace"));
        var root = document.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(host.BoneNames.ToArray(), root.GetProperty("names").EnumerateArray().Select(n => n.GetString()).ToArray());
        foreach (var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))), hash.Value.GetString()!.ToUpperInvariant());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz.ToString());
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init; var frame = 0; var bones = 0; var quick = 0;
        double maxParent = 0, maxPose = 0, maxCurve = 0, maxClock = 0, maxWeight = 0;
        var states = new HashSet<int>();
        var numericFailures = new Dictionary<string, (int Count, string First, double Maximum)>();
        var upstreamDifferences = new Dictionary<string, (string First, double Maximum)>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var r = row.GetProperty("input"); var delta = r.GetProperty("delta").GetSingle(); var moving = r.GetProperty("moving").GetBoolean();
            var side = r.GetProperty("side").GetSingle(); var pending = r.GetProperty("pending").GetBoolean();
            var speed = r.GetProperty("speed").GetSingle();
            var movement = new AlsRefactoredMovementInput(speed > 0 ? new(170, 100 * side, 0) : AlsDoubleVector.Zero,
                new(200, 100, 0), AlsQuaternion.Identity, speed, 1, 55 * side, 0, 1000, 800, "Als.Gait.Running", false, pending, delta, 1, 0, 0, .2f);
            var rest = new AlsRefactoredRestInput(delta, r.GetProperty("yaw").GetSingle(), 0, moving, false,
                r.GetProperty("aiming").GetBoolean() ? AlsRefactoredRestRotation.Aiming : AlsRefactoredRestRotation.ViewDirection,
                AlsRefactoredRestStance.Standing, true, pending, 1, r.GetProperty("dynamic").GetBoolean() ? 1 : 0, 0,
                new(20, 0, 0), default, default, default);
            var input = new AlsRefactoredStandingHostInput(movement, rest, new(1, 1, 1, 0), new(false, false, moving, 90, 0, 0),
                r.GetProperty("foot").GetSingle(), r.GetProperty("movingSmooth").GetBoolean(), r.GetProperty("pivot").GetBoolean());
            var id = new AlsFrameIdentity(frame, 19, 1); var context = new AlsPoseUpdateContext(id, 1, delta).WithUpdateCounter(counter);
            host.Prepare(context, input, init, frame == 0);
            Assert.True(host.State == row.GetProperty("state").GetInt32(), $"{hz}/{frame} state {host.State} vs {row.GetProperty("state")}"); states.Add(host.State);
            Parent(row.GetProperty("parent"));
            if (host.MovementPlayers is { } players)
            {
                var expectedPlayers = row.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("node").GetInt32());
                foreach (var player in players.Players)
                {
                    var node = profile.Details.Players.Players[player.PlayerId].PropertyIndex;
                    Compare(player.Time, expectedPlayers[node].GetProperty("time").GetDouble(), ref maxClock, 2e-6, "playerTime" + node);
                    var tick = players.Ticks.ToArray().Single(t => t.PlayerId == player.PlayerId);
                    Compare(tick.Weight, expectedPlayers[node].GetProperty("weight").GetDouble(), ref maxWeight, 2e-6, "playerWeight" + node);
                }
            }
            if (r.GetProperty("evaluate").GetBoolean())
            {
                host.Evaluate(AlsPrecisePose.Identity);
                var expected = row.GetProperty("pose"); Assert.Equal(790, expected.GetArrayLength());
                for (var b = 0; b < 79; b++)
                {
                    var p = host.Pose[b]; double[] actual = [p.Position.X, p.Position.Y, p.Position.Z, p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W, p.Scale.X, p.Scale.Y, p.Scale.Z];
                    var dot = Enumerable.Range(3, 4).Sum(c => actual[c] * expected[b * 10 + c].GetDouble());
                    for (var c = 0; c < 10; c++) Compare(actual[c] * (c is >= 3 and <= 6 && dot < 0 ? -1 : 1), expected[b * 10 + c].GetDouble(),
                        ref maxPose, c < 3 ? 2e-5 : 2e-6, $"bone{b}/{c}");
                }
                var curves = row.GetProperty("curves").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);
                Assert.Equal(curves.Count, host.Curves.ToArray().Count(c => c.Present));
                for (var c = 0; c < host.Curves.Length; c++)
                {
                    Assert.Equal(curves.ContainsKey(host.CurveNames[c]), host.Curves[c].Present);
                    if (host.Curves[c].Present) Compare(host.Curves[c].Value, curves[host.CurveNames[c]], ref maxCurve, 2e-6, host.CurveNames[c]);
                }
                bones += 79;
            }
            host.PostUpdateActions(); Parent(row.GetProperty("postParent"));
            Assert.Equal(row.GetProperty("quick").GetInt32(), host.QuickStopDispatchCount); quick += host.QuickStopDispatchCount;
            var montages = row.GetProperty("montages"); Assert.Equal(montages.GetArrayLength(), host.CandidateMontages.Length);
            for (var i = 0; i < host.CandidateMontages.Length; i++)
            {
                var a = host.CandidateMontages[i]; var e = montages[i];
                var path = a.AnimationId < 12 ? profile.Montages.SourcePath(a.AnimationId) : a.AnimationId < 14 ? profile.Actions.SourcePath(a.AnimationId) : profile.QuickStop.SourcePath(a.AnimationId);
                Assert.Equal(e.GetProperty("source").GetString(), path);
                Assert.Equal(e.GetProperty("slot").GetString(), a.Slot == AlsMontageSlot.Transition ? "Transition" : "TurnInPlaceStanding");
                Assert.Equal(e.GetProperty("playing").GetBoolean(), a.Playing); Assert.Equal(e.GetProperty("rate").GetSingle(), a.PlayRate);
                Compare(a.Position, e.GetProperty("time").GetDouble(), ref maxClock, 2e-6, "montageTime");
                Compare(a.Blend.CurrentWeight, e.GetProperty("weight").GetDouble(), ref maxWeight, 2e-6, "montageWeight");
            }
            host.Commit(id); counter = counter.Next((ulong)++frame);

            void Parent(JsonElement expected)
            {
                var s = host.RestState; Assert.Equal(expected.GetProperty("left").GetBoolean(), s.Rotate.Left); Assert.Equal(expected.GetProperty("right").GetBoolean(), s.Rotate.Right);
                Compare(s.Rotate.PlayRate, expected.GetProperty("rate").GetDouble(), ref maxParent, 2e-6, "rotateRate");
                Compare(s.TurnDelay, expected.GetProperty("turnDelay").GetDouble(), ref maxParent, 2e-6, "turnDelay");
                Compare(s.TurnPlayRate, expected.GetProperty("turnRate").GetDouble(), ref maxParent, 2e-6, "turnRate");
                Assert.Equal(expected.GetProperty("dynamicDelay").GetInt32(), s.DynamicFrameDelay);
                var m = host.MovementState; var p = expected.GetProperty("movement");
                double[] actual = [m.VelocityBlend.X, m.VelocityBlend.Y, m.VelocityBlend.Z, m.VelocityBlend.W, m.Lean.X, m.Lean.Y,
                    m.YawOffsets.X, m.YawOffsets.Y, m.YawOffsets.Z, m.YawOffsets.W, m.StandingStride, m.WalkRun, m.StandingRate,
                    m.SprintBlock, m.SprintTime, m.SprintAcceleration];
                for (var i = 0; i < actual.Length; i++) Compare(actual[i], p[i].GetDouble(), ref maxParent, 2e-6, "movement" + i);
            }
            void Compare(double a, double e, ref double maximum, double tolerance, string field)
            {
                var error = Math.Abs(a - e); maximum = Math.Max(maximum, error);
                var message = $"{hz}/{frame}/{field}: {a:R} vs {e:R}, error={error:R}";
                if (error > 0 && (field.StartsWith("playerTime", StringComparison.Ordinal) || field.StartsWith("movement", StringComparison.Ordinal)))
                {
                    var previous = upstreamDifferences.GetValueOrDefault(field);
                    upstreamDifferences[field] = (previous.First ?? message, Math.Max(previous.Maximum, error));
                }
                if (!(error <= tolerance))
                {
                    var category = field.StartsWith("bone", StringComparison.Ordinal) ? "pose" : field;
                    var previous = numericFailures.GetValueOrDefault(category);
                    numericFailures[category] = (previous.Count + 1, previous.First ?? message, Math.Max(previous.Maximum, error));
                }
            }
        }
        Assert.Equal(hz * 11, frame); Assert.Equal(5, states.Count); Assert.True(quick > 0);
        output.WriteLine($"hz={hz} frames={frame} bones={bones} quick={quick} parent={maxParent:R} pose={maxPose:R} curve={maxCurve:R} clock={maxClock:R} weight={maxWeight:R}");
        foreach (var difference in upstreamDifferences)
            output.WriteLine($"upstream {difference.Key}: first={difference.Value.First}; max={difference.Value.Maximum:R}");
        Assert.True(numericFailures.Count == 0, string.Join(Environment.NewLine, numericFailures.Select(f =>
            $"{f.Key}: {f.Value.Count} components exceeded budget; first: {f.Value.First}; max={f.Value.Maximum:R}")));
    }
}
