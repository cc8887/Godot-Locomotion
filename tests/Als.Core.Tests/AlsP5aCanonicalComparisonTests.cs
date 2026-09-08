using System.Text.Json.Nodes;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aCanonicalComparisonTests
{
    [Theory]
    [InlineData("events")]
    [InlineData("actionOutcomes")]
    [InlineData("activeNotifyStates")]
    public void MatchingRuntimeArraysAreComparedWithoutHistoricalFrameCardinalities(string member)
    {
        var bundle = P5aFrozenBundle.Create();
        MoveFirstPopulatedArray(bundle.NativeExpected, member);
        MoveFirstPopulatedArray(bundle.PortSchemaSeed, member);

        AlsP5aTrace.ValidateCrossEnginePair(bundle.NativeExpected, bundle.PortSchemaSeed);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("actionOutcomes")]
    [InlineData("activeNotifyStates")]
    public void MovingOnlyOneEngineArrayStillRejects(string member)
    {
        var bundle = P5aFrozenBundle.Create();
        MoveFirstPopulatedArray(bundle.NativeExpected, member);

        Assert.Throws<InvalidDataException>(() =>
            AlsP5aTrace.ValidateCrossEnginePair(bundle.NativeExpected, bundle.PortSchemaSeed));
    }

    [Theory]
    [InlineData("events", "missing")]
    [InlineData("events", "extra")]
    [InlineData("events", "type")]
    [InlineData("actionOutcomes", "missing")]
    [InlineData("actionOutcomes", "extra")]
    [InlineData("actionOutcomes", "type")]
    [InlineData("activeNotifyStates", "missing")]
    [InlineData("activeNotifyStates", "extra")]
    [InlineData("activeNotifyStates", "type")]
    public void IdenticalMalformedRuntimeRowsRejectInBothEngines(string member, string mutation)
    {
        var bundle = P5aFrozenBundle.Create();
        foreach (var document in new[] { bundle.NativeExpected, bundle.PortSchemaSeed })
        {
            var array = MoveFirstPopulatedArray(document, member);
            var row = array[0]!.AsObject();
            var firstProperty = row.First().Key;
            if (mutation == "missing") row.Remove(firstProperty);
            else if (mutation == "extra") row["unexpected"] = true;
            else row[firstProperty] = new JsonObject();
        }

        Assert.Throws<InvalidDataException>(() =>
            AlsP5aTrace.ValidateCrossEnginePair(bundle.NativeExpected, bundle.PortSchemaSeed));
    }

    [Theory]
    [InlineData("events", 16)]
    [InlineData("actionOutcomes", 2)]
    [InlineData("activeNotifyStates", 16)]
    public void IdenticalRuntimeArraysOverCapacityReject(string member, int capacity)
    {
        var bundle = P5aFrozenBundle.Create();
        foreach (var document in new[] { bundle.NativeExpected, bundle.PortSchemaSeed })
        {
            var array = MoveFirstPopulatedArray(document, member);
            while (array.Count <= capacity) array.Add(array[0]!.DeepClone());
        }

        Assert.Throws<InvalidDataException>(() =>
            AlsP5aTrace.ValidateCrossEnginePair(bundle.NativeExpected, bundle.PortSchemaSeed));
    }

    private static JsonArray MoveFirstPopulatedArray(JsonObject document, string member)
    {
        var arrays = document["cases"]!.AsArray()
            .SelectMany(caseNode => caseNode!["frames"]!.AsArray())
            .Select(frame => member == "activeNotifyStates"
                ? frame!["comparableActual"]!["stateAfter"]![member]!.AsArray()
                : frame!["comparableActual"]![member]!.AsArray()).ToArray();
        var source = arrays.First(array => array.Count > 0);
        var destination = arrays.First(array => array.Count == 0);
        foreach (var item in source) destination.Add(item!.DeepClone());
        source.Clear();
        return destination;
    }
}
