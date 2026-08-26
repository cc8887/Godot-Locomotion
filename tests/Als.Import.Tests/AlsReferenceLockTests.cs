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
        Assert.Empty(lockFile.GetProperty("compatibilityPatches").EnumerateArray());
        Assert.Equal(6, lockFile.EnumerateObject().Count());
    }
}
