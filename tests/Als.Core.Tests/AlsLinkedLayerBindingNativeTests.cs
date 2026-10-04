using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsLinkedLayerBindingNativeTests
{
    [Theory]
    [InlineData("named-three-groups")]
    [InlineData("mixed-named-and-none")]
    [InlineData("ungrouped-duplicate-function")]
    [InlineData("partial-first-node-keeps-class")]
    [InlineData("defaults-all-grouped")]
    [InlineData("defaults-all-none")]
    [InlineData("defaults-mixed-classes")]
    [InlineData("defaults-mixed-default-and-self")]
    [InlineData("no-interface-with-default")]
    [InlineData("main-selector-default-recreation")]
    public void OriginalBindingsMatchEveryNodeAndLifetimeAcrossOperations(string caseName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GodotALS.csproj")))
            root = root.Parent ?? throw new InvalidOperationException("Missing repository root.");
        var path = Path.Combine(root.FullName, "artifacts/lyra-analysis/layer-binding-matrix-v3-native.json");
        using var closure = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root.FullName,
            "artifacts/lyra-analysis/layer-binding-matrix-v3-closure.json")));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(closure.RootElement.GetProperty("nativeSha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var native = JsonDocument.Parse(bytes);
        Assert.True(native.RootElement.GetProperty("metadataRestored").GetBoolean());
        Assert.False(native.RootElement.GetProperty("posesEvaluated").GetBoolean());
        var fixture = native.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == caseName);
        var definitions = fixture.GetProperty("classes").EnumerateArray().Select(c => new AlsLinkedLayerClass(
            c.GetProperty("name").GetString()!, c.GetProperty("functions").EnumerateArray().Select(f =>
                new AlsLinkedLayerFunction(f.GetProperty("name").GetString()!, f.GetProperty("group").GetString()!,
                    f.GetProperty("implemented").GetBoolean())).ToImmutableArray(),
            c.GetProperty("receiveNotifies").GetBoolean(), c.GetProperty("propagateNotifies").GetBoolean())).ToArray();
        var calls = fixture.GetProperty("calls").EnumerateArray().Select(n => new AlsLinkedLayerCallSite(
            n.GetProperty("node").GetString()!, n.GetProperty("function").GetString()!,
            EmptyAsNull(n.GetProperty("defaultClass").GetString()!), n.GetProperty("hasInterface").GetBoolean(),
            n.GetProperty("receiveNotifies").GetBoolean(), n.GetProperty("propagateNotifies").GetBoolean())).ToArray();
        var bindings = new AlsLinkedLayerBindings(native.RootElement.GetProperty("mainClass").GetString()!, calls, definitions);
        var nativeOwners = new Dictionary<int, long> { [0] = 0 };
        var managedOwners = new Dictionary<long, int> { [0] = 0 };
        var index = 0;
        foreach (var step in fixture.GetProperty("steps").EnumerateArray())
        {
            var operation = step.GetProperty("operation").GetString()!;
            var requested = EmptyAsNull(step.GetProperty("requestedClass").GetString()!);
            AlsLinkedLayerBindingCandidate Prepare() => operation == "unlink"
                ? bindings.PrepareUnlink(requested) : bindings.PrepareLink(requested);
            var before = bindings.Targets;
            var cancelled = Prepare(); bindings.Cancel(cancelled);
            Assert.True(before.SequenceEqual(bindings.Targets), caseName + ": cancelled publication");
            var candidate = Prepare();
            Assert.True(cancelled.Targets.SequenceEqual(candidate.Targets), caseName + ": retry ownership");
            bindings.Commit(candidate);
            foreach (var node in step.GetProperty("nodes").EnumerateArray())
            {
                var name = node.GetProperty("node").GetString()!;
                var target = bindings.Target(name);
                var description = $"{caseName} step={index} operation={operation} node={name}";
                Assert.True(target.Kind.ToString() == node.GetProperty("kind").GetString(), description + ": target kind");
                Assert.True(target.Class == EmptyAsNull(node.GetProperty("class").GetString()!), description + ": target class");
                Assert.True(target.ReceiveNotifies == node.GetProperty("receiveNotifies").GetBoolean(), description + ": receive flag");
                Assert.True(target.PropagateNotifies == node.GetProperty("propagateNotifies").GetBoolean(), description + ": propagate flag");
                var owner = node.GetProperty("owner").GetInt32();
                if (owner < 0) continue;
                Assert.True(!nativeOwners.TryGetValue(owner, out var previous) || previous == target.Instance, description + ": native identity reused");
                Assert.True(!managedOwners.TryGetValue(target.Instance, out var nativeOwner) || nativeOwner == owner, description + ": managed identity reused");
                nativeOwners[owner] = target.Instance; managedOwners[target.Instance] = owner;
            }
            index++;
        }
        Assert.True(index >= 6 && calls.Length == 14, "Incomplete original operation sequence.");
    }
    private static string? EmptyAsNull(string text) => text.Length == 0 ? null : text;
}
