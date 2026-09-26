using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredNotifyTests
{
    private const string Walk = "/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward";
    private const string Blend = "/ALS/ALS/Animations/Grounded/WalkRun/BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward";
    private static string Inputs() => MantlingHostFixture.Read("refactored_notify_inputs");
    private static (AlsRefactoredAnimationCatalog Catalog, AlsRefactoredSyncBank Sync, AlsRefactoredNotifyBank Bank) Resources()
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var sync = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        return (catalog, sync, new(Inputs(), catalog, sync));
    }

    [Fact]
    public void CompleteOriginalClosureKeepsTypedPayloadsAndNativeBranchingSeparate()
    {
        var r = Resources(); var bank = r.Bank;
        Assert.Equal(136, bank.Assets.Count); Assert.Equal(127, bank.Sequences.Length); Assert.Equal(177, bank.Events.Length);
        Assert.Equal(154, bank.Events.ToArray().Count(e => e.Payload is AlsRefactoredFootstepPayload));
        Assert.Equal(15, bank.Events.ToArray().Count(e => e.NativeBranchingPoint));
        Assert.All(bank.Assets.Values.Where(a => !a.Montage), a => Assert.Equal(r.Sync.Sequences[a.AssetId].DurationSeconds, a.Length));
        Assert.Equal(1.3f, Assert.IsType<AlsRefactoredRootScalePayload>(Assert.Single(bank.Events.ToArray(), e => e.Payload is AlsRefactoredRootScalePayload).Payload).Scale);
        Assert.Equal("Als.GroundedEntryMode.FromRoll", Assert.IsType<AlsRefactoredGroundedEntryPayload>(Assert.Single(bank.Events.ToArray(), e => e.Payload is AlsRefactoredGroundedEntryPayload).Payload).Mode);
        // Input file order is not identity: source order and authored event indices are.
        var json = JsonNode.Parse(Inputs())!; var rows = json["assets"]!.AsArray();
        var reversed = rows.Reverse().Select(n => n!.DeepClone()).ToArray(); rows.Clear(); foreach (var row in reversed) rows.Add(row);
        var other = new AlsRefactoredNotifyBank(json.ToJsonString(), r.Catalog, r.Sync);
        Assert.True(bank.Definitions.SequenceEqual(other.Definitions)); Assert.True(bank.Policies.SequenceEqual(other.Policies));
        Assert.True(bank.Events.SequenceEqual(other.Events)); Assert.Equal(bank.Assets.OrderBy(p => p.Key), other.Assets.OrderBy(p => p.Key));
    }

    [Theory]
    [InlineData("catalog")][InlineData("source")][InlineData("closure")][InlineData("object")]
    [InlineData("order")][InlineData("trigger")][InlineData("class")][InlineData("weight")][InlineData("payload")]
    [InlineData("foot")][InlineData("follower")][InlineData("chance")][InlineData("flags")]
    public void RejectsChangedAuthoredIdentityAndPolicy(string change)
    {
        var r = Resources(); var root = JsonNode.Parse(Inputs())!; var rows = root["assets"]!.AsArray();
        var asset = rows.Single(n => n!["path"]!.GetValue<string>() == Walk)!; var n = asset["notifies"]![0]!;
        switch (change)
        {
            case "catalog": root["catalogSha256"] = new string('0', 64); break;
            case "source": asset["sourceSha256"] = new string('0', 64); break;
            case "closure": rows.RemoveAt(0); break;
            case "object": n["notifyObject"] = Walk + ":Foreign"; break;
            case "order": n["index"] = 9; break;
            case "trigger": n["triggerTime"] = .419f; break;
            case "class": n["class"] = "/Script/ALS.Unknown"; break;
            case "weight": n["weightThreshold"] = .987f; break;
            case "payload": n["payload"]!["foot_bone"] = "MISSING"; break;
            case "foot": n["payload"]!["foot_bone"] = n["payload"]!["foot_bone"]!.GetValue<string>() == "LEFT" ? "RIGHT" : "LEFT"; break;
            case "follower": n["onFollower"] = true; break;
            case "chance": n["chance"] = .5f; break;
            case "flags": n["stateBehaviorFlags"] = 1; break;
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredNotifyBank(root.ToJsonString(), r.Catalog, r.Sync));
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void ActualSourceTicksPreserveIdentityOrderEpochsAndRetry(int hz)
    {
        var r = Resources();
        var profiles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"),
            MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var players = new AlsRefactoredSourcePlayerRuntime(r.Catalog, r.Sync, profiles,
            [new(0, Walk, 0, .1f), new(1, Walk, 0, .6f), new(2, Blend, -1)]);
        var binding = new AlsRefactoredSourceNotifyBinding(11, players, r.Bank);
        var otherScope = new AlsRefactoredSourceNotifyBinding(12, players, r.Bank);
        var output = new AlsRefactoredSourceNotifyTick[9]; var occurrences = new AlsAssetNotifyOccurrence[32];
        var accepted = 0; var followers = 0; var reverse = 0; var loops = 0; var reset = false;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var rate = frame < hz * 2 ? 1f : -1f;
            AlsRefactoredSourcePlayerInput[] input = [new(1, default, rate, .2f), new(2, new Vector2(.6f, .4f), rate, .8f),
                new(0, default, rate, 1, Reinitialize: frame == hz, StartPosition: .3f)];
            AlsRefactoredNotifyPlayerContext[] contexts = [new(2, true), new(0, true), new(1, false, true)];
            players.Prepare(frame, input, 1f / hz);
            var count = binding.BuildTicks(frame, contexts, output); Assert.Equal(3, count);
            var first = output[..count].ToArray();
            Assert.Equal(players.TickContexts.ToArray().OrderBy(c => c.Order).Select(c => c.PlayerId), first.Select(t => t.Playback.PlayerId));
            Assert.Equal(3, first.Select(t => t.Playback).Distinct().Count());
            foreach (var t in first)
            {
                Assert.Equal(11u, t.Playback.OwnerScope);
                if (!t.Leader) followers++;
                if (t.Delta < 0) reverse++;
                if (t.PreviousTime + t.Delta > r.Bank.Sequences[t.SequenceIndex].Length || t.PreviousTime + t.Delta < 0) loops++;
                if (t.Playback.PlayerId == 0 && frame == hz) { Assert.Equal(2, t.Playback.Epoch); reset = true; }
                if (t.Playback.PlayerId == 1) { Assert.False(t.ActiveContext); Assert.True(t.ScopeFiltered); Assert.Equal(.2f, t.Weight); }
                if (t.Playback.PlayerId == 2)
                {
                    Assert.Equal(.8f, t.Weight); // Do not multiply graph weight by BlendSpace sample weight.
                    var h = players.Players.ToArray().Single(p => p.PlayerId == 2); Assert.Equal(h.Time, t.ContextTime);
                    var tick = players.Ticks.ToArray().Single(p => p.PlayerId == 2);
                    var best = players.ResolvedSamples.Slice(tick.SampleStart, tick.SampleCount).ToArray().OrderByDescending(s => s.Weight).First();
                    Assert.Equal(best.SampleId, t.Playback.SampleId);
                }
                accepted += binding.Extract(t, occurrences);
                Assert.Throws<ArgumentException>(() => otherScope.Extract(t, occurrences));
            }
            var unchanged = output.ToArray(); Assert.Throws<ArgumentException>(() => binding.BuildTicks(frame, contexts, output.AsSpan(0, 1)));
            Assert.Equal(unchanged, output);
            players.Cancel(); Assert.Throws<ArgumentException>(() => binding.Extract(first[0], occurrences));
            players.Prepare(frame, input, 1f / hz);
            Assert.Equal(count, binding.BuildTicks(frame, contexts, output)); Assert.Equal(first, output[..count]);
            players.Commit(frame);
        }
        Assert.True(accepted > 10 && followers > 0 && reverse > 0 && loops > 0 && reset);
    }

    [Theory]
    [InlineData(30, false)][InlineData(60, false)][InlineData(120, false)]
    [InlineData(30, true)][InlineData(60, true)][InlineData(120, true)]
    public void OriginalRollSourceDrivesQueueAndStateLifecycleWithoutEffects(int hz, bool hide)
    {
        var r = Resources(); const string roll = "/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll";
        var players = new AlsRefactoredSourcePlayerRuntime(r.Catalog, r.Sync,
            new Dictionary<string, AlsRefactoredTriangulationProfile>(), [new(0, roll, -1, Looping: false)]);
        var binding = new AlsRefactoredSourceNotifyBinding(23, players, r.Bank);
        var ticks = new AlsRefactoredSourceNotifyTick[1]; var occurrences = new AlsAssetNotifyOccurrence[16];
        var incoming = new AlsAssetNotifyReference[16]; var queue = new AlsAssetNotifyReference[16]; var queueScratch = new AlsAssetNotifyReference[16];
        var dispatch = new AlsAssetNotifyDispatchInput[16];
        var current = new AlsAssetNotifyActiveState[16]; var next = new AlsAssetNotifyActiveState[16]; var callbacks = new AlsAssetNotifyCallback[32];
        var remainingScratch = new AlsAssetNotifyActiveState[16]; var nextScratch = new AlsAssetNotifyActiveState[16];
        var beginScratch = new int[16]; var callbackScratch = new AlsAssetNotifyCallback[32];
        var scratch = new AlsAssetNotifyLifecycleScratch(remainingScratch, nextScratch, beginScratch, callbackScratch);
        var seed = AlsTimelineRuntime.InitialAssetNotifyRandomSeed; var currentCount = 0; var allocator = 0;
        var begins = 0; var ends = 0; var tickCount = 0; var entries = 0; var stateInstance = -1;
        for (var frame = 0; frame < hz * 2; frame++)
        {
            // The authored Roll state ends at 1.5001, beyond the 1.5-second clip.
            // Native extraction keeps it while the non-looping node is clamped at its end;
            // leaving the graph (or explicit EndAll) releases it, not a synthetic time clamp.
            var visible = frame < (hide ? hz / 2 : hz * 7 / 4);
            players.Prepare(frame, visible ? [new(0, default, 1, 1)] : [], 1f / hz);
            var count = binding.BuildTicks(frame, visible ? [new(0, true)] : [], ticks);
            var queueCount = 0; var candidateSeed = seed;
            if (count > 0)
            {
                var tick = ticks[0]; var extracted = binding.Extract(tick, occurrences);
                for (var i = 0; i < extracted; i++) incoming[i] = new(occurrences[i].DefinitionIndex, 0,
                    tick.ContextTime, tick.ActiveContext, occurrences[i].ReachedEnd);
                Assert.True(AlsTimelineRuntime.TryQueueAssetNotifies(r.Bank.Policies, [], incoming.AsSpan(0, extracted),
                    new(tick.Leader, false, 0, tick.Weight), AlsAssetNotifyQueueMode.Filtered, seed, queueScratch, queue,
                    out queueCount, out candidateSeed, out var failure), failure.ToString());
                for (var i = 0; i < queueCount; i++)
                    dispatch[i] = new(queue[i], AlsAssetNotifySourceKind.AssetPlayer, 23, false,
                        r.Bank.Events[queue[i].PolicyIndex].Duration, tick.Playback.Epoch, tick.Weight);
            }
            Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(r.Bank.Policies, current.AsSpan(0, currentCount), dispatch.AsSpan(0, queueCount),
                new(AlsAssetNotifyDispatchMode.Default, 1f / hz), allocator, scratch, next, callbacks,
                out var nextCount, out var callbackCount, out var nextAllocator, out var error), error.ToString());
            var snapshot = next[..nextCount].ToArray(); var callbackSnapshot = callbacks[..callbackCount].ToArray();
            // Candidate processing must be repeatable without advancing the committed allocator/states.
            Assert.True(AlsTimelineRuntime.TryAdvanceAssetNotifyStates(r.Bank.Policies, current.AsSpan(0, currentCount), dispatch.AsSpan(0, queueCount),
                new(AlsAssetNotifyDispatchMode.Default, 1f / hz), allocator, scratch, next, callbacks,
                out nextCount, out callbackCount, out var retryAllocator, out _));
            Assert.Equal(nextAllocator, retryAllocator); Assert.Equal(snapshot, next[..nextCount]); Assert.Equal(callbackSnapshot, callbacks[..callbackCount]);
            foreach (var callback in callbackSnapshot)
            {
                var e = r.Bank.Events[callback.State.Input.Reference.PolicyIndex];
                if (e.Payload is AlsRefactoredGroundedEntryPayload) entries++;
                if (e.Payload is not AlsRefactoredRootScalePayload) continue;
                switch (callback.Kind)
                {
                    case AlsAssetNotifyCallbackKind.Begin: begins++; stateInstance = callback.State.InstanceId; Assert.Equal(1.5f, callback.Seconds); break;
                    case AlsAssetNotifyCallbackKind.Tick: tickCount++; Assert.Equal(stateInstance, callback.State.InstanceId); break;
                    case AlsAssetNotifyCallbackKind.End: ends++; Assert.Equal(stateInstance, callback.State.InstanceId); break;
                }
            }
            seed = candidateSeed; allocator = nextAllocator; currentCount = nextCount; next.AsSpan(0, nextCount).CopyTo(current); players.Commit(frame);
            if (!hide && frame == hz * 8 / 5) { Assert.Equal(1, currentCount); Assert.Equal(0, ends); }
        }
        Assert.Equal(1, begins); Assert.Equal(1, ends); Assert.True(tickCount > hz / 3);
        Assert.Equal(hide ? 0 : 1, entries); Assert.Equal(0, currentCount);
    }
}
