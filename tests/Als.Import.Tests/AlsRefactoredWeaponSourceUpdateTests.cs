using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponSourceUpdateTests(ITestOutputHelper output)
{
    [Fact]
    public void ForeignOwnerAndInvalidInputLeaveTheLastCommittedUpdateRecoverable()
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profile = new AlsRefactoredWeaponSourceProfile(catalog,
            new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, AlsRefactoredWeaponKind.Rifle)));
        var machine = profile.Machine.CreateRuntime(); var update = profile.CreateRuntime(10);
        var pose = new AlsRefactoredWeaponPoseInput(0, 1, 1, 1, 0, 0, 0, .3f, new(1, 0, 0, 0));
        var rule = new AlsRefactoredWeaponRuleInput("", "", "", false, false);
        machine.Prepare(0, rule, .1f); update.Prepare(0, machine, pose, .1f);
        var original = update.SourceInputs.ToArray(); Assert.All(original, p => Assert.True(p.PlayerId >= 10));
        Assert.Throws<ArgumentException>(() => update.Prepare(0, machine, pose, .1f));
        Assert.Equal(original, update.SourceInputs.ToArray()); update.Commit(0); machine.Commit(0);
        machine.Prepare(1, rule, .1f);
        Assert.Throws<ArgumentException>(() => update.Prepare(1, machine, pose with { SprintAcceleration = float.NaN }, .1f));
        Assert.Throws<ArgumentException>(() => update.Prepare(2, machine, pose, .1f));
        var foreign = new AlsRefactoredWeaponMachineProfile(catalog, profile.Machine.Resources).CreateRuntime(); foreign.Prepare(1, rule, .1f);
        Assert.Throws<ArgumentException>(() => update.Prepare(1, foreign, pose, .1f));
        var sameProfileOwner = profile.Machine.CreateRuntime(); sameProfileOwner.Prepare(1, rule, .1f);
        Assert.Throws<ArgumentException>(() => update.Prepare(1, sameProfileOwner, pose, .1f));
        update.Prepare(1, machine, pose, .1f); var candidate = update.SourceInputs.ToArray();
        update.Cancel(); update.Prepare(1, machine, pose, .1f); Assert.Equal(candidate, update.SourceInputs.ToArray());
        Assert.Throws<ArgumentException>(() => update.Commit(2)); update.Commit(1); machine.Commit(1);
    }

    public static IEnumerable<object[]> Cases() => AlsRefactoredWeaponNativeTests.Cases();
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalStatePlayerWeightsAndClocksMatchNative(AlsRefactoredWeaponKind kind, int hz)
        => Compare(kind, hz, false);

    [Theory]
    [MemberData(nameof(Cases))]
    public void MovingStatePlayerWeightsAndClocksMatchNative(AlsRefactoredWeaponKind kind, int hz)
        => Compare(kind, hz, true);

    private void Compare(AlsRefactoredWeaponKind kind, int hz, bool moving)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profile = new AlsRefactoredWeaponSourceProfile(catalog,
            new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind)));
        var machine = profile.Machine.CreateRuntime(); var update = profile.CreateRuntime(0);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, new(MantlingHostFixture.Read("refactored_sync_inputs"), catalog),
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), profile.Players.Bind(0, new Dictionary<string, int> { ["Secondary Motion"] = 0, ["Movement"] = 1 }));
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read((moving ? "refactored_weapon_source_trace_" : "refactored_weapon_trace_") + kind));
        Assert.Equal(AlsRefactoredWeaponMachineResources.Blueprint(kind), doc.RootElement.GetProperty("source").GetString());
        foreach (var hash in doc.RootElement.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))), hash.Value.GetString()!.ToUpperInvariant());
        var trace = doc.RootElement.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz + "hz");
        var frame = 0; var ticks = 0; var resets = 0; var maxTime = 0f; var maxWeight = 0f;
        var visited = new HashSet<int>(); var resetPlayers = new HashSet<int>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var delta = input.GetProperty("delta").GetSingle(); var pose = input.GetProperty("poseState");
            var velocity = moving ? input.GetProperty("groundedState").GetProperty("VelocityBlend") : default;
            var value = new AlsRefactoredWeaponPoseInput(pose.GetProperty("GaitWalkingAmount").GetSingle(), moving ? pose.GetProperty("GaitRunningAmount").GetSingle() : 0,
                pose.GetProperty("GaitSprintingAmount").GetSingle(), pose.GetProperty("StandingAmount").GetSingle(), pose.GetProperty("CrouchingAmount").GetSingle(),
                pose.GetProperty("InAirAmount").GetSingle(), input.GetProperty("inAirState").GetProperty("GroundPredictionAmount").GetSingle(),
                moving ? input.GetProperty("standingState").GetProperty("SprintAccelerationAmount").GetSingle() : 0,
                moving ? new(velocity.GetProperty("ForwardAmount").GetSingle(), velocity.GetProperty("BackwardAmount").GetSingle(), velocity.GetProperty("LeftAmount").GetSingle(), velocity.GetProperty("RightAmount").GetSingle()) : Vector4.Zero);
            machine.Prepare(frame, new(input.GetProperty("rotationMode").GetString()!, input.GetProperty("gait").GetString()!,
                input.GetProperty("locomotionMode").GetString()!, input.GetProperty("moving").GetBoolean(), input.GetProperty("allowed").GetBoolean()), delta,
                reinitialize: input.GetProperty("reset").GetBoolean());
            update.Prepare(frame, machine, value, delta); players.Prepare(frame, update.SourceInputs, delta);
            var native = row.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("propertyIndex").GetInt32());
            foreach (var tick in update.SourceInputs)
            {
                ticks++; visited.Add(tick.PlayerId); if (tick.Reinitialize) { resets++; resetPlayers.Add(tick.PlayerId); }
                var expected = native[profile.Players.Players[tick.PlayerId].PropertyIndex];
                maxWeight = MathF.Max(maxWeight, MathF.Abs(tick.Weight - expected.GetProperty("weight").GetSingle()));
                var time = players.Players.ToArray().Single(p => p.PlayerId == tick.PlayerId).Time;
                maxTime = MathF.Max(maxTime, MathF.Abs(time - expected.GetProperty("time").GetSingle()));
                Assert.True(maxWeight <= 2e-6f && maxTime <= 2e-6f, $"{kind}/{hz}/{frame} player={tick.PlayerId} weight={maxWeight:R} time={maxTime:R} actual={time:R} expected={expected.GetProperty("time")}");
            }
            if (frame % 17 == 0)
            {
                var inputs = update.SourceInputs.ToArray(); var clocks = players.Players.ToArray();
                players.Cancel(); update.Cancel(); update.Prepare(frame, machine, value, delta); players.Prepare(frame, update.SourceInputs, delta);
                Assert.Equal(inputs, update.SourceInputs.ToArray()); Assert.Equal(clocks, players.Players.ToArray());
            }
            machine.ValidateCommit(frame); update.ValidateCommit(frame); players.ValidateCommit(frame);
            machine.Commit(frame); update.Commit(frame); players.Commit(frame++);
        }
        Assert.True(ticks > frame && resets > 3);
        if (moving) { Assert.Equal(profile.Players.Players.Length, visited.Count); Assert.Equal(visited.Order(), resetPlayers.Order()); }
        output.WriteLine($"{kind}/{hz} moving={moving}: frames={frame} sourceTicks={ticks} resets={resets} visited={visited.Count} maxTime={maxTime:R} maxWeight={maxWeight:R}");
    }
}
