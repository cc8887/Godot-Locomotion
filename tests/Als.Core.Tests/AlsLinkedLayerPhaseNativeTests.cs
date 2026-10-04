using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsLinkedLayerPhaseNativeTests
{
    private static string Evidence(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GodotALS.csproj")))
            directory = directory.Parent ?? throw new InvalidOperationException("Missing repository root.");
        return Path.Combine(directory.FullName, "artifacts/lyra-analysis", name);
    }

    private static JsonDocument Native()
    {
        var bytes = File.ReadAllBytes(Evidence("layer-phases-v5-native.json"));
        using var closure = JsonDocument.Parse(File.ReadAllBytes(Evidence("layer-phases-v5-closure.json")));
        Assert.Equal(closure.RootElement.GetProperty("nativeSha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        return JsonDocument.Parse(bytes);
    }

    public static IEnumerable<object[]> Cases()
    {
        using var native = Native();
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void InitializeAndCacheBonesMatchOriginalNodeOrderAcrossBindingStages(string name)
    {
        using var native = Native();
        var row = native.RootElement.GetProperty("cases").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == name);
        var valid = row.TryGetProperty("self", out _);
        var count = row.GetProperty("inputs").GetInt32();
        foreach (var initialize in new[] { true, false })
        {
            var order = new List<string>();
            var inputs = Enumerable.Range(0, count).Select<int, Action>(i =>
                () => order.Add(i == 0 ? "input:11" : "input:22")).ToArray();
            void Root() => order.Add("root");
            if (initialize) AlsLinkedLayerExecution.Initialize(valid, valid, inputs, Root);
            else AlsLinkedLayerExecution.CacheBones(valid, valid, inputs, Root);
            Assert.Equal(row.GetProperty(initialize ? "initializeOrder" : "cacheOrder")
                .EnumerateArray().Select(v => v.GetString()!), order);
            Assert.Equal(row.GetProperty(initialize ? "rootInitializations" : "rootCaches").GetInt32(),
                order.Count(v => v == "root"));
            Assert.Equal(row.GetProperty(initialize ? "firstInitializations" : "firstCaches").GetInt32(),
                order.Count(v => v == "input:11"));
            Assert.Equal(row.GetProperty(initialize ? "secondInitializations" : "secondCaches").GetInt32(),
                order.Count(v => v == "input:22"));
            // Full phases reach even the second unbound pose input; Update
            // fallback and the empty self root obey different traversal rules.
            Assert.Equal(count, order.Count(v => v.StartsWith("input:")));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SubgraphEntryDoesNotInventParentInputTraversal(bool target, bool root)
    {
        var visits = 0;
        AlsLinkedLayerExecution.InitializeSubGraph(target, root, () => ++visits);
        Assert.Equal(target && root ? 1 : 0, visits);
        AlsLinkedLayerExecution.CacheBonesSubGraph(target, root, () => ++visits);
        Assert.Equal(target && root ? 2 : 0, visits);
        var inputVisits = 0;
        Action[] inputs = [() => ++inputVisits, () => ++inputVisits];
        AlsLinkedLayerExecution.Initialize(target, root, inputs, () => ++visits);
        AlsLinkedLayerExecution.CacheBones(target, root, inputs, () => ++visits);
        Assert.Equal(4, inputVisits);
        Assert.Equal(target && root ? 4 : 0, visits);
    }
}
