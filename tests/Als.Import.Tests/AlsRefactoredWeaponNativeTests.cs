using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponNativeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    { foreach (var kind in Enum.GetValues<AlsRefactoredWeaponKind>()) foreach (var hz in new[] { 30, 60, 120 }) yield return [kind, hz]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ActualLinkedGraphStateStackAndGeneratedNotifiesMatch(AlsRefactoredWeaponKind kind, int hz)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var resources = new AlsRefactoredWeaponMachineResources(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind);
        var runtime = new AlsRefactoredWeaponMachineProfile(catalog, resources).CreateRuntime();
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_weapon_trace_" + kind));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(AlsRefactoredWeaponMachineResources.Blueprint(kind), root.GetProperty("source").GetString());
        foreach (var name in new[] { "refactored_animation_sources", "refactored_weapon_machines" })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))),
                root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        Assert.Equal(new[] { "RelaxedToReady", "ReadyToRelaxed" }, root.GetProperty("notifyDefinitions").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz + "hz");
        float maxTime = 0, maxAlpha = 0, maxWeight = 0; int frame = 0, stacked = 0, notifies = 0; var visited = new HashSet<int>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input");
            var value = new AlsRefactoredWeaponRuleInput(input.GetProperty("rotationMode").GetString()!, input.GetProperty("gait").GetString()!,
                input.GetProperty("locomotionMode").GetString()!, input.GetProperty("moving").GetBoolean(), input.GetProperty("allowed").GetBoolean());
            runtime.Prepare(frame, value, input.GetProperty("delta").GetSingle(), reinitialize: input.GetProperty("reset").GetBoolean());
            var next = runtime.Candidate; var state = next.State; var context = $"{kind}/{hz}/{frame}";
            Assert.True(state.CurrentState == row.GetProperty("current").GetInt32(), context + " state");
            maxTime = MathF.Max(maxTime, MathF.Abs(state.ElapsedSeconds - row.GetProperty("elapsed").GetSingle()));
            var native = row.GetProperty("transitions"); Assert.True(state.Transitions.Count == native.GetArrayLength(), context + " stack length");
            if (state.Transitions.Count > 1) stacked++;
            for (var i = 0; i < state.Transitions.Count; i++)
            {
                var actual = state.Transitions.GetTransition(i); var edge = native[i];
                Assert.Equal(edge.GetProperty("from").GetInt32(), actual.From); Assert.Equal(edge.GetProperty("to").GetInt32(), actual.To);
                Assert.Equal(new[] { state.GetActivePath(i).Edge }, edge.GetProperty("edges").EnumerateArray().Select(e => e.GetInt32()));
                maxTime = MathF.Max(maxTime, MathF.Abs(actual.Elapsed - edge.GetProperty("elapsed").GetSingle()));
                maxTime = MathF.Max(maxTime, MathF.Abs(actual.Duration - edge.GetProperty("duration").GetSingle()));
                maxAlpha = MathF.Max(maxAlpha, MathF.Abs(actual.Alpha - edge.GetProperty("alpha").GetSingle()));
            }
            for (var i = 0; i < 3; i++) maxWeight = MathF.Max(maxWeight,
                MathF.Abs(AlsTransitionStack.Weight(state.Transitions, i) - row.GetProperty("weights")[i].GetSingle()));
            var updates = row.GetProperty("updates").EnumerateArray().Select(v => v.GetInt32()).ToList();
            if (native.GetArrayLength() == 0 && !updates.Contains(state.CurrentState)) updates.Add(state.CurrentState);
            Assert.True(updates.SequenceEqual(Enumerable.Range(0, next.UpdateCount).Select(i => next.GetUpdate(i).State)), context + " update order");
            Assert.True(row.GetProperty("notifies").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(
                Enumerable.Range(0, next.NotifyCount).Select(i => next.GetNotify(i).GeneratedIndex)), context + " notify order");
            for (var i = 0; i < next.TransitionCount; i++) visited.Add(next.GetTransition(i).Path.Edge);
            notifies += next.NotifyCount;
            Assert.True(maxTime <= 2e-6f && maxAlpha <= 2e-6f && maxWeight <= 2e-6f,
                $"{context} time={maxTime:R} alpha={maxAlpha:R} weight={maxWeight:R}");
            runtime.Commit(frame++);
        }
        Assert.Equal(132 + hz, frame); Assert.True(stacked > 0 && notifies > 0);
        Assert.Equal(Enumerable.Range(0, 6), visited.Order());
        output.WriteLine($"{kind}/{hz} frames={frame} stacked={stacked} notifies={notifies} maxTime={maxTime:R} maxAlpha={maxAlpha:R} maxWeight={maxWeight:R}");
    }
}
