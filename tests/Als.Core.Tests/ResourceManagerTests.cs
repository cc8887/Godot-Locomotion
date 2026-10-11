using System.Text;
using System.Text.Json;
using GodotAls.Core.Resources;

namespace GodotAls.Core.Tests;

public sealed class ResourceManagerTests
{
    private sealed record Settings(int Width, string Theme);
    private sealed class SettingsLoader : IResourceLoader<Settings>
    {
        public Settings Load(ResourceLoadContext context, ReadOnlyMemory<byte> content)
            => JsonSerializer.Deserialize<Settings>(content.Span)!;
    }

    [Fact]
    public async Task OrdinaryConfigurationIsLoadedOnceAcrossConcurrentRequests()
    {
        using var manager = new ResourceManager("ui");
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var reads = 0;
        var id = new ResourceId("ui/settings");
        var bytes = Encoding.UTF8.GetBytes("{\"Width\":800,\"Theme\":\"green\"}");
        var provider = new DelegateResourceProvider(_ =>
        {
            Interlocked.Increment(ref reads);
            entered.Set();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return bytes;
        });
        manager.Register(id, provider, new SettingsLoader());
        Settings Read() { using var lease = manager.Acquire<Settings>(id); return lease.Value; }
        var first = Task.Run(Read);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var others = Enumerable.Range(0, 12).Select(_ => Task.Run(Read)).ToArray();
        finish.Set();
        var values = await Task.WhenAll(others.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.All(values, value => Assert.Same(values[0], value));
        Assert.Equal(new Settings(800, "green"), values[0]);
        Assert.Equal(1, reads);
        Assert.Equal(12, manager.GetStatistics().Hits);
        Assert.Equal(bytes.Length, manager.GetStatistics().RetainedBytes);
    }

    [Fact]
    public void ReplacingOneResourceRebuildsItsDependentsAndPreservesOldLeases()
    {
        using var manager = new ResourceManager();
        var a = new ResourceId("a"); var b = new ResourceId("b"); var unrelated = new ResourceId("unrelated");
        var released = new List<string>();
        manager.Register(a, _ => "first", released.Add);
        manager.Register(b, context => context.Read<string>(a) + "/derived", released.Add);
        manager.Register(unrelated, _ => new object());
        using var original = manager.Acquire<string>(b);
        using var independent = manager.Acquire<object>(unrelated);
        manager.Replace(a, _ => "second", released.Add);
        using var current = manager.Acquire<string>(b);
        using var stillIndependent = manager.Acquire<object>(unrelated);
        Assert.Equal("first/derived", original.Value);
        Assert.Equal("second/derived", current.Value);
        Assert.Same(independent.Value, stillIndependent.Value);
        Assert.NotEqual(original.Revision, current.Revision);
        Assert.Empty(released);
        original.Dispose();
        Assert.Equal(new[] { "first/derived", "first" }, released);
        Assert.Equal(2, manager.GetStatistics().Invalidations);
    }

    [Fact]
    public void FailedReplacementRetainsCurrentVersionAndCanBeRetried()
    {
        using var manager = new ResourceManager();
        var id = new ResourceId("settings");
        manager.Register(id, _ => new Settings(800, "green"));
        using var original = manager.Acquire<Settings>(id);
        Assert.Throws<InvalidDataException>(() => manager.Replace<Settings>(id, _ => throw new InvalidDataException()));
        using var afterFailure = manager.Acquire<Settings>(id);
        Assert.Same(original.Value, afterFailure.Value);
        Assert.Equal(original.Revision, afterFailure.Revision);
        manager.Replace(id, _ => new Settings(1200, "red"));
        using var replacement = manager.Acquire<Settings>(id);
        Assert.Equal(1200, replacement.Value.Width);
        Assert.Equal(800, original.Value.Width);
        Assert.Equal(1, manager.GetStatistics().Failures);
    }

    [Fact]
    public void InvalidationTraversesOnlyTheAffectedDependencyClosure()
    {
        using var manager = new ResourceManager();
        var a = new ResourceId("a"); var b = new ResourceId("b"); var c = new ResourceId("c"); var d = new ResourceId("d");
        var value = 1;
        manager.Register(a, _ => value);
        manager.Register(b, context => context.Read<int>(a) + 1);
        manager.Register(c, context => context.Read<int>(b) + context.Read<int>(a));
        manager.Register(d, _ => new object());
        using var old = manager.Acquire<int>(c);
        using var independent = manager.Acquire<object>(d);
        value = 5;
        manager.Invalidate(a);
        using var current = manager.Acquire<int>(c);
        using var unchanged = manager.Acquire<object>(d);
        Assert.Equal(3, old.Value); Assert.Equal(11, current.Value);
        Assert.Same(independent.Value, unchanged.Value);
        Assert.Equal(3, manager.GetStatistics().Invalidations);
    }

    [Fact]
    public void DisposingManagerLeavesPinnedDependencyVersionsAliveUntilLastLease()
    {
        var manager = new ResourceManager();
        var released = new List<string>();
        var a = new ResourceId("a"); var b = new ResourceId("b");
        manager.Register(a, _ => "leaf", released.Add);
        manager.Register(b, context => context.Read<string>(a) + "/parent", released.Add);
        var lease = manager.Acquire<string>(b);
        manager.Dispose();
        Assert.Empty(released);
        Assert.Equal(2, manager.GetStatistics().LiveVersions);
        Assert.Equal("leaf/parent", lease.Value);
        Assert.Throws<ObjectDisposedException>(() => manager.Acquire<string>(b));
        lease.Dispose(); lease.Dispose(); manager.Dispose();
        Assert.Equal(new[] { "leaf/parent", "leaf" }, released);
        Assert.Equal(0, manager.GetStatistics().LiveVersions);
        Assert.Throws<ObjectDisposedException>(() => lease.Value);
    }

    [Fact]
    public void LoaderFailureReleasesDependencyPinsAndDoesNotPoisonTheCache()
    {
        using var manager = new ResourceManager();
        var a = new ResourceId("a"); var b = new ResourceId("b");
        var calls = 0; var releases = 0;
        manager.Register(a, _ => new object(), _ => releases++);
        manager.Register(b, context =>
        {
            var value = context.Read<object>(a);
            if (++calls == 1) throw new InvalidDataException();
            return value;
        });
        Assert.Throws<InvalidDataException>(() => manager.Acquire<object>(b));
        using (var result = manager.Acquire<object>(b)) Assert.NotNull(result.Value);
        manager.Dispose();
        Assert.Equal(1, releases);
        Assert.Equal(0, manager.GetStatistics().LiveVersions);
    }

    [Fact]
    public void CyclesIncludingCachedIndirectDependenciesAreRejected()
    {
        using var manager = new ResourceManager();
        var a = new ResourceId("a"); var b = new ResourceId("b");
        manager.Register(a, _ => 1);
        manager.Register(b, context => context.Read<int>(a) + 1);
        using var previous = manager.Acquire<int>(b);
        Assert.Throws<InvalidOperationException>(() => manager.Replace(a, context => context.Read<int>(b)));
        using var leaf = manager.Acquire<int>(a);
        Assert.Equal(1, leaf.Value);
        using var cyclic = new ResourceManager();
        cyclic.Register(a, context => context.Read<int>(b));
        cyclic.Register(b, context => context.Read<int>(a));
        Assert.Throws<InvalidOperationException>(() => cyclic.Acquire<int>(a));
        Assert.Equal(0, cyclic.GetStatistics().LiveVersions);
    }

    [Fact]
    public void ContextCannotEscapeItsLoaderOrChangeDefinitionsDuringConstruction()
    {
        using var manager = new ResourceManager();
        ResourceLoadContext? saved = null;
        var id = new ResourceId("value");
        manager.Register(id, context =>
        {
            saved = context;
            Assert.Throws<InvalidOperationException>(() => manager.Register(new ResourceId("other"), _ => 2));
            Task.Run(() => Assert.Throws<InvalidOperationException>(() => context.ReportBytes(1))).GetAwaiter().GetResult();
            return 1;
        });
        using var lease = manager.Acquire<int>(id);
        Assert.Throws<InvalidOperationException>(() => saved!.Read<int>(id));
        Assert.Throws<ArgumentException>(() => manager.Acquire<string>(id));
        Assert.Throws<ArgumentException>(() => manager.Register(id, _ => 2));
    }

    [Fact]
    public void FileProviderSupportsSamePathReplacementWithUnchangedLengthAndTimestamp()
    {
        var directory = Path.Combine(Path.GetTempPath(), "resource-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(file, "{\"Width\":800,\"Theme\":\"green\"}");
            var timestamp = File.GetLastWriteTimeUtc(file);
            using var manager = new ResourceManager();
            var id = new ResourceId("ui/settings");
            manager.Register(id, new FileResourceProvider(_ => file), new SettingsLoader());
            using var old = manager.Acquire<Settings>(id);
            File.WriteAllText(file, "{\"Width\":900,\"Theme\":\"green\"}");
            File.SetLastWriteTimeUtc(file, timestamp);
            manager.Invalidate(id);
            using var current = manager.Acquire<Settings>(id);
            Assert.Equal(800, old.Value.Width); Assert.Equal(900, current.Value.Width);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReleaseFailureDoesNotLeakOtherVersionsOrUndoSuccessfulPublication()
    {
        var manager = new ResourceManager();
        var a = new ResourceId("a"); var b = new ResourceId("b");
        manager.Register(a, _ => 1, _ => throw new IOException("release"));
        manager.Register(b, context => context.Read<int>(a));
        using (var initial = manager.Acquire<int>(b)) Assert.Equal(1, initial.Value);
        Assert.Throws<AggregateException>(() => manager.Replace(a, _ => 2));
        using (var current = manager.Acquire<int>(b)) Assert.Equal(2, current.Value);
        manager.Dispose();
        Assert.Equal(0, manager.GetStatistics().LiveVersions);
        Assert.Equal(0, manager.GetStatistics().RetainedBytes);
    }
}
