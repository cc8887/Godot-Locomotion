using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsTransitionStackTests
{
    [Fact]
    public void AlphaAndInjectedStackWeightsMatchNativeEngineFunctions()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_transition_math.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("injected stacks", root.GetProperty("source").GetString());
        var alphas = root.GetProperty("alphas");
        Assert.Equal(363, alphas.GetArrayLength());
        foreach (var row in alphas.EnumerateArray())
        {
            var mode = Enum.Parse<AlsTransitionBlend>(row.GetProperty("mode").GetString()!);
            Assert.InRange(MathF.Abs(AlsTransitionStack.Alpha(row.GetProperty("progress").GetSingle(), mode) -
                row.GetProperty("alpha").GetSingle()), 0, 1e-6f);
        }
        var stacks = root.GetProperty("stacks");
        Assert.Equal(65, stacks.GetArrayLength());
        foreach (var row in stacks.EnumerateArray())
        {
            var state = AlsTransitionStack.Initialize(0);
            foreach (var entry in row.GetProperty("entries").EnumerateArray())
            {
                var to = entry.GetProperty("to").GetInt32();
                state.Entries[state.Count++] = new AlsActiveTransition(entry.GetProperty("from").GetInt32(), to,
                    1, 0, 1, entry.GetProperty("alpha").GetSingle(), AlsTransitionBlend.Linear);
                state.CurrentState = to;
            }
            for (var id = 0; id < 6; id++)
                Assert.InRange(MathF.Abs(AlsTransitionStack.Weight(state, id) - row.GetProperty("weights")[id].GetSingle()), 0, 1e-6f);
        }
    }

    [Fact]
    public void InterruptKeepsAndAttenuatesTheEarlierTransition()
    {
        var state = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .25f);
        state = AlsTransitionStack.Start(state, 2, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .25f);
        Assert.Equal(2, state.Count);
        Assert.Equal(.375f, AlsTransitionStack.Weight(state, 0));
        Assert.Equal(.375f, AlsTransitionStack.Weight(state, 1));
        Assert.Equal(.25f, AlsTransitionStack.Weight(state, 2));
        Assert.Equal(2, state.CurrentState);
    }

    [Theory]
    [InlineData(AlsTransitionBlend.Cubic, .6f)]
    [InlineData(AlsTransitionBlend.HermiteCubic, .55f)]
    public void ReentryShortensDurationUsingTheActualTargetContribution(AlsTransitionBlend mode, float expected)
    {
        var state = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .75f);
        state = AlsTransitionStack.Start(state, 0, .8f, mode);
        Assert.Equal(expected, state.Latest.Duration, 6);
        Assert.Equal(0, state.Latest.Alpha);
    }

    [Fact]
    public void CompletingANewerTransitionRemovesUnfinishedOlderEntries()
    {
        var state = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .1f);
        state = AlsTransitionStack.Start(state, 2, .2f, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .2f);
        Assert.Equal(0, state.Count);
        Assert.Equal(1, AlsTransitionStack.Weight(state, 2));
        Assert.Equal(0, AlsTransitionStack.Weight(state, 0));
    }

    [Fact]
    public void CompletingAnOlderTransitionRetainsTheNewerBlend()
    {
        var state = AlsTransitionStack.Start(default, 1, .25f, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .125f);
        state = AlsTransitionStack.Start(state, 2, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .125f);
        Assert.Equal(1, state.Count);
        Assert.Equal(1, state.GetTransition(0).From);
        Assert.Equal(.875f, AlsTransitionStack.Weight(state, 1));
        Assert.Equal(.125f, AlsTransitionStack.Weight(state, 2));
    }

    [Fact]
    public void BoneProfileIsAppliedToEachTransitionBeforeStackComposition()
    {
        var state = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .25f);
        state = AlsTransitionStack.Start(state, 2, 1, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Advance(state, .25f);
        var last = AlsStandingCycle.ProfileAlpha(.25f, 2);
        Assert.Equal(.2f * (1 - last), AlsTransitionStack.Weight(state, 0, 2), 6);
        Assert.Equal(.8f * (1 - last), AlsTransitionStack.Weight(state, 1, 2), 6);
        Assert.Equal(last, AlsTransitionStack.Weight(state, 2, 2), 6);
    }

    [Fact]
    public void CandidateMutationAndRejectedCurveCannotChangeCommittedEntries()
    {
        var committed = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Custom);
        var candidate = AlsTransitionStack.Advance(committed, .25f, static t => t);
        candidate = AlsTransitionStack.Start(candidate, 2, 1, AlsTransitionBlend.Linear);
        Assert.Equal(1, committed.Count);
        Assert.Equal(0, committed.GetTransition(0).Elapsed);
        Assert.Throws<InvalidOperationException>(() => AlsTransitionStack.Advance(committed, .1f, static _ => float.NaN));
        Assert.Equal(0, committed.GetTransition(0).Alpha);
        Assert.Equal(.25f, AlsTransitionStack.Advance(committed, .25f, static t => t).GetTransition(0).Alpha);
    }

    [Fact]
    public void ZeroDurationSettlesDuringUpdateAndInvalidInputsAreRejected()
    {
        var state = AlsTransitionStack.Start(default, 1, 0, AlsTransitionBlend.Linear);
        Assert.Equal(1, state.Count);
        Assert.Equal(0, AlsTransitionStack.Advance(state, 0).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsTransitionStack.Advance(state, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsTransitionStack.Start(state, 2, -1, AlsTransitionBlend.Linear));
        Assert.Throws<ArgumentException>(() => AlsTransitionStack.Alpha(.5f, AlsTransitionBlend.Custom));
    }

    [Fact]
    public void OverflowRejectsInsteadOfDiscardingContributingTransitions()
    {
        var state = default(AlsTransitionStackState);
        for (var i = 0; i < AlsTransitionStack.Capacity; i++)
            state = AlsTransitionStack.Start(state, (state.CurrentState + 1) % 6, .75f, AlsTransitionBlend.Cubic);
        Assert.Throws<InvalidOperationException>(() => AlsTransitionStack.Start(state,
            (state.CurrentState + 1) % 6, .75f, AlsTransitionBlend.Cubic));
        Assert.Equal(AlsTransitionStack.Capacity, state.Count);
    }

    [Fact]
    public void CurveEndpointCompletesWithTimeRemainingAndRetiresOlderTransitions()
    {
        var state = AlsTransitionStack.Start(default, 1, 2, AlsTransitionBlend.Linear);
        state = AlsTransitionStack.Start(state, 2, 1, AlsTransitionBlend.Custom);
        state = AlsTransitionStack.Advance(state, .5f, out var visited, static t => MathF.Min(t, .5f));
        Assert.Equal(0, state.Count);
        Assert.Equal(2, state.CurrentState);
        Assert.False(visited.GetTransition(0).Complete);
        Assert.True(visited.GetTransition(1).Complete);
        Assert.Equal(.5f, visited.GetTransition(1).Remaining);
        Assert.Equal(.5f, visited.GetTransition(1).Alpha);
    }

    [Fact]
    public void ConstantCurveIsAlreadyCompleteBeforeItsClockAdvances()
    {
        var state = AlsTransitionStack.Start(default, 1, 1, AlsTransitionBlend.Custom);
        state = AlsTransitionStack.Advance(state, 0, out var visited, static _ => .25f);
        Assert.Equal(0, state.Count);
        Assert.Equal(1, visited.GetTransition(0).Remaining);
        Assert.True(visited.GetTransition(0).Complete);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void RapidReversalsRemainNormalizedAndAllocationFree(int hz)
    {
        var state = default(AlsTransitionStackState);
        for (var i = 0; i < hz; i++) Step(ref state, hz, i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < hz * 10; i++) Step(ref state, hz, i);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        var total = Enumerable.Range(0, 6).Sum(id => AlsTransitionStack.Weight(state, id));
        Assert.InRange(total, .99999f, 1.00001f);
    }

    private static void Step(ref AlsTransitionStackState state, int hz, int frame)
    {
        state = AlsTransitionStack.Start(state, frame % 6, .75f, AlsTransitionBlend.Cubic);
        state = AlsTransitionStack.Advance(state, 1f / hz);
    }
}
