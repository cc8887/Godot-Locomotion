using System.Text;
using GodotAls.Import.Manifest;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Tests;

public sealed class AlsFileAuditorTests
{
    [Fact]
    public void AcceptsADeclaredFileWithMatchingSizeAndHash()
    {
        using var fixture = FileAuditFixture.Create();

        Assert.Empty(AlsFileAuditor.Validate(fixture.Root, fixture.Manifest));
    }

    [Fact]
    public void ReportsHashMismatchAndUndeclaredBinaryFiles()
    {
        using var fixture = FileAuditFixture.Create();
        File.WriteAllText(fixture.OutputPath, "changed", new UTF8Encoding(false));
        var extraPath = Path.Combine(fixture.Root, "textures", $"{new string('a', 40)}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(extraPath)!);
        File.WriteAllBytes(extraPath, [1, 2, 3]);

        var issues = AlsFileAuditor.Validate(fixture.Root, fixture.Manifest);

        Assert.Contains(issues, issue => issue.Code == "ALSFILE006" && issue.FieldPath == "$.files[0].size");
        Assert.Contains(issues, issue => issue.Code == "ALSFILE008" && issue.Actual == "textures/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
    }

    private sealed class FileAuditFixture : IDisposable
    {
        private FileAuditFixture(string root, string outputPath, AlsManifest manifest)
        {
            Root = root;
            OutputPath = outputPath;
            Manifest = manifest;
        }

        public string Root { get; }

        public string OutputPath { get; }

        public AlsManifest Manifest { get; }

        public static FileAuditFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"godot-als-file-audit-{Guid.NewGuid():N}");
            var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
            var outputPath = Path.Combine(root, manifest.Files[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "fixture-animation", new UTF8Encoding(false));
            return new FileAuditFixture(root, outputPath, manifest);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
