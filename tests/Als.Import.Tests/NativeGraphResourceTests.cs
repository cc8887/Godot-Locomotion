using System.Text;
using System.Text.RegularExpressions;
using GodotAls.Import.Compilation;
using GodotAls.Import.Resources;

namespace GodotAls.Import.Tests;

public sealed class NativeGraphResourceTests
{
    private const string Source = "/game/Test.Test";
    private static string Text(string value = "first") => $"""
        Begin Object Class=/Script/Engine.AnimBlueprint Name="Test"
           Begin Object Class=/Script/Engine.EdGraph Name="Left"
              Begin Object Class=/Script/Engine.EdGraph Name="Shared"
              End Object
           End Object
           Begin Object Class=/Script/Engine.EdGraph Name="Right"
              Begin Object Class=/Script/Engine.EdGraph Name="Shared"
              End Object
           End Object
        End Object
        Begin Object Name="Test"
           Begin Object Name="Left"
              Begin Object Name="Shared"
                 Value={value}
              End Object
           End Object
           Begin Object Name="Right"
              Begin Object Name="Shared"
                 Value=other
              End Object
           End Object
        End Object
        """;

    [Fact]
    public void IndexProducesIdenticalSlicesToLegacyExtractorIncludingDeclarationOrder()
    {
        var text = Text().Replace("\n", "\r\n", StringComparison.Ordinal);
        var index = new AlsNativeNestedGraph.Index(text, Source);
        foreach (var name in new[] { "", "Left", "Right", "Left.Shared", "Right.Shared" })
        {
            var path = Source + ":" + name;
            Assert.Equal(LegacyExtract(text, Source, path), index.Extract(path));
        }
    }

    [Fact]
    public void ResourceScopeReusesIndexAndSlicesThenInvalidatesSameIdReplacement()
    {
        using var scope = new ImportResourceScope("test");
        var left = Source + ":Left.Shared"; var right = Source + ":Right.Shared";
        var original = AlsNativeNestedGraph.Extract(Text(), Source, left);
        for (var i = 0; i < 20; i++) Assert.Same(original, AlsNativeNestedGraph.Extract(Text(), Source, left));
        Assert.Contains("Value=other", AlsNativeNestedGraph.Extract(Text(), Source, right));
        Assert.Equal(4, scope.Resources.GetStatistics().Loads);
        var replacement = AlsNativeNestedGraph.Extract(Text("after"), Source, left);
        Assert.Contains("Value=after", replacement);
        Assert.Contains("Value=first", original);
        Assert.Equal(7, scope.Resources.GetStatistics().Loads);
        Assert.Equal(4, scope.Resources.GetStatistics().Invalidations);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("root")]
    [InlineData("indent")]
    [InlineData("unbalanced")]
    [InlineData("foreign")]
    public void IndexPreservesMalformedSourceRejection(string change)
    {
        var text = Text(); var path = Source + ":Left.Shared";
        if (change == "missing") path += ".Absent";
        if (change == "duplicate") text += "\n" + text;
        if (change == "root") text = text.Replace("Name=\"Test\"", "Name=\"Foreign\"", StringComparison.Ordinal);
        if (change == "indent") text = text.Replace("         Value=first", " Value=first", StringComparison.Ordinal);
        if (change == "unbalanced") text += "\nEnd Object";
        if (change == "foreign") path = "/other:Left";
        Assert.Throws<ArgumentException>(() => LegacyExtract(text, Source, path));
        Assert.Throws<ArgumentException>(() => AlsNativeNestedGraph.Extract(text, Source, path));
    }

    [Fact]
    public void ScopesRestoreParentsAndDoNotShareAcrossThreads()
    {
        using var parent = new ImportResourceScope("parent");
        var child = new ImportResourceScope("child");
        Assert.Same(parent.Resources, child.Resources);
        Assert.Throws<InvalidOperationException>(parent.Dispose);
        ImportResourceScope? workerScope = parent;
        var worker = new Thread(() => workerScope = ImportResourceScope.Current);
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(workerScope);
        child.Dispose(); child.Dispose();
        Assert.Same(parent, ImportResourceScope.Current);
        using var independent = new ImportResourceScope("independent", shareWithParent: false);
        Assert.NotSame(parent.Resources, independent.Resources);
    }

    [Theory]
    [InlineData("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Standing.AB_Als_Standing")]
    [InlineData("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Crouching.AB_Als_Crouching")]
    [InlineData("/ALS/ALS/Character/AB_Als.AB_Als")]
    public void OriginalBlueprintSlicesMatchLegacyBytes(string source)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            path => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path)));
        var payload = catalog.Read(source);
        var text = payload.GetProperty("nativeText").GetString()!;
        var paths = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("graph").GetString()!).Distinct().Take(6).ToArray();
        Assert.NotEmpty(paths);
        var index = new AlsNativeNestedGraph.Index(text, source);
        foreach (var path in paths) Assert.Equal(LegacyExtract(text, source, path), index.Extract(path));
    }

    // Retain the previous algorithm as an independent byte-for-byte oracle.
    private static string LegacyExtract(string text, string source, string path)
    {
        if (!path.StartsWith(source + ":", StringComparison.Ordinal)) throw new ArgumentException();
        var lines = text.Replace("\r", "").Split('\n');
        var stack = new List<(string Name, int Start, int Indent, bool Declaration)>();
        var result = new StringBuilder(); var declarations = 0; var definitions = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var begin = Regex.Match(line, "^( *)Begin Object (?:Class=[^ ]+ )?Name=\"([^\"]+)\"");
            if (begin.Success) stack.Add((begin.Groups[2].Value, i, begin.Groups[1].Length, line.Contains("Begin Object Class=", StringComparison.Ordinal)));
            else if (line.Trim() == "End Object")
            {
                if (stack.Count == 0) throw new ArgumentException();
                var item = stack[^1];
                if (source + ":" + string.Join(".", stack.Skip(1).Select(s => s.Name)) == path)
                {
                    if (stack[0].Name != source[(source.LastIndexOf('.') + 1)..]) throw new ArgumentException();
                    if (item.Declaration) declarations++; else definitions++;
                    for (var j = item.Start; j <= i; j++)
                    {
                        if (lines[j].Length < item.Indent || lines[j][..item.Indent].Any(c => c != ' ')) throw new ArgumentException();
                        result.AppendLine(lines[j][item.Indent..]);
                    }
                }
                stack.RemoveAt(stack.Count - 1);
            }
        }
        if (stack.Count != 0 || declarations != 1 || definitions != 1) throw new ArgumentException();
        return result.ToString();
    }
}
