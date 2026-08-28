namespace GodotAls.Import.Tests;

public sealed class AlsCurveExporterSourceContractTests
{
    private static readonly string ReaderSource = File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(),
        "tools", "unreal", "AlsGodotExporter", "Source", "AlsGodotExporter", "Private",
        "AlsAnimationMetadataReader.cpp"));

    [Fact]
    public void WeightedCurveKeysAreExplicitlyRejectedWithAssetAndKeyContext()
    {
        Assert.Contains("RCTWM_WeightedNone", ReaderSource, StringComparison.Ordinal);
        Assert.Contains("TangentWeightMode", ReaderSource, StringComparison.Ordinal);
        Assert.Contains("Unsupported weighted float curve key", ReaderSource, StringComparison.Ordinal);
        Assert.Contains("asset=%s curve[%d]=%s key[%d] time=", ReaderSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unsupported float curve interpolation")]
    [InlineData("Non-finite float curve key")]
    [InlineData("Duplicate float curve key time")]
    public void CurveExportFailuresCarryDiagnosticContext(string failure)
    {
        Assert.Contains(failure, ReaderSource, StringComparison.Ordinal);
        Assert.Contains("asset=%s curve[%d]=%s", ReaderSource, StringComparison.Ordinal);
        Assert.Contains("key[%d] time=", ReaderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CurveInfinityFailuresCarryAssetAndCurveContext()
    {
        Assert.Contains("Unsupported float curve infinity mode", ReaderSource, StringComparison.Ordinal);
        Assert.Contains("asset=%s curve[%d]=%s", ReaderSource, StringComparison.Ordinal);
    }
}
