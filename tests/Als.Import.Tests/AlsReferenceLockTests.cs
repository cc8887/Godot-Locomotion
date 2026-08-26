using System.Text.Json;

namespace GodotAls.Import.Tests;

public sealed class AlsReferenceLockTests
{
    [Fact]
    public void PinsTheExactP3AlsReference()
    {
        var root = RepositoryRoot.Find();
        var path = Path.Combine(root, "reference", "als-refactored.lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var lockFile = document.RootElement;

        Assert.Equal(1, lockFile.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("https://github.com/Sixze/ALS-Refactored.git", lockFile.GetProperty("repository").GetString());
        Assert.Equal("b754d6f0f2bb03741d301f8fb88077ebfe561e17", lockFile.GetProperty("commit").GetString());
        Assert.Equal("2026-08-26", lockFile.GetProperty("observedDate").GetString());
        Assert.Equal("5.9.0", lockFile.GetProperty("targetEngine").GetString());
        Assert.Equal(JsonValueKind.Array, lockFile.GetProperty("compatibilityPatches").ValueKind);
        var patch = Assert.Single(lockFile.GetProperty("compatibilityPatches").EnumerateArray());
        Assert.Equal("reference/patches/als-refactored-ue-5.9-engine-version.patch", patch.GetProperty("path").GetString());
        Assert.Equal("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f", patch.GetProperty("sha256").GetString());
        Assert.Equal(2, patch.EnumerateObject().Count());
        Assert.Equal(6, lockFile.EnumerateObject().Count());
    }
}
