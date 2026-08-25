namespace GodotAls.Import.Tests;

public sealed class RepositoryImportPolicyTests
{
    [Fact]
    public void NonGodotContentIsHiddenFromTheEditorScanner()
    {
        var root = RepositoryRoot.Find();
        var gitIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));
        Assert.Contains("benchmark-results/*", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("!benchmark-results/.gdignore", gitIgnore, StringComparison.Ordinal);

        foreach (var relativePath in new[]
        {
            "artifacts",
            "benchmark-results",
            "docs",
            "src/Als.Core",
            "src/Als.Import",
            "tests",
            "tools",
        })
        {
            Assert.True(
                File.Exists(Path.Combine(root, relativePath, ".gdignore")),
                $"Missing .gdignore in {relativePath}.");
        }

        Assert.False(File.Exists(Path.Combine(root, "src", "Als.Godot", ".gdignore")));
        Assert.False(File.Exists(Path.Combine(root, "assets", "generated", ".gdignore")));
    }

    [Fact]
    public void FbxImportDoesNotResolveExporterWorkstationTextures()
    {
        var root = RepositoryRoot.Find();
        var project = File.ReadAllText(Path.Combine(root, "project.godot"));

        Assert.Contains("[importer_defaults]", project, StringComparison.Ordinal);
        Assert.Contains("\"fbx/embedded_image_handling\": 0", project, StringComparison.Ordinal);
        Assert.Contains("\"animation/import\": true", project, StringComparison.Ordinal);
        Assert.Contains("\"animation/fps\": 30", project, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "import_defaults.cfg")));
    }
}
