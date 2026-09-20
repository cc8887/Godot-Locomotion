using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPoseCacheCompilerTests
{
    [Fact]
    public void PreservesNativeRootOrderAndAllCacheIdentities()
    {
        var profile = Compile(Read());
        Assert.Equal(9, profile.RootNames.Count());
        Assert.Equal(new[] { 100, 99, 33, 32, 30, 31 }, profile.Root("BaseLayer").UpdateOrder.ToArray());
        Assert.Equal(new[] { 763, 760, 765, 757, 758, 764, 762, 761 }, profile.Root("(N) CycleBlending").UpdateOrder.ToArray());
        Assert.Empty(profile.Root("OverlayLayer").UpdateOrder.ToArray());
        Assert.Equal(32, profile.RootNames.SelectMany(r => profile.Root(r).UpdateOrder.ToArray()).Distinct().Count());
        Assert.Equal(105, profile.RootNames.SelectMany(r => profile.Root(r).Reads.ToArray()).Distinct().Count());
        Assert.Equal(profile.Digest, Compile(Read()).Digest);
        Assert.Throws<ArgumentException>(() => profile.Root("Missing"));
    }

    [Theory]
    [InlineData("duplicate_root")]
    [InlineData("duplicate_cache")]
    [InlineData("wrong_node_type")]
    [InlineData("missing_cache")]
    [InlineData("out_of_range")]
    [InlineData("schema")]
    public void RejectsIncompleteOrAmbiguousCompiledCacheLayout(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var orders = root["orderedSavedPoseNodes"]!.AsArray();
        var order = orders.First(n => n!["root"]!.GetValue<string>() == "BaseLayer")!["compiledNodeIndices"]!.AsArray();
        switch (mutation)
        {
            case "duplicate_root": orders.Add(orders[0]!.DeepClone()); break;
            case "duplicate_cache": order.Add(order[0]!.DeepClone()); break;
            case "wrong_node_type": order[0] = 199; break;
            case "missing_cache": order.RemoveAt(0); break;
            case "out_of_range": order[0] = 999999; break;
            case "schema": root["cacheSchemaVersion"] = 2; break;
        }
        Assert.Throws<ArgumentException>(() => Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void ReplaysNativeCacheUpdateAndInertializationForwarding(int hz)
    {
        using var document = JsonDocument.Parse(Read());
        var definition = new AlsPoseCacheDefinition(8, [7], [new(0, 7), new(1, 7), new(2, 7)]);
        var graph = new AlsPoseCacheTraversal(definition, 3);
        var count = 0;
        foreach (var row in document.RootElement.GetProperty("cacheUpdateNativeCases").EnumerateArray())
        {
            if (row.GetProperty("hz").GetInt32() != hz) continue;
            var sink = new NativeSink();
            var identity = new AlsFrameIdentity(count++, 1, 1);
            graph.Begin(identity);
            var index = 0;
            foreach (var call in row.GetProperty("calls").EnumerateArray())
            {
                var context = new AlsPoseUpdateContext(identity, call.GetProperty("weight").GetSingle(),
                    call.GetProperty("delta").GetSingle(), call.GetProperty("rootMotion").GetSingle(), call.GetProperty("shared").GetBoolean())
                    .WithState(6, index, call.GetProperty("sync").GetBoolean())
                    .WithInertialization(index, call.GetProperty("handler").GetBoolean());
                graph.Use(index++, context);
            }
            graph.Drain(sink);
            Assert.Equal(row.GetProperty("updates").GetInt32(), sink.Updates);
            Assert.Equal(row.GetProperty("weight").GetSingle(), sink.Context.Weight);
            Assert.Equal(row.GetProperty("delta").GetSingle(), sink.Context.Delta);
            Assert.Equal(row.GetProperty("rootMotion").GetSingle(), sink.Context.RootMotionWeight);
            Assert.Equal(row.GetProperty("sync").GetBoolean(), sink.Context.InertializationSync);
            Assert.Equal(row.GetProperty("inertialRequests").EnumerateArray().Select(n => n.GetInt32()), sink.Requests);
        }
        Assert.Equal(8, count);
    }

    private sealed class NativeSink : IAlsPoseCacheUpdateSink
    {
        public int Updates;
        public AlsPoseUpdateContext Context;
        public readonly int[] Requests = new int[3];
        public void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
        {
            Context = context; Updates++;
            if (context.InertializationRequester >= 0) Requests[context.InertializationRequester]++;
        }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped)
        {
            Assert.Equal(1, Updates);
            if (Requests[handlerNodeIndex] == 0) return;
            foreach (var context in skipped)
                if (context.InertializationRequester >= 0) Requests[context.InertializationRequester]++;
        }
    }

    internal static AlsPoseCacheProfile Compile(string json)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "p4_cycle_locomotion_profile.json")), set);
        var sources = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "v4_locomotion_source_graph.json")), set, locomotion.SkeletonId);
        return AlsPoseCacheCompiler.Compile(json, set, sources);
    }
    internal static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_pose_cache_graph.json"));
}
