using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSourceSyncScopeTests
{
    private const string Walk = "/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward";
    private const string Blend = "/ALS/ALS/Animations/Grounded/WalkRun/BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward";
    private static readonly Lazy<(AlsRefactoredAnimationCatalog Catalog, AlsRefactoredSyncBank Sync,
        IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles, AlsRefactoredNotifyBank Notify)> Frozen = new(() =>
    {
        var json = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var catalog = new AlsRefactoredAnimationCatalog(json, Read);
        var sync = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        return (catalog, sync, AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), json, Read),
            new(MantlingHostFixture.Read("refactored_notify_inputs"), catalog, sync));
    });
    private static AlsPoseUpdateContext Context(int frame, int hz = 60) =>
        new AlsPoseUpdateContext(new(frame, 7, 1), 1, 1f / hz).WithUpdateCounter(new(0, (ulong)frame));
    private static AlsRefactoredSourceSyncScope Create()
    {
        var r = Frozen.Value;
        return new(7, 1, r.Catalog, r.Sync, r.Triangles,
        [new(11, [new(0, Walk, 0, .1f), new(1, Walk, 1, .2f), new(2, Walk, -1, .3f)], new Dictionary<int, string>{{0,"Movement"},{1,"First Pivot"}}),
         new(22, [new(0, Walk, 9, .6f), new(1, Walk, 8, .7f), new(2, Walk, -1, .8f)], new Dictionary<int, string>{{9,"movement"},{8,"Jump"}})], r.Notify);
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void SameNameGroupsShareLeadershipAndNotifyPolicyAcrossOwnersWithAtomicRetry(int hz)
    {
        var scope = Create(); var r = Frozen.Value; var notifications = new AlsRefactoredSourceNotifyTick[6];
        var occurrences = new AlsAssetNotifyOccurrence[16]; var incoming = new AlsAssetNotifyReference[16];
        var queue = new AlsAssetNotifyReference[16]; var scratch = new AlsAssetNotifyReference[16];
        var flat = new AlsRefactoredSourcePlayerRuntime(r.Catalog, r.Sync, r.Triangles,
            [new(0, Walk, 0, .1f), new(1, Walk, 1, .2f), new(2, Walk, -1, .3f),
             new(3, Walk, 0, .6f), new(4, Walk, 2, .7f), new(5, Walk, -1, .8f)]);
        var followersWithEvents = 0; var admitted = 0; var leaders = new HashSet<int>();
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var root = Context(frame, hz); var hidden = frame >= hz && frame < hz + 3;
            void Prepare()
            {
                scope.Begin(root);
                var weight = frame < hz * 3 / 2 ? .6f : .4f;
                // Registration interleaves linked owners; no fixed owner concatenation.
                scope.Enqueue(22, new(2, default, 1, 1), root);
                scope.Enqueue(22, new(1, default, 1, 1), root);
                if (!hidden) scope.Enqueue(11, new(0, default, 1, 1 - weight), root.WithWeight(1 - weight));
                scope.Enqueue(22, new(0, default, 1, weight), root.WithWeight(weight));
                if (!hidden)
                {
                    scope.Enqueue(11, new(1, default, 1, 1), root);
                    scope.Enqueue(11, new(2, default, 1, 1), root.AsInactive(), true);
                }
                scope.Complete();
            }
            Prepare();
            var leaderWeight = frame < hz * 3 / 2 ? .6f : .4f;
            AlsRefactoredSourcePlayerInput[] flatInputs = hidden
                ? [new(5, default, 1, 1), new(4, default, 1, 1), new(3, default, 1, leaderWeight)]
                : [new(5, default, 1, 1), new(4, default, 1, 1), new(0, default, 1, 1 - leaderWeight),
                   new(3, default, 1, leaderWeight), new(1, default, 1, 1), new(2, default, 1, 1)];
            flat.Prepare(frame, flatInputs, root.Delta, groupOrder: [2, 0, 1]);
            // Marker conversion can give leader/follower clocks different float ULPs.
            // Compare each identity exactly to one flat shared native-algorithm bank.
            Assert.True(scope.Players.SequenceEqual(flat.Players)); Assert.True(scope.Samples.SequenceEqual(flat.Samples));
            Assert.True(scope.TickContexts.SequenceEqual(flat.TickContexts));
            Assert.Equal(new[] {"Jump", "Movement", "First Pivot"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g]));
            Assert.Equal(hidden ? 3 : 6, scope.Players.Length);
            var movement = scope.Groups.ToArray().Single(g => scope.GroupNames[g.Group.GroupId] == "Movement");
            var expectedLeader = hidden || frame < hz * 3 / 2 ? scope.PlayerId(22, 0) : scope.PlayerId(11, 0);
            Assert.Equal(expectedLeader, movement.Group.LeaderPlayerId); leaders.Add(expectedLeader);
            Assert.Equal(hidden ? 1 : 2, movement.PlayerCount);
            var ordered = scope.TickContexts.ToArray().OrderBy(t => t.Order).ToArray();
            Assert.Equal(scope.PlayerId(22, 1), ordered[0].PlayerId);
            Assert.Equal(expectedLeader, ordered[1].PlayerId);
            Assert.Equal(scope.PlayerId(22, 2), ordered[hidden ? ^1 : ^2].PlayerId);
            var count = scope.BuildNotifyTicks(notifications); Assert.Equal(scope.Players.Length, count);
            Assert.Equal(ordered.Select(t => t.PlayerId), notifications[..count].Select(t => t.Playback.PlayerId));
            foreach (var tick in notifications[..count])
            {
                var owner = scope.OwnerPlayer(tick.Playback.PlayerId);
                Assert.Equal(scope.PlayerId(owner.Owner, owner.LocalPlayer), tick.Playback.PlayerId);
                if (owner == new AlsRefactoredSourceOwnerPlayer(11, 2)) { Assert.False(tick.ActiveContext); Assert.True(tick.ScopeFiltered); }
                var found = scope.Extract(tick, occurrences);
                for (var i = 0; i < found; i++) incoming[i] = new(occurrences[i].DefinitionIndex, tick.Playback.SampleId,
                    tick.ContextTime, tick.ActiveContext, occurrences[i].ReachedEnd, ScopeFiltered: tick.ScopeFiltered);
                Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(r.Notify.Policies, [], incoming.AsSpan(0, found),
                    new(tick.Leader, false, 0, tick.Weight), AlsAssetNotifyQueueMode.Filtered,
                    AlsTimelineRuntime.InitialAssetNotifyRandomSeed, scratch, queue, out var filtered, out _, out _));
                if (!tick.Leader && found > 0) { followersWithEvents++; Assert.Equal(0, filtered); }
                admitted += filtered;
            }
            scope.Evaluate(22, 0); var pose = scope.Pose(22, 0).ToArray();
            var players = scope.Players.ToArray(); var samples = scope.Samples.ToArray(); var groups = scope.Groups.ToArray(); var notify = notifications[..count].ToArray();
            Assert.Throws<ArgumentException>(() => scope.ValidateCommit(new(frame, 999, 1)));
            scope.Discard(); Assert.Throws<InvalidOperationException>(() => scope.BuildNotifyTicks(notifications));
            Prepare(); Assert.Equal(players, scope.Players.ToArray()); Assert.Equal(samples, scope.Samples.ToArray()); Assert.Equal(groups, scope.Groups.ToArray());
            Assert.Equal(count, scope.BuildNotifyTicks(notifications)); Assert.Equal(notify, notifications[..count]);
            scope.Evaluate(22, 0); Assert.Equal(pose, scope.Pose(22, 0).ToArray()); scope.Commit(root.Identity); flat.Commit(frame);
        }
        Assert.Equal(2, leaders.Count); Assert.True(followersWithEvents > 0 && admitted > 0);
    }

    [Fact]
    public void GroupEncounterOrderAndOwnerResetAreCandidatesIncludingHiddenNodes()
    {
        var scope = Create(); var c = Context(0);
        scope.Begin(c); Assert.Throws<InvalidOperationException>(() => scope.Evaluate(11, 0));
        scope.Enqueue(22, new(1, default, 1, 1), c); scope.Complete();
        Assert.Equal(new[] {"Jump"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g])); scope.Commit(c.Identity);
        c = Context(1); scope.Begin(c); scope.Enqueue(11, new(1, default, 1, 1), c); scope.Complete();
        Assert.Equal(new[] {"First Pivot"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g])); scope.Discard();
        scope.Begin(c); scope.Enqueue(11, new(0, default, 1, 1), c); scope.Complete();
        Assert.Equal(new[] {"Movement"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g])); scope.Commit(c.Identity);
        c = Context(2); scope.Begin(c); scope.ReinitializeOwner(11); scope.Complete(); scope.Discard();
        scope.Begin(c); scope.Enqueue(11, new(0, default, 1, 1), c); scope.Complete(); Assert.Equal(1, scope.Players[0].Epoch);
        Assert.Equal(new[] {"Jump", "Movement"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g])); scope.Commit(c.Identity);
        c = Context(3); scope.Begin(c); scope.ReinitializeOwner(11); scope.Complete(); scope.Commit(c.Identity);
        c = Context(4); scope.Begin(c); scope.Enqueue(11, new(0, default, 1, 1), c); scope.Enqueue(22, new(1, default, 1, 1), c); scope.Complete();
        Assert.Equal(2, scope.Players.ToArray().Single(p => p.PlayerId == scope.PlayerId(11, 0)).Epoch);
        Assert.Equal(1, scope.Players.ToArray().Single(p => p.PlayerId == scope.PlayerId(22, 1)).Epoch); scope.Commit(c.Identity);
        c = Context(5); scope.Begin(c, true); scope.Enqueue(11, new(1, default, 1, 1), c); scope.Complete();
        Assert.Equal(new[] {"Jump", "Movement", "First Pivot"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g])); scope.Commit(c.Identity);
        c = Context(6); scope.Begin(c); scope.Enqueue(22, new(1, default, 1, 1), c); scope.Complete();
        Assert.Equal(new[] {"Movement", "Jump"}, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g]));
        Assert.Equal(2, scope.Players[0].Epoch); scope.Commit(c.Identity);
    }

    [Fact]
    public void SharedAndStandaloneBanksMatchForOneOwnerIncludingRetainedBlendSamplesAndGroupReordering()
    {
        var r = Frozen.Value;
        AlsRefactoredSourcePlayerDefinition[] definitions = [new(0, Blend, 0), new(1, Walk, 1), new(2, Walk, -1)];
        var scope = new AlsRefactoredSourceSyncScope(7, 1, r.Catalog, r.Sync, r.Triangles,
            [new(4, definitions, new Dictionary<int, string>{{0,"Movement"},{1,"Jump"}})]);
        var direct = new AlsRefactoredSourcePlayerRuntime(r.Catalog, r.Sync, r.Triangles, definitions);
        for (var frame = 0; frame < 180; frame++)
        {
            var c = Context(frame); var hidden = frame % 30 >= 20;
            AlsRefactoredSourcePlayerInput[] input = hidden ? [new(1, default, 1, 1)] :
                [new(1, default, 1, 1), new(0, new(.7f, .3f), frame < 90 ? 1 : -1, .8f), new(2, default, 1, 1)];
            scope.Begin(c); foreach (var tick in input) scope.Enqueue(4, tick, c.WithWeight(tick.Weight)); scope.Complete();
            direct.Prepare(frame, input, c.Delta, groupOrder: [1, 0]);
            Assert.True(scope.Players.SequenceEqual(direct.Players)); Assert.True(scope.Samples.SequenceEqual(direct.Samples));
            Assert.True(scope.TickContexts.SequenceEqual(direct.TickContexts));
            if (!hidden) { scope.Evaluate(4, 0); direct.Evaluate(frame, 0); Assert.True(scope.Pose(4, 0).SequenceEqual(direct.Pose(0))); }
            scope.Commit(c.Identity); direct.Commit(frame);
        }
    }

    [Fact]
    public void BothBuffersKeepDifferentInsertionOrderWithoutExchangingGroupHistory()
    {
        var scope = Create(); var movementTime = .1f; var jumpTime = .7f;
        for (var frame = 0; frame < 12; frame++)
        {
            var c = Context(frame); scope.Begin(c);
            // Seed distinct buffer orders; later registration order intentionally differs.
            if (frame == 0 || frame > 1 && frame % 2 == 1)
            { scope.Enqueue(22, new(1, default, 1, 1), c); scope.Enqueue(11, new(0, default, 1, 1), c); }
            else { scope.Enqueue(11, new(0, default, 1, 1), c); scope.Enqueue(22, new(1, default, 1, 1), c); }
            scope.Complete();
            var expected = frame % 2 == 0 ? new[] {"Jump", "Movement"} : new[] {"Movement", "Jump"};
            Assert.Equal(expected, scope.EncounteredGroups.ToArray().Select(g => scope.GroupNames[g]));
            var ordered = scope.TickContexts.ToArray().OrderBy(v => v.Order).Select(v => scope.OwnerPlayer(v.PlayerId).Owner).ToArray();
            Assert.Equal(frame % 2 == 0 ? new uint[] {22, 11} : new uint[] {11, 22}, ordered);
            var movement = scope.Players.ToArray().Single(p => p.PlayerId == scope.PlayerId(11, 0));
            var jump = scope.Players.ToArray().Single(p => p.PlayerId == scope.PlayerId(22, 1));
            Assert.Equal(movementTime, movement.DeltaPrevious); Assert.Equal(jumpTime, jump.DeltaPrevious);
            movementTime += c.Delta; jumpTime += c.Delta;
            Assert.Equal(movementTime, movement.Time); Assert.Equal(jumpTime, jump.Time);
            scope.Commit(c.Identity);
        }
    }

    [Fact]
    public void OriginalLocomotionInventoryBindsAllSixOwnersToOnePhysicalBank()
    {
        var p = AlsRefactoredLocomotionHostTests.Data.Value;
        var r = Frozen.Value; var scope = p.CreateSourceScope(7, 1, r.Notify);
        Assert.Equal(new[] {"Movement", "Run Start", "First Pivot", "Second Pivot", "Fall", "Jump", "Flail", "Land"}.Order(), scope.GroupNames.ToArray().Order());
        var owners = Enumerable.Range(0, scope.PlayerCount).Select(scope.OwnerPlayer).ToArray();
        Assert.Equal(Enum.GetValues<AlsRefactoredLocomotionSourceOwner>().Select(o => (uint)o), owners.Select(o => o.Owner).Distinct());
        var root = Context(0); scope.Begin(root, true);
        foreach (var owner in owners)
        {
            Assert.True(p.Actions.Standing.Catalog.Assets.ContainsKey(scope.Source(owner.Owner, owner.LocalPlayer)));
            scope.Enqueue(owner.Owner, new(owner.LocalPlayer, default, 1, 1), root);
        }
        scope.Complete(); Assert.Equal(scope.PlayerCount, scope.Players.Length);
        Assert.Equal(scope.PlayerCount, scope.Ticks.ToArray().Select(t => t.PlayerId).Distinct().Count());
        var movement = scope.Groups.ToArray().Single(g => scope.GroupNames[g.Group.GroupId] == "Movement");
        Assert.Equal(new[] {(uint)AlsRefactoredLocomotionSourceOwner.StandingMovement, (uint)AlsRefactoredLocomotionSourceOwner.CrouchingMovement},
            scope.Players.Slice(movement.PlayerStart, movement.PlayerCount).ToArray().Select(v => scope.OwnerPlayer(v.PlayerId).Owner).Distinct().Order());
        var notify = new AlsRefactoredSourceNotifyTick[scope.PlayerCount];
        Assert.Equal(scope.PlayerCount, scope.BuildNotifyTicks(notify));
        foreach (var owner in owners) { scope.Evaluate(owner.Owner, owner.LocalPlayer); Assert.Equal(79, scope.Pose(owner.Owner, owner.LocalPlayer).Length); }
        scope.Commit(root.Identity);
        root = Context(1); scope.Begin(root); scope.Complete(); Assert.Empty(scope.Players.ToArray()); scope.Commit(root.Identity);
    }

    [Fact]
    public void DuplicateVisitsAndForeignContextsDoNotPartiallyRegister()
    {
        var scope = Create(); var c = Context(0); scope.Begin(c);
        Assert.Throws<ArgumentException>(() => scope.Enqueue(11, new(0, default, 1, .5f), c));
        Assert.Throws<ArgumentException>(() => scope.Enqueue(11, new(0, default, 1, 1), Context(1)));
        scope.Enqueue(11, new(0, default, 1, 1), c);
        Assert.Throws<ArgumentException>(() => scope.Enqueue(11, new(0, default, 1, 1), c));
        Assert.Throws<InvalidOperationException>(() => scope.ReinitializeOwner(11));
        scope.Complete(); Assert.Single(scope.Players.ToArray()); scope.Commit(c.Identity);
        Assert.Throws<ArgumentException>(() => scope.Begin(c));
    }
}
