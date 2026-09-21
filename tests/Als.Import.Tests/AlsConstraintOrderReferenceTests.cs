using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsConstraintOrderReferenceTests
{
    [Fact]
    public void LevelsAndWithinIslandContainerOrderMatchNativeGraph()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_graph_reference.json")));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var rows = document.RootElement.GetProperty("cases"); Assert.Equal(16, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var dynamic = row.GetProperty("dynamic").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
            var raw = row.GetProperty("edges").EnumerateArray().OrderBy(e => e.GetProperty("container").GetInt32()).ToArray();
            var edges = raw.Select(e => new AlsConstraintEdge(e.GetProperty("container").GetInt32(),
                e.GetProperty("body0").GetInt32(), e.GetProperty("body1").GetInt32(), (ulong)e.GetProperty("id").GetInt32())).ToArray();
            var graph = new AlsConstraintOrder(dynamic, edges.Length); graph.Build(edges);
            Assert.Equal(row.GetProperty("bodyLevels").EnumerateArray().Select(v => v.GetInt32()),
                Enumerable.Range(0, dynamic.Length).Select(graph.BodyLevelAt));
            var expected = row.GetProperty("ordered").EnumerateArray().ToArray(); Assert.Equal(edges.Length, expected.Length);
            for (var i = 0; i < edges.Length; i++)
            {
                var index = graph.OrderedIndexAt(i);
                Assert.Equal(expected[i].GetProperty("id").GetInt32(), raw[index].GetProperty("id").GetInt32());
                Assert.Equal(expected[i].GetProperty("level").GetInt32(), graph.EdgeLevelAt(index));
            }
        }
    }
}
