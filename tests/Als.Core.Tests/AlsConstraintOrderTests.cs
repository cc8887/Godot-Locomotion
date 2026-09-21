using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsConstraintOrderTests
{
    [Fact]
    public void IndependentSingleEdgeAndUnsupportedComponentsKeepNativeZeroLevels()
    {
        var graph = new AlsConstraintOrder([false, true, true, true, true, true], 4);
        graph.Build([new(0, 0, 1, 0), new(1, 2, 3, 1), new(1, 3, 4, 2), new(1, 4, 5, 3)]);
        for (var i = 0; i < 6; i++) Assert.Equal(0, graph.BodyLevelAt(i));
        for (var i = 0; i < 4; i++) Assert.Equal(0, graph.EdgeLevelAt(i));
    }

    [Fact]
    public void InvalidSnapshotKeepsPriorOrderAndLevels()
    {
        var graph = new AlsConstraintOrder([false, true, true], 2);
        graph.Build([new(0, 0, 2, 1), new(1, 1, 2, 0)]);
        Assert.Equal(2, graph.BodyLevelAt(1)); Assert.Equal(1, graph.BodyLevelAt(2));
        foreach (var edge in new AlsConstraintEdge[] { new(-1, 0, 1, 0), new(0, 1, 3, 0), new(0, 1, 1, 0) })
            Assert.Throws<ArgumentException>(() => graph.Build([edge]));
        Assert.Equal(2, graph.Count); Assert.Equal(2, graph.BodyLevelAt(1)); Assert.Equal(1, graph.BodyLevelAt(2));
    }

    [Fact]
    public void BuildDoesNotAllocateAfterConstruction()
    {
        var graph = new AlsConstraintOrder([false, true, true, true], 3);
        AlsConstraintEdge[] edges = [new(0, 0, 3, 2), new(1, 1, 2, 0), new(1, 2, 3, 1)];
        for (var i = 0; i < 100; i++) graph.Build(edges);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) graph.Build(edges);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
