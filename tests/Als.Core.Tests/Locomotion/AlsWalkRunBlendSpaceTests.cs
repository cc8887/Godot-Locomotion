using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsWalkRunBlendSpaceTests
{
    [Fact]
    public void GridCenterUsesAllFourCorners()
    {
        Assert.Equal(new Vector4(0.25f), AlsWalkRunBlendSpace.SampleWeights(new Vector2(0.5f)));
        Assert.Equal(new Vector4(0, 1, 0, 0), AlsWalkRunBlendSpace.SampleWeights(new Vector2(2, -1)));
    }

    [Fact]
    public void FirstInputHasNoSyntheticZeroHistoryAndZeroDeltaDoesNotAdvance()
    {
        var state = AlsWalkRunBlendSpace.Advance(default, new Vector2(0.2f, 1), 1f / 60, Vector2.One);
        Assert.Equal(new Vector2(0.2f, 1), state.Output);
        var unchanged = AlsWalkRunBlendSpace.Advance(state, Vector2.Zero, 0, Vector2.One);
        Assert.Equal(state.Output, unchanged.Output);
    }

    [Fact]
    public void CubicHistoryWeightsAreNotExponentialInterpolation()
    {
        var state = AlsWalkRunBlendSpace.Advance(default, Vector2.Zero, 0.25f, Vector2.One);
        state = AlsWalkRunBlendSpace.Advance(state, Vector2.One, 0.5f, Vector2.One);
        Assert.Equal(1 / 1.875f, state.Output.X, 6);
    }

    [Fact]
    public void CandidateHistoryDoesNotMutateCommittedState()
    {
        var committed = AlsWalkRunBlendSpace.Advance(default, Vector2.Zero, 1f / 60, Vector2.One);
        var candidate = AlsWalkRunBlendSpace.Advance(committed, Vector2.One, 1f / 60, Vector2.One);
        var ignored = AlsWalkRunBlendSpace.Advance(candidate, Vector2.One, 1f / 60, Vector2.One);
        var replay = AlsWalkRunBlendSpace.Advance(committed, Vector2.One, 1f / 60, Vector2.One);
        Assert.Equal(candidate.Output, replay.Output);
        Assert.Equal(Vector2.Zero, committed.Output);
        Assert.True(ignored.Output.X > candidate.Output.X);
    }

    [Fact]
    public void OverflowFailsWithoutDroppingLiveHistory()
    {
        var state = default(AlsWalkRunFilterState);
        for (var i = 0; i < 256; i++)
            state = AlsWalkRunBlendSpace.Advance(state, Vector2.One, 0.001f, Vector2.One);
        Assert.Throws<InvalidOperationException>(() => AlsWalkRunBlendSpace.Advance(state, Vector2.Zero, 0.001f, Vector2.One));
        Assert.Equal(Vector2.One, state.Output);
    }

    [Fact]
    public void NonFiniteInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsWalkRunBlendSpace.SampleWeights(new Vector2(float.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsWalkRunBlendSpace.Advance(default, Vector2.Zero, -1, Vector2.One));
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsWalkRunBlendSpace.Advance(default, Vector2.Zero, 1, Vector2.Zero));
        var state = AlsWalkRunBlendSpace.Advance(default, Vector2.One, float.MaxValue, Vector2.One);
        Assert.Throws<InvalidOperationException>(() => AlsWalkRunBlendSpace.Advance(state, Vector2.One, float.MaxValue, Vector2.One));
    }

    [Fact]
    public void NativeBlendSpaceTraceMatchesAxisFiltersAndSampleWeights()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_walkrun_native.json")));
        var count = 0;
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(6, document.RootElement.GetProperty("assets").GetArrayLength());
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var axes = asset.GetProperty("axes");
            var windows = new Vector2(axes[0].GetProperty("seconds").GetSingle(), axes[1].GetProperty("seconds").GetSingle());
            foreach (var run in asset.GetProperty("runs").EnumerateArray())
            {
                var state = default(AlsWalkRunFilterState);
                foreach (var row in run.GetProperty("frames").EnumerateArray())
                {
                    state = AlsWalkRunBlendSpace.Advance(state,
                        new Vector2(row.GetProperty("stride").GetSingle(), row.GetProperty("gait").GetSingle()),
                        row.GetProperty("delta").GetSingle(), windows);
                    Assert.InRange(MathF.Abs(state.Output.X - row.GetProperty("filteredStride").GetSingle()), 0, 0.00001f);
                    Assert.InRange(MathF.Abs(state.Output.Y - row.GetProperty("filteredGait").GetSingle()), 0, 0.00001f);
                    var weights = AlsWalkRunBlendSpace.SampleWeights(state.Output);
                    var native = row.GetProperty("weights");
                    // Native sample order is Walk, WalkPose, Run, RunPose.
                    Assert.InRange(MathF.Abs(weights.X - native[1].GetSingle()), 0, 0.00001f);
                    Assert.InRange(MathF.Abs(weights.Y - native[0].GetSingle()), 0, 0.00001f);
                    Assert.InRange(MathF.Abs(weights.Z - native[3].GetSingle()), 0, 0.00001f);
                    Assert.InRange(MathF.Abs(weights.W - native[2].GetSingle()), 0, 0.00001f);
                    count++;
                }
            }
        }
        Assert.Equal(5040, count);
    }
}
