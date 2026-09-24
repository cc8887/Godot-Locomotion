using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponPlayerTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    private static AlsRefactoredWeaponMachineResources Resources(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponKind kind) =>
        new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind);

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)] [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)] [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void OriginalStatePlayersKeepIndependentIdentitiesAndExplicitHostGroups(AlsRefactoredWeaponKind kind)
    {
        var catalog = Catalog(); var profile = new AlsRefactoredWeaponPlayers(catalog, Resources(catalog, kind));
        var bound = profile.Bind(17, new Dictionary<string, int> { ["Secondary Motion"] = 42, ["Movement"] = 73 });
        Assert.Equal(kind == AlsRefactoredWeaponKind.Rifle ? 6 : 3, bound.Length);
        Assert.Equal(Enumerable.Range(17, bound.Length), bound.Select(d => d.PlayerId));
        var idle = profile.Players.ToArray().Where(p => p.Group == "Secondary Motion").ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, idle.Select(p => p.State));
        Assert.Single(idle.Select(p => p.Source).Distinct()); Assert.Equal(3, idle.Select(p => p.CompiledNode).Distinct().Count());
        Assert.All(idle, p => { Assert.Equal(1, p.PlayRate); Assert.Equal(AlsAssetSyncRole.CanBeLeader, p.Role); });
        Assert.All(profile.Players.ToArray().Where(p => p.Group == "Movement"), p =>
        { Assert.Equal(0, p.PlayRate); Assert.Equal(0, p.State); Assert.Equal(AlsAssetSyncRole.AlwaysFollower, p.Role); });
        using var native = JsonDocument.Parse(MantlingHostFixture.Read("refactored_weapon_trace_" + kind));
        Assert.Equal(profile.Players.ToArray().Select(p => p.PropertyIndex).Order(), native.RootElement.GetProperty("traces")[0].GetProperty("frames")[0]
            .GetProperty("players").EnumerateArray().Select(p => p.GetProperty("propertyIndex").GetInt32()).Order());
        for (var i = 0; i < bound.Length; i++)
        { Assert.Equal(profile.Players[i].Role, bound[i].Role); Assert.Equal(profile.Players[i].Group == "Movement" ? 73 : 42, bound[i].GroupId); }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ActualZeroRateRifleArmsFollowLowWeightMovementAndRetryKeepsClocks(int hz)
    {
        var catalog = Catalog(); var sync = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var profile = new AlsRefactoredWeaponPlayers(catalog, Resources(catalog, AlsRefactoredWeaponKind.Rifle));
        var definitions = profile.Bind(0, new Dictionary<string, int> { ["Movement"] = 0, ["Secondary Motion"] = 1 }).ToList();
        var movement = definitions.Count;
        definitions.Add(new(movement, "/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward", 0));
        var runtime = new AlsRefactoredSourcePlayerRuntime(catalog, sync, new Dictionary<string, AlsRefactoredTriangulationProfile>(), definitions);
        var arms = profile.Players.ToArray().Select((p, i) => (p, i)).Where(p => p.p.Role == AlsAssetSyncRole.AlwaysFollower).ToArray();
        // The old adapter attempts zero-rate arms as marker leaders first. A
        // later movement source can win, but those attempts never become followers.
        var previousAdapter = new AlsRefactoredSourcePlayerRuntime(catalog, sync, new Dictionary<string, AlsRefactoredTriangulationProfile>(),
            definitions.Select(d => d with { Role = AlsAssetSyncRole.CanBeLeader }));
        var firstInput = arms.Select(p => new AlsRefactoredSourcePlayerInput(p.i, default, p.p.PlayRate, 1)).Append(new(movement, default, 1, .001f)).ToArray();
        previousAdapter.Prepare(0, firstInput, 1f / hz);
        Assert.Equal(movement, previousAdapter.Groups.ToArray().Single(g => g.Group.GroupId == 0).Group.LeaderPlayerId);
        Assert.All(previousAdapter.TickContexts.ToArray().Where(c => c.PlayerId != movement), c => Assert.True(c.Leader));
        Assert.All(previousAdapter.Players.ToArray().Where(p => p.PlayerId != movement), p => Assert.Equal(0, p.Time));
        previousAdapter.Cancel();
        var advanced = 0;
        for (var frame = 0; frame < hz * 2; frame++)
        {
            var input = arms.Select(p => new AlsRefactoredSourcePlayerInput(p.i, default, p.p.PlayRate, 1)).Append(new(movement, default, 1, .001f)).ToArray();
            runtime.Prepare(frame, input, 1f / hz);
            var group = runtime.Groups.ToArray().Single(g => g.Group.GroupId == 0).Group;
            Assert.Equal(movement, group.LeaderPlayerId); Assert.Equal(.001f, group.LeaderScore);
            foreach (var arm in arms)
            {
                Assert.Equal(AlsAssetSyncRole.AlwaysFollower, runtime.Ticks.ToArray().Single(t => t.PlayerId == arm.i).Role);
                Assert.False(runtime.TickContexts.ToArray().Single(t => t.PlayerId == arm.i).Leader);
                if (runtime.Players.ToArray().Single(t => t.PlayerId == arm.i).Time > 0) advanced++;
                runtime.Evaluate(frame, arm.i); Assert.Equal(79, runtime.Pose(arm.i).Length);
            }
            if (frame % 13 == 0)
            {
                var expected = runtime.Players.ToArray(); var groups = runtime.Groups.ToArray();
                runtime.Cancel(); runtime.Prepare(frame, input, 1f / hz);
                Assert.Equal(expected, runtime.Players.ToArray()); Assert.Equal(groups, runtime.Groups.ToArray());
            }
            runtime.Commit(frame);
        }
        Assert.True(advanced > hz);
    }

    [Fact]
    public void UnsupportedNativePoliciesAndHostGroupAliasesAreRejected()
    {
        var catalog = Catalog(); var resources = Resources(catalog, AlsRefactoredWeaponKind.Rifle);
        var profile = new AlsRefactoredWeaponPlayers(catalog, resources);
        Assert.Throws<ArgumentException>(() => profile.Bind(0, new Dictionary<string, int> { ["Movement"] = 1, ["Secondary Motion"] = 1 }));
        Assert.Throws<ArgumentException>(() => profile.Bind(0, new Dictionary<string, int> { ["Movement"] = 1 }));
        var payload = catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(resources.Kind));
        foreach (var field in new[] { "groupRole", "playRate", "bLoopAnimation", "startPosition" })
        {
            var changed = JsonNode.Parse(payload.GetRawText())!;
            var node = changed["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == 38)!["runtime"]!;
            if (field == "groupRole") node[field] = "CanBeLeader";
            else if (field == "bLoopAnimation") node[field] = false;
            else node[field] = 2;
            using var doc = JsonDocument.Parse(changed.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredWeaponPlayers.Compile(doc.RootElement, resources));
        }
        var sync = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var bound = profile.Bind(0, new Dictionary<string, int> { ["Movement"] = 0, ["Secondary Motion"] = 1 });
        bound[0] = bound[0] with { Role = (AlsAssetSyncRole)255 };
        Assert.Throws<ArgumentException>(() => new AlsRefactoredSourcePlayerRuntime(catalog, sync, new Dictionary<string, AlsRefactoredTriangulationProfile>(), bound));
    }
}
