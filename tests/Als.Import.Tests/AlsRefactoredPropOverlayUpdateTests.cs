using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPropOverlayUpdateTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    [Theory]
    [InlineData(AlsRefactoredPropOverlayKind.Binoculars)]
    [InlineData(AlsRefactoredPropOverlayKind.Torch)]
    public void ActualNestedUpdatePoliciesRejectChangedTimesCurvesAndBindings(AlsRefactoredPropOverlayKind kind)
    {
        var catalog = Catalog(); var profile = new AlsRefactoredPropOverlayUpdateProfile(catalog, kind);
        Assert.Equal(kind, profile.Kind);
        var payload = catalog.Read(AlsRefactoredPropOverlayUpdateProfile.Blueprint(kind));
        foreach (var mutation in new[] { "blend", "time", "binding", "idle" })
        {
            var root = JsonNode.Parse(payload.GetRawText())!;
            var nodes = root["compiled"]!["nodes"]!.AsArray();
            var aim = nodes.Single(n => n!["runtime"]?["blendType"]?.GetValue<string>() == "HermiteCubic")!;
            if (mutation == "blend") aim["runtime"]!["blendType"] = "Linear";
            if (mutation == "time") aim["runtime"]!["blendTime"]![0] = .2f;
            if (mutation == "binding") root["nativeText"] = root["nativeText"]!.GetValue<string>().Replace("\"GetParent\",\"RotationMode\"", "\"GetParent\",\"LocomotionAction\"", StringComparison.Ordinal);
            if (mutation == "idle") nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_SequencePlayer")!["runtime"]!["playRate"] = 2;
            using var changed = JsonDocument.Parse(root.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredPropOverlayUpdateProfile.Validate(changed.RootElement, kind));
        }
    }
    public static IEnumerable<object[]> Cases()
    { foreach (var kind in Enum.GetValues<AlsRefactoredPropOverlayKind>()) foreach (var hz in new[] { 30, 60, 120 }) yield return [kind, hz]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeNestedActionAimAndIdleUpdatesMatchIncludingHiddenReset(AlsRefactoredPropOverlayKind kind, int hz)
    {
        var catalog = Catalog(); var profile = new AlsRefactoredPropOverlayUpdateProfile(catalog, kind); var runtime = profile.CreateRuntime(0);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, new(MantlingHostFixture.Read("refactored_sync_inputs"), catalog),
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), [profile.PlayerDefinition(0, 0)]);
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_prop_overlay_" + kind));
        var root = doc.RootElement;
        Assert.Equal(AlsRefactoredPropOverlayUpdateProfile.Blueprint(kind), root.GetProperty("source").GetString());
        foreach (var name in new[] { "refactored_animation_sources", "refactored_sync_inputs" })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))), root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == $"{hz}hz");
        int frame = 0, hidden = 0, zero = 0, mixed = 0; float maxAction = 0, maxAim = 0, maxTime = 0;
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var delta = input.GetProperty("delta").GetSingle(); var reset = input.GetProperty("reset").GetBoolean();
            var action = AlsOverlayActionBlend.Resolve(input.GetProperty("action").GetString()!);
            var aiming = input.GetProperty("rotationMode").GetString() == "Als.RotationMode.Aiming";
            var before = runtime.CommittedState;
            runtime.Prepare(frame, action, aiming, delta, reset); players.Prepare(frame, runtime.SourceInputs, delta);
            var candidate = runtime.Candidate; var inputs = runtime.SourceInputs.ToArray(); var time = players.Players.ToArray();
            runtime.Cancel(); players.Cancel(); Assert.Equal(before, runtime.CommittedState);
            runtime.Prepare(frame, action, aiming, delta, reset); players.Prepare(frame, runtime.SourceInputs, delta);
            Assert.Equal(candidate, runtime.Candidate); Assert.Equal(inputs, runtime.SourceInputs.ToArray()); Assert.Equal(time, players.Players.ToArray());
            var weights = candidate.State.Actions.Weights; var aim = AlsPropOverlayUpdate.AimWeights(candidate.State);
            for (var i = 0; i < 4; i++) maxAction = MathF.Max(maxAction, MathF.Abs(weights[i] - row.GetProperty("actionWeights")[i].GetSingle()));
            for (var i = 0; i < 2; i++) maxAim = MathF.Max(maxAim, MathF.Abs(aim[i] - row.GetProperty("aimWeights")[i].GetSingle()));
            if (aim.X > 1e-5f && aim.Y > 1e-5f) mixed++;
            if (candidate.UpdateIdle)
            {
                Assert.Single(inputs); Assert.Equal(candidate.IdleWeight, row.GetProperty("idleWeight").GetSingle());
                maxTime = MathF.Max(maxTime, MathF.Abs(players.Players[0].Time - row.GetProperty("idleTime").GetSingle()));
                if (candidate.IdleWeight == 0) zero++;
            }
            else { hidden++; Assert.Empty(inputs); }
            Assert.True(maxAction <= 2e-6 && maxAim <= 2e-6 && maxTime <= 2e-6, $"{kind}/{hz}/{frame} action={maxAction:R} aim={maxAim:R} time={maxTime:R}");
            players.ValidateCommit(frame); runtime.ValidateCommit(frame); players.Commit(frame); runtime.Commit(frame++);
        }
        Assert.Equal(hz * 4, frame); Assert.True(hidden > 0 && zero > 0 && mixed > 0);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(frame, AlsOverlayAction.Default, true, float.NaN));
        runtime.Prepare(frame, AlsOverlayAction.Default, true, .01f); runtime.Cancel();
        output.WriteLine($"{kind}/{hz} frames={frame} hidden={hidden} zero={zero} mixed={mixed} maxAction={maxAction:R} maxAim={maxAim:R} maxTime={maxTime:R}");
    }
}
