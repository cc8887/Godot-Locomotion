using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Tests;

public sealed class AlsWorldContactOrderReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ActualNativeWorldContactsUseCreationIdsThenPrimitiveDispatch()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_world_order_window.json")));
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(2, cases.GetArrayLength());
        var contacts = 0; var reversed = 0; var dynamicPairs = 0; var frames = 0;
        foreach (var row in cases.EnumerateArray())
        foreach (var sample in row.GetProperty("samples").EnumerateArray().Skip(1))
        {
            frames++; var bodies = sample.GetProperty("bodies").EnumerateArray().ToArray();
            var indices = bodies.Select((body, index) => (body.GetProperty("name").GetString()!, index))
                .ToDictionary(p => p.Item1, p => p.index);
            for (var i = 0; i < bodies.Length; i++)
            {
                Assert.Equal(-1, bodies[i].GetProperty("particleGlobalId").GetInt32());
                Assert.Equal(i, bodies[i].GetProperty("particleLocalId").GetInt32());
            }
            var graph = sample.GetProperty("contactsAfterSolve").EnumerateArray()
                .Where(p => p.GetProperty("inGraph").GetBoolean()).OrderBy(p => p.GetProperty("graphOrder").GetInt32()).ToArray();
            for (var i = 0; i < graph.Length; i++)
            {
                var pair = graph[i]; Assert.Equal(i, pair.GetProperty("graphOrder").GetInt32());
                Assert.Equal(-1, pair.GetProperty("graphColor").GetInt32()); Assert.False(pair.GetProperty("graphSleeping").GetBoolean());
                var a = Endpoint(pair, "body0", "shape0"); var b = Endpoint(pair, "body1", "shape1");
                Assert.NotEqual(a.Id, b.Id); var nativeReverse = a.Id > b.Id;
                var first = nativeReverse ? b : a; var second = nativeReverse ? a : b;
                Assert.Equal(nativeReverse, AlsCollisionPairOrder.ShouldReverse(first.Kind, first.Id, first.Dynamic,
                    second.Kind, second.Id, second.Dynamic));
                if (nativeReverse) reversed++;
                if (a.Dynamic && b.Dynamic) dynamicPairs++;
                contacts++;
            }
            (ulong Id, AlsBoundsShapeKind Kind, bool Dynamic) Endpoint(JsonElement pair, string bodyField, string shapeField)
            {
                var name = pair.GetProperty(bodyField).GetString()!;
                if (name.StartsWith("environment_", StringComparison.Ordinal))
                    return ((ulong)(bodies.Length + int.Parse(name["environment_".Length..])), AlsBoundsShapeKind.Polygon, false);
                var index = indices[name]; var state = bodies[index];
                var leaf = state.GetProperty("shapeFilters")[pair.GetProperty(shapeField).GetInt32()].GetProperty("leafType").GetString();
                var kind = leaf switch { "Sphere" => AlsBoundsShapeKind.Sphere, "Capsule" => AlsBoundsShapeKind.Capsule,
                    "Box" or "Convex" => AlsBoundsShapeKind.Polygon, _ => throw new InvalidDataException("Unexpected native leaf " + leaf) };
                Assert.Equal(4, state.GetProperty("objectState").GetInt32()); // EObjectStateType::Dynamic.
                return ((ulong)index, kind, true);
            }
        }
        output.WriteLine($"NATIVE_WORLD_ORDER frames={frames} contacts={contacts} reversed={reversed} dynamicPairs={dynamicPairs}");
        Assert.Equal(20, frames); Assert.True(contacts > 300); Assert.True(reversed > 20 && dynamicPairs >= reversed);
    }
}
