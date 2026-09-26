using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPivotNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30, false)] [InlineData(60, false)] [InlineData(120, false)]
    [InlineData(30, true)] [InlineData(60, true)] [InlineData(120, true)]
    public void GeneratedDirectionNotifyAndFollowingPivotMatchOriginalGraph(int hz, bool shared)
    {
        var actions = shared ? AlsRefactoredCharacterActionTests.Data.Value.Profile.CreateRuntime(19, 1) : null;
        var profile = shared ? AlsRefactoredCharacterActionTests.Data.Value.Profile.Standing : AlsRefactoredStandingHostTests.Data.Value;
        var host = actions?.Standing ?? profile.CreateRuntime(19, 1);
        using var document = JsonDocument.Parse(MantlingHostFixture.Read("refactored_pivot_trace"));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        foreach (var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))), hash.Value.GetString()!.ToUpperInvariant());
        Assert.Equal(host.BoneNames.ToArray(), root.GetProperty("names").EnumerateArray().Select(n => n.GetString()).ToArray());
        var counter = new AlsGraphTraversalCounter(0, 0); var f = 0; var notifyCount = 0; var bones = 0;
        var states = new HashSet<int>(); var failures = new List<string>();
        double maxPosition = 0, maxCurve = 0, maxClock = 0, maxParent = 0;
        foreach (var row in root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz.ToString()).GetProperty("frames").EnumerateArray())
        {
            var r = row.GetProperty("input"); float N(string name) => r.GetProperty(name).GetSingle();
            var input = AlsRefactoredStandingHostTests.Input(0, hz);
            input = input with { Movement = input.Movement with { Velocity = new(N("velocityX"), N("velocityY"), 0),
                Speed = N("speed"), VelocityYaw = N("heading"), PendingUpdate = f == 0 },
                Rest = input.Rest with { Moving = true, PendingUpdate = f == 0 }, MovingSmooth = true, FootPlanted = N("foot") };
            Assert.False(input.ActivatePivot);
            var id = new AlsFrameIdentity(f, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / hz).WithUpdateCounter(counter);
            void Prepare()
            {
                if (actions is not null) { actions.Begin(id, context.Delta); actions.Transition.Prepare(context, f == 0); }
                host.Prepare(context, input, new(0, 0), f == 0);
            }
            void Post() { if (actions is null) host.PostUpdateActions(); else actions.PostUpdateActions(); }
            Prepare();
            Assert.Equal(row.GetProperty("state").GetInt32(), host.State);
            Assert.Equal(row.GetProperty("detailsState").GetInt32(), host.MovementDetailsState);
            Assert.Equal(row.GetProperty("directionState").GetInt32(), host.DirectionUpdate!.Value.State.CurrentState);
            states.Add(host.MovementDetailsState); Parent(row.GetProperty("parent"));
            var players = host.MovementPlayers!;
            var nativePlayers = row.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("node").GetInt32());
            foreach (var p in players.Players)
            {
                var node = profile.Details.Players.Players[p.PlayerId].PropertyIndex;
                Compare(p.Time, nativePlayers[node].GetProperty("time").GetDouble(), 2e-6, "clock" + node, ref maxClock);
                var tick = players.Ticks.ToArray().Single(t => t.PlayerId == p.PlayerId);
                Compare(tick.Weight, nativePlayers[node].GetProperty("weight").GetDouble(), 2e-6, "weight" + node, ref maxClock);
            }
            if (r.GetProperty("evaluate").GetBoolean())
            {
                host.Evaluate(AlsPrecisePose.Identity);
                var native = row.GetProperty("pose"); Assert.Equal(host.Pose.Length * 10, native.GetArrayLength());
                for (var b = 0; b < host.Pose.Length; b++)
                {
                    var p = host.Pose[b]; double[] values = [p.Position.X, p.Position.Y, p.Position.Z,
                        p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W, p.Scale.X, p.Scale.Y, p.Scale.Z];
                    var sign = Enumerable.Range(3, 4).Sum(c => values[c] * native[b * 10 + c].GetDouble()) < 0 ? -1 : 1;
                    for (var c = 0; c < 10; c++) Compare(values[c] * (c is >= 3 and <= 6 ? sign : 1), native[b * 10 + c].GetDouble(),
                        c < 3 ? 2e-5 : 2e-6, $"bone{b}/{c}", ref maxPosition);
                }
                var curves = row.GetProperty("curves"); Assert.Equal(curves.EnumerateObject().Count(), host.Curves.ToArray().Count(c => c.Present));
                for (var c = 0; c < host.Curves.Length; c++)
                {
                    Assert.Equal(curves.TryGetProperty(host.CurveNames[c], out var value), host.Curves[c].Present);
                    if (host.Curves[c].Present) Compare(host.Curves[c].Value, value.GetDouble(), 2e-6, host.CurveNames[c], ref maxCurve);
                }
                bones += host.Pose.Length;
            }
            Post(); Parent(row.GetProperty("postParent"));
            var count = row.GetProperty("pivotNotifies").GetInt32();
            Assert.Equal(count, host.PivotDispatchCount); notifyCount += count;
            if (f % 17 == 0 || count > 0)
            {
                var pivot = host.PivotActive; var state = host.MovementDetailsState;
                host.Cancel(); Prepare();
                if (r.GetProperty("evaluate").GetBoolean()) host.Evaluate(AlsPrecisePose.Identity);
                Post();
                Assert.Equal(pivot, host.PivotActive); Assert.Equal(count, host.PivotDispatchCount); Assert.Equal(state, host.MovementDetailsState);
            }
            if (actions is null) host.Commit(id); else actions.Commit(id);
            counter = counter.Next((ulong)++f);

            void Parent(JsonElement native)
            {
                var m = host.MovementState; var values = native.GetProperty("movement");
                double[] actual = [m.VelocityBlend.X, m.VelocityBlend.Y, m.VelocityBlend.Z, m.VelocityBlend.W, m.Lean.X, m.Lean.Y,
                    m.YawOffsets.X, m.YawOffsets.Y, m.YawOffsets.Z, m.YawOffsets.W, m.StandingStride, m.WalkRun, m.StandingRate,
                    m.SprintBlock, m.SprintTime, m.SprintAcceleration];
                for (var i = 0; i < actual.Length; i++) Compare(actual[i], values[i].GetDouble(), 2e-6, "parent" + i, ref maxParent);
                Assert.Equal(values[16].GetInt32() == 1, host.PivotActive);
            }
            void Compare(double a, double e, double tolerance, string field, ref double maximum)
            {
                var error = Math.Abs(a - e); maximum = Math.Max(maximum, error);
                if (!(error <= tolerance) && failures.Count < 20) failures.Add($"{hz}/{f}/{field} {a:R} vs {e:R}, error {error:R}");
            }
        }
        output.WriteLine($"hz={hz} shared={shared} frames={f} bones={bones} notifies={notifyCount} pose={maxPosition:R} curve={maxCurve:R} clock={maxClock:R} parent={maxParent:R}");
        Assert.Equal(hz * 8, f); Assert.True(notifyCount >= 5); Assert.Contains(3, states); Assert.Contains(4, states);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
