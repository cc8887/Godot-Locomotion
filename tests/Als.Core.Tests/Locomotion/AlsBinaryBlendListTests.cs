using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsBinaryBlendListTests
{
    private static readonly AlsBinaryBlendSettings Settings = new(.3f, .2f, AlsTransitionBlend.Cubic);

    [Fact]
    public void ContinuousUpdatesMatchNativeBlendListWeightsAndChildDispatch()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_binary_blend_list_native.json")));
        var count = 0;
        foreach (var scenario in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var state = default(AlsBinaryBlendState);
            foreach (var row in scenario.GetProperty("frames").EnumerateArray())
            {
                if (row.GetProperty("reset").GetBoolean()) state = default;
                var child = row.GetProperty("child").GetInt32(); var delta = row.GetProperty("delta").GetSingle();
                var candidate = AlsBinaryBlendList.Advance(state, child, delta, Settings);
                Assert.Equal(candidate, AlsBinaryBlendList.Advance(state, child, delta, Settings));
                state = candidate.State;
                Assert.InRange(MathF.Abs(state.FirstWeight - row.GetProperty("a").GetSingle()), 0, .000002f);
                Assert.InRange(MathF.Abs(state.SecondWeight - row.GetProperty("b").GetSingle()), 0, .000002f);
                for (var i = 0; i < 2; i++)
                {
                    var prefix = i == 0 ? "a" : "b"; var weight = i == 0 ? state.FirstWeight : state.SecondWeight;
                    var updated = weight > AlsPoseBlender.WeightThreshold;
                    Assert.Equal((updated ? 1 : 0) + (candidate.ZeroWeightPreviousChild == i ? 1 : 0),
                        row.GetProperty(prefix + "Updates").GetInt32());
                    Assert.InRange(MathF.Abs((updated ? weight : 0) - row.GetProperty(prefix + "UpdateWeight").GetSingle()), 0, .000002f);
                    Assert.Equal(updated && child == i || candidate.ZeroWeightPreviousChild == i,
                        row.GetProperty(prefix + "Active").GetBoolean());
                }
                count++;
            }
        }
        Assert.Equal(2520, count);
    }

    [Fact]
    public void FirstActivationIsImmediateAndReversalScalesRemainingDuration()
    {
        var first = AlsBinaryBlendList.Advance(default, 1, 0, Settings).State;
        Assert.Equal(1, first.SecondWeight);
        var partial = AlsBinaryBlendList.Advance(first, 0, .15f, Settings).State;
        Assert.Equal(.5f, partial.FirstWeight, 5);
        var reverse = AlsBinaryBlendList.Advance(partial, 1, 0, Settings).State;
        Assert.Equal(.1f, reverse.Second.Remaining, 5);
        Assert.Equal(1, AlsBinaryBlendList.Advance(reverse, 1, .1f, Settings).State.SecondWeight);
    }

    [Theory]
    [InlineData(-1, .1f)]
    [InlineData(2, .1f)]
    [InlineData(0, -1)]
    [InlineData(0, float.NaN)]
    public void InvalidInputsAreRejected(int child, float delta) =>
        Assert.Throws<ArgumentException>(() => AlsBinaryBlendList.Advance(default, child, delta, Settings));

    [Fact]
    public void SteadyAndInterruptedUpdatesAllocateNothing()
    {
        var state = default(AlsBinaryBlendState);
        for (var i = 0; i < 1000; i++) state = AlsBinaryBlendList.Advance(state, i % 2, .01f, Settings).State;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) state = AlsBinaryBlendList.Advance(state, i % 2, .01f, Settings).State;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
